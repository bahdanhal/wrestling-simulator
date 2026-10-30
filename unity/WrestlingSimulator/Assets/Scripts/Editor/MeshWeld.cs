using System.Collections.Generic;
using UnityEngine;

namespace Wrestling.Editor
{
    /// <summary>
    /// Surface connectivity of a mesh with UV-seam duplicates merged: each vertex maps to a welded
    /// point, and each point knows its neighbours, so smoothing, curvature and edge splits treat the
    /// body as one closed surface.
    /// </summary>
    internal sealed class MeshWeld
    {
        /// <summary>Welded point of each vertex.</summary>
        public readonly int[] PointOf;

        /// <summary>Position of each welded point (in the space the positions were given in).</summary>
        public readonly Vector3[] Points;

        /// <summary>Neighbouring points of each welded point.</summary>
        public readonly List<int>[] Neighbours;

        public MeshWeld(IReadOnlyList<Vector3> positions, IEnumerable<List<int>> submeshes, float tolerance = 1e-5f)
        {
            PointOf = new int[positions.Count];
            var cells = new Dictionary<(long, long, long), int>(positions.Count);
            var points = new List<Vector3>(positions.Count);
            float inverse = 1f / tolerance;
            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 p = positions[i];
                var key = ((long)Mathf.Round(p.x * inverse), (long)Mathf.Round(p.y * inverse), (long)Mathf.Round(p.z * inverse));
                if (!cells.TryGetValue(key, out int point))
                {
                    point = points.Count;
                    points.Add(p);
                    cells[key] = point;
                }

                PointOf[i] = point;
            }

            Points = points.ToArray();
            var sets = new HashSet<int>[Points.Length];
            for (int i = 0; i < sets.Length; i++)
            {
                sets[i] = new HashSet<int>();
            }

            foreach (List<int> triangles in submeshes)
            {
                for (int t = 0; t < triangles.Count; t += 3)
                {
                    int a = PointOf[triangles[t]];
                    int b = PointOf[triangles[t + 1]];
                    int c = PointOf[triangles[t + 2]];
                    Link(sets, a, b);
                    Link(sets, b, c);
                    Link(sets, c, a);
                }
            }

            Neighbours = new List<int>[Points.Length];
            for (int i = 0; i < sets.Length; i++)
            {
                Neighbours[i] = new List<int>(sets[i]);
            }
        }

        public int PointCount => Points.Length;

        /// <summary>Key of the undirected edge between two welded points.</summary>
        public static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        /// <summary>Averages a per-point value with its neighbours, <paramref name="iterations"/> times.</summary>
        public void Smooth(float[] values, int iterations, float amount = 0.5f)
        {
            var next = new float[values.Length];
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    List<int> around = Neighbours[i];
                    if (around.Count == 0)
                    {
                        next[i] = values[i];
                        continue;
                    }

                    float sum = 0f;
                    foreach (int j in around)
                    {
                        sum += values[j];
                    }

                    next[i] = Mathf.Lerp(values[i], sum / around.Count, amount);
                }

                System.Array.Copy(next, values, values.Length);
            }
        }

        /// <summary>Area-weighted point normals of the surface for the given point positions.</summary>
        public Vector3[] PointNormals(IReadOnlyList<Vector3> pointPositions, IEnumerable<List<int>> submeshes)
        {
            var normals = new Vector3[Points.Length];
            foreach (List<int> triangles in submeshes)
            {
                for (int t = 0; t < triangles.Count; t += 3)
                {
                    int a = PointOf[triangles[t]];
                    int b = PointOf[triangles[t + 1]];
                    int c = PointOf[triangles[t + 2]];
                    Vector3 n = Vector3.Cross(pointPositions[b] - pointPositions[a], pointPositions[c] - pointPositions[a]);
                    normals[a] += n;
                    normals[b] += n;
                    normals[c] += n;
                }
            }

            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = normals[i].normalized;
            }

            return normals;
        }

        private static void Link(HashSet<int>[] sets, int a, int b)
        {
            if (a != b)
            {
                sets[a].Add(b);
                sets[b].Add(a);
            }
        }
    }
}
