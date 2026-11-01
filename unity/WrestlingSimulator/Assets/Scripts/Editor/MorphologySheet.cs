using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wrestling.Core.Numerics;
using Wrestling.Core.Wrestlers;
using Wrestling.Presentation;
using Pose = Wrestling.Presentation.Pose;

namespace Wrestling.Editor
{
    /// <summary>Same-height source, short-limbed and long-limbed skin/rig review in bind and wrestling poses.</summary>
    public static class MorphologySheet
    {
        public static void RenderFromCommandLine()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "-morphologyOutput");
                if (index < 0 || index + 1 >= args.Length)
                {
                    throw new ArgumentException("Pass -morphologyOutput DIR.");
                }

                Render(args[index + 1]);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Render(string folder)
        {
            Directory.CreateDirectory(folder);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterLibrary library = AssetDatabase.LoadAssetAtPath<CharacterLibrary>(CharacterLibraryBuilder.LibraryPath);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.40f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.22f, 0.2f);
            Light light = new GameObject("Key").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.transform.rotation = Quaternion.Euler(38f, 150f, 0f);
            Camera camera = new GameObject("Review camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            camera.orthographic = true;
            camera.orthographicSize = 1.1f;
            camera.nearClipPlane = 0.05f;
            bool asyncShaders = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                foreach ((string label, CharacterLibrary.CharacterBase model, Gender gender) in new[]
                {
                    ("male", library.Male, Gender.Male), ("female", library.Female, Gender.Female),
                })
                {
                    RenderModel(Path.Combine(folder, "morphology-" + label + ".png"), model, gender, camera);
                }

                File.WriteAllText(Path.Combine(folder, "legend.txt"),
                    "Columns: original base skeleton at 1.80 m; 1.80 m / leg ratio 0.43 / span 1.70 m; 1.80 m / leg ratio 0.53 / span 1.90 m. Rows: bind pose, wrestling stance. Original retains base build; changed profiles use identical identity and body mass.\n");
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncShaders;
            }
        }

        private static void RenderModel(string file, CharacterLibrary.CharacterBase model, Gender gender, Camera camera)
        {
            const int Width = 720;
            const int Height = 800;
            var sheet = new Texture2D(Width * 3, Height * 2, TextureFormat.RGB24, false);
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = target;
            try
            {
                for (int column = 0; column < 3; column++)
                {
                    GameObject root = new GameObject("Morphology " + column);
                    Material originalSkin = null;
                    try
                    {
                        CharacterRig rig;
                        WrestlerCharacter figure = null;
                        if (column == 0)
                        {
                            GameObject instance = UnityEngine.Object.Instantiate(model.Model, root.transform);
                            originalSkin = new Material(model.Body);
                            originalSkin.SetTexture("_BaseMap", model.Skins[0].Albedo);
                            originalSkin.SetColor("_SingletColor", new Color(0.78f, 0.06f, 0.08f));
                            SkinnedMeshRenderer main = null;
                            foreach (SkinnedMeshRenderer renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                            {
                                if (renderer.sharedMesh.blendShapeCount > 0 && (main == null || renderer.sharedMesh.vertexCount > main.sharedMesh.vertexCount))
                                {
                                    main = renderer;
                                }
                            }

                            float top = float.MinValue;
                            float bottom = float.MaxValue;
                            foreach (SkinnedMeshRenderer renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                            {
                                renderer.updateWhenOffscreen = true;
                                if (renderer == main)
                                {
                                    var materials = new Material[renderer.sharedMesh.subMeshCount];
                                    for (int m = 0; m < materials.Length; m++)
                                    {
                                        materials[m] = originalSkin;
                                    }

                                    renderer.sharedMaterials = materials;
                                }
                                else
                                {
                                    int part = Array.FindIndex(model.PartNames, name => renderer.name.Contains(name));
                                    if (part >= 0)
                                    {
                                        renderer.sharedMaterial = model.PartMaterials[part];
                                    }
                                }

                                top = Mathf.Max(top, renderer.bounds.max.y);
                                bottom = Mathf.Min(bottom, renderer.bounds.min.y);
                            }

                            root.transform.localScale = Vector3.one * (1.8f / (top - bottom));
                            rig = new CharacterRig(root.transform);
                        }
                        else
                        {
                            var wrestler = new Wrestler
                            {
                                Id = "morphology-review", Gender = gender,
                                Name = new PersonName { Given = "Morphology", Family = "Review" }, Country = "POL",
                                Attributes = new AttributeSet(45),
                                Body = new BodyProfile
                                {
                                    HeightCm = 180, WingspanCm = column == 1 ? 170 : 190,
                                    LegRatio = Fix64.Parse(column == 1 ? "0.43" : "0.53"),
                                    NaturalWeightKg = Fix64.FromInt(75),
                                },
                            };
                            figure = root.AddComponent<WrestlerCharacter>();
                            figure.Build(model, new WrestlerLook(wrestler, model.Skins.Length), new Color(0.78f, 0.06f, 0.08f));
                            rig = figure.Rig;
                            Debug.Log(FormattableString.Invariant($"[morphology] {gender} column {column}: thigh {rig.Body.Thigh:F4}, shin {rig.Body.Shin:F4}, ratio {(rig.Body.Thigh + rig.Body.Shin) / 1.8f:F4}, torso {rig.Body.Torso:F4}, upper arm {rig.Body.UpperArm:F4}, forearm {rig.Body.Forearm:F4}, measured height {rig.Body.Height:F4}"));
                            Debug.Log(FormattableString.Invariant($"[morphology] {gender} column {column}: measured wingspan {figure.Wingspan:F4}, requested {wrestler.Body.WingspanCm / 100f:F4}"));
                            if (Mathf.Abs(figure.Wingspan - (wrestler.Body.WingspanCm / 100f)) > 0.005f)
                            {
                                throw new InvalidOperationException("Morphology fingertip reach does not match athlete data.");
                            }
                            if (Mathf.Abs((rig.Body.Thigh + rig.Body.Shin) / 1.8f - wrestler.Body.LegRatio.ToFloat()) > 0.001f)
                            {
                                throw new InvalidOperationException("Morphology leg length does not match athlete data.");
                            }
                        }

                        for (int row = 0; row < 2; row++)
                        {
                            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                            {
                                renderer.forceMatrixRecalculationPerRender = true;
                            }

                            if (row == 0)
                            {
                                rig.Apply(rig.Bind, 0f, 0f);
                            }
                            else
                            {
                                Vector3 front = Vector3.ProjectOnPlane(rig.Bind.ChestNormal, Vector3.up).normalized;
                                var facing = new Vector2(front.x, front.z);
                                var left = new Vector2(-facing.y, facing.x);
                                Pose pose = PoseSolver.SolveBody(new WrestlerFrame
                                {
                                    Facing = facing, Level = 0.75f, Posture = 0.85f,
                                    Ground = Wrestling.Core.Match.GroundState.Standing,
                                    LeftFoot = left * 0.22f, RightFoot = -left * 0.22f,
                                }, rig.Body, PoseRole.Neutral);
                                Pose other = default;
                                PoseSolver.SolveArms(ref pose, rig.Body, PoseRole.Neutral, new WrestlerFrame
                                {
                                    Facing = facing, Ground = Wrestling.Core.Match.GroundState.Standing,
                                }, in other, Array.Empty<Wrestling.Core.Match.Grip>());
                                rig.Apply(in pose, 0f, 0f);
                            }

                            figure?.UpdateDeformation();
                            Vector3 focus = new Vector3(0f, 0.92f, 0f);
                            Vector3 direction = rig.Bind.ChestNormal;
                            camera.transform.position = focus + (direction * 5f) + (Vector3.right * 0.4f);
                            camera.transform.LookAt(focus);
                            var labelObject = new GameObject("Morphology label");
                            try
                            {
                                labelObject.transform.SetPositionAndRotation(focus + (camera.transform.up * 1.0f), camera.transform.rotation);
                                TextMesh label = labelObject.AddComponent<TextMesh>();
                                label.anchor = TextAnchor.MiddleCenter;
                                label.alignment = TextAlignment.Center;
                                label.fontSize = 36;
                                label.characterSize = 0.018f;
                                label.color = Color.white;
                                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                                label.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
                                string profile = column == 0 ? "Original base / 1.80 m" : column == 1
                                    ? "1.80 m / legs .43 / span 1.70 m" : "1.80 m / legs .53 / span 1.90 m";
                                label.text = profile + "\n" + (row == 0 ? "Bind pose" : "Wrestling stance");
                                label.font.RequestCharactersInTexture(label.text, label.fontSize, label.fontStyle);
                                camera.Render();
                                camera.Render();
                                RenderTexture.active = target;
                                sheet.ReadPixels(new Rect(0, 0, Width, Height), column * Width, (1 - row) * Height);
                            }
                            finally
                            {
                                UnityEngine.Object.DestroyImmediate(labelObject);
                            }
                        }
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(root);
                        if (originalSkin != null)
                        {
                            UnityEngine.Object.DestroyImmediate(originalSkin);
                        }
                    }
                }

                sheet.Apply();
                File.WriteAllBytes(file, sheet.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(sheet);
            }
        }
    }
}
