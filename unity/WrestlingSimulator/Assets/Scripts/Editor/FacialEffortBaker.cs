using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Wrestling.Presentation;

namespace Wrestling.Editor
{
    /// <summary>
    /// Bakes the facial effort shapes of <see cref="FacialEffort"/> into the body mesh at import,
    /// so a reimport rebuilds them without Blender. The face is measured from the model itself:
    /// the eyes from the eye bones (all sizes are shares of the distance between them), the nose
    /// tip as the most forward point on the midline below the eyes, the mouth line where the jaw
    /// bone's skin weight crosses one half on the front of the face, and the mouth corners as the
    /// ends of that line. Each action unit is a smooth displacement field over its muscle's region
    /// (a few millimetres, as in a real face); the field is taken over welded points so seams never
    /// open, and each shape carries its own normals.
    /// </summary>
    internal static class FacialEffortBaker
    {
        /// <summary>The largest displacement of any vertex (shares of the eye spacing, about 6 mm).</summary>
        private const float MaxDisplacement = 0.1f;

        public static void Build(Transform root, SkinnedMeshRenderer body, EditableMesh mesh, Matrix4x4 toRoot)
        {
            Transform[] bones = body.bones;
            Transform leftEyeBone = null;
            Transform rightEyeBone = null;
            int jaw = -1;
            float front = 1f;
            for (int i = 0; i < bones.Length; i++)
            {
                Transform bone = bones[i];
                if (bone == null)
                {
                    continue;
                }

                string name = TwistRig.Strip(bone.name);
                if (name == "LeftEye")
                {
                    leftEyeBone = bone;
                }
                else if (name == "RightEye")
                {
                    rightEyeBone = bone;
                }
                else if (name == "Jaw")
                {
                    jaw = i;
                }
                else if (name == "LeftToeBase" && bone.parent != null)
                {
                    front = root.InverseTransformPoint(bone.position).z - root.InverseTransformPoint(bone.parent.position).z >= 0f ? 1f : -1f;
                }
            }

            if (leftEyeBone == null || rightEyeBone == null || jaw < 0)
            {
                throw new InvalidOperationException("The character rig needs eye and jaw bones for the facial effort shapes.");
            }

            Vector3 leftEye = root.InverseTransformPoint(leftEyeBone.position);
            Vector3 rightEye = root.InverseTransformPoint(rightEyeBone.position);
            Vector3 eyeMid = (leftEye + rightEye) * 0.5f;
            float ipd = Vector3.Distance(leftEye, rightEye);
            float eyeX = Mathf.Abs(leftEye.x - eyeMid.x) / ipd;

            var rootPositions = new Vector3[mesh.VertexCount];
            for (int i = 0; i < rootPositions.Length; i++)
            {
                rootPositions[i] = toRoot.MultiplyPoint3x4(mesh.Positions[i]);
            }

            var weld = new MeshWeld(rootPositions, mesh.Triangles);
            var jawWeight = new float[weld.PointCount];
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                BoneWeight w = mesh.Weights[i];
                jawWeight[weld.PointOf[i]] =
                    (w.boneIndex0 == jaw ? w.weight0 : 0f) + (w.boneIndex1 == jaw ? w.weight1 : 0f) +
                    (w.boneIndex2 == jaw ? w.weight2 : 0f) + (w.boneIndex3 == jaw ? w.weight3 : 0f);
            }

            // Face coordinates of each welded point, in eye spacings from between the eyes: x to the
            // model's left, y up, z out of the face.
            var face = new Vector3[weld.PointCount];
            for (int p = 0; p < weld.PointCount; p++)
            {
                Vector3 d = weld.Points[p] - eyeMid;
                face[p] = new Vector3(d.x / ipd, d.y / ipd, front * d.z / ipd);
            }

