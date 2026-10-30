using System.Collections.Generic;
using UnityEngine;

namespace Wrestling.Editor
{
    /// <summary>
    /// Bakes the singlet into the body mesh at import. The cut (neckline, straps, armholes and leg
    /// bands) is traced as a smooth curve on the surface; body triangles near it are subdivided,
    /// every vertex stores its signed distance to the seam in metres (UV3.z, positive on the
    /// singlet), and the singlet is raised off the skin with a rolled binding at its edge.
    /// </summary>
    internal static class SingletSeam
    {
        /// <summary>Width of the elastic binding along the singlet's edges (metres).</summary>
        public const float TrimWidth = 0.008f;

        /// <summary>How far the fabric stands off the skin (metres).</summary>
        private const float Lift = 0.0015f;

        /// <summary>Extra height of the rolled binding over the fabric (metres).</summary>
        private const float Bead = 0.0005f;

        /// <summary>Distance from the seam within which triangles are subdivided, per pass (metres).</summary>
        private static readonly float[] RefineBands = { 0.02f, 0.008f, 0.0035f };

        /// <summary>Distances are exact within this band; beyond it the analytic cut is used (metres).</summary>
        private const float Band = 0.04f;

        private const float GridCell = 0.02f;

        /// <summary>Smoothing passes over the skin-weight regions, and along the cut's zero line.</summary>
        private const int RegionSmoothing = 4;

        private const int LineSmoothing = 80;

        /// <summary>How much of the profile's slope tilts the normals (the full slope over a few millimetres looks torn).</summary>
        private const float BevelShading = 0.35f;

        /// <param name="mesh">Body mesh with UV2 = normalised bind position and UV3 = region shares (torso, girdle, thigh, arm).</param>
        /// <param name="toRoot">Mesh space to model root space (metres).</param>
        /// <param name="height">Model height in root units.</param>
        /// <param name="curvature">Surface curvature per vertex, packed into UV3.w (extended as vertices are added).</param>
        /// <param name="armholes">The armhole planes, left then right (see <see cref="Armholes"/>).</param>
        public static void Build(EditableMesh mesh, Matrix4x4 toRoot, float height, List<float> curvature, (Armhole Left, Armhole Right) armholes)
        {
            Matrix4x4 fromRoot = toRoot.inverse;
            List<Vector4> body = mesh.Uvs[2];
            List<Vector4> regions = mesh.Uvs[3];

            // The raw cut per welded point, from position and smoothed skin-weight regions.
            var rootPositions = new List<Vector3>(mesh.VertexCount);
            foreach (Vector3 p in mesh.Positions)
            {
                rootPositions.Add(toRoot.MultiplyPoint3x4(p));
            }

            var weld = new MeshWeld(rootPositions, mesh.Triangles);
            var shares = new float[4][];
            for (int k = 0; k < 4; k++)
            {
                shares[k] = new float[weld.PointCount];
            }

            var pointBody = new Vector3[weld.PointCount];
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                int point = weld.PointOf[i];
                pointBody[point] = body[i];
                for (int k = 0; k < 4; k++)
                {
                    shares[k][point] = regions[i][k];
                }
            }

            for (int k = 0; k < 4; k++)
            {
                // Skin weights change in steps from vertex to vertex, which would notch the armholes.
                weld.Smooth(shares[k], RegionSmoothing);
            }

            var cut = new List<float>(mesh.VertexCount);
            var pointCut = new float[weld.PointCount];
            for (int p = 0; p < weld.PointCount; p++)
            {
                pointCut[p] = Cut(pointBody[p], new Vector4(shares[0][p], shares[1][p], shares[2][p], shares[3][p]), armholes);
            }

            for (int i = 0; i < mesh.VertexCount; i++)
            {
                cut.Add(pointCut[weld.PointOf[i]]);
            }

            var contour = new Contour(weld, pointCut, mesh.Triangles, height);

            var distance = new List<float>(mesh.VertexCount);
            var gradient = new List<Vector3>(mesh.VertexCount);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                contour.Distance(rootPositions[i], cut[i], out float d, out Vector3 g);
                distance.Add(d);
                gradient.Add(g);
            }

            foreach (float band in RefineBands)
            {
                Refine(mesh, toRoot, contour, band, cut, distance, gradient, rootPositions, curvature);
            }

