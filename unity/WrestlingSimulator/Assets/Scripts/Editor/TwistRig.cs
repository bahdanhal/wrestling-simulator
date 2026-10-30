using System;
using System.Collections.Generic;
using UnityEngine;
using Wrestling.Presentation;

namespace Wrestling.Editor
{
    /// <summary>
    /// Adds the twist bones of <see cref="TwistBones.Limbs"/> to an imported rig: a child of each limb
    /// bone halfway along it, with its bind pose, and part of the limb's skin weight moved onto it,
    /// rising linearly along the limb from 0 to <see cref="TwistBones.MaxShare"/>.
    /// </summary>
    internal static class TwistRig
    {
        public static void Build(SkinnedMeshRenderer body, EditableMesh mesh)
        {
            Transform[] bones = body.bones;
            var boneList = new List<Transform>(bones);
            var limbBones = new HashSet<int>();
            foreach ((string boneName, string distalName, bool cancelsRoot) in TwistBones.Limbs)
            {
                int index = boneList.FindIndex(b => b != null && Strip(b.name) == boneName);
                if (index < 0)
                {
                    throw new InvalidOperationException($"Body mesh is not skinned to '{boneName}'.");
                }

                Transform bone = boneList[index];
                Transform distal = FindChild(bone, distalName) ?? throw new InvalidOperationException($"'{boneName}' has no child '{distalName}'.");
                if (FindChild(bone, boneName + TwistBones.Suffix) != null)
                {
                    throw new InvalidOperationException($"'{boneName}' already has a twist bone.");
                }

                var twist = new GameObject(bone.name + TwistBones.Suffix).transform;
                twist.SetParent(bone, false);
                twist.localPosition = distal.localPosition * 0.5f;
                twist.localRotation = Quaternion.identity;
                twist.localScale = Vector3.one;

                Matrix4x4 bindpose = mesh.Bindposes[index];
                mesh.Bindposes.Add(Matrix4x4.TRS(twist.localPosition, Quaternion.identity, Vector3.one).inverse * bindpose);
                int twistIndex = boneList.Count;
                boneList.Add(twist);
                limbBones.Add(index);
                limbBones.Add(twistIndex);

                // The limb as a segment in mesh space, from the bone's joint to the far joint.
                Matrix4x4 boneToMesh = bindpose.inverse;
                Vector3 start = boneToMesh.MultiplyPoint3x4(Vector3.zero);
                Vector3 end = boneToMesh.MultiplyPoint3x4(distal.localPosition);
                Vector3 axis = end - start;
                float lengthSquared = axis.sqrMagnitude;
                var influences = new Dictionary<int, float>(5);
                for (int v = 0; v < mesh.VertexCount; v++)
                {
                    BoneWeight w = mesh.Weights[v];
                    float onBone = WeightOf(w, index);
                    if (onBone <= 0f)
                    {
                        continue;
                    }

                    float t = Mathf.Clamp01(Vector3.Dot(mesh.Positions[v] - start, axis) / lengthSquared);
                    float share = TwistBones.MaxShare * (cancelsRoot ? 1f - t : t);
                    influences.Clear();
                    Add(influences, w.boneIndex0, w.weight0);
                    Add(influences, w.boneIndex1, w.weight1);
                    Add(influences, w.boneIndex2, w.weight2);
                    Add(influences, w.boneIndex3, w.weight3);
                    influences[index] = onBone * (1f - share);
                    influences[twistIndex] = onBone * share;
                    mesh.Weights[v] = EditableMesh.Strongest(influences);
                }
            }

            body.bones = boneList.ToArray();
            SmoothLimbWeights(mesh, limbBones);
        }

        /// <summary>Weight-smoothing passes over the skin round the twisted limbs.</summary>
        private const int SmoothingPasses = 3;

        /// <summary>
        /// Diffuses the skin weights of vertices on the twisted limbs over their neighbours, so the
        /// shoulder and hip blend zones bend smoothly and seams drawn across them stay straight.
        /// </summary>
        private static void SmoothLimbWeights(EditableMesh mesh, HashSet<int> limbBones)
        {
            var weld = new MeshWeld(mesh.Positions, mesh.Triangles);
            var pointWeights = new Dictionary<int, float>[weld.PointCount];
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                int p = weld.PointOf[i];
                if (pointWeights[p] == null)
                {
                    pointWeights[p] = Influences(mesh.Weights[i]);
                }
            }

            var touched = new bool[weld.PointCount];
            for (int p = 0; p < weld.PointCount; p++)
            {
                foreach (int bone in pointWeights[p].Keys)
                {
                    touched[p] |= limbBones.Contains(bone);
                }
            }

            for (int pass = 0; pass < SmoothingPasses; pass++)
            {
                var next = new Dictionary<int, float>[weld.PointCount];
                for (int p = 0; p < weld.PointCount; p++)
                {
                    if (!touched[p] || weld.Neighbours[p].Count == 0)
                    {
                        next[p] = pointWeights[p];
                        continue;
                    }

                    var blended = new Dictionary<int, float>();
                    foreach (KeyValuePair<int, float> influence in pointWeights[p])
                    {
                        blended[influence.Key] = 0.5f * influence.Value;
                    }

                    float share = 0.5f / weld.Neighbours[p].Count;
                    foreach (int q in weld.Neighbours[p])
                    {
                        foreach (KeyValuePair<int, float> influence in pointWeights[q])
                        {
                            blended.TryGetValue(influence.Key, out float existing);
                            blended[influence.Key] = existing + (share * influence.Value);
                        }
                    }

                    next[p] = Influences(EditableMesh.Strongest(blended));
                }

                pointWeights = next;
            }

            for (int i = 0; i < mesh.VertexCount; i++)
            {
                mesh.Weights[i] = EditableMesh.Strongest(pointWeights[weld.PointOf[i]]);
            }
        }

        private static Dictionary<int, float> Influences(BoneWeight w)
        {
            var influences = new Dictionary<int, float>(4);
            Add(influences, w.boneIndex0, w.weight0);
            Add(influences, w.boneIndex1, w.weight1);
            Add(influences, w.boneIndex2, w.weight2);
            Add(influences, w.boneIndex3, w.weight3);
            return influences;
        }

        public static string Strip(string name) => name.Contains(":") ? name.Substring(name.IndexOf(':') + 1) : name;

        private static Transform FindChild(Transform bone, string name)
        {
            foreach (Transform child in bone)
            {
                if (Strip(child.name) == name)
                {
                    return child;
                }
            }

            return null;
        }

        private static float WeightOf(BoneWeight w, int bone) =>
            (w.boneIndex0 == bone ? w.weight0 : 0f) + (w.boneIndex1 == bone ? w.weight1 : 0f) + (w.boneIndex2 == bone ? w.weight2 : 0f) + (w.boneIndex3 == bone ? w.weight3 : 0f);

        private static void Add(Dictionary<int, float> influences, int bone, float weight)
        {
            if (weight > 0f)
            {
                influences.TryGetValue(bone, out float existing);
                influences[bone] = existing + weight;
            }
        }
    }
}
