using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wrestling.Editor
{
    /// <summary>
    /// Per-vertex skin data for the skin shader: surface curvature (how strongly light scatters
    /// round the terminator) and sweat affinity (where sweat shows first as exertion builds:
    /// brow, neck, deltoids, forearms, then the centre of the chest and back).
    /// </summary>
    internal static class SkinMaps
    {
        /// <summary>Mean surface curvature per vertex (1/metre), smoothed over the neighbouring vertices.</summary>
        public static List<float> Curvature(EditableMesh mesh, Matrix4x4 toRoot)
        {
            var rootPositions = new Vector3[mesh.VertexCount];
            for (int i = 0; i < rootPositions.Length; i++)
            {
                rootPositions[i] = toRoot.MultiplyPoint3x4(mesh.Positions[i]);
            }

            var weld = new MeshWeld(rootPositions, mesh.Triangles);
            var normals = new Vector3[weld.PointCount];
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                normals[weld.PointOf[i]] += toRoot.MultiplyVector(mesh.Normals[i]);
            }

            for (int p = 0; p < normals.Length; p++)
            {
                normals[p] = normals[p].normalized;
            }

            var curvature = new float[weld.PointCount];
            for (int p = 0; p < weld.PointCount; p++)
            {
                float sum = 0f;
                int count = 0;
                foreach (int q in weld.Neighbours[p])
                {
                    Vector3 edge = weld.Points[p] - weld.Points[q];
                    float lengthSquared = edge.sqrMagnitude;
                    if (lengthSquared > 1e-12f)
                    {
                        sum += Vector3.Dot(normals[p] - normals[q], edge) / lengthSquared;
                        count++;
                    }
                }

                curvature[p] = count > 0 ? Mathf.Abs(sum / count) : 0f;
            }

            weld.Smooth(curvature, 3);
            var result = new List<float>(mesh.VertexCount);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                result.Add(curvature[weld.PointOf[i]]);
            }

            return result;
        }

        /// <summary>Writes sweat affinity (0–1) into UV2.w.</summary>
        public static void SweatAffinity(Transform root, SkinnedMeshRenderer body, EditableMesh mesh, float bottom, float height)
        {
            Transform[] bones = body.bones;
            var group = new int[bones.Length];
            Transform leftEye = null;
            Transform rightEye = null;
            float front = 1f;
            for (int b = 0; b < bones.Length; b++)
            {
                string name = bones[b] != null ? TwistRig.Strip(bones[b].name) : string.Empty;
                group[b] = name.EndsWith("ForeArm") || name.EndsWith("ForeArmTwist") ? Forearm
                    : name.EndsWith("Arm") || name.EndsWith("ArmTwist") ? UpperArm
                    : name.EndsWith("Shoulder") ? Clavicle
                    : name == "Neck" ? Neck
                    : name.StartsWith("Spine") ? Trunk
                    : None;
            }

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string name = TwistRig.Strip(t.name);
                if (name == "LeftEye")
                {
                    leftEye = t;
                }
                else if (name == "RightEye")
                {
                    rightEye = t;
                }
                else if (name == "LeftToeBase" && t.parent != null)
                {
                    front = root.InverseTransformPoint(t.position).z - root.InverseTransformPoint(t.parent.position).z >= 0f ? 1f : -1f;
                }
            }

            if (leftEye == null || rightEye == null)
            {
                throw new InvalidOperationException("Rig has no eye bones.");
            }

            Vector3 eyes = 0.5f * (root.InverseTransformPoint(leftEye.position) + root.InverseTransformPoint(rightEye.position));
            float eyeY = (eyes.y - bottom) / height;
            float eyeZ = front * eyes.z / height;

            // Upper arm param: 0 at the shoulder joint, 1 at the elbow (mesh space, from the bind poses).
            (Vector3 Start, Vector3 Axis)[] upperArms =
            {
                Segment(bones, mesh, "LeftArm", "LeftForeArm"),
                Segment(bones, mesh, "RightArm", "RightForeArm"),
            };

            List<Vector4> coordinates = mesh.Uvs[2];
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                BoneWeight w = mesh.Weights[i];
                var shares = new float[GroupCount];
                shares[group[w.boneIndex0]] += w.weight0;
                shares[group[w.boneIndex1]] += w.weight1;
                shares[group[w.boneIndex2]] += w.weight2;
                shares[group[w.boneIndex3]] += w.weight3;
                Vector4 c = coordinates[i];
                Vector3 p = mesh.Positions[i];

                float nearShoulder = 1f;
                if (shares[UpperArm] > 0f)
                {
                    (Vector3 start, Vector3 axis) = (p - upperArms[0].Start).sqrMagnitude < (p - upperArms[1].Start).sqrMagnitude ? upperArms[0] : upperArms[1];
                    float t = Vector3.Dot(p - start, axis) / axis.sqrMagnitude;
                    nearShoulder = 1f - Smooth(0.3f, 0.65f, t);
                }

                float brow = Smooth(eyeY + 0.008f, eyeY + 0.022f, c.y) * Smooth(eyeY + 0.075f, eyeY + 0.05f, c.y) * Smooth(eyeZ - 0.035f, eyeZ - 0.015f, c.z);
                float upperLip = Smooth(eyeY - 0.075f, eyeY - 0.06f, c.y) * Smooth(eyeY - 0.035f, eyeY - 0.045f, c.y) * Smooth(eyeZ - 0.005f, eyeZ + 0.01f, c.z) * 0.6f;
                float deltoid = (shares[UpperArm] * nearShoulder) + (0.7f * shares[Clavicle]);
                float forearm = shares[Forearm];
                float neck = shares[Neck];
                float sternum = shares[Trunk] * Mathf.Exp(-Mathf.Pow(c.x / 0.045f, 2f)) * Smooth(0.58f, 0.64f, c.y) * Smooth(0.82f, 0.76f, c.y) * 0.75f;
                float affinity = Mathf.Max(0.18f, Mathf.Max(Mathf.Max(brow, upperLip), Mathf.Max(Mathf.Max(deltoid, forearm), Mathf.Max(neck, sternum))));
                coordinates[i] = new Vector4(c.x, c.y, c.z, Mathf.Clamp01(affinity));
            }
        }

        private const int None = 0;
        private const int Forearm = 1;
        private const int UpperArm = 2;
        private const int Clavicle = 3;
        private const int Neck = 4;
        private const int Trunk = 5;
        private const int GroupCount = 6;

        private static (Vector3 Start, Vector3 Axis) Segment(Transform[] bones, EditableMesh mesh, string boneName, string distalName)
        {
            int bone = Array.FindIndex(bones, b => b != null && TwistRig.Strip(b.name) == boneName);
            int distal = Array.FindIndex(bones, b => b != null && TwistRig.Strip(b.name) == distalName);
            if (bone < 0 || distal < 0)
            {
                throw new InvalidOperationException($"Body mesh is not skinned to '{boneName}' and '{distalName}'.");
            }

            Vector3 start = mesh.Bindposes[bone].inverse.MultiplyPoint3x4(Vector3.zero);
            Vector3 end = mesh.Bindposes[distal].inverse.MultiplyPoint3x4(Vector3.zero);
            return (start, end - start);
        }

        private static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - (2f * t));
        }
    }
}
