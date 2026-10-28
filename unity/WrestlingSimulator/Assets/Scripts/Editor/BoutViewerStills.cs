using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Core.Wrestlers;
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

        private const ulong SeedsToSearch = 40;

        private enum Moment
        {
            HandFighting,
            Shot,
            Throw,
            Counter,
            ParTerre,
            ParTerreTurn,
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

            var shots = new (WrestlingStyle Style, Moment[] Moments)[]
            {
                (WrestlingStyle.Freestyle, new[] { Moment.HandFighting, Moment.Shot, Moment.Throw, Moment.Counter, Moment.ParTerreTurn, Moment.Danger, Moment.Result }),
                (WrestlingStyle.GrecoRoman, new[] { Moment.HandFighting, Moment.Throw, Moment.ParTerre, Moment.ParTerreTurn }),
                (WrestlingStyle.Womens, new[] { Moment.HandFighting, Moment.Shot, Moment.Throw, Moment.ParTerre }),
            };

            foreach ((WrestlingStyle style, Moment[] moments) in shots)
            {
                var found = new Dictionary<Moment, (ulong Seed, int Tick)>();
                for (ulong seed = 1; seed <= SeedsToSearch && found.Count < moments.Length; seed++)
                {
                    foreach (KeyValuePair<Moment, int> moment in FindMoments(content, style, seed))
                    {
                        if (Array.IndexOf(moments, moment.Key) >= 0 && !found.ContainsKey(moment.Key))
                        {
                            found[moment.Key] = (seed, moment.Value);
                        }
                    }
                }

                foreach (Moment moment in moments)
                {
                    if (!found.TryGetValue(moment, out (ulong Seed, int Tick) shot))
                    {
                        Debug.LogWarning($"{style}: no {moment} in the first {SeedsToSearch} bouts; skipped.");
                        continue;
                    }

                    viewer.Style = style;
                    var build = System.Diagnostics.Stopwatch.StartNew();
                    viewer.StartBout(shot.Seed);
                    Debug.Log($"Built both wrestlers in {build.ElapsedMilliseconds} ms.");
                    viewer.FastForward(shot.Tick);
                    camera.Render(); // Warm-up frame so shaders and fonts are ready.
                    string file = Path.Combine(folder, $"bout-{Slug(style)}-{Slug(moment.ToString())}.png");
                    Capture(camera, file);
                    Debug.Log($"Wrote {file} (seed {shot.Seed}, tick {shot.Tick}).");
                    if (moment == Moment.HandFighting)
                    {
                        string closeUp = Path.Combine(folder, $"bout-{Slug(style)}-close-up.png");
                        CaptureCloseUp(camera, closeUp, "Head", 0.25f, 1.1f);
                        Debug.Log($"Wrote {closeUp}.");
                        string hands = Path.Combine(folder, $"bout-{Slug(style)}-close-up-hands.png");
                        CaptureCloseUp(camera, hands, "LeftHand", 0f, 0.45f);
                        Debug.Log($"Wrote {hands}.");
                    }
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
                if (active != null && !engine.IsStopped)
                {
                    float progress = active.PhaseLength > 0 ? (float)active.PhaseTick / active.PhaseLength : 1f;
                    TechniqueDefinition technique = active.Definition;
                    if (!found.ContainsKey(Moment.Shot) && technique.Family == TechniqueFamily.LegAttacks && active.PhaseIndex == 0 && progress >= 0.85f)
                    {
                        found[Moment.Shot] = tick;
                    }

                    if (!found.ContainsKey(Moment.Throw) && technique.Amplitude == Amplitude.GrandAmplitude && active.IsLastPhase && progress >= 0.45f
                        && technique.Start.Positions.Contains(StartPosition.NeutralTied))
                    {
                        found[Moment.Throw] = tick;
                    }

                    if (!found.ContainsKey(Moment.Counter) && technique.Role != TechniqueRole.Attack && engine.Position == BoutPosition.Neutral && progress >= 0.6f)
                    {
                        found[Moment.Counter] = tick;
                    }

                    if (!found.ContainsKey(Moment.ParTerreTurn) && technique.Family == TechniqueFamily.ParTerreTurns && progress >= 0.7f)
                    {
                        found[Moment.ParTerreTurn] = tick;
                    }
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

        /// <summary>Close view of one bone of the red wrestler (the figure furthest left), from in front and to the side, for checking faces, hands and joints.</summary>
        private static void CaptureCloseUp(Camera camera, string file, string boneName, float drop, float distance)
        {
            Transform Find(WrestlerCharacter figure, string name) =>
                figure.GetComponentsInChildren<Transform>().First(t => t.name == name || t.name.EndsWith(":" + name));

            WrestlerCharacter[] figures = UnityEngine.Object.FindObjectsByType<WrestlerCharacter>(FindObjectsInactive.Exclude)
                .OrderBy(f => Find(f, "Hips").position.x)
                .ToArray();
            Transform bone = Find(figures[0], boneName);
            Transform hips = Find(figures[0], "Hips");
            Vector3 opponent = Find(figures[1], "Hips").position;
            Vector3 front = Vector3.ProjectOnPlane(opponent - hips.position, Vector3.up);
            front = front.sqrMagnitude > 1e-6f ? front.normalized : Vector3.back;

            Vector3 side = Vector3.Cross(Vector3.up, front);
            Vector3 away = bone.position - opponent;
            if (Vector3.Dot(side, away) < 0f)
            {
                side = -side;
            }

            Vector3 target = bone.position - (Vector3.up * drop);
            Vector3 eye = target + (front * (distance * 0.6f)) + (side * (distance * 0.8f)) + (Vector3.up * (distance * 0.3f));
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fieldOfView = camera.fieldOfView;
            try
            {
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
                camera.fieldOfView = 30f;
                camera.Render();
                Capture(camera, file);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fieldOfView;
            }
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
