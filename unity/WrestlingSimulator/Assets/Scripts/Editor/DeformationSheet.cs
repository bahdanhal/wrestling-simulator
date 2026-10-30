using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wrestling.Presentation;

namespace Wrestling.Editor
{
    /// <summary>
    /// Renders each base character in the poses that stress skinning (arms raised, arm across the
    /// chest, deep hip flexion, forearm and upper-arm roll), plain linear skinning on the left and
    /// with the twist bones and corrective shapes on the right, to check the deformation fixes.
    /// Menu Wrestling → Diagnostics → Render Deformation Sheet, or in batch mode (with graphics):
    /// -executeMethod Wrestling.Editor.DeformationSheet.RenderFromCommandLine -deformationOutput DIR
    /// </summary>
    public static class DeformationSheet
    {
        private const int TileWidth = 640;
        private const int TileHeight = 720;

        private enum Stress
        {
            ArmsRaised,
            ArmAcross,
            HipFlexed,
            LimbRoll,
        }

        [MenuItem("Wrestling/Diagnostics/Render Deformation Sheet")]
        public static void RenderFromMenu()
        {
            string folder = EditorUtility.SaveFolderPanel("Deformation sheet folder", "", "");
            if (!string.IsNullOrEmpty(folder))
            {
                Render(folder);
                EditorUtility.RevealInFinder(folder);
            }
        }

        public static void RenderFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-deformationOutput");
            if (index < 0 || index + 1 >= args.Length)
            {
                Debug.LogError("Pass -deformationOutput DIR.");
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
            CharacterLibrary library = AssetDatabase.LoadAssetAtPath<CharacterLibrary>(CharacterLibraryBuilder.LibraryPath)
                ?? throw new FileNotFoundException("Build the character library first.", CharacterLibraryBuilder.LibraryPath);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.40f, 0.38f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.22f, 0.2f);
            var lightObject = new GameObject("Key light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(38f, 150f, 0f);
            var cameraObject = new GameObject("Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            camera.fieldOfView = 26f;
            camera.nearClipPlane = 0.05f;

            bool asyncShaders = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                foreach ((string name, CharacterLibrary.CharacterBase characterBase) in new[] { ("male", library.Male), ("female", library.Female) })
                {
                    RenderCharacter(Path.Combine(folder, $"deformation-{name}.png"), characterBase, camera);
                }
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncShaders;
            }
        }

        private static void RenderCharacter(string file, CharacterLibrary.CharacterBase characterBase, Camera camera)
        {
            Stress[] stresses = (Stress[])Enum.GetValues(typeof(Stress));
            var sheet = new Texture2D(TileWidth * 2, TileHeight * stresses.Length, TextureFormat.RGB24, false);
            var target = new RenderTexture(TileWidth, TileHeight, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = target;
            var materials = new List<Material>();
            try
            {
                for (int row = 0; row < stresses.Length; row++)
                {
                    for (int column = 0; column < 2; column++)
                    {
                        bool corrected = column == 1;
                        GameObject model = UnityEngine.Object.Instantiate(characterBase.Model);
                        try
                        {
                            Dress(model, characterBase, materials, out SkinnedMeshRenderer body);
                            Dictionary<string, Transform> bones = Bones(model);
                            Transform Find(string boneName) => bones.TryGetValue(boneName, out Transform bone) ? bone : null;
                            var twist = new TwistBones(Find);
                            var correctives = new CorrectiveShapes(Find, body);
                            Pose(stresses[row], Find, out Vector3 focus, out Vector3 viewFrom);
                            if (corrected)
                            {
                                twist.Update();
                                correctives.Update();
                            }

                            camera.transform.position = focus + viewFrom;
                            camera.transform.LookAt(focus);
                            camera.Render();
                            camera.Render();
                            RenderTexture.active = target;
                            sheet.ReadPixels(new Rect(0, 0, TileWidth, TileHeight), column * TileWidth, (stresses.Length - 1 - row) * TileHeight);
                        }
                        finally
                        {
                            UnityEngine.Object.DestroyImmediate(model);
                        }
                    }
                }

                sheet.Apply();
                File.WriteAllBytes(file, sheet.EncodeToPNG());
                Debug.Log($"Deformation sheet written to {file}.");
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(sheet);
                foreach (Material material in materials)
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }
            }
        }

        private static void Dress(GameObject model, CharacterLibrary.CharacterBase characterBase, List<Material> materials, out SkinnedMeshRenderer body)
        {
            body = null;
            foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh.blendShapeCount > 0 && (body == null || renderer.sharedMesh.vertexCount > body.sharedMesh.vertexCount))
                {
                    body = renderer;
                }
            }

            if (body == null)
            {
                throw new InvalidOperationException($"{characterBase.Name}: no body mesh.");
            }

