using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;
using Wrestling.Data;
using Wrestling.Data.Json;
using Wrestling.Data.Motion;
using Wrestling.Presentation;

namespace Wrestling.Editor
{
    /// <summary>
    /// Measures whether the engine's grips become physical holds while review-only recorded entries
    /// play: for every hand the engine has in a grip, how long the palm takes to hold, how much of the
    /// grip it holds, how far the physical palm stays from its hold or target, and how often holds are
    /// torn off while the engine still grips. Cases cover each executor corner, both mirror states the
    /// engine produces, and men's and women's bodies. A held link is a game constraint, not evidence of
    /// a measured human contact; this check reports, it does not accept motion for production.
    /// </summary>
    public static class RecordedAcquisitionChecks
    {
        private const string DefaultIndex = "tools/motion/harmony4d/reviews/entry-review-index.json";
        private const int SearchSeeds = 160;
        private const int SearchTicks = 3600;
        private const int CasesPerKey = 2;
        private const int TailTicks = 30;

        /// <summary>How much smaller a recorded entry's held share of engine-grip frames may be than the keyed clips' before the check fails.</summary>
        private const float HeldShareTolerance = 0.10f;

        public static void RunFromCommandLine()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        private readonly struct Case
        {
            public readonly string Clip;
            public readonly WrestlingStyle Style;
            public readonly ulong Seed;
            public readonly int Tick;
            public readonly Corner Executor;
            public Case(string clip, WrestlingStyle style, ulong seed, int tick, Corner executor)
            { Clip = clip; Style = style; Seed = seed; Tick = tick; Executor = executor; }
        }

        /// <summary>Per hand and per case: frames, acquisition and gaps while the engine grips.</summary>
        private sealed class HandStats
        {
            public int GripFrames;
            public int HeldFrames;
            public int Acquisitions;
            public int TearOffs;
            public int LatencyFrames;
            public int LatencyCount;
            public int NeverHeld;
            public float HeldGapSum;
            public float HeldGapMax;
            public float FreeDistanceSum;
            public int FreeFrames;
            public float ShownErrorSum;
            public int ShownFrames;

            public void Add(HandStats other)
            {
                GripFrames += other.GripFrames; HeldFrames += other.HeldFrames; Acquisitions += other.Acquisitions;
                TearOffs += other.TearOffs; LatencyFrames += other.LatencyFrames; LatencyCount += other.LatencyCount;
                NeverHeld += other.NeverHeld; HeldGapSum += other.HeldGapSum; HeldGapMax = Mathf.Max(HeldGapMax, other.HeldGapMax);
                FreeDistanceSum += other.FreeDistanceSum; FreeFrames += other.FreeFrames;
                ShownErrorSum += other.ShownErrorSum; ShownFrames += other.ShownFrames;
            }

            public string Describe() => string.Format(CultureInfo.InvariantCulture,
                "grip frames {0}, held {1:P0}, acquisitions {2}, mean latency {3} frames, tear-offs {4}, never held {5}, held gap mean {6:F1} cm max {7:F1} cm, unheld palm to allowed surface {8} cm, unheld recorded palm-to-target {9} cm",
                GripFrames, GripFrames > 0 ? HeldFrames / (float)GripFrames : 0f, Acquisitions,
                LatencyCount > 0 ? (LatencyFrames / (float)LatencyCount).ToString("F1", CultureInfo.InvariantCulture) : "n/a",
                TearOffs, NeverHeld, HeldFrames > 0 ? 100f * HeldGapSum / HeldFrames : 0f, 100f * HeldGapMax,
                FreeFrames > 0 ? (100f * FreeDistanceSum / FreeFrames).ToString("F1", CultureInfo.InvariantCulture) : "n/a",
                ShownFrames > 0 ? (100f * ShownErrorSum / ShownFrames).ToString("F1", CultureInfo.InvariantCulture) : "n/a");
        }

        private static string Argument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        private static string _trace;

