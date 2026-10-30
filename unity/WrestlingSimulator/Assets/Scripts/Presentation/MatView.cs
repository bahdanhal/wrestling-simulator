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
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                DestroyImmediateOrLater(transform.GetChild(i).gameObject);
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

            Material whiteLine = MakeMaterial("Mat_WhiteLine", new Color(0.95f, 0.96f, 0.98f), 0.32f);
            Flat("Central circle inner line", ProceduralMesh.Annulus(Mathf.Max(0.02f, circle - 0.018f), circle, 0.0045f), whiteLine);
            Flat("Central boundary line", ProceduralMesh.Annulus(central - 0.014f, central + 0.014f, 0.0044f), whiteLine);
            Flat("Wrestling boundary line", ProceduralMesh.Annulus(wrestling - 0.018f, wrestling + 0.018f, 0.0044f), whiteLine);

            // Starting position bars inside the 1 m central circle and UWW corner arcs inside the central surface.
            Flat("Red start line", ProceduralMesh.Quad(new Vector2(-0.18f, 0f), new Vector2(0.05f, 0.28f), 0f, 0.0046f), _redCorner);
            Flat("Blue start line", ProceduralMesh.Quad(new Vector2(0.18f, 0f), new Vector2(0.05f, 0.28f), 0f, 0.0046f), _blueCorner);
            Flat("Center start line", ProceduralMesh.Quad(Vector2.zero, new Vector2(0.32f, 0.035f), 0f, 0.0045f), whiteLine);
            Flat("Red corner arc", ProceduralMesh.AnnulusArc(central - 0.24f, central - 0.14f, 200f, 250f, 0.0045f, 32), _redCorner);
            Flat("Blue corner arc", ProceduralMesh.AnnulusArc(central - 0.24f, central - 0.14f, 20f, 70f, 0.0045f, 32), _blueCorner);

            // Red corner on the red wrestler's side (left from the officials' table), blue opposite.
            float leg = outer - wrestling;
            Flat("Red corner", ProceduralMesh.CornerTriangle(new Vector2(-outer, -outer), leg * 1.6f, 0.002f), _redCorner);
            Flat("Blue corner", ProceduralMesh.CornerTriangle(new Vector2(outer, outer), leg * 1.6f, 0.002f), _blueCorner);

            BuildArena(outer, platformSide);
        }

        private void BuildArena(float outer, float platformSide)
        {
            var arenaRoot = new GameObject("Arena Environment");
            arenaRoot.transform.SetParent(transform, false);

            float floorY = -MatThickness - PlatformHeight;
            Material arenaFloorMat = MakeMaterial("Arena_Floor", new Color(0.06f, 0.075f, 0.105f), 0.35f);
            Material carpetMat = MakeMaterial("Arena_Carpet", new Color(0.08f, 0.14f, 0.28f), 0.18f);
            Material stageTrimMat = MakeMaterial("Arena_StageTrim", new Color(0.10f, 0.11f, 0.14f), 0.45f);
            Material barrierMat = MakeMaterial("Arena_Barrier", new Color(0.07f, 0.11f, 0.22f), 0.40f);
            Material tierDarkMat = MakeMaterial("Arena_TierDark", new Color(0.075f, 0.09f, 0.13f), 0.22f);
            Material tierSeatMat = MakeMaterial("Arena_TierSeat", new Color(0.10f, 0.18f, 0.34f), 0.30f);
            Material trussMat = MakeMaterial("Arena_Truss", new Color(0.14f, 0.16f, 0.20f), 0.50f);
            Material ledCyanMat = MakeEmissiveMaterial("Arena_LedCyan", new Color(0.18f, 0.58f, 0.96f), new Color(0.25f, 0.75f, 1.35f));
            Material ledGoldMat = MakeEmissiveMaterial("Arena_LedGold", new Color(0.95f, 0.72f, 0.20f), new Color(1.1f, 0.80f, 0.22f));
            Material ledRedMat = MakeEmissiveMaterial("Arena_LedRed", new Color(0.90f, 0.16f, 0.18f), new Color(1.2f, 0.18f, 0.20f));
            Material ledBlueMat = MakeEmissiveMaterial("Arena_LedBlue", new Color(0.14f, 0.36f, 0.95f), new Color(0.18f, 0.45f, 1.3f));
            Material spotLampMat = MakeEmissiveMaterial("Arena_SpotLamp", new Color(1.0f, 0.97f, 0.90f), new Color(2.2f, 2.1f, 1.9f));

            // Venue floor and competition carpet around the podium.
            VisualBox(arenaRoot.transform, "Arena Floor", new Vector3(0f, floorY - 0.05f, 0f), new Vector3(80f, 0.10f, 80f), Quaternion.identity, arenaFloorMat);
            VisualBox(arenaRoot.transform, "Competition Carpet", new Vector3(0f, floorY + 0.005f, 0f), new Vector3(platformSide + 6.4f, 0.01f, platformSide + 6.4f), Quaternion.identity, carpetMat);

            // Podium fascia coping and illuminated corner strips.
            float halfPlatform = platformSide * 0.5f;
            for (int side = 0; side < 4; side++)
            {
                Quaternion rot = Quaternion.Euler(0f, side * 90f, 0f);
                VisualBox(arenaRoot.transform, $"Podium Coping {side}", rot * new Vector3(0f, -MatThickness - 0.04f, halfPlatform + 0.03f), new Vector3(platformSide + 0.12f, 0.08f, 0.06f), rot, stageTrimMat);
                VisualBox(arenaRoot.transform, $"Podium Base Trim {side}", rot * new Vector3(0f, floorY + 0.06f, halfPlatform + 0.04f), new Vector3(platformSide + 0.14f, 0.12f, 0.08f), rot, stageTrimMat);
                Material stripMat = (side == 1 || side == 2) ? ledRedMat : ledBlueMat;
                VisualBox(arenaRoot.transform, $"Podium LED {side}", rot * new Vector3(0f, -MatThickness - 0.14f, halfPlatform + 0.025f), new Vector3(platformSide - 0.2f, 0.035f, 0.02f), rot, stripMat);
            }

            // Corner stairs leading up to Red (-X, -Z) and Blue (+X, +Z) corners.
            for (int step = 0; step < 4; step++)
            {
                float stepHeight = PlatformHeight * ((step + 1) / 4f);
                float offset = halfPlatform + 0.22f + ((3 - step) * 0.28f);
                VisualBox(arenaRoot.transform, $"Red Stair {step}", new Vector3(-halfPlatform + 0.9f, floorY + (stepHeight * 0.5f), -offset), new Vector3(1.6f, stepHeight, 0.28f), Quaternion.identity, stageTrimMat);
                VisualBox(arenaRoot.transform, $"Blue Stair {step}", new Vector3(halfPlatform - 0.9f, floorY + (stepHeight * 0.5f), offset), new Vector3(1.6f, stepHeight, 0.28f), Quaternion.identity, stageTrimMat);
            }

            // Octagonal perimeter LED barrier boards and tiered stadium seating bowl so all camera angles have depth.
            const int octSides = 8;
            float barrierRadius = halfPlatform + 3.2f;
            float barrierWidth = 2f * barrierRadius * Mathf.Tan(Mathf.PI / octSides) * 1.02f;
            for (int i = 0; i < octSides; i++)
            {
                float angle = i * (360f / octSides);
                Quaternion rot = Quaternion.Euler(0f, angle, 0f);
                VisualBox(arenaRoot.transform, $"Perimeter Barrier {i}", rot * new Vector3(0f, floorY + 0.48f, barrierRadius), new Vector3(barrierWidth, 0.96f, 0.22f), rot, barrierMat);
                Material ribbon = (i % 2 == 0) ? ledCyanMat : ledGoldMat;
                VisualBox(arenaRoot.transform, $"Perimeter Ribbon {i}", rot * new Vector3(0f, floorY + 0.82f, barrierRadius - 0.10f), new Vector3(barrierWidth * 0.96f, 0.08f, 0.04f), rot, ribbon);
                VisualBox(arenaRoot.transform, $"Perimeter Lower Ribbon {i}", rot * new Vector3(0f, floorY + 0.18f, barrierRadius - 0.10f), new Vector3(barrierWidth * 0.96f, 0.05f, 0.04f), rot, ledCyanMat);

                for (int tier = 0; tier < 8; tier++)
                {
                    float tierDist = barrierRadius + 2.6f + (tier * 1.55f);
                    float tierTop = floorY + 0.55f + (tier * 0.72f);
                    float tierHeight = tierTop - floorY;
                    float segWidth = 2f * tierDist * Mathf.Tan(Mathf.PI / octSides) * 1.03f;
                    VisualBox(arenaRoot.transform, $"Stand Riser {i}_{tier}", rot * new Vector3(0f, floorY + (tierHeight * 0.5f), tierDist), new Vector3(segWidth, tierHeight, 1.45f), rot, tierDarkMat);
                    VisualBox(arenaRoot.transform, $"Stand Seats {i}_{tier}", rot * new Vector3(0f, tierTop + 0.16f, tierDist - 0.18f), new Vector3(segWidth * 0.92f, 0.32f, 0.75f), rot, tierSeatMat);
                    if (tier % 2 == 1)
                    {
                        VisualBox(arenaRoot.transform, $"Stand Aisle Glow {i}_{tier}", rot * new Vector3(0f, tierTop + 0.02f, tierDist - 0.68f), new Vector3(segWidth * 0.95f, 0.03f, 0.05f), rot, ledCyanMat);
                    }
                }
            }

            // Overhead lighting truss rig above the mat.
            const float rigY = 8.4f;
            float rigHalf = outer + 1.2f;
            float rigSpan = rigHalf * 2f;
            for (int side = 0; side < 4; side++)
            {
                Quaternion rot = Quaternion.Euler(0f, side * 90f, 0f);
                VisualBox(arenaRoot.transform, $"Light Truss {side}", rot * new Vector3(0f, rigY, rigHalf), new Vector3(rigSpan + 0.3f, 0.32f, 0.32f), rot, trussMat);
                for (int lamp = -2; lamp <= 2; lamp++)
                {
                    VisualBox(arenaRoot.transform, $"Floodlight {side}_{lamp}", rot * new Vector3(lamp * 1.8f, rigY - 0.22f, rigHalf - 0.15f), new Vector3(0.55f, 0.16f, 0.28f), rot * Quaternion.Euler(35f, 0f, 0f), spotLampMat);
                }
            }
        }

        private Material MakeMaterial(string matName, Color colour, float smoothness)
        {
            Shader shader = _platform != null ? _platform.shader : Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader) { name = matName };
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private Material MakeEmissiveMaterial(string matName, Color baseColour, Color emissionColour)
        {
            Material material = MakeMaterial(matName, baseColour, 0.6f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emissionColour);
            return material;
        }

        private static void VisualBox(Transform parent, string partName, Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = partName;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyImmediateOrLater(collider);
            }

            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
