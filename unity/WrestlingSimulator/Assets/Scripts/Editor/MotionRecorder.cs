using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;
using Wrestling.Data;
using Wrestling.Data.Motion;
using Wrestling.Presentation;

namespace Wrestling.Editor
{
    /// <summary>
    /// Records both wrestlers' joints from a bout in the game into the motion-take format: from a
    /// little before a chosen technique starts until a little after it ends. Used to test the
    /// recorded-motion pipeline end to end and to make reference motions for imitation training.
    /// Recordings of the game's own animation are not real motion and must not be shipped as content.
    /// Unity -batchmode -projectPath unity/WrestlingSimulator -executeMethod Wrestling.Editor.MotionRecorder.RecordFromCommandLine
    ///   -recordTechnique technique.all.headlock_throw -recordStyle Womens -recordOut DIR
    /// </summary>
    public static class MotionRecorder
    {
        private const int Lead = 20;
        private const int Tail = 30;
        private const ulong SeedsToSearch = 60;

        public static void RecordFromCommandLine()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string Arg(string name) => Array.IndexOf(args, name) is int i && i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new ArgumentException($"Pass {name}.");
                string technique = Arg("-recordTechnique");
                var style = (WrestlingStyle)Enum.Parse(typeof(WrestlingStyle), Arg("-recordStyle"));
                string output = Arg("-recordOut");
                Record(technique, style, output);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void Record(string techniqueId, WrestlingStyle style, string outputDirectory)
        {
            EditorSceneManager.OpenScene(BoutViewerSceneBuilder.ScenePath, OpenSceneMode.Single);
            BoutViewer viewer = UnityEngine.Object.FindAnyObjectByType<BoutViewer>();
            GameContent content = ContentLocator.LoadDirect();
            viewer.Initialise(content);

            (ulong seed, int start, int end, Corner executor) = Find(content, style, techniqueId);
            viewer.Style = style;
            viewer.StartBout(seed);
            viewer.FastForward(Math.Max(1, start - Lead));
            var frames = new List<IReadOnlyList<IReadOnlyList<float>>>();
            const float tick = 1f / BoutClock.TicksPerSecond;
            WrestlerCharacter executorFigure = executor == Corner.Red ? viewer.RedFigure : viewer.BlueFigure;
            WrestlerCharacter opponentFigure = executor == Corner.Red ? viewer.BlueFigure : viewer.RedFigure;
            for (int t = start - Lead; t <= end + Tail; t++)
            {
                viewer.FastForward(1);
                frames.Add(new IReadOnlyList<float>[] { Flatten(executorFigure.JointPositions()), Flatten(opponentFigure.JointPositions()) });
            }

            var take = new MotionTake
            {
                SchemaVersion = 1,
                Id = $"recorded.{techniqueId.Replace("technique.", string.Empty)}",
                Source = new MotionSource
                {
                    Title = $"{techniqueId}, {style}, seed {seed}",
                    Url = string.Empty,
                    Author = "Wrestling Simulator motion recorder",
                    License = "project",
                    Notes = "Recorded from the game's keyed animation and physics for pipeline testing and imitation references; not real motion.",
                },
                FramesPerSecond = BoutClock.TicksPerSecond,
                Actors = new[]
                {
                    new MotionActor { Name = "executor", HeightMetres = executorFigure.Height },
                    new MotionActor { Name = "opponent", HeightMetres = opponentFigure.Height },
                },
                Frames = frames,
            };
            IReadOnlyList<string> errors = take.Validate();
            if (errors.Count > 0)
            {
                throw new InvalidOperationException("Recorded take is invalid:\n- " + string.Join("\n- ", errors));
            }

            MotionLoader.WriteTake(take, outputDirectory);
            Debug.Log($"[recorder] Wrote {take.Id}: {frames.Count} frames, technique ticks {Lead}–{Lead + (end - start)} (seed {seed}).");
        }

        /// <summary>Right-handed, Y up: Unity's z is flipped.</summary>
        private static float[] Flatten(Vector3[] joints)
        {
            var values = new float[joints.Length * 3];
            for (int i = 0; i < joints.Length; i++)
            {
                values[i * 3] = joints[i].x;
                values[(i * 3) + 1] = joints[i].y;
                values[(i * 3) + 2] = -joints[i].z;
            }

            return values;
        }

        private static (ulong Seed, int Start, int End, Corner Executor) Find(GameContent content, WrestlingStyle style, string techniqueId)
        {
            for (ulong seed = 1; seed <= SeedsToSearch; seed++)
            {
                var session = new BoutSession(content, style, 60, seed);
                BoutEngine engine = session.Engine;
                TechniqueExecution tracked = null;
                int start = 0;
                while (!engine.IsFinished)
                {
                    session.StepTicks(1);
                    TechniqueExecution active = engine.ActiveTechnique;
                    if (tracked == null && active != null && active.Definition.Id == techniqueId && engine.Tick > Lead + 5)
                    {
                        tracked = active;
                        start = active.StartTick;
                    }

                    if (tracked != null && !ReferenceEquals(engine.ActiveTechnique, tracked))
                    {
                        return (seed, start, engine.Tick, tracked.Executor);
                    }
                }
            }

            throw new InvalidOperationException($"No {techniqueId} in the first {SeedsToSearch} {style} bouts.");
        }
    }
}
