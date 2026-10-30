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
        public override uint GetVersion() => 15;

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
            bool normal = file.Contains("normal") || file.EndsWith("_nm") || file.EndsWith("_nrm") || file.Contains("_norm");
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

            WriteBodyCoordinates(root.transform, body, out float bottom, out float height);
            if (assetPath.ToLowerInvariant().Contains("female"))
            {
                CompressAthleticChest(root.transform, body, bottom, height);
            }

            AddExpressionBlendShapes(root.transform, body);
            OffsetHairCap(root.transform, body, bottom, height);
        }

        /// <summary>
        /// Measures the female mesh's own midline sternum and lateral ribcage depth at each height slice,
        /// then bridges a smooth convex sports-bra contour across the anterior chest for both the base mesh
        /// and every build blend shape, recalculating normals and tangents so chest lighting is completely smooth.
        /// </summary>
        private static void CompressAthleticChest(Transform root, SkinnedMeshRenderer body, float bottom, float height)
        {
            float front = 1f;
            foreach (Transform bone in body.bones)
            {
                if (bone.name.EndsWith("ToeBase") && bone.parent != null)
                {
                    front = root.InverseTransformPoint(bone.position).z - root.InverseTransformPoint(bone.parent.position).z >= 0f ? 1f : -1f;
                    break;
                }
            }

            Mesh mesh = body.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Vector3[] originalNormals = mesh.normals;
            Matrix4x4 toRoot = root.worldToLocalMatrix * body.transform.localToWorldMatrix;
            Matrix4x4 fromRoot = toRoot.inverse;

            int vertexCount = vertices.Length;
            int shapeCount = mesh.blendShapeCount;
            var shapeNames = new string[shapeCount];
            var shapeWeights = new float[shapeCount];
            var deltaVertices = new Vector3[shapeCount][];
            var deltaNormals = new Vector3[shapeCount][];
            var deltaTangents = new Vector3[shapeCount][];
            for (int s = 0; s < shapeCount; s++)
            {
                shapeNames[s] = mesh.GetBlendShapeName(s);
                shapeWeights[s] = mesh.GetBlendShapeFrameWeight(s, 0);
                deltaVertices[s] = new Vector3[vertexCount];
                deltaNormals[s] = new Vector3[vertexCount];
                deltaTangents[s] = new Vector3[vertexCount];
                mesh.GetBlendShapeFrameVertices(s, 0, deltaVertices[s], deltaNormals[s], deltaTangents[s]);
            }

            var baseRootBefore = new Vector3[vertexCount];
            var baseRootAfter = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                baseRootBefore[i] = toRoot.MultiplyPoint3x4(vertices[i]);
                baseRootAfter[i] = baseRootBefore[i];
            }

            var chestMask = new float[vertexCount];
            CompressChestPositions(baseRootAfter, bottom, height, front, chestMask);
            for (int i = 0; i < vertexCount; i++)
            {
                if (chestMask[i] > 0f)
                {
                    vertices[i] = fromRoot.MultiplyPoint3x4(baseRootAfter[i]);
                }
            }

            var shapeRoot = new Vector3[vertexCount];
            for (int s = 0; s < shapeCount; s++)
            {
                for (int i = 0; i < vertexCount; i++)
                {
                    shapeRoot[i] = baseRootBefore[i] + toRoot.MultiplyVector(deltaVertices[s][i]);
                }

                CompressChestPositions(shapeRoot, bottom, height, front, null);
                for (int i = 0; i < vertexCount; i++)
                {
                    if (chestMask[i] > 0f)
                    {
                        deltaVertices[s][i] = fromRoot.MultiplyVector(shapeRoot[i] - baseRootAfter[i]);
                        deltaNormals[s][i] *= 1f - chestMask[i];
                    }
                }
            }

            Bounds originalBounds = mesh.bounds;
            if (shapeCount > 0)
            {
                mesh.ClearBlendShapes();
            }

            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            Vector3[] recalculatedNormals = mesh.normals;
            if (originalNormals != null && originalNormals.Length == vertexCount)
            {
                for (int i = 0; i < vertexCount; i++)
                {
                    float blend = Mathf.Clamp01(chestMask[i] * 1.25f);
                    if (blend > 0f)
                    {
                        originalNormals[i] = Vector3.Slerp(originalNormals[i], recalculatedNormals[i], blend).normalized;
                    }
                }

                mesh.normals = originalNormals;
            }

            mesh.RecalculateTangents();

            for (int s = 0; s < shapeCount; s++)
            {
                mesh.AddBlendShapeFrame(shapeNames[s], shapeWeights[s], deltaVertices[s], deltaNormals[s], deltaTangents[s]);
            }

            mesh.bounds = originalBounds;
        }

        private static void CompressChestPositions(Vector3[] rootPositions, float bottom, float height, float front, float[] maskOut)
        {
            const int Bins = 20;
            const float MinY = 0.63f;
            const float MaxY = 0.83f;
            var sternumNz = new float[Bins];
            var lateralNz = new float[Bins];
            for (int i = 0; i < rootPositions.Length; i++)
            {
                Vector3 p = rootPositions[i];
                float ny = (p.y - bottom) / height;
                if (ny < MinY || ny > MaxY)
                {
                    continue;
                }

                float nz = (front * p.z) / height;
                if (nz <= 0.01f)
                {
                    continue;
                }

                float nx = Mathf.Abs(p.x) / height;
                int b = Mathf.Clamp(Mathf.FloorToInt(((ny - MinY) / (MaxY - MinY)) * Bins), 0, Bins - 1);
                if (nx < 0.018f)
                {
                    sternumNz[b] = Mathf.Max(sternumNz[b], nz);
                }
                else if (nx >= 0.106f && nx <= 0.128f)
                {
                    lateralNz[b] = Mathf.Max(lateralNz[b], nz);
                }
            }

            FillAndSmoothProfile(sternumNz, 0.085f);
            FillAndSmoothProfile(lateralNz, 0.055f);

            float Sample(float[] profile, float ny)
            {
                float u = Mathf.Clamp01((ny - MinY) / (MaxY - MinY)) * (Bins - 1);
                int b0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, Bins - 1);
                int b1 = Mathf.Min(b0 + 1, Bins - 1);
                return Mathf.Lerp(profile[b0], profile[b1], u - b0);
            }

            for (int i = 0; i < rootPositions.Length; i++)
            {
                Vector3 p = rootPositions[i];
                float nx = Mathf.Abs(p.x) / height;
                float ny = (p.y - bottom) / height;
                float nz = (front * p.z) / height;
                if (ny < 0.645f || ny > 0.805f || nx > 0.122f || nz <= 0.01f)
                {
                    continue;
                }

                float wy = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.645f, 0.680f, ny))
                    * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.805f, 0.768f, ny));
                float wx = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.008f, 0.026f, nx))
                    * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.120f, 0.098f, nx));
                float tipDx = (nx - 0.072f) / 0.024f;
                float tipDy = (ny - 0.720f) / 0.024f;
                float tipMask = Mathf.Exp(-((tipDx * tipDx) + (tipDy * tipDy)));
                float mask = Mathf.Clamp01((wx * wy) + (tipMask * 0.35f));
                if (maskOut != null)
                {
                    maskOut[i] = mask;
                }

                if (mask <= 1e-4f)
                {
                    continue;
                }

                float sNz = Sample(sternumNz, ny);
                float lNz = Sample(lateralNz, ny);
                float t = Mathf.Clamp01(nx / 0.116f);
                float smoothT = t * t * (3f - (2f * t));
                float verticalDome = Mathf.Exp(-Mathf.Pow((ny - 0.732f) / 0.044f, 2f));
                float braNz = Mathf.Lerp(sNz, lNz, smoothT) + (0.005f * Mathf.Sin(t * Mathf.PI) * verticalDome);
                if (nz > braNz)
                {
                    float flatten = Mathf.Clamp01((mask * 0.90f) + (tipMask * 0.10f));
                    nz = Mathf.Lerp(nz, braNz, flatten);
                    if (ny < 0.732f)
                    {
                        p.y += (0.732f - ny) * 0.20f * flatten * height;
                    }

                    p.z = front * nz * height;
                    rootPositions[i] = p;
                }
            }
        }

        private static void FillAndSmoothProfile(float[] values, float fallback)
        {
            float last = fallback;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > 1e-4f)
                {
                    last = values[i];
                }
                else
                {
                    values[i] = last;
                }
            }

            last = values[values.Length - 1];
            for (int i = values.Length - 1; i >= 0; i--)
            {
                if (values[i] <= 1e-4f)
                {
                    values[i] = last;
                }
                else
                {
                    last = values[i];
                }
            }

            var copy = (float[])values.Clone();
            for (int i = 1; i < values.Length - 1; i++)
            {
                values[i] = (0.25f * copy[i - 1]) + (0.50f * copy[i]) + (0.25f * copy[i + 1]);
            }
        }

        /// <summary>
        /// Adds procedural facial expression blend shapes (Expr_BrowFurrow and Expr_Effort) relative to the
        /// eye and jaw bones so wrestlers show competitive focus and physical exertion during bouts.
        /// </summary>
        private static void AddExpressionBlendShapes(Transform root, SkinnedMeshRenderer body)
        {
            Transform leftEyeBone = null;
            Transform rightEyeBone = null;
            float front = 1f;
            foreach (Transform bone in body.bones)
            {
                if (bone.name.EndsWith("LeftEye"))
                {
                    leftEyeBone = bone;
                }
                else if (bone.name.EndsWith("RightEye"))
                {
                    rightEyeBone = bone;
                }
                else if (bone.name.EndsWith("ToeBase") && bone.parent != null)
                {
                    front = root.InverseTransformPoint(bone.position).z - root.InverseTransformPoint(bone.parent.position).z >= 0f ? 1f : -1f;
                }
            }

            if (leftEyeBone == null || rightEyeBone == null)
            {
                return;
            }

            Vector3 leftEye = root.InverseTransformPoint(leftEyeBone.position);
            Vector3 rightEye = root.InverseTransformPoint(rightEyeBone.position);
            Vector3 eyeMid = (leftEye + rightEye) * 0.5f;
            float ipd = Mathf.Max(Vector3.Distance(leftEye, rightEye), 0.045f);

            Mesh mesh = body.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int vertexCount = vertices.Length;
            Matrix4x4 toRoot = root.worldToLocalMatrix * body.transform.localToWorldMatrix;
            Matrix4x4 fromRoot = toRoot.inverse;

            var browDelta = new Vector3[vertexCount];
            var effortDelta = new Vector3[vertexCount];
            var zeroNormals = new Vector3[vertexCount];
            var zeroTangents = new Vector3[vertexCount];

            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 p = toRoot.MultiplyPoint3x4(vertices[i]);
                float dx = (p.x - eyeMid.x) / ipd;
                float ax = Mathf.Abs(dx);
                float dy = (p.y - eyeMid.y) / ipd;
                float dz = front * (p.z - eyeMid.z) / ipd;
                if (dz < -0.15f || ax > 1.15f || dy < -1.10f || dy > 0.90f)
                {
                    continue;
                }

                // Brow furrow: pulls the inner brow ridge (corrugator/glabella) downward and inward.
                float browY = Mathf.Exp(-Mathf.Pow((dy - 0.30f) / 0.22f, 2f));
                float browX = Mathf.Exp(-Mathf.Pow(ax / 0.68f, 2f));
                float browFront = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.05f, 0.25f, dz));
                float browMask = browY * browX * browFront;
                if (browMask > 1e-3f)
                {
                    float innerBias = Mathf.Exp(-Mathf.Pow(ax / 0.38f, 2f));
                    Vector3 dRoot = new Vector3(
                        -Mathf.Sign(dx) * 0.045f * ipd * browMask * Mathf.Clamp01(ax / 0.25f),
                        -0.065f * ipd * browMask * (0.45f + (0.55f * innerBias)),
                        front * 0.015f * ipd * browMask * innerBias);
                    browDelta[i] = fromRoot.MultiplyVector(dRoot);
                }

                // Effort grimace: flares the nostrils, tenses the upper lip and lifts the infraorbital cheek.
                float noseY = Mathf.Exp(-Mathf.Pow((dy + 0.45f) / 0.22f, 2f));
                float noseX = Mathf.Exp(-Mathf.Pow((ax - 0.24f) / 0.18f, 2f));
                float cheekY = Mathf.Exp(-Mathf.Pow((dy + 0.25f) / 0.24f, 2f));
                float cheekX = Mathf.Exp(-Mathf.Pow((ax - 0.48f) / 0.26f, 2f));
                float noseMask = noseY * noseX * browFront;
                float cheekMask = cheekY * cheekX * browFront;
                if (noseMask > 1e-3f || cheekMask > 1e-3f)
                {
                    Vector3 dRoot = new Vector3(
                        Mathf.Sign(dx) * 0.038f * ipd * noseMask,
                        (0.032f * ipd * noseMask) + (0.042f * ipd * cheekMask),
                        front * 0.018f * ipd * cheekMask);
                    effortDelta[i] = fromRoot.MultiplyVector(dRoot);
                }
            }

            mesh.AddBlendShapeFrame("Expr_BrowFurrow", 100f, browDelta, zeroNormals, zeroTangents);
            mesh.AddBlendShapeFrame("Expr_Effort", 100f, effortDelta, zeroNormals, zeroTangents);
        }

        /// <summary>
        /// Pushes the hair cap slightly outward from the cranium centre so head and face blend shapes
        /// (which only exist on the body mesh) never poke bare skin through the hair cards.
        /// </summary>
        private static void OffsetHairCap(Transform root, SkinnedMeshRenderer body, float bottom, float height)
        {
            Vector3 skullCenter = new Vector3(0f, bottom + (0.935f * height), 0f);
            float pushDistance = 0.0048f * height;
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer == body)
                {
                    continue;
                }

                string lower = renderer.name.ToLowerInvariant();
                if (!lower.Contains("short") && !lower.Contains("ponytail") && !lower.Contains("hair"))
                {
                    continue;
                }

                Mesh mesh = renderer.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                Matrix4x4 toRoot = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                Matrix4x4 fromRoot = toRoot.inverse;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 p = toRoot.MultiplyPoint3x4(vertices[i]);
                    float normalizedY = (p.y - bottom) / height;
                    float weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.89f, 0.92f, normalizedY));
                    if (weight <= 0f)
                    {
                        continue;
                    }

                    Vector3 radial = p - skullCenter;
                    if (radial.y < 0f)
                    {
                        radial.y *= 0.35f;
                    }

                    if (radial.sqrMagnitude > 1e-6f)
                    {
                        p += radial.normalized * (pushDistance * weight);
                        vertices[i] = fromRoot.MultiplyPoint3x4(p);
                    }
                }

                Bounds originalBounds = mesh.bounds;
                mesh.vertices = vertices;
                mesh.bounds = originalBounds;
            }
        }

        /// <summary>
        /// UV2: bind-pose position normalised by height (feet at 0, +z the front). UV3: how much of
        /// the vertex belongs to the torso, shoulder girdle, thighs, and arms (summed skin weights). The skin
        /// shader evaluates the singlet's cut per pixel from these, so its edges are smooth and sharp.
        /// </summary>
        private static void WriteBodyCoordinates(Transform root, SkinnedMeshRenderer body, out float bottom, out float height)
        {
            Mesh mesh = body.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Matrix4x4 toRoot = root.worldToLocalMatrix * body.transform.localToWorldMatrix;
            var positions = new Vector3[vertices.Length];
            bottom = float.MaxValue;
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
            height = top - bottom;
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
                        case Region.Arm:
                        case Region.UpperArm:
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
