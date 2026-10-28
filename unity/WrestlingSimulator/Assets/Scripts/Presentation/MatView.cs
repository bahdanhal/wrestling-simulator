using UnityEngine;
using Wrestling.Core.Rules;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Builds the mat from the ruleset's geometry (Art. 4): central surface, orange passivity zone,
    /// central circle with its 10 cm band, protection area, red and blue corners, and the raised platform.
    /// </summary>
    public sealed class MatView : MonoBehaviour
    {
        /// <summary>Width of the band around the central circle (Art. 4).</summary>
        public const float CentralBandWidth = 0.10f;

        /// <summary>Platform height used for display; the rules allow 0.80–1.10 m.</summary>
        public const float PlatformHeight = 0.95f;

        /// <summary>Clear platform beyond the mat; the rules require at least 1 m.</summary>
        public const float PlatformMargin = 1.2f;

        public const float MatThickness = 0.06f;

        private static PhysicsMaterial _matSurface;

        private static PhysicsMaterial MatSurface => _matSurface != null ? _matSurface : (_matSurface = new PhysicsMaterial("Mat")
        {
            dynamicFriction = 0.8f,
            staticFriction = 0.9f,
            bounciness = 0.05f,
            frictionCombine = PhysicsMaterialCombine.Maximum,
            bounceCombine = PhysicsMaterialCombine.Minimum,
        });

        [SerializeField] private Material _centralSurface;
        [SerializeField] private Material _passivityZone;
        [SerializeField] private Material _protectionArea;
        [SerializeField] private Material _redCorner;
        [SerializeField] private Material _blueCorner;
        [SerializeField] private Material _platform;

        public void Configure(Material centralSurface, Material passivityZone, Material protectionArea, Material redCorner, Material blueCorner, Material platform)
        {
            _centralSurface = centralSurface;
            _passivityZone = passivityZone;
            _protectionArea = protectionArea;
            _redCorner = redCorner;
            _blueCorner = blueCorner;
            _platform = platform;
        }

        public void Build(MatGeometry mat)
        {
            foreach (Transform child in transform)
            {
                DestroyImmediateOrLater(child.gameObject);
            }

            float outer = mat.OuterRadius.ToFloat();
            float wrestling = mat.WrestlingAreaRadius.ToFloat();
            float central = mat.CentralSurfaceRadius.ToFloat();
            float circle = mat.CentralCircleRadius.ToFloat();

            // The mat is a square slab whose side is the protection area's outer diameter.
            Box("Protection area", new Vector3(0f, -MatThickness / 2f, 0f), new Vector3(outer * 2f, MatThickness, outer * 2f), _protectionArea);
            float platformSide = (outer + PlatformMargin) * 2f;
            Box("Platform", new Vector3(0f, -MatThickness - (PlatformHeight / 2f), 0f), new Vector3(platformSide, PlatformHeight, platformSide), _platform);

            Flat("Passivity zone", ProceduralMesh.Annulus(central, wrestling, 0.002f), _passivityZone);
            Flat("Central surface", ProceduralMesh.Annulus(0f, central, 0.003f), _centralSurface);
            Flat("Central circle band", ProceduralMesh.Annulus(circle, circle + CentralBandWidth, 0.004f), _passivityZone);

            // Red corner on the red wrestler's side (left from the officials' table), blue opposite.
            float leg = outer - wrestling;
            Flat("Red corner", ProceduralMesh.CornerTriangle(new Vector2(-outer, -outer), leg * 1.6f, 0.002f), _redCorner);
            Flat("Blue corner", ProceduralMesh.CornerTriangle(new Vector2(outer, outer), leg * 1.6f, 0.002f), _blueCorner);
        }

        private void Flat(string partName, Mesh mesh, Material material)
        {
            var part = new GameObject(partName);
            part.transform.SetParent(transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void Box(string partName, Vector3 centre, Vector3 size, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = partName;
            // The mat and platform are solid for falling bodies: grippy, with almost no bounce.
            part.GetComponent<Collider>().sharedMaterial = MatSurface;
            part.transform.SetParent(transform, false);
            part.transform.localPosition = centre;
            part.transform.localScale = size;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void DestroyImmediateOrLater(Object target)
        {
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
