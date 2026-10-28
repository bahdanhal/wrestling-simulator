using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;
using Wrestling.Data;
using Wrestling.Presentation;

namespace Wrestling.Editor
{
    /// <summary>
    /// Renders still images of the bout viewer at notable moments of real bouts (hand-fighting, an
    /// attack in flight, par terre, danger position, the result). For reviews and store material:
    /// Unity -batchmode -projectPath unity/WrestlingSimulator -executeMethod Wrestling.Editor.BoutViewerStills.RenderFromCommandLine -stillsOutput DIR
    /// </summary>
    public static class BoutViewerStills
    {
        private const int Width = 1920;
        private const int Height = 1080;

        private enum Moment
        {
            HandFighting,
            Attack,
            ParTerre,
            Danger,
            Result,
        }

        [MenuItem("Wrestling/Render Bout Viewer Stills")]
        public static void RenderFromMenu()
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "docs", "images"));
            Render(folder);
            EditorUtility.RevealInFinder(folder);
        }

        public static void RenderFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-stillsOutput");
            if (index < 0 || index + 1 >= args.Length)
            {
                Debug.LogError("Pass -stillsOutput DIR.");
                EditorApplication.Exit(1);
                return;
            }

            try
            {
                Render(args[index + 1]);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static void Render(string folder)
        {
            Directory.CreateDirectory(folder);
            EditorSceneManager.OpenScene(BoutViewerSceneBuilder.ScenePath, OpenSceneMode.Single);
            BoutViewer viewer = UnityEngine.Object.FindAnyObjectByType<BoutViewer>();
            Camera camera = UnityEngine.Object.FindAnyObjectByType<BroadcastCamera>().GetComponent<Camera>();
            GameContent content = ContentLocator.LoadDirect();
            viewer.Initialise(content);
            bool asyncShaders = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                RenderShots(folder, viewer, camera, content);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncShaders;
            }
        }

        private static void RenderShots(string folder, BoutViewer viewer, Camera camera, GameContent content)
        {

            var shots = new (WrestlingStyle Style, ulong Seed, Moment[] Moments)[]
            {
                (WrestlingStyle.Freestyle, 4, new[] { Moment.HandFighting, Moment.Attack, Moment.Danger, Moment.Result }),
                (WrestlingStyle.GrecoRoman, 7, new[] { Moment.HandFighting, Moment.ParTerre, Moment.Attack }),
                (WrestlingStyle.Womens, 3, new[] { Moment.HandFighting, Moment.Attack, Moment.ParTerre }),
            };

            foreach ((WrestlingStyle style, ulong seed, Moment[] moments) in shots)
            {
                Dictionary<Moment, int> ticks = FindMoments(content, style, seed);
                foreach (Moment moment in moments)
                {
                    if (!ticks.TryGetValue(moment, out int tick))
                    {
                        Debug.LogWarning($"{style} seed {seed}: no {moment} in this bout; skipped.");
                        continue;
                    }

                    viewer.Style = style;
                    viewer.StartBout(seed);
                    viewer.FastForward(tick);
                    camera.Render(); // Warm-up frame so shaders and fonts are ready.
                    string file = Path.Combine(folder, $"bout-{Slug(style)}-{Slug(moment.ToString())}.png");
                    Capture(camera, file);
                    Debug.Log($"Wrote {file} (tick {tick}).");
                }
            }
        }

        /// <summary>Runs the bout once to find the tick of each moment; the replay is identical because the engine is deterministic.</summary>
        private static Dictionary<Moment, int> FindMoments(GameContent content, WrestlingStyle style, ulong seed)
        {
            var session = new BoutSession(content, style, 60, seed);
            BoutEngine engine = session.Engine;
            var found = new Dictionary<Moment, int>();
            int groundedSince = -1;
            while (!engine.IsFinished)
            {
                session.StepTicks(1);
                int tick = engine.Tick;
                if (!found.ContainsKey(Moment.HandFighting) && engine.Clock.TickInPeriod > 20 * BoutClock.TicksPerSecond && engine.Position == BoutPosition.Neutral
                    && engine.ActiveTechnique == null && engine.Grips.All.Count >= 2 && !engine.IsStopped)
                {
                    found[Moment.HandFighting] = tick;
                }

                TechniqueExecution active = engine.ActiveTechnique;
                if (!found.ContainsKey(Moment.Attack) && active != null && active.Definition.Role == Core.Techniques.TechniqueRole.Attack
                    && active.PhaseIndex >= Math.Min(2, active.Definition.Phases.Count - 1) && !engine.IsStopped)
                {
                    found[Moment.Attack] = tick;
                }

                if (engine.Position == BoutPosition.ParTerre && !engine.IsStopped)
                {
                    groundedSince = groundedSince < 0 ? tick : groundedSince;
                    if (!found.ContainsKey(Moment.ParTerre) && tick - groundedSince >= 45)
                    {
                        found[Moment.ParTerre] = tick;
                    }
                }
                else
                {
                    groundedSince = -1;
                }

                if (!found.ContainsKey(Moment.Danger) && engine.Position == BoutPosition.Danger)
                {
                    found[Moment.Danger] = tick + 20;
                }
            }

            found[Moment.Result] = engine.Tick + 1;
            return found;
        }

        private static void Capture(Camera camera, string file)
        {
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                File.WriteAllBytes(file, image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static string Slug(object value)
        {
            string text = value.ToString();
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsUpper(text[i]) && i > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(text[i]));
            }

            return builder.ToString();
        }
    }
}
