using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;
using Wrestling.Data;
using Wrestling.Presentation;
using Debug = UnityEngine.Debug;

namespace Wrestling.Editor
{
    /// <summary>
    /// Measures the physical bodies over real bouts, played live (60 frames per second unless set): how closely
    /// they follow what the wrestlers are trying to do, how often a body loses it while the engine
    /// has it on its feet, how deep the bodies overlap and sink into the mat, how many of the engine's
    /// grips are physically held, and what the physics costs per frame. For tuning and regressions:
    /// Unity -batchmode -nographics -projectPath unity/WrestlingSimulator -executeMethod Wrestling.Editor.PhysicsLab.RunFromCommandLine -logFile -
    /// Optional: -labSeconds N (per bout, default 45), -labBouts N (per style, default 2), -labFps N
    /// (frame rate, default 60) and -labJitter F (each frame's time varied by up to ±F of it, as on a
    /// device that stutters): the bodies must behave the same at any frame rate.
    /// </summary>
    public static class PhysicsLab
    {
        /// <summary>Pelvis this far (m) from its animated place counts as the body having lost the motion.</summary>
        private const float LostDistance = 0.25f;

        private sealed class Sample
        {
            public readonly List<float> Tracking = new List<float>();
            public readonly List<float> Overlap = new List<float>();
            public readonly List<float> JointError = new List<float>();
            public readonly List<float> StandingSupport = new List<float>();
            public readonly List<double> FrameMs = new List<double>();
            public readonly Dictionary<string, int> WorstJoints = new Dictionary<string, int>();
            public readonly Dictionary<string, float> JointSums = new Dictionary<string, float>();
            public readonly Dictionary<string, int> Limits = new Dictionary<string, int>();
            public readonly List<float> Feasibility = new List<float>();
            public readonly List<float> HipJerk = new List<float>();
            public readonly List<float> HandJerk = new List<float>();
            public readonly List<float> Give = new List<float>();
            public readonly List<float> Sink = new List<float>();
            public readonly List<float> Lean = new List<float>();
            public readonly List<float> OpponentLoad = new List<float>();
            public float LowestFoot = float.MaxValue;
            public string LowestFootContext;
            public int Frames;
            public int ActiveFrames;
            public int LostFrames;
            public int CollapsedFrames;
            public int StandingFrames;
            public int OverpoweredFrames;
            public int GripHands;
            public int GripHeld;
            public int ShownHolds;
            public float ShownGapBefore;
            public float ShownGapAfter;
            public float ShownGapWorst;
            public int Throws;
            public float ThrowAngleSum;
        }

        [MenuItem("Wrestling/Diagnostics/Run Physics Lab")]
        public static void RunFromMenu() => Debug.Log(Run(45f, 2));

        public static void RunFromCommandLine()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                float seconds = Argument(args, "-labSeconds", 45f);
                int bouts = (int)Argument(args, "-labBouts", 2f);
                Verbose = Array.IndexOf(args, "-labVerbose") >= 0;
                FramesPerSecond = Mathf.Max(10f, Argument(args, "-labFps", 60f));
                Jitter = Mathf.Clamp(Argument(args, "-labJitter", 0f), 0f, 0.9f);
                // -labNoCompliance: the bodies do not give under load (to compare against).
                BoutViewer.PhysicalCompliance = Array.IndexOf(args, "-labNoCompliance") < 0;
                Debug.Log(Run(seconds, bouts));
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Renders a stretch of one bout as video: -clipStyle fs|gr|ww -clipSeed N -clipFrom TICK -clipTicks N -clipOutput FILE.mp4
        /// (needs graphics: run without -nographics).
        /// </summary>
        public static void ClipFromCommandLine()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string Text(string name, string fallback)
                {
                    int index = Array.IndexOf(args, name);
                    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
                }

