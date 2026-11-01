using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Wrestling.Core.Rules;
using Wrestling.Data;
using Wrestling.Data.Motion;
using Wrestling.Presentation;
using Pose = Wrestling.Presentation.Pose;

namespace Wrestling.Editor
{
    /// <summary>Native-frame comparison of recorded joints and the character rig. Does not assign a production clip.</summary>
    public static class RecordedMotionReview
    {
        private const int Width = 1280, Height = 720;
        private static readonly (int, int)[] Links = {
            (0,1),(1,2),(1,3),(1,4),(3,5),(5,7),(4,6),(6,8),
            (0,9),(0,10),(9,11),(11,13),(13,15),(10,12),(12,14),(14,16)
        };

        public static void RenderFromCommandLine()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string Arg(string key)
                {
                    int i = Array.IndexOf(args, key);
                    if (i < 0 || i + 1 >= args.Length || args[i + 1].StartsWith("-"))
                    {
                        throw new ArgumentException("Missing " + key);
                    }

                    return args[i + 1];
                }

                bool retarget = Array.IndexOf(args, "-reviewRawRig") < 0;
                string retargetOption = Array.IndexOf(args, "--reviewRetargetLegs") >= 0 ? "--reviewRetargetLegs" : "-reviewRetargetLegs";
                if (Array.IndexOf(args, retargetOption) >= 0 && !bool.TryParse(Arg(retargetOption), out retarget))
                {
                    throw new ArgumentException(retargetOption + " must be true or false.");
                }

