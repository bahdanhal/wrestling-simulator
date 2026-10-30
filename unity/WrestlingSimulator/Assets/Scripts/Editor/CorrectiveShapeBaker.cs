using System;
using System.Collections.Generic;
using UnityEngine;
using Wrestling.Presentation;

namespace Wrestling.Editor
{
    /// <summary>
    /// Bakes the pose-space corrective shapes of <see cref="CorrectiveShapes"/>. At each key pose
    /// (one limb swung about its joint) every vertex's linear-skinning position is compared with
    /// its volume-preserving (dual-quaternion) position, the joint's blend zone is faired to
    /// smooth the crease, and the difference is carried back to the bind pose through the inverse
    /// of the vertex's blended skinning matrix. With the shape at full weight, linear skinning at
    /// the key pose then lands exactly on the corrected surface.
    /// </summary>
    internal static class CorrectiveShapeBaker
    {
        /// <summary>The largest correction of a single vertex (metres).</summary>
        private const float MaxCorrection = 0.025f;

        private const int FairingPasses = 8;

        /// <summary>Corrections fade out between these distances from the joint, as shares of the limb's length.</summary>
        private const float NearJoint = 0.35f;

        private const float FarFromJoint = 0.6f;

        public static void Build(Transform root, SkinnedMeshRenderer body, EditableMesh mesh, Matrix4x4 toRoot)
        {
            var byName = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                byName[TwistRig.Strip(t.name)] = t;
            }

            Transform Required(string name) => byName.TryGetValue(name, out Transform t) ? t : throw new InvalidOperationException($"Rig has no bone '{name}'.");

            Transform[] bones = body.bones;
            Matrix4x4 fromRoot = toRoot.inverse;

            // Joint positions in model root space, from the bind poses.
            Vector3 Joint(string name)
            {
                Transform bone = Required(name);
                int index = Array.IndexOf(bones, bone);
                if (index >= 0)
                {
                    return toRoot.MultiplyPoint3x4(mesh.Bindposes[index].inverse.MultiplyPoint3x4(Vector3.zero));
                }

                return root.InverseTransformPoint(bone.position);
            }

            Transform toe = Required("LeftToeBase");
            CorrectiveShapes.BodyFrame(
                Joint("Hips"),
                Joint("Neck"),
                Joint("LeftArm"),
                Joint("RightArm"),
                root.InverseTransformPoint(toe.position) - root.InverseTransformPoint(toe.parent.position),
                out Vector3 up,
                out Vector3 left,
                out Vector3 forward);

            var rootPositions = new Vector3[mesh.VertexCount];
            for (int i = 0; i < rootPositions.Length; i++)
            {
                rootPositions[i] = toRoot.MultiplyPoint3x4(mesh.Positions[i]);
            }

            var weld = new MeshWeld(rootPositions, mesh.Triangles);
            Vector3[] baseNormals = weld.PointNormals(weld.Points, mesh.Triangles);

            foreach ((string side, float sign) in new[] { ("Left", 1f), ("Right", -1f) })
            {
                Vector3 outward = left * sign;
                string suffix = sign > 0f ? "_L" : "_R";
                Bake(side + "Arm", side + "ForeArm", CorrectiveShapes.ArmDirection(CorrectiveShapes.RaiseKeyElevation, 0f, up, outward, forward), "PSD_ShoulderRaise" + suffix);
                Bake(side + "Arm", side + "ForeArm", CorrectiveShapes.ArmDirection(CorrectiveShapes.AcrossKeyElevation, CorrectiveShapes.AcrossKeyAzimuth, up, outward, forward), "PSD_ShoulderAcross" + suffix);
                Bake(side + "UpLeg", side + "Leg", CorrectiveShapes.ThighDirection(CorrectiveShapes.HipFlexKey, up, forward), "PSD_HipFlex" + suffix);
            }

            void Bake(string jointName, string distalName, Vector3 keyDirection, string shapeName)
            {
                Transform joint = Required(jointName);
                Vector3 pivot = Joint(jointName);
                Vector3 limb = Joint(distalName) - pivot;
                Vector3 bindDirection = limb.normalized;
                float reach = limb.magnitude;
                Quaternion rotation = Quaternion.FromToRotation(bindDirection, keyDirection);
                if (rotation.w < 0f)
                {
                    rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
                }

                var moving = new bool[bones.Length];
                for (int b = 0; b < bones.Length; b++)
                {
                    moving[b] = bones[b] != null && bones[b].IsChildOf(joint);
                }

                // Share of each welded point's weight that moves with the limb.
                var share = new float[weld.PointCount];
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    BoneWeight w = mesh.Weights[i];
                    share[weld.PointOf[i]] =
                        (moving[w.boneIndex0] ? w.weight0 : 0f) + (moving[w.boneIndex1] ? w.weight1 : 0f) +
                        (moving[w.boneIndex2] ? w.weight2 : 0f) + (moving[w.boneIndex3] ? w.weight3 : 0f);
                }

