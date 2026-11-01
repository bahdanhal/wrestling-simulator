using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;
using Wrestling.Data.Motion;
using Wrestling.Presentation;

namespace Wrestling.Editor
{
    /// <summary>Exercises the recorded-phase viewer path with explicit review-only assignments.
    /// These windows are real recordings, but the checks do not approve their contacts for shipping.</summary>
    public static class RecordedBoutChecks
    {
        public static void RunFromCommandLine()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        private static void Run()
        {
            var content = ContentLocator.LoadDirect();
            var phases = new[] {
                new MotionPhase { Clip = "anim.double_leg_blast.penetration", Take = "harmony4d.train-grappling-001", FromFrame = 126, ToFrame = 146, Executor = 0 },
                new MotionPhase { Clip = "anim.snap_down_go_behind.snap", Take = "harmony4d.test-grappling2-043", FromFrame = 176, ToFrame = 204, Executor = 1 },
                new MotionPhase { Clip = "anim.body_lock_throw.lock", Take = "harmony4d.train-grappling-055", FromFrame = 40, ToFrame = 60, Executor = 1 },
            };
            var clips = new HashSet<string>(phases.Select(p => p.Clip));
            var review = MotionLibrary.Create(new MotionIndexFile { SchemaVersion = 1, Phases = phases },
                content.Motion.Takes, new HashSet<string>(content.Animations.All.Select(c => c.Id)));
            var cases = new Dictionary<string, (WrestlingStyle Style, ulong Seed, int Tick)>();
            foreach (WrestlingStyle style in new[] { WrestlingStyle.Freestyle, WrestlingStyle.GrecoRoman })
            {
                for (ulong seed = 1; seed <= 128 && cases.Count < phases.Length; seed++)
                {
                    var session = new BoutSession(content, style, 60, seed);
                    for (int i = 0; i < 1800 && !session.Engine.IsFinished; i++)
                    {
                        session.StepTicks(1);
                        string clip = session.Engine.ActiveTechnique?.Phase.Animation;
                        if (clip != null && clips.Contains(clip) && !cases.ContainsKey(clip))
                            cases.Add(clip, (style, seed, session.Engine.Tick));
                        if (cases.Count == phases.Length) break;
                    }
                }
            }
            if (cases.Count != phases.Length) throw new InvalidOperationException("Missing review-phase cases: " + string.Join(", ", clips.Except(cases.Keys)));
            EditorSceneManager.OpenScene(BoutViewerSceneBuilder.ScenePath, OpenSceneMode.Single);
            var viewer = UnityEngine.Object.FindAnyObjectByType<BoutViewer>();
            viewer.Initialise(content, review);
            foreach (var item in cases.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                var test = item.Value;
                viewer.Style = test.Style;
                viewer.StartBout(test.Seed);
                var baseline = new BoutSession(content, test.Style, 60, test.Seed);
                int recordedFrames = 0;
                int until = test.Tick + 120;
                int start = Math.Max(0, test.Tick - 45);
                viewer.FastForward(start);
                baseline.StepTicks(start);
                for (int i = start; i < until && !viewer.Session.Engine.IsFinished; i++)
                {
                    viewer.PlayFrame(1f / 60f);
                    baseline.Advance(1f / 60f);
                    foreach (Corner corner in new[] { Corner.Red, Corner.Blue })
                    {
                        var figure = corner == Corner.Red ? viewer.RedFigure : viewer.BlueFigure;
                        Vector3 hips = figure.Physical.HipPosition;
                        if (float.IsNaN(hips.x) || float.IsNaN(hips.y) || float.IsNaN(hips.z) || hips.sqrMagnitude > 400f)
                            throw new InvalidOperationException("Recorded review body became nonfinite or escaped the mat scene.");
                        if (viewer.RecordedLegs(corner).HasValue && !viewer.RecordedArms(corner).HasValue)
                            throw new InvalidOperationException("Recorded legs were active without recorded wrist retargeting.");
                    }
                    if (viewer.CurrentClip == item.Key && viewer.RecordedLegs(Corner.Red).HasValue) recordedFrames++;
                    var actual = viewer.Session.Engine;
                    var expected = baseline.Engine;
                    if (actual.Tick != expected.Tick || actual.Sheet.Points(Corner.Red) != expected.Sheet.Points(Corner.Red)
                        || actual.Sheet.Points(Corner.Blue) != expected.Sheet.Points(Corner.Blue)
                        || actual.Position != expected.Position || actual.ActiveTechnique?.Phase.Animation != expected.ActiveTechnique?.Phase.Animation)
                        throw new InvalidOperationException("Recorded presentation changed the authoritative simulation.");
                }
                if (recordedFrames < 2) throw new InvalidOperationException("Recorded phase was not exercised: " + item.Key);
                Debug.Log($"[recorded-bout] {item.Key}: {recordedFrames} presented recorded frames, {test.Style}, seed {test.Seed}, phase found at tick {test.Tick}; review-only library, authoritative tick/score/state/phase unchanged.");
            }
            Debug.Log("[recorded-bout] PASS all three recorded phase paths, entry and exit, actual limb retarget activation and independent simulation comparison. Contacts/exit continuity are not accepted by this check; production motion index is unchanged.");
        }
    }
}
