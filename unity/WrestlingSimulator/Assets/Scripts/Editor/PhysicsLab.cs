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
    /// Measures the physical bodies over real bouts, played live at 60 frames per second: how closely
    /// they follow what the wrestlers are trying to do, how often a body loses it while the engine
    /// has it on its feet, how deep the bodies overlap and sink into the mat, how many of the engine's
    /// grips are physically held, and what the physics costs per frame. For tuning and regressions:
    /// Unity -batchmode -nographics -projectPath unity/WrestlingSimulator -executeMethod Wrestling.Editor.PhysicsLab.RunFromCommandLine -logFile -
    /// Optional: -labSeconds N (per bout, default 45), -labBouts N (per style, default 2).
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
            public float LowestFoot = float.MaxValue;
            public int Frames;
            public int ActiveFrames;
            public int LostFrames;
            public int CollapsedFrames;
            public int StandingFrames;
            public int OverpoweredFrames;
            public int GripHands;
            public int GripHeld;
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

                viewer.FastForward(from);

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
                        markers[0].transform.position = red.PelvisTarget;
                        markers[1].transform.position = blue.PelvisTarget;
                        Debug.Log(FormattableString.Invariant($"[clip] frame {frame++} tick {viewer.Session.Engine.Tick}: red err {red.TrackingError:F2} support {red.Support:F2} {(red.IsThrown ? "thrown" : red.IsFalling ? "falling" : "")} {red.Limit} | blue err {blue.TrackingError:F2} support {blue.Support:F2} {(blue.IsThrown ? "thrown" : blue.IsFalling ? "falling" : "")} {blue.Limit} | overlap {red.DeepestOverlap(blue) * 100f:F1} cm | red hips {red.HipPosition:F2} target {red.PelvisTarget:F2} | blue hips {blue.HipPosition:F2} target {blue.PelvisTarget:F2} | clip {viewer.CurrentClip} grips {viewer.HandGrips.Holding}/{viewer.HandGrips.Gripping} push {red.GroundPush:F2}/{blue.GroundPush:F2} worst {red.WorstJointError(out string rj):F0} {rj} / {blue.WorstJointError(out string bj):F0} {bj}"));
                    });
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
            var report = new StringBuilder("[lab] Physics lab: live presentation at 60 fps\n");
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
                    Measure(viewer, seconds, measured);
                    Merge(measured, sample);
                    Merge(measured, all);
                }

                report.AppendLine(Line(style.ToString(), sample));
            }

            report.AppendLine(Line("All", all));
            float active = Mathf.Max(1, all.ActiveFrames);
            report.AppendLine("[lab] mean joint error (deg): " + string.Join(", ", all.JointSums.OrderByDescending(j => j.Value).Select(j => FormattableString.Invariant($"{j.Key} {j.Value / active:F0}"))));
            report.AppendLine("[lab] balance limits hit (% of active frames): " + string.Join(", ", all.Limits.OrderByDescending(j => j.Value).Select(j => FormattableString.Invariant($"{j.Key} {100f * j.Value / active:F0}"))));
            return report.ToString();
        }

        private static void Measure(BoutViewer viewer, float seconds, Sample into)
        {
            int frames = Mathf.RoundToInt(seconds * BoutClock.TicksPerSecond);
            var watch = new Stopwatch();
            var thrown = new bool[2];
            for (int frame = 0; frame < frames && !viewer.Session.Engine.IsFinished; frame++)
            {
                watch.Restart();
                viewer.FastForward(1);
                into.FrameMs.Add(watch.Elapsed.TotalMilliseconds);
                into.Frames++;
                into.Overlap.Add(viewer.RedFigure.Physical.DeepestOverlap(viewer.BlueFigure.Physical) * 100f);
                into.GripHands += viewer.HandGrips.Gripping;
                into.GripHeld += viewer.HandGrips.Holding;
                for (int corner = 0; corner < 2; corner++)
                {
                    WrestlerCharacter figure = corner == 0 ? viewer.RedFigure : viewer.BlueFigure;
                    PhysicalBody body = figure.Physical;
                    into.LowestFoot = Mathf.Min(into.LowestFoot, body.FootClearance * 100f);
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

                    into.ActiveFrames++;
                    float error = body.TrackingError;
                    into.Tracking.Add(error * 100f);
                    into.JointError.Add(body.WorstJointError(out string joint));
                    into.WorstJoints[joint] = into.WorstJoints.TryGetValue(joint, out int n) ? n + 1 : 1;
                    if (error > LostDistance)
                    {
                        into.LostFrames++;
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
            }
        }

        /// <summary>Logs each frame a body has lost its motion (-labVerbose).</summary>
        private static bool Verbose;

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
            into.Overlap.AddRange(from.Overlap);
            into.JointError.AddRange(from.JointError);
            into.StandingSupport.AddRange(from.StandingSupport);
            into.FrameMs.AddRange(from.FrameMs);
            foreach (KeyValuePair<string, int> joint in from.WorstJoints)
            {
                into.WorstJoints[joint.Key] = into.WorstJoints.TryGetValue(joint.Key, out int n) ? n + joint.Value : joint.Value;
            }

            into.LowestFoot = Mathf.Min(into.LowestFoot, from.LowestFoot);
            into.Frames += from.Frames;
            into.ActiveFrames += from.ActiveFrames;
            into.LostFrames += from.LostFrames;
            into.CollapsedFrames += from.CollapsedFrames;
            into.StandingFrames += from.StandingFrames;
            into.OverpoweredFrames += from.OverpoweredFrames;
            into.GripHands += from.GripHands;
            into.GripHeld += from.GripHeld;
            into.Throws += from.Throws;
            into.ThrowAngleSum += from.ThrowAngleSum;
        }

        private static string Line(string label, Sample s)
        {
            static float Mean(List<float> values) => values.Count == 0 ? 0f : values.Average();
            static float Percentile(List<float> values, float p) => values.Count == 0 ? 0f : values.OrderBy(v => v).ElementAt(Mathf.Clamp(Mathf.FloorToInt(p * values.Count), 0, values.Count - 1));
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