            var skin = new Material(characterBase.Body);
            materials.Add(skin);
            CharacterLibrary.SkinSet set = characterBase.Skins[0];
            skin.SetTexture("_BaseMap", set.Albedo);
            skin.SetFloat("_BumpScale", set.Normal != null ? 0.6f : 0f);
            if (set.Normal != null)
            {
                skin.SetTexture("_BumpMap", set.Normal);
            }

            skin.SetColor("_SingletColor", new Color(0.78f, 0.06f, 0.08f));
            foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.updateWhenOffscreen = true;
                if (renderer == body)
                {
                    var bodyMaterials = new Material[renderer.sharedMesh.subMeshCount];
                    for (int i = 0; i < bodyMaterials.Length; i++)
                    {
                        bodyMaterials[i] = skin;
                    }

                    renderer.sharedMaterials = bodyMaterials;
                    continue;
                }

                int part = Array.FindIndex(characterBase.PartNames, n => renderer.name.Contains(n));
                if (part >= 0)
                {
                    renderer.sharedMaterial = characterBase.PartMaterials[part];
                }
            }
        }

        private static Dictionary<string, Transform> Bones(GameObject model)
        {
            var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                bones[TwistRig.Strip(t.name)] = t;
            }

            return bones;
        }

        /// <summary>Poses the rig for a stress test and returns where to look from.</summary>
        private static void Pose(Stress stress, Func<string, Transform> find, out Vector3 focus, out Vector3 viewFrom)
        {
            Transform toe = find("LeftToeBase");
            CorrectiveShapes.BodyFrame(find("Hips").position, find("Neck").position, find("LeftArm").position, find("RightArm").position, toe.position - toe.parent.position, out Vector3 up, out Vector3 left, out Vector3 forward);
            Vector3 chest = find("Spine2").position;
            switch (stress)
            {
                case Stress.ArmsRaised:
                    foreach (float side in new[] { 1f, -1f })
                    {
                        string prefix = side > 0f ? "Left" : "Right";
                        Aim(find(prefix + "Arm"), find(prefix + "ForeArm"), CorrectiveShapes.ArmDirection(150f, 15f, up, left * side, forward));
                    }

                    focus = chest + (up * 0.12f);
                    viewFrom = (forward * 1.1f) + (left * 0.75f) + (up * 0.25f);
                    break;
                case Stress.ArmAcross:
                    foreach (float side in new[] { 1f, -1f })
                    {
                        string prefix = side > 0f ? "Left" : "Right";
                        Aim(find(prefix + "Arm"), find(prefix + "ForeArm"), CorrectiveShapes.ArmDirection(side > 0f ? 90f : 40f, side > 0f ? 140f : 60f, up, left * side, forward));
                        Aim(find(prefix + "ForeArm"), find(prefix + "Hand"), CorrectiveShapes.ArmDirection(90f, 170f, up, left * side, forward));
                    }

                    focus = chest;
                    viewFrom = (forward * 1.2f) - (left * 0.6f) + (up * 0.15f);
                    break;
                case Stress.HipFlexed:
                    foreach (float side in new[] { 1f, -1f })
                    {
                        string prefix = side > 0f ? "Left" : "Right";
                        Aim(find(prefix + "UpLeg"), find(prefix + "Leg"), CorrectiveShapes.ThighDirection(side > 0f ? 105f : 70f, up, forward) + (left * side * 0.15f));
                        Aim(find(prefix + "Leg"), find(prefix + "Foot"), -up);
                    }

                    focus = find("Hips").position - (up * 0.05f);
                    viewFrom = (forward * 0.9f) + (left * 1.0f) + (up * 0.1f);
                    break;
                default:
                    foreach (float side in new[] { 1f, -1f })
                    {
                        string prefix = side > 0f ? "Left" : "Right";
                        Transform arm = find(prefix + "Arm");
                        Transform foreArm = find(prefix + "ForeArm");
                        Transform hand = find(prefix + "Hand");
                        Aim(arm, foreArm, CorrectiveShapes.ArmDirection(80f, 45f, up, left * side, forward));
                        Aim(foreArm, hand, CorrectiveShapes.ArmDirection(100f, 80f, up, left * side, forward));
                        Roll(arm, (foreArm.position - arm.position).normalized, side * 70f);
                        Roll(hand, (hand.position - foreArm.position).normalized, side * 95f);
                    }

                    focus = chest + (forward * 0.25f);
                    viewFrom = (forward * 1.0f) + (left * 0.8f) + (up * 0.35f);
                    break;
            }
        }

        /// <summary>Swings <paramref name="bone"/> so its child lies along <paramref name="direction"/>.</summary>
        private static void Aim(Transform bone, Transform child, Vector3 direction)
        {
            bone.rotation = Quaternion.FromToRotation(child.position - bone.position, direction.normalized) * bone.rotation;
        }

        /// <summary>Rolls <paramref name="bone"/> about a limb axis.</summary>
        private static void Roll(Transform bone, Vector3 axis, float degrees)
        {
            bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }
    }
}
