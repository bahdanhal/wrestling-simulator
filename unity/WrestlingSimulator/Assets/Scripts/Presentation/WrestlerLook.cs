using System.Collections.Generic;
using UnityEngine;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Presentation
{
    /// <summary>
    /// How a wrestler looks: which base model and skin, the blend-shape weights for build and
    /// face, height, leg ratio and reach. Build comes from the wrestler's body and strength; skin and
    /// face come from a stable hash of their identity, so a wrestler always looks the same.
    /// </summary>
    public sealed class WrestlerLook
    {
        private static readonly string[] FaceShapes = { "Face_Square", "Face_Round", "Face_Oval" };

        private static readonly string[] FaceFeatures =
        {
            "Nose_Large", "Nose_Hump", "Nose_Broken", "Chin_Prominent", "Jaw_Wide", "Cheekbones_High", "Ears_Out",
        };

        private static readonly (Color Tint, Color Scalp)[] HairPalette =
        {
            (new Color(0.32f, 0.30f, 0.30f), new Color(0.08f, 0.07f, 0.07f)),
            (new Color(0.55f, 0.46f, 0.40f), new Color(0.13f, 0.10f, 0.08f)),
            (new Color(0.82f, 0.68f, 0.56f), new Color(0.18f, 0.13f, 0.10f)),
            (new Color(1.05f, 0.76f, 0.58f), new Color(0.24f, 0.15f, 0.10f)),
            (new Color(0.95f, 0.85f, 0.74f), new Color(0.22f, 0.18f, 0.14f)),
            (new Color(1.22f, 1.02f, 0.76f), new Color(0.28f, 0.21f, 0.14f)),
        };

        public WrestlerLook(Wrestler wrestler, int skinCount)
        {
            uint hash = Hash(wrestler.Id + "|" + wrestler.Name.Full + "|" + wrestler.Country);
            Female = wrestler.Gender == Gender.Female;
            Skin = skinCount > 0 ? (int)(hash % (uint)skinCount) : 0;
            HeightMetres = wrestler.Body.HeightCm / 100f;
            WingspanMetres = wrestler.Body.WingspanCm / 100f;
            LegRatio = wrestler.Body.LegRatio.ToFloat();
            MassKg = wrestler.Body.NaturalWeightKg.ToFloat();

            var weights = new Dictionary<string, float>();
            float height = HeightMetres;
            float bmi = wrestler.Body.NaturalWeightKg.ToFloat() / (height * height);
            weights["Build_Heavy"] = Mathf.Clamp01((bmi - 24.5f) / 7f);
            weights["Build_Light"] = Mathf.Clamp01((22.5f - bmi) / 3.5f);
            float strength = (wrestler.Attributes[WrestlerAttribute.UpperBodyStrength] + wrestler.Attributes[WrestlerAttribute.LowerBodyStrength]) * 0.5f;
            weights["Build_Muscular"] = Mathf.Clamp01((strength - 45f) / 40f);
            weights["Build_Soft"] = Mathf.Clamp01((40f - strength) / 30f);

            var random = new HashRandom(hash);
            weights[FaceShapes[random.Next(FaceShapes.Length)]] = 0.3f + (0.6f * random.NextFloat());
            foreach (string feature in FaceFeatures)
            {
                // Each feature shows on about half the wrestlers, at varying strength.
                weights[feature] = random.NextFloat() < 0.5f ? 0.2f + (0.7f * random.NextFloat()) : 0f;
            }

            bool darkHairOnly = Skin == 1 || Skin == 2 || (!Female && Skin == 4);
            int hairChoices = darkHairOnly ? 3 : HairPalette.Length;
            (Color tint, Color scalp) = HairPalette[random.Next(hairChoices)];
            HairTint = tint;
            ScalpColor = scalp;

            ShapeWeights = weights;
        }

        public bool Female { get; }

        /// <summary>Index into the base model's skins.</summary>
        public int Skin { get; }

        public float HeightMetres { get; }

        public float WingspanMetres { get; }

        /// <summary>Hip-to-ankle segment length as a share of standing height.</summary>
        public float LegRatio { get; }

        public float MassKg { get; }

        /// <summary>Multiplier applied to the hair and eyebrow base textures.</summary>
        public Color HairTint { get; }

        /// <summary>Root colour used to shade the cranial scalp beneath the hair cap.</summary>
        public Color ScalpColor { get; }

        /// <summary>Blend-shape weights 0–1 by shape-key name.</summary>
        public IReadOnlyDictionary<string, float> ShapeWeights { get; }

        /// <summary>FNV-1a, stable across runs and platforms (unlike string.GetHashCode).</summary>
        private static uint Hash(string text)
        {
            uint hash = 2166136261u;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619u;
            }

            return hash;
        }

        /// <summary>Small xorshift sequence seeded by the identity hash (display only).</summary>
        private struct HashRandom
        {
            private uint _state;

            public HashRandom(uint seed) => _state = seed == 0 ? 0x9E3779B9u : seed;

            public uint NextUInt()
            {
                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
                return _state;
            }

            public int Next(int count) => (int)(NextUInt() % (uint)count);

            public float NextFloat() => (NextUInt() & 0xFFFFFF) / 16777216f;
        }
    }
}