            // Raise the fabric and roll the binding, tilting normals with the profile's slope.
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                float d = distance[i];
                float h = Profile(d);
                const float Step = 0.0002f;
                float slope = BevelShading * (Profile(d + Step) - Profile(d - Step)) / (2f * Step);
                Vector3 normalRoot = toRoot.MultiplyVector(mesh.Normals[i]).normalized;
                Vector3 g = Vector3.ProjectOnPlane(gradient[i], normalRoot);
                if (h > 0f)
                {
                    mesh.Positions[i] += fromRoot.MultiplyVector(normalRoot * h);
                }

                if (Mathf.Abs(slope) > 1e-4f && g.sqrMagnitude > 1e-8f)
                {
                    Vector3 tilted = (normalRoot - (g.normalized * slope)).normalized;
                    Vector3 n = fromRoot.MultiplyVector(tilted).normalized;
                    mesh.Normals[i] = n;
                    Vector4 tangent = mesh.Tangents[i];
                    Vector3 t = Vector3.ProjectOnPlane(tangent, n).normalized;
                    mesh.Tangents[i] = new Vector4(t.x, t.y, t.z, tangent.w);
                }
            }

            // UV3 becomes (torso share, arm share, seam distance, curvature).
            var packed = new List<Vector4>(mesh.VertexCount);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                Vector4 r = regions[i];
                packed.Add(new Vector4(r.x, r.w, distance[i], curvature[i]));
            }

            mesh.SetUv4(3, packed);
        }

        /// <summary>Height of the singlet surface over the skin at signed seam distance <paramref name="d"/>.</summary>
        private static float Profile(float d)
        {
            float lift = Lift * Smooth(-0.0004f, 0.0022f, d);
            float u = Mathf.Clamp01(d / TrimWidth);
            float bead = Bead * Mathf.Sin(Mathf.PI * u) * Mathf.Sin(Mathf.PI * u) * (d > 0f ? 1f : 0f);
            return lift + bead;
        }

        private static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - (2f * t));
        }

        /// <summary>
        /// Splits triangles within <paramref name="band"/> of the seam into four (and neighbours with
        /// one split edge into two, so the surface stays watertight across UV seams).
        /// </summary>
        private static void Refine(EditableMesh mesh, Matrix4x4 toRoot, Contour contour, float band, List<float> cut, List<float> distance, List<Vector3> gradient, List<Vector3> rootPositions, List<float> curvature)
        {
            var weld = new MeshWeld(rootPositions, mesh.Triangles);
            var refine = new List<bool[]>();
            var split = new HashSet<long>();
            foreach (List<int> triangles in mesh.Triangles)
            {
                var flags = new bool[triangles.Count / 3];
                for (int t = 0; t < flags.Length; t++)
                {
                    float d0 = distance[triangles[3 * t]];
                    float d1 = distance[triangles[(3 * t) + 1]];
                    float d2 = distance[triangles[(3 * t) + 2]];
                    bool straddles = (d0 >= 0f) != (d1 >= 0f) || (d1 >= 0f) != (d2 >= 0f);
                    if (straddles || Mathf.Min(Mathf.Abs(d0), Mathf.Min(Mathf.Abs(d1), Mathf.Abs(d2))) < band)
                    {
                        flags[t] = true;
                        AddEdges(split, weld, triangles, t);
                    }
                }

                refine.Add(flags);
            }

            // Closure: a triangle with two split edges is split fully.
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int s = 0; s < mesh.Triangles.Count; s++)
                {
                    List<int> triangles = mesh.Triangles[s];
                    bool[] flags = refine[s];
                    for (int t = 0; t < flags.Length; t++)
                    {
                        if (flags[t] || SplitCount(split, weld, triangles, t) < 2)
                        {
                            continue;
                        }

                        flags[t] = true;
                        AddEdges(split, weld, triangles, t);
                        changed = true;
                    }
                }
            }

            var midpoints = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = MeshWeld.EdgeKey(a, b);
                if (midpoints.TryGetValue(key, out int m))
                {
                    return m;
                }

                m = mesh.AddLerp(a, b, 0.5f);
                Vector3 root = toRoot.MultiplyPoint3x4(mesh.Positions[m]);
                rootPositions.Add(root);
                float c = 0.5f * (cut[a] + cut[b]);
                cut.Add(c);
                curvature.Add(0.5f * (curvature[a] + curvature[b]));
                contour.Distance(root, c, out float d, out Vector3 g);
                distance.Add(d);
                gradient.Add(g);
                midpoints[key] = m;
                return m;
            }

            for (int s = 0; s < mesh.Triangles.Count; s++)
            {
                List<int> triangles = mesh.Triangles[s];
                var result = new List<int>(triangles.Count * 2);
                for (int t = 0; t < triangles.Count; t += 3)
                {
                    int i0 = triangles[t];
                    int i1 = triangles[t + 1];
                    int i2 = triangles[t + 2];
                    bool s01 = split.Contains(MeshWeld.EdgeKey(weld.PointOf[i0], weld.PointOf[i1]));
                    bool s12 = split.Contains(MeshWeld.EdgeKey(weld.PointOf[i1], weld.PointOf[i2]));
                    bool s20 = split.Contains(MeshWeld.EdgeKey(weld.PointOf[i2], weld.PointOf[i0]));
                    int count = (s01 ? 1 : 0) + (s12 ? 1 : 0) + (s20 ? 1 : 0);
                    if (count == 0)
                    {
                        result.Add(i0);
                        result.Add(i1);
                        result.Add(i2);
                    }
                    else if (count == 3)
                    {
                        int m01 = Mid(i0, i1);
                        int m12 = Mid(i1, i2);
                        int m20 = Mid(i2, i0);
                        result.AddRange(new[] { i0, m01, m20, m01, i1, m12, m20, m12, i2, m01, m12, m20 });
                    }
                    else if (s01)
                    {
                        int m = Mid(i0, i1);
                        result.AddRange(new[] { i0, m, i2, m, i1, i2 });
                    }
                    else if (s12)
                    {
                        int m = Mid(i1, i2);
                        result.AddRange(new[] { i1, m, i0, m, i2, i0 });
                    }
                    else
                    {
                        int m = Mid(i2, i0);
                        result.AddRange(new[] { i2, m, i1, m, i0, i1 });
                    }
                }

                mesh.Triangles[s].Clear();
                mesh.Triangles[s].AddRange(result);
            }
        }

        private static void AddEdges(HashSet<long> split, MeshWeld weld, List<int> triangles, int t)
        {
            int a = weld.PointOf[triangles[3 * t]];
            int b = weld.PointOf[triangles[(3 * t) + 1]];
            int c = weld.PointOf[triangles[(3 * t) + 2]];
            split.Add(MeshWeld.EdgeKey(a, b));
            split.Add(MeshWeld.EdgeKey(b, c));
            split.Add(MeshWeld.EdgeKey(c, a));
        }

        private static int SplitCount(HashSet<long> split, MeshWeld weld, List<int> triangles, int t)
        {
            int a = weld.PointOf[triangles[3 * t]];
            int b = weld.PointOf[triangles[(3 * t) + 1]];
            int c = weld.PointOf[triangles[(3 * t) + 2]];
            return (split.Contains(MeshWeld.EdgeKey(a, b)) ? 1 : 0) + (split.Contains(MeshWeld.EdgeKey(b, c)) ? 1 : 0) + (split.Contains(MeshWeld.EdgeKey(c, a)) ? 1 : 0);
        }

        /// <summary>
        /// An armhole as a plane across the top of the arm in normalised bind coordinates (see
        /// CharacterImport): everything past <see cref="Point"/> along <see cref="Along"/> (the arm's
        /// direction) is arm, so the opening is a clean oval round the shoulder.
        /// </summary>
        public readonly struct Armhole
        {
            public readonly Vector3 Point;
            public readonly Vector3 Along;

            public Armhole(Vector3 point, Vector3 along)
            {
                Point = point;
                Along = along.normalized;
            }
        }

        /// <summary>How far in from the shoulder joint the armhole plane sits, in height units.</summary>
        private const float ArmholeInset = 0.032f;

        /// <summary>How far in from the armhole plane's centre the torso's side wall is, in height units.</summary>
        private const float ArmholeSideInset = 0.006f;

        /// <summary>How far below the armhole plane's centre the armpit ends, in height units.</summary>
        private const float ArmholeDepth = 0.07f;

        /// <summary>The armhole planes from the shoulder and elbow joints, given in normalised bind coordinates.</summary>
        public static Armhole MakeArmhole(Vector3 shoulder, Vector3 elbow)
        {
            Vector3 along = (elbow - shoulder).normalized;
            return new Armhole(shoulder - (along * ArmholeInset), along);
        }

        /// <summary>
        /// The singlet's cut as an approximate signed distance in height units (positive on the
        /// singlet): an arch from the chest and back scoop over the shoulder straps into the underarm,
        /// cut back at the armhole planes and where the head takes over, and the leg bands.
        /// </summary>
        public static float Cut(Vector3 p, Vector4 regions, (Armhole Left, Armhole Right) armholes)
        {
            float x = Mathf.Abs(p.x);
            float y = p.y;
            float front = Smooth(-0.035f, 0.035f, p.z);

            const float StrapInner = 0.062f;
            const float StrapOuter = 0.092f;
            float scoopY = Mathf.Lerp(0.752f, 0.726f, front);
            const float ArmpitY = 0.706f;
            const float PeakY = 0.892f;

            float u = Mathf.Clamp01(x / StrapInner);
            float su = u * u * (3f - (2f * u));
            float dsu = 6f * u * (1f - u) / StrapInner;

            const float ArmSpan = 0.118f - StrapOuter;
            float v = Mathf.Clamp01((x - StrapOuter) / ArmSpan);
            float sv = v * v * (3f - (2f * v));
            float dsv = -6f * v * (1f - v) / ArmSpan;

            float upperY = x < StrapInner ? Mathf.Lerp(scoopY, PeakY, su) : (x <= StrapOuter ? PeakY : Mathf.Lerp(PeakY, ArmpitY, sv));
            float slopeX = x < StrapInner ? (PeakY - scoopY) * dsu : (x <= StrapOuter ? 0f : (PeakY - ArmpitY) * dsv);

            float bodyWeight = regions.x + regions.y + regions.z;
            float otherWeight = Mathf.Clamp01(1f - bodyWeight - regions.w);
            float headWeight = otherWeight * Smooth(0.865f, 0.895f, y);
            upperY -= 0.35f * Smooth(0.08f, 0.38f, headWeight);

            float upper = (upperY - y) / Mathf.Sqrt(1f + (slopeX * slopeX));

            Armhole armhole = p.x >= 0f ? armholes.Left : armholes.Right;
            // Past the plane and outside the torso's side wall below the shoulder.
            // Well below the armpit the side wall gives way to the torso and hips (not to the hanging arm).
            float belowArmpit = Mathf.Max(0f, armhole.Point.y - ArmholeDepth - y) * 4f * (1f - Smooth(0.3f, 0.7f, regions.w));
            float arm = Mathf.Max(Vector3.Dot(armhole.Point - p, armhole.Along), Mathf.Abs(armhole.Point.x) - ArmholeSideInset - x + belowArmpit);

            float legY = 0.382f + (0.014f * Smooth(0.025f, 0.095f, x));
            float lowerLegWeight = otherWeight * Smooth(0.36f, 0.26f, y);
            float lower = (y - legY) - (0.25f * lowerLegWeight);

            return Mathf.Min(Mathf.Min(upper, arm), lower);
        }

        /// <summary>The zero line of the cut, smoothed along its length, with a grid for distance queries.</summary>
        private sealed class Contour
        {
            private readonly List<Vector3> _points = new List<Vector3>();
            private readonly List<(int A, int B, Vector3 Inside)> _segments = new List<(int, int, Vector3)>();
            private readonly Dictionary<(int, int, int), List<int>> _grid = new Dictionary<(int, int, int), List<int>>();
            private readonly float _height;

            public Contour(MeshWeld weld, float[] cut, List<List<int>> submeshes, float height)
            {
                _height = height;
                var pointOfEdge = new Dictionary<long, int>();
                int EdgePoint(int a, int b)
                {
                    long key = MeshWeld.EdgeKey(a, b);
                    if (!pointOfEdge.TryGetValue(key, out int index))
                    {
                        float t = cut[a] / (cut[a] - cut[b]);
                        index = _points.Count;
                        _points.Add(Vector3.Lerp(weld.Points[a], weld.Points[b], t));
                        pointOfEdge[key] = index;
                    }

                    return index;
                }

                foreach (List<int> triangles in submeshes)
                {
                    for (int t = 0; t < triangles.Count; t += 3)
                    {
                        int a = weld.PointOf[triangles[t]];
                        int b = weld.PointOf[triangles[t + 1]];
                        int c = weld.PointOf[triangles[t + 2]];
                        if (a == b || b == c || c == a)
                        {
                            continue;
                        }

                        var crossings = new List<int>(2);
                        if ((cut[a] >= 0f) != (cut[b] >= 0f))
                        {
                            crossings.Add(EdgePoint(a, b));
                        }

                        if ((cut[b] >= 0f) != (cut[c] >= 0f))
                        {
                            crossings.Add(EdgePoint(b, c));
                        }

                        if ((cut[c] >= 0f) != (cut[a] >= 0f))
                        {
                            crossings.Add(EdgePoint(c, a));
                        }

                        if (crossings.Count == 2 && crossings[0] != crossings[1])
                        {
                            Vector3 inside = TriangleGradient(weld.Points[a], weld.Points[b], weld.Points[c], cut[a], cut[b], cut[c]);
                            _segments.Add((crossings[0], crossings[1], inside.normalized));
                        }
                    }
                }

                SmoothAlongLine();
                for (int s = 0; s < _segments.Count; s++)
                {
                    Vector3 p0 = _points[_segments[s].A];
                    Vector3 p1 = _points[_segments[s].B];
                    Vector3 min = Vector3.Min(p0, p1) - (Vector3.one * Band);
                    Vector3 max = Vector3.Max(p0, p1) + (Vector3.one * Band);
                    (int x0, int y0, int z0) = Cell(min);
                    (int x1, int y1, int z1) = Cell(max);
                    for (int x = x0; x <= x1; x++)
                    {
                        for (int y = y0; y <= y1; y++)
                        {
                            for (int z = z0; z <= z1; z++)
                            {
                                if (!_grid.TryGetValue((x, y, z), out List<int> list))
                                {
                                    list = new List<int>();
                                    _grid[(x, y, z)] = list;
                                }

                                list.Add(s);
                            }
                        }
                    }
                }
            }

            /// <summary>Signed distance (metres, positive on the singlet) and its direction of increase.</summary>
            public void Distance(Vector3 p, float cut, out float distance, out Vector3 gradient)
            {
                float far = Mathf.Abs(cut) * _height;
                float sign = cut >= 0f ? 1f : -1f;
                distance = sign * Mathf.Max(far, Band);
                gradient = Vector3.zero;
                if (far > 1.5f * Band || !_grid.TryGetValue(Cell(p), out List<int> candidates))
                {
                    return;
                }

                float best = float.MaxValue;
                Vector3 closest = p;
                Vector3 inside = Vector3.zero;
                foreach (int s in candidates)
                {
                    Vector3 a = _points[_segments[s].A];
                    Vector3 b = _points[_segments[s].B];
                    Vector3 ab = b - a;
                    float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                    Vector3 q = a + (ab * t);
                    float d = (p - q).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        closest = q;
                        inside = _segments[s].Inside;
                    }
                }

                float dist = Mathf.Sqrt(best);
                if (dist > Band)
                {
                    return;
                }

                Vector3 offset = p - closest;
                float along = Vector3.Dot(offset, inside);
                if (Mathf.Abs(along) > 0.25f * dist)
                {
                    sign = along >= 0f ? 1f : -1f;
                }

                distance = sign * dist;
                gradient = dist > 1e-6f ? offset * (sign / dist) : inside;
            }

            private void SmoothAlongLine()
            {
                var neighbours = new List<int>[_points.Count];
                for (int i = 0; i < neighbours.Length; i++)
                {
                    neighbours[i] = new List<int>(2);
                }

                foreach ((int a, int b, Vector3 _) in _segments)
                {
                    neighbours[a].Add(b);
                    neighbours[b].Add(a);
                }

                // Taubin smoothing (shrink, then inflate) keeps the line's length and position.
                var next = new Vector3[_points.Count];
                for (int iteration = 0; iteration < LineSmoothing; iteration++)
                {
                    float factor = iteration % 2 == 0 ? 0.5f : -0.53f;
                    for (int i = 0; i < _points.Count; i++)
                    {
                        next[i] = _points[i];
                        if (neighbours[i].Count == 2)
                        {
                            Vector3 mid = 0.5f * (_points[neighbours[i][0]] + _points[neighbours[i][1]]);
                            next[i] = _points[i] + (factor * (mid - _points[i]));
                        }
                    }

                    _points.Clear();
                    _points.AddRange(next);
                }
            }

            private static (int, int, int) Cell(Vector3 p) =>
                (Mathf.FloorToInt(p.x / GridCell), Mathf.FloorToInt(p.y / GridCell), Mathf.FloorToInt(p.z / GridCell));

            /// <summary>Gradient of the linear function with values fa, fb, fc on the triangle's corners.</summary>
            private static Vector3 TriangleGradient(Vector3 a, Vector3 b, Vector3 c, float fa, float fb, float fc)
            {
                Vector3 e1 = b - a;
                Vector3 e2 = c - a;
                Vector3 n = Vector3.Cross(e1, e2);
                float area2 = n.sqrMagnitude;
                if (area2 < 1e-16f)
                {
                    return Vector3.zero;
                }

                return (((fb - fa) * Vector3.Cross(e2, n)) + ((fc - fa) * Vector3.Cross(n, e1))) / area2;
            }
        }
    }
}