                WrestlingStyle style = Text("-clipStyle", "fs") switch { "gr" => WrestlingStyle.GrecoRoman, "ww" => WrestlingStyle.Womens, _ => WrestlingStyle.Freestyle };
                EditorSceneManager.OpenScene(BoutViewerSceneBuilder.ScenePath, OpenSceneMode.Single);
                BoutViewer viewer = UnityEngine.Object.FindAnyObjectByType<BoutViewer>();
                Camera camera = UnityEngine.Object.FindAnyObjectByType<BroadcastCamera>().GetComponent<Camera>();
                viewer.Initialise(ContentLocator.LoadDirect());
                viewer.Style = style;
                viewer.StartBout((ulong)Argument(args, "-clipSeed", 1f));
                if (Array.IndexOf(args, "-clipNoContact") >= 0)
                {
                    viewer.RedFigure.Physical.IgnoreContactWith(viewer.BlueFigure.Physical);
                }

                int from = Math.Max(1, (int)Argument(args, "-clipFrom", 60f));
                if (Array.IndexOf(args, "-clipLog") >= 0)
                {
                    // Every presented frame logged, the jump's warm-up included, to see how a state came about.
                    BoutViewer.Presented = v =>
                    {
                        PhysicalBody r = v.RedFigure.Physical;
                        PhysicalBody b = v.BlueFigure.Physical;
                        Debug.Log(FormattableString.Invariant($"[live] tick {v.Session.Engine.Tick}: red hips {r.HipPosition:F2} target {r.PelvisTarget:F2} {(r.IsThrown ? "thrown" : r.IsFalling ? "falling" : "")} w {r.Weight:F0} sup {r.Support:F2} push {r.GroundPush:F2} {r.Limit} foot {r.FootClearance:F2} | blue hips {b.HipPosition:F2} target {b.PelvisTarget:F2} {(b.IsThrown ? "thrown" : b.IsFalling ? "falling" : "")} | clip {v.CurrentClip} grips {v.HandGrips.Holding}"));
                    };
                }

                bool livePlayback = Array.IndexOf(args, "-clipNoJump") >= 0;
                if (livePlayback)
                {
                    // Use the same event handling and interpolation as the lab, including restarts.
                    for (int i = 0; i < from; i++)
                    {
                        viewer.PlayFrame(1f / 60f);
                    }
                }
                else
                {
                    viewer.FastForward(from);
                }

