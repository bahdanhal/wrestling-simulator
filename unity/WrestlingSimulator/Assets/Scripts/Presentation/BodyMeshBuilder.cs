using System.Collections.Generic;
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

    /// <summary>A generated, skinned body mesh with its bind poses.</summary>
    public sealed class BodyMesh
    {
        public BodyMesh(Mesh mesh, Matrix4x4[] bindPoses)
        {
            Mesh = mesh;
            BindPoses = bindPoses;
        }

        public Mesh Mesh { get; }

        public Matrix4x4[] BindPoses { get; }
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
            public Bounds Bounds;

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

        public static BodyMesh Build(BodyProportions body, BodyShape shape, float cellSize)
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
            float blend = 0.02f * body.Height / 1.8f;
            Bounds bounds = volumes[0].Bounds;
            foreach (Volume volume in volumes)
            {
                bounds.Encapsulate(volume.Bounds);
            }

            bounds.Expand(cellSize * 4f);
            var field = new Field(volumes, blend);
            Mesh mesh = Polygonise(field, bounds, cellSize, body, lengths, bindPoses);
            mesh.bindposes = bindPoses;
            return new BodyMesh(mesh, bindPoses);
        }

        private static List<Volume> Volumes(BodyProportions body, BodyShape shape, float[] lengths, Matrix4x4[] worldToBone)
        {
            float h = body.Height;
            float g = shape.Girth;
            float hip = shape.Female ? 1.07f : 1f;
            float chestWidth = shape.Female ? 1.22f : 1.36f;
            var volumes = new List<Volume>();

            void Add(Bone bone, Vector3 a, Vector3 b, float r0, float r1, float sx = 1f, float sz = 1f)
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
                volumes.Add(volume);
            }

            Vector3 Y(float y, float z = 0f) => new Vector3(0f, y, z);

            float hipsLength = lengths[(int)Bone.Hips];
            Add(Bone.Hips, Y(-0.045f * h), Y(hipsLength), 0.075f * h * g * hip, 0.068f * h * g, 1.3f, 0.85f);
            Add(Bone.Spine, Y(0f), Y(lengths[(int)Bone.Spine]), 0.068f * h * g, 0.075f * h * g, 1.3f, 0.85f);
            Add(Bone.Chest, Y(0f), Y(lengths[(int)Bone.Chest] * 0.8f), 0.078f * h * g, 0.084f * h * g, chestWidth, 0.78f);
            Add(Bone.Neck, Y(-0.03f * h), Y(lengths[(int)Bone.Neck]), 0.034f * h * g, 0.031f * h * g, 1.1f, 1f);
            Vector3 H(float x, float y, float z) => new Vector3(x * h, y * h, z * h);
            float chestLength = lengths[(int)Bone.Chest];
            foreach (float side in new[] { -1f, 1f })
            {
                // Trapezius from the neck to the shoulder.
                Add(Bone.Chest, new Vector3(side * 0.03f * h, chestLength * 0.92f, -0.008f * h), new Vector3(side * 0.085f * h, chestLength * 0.82f, -0.004f * h), 0.03f * h * g, 0.026f * h * g);
            }

            Add(Bone.Head, H(0f, -0.02f, 0f), H(0f, 0.02f, 0f), 0.058f * h, 0.058f * h, 0.95f, 1.1f);
            Add(Bone.Head, H(0f, -0.03f, 0.01f), H(0f, -0.05f, 0.03f), 0.032f * h, 0.024f * h, 1.15f, 1f);
            Add(Bone.Head, H(0f, -0.058f, 0.042f), H(0f, -0.058f, 0.042f), 0.013f * h, 0.013f * h);
            Add(Bone.Head, H(-0.028f, 0.014f, 0.05f), H(0.028f, 0.014f, 0.05f), 0.01f * h, 0.01f * h);
            Add(Bone.Head, H(0f, 0.006f, 0.06f), H(0f, -0.012f, 0.071f), 0.007f * h, 0.011f * h);
            foreach (float side in new[] { -1f, 1f })
            {
                Add(Bone.Head, H(side * 0.032f, -0.004f, 0.045f), H(side * 0.032f, -0.004f, 0.045f), 0.014f * h, 0.014f * h);
                Add(Bone.Head, H(side * 0.054f, 0f, 0f), H(side * 0.054f, -0.012f, 0f), 0.016f * h, 0.013f * h, 0.6f, 1f);
            }

            foreach ((Bone upper, Bone lower, Bone hand) in new[] { (Bone.LeftUpperArm, Bone.LeftLowerArm, Bone.LeftHand), (Bone.RightUpperArm, Bone.RightLowerArm, Bone.RightHand) })
            {
                Add(upper, Y(0.005f * h), Y(0.015f * h), 0.045f * h * g, 0.042f * h * g);
                Add(upper, Y(0f), Y(lengths[(int)upper]), 0.038f * h * g, 0.03f * h * g);
                Add(lower, Y(0f), Y(lengths[(int)lower]), 0.03f * h * g, 0.021f * h, 1.15f, 0.85f);
                Add(hand, Y(0f), Y(0.055f * h), 0.022f * h, 0.02f * h, 1.35f, 0.55f);
                Add(hand, Y(0.05f * h, 0.004f * h), Y(0.085f * h, 0.012f * h), 0.016f * h, 0.011f * h, 1.3f, 0.5f);
                Add(hand, Y(0.01f * h, 0.015f * h), Y(0.045f * h, 0.022f * h), 0.009f * h, 0.008f * h);
            }

            foreach ((Bone upper, Bone lower, Bone foot) in new[] { (Bone.LeftUpperLeg, Bone.LeftLowerLeg, Bone.LeftFoot), (Bone.RightUpperLeg, Bone.RightLowerLeg, Bone.RightFoot) })
            {
                float thigh = lengths[(int)upper];
                float shin = lengths[(int)lower];
                Add(upper, Y(0f), Y(thigh), 0.066f * h * g, 0.042f * h * g);
                Add(lower, Y(0f), Y(shin), 0.04f * h * g, 0.025f * h);
                Add(lower, Y(0.15f * shin, -0.01f * h), Y(0.45f * shin, -0.012f * h), 0.043f * h * g, 0.04f * h * g);
                Add(foot, Y(-0.035f * h, -0.022f * h), Y(lengths[(int)foot] * 0.95f, -0.025f * h), 0.028f * h, 0.024f * h, 1.25f, 0.7f);
            }

            return volumes;
        }

        /// <summary>Signed distance to the body: smooth union of the volumes near the point.</summary>
        private sealed class Field
        {
            private readonly List<Volume> _volumes;
            private readonly float _blend;

            public Field(List<Volume> volumes, float blend)
            {
                _volumes = volumes;
                _blend = blend;
            }

            public IReadOnlyList<Volume> Volumes => _volumes;

            public float Sample(Vector3 point)
            {
                float distance = 1f;
                for (int i = 0; i < _volumes.Count; i++)
                {
                    Volume volume = _volumes[i];
                    Bounds bounds = volume.Bounds;
                    if (point.x < bounds.min.x - _blend || point.x > bounds.max.x + _blend
                        || point.y < bounds.min.y - _blend || point.y > bounds.max.y + _blend
                        || point.z < bounds.min.z - _blend || point.z > bounds.max.z + _blend)
                    {
                        continue;
                    }

                    distance = SmoothMin(distance, volume.Distance(point), _blend);
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

        private static Mesh Polygonise(Field field, Bounds bounds, float cell, BodyProportions body, float[] lengths, Matrix4x4[] worldToBone)
        {
            int nx = Mathf.CeilToInt(bounds.size.x / cell) + 1;
            int ny = Mathf.CeilToInt(bounds.size.y / cell) + 1;
            int nz = Mathf.CeilToInt(bounds.size.z / cell) + 1;
            Vector3 origin = bounds.min;
            var values = new float[nx * ny * nz];
            int Index(int x, int y, int z) => x + (nx * (y + (ny * z)));
            for (int z = 0; z < nz; z++)
            {
                for (int y = 0; y < ny; y++)
                {
                    for (int x = 0; x < nx; x++)
                    {
                        values[Index(x, y, z)] = field.Sample(origin + new Vector3(x * cell, y * cell, z * cell));
                    }
                }
            }

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
            var masks = new List<Color>(vertices.Count);
            var head = new List<Vector4>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
            {
                normals[i] = field.Normal(vertices[i], cell * 0.5f);
                (BoneWeight weight, List<(int Bone, float Weight)> influences) = Skin(field, vertices[i]);
                weights[i] = weight;
                masks.Add(Masks(vertices[i], influences, body, lengths, worldToBone));
                float headWeight = 0f;
                foreach ((int bone, float w) in influences)
                {
                    if (bone == (int)Bone.Head)
                    {
                        headWeight = w;
                    }
                }

                Vector3 local = worldToBone[(int)Bone.Head].MultiplyPoint3x4(vertices[i]) / body.Height;
                head.Add(new Vector4(local.x, local.y, local.z, headWeight));
            }

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

            var mesh = new Mesh { name = "Wrestler body", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(masks);
            mesh.SetUVs(1, head);
            mesh.boneWeights = weights;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
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
            return Mathf.Max(bodyLine, StrapBand(x, halfWidth));
        }

        /// <summary>The strap band only, above shoulder height, for neck and upper-arm skin (chest-bone space).</summary>
        private static float ShoulderStrap(Vector3 local, float length, BodyProportions body)
        {
            float halfWidth = 0.084f * body.Height * 1.3f;
            return Mathf.Min(StrapBand(Mathf.Abs(local.x), halfWidth), local.y - (0.85f * length));
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
