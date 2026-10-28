using System.Collections.Generic;
using UnityEngine;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Presentation
{
    /// <summary>
    /// How a wrestler looks: which base model and skin, the blend-shape weights for build and
    /// face, and the overall scale. Build comes from the wrestler's body and strength; skin and
    /// face come from a stable hash of their identity, so a wrestler always looks the same.
    /// </summary>
    public sealed class WrestlerLook
    {
        private static readonly string[] FaceShapes = { "Face_Square", "Face_Round", "Face_Oval" };

        private static readonly string[] FaceFeatures =
        {
            "Nose_Large", "Nose_Hump", "Nose_Broken", "Chin_Prominent", "Jaw_Wide", "Cheekbones_High", "Ears_Out",
        };

        public WrestlerLook(Wrestler wrestler, int skinCount)
        {
            uint hash = Hash(wrestler.Id + "|" + wrestler.Name.Full + "|" + wrestler.Country);
            Female = wrestler.Gender == Gender.Female;
            Skin = skinCount > 0 ? (int)(hash % (uint)skinCount) : 0;
            HeightMetres = wrestler.Body.HeightCm / 100f;
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

            ShapeWeights = weights;
        }

        public bool Female { get; }

        /// <summary>Index into the base model's skins.</summary>
        public int Skin { get; }

        public float HeightMetres { get; }

        public float MassKg { get; }

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