                bool asyncShaders = ShaderUtil.allowAsyncCompilation;
                ShaderUtil.allowAsyncCompilation = false;
                try
                {
                    // Markers: where the animation wants each pelvis (red and blue spheres), to tell the
                    // physical bodies' own motion from the animation's.
                    GameObject[] markers = new GameObject[2];
                    for (int i = 0; i < 2; i++)
                    {
                        markers[i] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        UnityEngine.Object.DestroyImmediate(markers[i].GetComponent<Collider>());
                        markers[i].transform.localScale = Vector3.one * 0.12f;
                        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                        material.SetColor("_BaseColor", i == 0 ? new Color(1f, 0.3f, 0.3f) : new Color(0.3f, 0.6f, 1f));
                        markers[i].GetComponent<MeshRenderer>().sharedMaterial = material;
                    }

                    int frame = 0;
                    BoutViewerStills.CaptureVideo(viewer, camera, Text("-clipOutput", "clip.mp4"), (int)Argument(args, "-clipTicks", 150f), () =>
                    {
                        PhysicalBody red = viewer.RedFigure.Physical;
                        PhysicalBody blue = viewer.BlueFigure.Physical;
                        if (Array.IndexOf(args, "-clipSide") >= 0)
                        {
                            // Profile view at hip height, square to the line between the wrestlers.
                            Vector3 a = red.HipPosition;
                            Vector3 b = blue.HipPosition;
                            Vector3 across = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(b - a, Vector3.up).normalized);
                            Vector3 focus = ((a + b) * 0.5f) + (Vector3.up * 0.15f);
                            camera.transform.SetPositionAndRotation(focus + (across * 3.2f) + (Vector3.up * 0.25f), Quaternion.LookRotation(-across - (Vector3.up * 0.08f), Vector3.up));
                            camera.fieldOfView = 40f;
                        }

                        markers[0].transform.position = red.PelvisTarget;
                        markers[1].transform.position = blue.PelvisTarget;
                        Debug.Log(FormattableString.Invariant($"[clip] frame {frame++} tick {viewer.Session.Engine.Tick}: red err {red.TrackingError:F2} support {red.Support:F2} {(red.IsThrown ? "thrown" : red.IsFalling ? "falling" : "")} {red.Limit} | blue err {blue.TrackingError:F2} support {blue.Support:F2} {(blue.IsThrown ? "thrown" : blue.IsFalling ? "falling" : "")} {blue.Limit} | overlap {red.DeepestOverlap(blue) * 100f:F1} cm | red hips {red.HipPosition:F2} target {red.PelvisTarget:F2} | blue hips {blue.HipPosition:F2} target {blue.PelvisTarget:F2} | clip {viewer.CurrentClip} grips {viewer.HandGrips.Holding}/{viewer.HandGrips.Gripping} push {red.GroundPush:F2}/{blue.GroundPush:F2} worst {red.WorstJointError(out string rj):F0} {rj} / {blue.WorstJointError(out string bj):F0} {bj}"));
                    }, livePlayback);
                }
                finally
                {
                    ShaderUtil.allowAsyncCompilation = asyncShaders;
                }

                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static string Run(float seconds, int bouts)
        {
            EditorSceneManager.OpenScene(BoutViewerSceneBuilder.ScenePath, OpenSceneMode.Single);
            BoutViewer viewer = UnityEngine.Object.FindAnyObjectByType<BoutViewer>();
            GameContent content = ContentLocator.LoadDirect();
            viewer.Initialise(content);
            PhysicalBody.MeasureFeasibility = true;
            var report = new StringBuilder(FormattableString.Invariant($"[lab] Physics lab: live presentation at {FramesPerSecond:F0} fps (frame time ±{Jitter * 100f:F0}%), load compliance {(BoutViewer.PhysicalCompliance ? "on" : "off")}\n"));
            report.AppendLine("[lab] style | frames | pelvis err mean/p95/max cm | lost % | collapsed % | overlap mean/max cm | joint err mean deg | lowest foot cm | standing support | overpowered % | grips held % | throw angle | ms/frame mean/max");
            var all = new Sample();
            foreach (WrestlingStyle style in new[] { WrestlingStyle.Freestyle, WrestlingStyle.GrecoRoman, WrestlingStyle.Womens })
            {
                var sample = new Sample();
                for (int bout = 0; bout < bouts; bout++)
                {
                    viewer.Style = style;
                    viewer.StartBout((ulong)(bout + 1));
                    var measured = new Sample();
                    Measure(viewer, seconds, measured, bout + 1);
                    Merge(measured, sample);
                    Merge(measured, all);
                }

                report.AppendLine(Line(style.ToString(), sample));
                if (sample.LowestFootContext != null)
                {
                    report.AppendLine("[lab] lowest foot: " + sample.LowestFootContext);
                }
            }

            report.AppendLine(Line("All", all));
            report.AppendLine(FormattableString.Invariant($"[lab] contact feasibility: {(all.Feasibility.Count == 0 ? 1f : all.Feasibility.Average()) * 100f:F0}% of the balance force and turn the mat contacts could make themselves (the rest stands in for stepping and leaning on the opponent)"));
            float active = Mathf.Max(1, all.ActiveFrames);
            report.AppendLine(FormattableString.Invariant($"[lab] pelvis err median {Percentile(all.Tracking, 0.5f):F1} cm, p75 {Percentile(all.Tracking, 0.75f):F1} cm"));
            float holds = Mathf.Max(1, all.ShownHolds);
            report.AppendLine(FormattableString.Invariant($"[lab] shown palm to held point over {all.ShownHolds} holding-hand frames: {all.ShownGapBefore / holds:F2} cm from physics, {all.ShownGapAfter / holds:F2} cm after the post-physics pass (worst frame {all.ShownGapWorst:F2} cm)"));
            report.AppendLine(FormattableString.Invariant($"[lab] shown jerk (mm per frame², mean/p99): hips {Mean(all.HipJerk):F2}/{Percentile(all.HipJerk, 0.99f):F2}, hands {Mean(all.HandJerk):F2}/{Percentile(all.HandJerk, 0.99f):F2}"));
            report.AppendLine(FormattableString.Invariant($"[lab] load: opponent force on a body mean/p95 {Mean(all.OpponentLoad):F0}/{Percentile(all.OpponentLoad, 0.95f):F0} N; give: largest joint shift mean/p95/max {Mean(all.Give):F2}/{Percentile(all.Give, 0.95f):F2}/{(all.Give.Count == 0 ? 0f : all.Give.Max()):F2} cm, hips sink mean/max {Mean(all.Sink):F2}/{(all.Sink.Count == 0 ? 0f : all.Sink.Max()):F2} cm, lean mean/max {Mean(all.Lean):F1}/{(all.Lean.Count == 0 ? 0f : all.Lean.Max()):F1} deg"));
            report.AppendLine("[lab] mean joint error (deg): " + string.Join(", ", all.JointSums.OrderByDescending(j => j.Value).Select(j => FormattableString.Invariant($"{j.Key} {j.Value / active:F0}"))));
            report.AppendLine("[lab] balance limits hit (% of active frames): " + string.Join(", ", all.Limits.OrderByDescending(j => j.Value).Select(j => FormattableString.Invariant($"{j.Key} {100f * j.Value / active:F0}"))));
            return report.ToString();
        }