        private static void Run()
        {
            _trace = Argument("-acquisitionTrace", null);
            var content = ContentLocator.LoadDirect();
            string repository = Path.GetDirectoryName(ContentLocator.DirectDataDirectory().TrimEnd('/', '\\'));
            string indexPath = Argument("-acquisitionIndex", Path.Combine(repository, DefaultIndex));
            string reportPath = Argument("-acquisitionReport", null);
            // Relative paths are from the repository root, not Unity's project folder.
            if (!Path.IsPathRooted(indexPath)) indexPath = Path.Combine(repository, indexPath);
            if (reportPath != null && !Path.IsPathRooted(reportPath)) reportPath = Path.Combine(repository, reportPath);
            MotionIndexFile index = ContentJson.Deserialize<MotionIndexFile>(File.ReadAllText(indexPath), indexPath);
            var review = MotionLibrary.Create(index, content.Motion.Takes,
                new HashSet<string>(content.Animations.All.Select(c => c.Id)), content.Motion.Contacts);
            var clips = new HashSet<string>(index.Phases.Select(p => p.Clip));
            List<Case> cases = FindCases(content, clips);

            EditorSceneManager.OpenScene(BoutViewerSceneBuilder.ScenePath, OpenSceneMode.Single);
            var viewer = UnityEngine.Object.FindAnyObjectByType<BoutViewer>();
            viewer.Initialise(content, review);
            var report = new StringBuilder();
            var totals = new Dictionary<string, HandStats>();
            int exercised = 0;
            foreach (Case test in cases)
            {
                viewer.UseMotion(review);
                var recorded = Measure(viewer, test, true);
                if (recorded.Frames == 0)
                {
                    report.AppendLine($"{test.Clip} {test.Style} seed {test.Seed} executor {test.Executor}: recorded motion did not play (grip compatibility or mirror grip rejected it).");
                    continue;
                }
                exercised++;
                // The same bout with the keyed clips: the baseline the recorded entry must not be worse than.
                viewer.UseMotion(null);
                var keyed = Measure(viewer, test, false);
                foreach (var (mode, result) in new[] { ("recorded", recorded), ("keyed", keyed) })
                {
                    foreach (var hand in result.Stats.OrderBy(h => h.Key, StringComparer.Ordinal))
                    {
                        if (hand.Value.GripFrames == 0) continue;
                        string line = $"{mode} {test.Clip} {test.Style} ({recorded.Gender}) seed {test.Seed} tick {test.Tick} executor {test.Executor} {(recorded.Mirrored ? "mirrored" : "unmirrored")}, {result.Frames} phase frames, {hand.Key}: {hand.Value.Describe()}";
                        report.AppendLine(line);
                        Debug.Log("[acquisition] " + line);
                        string role = hand.Key.StartsWith("executor", StringComparison.Ordinal) ? "executor" : "opponent";
                        string key = $"{test.Clip} {role} {(recorded.Mirrored ? "mirrored" : "unmirrored")} {mode}";
                        if (!totals.TryGetValue(key, out HandStats total)) totals[key] = total = new HandStats();
                        total.Add(hand.Value);
                    }
                }
            }

            report.AppendLine();
            foreach (var total in totals.OrderBy(t => t.Key, StringComparer.Ordinal))
            {
                string line = $"TOTAL {total.Key}: {total.Value.Describe()}";
                report.AppendLine(line);
                Debug.Log("[acquisition] " + line);
            }

            if (exercised == 0) throw new InvalidOperationException("No recorded entry played in any case.");
            // Regression guard: per clip and role, recorded playback may not hold the engine's grips
            // markedly less than the keyed clips do in the same bouts.
            var failures = new List<string>();
            foreach (string clipRole in totals.Keys.Select(k => k.Substring(0, k.LastIndexOf(' ', k.LastIndexOf(' ') - 1))).Distinct())
            {
                HandStats Sum(string mode)
                {
                    var sum = new HandStats();
                    foreach (var t in totals.Where(t => t.Key.StartsWith(clipRole + " ", StringComparison.Ordinal) && t.Key.EndsWith(" " + mode, StringComparison.Ordinal)))
                        sum.Add(t.Value);
                    return sum;
                }
                HandStats recorded = Sum("recorded"), keyed = Sum("keyed");
                if (recorded.GripFrames == 0 || keyed.GripFrames == 0) continue;
                float recordedShare = recorded.HeldFrames / (float)recorded.GripFrames;
                float keyedShare = keyed.HeldFrames / (float)keyed.GripFrames;
                string line = string.Format(CultureInfo.InvariantCulture, "COMPARE {0}: held {1:P0} recorded versus {2:P0} keyed", clipRole, recordedShare, keyedShare);
                report.AppendLine(line);
                Debug.Log("[acquisition] " + line);
                if (recordedShare < keyedShare - HeldShareTolerance) failures.Add(line);
            }

            if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, report.ToString());
            if (failures.Count > 0) throw new InvalidOperationException("Recorded entries hold grips less than keyed clips: " + string.Join("; ", failures));
            Debug.Log($"[acquisition] DONE {exercised} of {cases.Count} cases played recorded motion. Held links are game constraints, not measured contacts; production motion index unchanged.");
        }

