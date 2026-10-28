using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Wrestling.Editor
{
    /// <summary>
    /// Import rules for the MakeHuman wrestler models in Assets/Characters: keep blend shapes and the
    /// rig, no imported materials (the library builder makes them), normal maps flagged, and UV2
    /// and UV3 filled with each body vertex's bind position and body regions, which the skin shader
    /// uses to draw the singlet.
    /// </summary>
    public sealed class CharacterImport : AssetPostprocessor
    {
        public const string Root = "Assets/Characters/";
        public const string Models = Root + "Models/";
        public const string Textures = Root + "Textures/";

        /// <summary>Body regions written to UV2.w; the skin shader's singlet rules use the same ids.</summary>
        public enum Region
        {
            Torso = 0,
            ShoulderGirdle = 1,
            Thigh = 2,
            Arm = 3,
            Head = 4,
            LowerLeg = 5,
            UpperArm = 6,
        }

        /// <summary>Bumped whenever the import rules change, so Unity re-imports the character models.</summary>
        public override uint GetVersion() => 7;

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Models))
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            importer.importBlendShapes = true;
            importer.importBlendShapeNormals = ModelImporterNormals.Calculate;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = false;
            importer.bakeAxisConversion = true;
            importer.useFileScale = true;
            importer.optimizeGameObjects = false;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Textures))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            bool normal = file.Contains("normal") || file.EndsWith("_nm") || file.Contains("_norm");
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.alphaIsTransparency = !normal;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(Models))
            {
                return;
            }

            SkinnedMeshRenderer body = null;
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh;
                if (mesh.blendShapeCount > 0 && (body == null || mesh.vertexCount > body.sharedMesh.vertexCount))
                {
                    body = renderer;
                }
            }

            if (body == null)
            {
                Debug.LogError($"{assetPath}: no body mesh with blend shapes found.");
                return;
            }

            WriteBodyCoordinates(root.transform, body);
        }

        /// <summary>
        /// UV2: bind-pose position normalised by height (feet at 0, +z the front). UV3: how much of
        /// the vertex belongs to the torso, shoulder girdle, thighs, and arms (summed skin weights). The skin
        /// shader evaluates the singlet's cut per pixel from these, so its edges are smooth and sharp.
        /// </summary>
        private static void WriteBodyCoordinates(Transform root, SkinnedMeshRenderer body)
        {
            Mesh mesh = body.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Matrix4x4 toRoot = root.worldToLocalMatrix * body.transform.localToWorldMatrix;
            var positions = new Vector3[vertices.Length];
            float bottom = float.MaxValue;
            float top = float.MinValue;
            for (int i = 0; i < vertices.Length; i++)
            {
                positions[i] = toRoot.MultiplyPoint3x4(vertices[i]);
                bottom = Mathf.Min(bottom, positions[i].y);
                top = Mathf.Max(top, positions[i].y);
            }

            var regions = new Region[body.bones.Length];
            float frontSum = 0f;
            for (int i = 0; i < body.bones.Length; i++)
            {
                Transform bone = body.bones[i];
                regions[i] = RegionOf(bone.name);
                if (bone.name.EndsWith("ToeBase") && bone.parent != null)
                {
                    // The toes are in front of the ankles.
                    frontSum += root.InverseTransformPoint(bone.position).z - root.InverseTransformPoint(bone.parent.position).z;
                }
            }

            float front = frontSum >= 0f ? 1f : -1f;
            float height = top - bottom;
            BoneWeight[] weights = mesh.boneWeights;
            var position = new List<Vector4>(vertices.Length);
            var share = new List<Vector4>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = positions[i];
                position.Add(new Vector4(p.x / height, (p.y - bottom) / height, front * p.z / height, 0f));
                BoneWeight w = weights[i];
                var regionShare = Vector4.zero;
                void Add(int bone, float weight)
                {
                    switch (regions[bone])
                    {
                        case Region.Torso:
                            regionShare.x += weight;
                            break;
                        case Region.ShoulderGirdle:
                            regionShare.y += weight;
                            break;
                        case Region.Thigh:
                            regionShare.z += weight;
                            break;
                        case Region.UpperArm:
                        case Region.Arm:
                            regionShare.w += weight;
                            break;
                    }
                }

                Add(w.boneIndex0, w.weight0);
                Add(w.boneIndex1, w.weight1);
                Add(w.boneIndex2, w.weight2);
                Add(w.boneIndex3, w.weight3);
                share.Add(regionShare);
            }

            mesh.SetUVs(2, position);
            mesh.SetUVs(3, share);
        }

        private static Region RegionOf(string bone)
        {
            string name = bone.Contains(":") ? bone.Substring(bone.IndexOf(':') + 1) : bone;
            if (name.EndsWith("Arm") && !name.Contains("Fore"))
            {
                return Region.UpperArm;
            }

            if (name.Contains("Hand") || name.Contains("Arm"))
            {
                return Region.Arm;
            }

            if (name.Contains("UpLeg"))
            {
                return Region.Thigh;
            }

            if (name.Contains("Leg") || name.Contains("Foot") || name.Contains("Toe"))
            {
                return Region.LowerLeg;
            }

            if (name.Contains("Head") || name.Contains("Neck") || name.Contains("Jaw") || name.Contains("Eye") || name.Contains("Orbicularis"))
            {
                return Region.Head;
            }

            if (name.Contains("Shoulder") || name == "Spine2" || name.Contains("Breast"))
            {
                return Region.ShoulderGirdle;
            }

            return Region.Torso;
        }
    }
}
