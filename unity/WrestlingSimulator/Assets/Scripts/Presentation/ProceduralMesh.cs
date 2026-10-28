using System.Collections.Generic;
using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>Mesh generation for the mat markings.</summary>
    public static class ProceduralMesh
    {
        /// <summary>A flat ring (or disc when <paramref name="inner"/> is 0) on the XZ plane at height <paramref name="y"/>.</summary>
        public static Mesh Annulus(float inner, float outer, float y, int segments = 128)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices.Add((direction * inner) + (Vector3.up * y));
                vertices.Add((direction * outer) + (Vector3.up * y));
            }

            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                triangles.Add(a);
                triangles.Add(a + 2);
                triangles.Add(a + 1);
                triangles.Add(a + 1);
                triangles.Add(a + 2);
                triangles.Add(a + 3);
            }

            var mesh = new Mesh { name = "Annulus" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A right triangle in a corner of a square: the corner at <paramref name="corner"/>, legs of <paramref name="leg"/> along the square's sides.</summary>
        public static Mesh CornerTriangle(Vector2 corner, float leg, float y)
        {
            float sx = -Mathf.Sign(corner.x);
            float sz = -Mathf.Sign(corner.y);
            var a = new Vector3(corner.x, y, corner.y);
            var b = new Vector3(corner.x + (sx * leg), y, corner.y);
            var c = new Vector3(corner.x, y, corner.y + (sz * leg));
            var mesh = new Mesh { name = "Corner" };
            mesh.vertices = new[] { a, b, c };
            bool clockwise = (sx * sz) > 0f;
            mesh.triangles = clockwise ? new[] { 0, 2, 1 } : new[] { 0, 1, 2 };
            mesh.RecalculateNormals();
            if (mesh.normals[0].y < 0f)
            {
                mesh.triangles = clockwise ? new[] { 0, 1, 2 } : new[] { 0, 2, 1 };
                mesh.RecalculateNormals();
            }

            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