        private static void Measure(BoutViewer viewer, float seconds, Sample into, int seed)
        {
            int frames = Mathf.RoundToInt(seconds * FramesPerSecond);
            // Editor diagnostics only: a fixed seed so a jittery run repeats exactly.
            var jitter = new System.Random(17);
            var watch = new Stopwatch();
            var thrown = new bool[2];
            // The last two shown positions of each wrestler's hips and hands, for frame-to-frame jerk.
            var history = new Vector3[2, 3, 2];
            int tracked = 0;
            for (int frame = 0; frame < frames && !viewer.Session.Engine.IsFinished; frame++)
            {
                watch.Restart();
                float frameSeconds = (1f / FramesPerSecond) * (1f + (Jitter * (((float)jitter.NextDouble() * 2f) - 1f)));
                viewer.PlayFrame(frameSeconds);
                into.FrameMs.Add(watch.Elapsed.TotalMilliseconds);
                into.Frames++;
                into.Overlap.Add(viewer.RedFigure.Physical.DeepestOverlap(viewer.BlueFigure.Physical) * 100f);
                into.GripHands += viewer.HandGrips.Gripping;
                into.GripHeld += viewer.HandGrips.ActiveHolding;
                foreach (Corner shown in new[] { Corner.Red, Corner.Blue })
                {
                    // The hands as shown, after the post-physics pass, against what they hold.
                    PostPhysicsReprojector.Result reprojected = viewer.Reprojected(shown);
                    into.ShownHolds += reprojected.Holds;
                    into.ShownGapBefore += reprojected.GapBefore * 100f;
                    into.ShownGapAfter += reprojected.GapAfter * 100f;
                    into.ShownGapWorst = Mathf.Max(into.ShownGapWorst, reprojected.Holds == 0 ? 0f : reprojected.GapAfter * 100f / reprojected.Holds);
                }
                for (int corner = 0; corner < 2; corner++)
                {
                    WrestlerCharacter figure = corner == 0 ? viewer.RedFigure : viewer.BlueFigure;
                    PhysicalBody body = figure.Physical;
                    float foot = body.FootClearance * 100f;
                    // A reset teleports the articulation to the animated pose without advancing
                    // physics. Its collider is not the displayed physical pose on that frame.
                    if (body.Weight > 0f && foot < into.LowestFoot)
                    {
                        into.LowestFoot = foot;
                        into.LowestFootContext = FormattableString.Invariant($"seed {seed}, tick {viewer.Session.Engine.Tick}, {(corner == 0 ? "red" : "blue")}, {foot:F1} cm, clip {viewer.CurrentClip}, thrown {body.IsThrown}, falling {body.IsFalling}");
                    }
                    if (thrown[corner] && !body.IsThrown)
                    {
                        into.Throws++;
                        into.ThrowAngleSum += Mathf.Abs(body.ThrowAngle);
                    }

                    thrown[corner] = body.IsThrown;
                    if (body.IsThrown || body.IsFalling || body.Weight <= 0f)
                    {
                        continue;
                    }

                    if (tracked >= 2)
                    {
                        into.HipJerk.Add(Jerk(body.HipPosition, history[corner, 0, 0], history[corner, 0, 1]));
                        into.HandJerk.Add(Jerk(body.Link("LeftHand").transform.position, history[corner, 1, 0], history[corner, 1, 1]));
                        into.HandJerk.Add(Jerk(body.Link("RightHand").transform.position, history[corner, 2, 0], history[corner, 2, 1]));
                    }

                    LoadCompliance give = viewer.Compliance((Corner)corner);
                    into.Give.Add(give.Displacement * 100f);
                    into.Sink.Add(give.Sink * 100f);
                    into.Lean.Add(give.LeanDegrees);
                    into.OpponentLoad.Add(body.OpponentForce.magnitude);

                    into.ActiveFrames++;
                    float error = body.TrackingError;
                    into.Tracking.Add(error * 100f);
                    into.JointError.Add(body.WorstJointError(out string joint));
                    into.WorstJoints[joint] = into.WorstJoints.TryGetValue(joint, out int n) ? n + 1 : 1;
                    if (error > LostDistance)
                    {
                        into.LostFrames++;
                    }

                    if (body.Support > 0f)
                    {
                        into.Feasibility.Add(body.Feasibility);
                    }

                    if (body.Overpowered)
                    {
                        into.OverpoweredFrames++;
                        into.Limits[body.Limit] = into.Limits.TryGetValue(body.Limit, out int l) ? l + 1 : 1;
                    }

                    foreach ((string name, float jointError, float twist, float target) in body.JointErrors())
                    {
                        into.JointSums[name + " target"] = (into.JointSums.TryGetValue(name + " target", out float tt) ? tt : 0f) + target;
                        into.JointSums[name] = (into.JointSums.TryGetValue(name, out float sum) ? sum : 0f) + jointError;
                        into.JointSums[name + " twist"] = (into.JointSums.TryGetValue(name + " twist", out float t) ? t : 0f) + twist;
                    }

                    if (error > LostDistance && Verbose)
                    {
                        Debug.Log(FormattableString.Invariant($"[lost] {viewer.Style} tick {viewer.Session.Engine.Tick} {(Corner)corner}: err {error:F2} m, hips {body.HipHeight:F2} m, support {body.Support:F2}, ground {viewer.Shown((Corner)corner).Ground}, limit {body.Limit}, push {body.GroundPush:F2}"));
                    }

                    if (viewer.Shown((Corner)corner).Ground == GroundState.Standing)
                    {
                        into.StandingFrames++;
                        into.StandingSupport.Add(body.Support);
                        if (body.HipHeight < 0.45f * figure.Height * 0.53f)
                        {
                            into.CollapsedFrames++;
                        }
                    }
                }

                for (int corner = 0; corner < 2; corner++)
                {
                    PhysicalBody body = (corner == 0 ? viewer.RedFigure : viewer.BlueFigure).Physical;
                    Remember(history, corner, 0, body.HipPosition);
                    Remember(history, corner, 1, body.Link("LeftHand").transform.position);
                    Remember(history, corner, 2, body.Link("RightHand").transform.position);
                }

                tracked++;
            }
        }

