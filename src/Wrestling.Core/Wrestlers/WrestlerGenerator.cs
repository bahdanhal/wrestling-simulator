using System;
using System.Collections.Generic;
using Wrestling.Core.Calendar;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;

namespace Wrestling.Core.Wrestlers
{
    /// <summary>Fictional names to draw generated wrestlers from (<c>data/names/*.json</c>).</summary>
    public sealed class NamePool
    {
        public int SchemaVersion { get; init; }

        public string Id { get; init; } = string.Empty;

        public IReadOnlyList<string> Male { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> Female { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> Family { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (Male.Count == 0 || Female.Count == 0 || Family.Count == 0)
            {
                errors.Add("Name pool needs at least one male, female and family name.");
            }

            foreach (string name in Concat())
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    errors.Add("Name pool contains an empty name.");
                    break;
                }
            }

            return errors;
        }

        private IEnumerable<string> Concat()
        {
            foreach (string name in Male)
            {
                yield return name;
            }

            foreach (string name in Female)
            {
                yield return name;
            }

            foreach (string name in Family)
            {
                yield return name;
            }
        }
    }

    /// <summary>
    /// Generates fictional opponents: attributes spread around a level, the style's full senior
    /// repertoire at proficiencies around that level, and a body within the normal adult range.
    /// Deterministic for a given random state.
    /// </summary>
    public static class WrestlerGenerator
    {
        private const int MaximumGeneratedAttribute = 95;

        public static Wrestler Generate(string id, WrestlingStyle style, int level, int spread, TechniqueCatalog catalog, NamePool names, Pcg32 random)
        {
            if (level < 1 || level > MaximumGeneratedAttribute)
            {
                throw new ArgumentOutOfRangeException(nameof(level), level, $"Level must be 1–{MaximumGeneratedAttribute}.");
            }

            if (spread < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(spread), spread, "Spread cannot be negative.");
            }

            Gender gender = style == WrestlingStyle.Womens ? Gender.Female : Gender.Male;
            var attributes = new AttributeSet();
            foreach (WrestlerAttribute attribute in WrestlerAttributes.All)
            {
                attributes[attribute] = Math.Clamp(level + random.NextInt(-spread, spread + 1), 1, MaximumGeneratedAttribute);
            }

            IReadOnlyList<string> given = gender == Gender.Female ? names.Female : names.Male;
            var wrestler = new Wrestler
            {
                Id = id,
                Name = new PersonName
                {
                    Given = given[random.NextInt(0, given.Count)],
                    Family = names.Family[random.NextInt(0, names.Family.Count)],
                },
                Country = "SIM",
                Gender = gender,
                Style = style,
                BirthDate = GameDate.Create(2000, 1, 1),
                Body = new BodyProfile
                {
                    HeightCm = 170 + random.NextInt(0, 15),
                    WingspanCm = 172 + random.NextInt(0, 16),
                    LegRatio = Fix64.FromRatio(46 + random.NextInt(0, 4), 100),
                    Frame = Frame.Medium,
                    NaturalWeightKg = Fix64.FromInt(74),
                    DominantSide = random.NextUInt(2) == 0 ? Side.Left : Side.Right,
                },
                Attributes = attributes,
                Hidden = new HiddenTraits { Potential = new AttributeSet(WrestlerAttributes.Maximum) },
                Condition = new Condition { WalkAroundWeightKg = Fix64.FromInt(76) },
            };

            foreach (TechniqueDefinition technique in catalog.AllowedFor(style, AgeCategory.Senior))
            {
                wrestler.Repertoire.Add(new RepertoireEntry
                {
                    TechniqueId = technique.Id,
                    Proficiency = Math.Clamp(level - 20 + random.NextInt(0, 41), 1, 100),
                    Side = TechniqueSide.Both,
                });
            }

            return wrestler.EnsureValid();
        }
    }
}