                var linear = new Vector3[weld.PointCount];
                var target = new Vector3[weld.PointCount];
                var blend = new float[weld.PointCount];
                for (int p = 0; p < weld.PointCount; p++)
                {
                    float w = share[p];
                    Vector3 offset = weld.Points[p] - pivot;
                    linear[p] = pivot + ((1f - w) * offset) + (w * (rotation * offset));
                    target[p] = pivot + (Nlerp(rotation, w) * offset);
                    blend[p] = Mathf.Clamp01(4f * w * (1f - w));
                }

                // Fair the blend zone (Taubin: shrink then inflate, so volume stays).
                weld.Smooth(blend, 2);
                var next = new Vector3[weld.PointCount];
                for (int pass = 0; pass < 2 * FairingPasses; pass++)
                {
                    float factor = pass % 2 == 0 ? 0.5f : -0.53f;
                    for (int p = 0; p < weld.PointCount; p++)
                    {
                        next[p] = target[p];
                        List<int> around = weld.Neighbours[p];
                        if (blend[p] <= 0f || share[p] <= 1e-4f || share[p] >= 1f - 1e-4f || around.Count == 0)
                        {
                            continue;
                        }

                        Vector3 sum = Vector3.zero;
                        foreach (int q in around)
                        {
                            sum += target[q];
                        }

                        next[p] = target[p] + (factor * blend[p] * ((sum / around.Count) - target[p]));
                    }

                    Array.Copy(next, target, next.Length);
                }

                var correctedPoints = new Vector3[weld.PointCount];
                var pointDelta = new Vector3[weld.PointCount];
                for (int p = 0; p < weld.PointCount; p++)
                {
                    float w = share[p];
                    Vector3 delta = Vector3.zero;
                    // Corrections stay round the joint; skin weights that leak far up the trunk are not followed.
                    float near = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(NearJoint * reach, FarFromJoint * reach, (weld.Points[p] - pivot).magnitude));
                    if (w > 1e-4f && w < 1f - 1e-4f && near > 0f)
                    {
                        Matrix4x4 blended = Blend(rotation, w);
                        delta = blended.inverse.MultiplyVector(target[p] - linear[p]);
                        float length = delta.magnitude;
                        if (length > 1e-9f)
                        {
                            // Soft limit, so very large corrections roll off smoothly instead of clipping.
                            delta *= MaxCorrection * (float)System.Math.Tanh(length / MaxCorrection) / length;
                        }

                        delta *= near;
                        if (delta.sqrMagnitude < 1e-12f)
                        {
                            delta = Vector3.zero;
                        }
                    }

                    pointDelta[p] = delta;
                    correctedPoints[p] = weld.Points[p] + delta;
                }

                Vector3[] correctedNormals = weld.PointNormals(correctedPoints, mesh.Triangles);
                var vertices = new Vector3[mesh.VertexCount];
                var normals = new Vector3[mesh.VertexCount];
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    int p = weld.PointOf[i];
                    if (pointDelta[p] == Vector3.zero)
                    {
                        continue;
                    }

                    vertices[i] = fromRoot.MultiplyVector(pointDelta[p]);
                    normals[i] = fromRoot.MultiplyVector(correctedNormals[p] - baseNormals[p]);
                }

                mesh.AddShape(shapeName, vertices, normals);
            }
        }

        /// <summary>Normalised blend from no rotation to <paramref name="rotation"/> (what dual-quaternion skinning does for two coaxial transforms).</summary>
        private static Quaternion Nlerp(Quaternion rotation, float t)
        {
            var q = new Quaternion(rotation.x * t, rotation.y * t, rotation.z * t, (1f - t) + (rotation.w * t));
            float length = Mathf.Sqrt((q.x * q.x) + (q.y * q.y) + (q.z * q.z) + (q.w * q.w));
            return new Quaternion(q.x / length, q.y / length, q.z / length, q.w / length);
        }

        /// <summary>The linear-skinning matrix (1 − t)·I + t·R.</summary>
        private static Matrix4x4 Blend(Quaternion rotation, float t)
        {
            Matrix4x4 r = Matrix4x4.Rotate(rotation);
            Matrix4x4 m = Matrix4x4.identity;
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    m[row, column] = ((row == column ? 1f : 0f) * (1f - t)) + (r[row, column] * t);
                }
            }

            return m;
        }
    }
}
