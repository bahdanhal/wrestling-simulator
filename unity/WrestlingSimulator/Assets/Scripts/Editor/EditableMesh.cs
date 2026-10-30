using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wrestling.Editor
{
    /// <summary>
    /// A skinned mesh opened for topology edits at import: every vertex attribute, the bone
    /// weights, the submesh triangles and all blend-shape frames, so vertices can be added by
    /// interpolation and the result written back without losing anything.
    /// </summary>
    internal sealed class EditableMesh
    {
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector4> Tangents = new List<Vector4>();
        public readonly List<Vector4>[] Uvs = new List<Vector4>[8];
        public readonly List<BoneWeight> Weights = new List<BoneWeight>();
        public readonly List<List<int>> Triangles = new List<List<int>>();
        public readonly List<Matrix4x4> Bindposes = new List<Matrix4x4>();
        public readonly List<Shape> Shapes = new List<Shape>();
        private List<Color> _colors;
        private readonly List<int> _uvDimensions = new List<int>(8);

        public sealed class Frame
        {
            public float Weight;
            public List<Vector3> Vertices;
            public List<Vector3> Normals;
            public List<Vector3> Tangents;
        }

        public sealed class Shape
        {
            public string Name;
            public List<Frame> Frames = new List<Frame>();
        }

        public int VertexCount => Positions.Count;

        public static EditableMesh Read(Mesh mesh)
        {
            var result = new EditableMesh();
            int count = mesh.vertexCount;
            mesh.GetVertices(result.Positions);
            mesh.GetNormals(result.Normals);
            mesh.GetTangents(result.Tangents);
            if (result.Normals.Count != count)
            {
                mesh.RecalculateNormals();
                mesh.GetNormals(result.Normals);
            }

            if (result.Tangents.Count != count)
            {
                result.Tangents.Clear();
                result.Tangents.AddRange(new Vector4[count]);
            }

            for (int channel = 0; channel < 8; channel++)
            {
                var uv = new List<Vector4>();
                mesh.GetUVs(channel, uv);
                result.Uvs[channel] = uv.Count == count ? uv : null;
                result._uvDimensions.Add(mesh.HasVertexAttribute(VertexAttribute.TexCoord0 + channel) ? mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0 + channel) : 0);
            }

            var colors = new List<Color>();
            mesh.GetColors(colors);
            result._colors = colors.Count == count ? colors : null;
            result.Weights.AddRange(mesh.boneWeights);
            if (result.Weights.Count != count)
            {
                throw new System.InvalidOperationException($"{mesh.name}: skinned body mesh without bone weights.");
            }

            mesh.GetBindposes(result.Bindposes);
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var triangles = new List<int>();
                mesh.GetTriangles(triangles, s);
                result.Triangles.Add(triangles);
            }

            var dv = new Vector3[count];
            var dn = new Vector3[count];
            var dt = new Vector3[count];
            for (int s = 0; s < mesh.blendShapeCount; s++)
            {
                var shape = new Shape { Name = mesh.GetBlendShapeName(s) };
                for (int f = 0; f < mesh.GetBlendShapeFrameCount(s); f++)
                {
                    mesh.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    shape.Frames.Add(new Frame
                    {
                        Weight = mesh.GetBlendShapeFrameWeight(s, f),
                        Vertices = new List<Vector3>(dv),
                        Normals = new List<Vector3>(dn),
                        Tangents = new List<Vector3>(dt),
                    });
                }

                result.Shapes.Add(shape);
            }

            return result;
        }

        /// <summary>Adds a vertex between <paramref name="a"/> and <paramref name="b"/> with every attribute interpolated.</summary>
        public int AddLerp(int a, int b, float t)
        {
            Positions.Add(Vector3.Lerp(Positions[a], Positions[b], t));
            Normals.Add(Vector3.Lerp(Normals[a], Normals[b], t).normalized);
            Vector4 ta = Tangents[a];
            Vector4 tangent = Vector4.Lerp(ta, Tangents[b], t);
            Vector3 t3 = ((Vector3)tangent).normalized;
            Tangents.Add(new Vector4(t3.x, t3.y, t3.z, ta.w));
            foreach (List<Vector4> uv in Uvs)
            {
                uv?.Add(Vector4.Lerp(uv[a], uv[b], t));
            }

            _colors?.Add(Color.Lerp(_colors[a], _colors[b], t));
            Weights.Add(BlendWeights(Weights[a], Weights[b], t));
            foreach (Shape shape in Shapes)
            {
                foreach (Frame frame in shape.Frames)
                {
                    frame.Vertices.Add(Vector3.Lerp(frame.Vertices[a], frame.Vertices[b], t));
                    frame.Normals.Add(Vector3.Lerp(frame.Normals[a], frame.Normals[b], t));
                    frame.Tangents.Add(Vector3.Lerp(frame.Tangents[a], frame.Tangents[b], t));
                }
            }

            return Positions.Count - 1;
        }

        /// <summary>Adds a blend shape with a single frame at full weight.</summary>
        public void AddShape(string name, Vector3[] vertices, Vector3[] normals)
        {
            var shape = new Shape { Name = name };
            shape.Frames.Add(new Frame
            {
                Weight = 100f,
                Vertices = new List<Vector3>(vertices),
                Normals = new List<Vector3>(normals),
                Tangents = new List<Vector3>(new Vector3[vertices.Length]),
            });
            Shapes.Add(shape);
        }

        public void Write(Mesh mesh)
        {
            mesh.ClearBlendShapes();
            // Resized in place: clearing an imported mesh would drop its CPU-side data.
            mesh.indexFormat = VertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(Positions);
            mesh.SetNormals(Normals);
            mesh.SetTangents(Tangents);
            for (int channel = 0; channel < 8; channel++)
            {
                if (Uvs[channel] == null)
                {
                    continue;
                }

                int dimension = _uvDimensions[channel];
                if (dimension == 2)
                {
                    var uv2 = new List<Vector2>(VertexCount);
                    foreach (Vector4 uv in Uvs[channel])
                    {
                        uv2.Add(uv);
                    }

                    mesh.SetUVs(channel, uv2);
                }
                else if (dimension == 3)
                {
                    var uv3 = new List<Vector3>(VertexCount);
                    foreach (Vector4 uv in Uvs[channel])
                    {
                        uv3.Add(uv);
                    }

                    mesh.SetUVs(channel, uv3);
                }
                else
                {
                    mesh.SetUVs(channel, Uvs[channel]);
                }
            }

            if (_colors != null)
            {
                mesh.SetColors(_colors);
            }

            mesh.subMeshCount = Triangles.Count;
            for (int s = 0; s < Triangles.Count; s++)
            {
                mesh.SetTriangles(Triangles[s], s, false);
            }

            mesh.boneWeights = Weights.ToArray();
            mesh.bindposes = Bindposes.ToArray();
            foreach (Shape shape in Shapes)
            {
                foreach (Frame frame in shape.Frames)
                {
                    mesh.AddBlendShapeFrame(shape.Name, frame.Weight, frame.Vertices.ToArray(), frame.Normals.ToArray(), frame.Tangents.ToArray());
                }
            }

            mesh.RecalculateBounds();
        }

        /// <summary>Sets UV channel <paramref name="channel"/> as four components.</summary>
        public void SetUv4(int channel, List<Vector4> values)
        {
            Uvs[channel] = values;
            while (_uvDimensions.Count <= channel)
            {
                _uvDimensions.Add(0);
            }

            _uvDimensions[channel] = 4;
        }

        /// <summary>Merges two vertices' influences, keeping the four strongest.</summary>
        public static BoneWeight BlendWeights(BoneWeight a, BoneWeight b, float t)
        {
            var influences = new Dictionary<int, float>(8);
            void Add(int bone, float weight)
            {
                if (weight > 0f)
                {
                    influences.TryGetValue(bone, out float existing);
                    influences[bone] = existing + weight;
                }
            }

            Add(a.boneIndex0, a.weight0 * (1f - t));
            Add(a.boneIndex1, a.weight1 * (1f - t));
            Add(a.boneIndex2, a.weight2 * (1f - t));
            Add(a.boneIndex3, a.weight3 * (1f - t));
            Add(b.boneIndex0, b.weight0 * t);
            Add(b.boneIndex1, b.weight1 * t);
            Add(b.boneIndex2, b.weight2 * t);
            Add(b.boneIndex3, b.weight3 * t);
            return Strongest(influences);
        }

        /// <summary>The four strongest influences, normalised to sum to one.</summary>
        public static BoneWeight Strongest(Dictionary<int, float> influences)
        {
            var sorted = new List<KeyValuePair<int, float>>(influences);
            sorted.Sort((x, y) => y.Value.CompareTo(x.Value));
            float total = 0f;
            for (int i = 0; i < sorted.Count && i < 4; i++)
            {
                total += sorted[i].Value;
            }

            var result = default(BoneWeight);
            float scale = total > 0f ? 1f / total : 0f;
            for (int i = 0; i < sorted.Count && i < 4; i++)
            {
                int bone = sorted[i].Key;
                float weight = sorted[i].Value * scale;
                switch (i)
                {
                    case 0:
                        result.boneIndex0 = bone;
                        result.weight0 = weight;
                        break;
                    case 1:
                        result.boneIndex1 = bone;
                        result.weight1 = weight;
                        break;
                    case 2:
                        result.boneIndex2 = bone;
                        result.weight2 = weight;
                        break;
                    default:
                        result.boneIndex3 = bone;
                        result.weight3 = weight;
                        break;
                }
            }

            return result;
        }
    }
}