        /// <summary>Finds headless bouts where each recorded phase starts, per clip, style and executor corner.</summary>
        private static List<Case> FindCases(GameContent content, HashSet<string> clips)
        {
            var found = new List<Case>();
            var counts = new Dictionary<string, int>();
            foreach (WrestlingStyle style in new[] { WrestlingStyle.Freestyle, WrestlingStyle.GrecoRoman, WrestlingStyle.Womens })
            {
                for (ulong seed = 1; seed <= SearchSeeds; seed++)
                {
                    var session = new BoutSession(content, style, 60, seed);
                    TechniqueExecution last = null;
                    int lastPhase = -1;
                    for (int i = 0; i < SearchTicks && !session.Engine.IsFinished; i++)
                    {
                        session.StepTicks(1);
                        TechniqueExecution active = session.Engine.ActiveTechnique;
                        if (active == null || (ReferenceEquals(active, last) && active.PhaseIndex == lastPhase)) continue;
                        last = active;
                        lastPhase = active.PhaseIndex;
                        string clip = active.Animation;
                        if (!clips.Contains(clip)) continue;
                        string key = $"{clip}|{style}|{active.Executor}";
                        counts.TryGetValue(key, out int count);
                        if (count >= CasesPerKey) continue;
                        counts[key] = count + 1;
                        found.Add(new Case(clip, style, seed, session.Engine.Tick, active.Executor));
                    }
                }
            }

            foreach (string clip in clips)
            {
                if (!found.Any(c => c.Clip == clip))
                    throw new InvalidOperationException("No bout reaches review phase " + clip);
            }

            return found.OrderBy(c => c.Clip, StringComparer.Ordinal).ThenBy(c => c.Style).ThenBy(c => c.Executor).ThenBy(c => c.Seed).ToList();
        }