                Render(Arg("-reviewTake"), int.Parse(Arg("-reviewFrom"), CultureInfo.InvariantCulture), int.Parse(Arg("-reviewTo"), CultureInfo.InvariantCulture), int.Parse(Arg("-reviewExecutor"), CultureInfo.InvariantCulture), Arg("-reviewOutput"), retarget);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        public static void Render(string id, int from, int to, int executor, string output, bool retargetLegs = true)
        {
            GameContent content = ContentLocator.LoadDirect();
            MotionTake take = content.Motion.Takes.FirstOrDefault(t => t.Id == id) ?? throw new ArgumentException("Unknown take: " + id);
            if (from < 0 || to <= from || to >= take.FrameCount || (executor != 0 && executor != 1))
            {
                throw new ArgumentException("Invalid frame range or executor (must be 0 or 1).");
            }
            var library = AssetDatabase.LoadAssetAtPath<CharacterLibrary>(CharacterLibraryBuilder.LibraryPath) ?? throw new FileNotFoundException("Build the character library first.");
            Directory.CreateDirectory(output);
            string frames = Path.Combine(output, "frames");
            if (Directory.Exists(frames) && Directory.EnumerateFileSystemEntries(frames).Any())
            {
                throw new IOException("Frame directory is not empty: " + frames);
            }

            Directory.CreateDirectory(frames);
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            RenderTexture target = null;
            Texture2D image = null;
            Texture2D sheet = null;
            var materials = new Material[3];
            RenderTexture old = RenderTexture.active;
            bool async = ShaderUtil.allowAsyncCompilation;
            try
            {
                root = new GameObject("Recorded motion review");
                SceneManager.MoveGameObjectToScene(root, scene);
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                int count = Math.Min(6, to - from + 1);
                sheet = new Texture2D(Width * 2, Height * ((count + 1) / 2), TextureFormat.RGB24, false);
                ShaderUtil.allowAsyncCompilation = false;
                var session = new BoutSession(content, WrestlingStyle.Freestyle, 60, 1);
                var figures = new WrestlerCharacter[2];
                var looks = new[] { new WrestlerLook(session.Red, library.Male.Skins.Length), new WrestlerLook(session.Blue, library.Male.Skins.Length) };
                for (int i = 0; i < 2; i++)
                {
                    var go = new GameObject("Character " + i);
                    go.transform.SetParent(root.transform);
                    figures[i] = go.AddComponent<WrestlerCharacter>();
                    figures[i].Build(library.Male, looks[i], i == 0 ? Color.red : Color.blue);
                    foreach (var renderer in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        renderer.forceMatrixRecalculationPerRender = true;
                    }
                }
                var phase = new MotionPhase { Clip = "review only", Take = id, FromFrame = from, ToFrame = to, Executor = executor, LeftSide = false };
                var playback = new MotionPlayback();
                var initial = new Pose { Pelvis = Vector3.zero };
                var opponent = new Pose { Pelvis = Vector3.right };
                playback.Begin(take, phase, in initial, in opponent, looks[0].HeightMetres, looks[1].HeightMetres, false);
                // One global playback transform and scale drive both panels; only the panel offset differs.
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                for (int f = from; f <= to; f++)
                {
                    var p = playback.Sample((float)(f - from) / (to - from));
                    foreach (Vector3 v in Points(p.Executor).Concat(Points(p.Opponent)))
                    {
                        minX = Mathf.Min(minX, v.x);
                        maxX = Mathf.Max(maxX, v.x);
                        minY = Mathf.Min(minY, v.y);
                        maxY = Mathf.Max(maxY, v.y);
                    }
                }
                float span = Mathf.Max(2.2f, maxX - minX + 1f);
                Vector3 left = Vector3.left * span * 0.6f, right = Vector3.right * span * 0.6f;
                var segments = new Transform[2, Links.Length];
                for (int a = 0; a < 2; a++)
                {
                    materials[a] = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    materials[a].color = a == 0 ? new Color(1f, 0.2f, 0.15f) : new Color(0.2f, 0.55f, 1f);
                    for (int j = 0; j < Links.Length; j++)
                    {
                        var bone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        bone.transform.SetParent(root.transform);
                        bone.GetComponent<Renderer>().sharedMaterial = materials[a];
                        segments[a, j] = bone.transform;
                    }
                }
                // Both lines mark the same world-space mat height as the sampled poses.
                materials[2] = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                materials[2].color = new Color(0.75f, 0.78f, 0.8f);
                foreach (Vector3 panel in new[] { left, right })
                {
                    var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    floor.name = "Floor height reference";
                    floor.transform.SetParent(root.transform);
                    floor.transform.position = new Vector3((minX + maxX) * 0.5f + panel.x, 0f, 0f);
                    floor.transform.localScale = new Vector3(span, 0.008f, 0.008f);
                    floor.GetComponent<Renderer>().sharedMaterial = materials[2];
                }

                var camObj = new GameObject("Camera");
                camObj.transform.SetParent(root.transform);
                var camera = camObj.AddComponent<Camera>();
                camera.scene = scene;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
                camera.orthographic = true;
                float centreX = (minX + maxX) * 0.5f, centreY = (minY + maxY) * 0.5f;
                camera.orthographicSize = Mathf.Max((maxY - minY + 1.2f) * 0.5f, (span * 2.2f) / (2f * Width / Height));
                camera.transform.position = new Vector3(centreX, centreY, -12f);
                camera.transform.rotation = Quaternion.identity;
                camera.targetTexture = target;
                var lightObj = new GameObject("Light");
                lightObj.transform.SetParent(root.transform);
                var light = lightObj.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.5f;
                lightObj.transform.rotation = Quaternion.Euler(30f, -30f, 0f);
                Label(root.transform, "ALIGNED SOURCE JOINTS", new Vector3(centreX + left.x, centreY + camera.orthographicSize * 0.85f, 0));
                Label(root.transform, retargetLegs ? "CHARACTER RIG / ANKLE TRAJECTORY IK" : "CHARACTER RIG / RAW ROTATIONS", new Vector3(centreX + right.x, centreY + camera.orthographicSize * 0.85f, 0));
                TextMesh frameLabel = Label(root.transform, "", new Vector3(centreX, centreY - camera.orthographicSize * 0.88f, 0));
                frameLabel.characterSize = 0.014f;
                int[] selected = Enumerable.Range(0, count).Select(i => from + Mathf.RoundToInt((to - from) * (float)i / (count - 1))).ToArray();
                var ankleFrames = new List<object>();
                int unreachableAnkles = 0;
                float maxUnreachableResidual = 0f;
                float maxActualSolveError = 0f;
                for (int f = from; f <= to; f++)
                {
                    frameLabel.text = $"FRAME {f}   |   {(f - from) / take.FramesPerSecond:F3} s from start   |   {take.FramesPerSecond:F3} fps";
                    var pair = playback.Sample((float)(f - from) / (to - from));
                    Pose[] poses = { pair.Executor, pair.Opponent };
                    for (int a = 0; a < 2; a++)
                    {
                        Vector3[] pts = Points(poses[a]);
                        for (int j = 0; j < Links.Length; j++)
                        {
                            var (x, y) = Links[j];
                            Vector3 start = pts[x] + left, end = pts[y] + left, delta = end - start;
                            segments[a, j].position = (start + end) * 0.5f;
                            segments[a, j].rotation = Quaternion.FromToRotation(Vector3.up, delta);
                            segments[a, j].localScale = new Vector3(0.025f, delta.magnitude * 0.5f, 0.025f);
                        }
                        Pose body = Offset(poses[a], right);
                        Vector3 targetL = body.AnkleL;
                        Vector3 targetR = body.AnkleR;
                        CharacterRig.RecordedLegsResult? fit = retargetLegs ? figures[a].Rig.RetargetRecordedLegs(ref body) : (CharacterRig.RecordedLegsResult?)null;
                        figures[a].Apply(in body, 0f, 0f);
                        Vector3 actualL = figures[a].Rig.Find("LeftFoot").position;
                        Vector3 actualR = figures[a].Rig.Find("RightFoot").position;
                        float residualL = fit?.LeftResidual ?? 0f;
                        float residualR = fit?.RightResidual ?? 0f;
                        unreachableAnkles += residualL > 1e-5f ? 1 : 0;
                        unreachableAnkles += residualR > 1e-5f ? 1 : 0;
                        maxUnreachableResidual = Mathf.Max(maxUnreachableResidual, residualL, residualR);
                        float errorL = Vector3.Distance(actualL, body.AnkleL);
                        float errorR = Vector3.Distance(actualR, body.AnkleR);
                        maxActualSolveError = Mathf.Max(maxActualSolveError, errorL, errorR);
                        float[] V(Vector3 v) => new[] { v.x - right.x, v.y, v.z };
                        ankleFrames.Add(new
                        {
                            frame = f, role = a == 0 ? "executor" : "opponent",
                            leftTarget = V(targetL), rightTarget = V(targetR),
                            leftReached = V(body.AnkleL), rightReached = V(body.AnkleR),
                            leftActual = V(actualL), rightActual = V(actualR),
                            leftUnreachableResidualMetres = residualL, rightUnreachableResidualMetres = residualR,
                            leftActualToSourceErrorMetres = Vector3.Distance(actualL, targetL),
                            rightActualToSourceErrorMetres = Vector3.Distance(actualR, targetR),
                            leftActualToReachedErrorMetres = errorL, rightActualToReachedErrorMetres = errorR,
                        });
                    }
                    camera.Render();
                    camera.Render();
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    image.Apply();
                    File.WriteAllBytes(Path.Combine(frames, $"{f - from:D06}.png"), image.EncodeToPNG());
                    int index = Array.IndexOf(selected, f);
                    if (index >= 0)
                    {
                        sheet.SetPixels((index % 2) * Width, (((count + 1) / 2) - 1 - index / 2) * Height, Width, Height, image.GetPixels());
                    }
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(output, "contact-sheet.png"), sheet.EncodeToPNG());
                EncodeVideo(frames, output, take.FramesPerSecond, to - from + 1);
                var profiles = new[]
                {
                    new { role = "red/executor", session.Red.Body.HeightCm, session.Red.Body.WingspanCm, legRatio = session.Red.Body.LegRatio.ToFloat() },
                    new { role = "blue/opponent", session.Blue.Body.HeightCm, session.Blue.Body.WingspanCm, legRatio = session.Blue.Body.LegRatio.ToFloat() },
                };
                var manifest = new
                {
                    take = id,
                    fromFrame = from,
                    toFrame = to,
                    executor,
                    redActorIndex = executor,
                    blueActorIndex = 1 - executor,
                    fps = take.FramesPerSecond,
                    frameIntervalSeconds = 1.0 / take.FramesPerSecond,
                    durationBetweenEndpointsSeconds = (to - from) / take.FramesPerSecond,
                    encodedFrameCount = to - from + 1,
                    contactSheetFrames = selected,
                    source = take.Source,
                    actors = take.Actors,
                    generatedBodyProfiles = profiles,
                    globalScale = (looks[0].HeightMetres + looks[1].HeightMetres) / (take.Actors[0].HeightMetres + take.Actors[1].HeightMetres),
                    ankleTrajectoryRetargeting = retargetLegs,
                    unreachableAnkleFrameCount = unreachableAnkles,
                    maximumUnreachableResidualMetres = maxUnreachableResidual,
                    maximumActualToReachedErrorMetres = maxActualSolveError,
                    ankleFrames,
                    notes = "Frames are consecutive original samples. Left uses unmodified MotionPlayback joint positions. Right optionally retargets legs with the actual rig's per-side lengths while retaining the common pair scale and pelvis trajectory. Unreachable ankles are explicitly reported, not preserved. Toe vectors and foot roll are inferred; there are no verified foot-contact labels. The source floor is fitted to ankle samples, not measured shoe soles. Panel offsets are display-only. No physics or production clip assignment.",
                };
                File.WriteAllText(Path.Combine(output, "manifest.json"), JsonConvert.SerializeObject(manifest, Formatting.Indented));
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
                RenderTexture.active = old;
                if (target != null)
                {
                    target.Release();
                }
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(sheet);
                UnityEngine.Object.DestroyImmediate(root);
                foreach (var material in materials)
                {
                    if (material != null)
                    {
                        UnityEngine.Object.DestroyImmediate(material);
                    }
                }

                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        private static void EncodeVideo(string frames, string output, float fps, int count)
        {
            string ffmpeg = new[] { "/Users/bahdan/git/brew/bin/ffmpeg", "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/usr/bin/ffmpeg" }.FirstOrDefault(File.Exists);
            if (ffmpeg == null)
            {
                Debug.LogWarning("ffmpeg unavailable; native-rate PNG frames remain available.");
                return;
            }
            // ArgumentList is not available in every Unity runtime; reject paths that would break quoting.
            if (frames.Contains("\"") || output.Contains("\""))
            {
                throw new ArgumentException("Output path cannot contain a double quote.");
            }
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                FileName = ffmpeg,
                Arguments = "-y -loglevel error -framerate " + fps.ToString("R", CultureInfo.InvariantCulture) + " -i \"" + Path.Combine(frames, "%06d.png") + "\" -frames:v " + count + " -c:v libx264 -pix_fmt yuv420p -crf 20 \"" + Path.Combine(output, "native-rate.mp4") + "\"",
                UseShellExecute = false,
            });
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("ffmpeg failed: " + process.ExitCode);
            }
            process.Dispose();
        }
        private static TextMesh Label(Transform parent, string text, Vector3 position)
        {
            var go = new GameObject("Label " + text);
            go.transform.SetParent(parent);
            go.transform.position = position;
            var label = go.AddComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.018f;
            label.fontSize = 48;
            label.color = Color.white;
            return label;
        }
        private static Vector3[] Points(Pose p) => new[] { p.Pelvis, p.Neck, p.Head, p.ShoulderL, p.ShoulderR, p.ElbowL, p.ElbowR, p.HandL, p.HandR, p.HipL, p.HipR, p.KneeL, p.KneeR, p.AnkleL, p.AnkleR, p.ToeL, p.ToeR };
        private static Pose Offset(Pose p, Vector3 d)
        {
            p.Pelvis += d;
            p.Neck += d;
            p.Head += d;
            p.ShoulderL += d;
            p.ShoulderR += d;
            p.ElbowL += d;
            p.ElbowR += d;
            p.HandL += d;
            p.HandR += d;
            p.HipL += d;
            p.HipR += d;
            p.KneeL += d;
            p.KneeR += d;
            p.AnkleL += d;
            p.AnkleR += d;
            p.ToeL += d;
            p.ToeR += d;
            return p;
        }
    }
}