        /// <summary>Second difference of a shown point over the last three frames (mm): how hard it is shaking.</summary>
        private static float Jerk(Vector3 now, Vector3 last, Vector3 before) => (now - (2f * last) + before).magnitude * 1000f;

        private static void Remember(Vector3[,,] history, int corner, int point, Vector3 position)
        {
            history[corner, point, 1] = history[corner, point, 0];
            history[corner, point, 0] = position;
        }

        /// <summary>Logs each frame a body has lost its motion (-labVerbose).</summary>
        private static bool Verbose;

        /// <summary>Frame rate the lab plays at (-labFps), and how much each frame's time varies (-labJitter).</summary>
        private static float FramesPerSecond = 60f;
        private static float Jitter;

        private static void Merge(Sample from, Sample into)
        {
            foreach (KeyValuePair<string, float> joint in from.JointSums)
            {
                into.JointSums[joint.Key] = (into.JointSums.TryGetValue(joint.Key, out float sum) ? sum : 0f) + joint.Value;
            }

            foreach (KeyValuePair<string, int> limit in from.Limits)
            {
                into.Limits[limit.Key] = into.Limits.TryGetValue(limit.Key, out int n) ? n + limit.Value : limit.Value;
            }

            into.Tracking.AddRange(from.Tracking);
            into.Feasibility.AddRange(from.Feasibility);
            into.HipJerk.AddRange(from.HipJerk);
            into.HandJerk.AddRange(from.HandJerk);
            into.Give.AddRange(from.Give);
            into.Sink.AddRange(from.Sink);
            into.Lean.AddRange(from.Lean);
            into.OpponentLoad.AddRange(from.OpponentLoad);
            into.Overlap.AddRange(from.Overlap);
            into.JointError.AddRange(from.JointError);
            into.StandingSupport.AddRange(from.StandingSupport);
            into.FrameMs.AddRange(from.FrameMs);
            foreach (KeyValuePair<string, int> joint in from.WorstJoints)
            {
                into.WorstJoints[joint.Key] = into.WorstJoints.TryGetValue(joint.Key, out int n) ? n + joint.Value : joint.Value;
            }

            if (from.LowestFoot < into.LowestFoot)
            {
                into.LowestFoot = from.LowestFoot;
                into.LowestFootContext = from.LowestFootContext;
            }
            into.Frames += from.Frames;
            into.ActiveFrames += from.ActiveFrames;
            into.LostFrames += from.LostFrames;
            into.CollapsedFrames += from.CollapsedFrames;
            into.StandingFrames += from.StandingFrames;
            into.OverpoweredFrames += from.OverpoweredFrames;
            into.GripHands += from.GripHands;
            into.GripHeld += from.GripHeld;
            into.ShownHolds += from.ShownHolds;
            into.ShownGapBefore += from.ShownGapBefore;
            into.ShownGapAfter += from.ShownGapAfter;
            into.ShownGapWorst = Mathf.Max(into.ShownGapWorst, from.ShownGapWorst);
            into.Throws += from.Throws;
            into.ThrowAngleSum += from.ThrowAngleSum;
        }