        private static (Dictionary<string, HandStats> Stats, int Frames, bool Mirrored, string Gender) Measure(BoutViewer viewer, Case test, bool recordedMode)
        {
            viewer.Style = test.Style;
            viewer.StartBout(test.Seed);
            int start = Math.Max(0, test.Tick - 45);
            viewer.FastForward(start);
            var stats = new Dictionary<string, HandStats>();
            var wasHolding = new bool[4];
            var wasGripping = new bool[4];
            var gripStart = new int[4];
            var everHeld = new bool[4];
            var gripped = new bool[4];
            int frames = 0;
            bool mirrored = false;
            int windowEnd = int.MaxValue;
            int frame = 0;
            string gender = viewer.Session.WrestlerOf(test.Executor).Gender.ToString().ToLowerInvariant();
            while (!viewer.Session.Engine.IsFinished && frame < 600)
            {
                viewer.PlayFrame(1f / 60f);
                frame++;
                BoutEngine engine = viewer.Session.Engine;
                bool playing = viewer.CurrentClip == test.Clip && (!recordedMode || viewer.RecordedTakeId != null);
                if (playing)
                {
                    frames++;
                    mirrored |= viewer.RecordedMirrored;
                    windowEnd = engine.Tick + TailTicks;
                }
                else if (frames == 0 && engine.Tick > test.Tick + 120)
                {
                    break;
                }

                bool inWindow = playing || (frames > 0 && engine.Tick <= windowEnd);
                if (frames > 0 && !inWindow) break;
                for (int i = 0; i < 4; i++)
                {
                    var corner = (Corner)(i / 2);
                    var side = (GripSide)(i % 2);
                    bool gripping = HandGrips.ForHand(engine, corner, side) != null;
                    GripLink link = viewer.HandGrips.Link(corner, side);
                    bool holding = link != null;
                    if (gripping && !wasGripping[i]) gripStart[i] = frame;
                    if (inWindow && gripping)
                    {
                        string name = (corner == test.Executor ? "executor " : "opponent ") + (side == GripSide.Left ? "left" : "right");
                        if (!stats.TryGetValue(name, out HandStats hand)) stats[name] = hand = new HandStats();
                        gripped[i] = true;
                        hand.GripFrames++;
                        if (holding)
                        {
                            hand.HeldFrames++;
                            hand.HeldGapSum += link.Gap;
                            hand.HeldGapMax = Mathf.Max(hand.HeldGapMax, link.Gap);
                            if (!wasHolding[i])
                            {
                                hand.Acquisitions++;
                                hand.LatencyFrames += frame - gripStart[i];
                                hand.LatencyCount++;
                            }
                            everHeld[i] = true;
                        }
                        else
                        {
                            if (wasHolding[i] && wasGripping[i]) hand.TearOffs++;
                            WrestlerCharacter figure = corner == Corner.Red ? viewer.RedFigure : viewer.BlueFigure;
                            string handName = side == GripSide.Left ? "LeftHand" : "RightHand";
                            Vector3 palm = figure.Physical.Link(handName).transform.TransformPoint(figure.Physical.PalmAnchor(handName));
                            WrestlerCharacter other = corner == Corner.Red ? viewer.BlueFigure : viewer.RedFigure;
                            hand.FreeDistanceSum += other.Physical.NearestSurface(palm, HandGrips.HeldParts(HandGrips.ForHand(engine, corner, side), side)).Distance;
                            hand.FreeFrames++;
                            RecordedGripRetargeter.Result shown = viewer.RecordedGrip(corner, side);
                            if (shown.Requested)
                            {
                                hand.ShownErrorSum += shown.PalmError;
                                hand.ShownFrames++;
                            }
                        }
                    }

                    if (inWindow && gripping && _trace != null && test.Clip.Contains(_trace))
                    {
                        WrestlerCharacter self = corner == Corner.Red ? viewer.RedFigure : viewer.BlueFigure;
                        string hn = side == GripSide.Left ? "LeftHand" : "RightHand";
                        Vector3 hips = self.Physical.HipPosition;
                        Vector3 palm = self.Physical.Link(hn).transform.TransformPoint(self.Physical.PalmAnchor(hn));
                        RecordedGripRetargeter.Result r = viewer.RecordedGrip(corner, side);
                        Vector3 shoulder = self.Rig.BoneOf(side == GripSide.Left ? Bone.LeftUpperArm : Bone.RightUpperArm).position;
                        Grip g = HandGrips.ForHand(engine, corner, side);
                        Debug.Log(string.Format(CultureInfo.InvariantCulture,
                            (recordedMode ? "recorded" : "keyed") + " [acq-trace] clip " + viewer.CurrentClip + " pos " + engine.Position + " seed {0} tick {1} {2} {3} {4} grip {5}/{6} playing {7} hold {8} gap {9:F2} target-hips {10:F2} palm-hips {11:F2} shownErr {12:F2} srcDisp {13:F2} shoulder-target {14:F2}",
                            test.Seed, engine.Tick, corner, side, corner == test.Executor ? "exec" : "opp", g?.Type, g?.Side, playing, holding,
                            holding ? link.Gap : -1f, r.PalmTarget - hips, palm - hips, r.PalmError, r.SourceWristDisplacement, Vector3.Distance(shoulder, r.PalmTarget)));
                    }

                    wasHolding[i] = holding;
                    wasGripping[i] = gripping;
                }
            }

            for (int i = 0; i < 4; i++)
            {
                if (!gripped[i] || everHeld[i]) continue;
                var corner = (Corner)(i / 2);
                string name = (corner == test.Executor ? "executor " : "opponent ") + (i % 2 == 0 ? "left" : "right");
                stats[name].NeverHeld++;
            }

            return (stats, frames, mirrored, gender);
        }
    }
}