            Landmarks marks = Measure(face, jawWeight);
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "Facial effort landmarks (eye spacings from between the eyes, eye spacing {0:F3} m): eyes ±{1:F2}, nose tip y {2:F2} z {3:F2}, mouth y {4:F2} z {5:F2}, corners ±{6:F2}.",
                ipd, eyeX, marks.NoseY, marks.NoseZ, marks.MouthY, marks.MouthZ, marks.Corner));

            Vector3[] baseNormals = weld.PointNormals(weld.Points, mesh.Triangles);
            Matrix4x4 fromRoot = toRoot.inverse;
            void Add(string shape, Func<Vector3, Vector3> field)
            {
                var pointDelta = new Vector3[weld.PointCount];
                var moved = new Vector3[weld.PointCount];
                for (int p = 0; p < weld.PointCount; p++)
                {
                    Vector3 f = face[p];
                    Vector3 delta = Vector3.zero;
                    // Only the front of the head and face, from the brows to the chin.
                    if (f.z > -1.1f && f.y > -2.0f && f.y < 0.9f && Mathf.Abs(f.x) < 1.25f)
                    {
                        delta = Vector3.ClampMagnitude(field(f), MaxDisplacement);
                    }

                    pointDelta[p] = new Vector3(delta.x * ipd, delta.y * ipd, front * delta.z * ipd);
                    moved[p] = weld.Points[p] + pointDelta[p];
                }

                Vector3[] normals = weld.PointNormals(moved, mesh.Triangles);
                var vertices = new Vector3[mesh.VertexCount];
                var vertexNormals = new Vector3[mesh.VertexCount];
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    int p = weld.PointOf[i];
                    if (pointDelta[p].sqrMagnitude < 1e-14f)
                    {
                        continue;
                    }

                    vertices[i] = fromRoot.MultiplyVector(pointDelta[p]);
                    vertexNormals[i] = fromRoot.MultiplyVector(normals[p] - baseNormals[p]);
                }

                mesh.AddShape(shape, vertices, vertexNormals);
            }

            float onFace(Vector3 f, float behind) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(behind - 0.2f, behind + 0.1f, f.z));

            // AU4: the inner brows drawn down and together over the nose.
            Add(FacialEffort.BrowFurrow, f =>
            {
                float ax = Mathf.Abs(f.x);
                float mask = G(f.y - 0.3f, 0.22f) * G(ax, 0.68f) * onFace(f, 0f);
                float inner = G(ax, 0.38f);
                return new Vector3(-Mathf.Sign(f.x) * 0.045f * mask * Mathf.Clamp01(ax / 0.25f), -0.065f * mask * (0.45f + (0.55f * inner)), 0.015f * mask * inner);
            });

            // AU6/7: the lower lids and the cheeks under them pushed up, narrowing the eyes.
            Add(FacialEffort.EyeSquint, f =>
            {
                float rx = Mathf.Abs(f.x) - eyeX;
                float lower = G(rx, 0.3f) * G(f.y + 0.22f, 0.14f) * onFace(f, -0.25f);
                float cheek = G(rx - 0.05f, 0.35f) * G(f.y + 0.42f, 0.16f) * onFace(f, -0.3f);
                float crow = G(rx - 0.42f, 0.12f) * G(f.y + 0.05f, 0.2f);
                return new Vector3(-Mathf.Sign(f.x) * 0.02f * crow, (0.07f * lower) + (0.05f * cheek), (0.015f * lower) + (0.025f * cheek));
            });

            // AU38: the alar wings of the nose spread outward.
            Add(FacialEffort.NostrilFlare, f =>
            {
                float ax = Mathf.Abs(f.x);
                float mask = G(ax - 0.24f, 0.11f) * G(f.y - (marks.NoseY - 0.12f), 0.12f) * G(f.z - (marks.NoseZ - 0.2f), 0.2f);
                return new Vector3(Mathf.Sign(f.x) * 0.045f * mask, 0.012f * mask, 0f);
            });

            // AU31: the masseter bulging over the angle of the clenched jaw, and the chin set up.
            Add(FacialEffort.JawClench, f =>
            {
                float ax = Mathf.Abs(f.x);
                float masseter = G(ax - 0.98f, 0.18f) * G(f.y - (marks.MouthY - 0.2f), 0.35f) * G(f.z - (marks.MouthZ - 1.05f), 0.4f);
                float chin = G(f.x, 0.45f) * G(f.y - (marks.MouthY - 0.55f), 0.25f) * onFace(f, marks.MouthZ - 0.4f);
                return new Vector3(Mathf.Sign(f.x) * 0.04f * masseter, 0.025f * chin, 0f);
            });

            // AU24: the lips pressed together and thinned, rolled in a little.
            Add(FacialEffort.LipPress, f =>
            {
                float lip = f.y - marks.MouthY;
                float mask = Width(f.x, marks.Corner) * G(lip, 0.2f) * onFace(f, marks.MouthZ - 0.35f);
                return new Vector3(0f, -lip * 0.35f * mask, -0.03f * mask * (1f - Mathf.Clamp01(Mathf.Abs(lip) / 0.3f)));
            });

            // AU20 with the platysma: the mouth corners pulled back, out and down, the lips stretched across the teeth.
            Add(FacialEffort.Grimace, f =>
            {
                float lip = f.y - marks.MouthY;
                float corner = G(Mathf.Abs(f.x) - marks.Corner, 0.16f) * G(lip, 0.18f) * onFace(f, marks.MouthZ - 0.5f);
                float stretch = Width(f.x, marks.Corner) * G(lip, 0.2f) * onFace(f, marks.MouthZ - 0.35f) * Mathf.Clamp01(Mathf.Abs(f.x) / Mathf.Max(marks.Corner, 0.1f));
                return new Vector3(Mathf.Sign(f.x) * ((0.07f * corner) + (0.03f * stretch)), (-0.035f * corner) + (0.012f * Mathf.Sign(lip) * stretch), -0.035f * corner);
            });
        }

        private struct Landmarks
        {
            public float NoseY;
            public float NoseZ;
            public float MouthY;
            public float MouthZ;
            public float Corner;
        }

        /// <summary>The nose tip, mouth line and mouth corners measured on the mesh.</summary>
        private static Landmarks Measure(Vector3[] face, float[] jawWeight)
        {
            var marks = new Landmarks { NoseZ = float.MinValue };
            for (int p = 0; p < face.Length; p++)
            {
                Vector3 f = face[p];
                if (Mathf.Abs(f.x) < 0.12f && f.y < -0.2f && f.y > -1.3f && f.z > marks.NoseZ)
                {
                    marks.NoseZ = f.z;
                    marks.NoseY = f.y;
                }
            }

            if (marks.NoseZ == float.MinValue)
            {
                throw new InvalidOperationException("Could not find the nose tip on the body mesh.");
            }

            // The lips' contact line: on the front of the face below the nose, where the jaw takes half the skin.
            float y = 0f;
            float z = 0f;
            float weight = 0f;
            for (int p = 0; p < face.Length; p++)
            {
                Vector3 f = face[p];
                if (Mathf.Abs(f.x) < 0.15f && f.y < marks.NoseY - 0.15f && f.y > marks.NoseY - 1.2f && f.z > marks.NoseZ - 0.6f)
                {
                    float w = G(jawWeight[p] - 0.5f, 0.12f);
                    y += f.y * w;
                    z += f.z * w;
                    weight += w;
                }
            }

            if (weight < 1e-3f)
            {
                throw new InvalidOperationException("The jaw bone takes no skin around the mouth; cannot find the mouth line.");
            }

            marks.MouthY = y / weight;
            marks.MouthZ = z / weight;
            float corner = 0f;
            for (int p = 0; p < face.Length; p++)
            {
                Vector3 f = face[p];
                // On the lips' front only: past the corners the jaw's weight runs on round the jawline.
                if (Mathf.Abs(f.y - marks.MouthY) < 0.08f && f.z > marks.MouthZ - 0.3f && jawWeight[p] > 0.3f && jawWeight[p] < 0.7f && Mathf.Abs(f.x) < 0.6f)
                {
                    corner = Mathf.Max(corner, Mathf.Abs(f.x));
                }
            }

            // An adult mouth is 0.7–1 eye spacing wide.
            marks.Corner = Mathf.Clamp(corner, 0.35f, 0.5f);
            return marks;
        }

        /// <summary>A Gaussian bump of width <paramref name="width"/>.</summary>
        private static float G(float x, float width) => Mathf.Exp(-(x / width) * (x / width));

        /// <summary>1 across the mouth's width, fading out just past its corners.</summary>
        private static float Width(float x, float corner) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(corner * 0.85f, corner * 1.2f, Mathf.Abs(x)));
    }
}
