using UnityEngine;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Skin and hair colours for a wrestler, chosen from fixed palettes by a stable hash of their
    /// identity, so the same wrestler always looks the same.
    /// </summary>
    public readonly struct Appearance
    {
        private static readonly Color[] SkinTones =
        {
            new Color(0.95f, 0.78f, 0.66f),
            new Color(0.89f, 0.69f, 0.55f),
            new Color(0.80f, 0.59f, 0.44f),
            new Color(0.69f, 0.48f, 0.34f),
            new Color(0.55f, 0.37f, 0.26f),
            new Color(0.40f, 0.27f, 0.19f),
        };

        private static readonly Color[] HairColours =
        {
            new Color(0.06f, 0.05f, 0.05f),
            new Color(0.14f, 0.09f, 0.06f),
            new Color(0.25f, 0.16f, 0.09f),
            new Color(0.42f, 0.29f, 0.16f),
            new Color(0.62f, 0.48f, 0.30f),
        };

        public Appearance(Wrestler wrestler)
        {
            uint hash = Hash(wrestler.Id + "|" + wrestler.Name.Full + "|" + wrestler.Country);
            Skin = SkinTones[hash % (uint)SkinTones.Length];
            Hair = HairColours[(hash / 7u) % (uint)HairColours.Length];
        }

        public Color Skin { get; }

        public Color Hair { get; }

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
    }
}