        private static float Mean(List<float> values) => values.Count == 0 ? 0f : values.Average();

        private static float Percentile(List<float> values, float p) => values.Count == 0 ? 0f : values.OrderBy(v => v).ElementAt(Mathf.Clamp(Mathf.FloorToInt(p * values.Count), 0, values.Count - 1));

        private static string Line(string label, Sample s)
        {
            float active = Mathf.Max(1, s.ActiveFrames);
            string joints = string.Join(", ", s.WorstJoints.OrderByDescending(j => j.Value).Take(3).Select(j => $"{j.Key} {100f * j.Value / active:F0}%"));
            return string.Format(
                CultureInfo.InvariantCulture,
                "[lab] {0} | {1} | {2:F1}/{3:F1}/{4:F1} | {5:F1} | {6:F1} | {7:F1}/{8:F1} | {9:F1} ({10}) | {11:F1} | {12:F2} | {13:F1} | {14:F0} | {15}/{16:F0}° | {17:F2}/{18:F1}",
                label,
                s.Frames,
                Mean(s.Tracking), Percentile(s.Tracking, 0.95f), s.Tracking.Count == 0 ? 0f : s.Tracking.Max(),
                100f * s.LostFrames / active,
                100f * s.CollapsedFrames / Mathf.Max(1, s.StandingFrames),
                Mean(s.Overlap), s.Overlap.Count == 0 ? 0f : s.Overlap.Max(),
                Mean(s.JointError), joints,
                s.LowestFoot,
                Mean(s.StandingSupport),
                100f * s.OverpoweredFrames / active,
                s.GripHands == 0 ? 0f : 100f * s.GripHeld / s.GripHands,
                s.Throws, s.Throws == 0 ? 0f : s.ThrowAngleSum / s.Throws,
                s.FrameMs.Count == 0 ? 0.0 : s.FrameMs.Average(), s.FrameMs.Count == 0 ? 0.0 : s.FrameMs.Max());
        }

        private static float Argument(string[] args, string name, float fallback)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length && float.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
        }
    }
}
