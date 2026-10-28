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
    /// Generates a wrestler's body as one smooth skinned mesh: anatomical volumes on each bone,
    /// blended into a signed distance field, polygonised with surface nets, skinned to the
    /// <see cref="Skeleton"/> and split into skin, singlet, shoe and hair materials. Every body is
    /// built from the wrestler's own measurements.
    /// </summary>
    public static class BodyMeshBuilder
    {
        public const int SkinSubmesh = 0;
        public const int SingletSubmesh = 1;
        public const int ShoeSubmesh = 2;
        public const int HairSubmesh = 3;
        private const int SubmeshCount = 4;

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
            Add(Bone.Head, Y(-0.02f * h), Y(0.02f * h), 0.058f * h, 0.058f * h, 0.95f, 1.1f);
            Add(Bone.Head, Y(-0.03f * h, 0.018f * h), Y(-0.048f * h, 0.035f * h), 0.03f * h, 0.026f * h);
            Add(Bone.Head, Y(-0.002f * h, 0.062f * h), Y(-0.012f * h, 0.068f * h), 0.009f * h, 0.008f * h);
            Add(Bone.Head, new Vector3(0.054f * h, 0f, 0f), new Vector3(0.054f * h, -0.012f * h, 0f), 0.016f * h, 0.013f * h, 0.6f, 1f);
            Add(Bone.Head, new Vector3(-0.054f * h, 0f, 0f), new Vector3(-0.054f * h, -0.012f * h, 0f), 0.016f * h, 0.013f * h, 0.6f, 1f);

            foreach ((Bone upper, Bone lower, Bone hand) in new[] { (Bone.LeftUpperArm, Bone.LeftLowerArm, Bone.LeftHand), (Bone.RightUpperArm, Bone.RightLowerArm, Bone.RightHand) })
            {
                Add(upper, Y(0.005f * h), Y(0.015f * h), 0.045f * h * g, 0.042f * h * g);
                Add(upper, Y(0f), Y(lengths[(int)upper]), 0.038f * h * g, 0.03f * h * g);
                Add(lower, Y(0f), Y(lengths[(int)lower]), 0.03f * h * g, 0.021f * h, 1.15f, 0.85f);
                Add(hand, Y(0f), Y(0.07f * h), 0.022f * h, 0.017f * h, 1.4f, 0.6f);
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
            var regions = new int[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                normals[i] = field.Normal(vertices[i], cell * 0.5f);
                (weights[i], regions[i]) = Skin(field, vertices[i], body, lengths, worldToBone);
            }

            var submeshes = new List<int>[SubmeshCount];
            for (int i = 0; i < SubmeshCount; i++)
            {
                submeshes[i] = new List<int>();
            }

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

                Triangle(a, b, c);
                Triangle(a, c, d);
            }

            // Each triangle takes the material of its centre, so region borders follow the surface at half-cell detail.
            void Triangle(int a, int b, int c)
            {
                int region = regions[a] == regions[b] && regions[b] == regions[c]
                    ? regions[a]
                    : Skin(field, (vertices[a] + vertices[b] + vertices[c]) / 3f, body, lengths, worldToBone).Region;
                List<int> target = submeshes[region];
                target.Add(a);
                target.Add(b);
                target.Add(c);
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
            mesh.boneWeights = weights;
            mesh.subMeshCount = SubmeshCount;
            for (int i = 0; i < SubmeshCount; i++)
            {
                mesh.SetTriangles(submeshes[i], i);
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Up to four bone weights from distance to each bone's volumes, and the material region.</summary>
        private static (BoneWeight Weight, int Region) Skin(Field field, Vector3 point, BodyProportions body, float[] lengths, Matrix4x4[] worldToBone)
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

            const float falloff = 0.012f;
            var chosen = new List<(int Bone, float Weight)>();
            for (int i = 0; i < best.Length; i++)
            {
                if (best[i] - best[closest] < falloff * 5f)
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
                float w = chosen[i].Weight / total;
                switch (i)
                {
                    case 0:
                        weight.boneIndex0 = chosen[i].Bone;
                        weight.weight0 = w;
                        break;
                    case 1:
                        weight.boneIndex1 = chosen[i].Bone;
                        weight.weight1 = w;
                        break;
                    case 2:
                        weight.boneIndex2 = chosen[i].Bone;
                        weight.weight2 = w;
                        break;
                    default:
                        weight.boneIndex3 = chosen[i].Bone;
                        weight.weight3 = w;
                        break;
                }
            }

            return (weight, Region((Bone)closest, worldToBone[closest].MultiplyPoint3x4(point), lengths[closest], body));
        }

        /// <summary>Material region from the dominant bone and the point in that bone's space.</summary>
        private static int Region(Bone bone, Vector3 local, float length, BodyProportions body)
        {
            float h = body.Height;
            switch (bone)
            {
                case Bone.Hips:
                case Bone.Spine:
                    return SingletSubmesh;
                case Bone.Chest:
                {
                    if (local.y < length * 0.7f)
                    {
                        return SingletSubmesh;
                    }

                    // Straps over the shoulders; neckline and armholes show skin.
                    float side = Mathf.Abs(local.x) / (0.084f * h * 1.3f);
                    bool strap = side > 0.3f && side < 0.62f;
                    bool scoop = local.z > 0f && local.y > length * 0.78f;
                    return strap && !scoop ? SingletSubmesh : SkinSubmesh;
                }

                case Bone.LeftUpperLeg:
                case Bone.RightUpperLeg:
                    return local.y < length * 0.42f ? SingletSubmesh : SkinSubmesh;
                case Bone.LeftLowerLeg:
                case Bone.RightLowerLeg:
                    return local.y > length * 0.68f ? ShoeSubmesh : SkinSubmesh;
                case Bone.LeftFoot:
                case Bone.RightFoot:
                    return ShoeSubmesh;
                case Bone.Head:
                {
                    float top = local.y / (0.058f * h);
                    float front = local.z / (0.058f * h * 1.1f);
                    bool ear = Mathf.Abs(local.x) > 0.05f * h && top < 0.3f;
                    return !ear && (top > 0.55f || (top > 0.05f && front < -0.2f)) ? HairSubmesh : SkinSubmesh;
                }

                default:
                    return SkinSubmesh;
            }
        }
    }
}
