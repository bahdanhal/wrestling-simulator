using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Presentation
{
    /// <summary>Build-time shape parameters for one wrestler's body.</summary>
    public readonly struct BodyShape
    {
        public BodyShape(Wrestler wrestler)
        {
            float height = wrestler.Body.HeightCm / 100f;
            float bmi = wrestler.Body.NaturalWeightKg.ToFloat() / (height * height);
            float frame = wrestler.Body.Frame switch
            {
                Frame.Light => 0.94f,
                Frame.Heavy => 1.07f,
                _ => 1f,
            };
            Girth = Mathf.Clamp(Mathf.Sqrt(bmi / 22f) * frame, 0.85f, 1.4f);
            Female = wrestler.Gender == Gender.Female;
        }

        /// <summary>Scales limb and torso thickness from build (weight for height and frame).</summary>
        public float Girth { get; }

        public bool Female { get; }
    }

    /// <summary>
    /// A generated, skinned body as plain arrays. Built on any thread; <see cref="CreateMesh"/>
    /// turns it into a Unity mesh on the main thread.
    /// </summary>
    public sealed class BodyMeshData
    {
        internal BodyMeshData(Vector3[] vertices, Vector3[] normals, Color[] masks, Vector4[] head, BoneWeight[] weights, int[] triangles, Matrix4x4[] bindPoses)
        {
            Vertices = vertices;
            Normals = normals;
            Masks = masks;
            Head = head;
            Weights = weights;
            Triangles = triangles;
            BindPoses = bindPoses;
        }

        public Vector3[] Vertices { get; }

        public Vector3[] Normals { get; }

        /// <summary>Singlet, shoe and hair masks (vertex colour).</summary>
        public Color[] Masks { get; }

        /// <summary>Head-space position normalised by height, and head weight (UV1).</summary>
        public Vector4[] Head { get; }

        public BoneWeight[] Weights { get; }

        public int[] Triangles { get; }

        public Matrix4x4[] BindPoses { get; }

        public Mesh CreateMesh()
        {
            var mesh = new Mesh { name = "Wrestler body", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = Vertices;
            mesh.normals = Normals;
            mesh.colors = Masks;
            mesh.SetUVs(1, Head);
            mesh.boneWeights = Weights;
            mesh.triangles = Triangles;
            mesh.bindposes = BindPoses;
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// Generates a wrestler's body as one smooth skinned mesh: anatomical volumes on each bone
    /// blended into a signed distance field, polygonised with surface
    /// nets and skinned to the <see cref="Skeleton"/>. Vertex colours hold smooth singlet, shoe and
    /// hair masks and UV1 holds the head-space position for the face, both read by the
    /// "Wrestling/Wrestler Body" shader. Every body is built from the wrestler's own measurements.
    /// </summary>
    public static class BodyMeshBuilder
    {
        /// <summary>Volumes further than this from a vertex are ignored when weighting it.</summary>
        private const float SkinReach = 0.12f;

        /// <summary>Signed distances in metres map to mask values 0.5 ± distance / this range.</summary>
        private const float MaskRange = 0.08f;

        private static readonly int[,] CubeEdges =
        {
            { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 },
            { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 },
            { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
        };

        private struct Volume
        {
            public int Bone;
            public Matrix4x4 WorldToBone;
            public Vector3 A;
            public Vector3 B;
            public float R0;
            public float R1;
            public float ScaleX;
            public float ScaleZ;

            /// <summary>Smooth-union radius with the rest of the body: large for muscle, small for fingers and facial detail.</summary>
            public float Blend;
            public Bounds Bounds;

            /// <summary>Bounds corners expanded by the blend radius, cached for the sampling hot path.</summary>
            public Vector3 Min;
            public Vector3 Max;

            public float Distance(Vector3 world)
            {
                Vector3 p = WorldToBone.MultiplyPoint3x4(world);
                p.x /= ScaleX;
                p.z /= ScaleZ;
                Vector3 ba = B - A;
                float lengthSquared = ba.sqrMagnitude;
                float h = lengthSquared > 1e-10f ? Mathf.Clamp01(Vector3.Dot(p - A, ba) / lengthSquared) : 0f;
                float radius = Mathf.Lerp(R0, R1, h);
                return ((p - A - (ba * h)).magnitude - radius) * Mathf.Min(1f, Mathf.Min(ScaleX, ScaleZ));
            }
        }

        /// <summary>Generates the body. Thread-safe: uses no Unity objects, so bodies can be built in the background.</summary>
        public static BodyMeshData Build(BodyProportions body, BodyShape shape, float cellSize)
        {
            Pose bind = PoseSolver.BindPose(body);
            var positions = new Vector3[Skeleton.BoneCount];
            var rotations = new Quaternion[Skeleton.BoneCount];
            var lengths = new float[Skeleton.BoneCount];
            Skeleton.Compute(in bind, positions, rotations, lengths);
            var boneToWorld = new Matrix4x4[Skeleton.BoneCount];
            var bindPoses = new Matrix4x4[Skeleton.BoneCount];
            for (int i = 0; i < Skeleton.BoneCount; i++)
            {
                boneToWorld[i] = Matrix4x4.TRS(positions[i], rotations[i], Vector3.one);
                bindPoses[i] = boneToWorld[i].inverse;
            }

            List<Volume> volumes = Volumes(body, shape, lengths, bindPoses);
            Bounds bounds = volumes[0].Bounds;
            foreach (Volume volume in volumes)
            {
                bounds.Encapsulate(volume.Bounds);
            }

            bounds.Expand(cellSize * 4f);
            var field = new Field(volumes);
            return Polygonise(field, bounds, cellSize, body, lengths, bindPoses);
        }

        /// <summary>
        /// Anatomical volumes per bone, in bone space (+Y along the bone, +Z the front of the body),
        /// sized from height and scaled by build. Muscle groups follow surface anatomy: pelvis and
        /// glutes, a ribcage tapering to the waist, pectorals, lats and trapezius, deltoids, biceps,
        /// triceps and forearms, hands with four fingers and a thumb, quadriceps, hamstrings,
        /// adductors, kneecaps and calves, and a skull with forehead, cheekbones, jaw, chin, nose,
        /// lips, ears and cropped hair.
        /// </summary>
        private static List<Volume> Volumes(BodyProportions body, BodyShape shape, float[] lengths, Matrix4x4[] worldToBone)
        {
            float h = body.Height;
            float g = shape.Girth;
            bool female = shape.Female;
            float muscle = female ? 0.9f : 1f;
            float hip = female ? 1.08f : 1f;
            float chestWidth = female ? 1.22f : 1.36f;
            float softBlend = 0.011f * h;
            float muscleBlend = 0.012f * h;
            float detailBlend = 0.005f * h;
            float fingerBlend = 0.0035f * h;
            var volumes = new List<Volume>();

            void Add(Bone bone, Vector3 a, Vector3 b, float r0, float r1, float sx = 1f, float sz = 1f, float blend = -1f)
            {
                var volume = new Volume
                {
                    Bone = (int)bone,
                    WorldToBone = worldToBone[(int)bone],
                    A = a,
                    B = b,
                    R0 = r0,
                    R1 = r1,
                    ScaleX = sx,
                    ScaleZ = sz,
                    Blend = blend > 0f ? blend : softBlend,
                };
                Matrix4x4 toWorld = volume.WorldToBone.inverse;
                float r = Mathf.Max(r0, r1);
                Vector3 min = Vector3.Min(a, b) - new Vector3(r * sx, r, r * sz);
                Vector3 max = Vector3.Max(a, b) + new Vector3(r * sx, r, r * sz);
                var worldBounds = new Bounds(toWorld.MultiplyPoint3x4(min), Vector3.zero);
                for (int corner = 0; corner < 8; corner++)
                {
                    var local = new Vector3((corner & 1) == 0 ? min.x : max.x, (corner & 2) == 0 ? min.y : max.y, (corner & 4) == 0 ? min.z : max.z);
                    worldBounds.Encapsulate(toWorld.MultiplyPoint3x4(local));
                }

                volume.Bounds = worldBounds;
                volume.Min = worldBounds.min - (Vector3.one * volume.Blend);
                volume.Max = worldBounds.max + (Vector3.one * volume.Blend);
                volumes.Add(volume);
            }

            // Points in units of height.
            Vector3 H(float x, float y, float z) => new Vector3(x * h, y * h, z * h);
            float L(Bone bone) => lengths[(int)bone] / h;

            // ---- Torso ----------------------------------------------------------------------
            Add(Bone.Hips, H(0f, -0.045f, -0.004f), H(0f, L(Bone.Hips), 0f), 0.072f * h * g * hip, 0.064f * h * g, 1.32f, 0.82f);
            Add(Bone.Spine, H(0f, 0f, 0f), H(0f, L(Bone.Spine), -0.004f), 0.064f * h * g, 0.078f * h * g, 1.3f, 0.82f);
            Add(Bone.Spine, H(0f, 0.005f, 0.034f), H(0f, L(Bone.Spine) * 0.85f, 0.04f), 0.03f * h * g, 0.03f * h * g, 1.6f, 0.5f, muscleBlend);
            Add(Bone.Chest, H(0f, 0f, 0f), H(0f, L(Bone.Chest) * 0.8f, -0.004f), 0.082f * h * g, 0.086f * h * g, chestWidth, 0.76f);
            Add(Bone.Neck, H(0f, -0.03f, -0.004f), H(0f, L(Bone.Neck), -0.006f), 0.037f * h * g * muscle, 0.03f * h * g, 1.12f, 1f, muscleBlend);
            foreach (float side in new[] { -1f, 1f })
            {
                // Glutes, lats, pectorals (or bust), trapezius.
                Add(Bone.Hips, H(side * 0.036f, -0.032f, -0.036f), H(side * 0.034f, 0.002f, -0.04f), 0.038f * h * g * hip, 0.034f * h * g, 1f, 1f, muscleBlend);
                Add(Bone.Spine, H(side * 0.066f, L(Bone.Spine), -0.014f), H(side * 0.05f, 0.03f, -0.01f), 0.03f * h * g * muscle, 0.02f * h * g, 1f, 1f, muscleBlend);
                if (female)
                {
                    Add(Bone.Chest, H(side * 0.04f, -0.01f, 0.044f), H(side * 0.04f, -0.004f, 0.046f), 0.026f * h * g, 0.024f * h * g, 1.1f, 0.85f, muscleBlend);
                }
                else
                {
                    Add(Bone.Chest, H(side * 0.04f, -0.006f, 0.04f), H(side * 0.052f, 0.02f, 0.034f), 0.031f * h * g, 0.027f * h * g, 1.25f, 0.62f, muscleBlend);
                }

                Add(Bone.Chest, H(side * 0.028f, L(Bone.Chest) * 0.95f, -0.012f), H(side * 0.085f, L(Bone.Chest) * 0.78f, -0.006f), 0.032f * h * g * muscle, 0.026f * h * g, 1f, 1f, muscleBlend);
            }

            // ---- Head -----------------------------------------------------------------------
            Add(Bone.Head, H(0f, -0.005f, -0.01f), H(0f, 0.025f, -0.012f), 0.056f * h, 0.056f * h, 0.93f, 1.08f);
            Add(Bone.Head, H(0f, 0.012f, -0.012f), H(0f, 0.03f, -0.016f), 0.0585f * h, 0.0585f * h, 0.95f, 1.08f, detailBlend);
            Add(Bone.Head, H(0f, 0.022f, 0.03f), H(0f, 0f, 0.045f), 0.035f * h, 0.035f * h, 1.2f, 1f, detailBlend);
            Add(Bone.Head, H(0f, -0.005f, 0.035f), H(0f, -0.035f, 0.04f), 0.034f * h, 0.03f * h, 1.2f, 0.9f);
            Add(Bone.Head, H(0f, -0.03f, 0f), H(0f, -0.052f, 0.03f), 0.033f * h, 0.022f * h, 1.25f, 1f, detailBlend);
            Add(Bone.Head, H(0f, -0.058f, 0.04f), H(0f, -0.058f, 0.04f), 0.014f * h, 0.014f * h, 1.2f, 1f, detailBlend);
            Add(Bone.Head, H(0f, 0.01f, 0.058f), H(0f, -0.016f, 0.073f), 0.006f * h, 0.01f * h, 1f, 1f, detailBlend);
            Add(Bone.Head, H(-0.012f, -0.028f, 0.058f), H(0.012f, -0.028f, 0.058f), 0.0055f * h, 0.0055f * h, 1f, 1f, detailBlend);
            Add(Bone.Head, H(-0.011f, -0.034f, 0.056f), H(0.011f, -0.034f, 0.056f), 0.006f * h, 0.006f * h, 1f, 1f, detailBlend);
            foreach (float side in new[] { -1f, 1f })
            {
                Add(Bone.Head, H(side * 0.034f, -0.004f, 0.04f), H(side * 0.034f, -0.004f, 0.04f), 0.014f * h, 0.014f * h, 1f, 1f, detailBlend);
                Add(Bone.Head, H(side * 0.012f, 0.016f, 0.054f), H(side * 0.03f, 0.016f, 0.05f), 0.009f * h, 0.009f * h, 1f, 1f, detailBlend);
                Add(Bone.Head, H(side * 0.009f, -0.018f, 0.064f), H(side * 0.009f, -0.018f, 0.064f), 0.007f * h, 0.007f * h, 1f, 1f, detailBlend);
                Add(Bone.Head, H(side * 0.054f, 0.002f, -0.005f), H(side * 0.054f, -0.016f, -0.008f), 0.016f * h, 0.012f * h, 0.45f, 1f, detailBlend);
                Add(Bone.Head, H(side * 0.056f, 0f, -0.004f), H(side * 0.056f, 0f, -0.004f), 0.009f * h, 0.009f * h, 1f, 1f, detailBlend);
            }

            // ---- Arms (lateral is +X on the left arm, -X on the right) -------------------------
            foreach ((Bone upper, Bone lower, Bone hand, float lateral) in new[] { (Bone.LeftUpperArm, Bone.LeftLowerArm, Bone.LeftHand, 1f), (Bone.RightUpperArm, Bone.RightLowerArm, Bone.RightHand, -1f) })
            {
                float upperLength = L(upper);
                float lowerLength = L(lower);
                Add(upper, H(0f, 0f, 0f), H(0f, upperLength, 0f), 0.035f * h * g, 0.028f * h * g);
                Add(upper, H(lateral * 0.006f, -0.012f, 0f), H(lateral * 0.01f, 0.06f, 0f), 0.046f * h * g * muscle, 0.034f * h * g, 1f, 1f, muscleBlend);
                Add(upper, H(0f, 0.05f, 0.012f), H(0f, upperLength * 0.85f, 0.014f), 0.032f * h * g * muscle, 0.026f * h * g, 1f, 0.9f, muscleBlend);
                Add(upper, H(0f, 0.03f, -0.012f), H(0f, upperLength * 0.75f, -0.014f), 0.034f * h * g * muscle, 0.026f * h * g, 1f, 1f, muscleBlend);
                Add(lower, H(0f, 0f, 0f), H(0f, lowerLength, 0f), 0.03f * h * g, 0.02f * h, 1.15f, 0.85f);
                Add(lower, H(0f, 0.01f, 0f), H(0f, lowerLength * 0.55f, 0f), 0.034f * h * g * muscle, 0.026f * h * g, 1.15f, 0.9f, muscleBlend);

                Add(hand, H(0f, 0f, 0f), H(0f, 0.05f, 0.002f), 0.021f * h, 0.02f * h, 1.35f, 0.52f, detailBlend);
                float[] fingerLengths = { 0.043f, 0.047f, 0.044f, 0.036f };
                for (int f = 0; f < 4; f++)
                {
                    // From the thumb side (medial) outwards: index, middle, ring, little; slightly curled forward.
                    float x = -lateral * (0.0165f - (f * 0.011f));
                    float length = fingerLengths[f];
                    Add(hand, H(x, 0.046f, 0.003f), H(x * 1.04f, 0.046f + length, 0.012f), 0.0062f * h, 0.005f * h, 1f, 1f, fingerBlend);
                }

                Add(hand, H(-lateral * 0.022f, 0.012f, 0.01f), H(-lateral * 0.03f, 0.045f, 0.02f), 0.0075f * h, 0.006f * h, 1f, 1f, fingerBlend);
            }

            // ---- Legs (lateral is +X on the left leg, -X on the right) -------------------------
            foreach ((Bone upper, Bone lower, Bone foot, float lateral) in new[] { (Bone.LeftUpperLeg, Bone.LeftLowerLeg, Bone.LeftFoot, 1f), (Bone.RightUpperLeg, Bone.RightLowerLeg, Bone.RightFoot, -1f) })
            {
                float thigh = L(upper);
                float shin = L(lower);
                Add(upper, H(0f, 0f, 0f), H(0f, thigh, 0f), 0.062f * h * g, 0.04f * h * g);
                Add(upper, H(0f, 0.03f, 0.022f), H(0f, thigh * 0.82f, 0.018f), 0.045f * h * g * muscle, 0.035f * h * g, 1f, 1f, muscleBlend);
                Add(upper, H(-lateral * 0.018f, thigh * 0.78f, 0.02f), H(-lateral * 0.018f, thigh * 0.78f, 0.02f), 0.024f * h * g, 0.024f * h * g, 1f, 1f, muscleBlend);
                Add(upper, H(0f, 0.05f, -0.02f), H(0f, thigh * 0.82f, -0.018f), 0.042f * h * g * muscle, 0.032f * h * g, 1f, 1f, muscleBlend);
                Add(upper, H(-lateral * 0.02f, 0.02f, 0f), H(-lateral * 0.012f, thigh * 0.5f, 0f), 0.04f * h * g, 0.03f * h * g, 1f, 1f, muscleBlend);
                Add(upper, H(0f, thigh * 0.98f, 0.028f), H(0f, thigh * 0.98f, 0.028f), 0.016f * h, 0.016f * h, 1f, 1f, detailBlend);
                Add(lower, H(0f, 0f, 0f), H(0f, shin, 0f), 0.036f * h * g, 0.024f * h);
                Add(lower, H(0f, 0.04f, -0.018f), H(0f, shin * 0.55f, -0.016f), 0.042f * h * g * muscle, 0.032f * h * g, 1f, 1f, muscleBlend);
                Add(lower, H(-lateral * 0.008f, 0.06f, -0.015f), H(-lateral * 0.008f, 0.06f, -0.015f), 0.035f * h * g, 0.035f * h * g, 1f, 1f, muscleBlend);
                Add(foot, H(0f, -0.035f, -0.022f), H(0f, L(foot) * 0.95f, -0.025f), 0.028f * h, 0.024f * h, 1.25f, 0.7f);
            }

            return volumes;
        }

        /// <summary>Signed distance to the body: smooth union of the volumes near the point.</summary>
        private sealed class Field
        {
            private readonly List<Volume> _volumes;

            public Field(List<Volume> volumes)
            {
                _volumes = volumes;
            }

            public IReadOnlyList<Volume> Volumes => _volumes;

            public float Sample(Vector3 point)
            {
                float distance = 1f;
                for (int i = 0; i < _volumes.Count; i++)
                {
                    Volume volume = _volumes[i];
                    if (point.x < volume.Min.x || point.x > volume.Max.x
                        || point.y < volume.Min.y || point.y > volume.Max.y
                        || point.z < volume.Min.z || point.z > volume.Max.z)
                    {
                        continue;
                    }

                    distance = SmoothMin(distance, volume.Distance(point), volume.Blend);
                }

                return distance;
            }

            public Vector3 Normal(Vector3 point, float step)
            {
                var normal = new Vector3(
                    Sample(point + new Vector3(step, 0f, 0f)) - Sample(point - new Vector3(step, 0f, 0f)),
                    Sample(point + new Vector3(0f, step, 0f)) - Sample(point - new Vector3(0f, step, 0f)),
                    Sample(point + new Vector3(0f, 0f, step)) - Sample(point - new Vector3(0f, 0f, step)));
                return normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.up;
            }

            private static float SmoothMin(float a, float b, float k)
            {
                float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
                return Mathf.Min(a, b) - (h * h * k * 0.25f);
            }
        }

        /// <summary>
        /// Fills the fine grid: exact distances only within a band around the surface, found from a
        /// grid four times coarser; elsewhere only the sign matters, so coarse values are interpolated.
        /// </summary>
        private static void SampleNearSurface(Field field, Vector3 origin, float cell, int nx, int ny, int nz, float[] values)
        {
            const int factor = 4;
            float coarseCell = cell * factor;
            int mx = ((nx - 1) / factor) + 2;
            int my = ((ny - 1) / factor) + 2;
            int mz = ((nz - 1) / factor) + 2;
            var coarse = new float[mx * my * mz];
            int CoarseIndex(int x, int y, int z) => x + (mx * (y + (my * z)));
            Parallel.For(0, mz, z =>
            {
                for (int y = 0; y < my; y++)
                {
                    for (int x = 0; x < mx; x++)
                    {
                        coarse[CoarseIndex(x, y, z)] = field.Sample(origin + (new Vector3(x, y, z) * coarseCell));
                    }
                }
            });

            // A distance field changes by at most the distance travelled, so a coarse cell whose
            // corners are all further than its diagonal from the surface cannot contain it.
            float band = coarseCell * 1.8f;
            Parallel.For(0, nz, z =>
            {
                int cz = z / factor;
                float tz = (float)(z % factor) / factor;
                for (int y = 0; y < ny; y++)
                {
                    int cy = y / factor;
                    float ty = (float)(y % factor) / factor;
                    for (int x = 0; x < nx; x++)
                    {
                        int cx = x / factor;
                        float tx = (float)(x % factor) / factor;
                        float c000 = coarse[CoarseIndex(cx, cy, cz)];
                        float c100 = coarse[CoarseIndex(cx + 1, cy, cz)];
                        float c010 = coarse[CoarseIndex(cx, cy + 1, cz)];
                        float c110 = coarse[CoarseIndex(cx + 1, cy + 1, cz)];
                        float c001 = coarse[CoarseIndex(cx, cy, cz + 1)];
                        float c101 = coarse[CoarseIndex(cx + 1, cy, cz + 1)];
                        float c011 = coarse[CoarseIndex(cx, cy + 1, cz + 1)];
                        float c111 = coarse[CoarseIndex(cx + 1, cy + 1, cz + 1)];
                        float nearest = Mathf.Min(
                            Mathf.Min(Mathf.Min(Mathf.Abs(c000), Mathf.Abs(c100)), Mathf.Min(Mathf.Abs(c010), Mathf.Abs(c110))),
                            Mathf.Min(Mathf.Min(Mathf.Abs(c001), Mathf.Abs(c101)), Mathf.Min(Mathf.Abs(c011), Mathf.Abs(c111))));
                        int index = x + (nx * (y + (ny * z)));
                        if (nearest < band)
                        {
                            values[index] = field.Sample(origin + (new Vector3(x, y, z) * cell));
                        }
                        else
                        {
                            float x00 = Mathf.Lerp(c000, c100, tx);
                            float x10 = Mathf.Lerp(c010, c110, tx);
                            float x01 = Mathf.Lerp(c001, c101, tx);
                            float x11 = Mathf.Lerp(c011, c111, tx);
                            values[index] = Mathf.Lerp(Mathf.Lerp(x00, x10, ty), Mathf.Lerp(x01, x11, ty), tz);
                        }
                    }
                }
            });
        }

        private static BodyMeshData Polygonise(Field field, Bounds bounds, float cell, BodyProportions body, float[] lengths, Matrix4x4[] worldToBone)
        {
            int nx = Mathf.CeilToInt(bounds.size.x / cell) + 1;
            int ny = Mathf.CeilToInt(bounds.size.y / cell) + 1;
            int nz = Mathf.CeilToInt(bounds.size.z / cell) + 1;
            Vector3 origin = bounds.min;
            var values = new float[nx * ny * nz];
            int Index(int x, int y, int z) => x + (nx * (y + (ny * z)));
            SampleNearSurface(field, origin, cell, nx, ny, nz, values);

            int cx = nx - 1;
            int cy = ny - 1;
            var cellVertex = new int[cx * cy * (nz - 1)];
            var vertices = new List<Vector3>();
            var corner = new float[8];
            for (int z = 0; z < nz - 1; z++)
            {
                for (int y = 0; y < cy; y++)
                {
                    for (int x = 0; x < cx; x++)
                    {
                        int inside = 0;
                        for (int c = 0; c < 8; c++)
                        {
                            corner[c] = values[Index(x + (c & 1), y + ((c >> 1) & 1), z + ((c >> 2) & 1))];
                            if (corner[c] < 0f)
                            {
                                inside++;
                            }
                        }

                        int cellIndex = x + (cx * (y + (cy * z)));
                        if (inside == 0 || inside == 8)
                        {
                            cellVertex[cellIndex] = -1;
                            continue;
                        }

                        Vector3 sum = Vector3.zero;
                        int crossings = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            int a = CubeEdges[e, 0];
                            int b = CubeEdges[e, 1];
                            if ((corner[a] < 0f) == (corner[b] < 0f))
                            {
                                continue;
                            }

                            float t = corner[a] / (corner[a] - corner[b]);
                            var pa = new Vector3(a & 1, (a >> 1) & 1, (a >> 2) & 1);
                            var pb = new Vector3(b & 1, (b >> 1) & 1, (b >> 2) & 1);
                            sum += Vector3.Lerp(pa, pb, t);
                            crossings++;
                        }

                        cellVertex[cellIndex] = vertices.Count;
                        vertices.Add(origin + ((new Vector3(x, y, z) + (sum / crossings)) * cell));
                    }
                }
            }

            var normals = new Vector3[vertices.Count];
            var weights = new BoneWeight[vertices.Count];
            var masks = new Color[vertices.Count];
            var head = new Vector4[vertices.Count];
            Parallel.For(0, vertices.Count, i =>
            {
                Vector3 vertex = vertices[i];
                normals[i] = field.Normal(vertex, cell * 0.5f);
                (BoneWeight weight, List<(int Bone, float Weight)> influences) = Skin(field, vertex);
                weights[i] = weight;
                masks[i] = Masks(vertex, influences, body, lengths, worldToBone);
                float headWeight = 0f;
                foreach ((int bone, float w) in influences)
                {
                    if (bone == (int)Bone.Head)
                    {
                        headWeight = w;
                    }
                }

                Vector3 local = worldToBone[(int)Bone.Head].MultiplyPoint3x4(vertex) / body.Height;
                head[i] = new Vector4(local.x, local.y, local.z, headWeight);
            });

            var triangles = new List<int>();
            int CellAt(int x, int y, int z) => (x < 0 || y < 0 || z < 0 || x >= cx || y >= cy || z >= nz - 1) ? -1 : cellVertex[x + (cx * (y + (cy * z)))];

            void Quad(int a, int b, int c, int d)
            {
                if (a < 0 || b < 0 || c < 0 || d < 0)
                {
                    return;
                }

                Vector3 faceNormal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                Vector3 average = normals[a] + normals[b] + normals[c] + normals[d];
                if (Vector3.Dot(faceNormal, average) < 0f)
                {
                    (b, d) = (d, b);
                }

                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(d);
            }

            for (int z = 0; z < nz; z++)
            {
                for (int y = 0; y < ny; y++)
                {
                    for (int x = 0; x < nx; x++)
                    {
                        bool here = values[Index(x, y, z)] < 0f;
                        if (x + 1 < nx && here != (values[Index(x + 1, y, z)] < 0f))
                        {
                            Quad(CellAt(x, y - 1, z - 1), CellAt(x, y, z - 1), CellAt(x, y, z), CellAt(x, y - 1, z));
                        }

                        if (y + 1 < ny && here != (values[Index(x, y + 1, z)] < 0f))
                        {
                            Quad(CellAt(x - 1, y, z - 1), CellAt(x, y, z - 1), CellAt(x, y, z), CellAt(x - 1, y, z));
                        }

                        if (z + 1 < nz && here != (values[Index(x, y, z + 1)] < 0f))
                        {
                            Quad(CellAt(x - 1, y - 1, z), CellAt(x, y - 1, z), CellAt(x, y, z), CellAt(x - 1, y, z));
                        }
                    }
                }
            }

            return new BodyMeshData(vertices.ToArray(), normals, masks, head, weights, triangles.ToArray(), worldToBone);
        }

        /// <summary>Up to four bone weights from distance to each bone's volumes (softened so joints bend smoothly).</summary>
        private static (BoneWeight Weight, List<(int Bone, float Weight)> Influences) Skin(Field field, Vector3 point)
        {
            var best = new float[Skeleton.BoneCount];
            for (int i = 0; i < best.Length; i++)
            {
                best[i] = float.MaxValue;
            }

            foreach (Volume volume in field.Volumes)
            {
                // Volumes far beyond the weight falloff cannot influence this vertex.
                if (volume.Bounds.SqrDistance(point) > SkinReach * SkinReach)
                {
                    continue;
                }

                float distance = volume.Distance(point);
                if (distance < best[volume.Bone])
                {
                    best[volume.Bone] = distance;
                }
            }

            int closest = 0;
            for (int i = 1; i < best.Length; i++)
            {
                if (best[i] < best[closest])
                {
                    closest = i;
                }
            }

            const float falloff = 0.022f;
            var chosen = new List<(int Bone, float Weight)>();
            for (int i = 0; i < best.Length; i++)
            {
                if (best[i] - best[closest] < falloff * 4f)
                {
                    chosen.Add((i, Mathf.Exp(-(best[i] - best[closest]) / falloff)));
                }
            }

            chosen.Sort((x, y) => y.Weight.CompareTo(x.Weight));
            if (chosen.Count > 4)
            {
                chosen.RemoveRange(4, chosen.Count - 4);
            }

            float total = 0f;
            foreach ((int _, float w) in chosen)
            {
                total += w;
            }

            var weight = new BoneWeight();
            for (int i = 0; i < chosen.Count; i++)
            {
                chosen[i] = (chosen[i].Bone, chosen[i].Weight / total);
                switch (i)
                {
                    case 0:
                        weight.boneIndex0 = chosen[i].Bone;
                        weight.weight0 = chosen[i].Weight;
                        break;
                    case 1:
                        weight.boneIndex1 = chosen[i].Bone;
                        weight.weight1 = chosen[i].Weight;
                        break;
                    case 2:
                        weight.boneIndex2 = chosen[i].Bone;
                        weight.weight2 = chosen[i].Weight;
                        break;
                    default:
                        weight.boneIndex3 = chosen[i].Bone;
                        weight.weight3 = chosen[i].Weight;
                        break;
                }
            }

            return (weight, chosen);
        }

        /// <summary>
        /// Singlet, shoe and hair masks: each bone's signed distance to the region border (metres,
        /// positive inside), blended by skin weight so borders run smoothly across joints.
        /// </summary>
        private static Color Masks(Vector3 point, List<(int Bone, float Weight)> influences, BodyProportions body, float[] lengths, Matrix4x4[] worldToBone)
        {
            float singlet = 0f;
            float shoe = 0f;
            float hair = 0f;
            foreach ((int bone, float weight) in influences)
            {
                Vector3 local = worldToBone[bone].MultiplyPoint3x4(point);
                (float s, float f, float h) = RegionDistances((Bone)bone, local, lengths[bone], body);
                if ((Bone)bone == Bone.Neck || (Bone)bone == Bone.LeftUpperArm || (Bone)bone == Bone.RightUpperArm)
                {
                    // Straps continue over the top of the shoulders, where neck and upper-arm skinning takes over.
                    int chest = (int)Bone.Chest;
                    s = ShoulderStrap(worldToBone[chest].MultiplyPoint3x4(point), lengths[chest], body);
                }
                singlet += s * weight;
                shoe += f * weight;
                hair += h * weight;
            }

            return new Color(Encode(singlet), Encode(shoe), Encode(hair), 1f);
        }

        private static float Encode(float distance) => Mathf.Clamp01(0.5f + (distance / MaskRange));

        /// <summary>
        /// Singlet over the chest in chest-bone space: the body up to a line that rises towards the
        /// centre, plus a strap band over each shoulder (beside the neck, inside the arm).
        /// </summary>
        private static float ChestSinglet(Vector3 local, float length, BodyProportions body)
        {
            float halfWidth = 0.084f * body.Height * 1.3f;
            float x = Mathf.Abs(local.x);
            float bodyLine = (0.72f * length) + (0.08f * length * (1f - Mathf.Clamp01(x / halfWidth))) - local.y;
            return SmoothMax(bodyLine, StrapBand(x, halfWidth), 0.03f * body.Height);
        }

        /// <summary>The strap band only, for neck and upper-arm skin over the shoulders (chest-bone space).</summary>
        private static float ShoulderStrap(Vector3 local, float length, BodyProportions body)
        {
            float halfWidth = 0.084f * body.Height * 1.3f;
            return StrapBand(Mathf.Abs(local.x), halfWidth);
        }

        private static float SmoothMax(float a, float b, float k)
        {
            float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
            return Mathf.Max(a, b) + (h * h * k * 0.25f);
        }

        private static float StrapBand(float x, float halfWidth) => Mathf.Min(x - (0.28f * halfWidth), (0.6f * halfWidth) - x);

        private static (float Singlet, float Shoe, float Hair) RegionDistances(Bone bone, Vector3 local, float length, BodyProportions body)
        {
            const float inside = MaskRange;
            const float outside = -MaskRange;
            float h = body.Height;
            switch (bone)
            {
                case Bone.Hips:
                case Bone.Spine:
                    return (inside, outside, outside);
                case Bone.Chest:
                    return (ChestSinglet(local, length, body), outside, outside);

                case Bone.LeftUpperLeg:
                case Bone.RightUpperLeg:
                    return ((0.42f * length) - local.y, outside, outside);
                case Bone.LeftLowerLeg:
                case Bone.RightLowerLeg:
                    return (outside, local.y - (0.68f * length), outside);
                case Bone.LeftFoot:
                case Bone.RightFoot:
                    return (outside, inside, outside);
                case Bone.Head:
                {
                    float radiusY = 0.058f * h;
                    float radiusZ = 0.058f * h * 1.1f;
                    float top = local.y - (0.55f * radiusY);
                    float back = Mathf.Min(local.y - (0.05f * radiusY), (-0.2f * radiusZ) - local.z);
                    float ear = Mathf.Min(Mathf.Abs(local.x) - (0.05f * h), (0.3f * radiusY) - local.y);
                    return (outside, outside, Mathf.Min(-ear, Mathf.Max(top, back)));
                }

                default:
                    return (outside, outside, outside);
            }
        }
    }
}
