using System;
using System.Collections.Generic;
using Wrestling.Core.Numerics;

namespace Wrestling.Core.Rules
{
    /// <summary>A weight category: wrestlers weigh at most <see cref="LimitKg"/> (and more than <see cref="MinimumKgExclusive"/> when set).</summary>
    public sealed class WeightCategory : IEquatable<WeightCategory>
    {
        public WeightCategory(int limitKg, int? minimumKgExclusive, int? minimumKgInclusive)
        {
            LimitKg = limitKg;
            MinimumKgExclusive = minimumKgExclusive;
            MinimumKgInclusive = minimumKgInclusive;
        }

        public int LimitKg { get; }

        /// <summary>Heavyweight entry condition: body weight must be strictly above this value.</summary>
        public int? MinimumKgExclusive { get; }

        /// <summary>Lightest youth categories (for example 34–38 kg): body weight must be at least this value.</summary>
        public int? MinimumKgInclusive { get; }

        /// <summary>Display label in the UWW style, for example "65 kg" or "34-38 kg".</summary>
        public string Label => MinimumKgInclusive.HasValue ? $"{MinimumKgInclusive.Value}-{LimitKg} kg" : $"{LimitKg} kg";

        /// <summary>Whether a body weight (with any allowed tolerance already added to the limit) fits this category.</summary>
        public bool Accepts(Fix64 bodyWeightKg, Fix64 toleranceKg)
        {
            if (bodyWeightKg > Fix64.FromInt(LimitKg) + toleranceKg)
            {
                return false;
            }

            if (MinimumKgExclusive.HasValue && bodyWeightKg <= Fix64.FromInt(MinimumKgExclusive.Value))
            {
                return false;
            }

            return !MinimumKgInclusive.HasValue || bodyWeightKg >= Fix64.FromInt(MinimumKgInclusive.Value);
        }

        public bool Equals(WeightCategory? other) =>
            other != null && LimitKg == other.LimitKg && MinimumKgExclusive == other.MinimumKgExclusive && MinimumKgInclusive == other.MinimumKgInclusive;

        public override bool Equals(object? obj) => obj is WeightCategory other && Equals(other);

        public override int GetHashCode() => (LimitKg * 397) ^ (MinimumKgExclusive ?? -1) ^ ((MinimumKgInclusive ?? -1) << 16);

        public override string ToString() => Label;
    }

    /// <summary>Weight category list for one style and age group as written in content files.</summary>
    public sealed class WeightCategoryList
    {
        /// <summary>Upper limits in kg, lightest first.</summary>
        public IReadOnlyList<int> LimitsKg { get; init; } = Array.Empty<int>();

        /// <summary>Minimum weight of the lightest category, when the rulebook gives a range (U15, U17).</summary>
        public int? LightestMinimumKg { get; init; }

        /// <summary>The heaviest category requires a body weight strictly above this value (Art. 7), when set.</summary>
        public int? HeaviestMinimumKgExclusive { get; init; }

        /// <summary>Categories lightest first.</summary>
        public IReadOnlyList<WeightCategory> Categories
        {
            get
            {
                var categories = new List<WeightCategory>(LimitsKg.Count);
                for (int i = 0; i < LimitsKg.Count; i++)
                {
                    bool lightest = i == 0;
                    bool heaviest = i == LimitsKg.Count - 1;
                    categories.Add(new WeightCategory(
                        LimitsKg[i],
                        heaviest ? HeaviestMinimumKgExclusive : null,
                        lightest ? LightestMinimumKg : null));
                }

                return categories;
            }
        }

        public WeightCategory Find(int limitKg)
        {
            foreach (WeightCategory category in Categories)
            {
                if (category.LimitKg == limitKg)
                {
                    return category;
                }
            }

            throw new ArgumentException($"No {limitKg} kg category in this list.", nameof(limitKg));
        }

        internal void Validate(ValidationContext context, string path)
        {
            if (LimitsKg.Count == 0)
            {
                context.Error(path + ".limitsKg must not be empty.");
                return;
            }

            for (int i = 0; i < LimitsKg.Count; i++)
            {
                context.RequireRange(LimitsKg[i], 1, 300, path + ".limitsKg[]");
                if (i > 0 && LimitsKg[i] <= LimitsKg[i - 1])
                {
                    context.Error(path + ".limitsKg must be strictly increasing.");
                }
            }

            if (LightestMinimumKg.HasValue && LightestMinimumKg.Value >= LimitsKg[0])
            {
                context.Error(path + ".lightestMinimumKg must be below the lightest limit.");
            }

            if (HeaviestMinimumKgExclusive.HasValue && HeaviestMinimumKgExclusive.Value >= LimitsKg[LimitsKg.Count - 1])
            {
                context.Error(path + ".heaviestMinimumKgExclusive must be below the heaviest limit.");
            }
        }
    }

    /// <summary>All weight categories of a ruleset (Art. 7).</summary>
    public sealed class WeightCategoryRules
    {
        /// <summary>Senior, U23 and U20 categories.</summary>
        public StyleValues<WeightCategoryList> Senior { get; init; } = new StyleValues<WeightCategoryList>();

        public StyleValues<WeightCategoryList> U17 { get; init; } = new StyleValues<WeightCategoryList>();

        public StyleValues<WeightCategoryList> U15 { get; init; } = new StyleValues<WeightCategoryList>();

        /// <summary>Olympic Games and Olympic qualifiers.</summary>
        public StyleValues<WeightCategoryList> Olympic { get; init; } = new StyleValues<WeightCategoryList>();

        /// <summary>
        /// Senior and U23 wrestlers may enter the next category up from their body weight
        /// (except into heavyweight without meeting its minimum).
        /// </summary>
        public IReadOnlyList<AgeCategory> MayMoveUpOneCategory { get; init; } = Array.Empty<AgeCategory>();

        /// <summary>Categories for an age group. Veterans categories are set by separate regulations and are not part of these rules.</summary>
        public WeightCategoryList For(AgeCategory age, WrestlingStyle style) => age switch
        {
            AgeCategory.U15 => U15.For(style),
            AgeCategory.U17 => U17.For(style),
            AgeCategory.U20 or AgeCategory.U23 or AgeCategory.Senior => Senior.For(style),
            _ => throw new ArgumentOutOfRangeException(nameof(age), age, "No weight categories defined for this age group."),
        };

        internal void Validate(ValidationContext context)
        {
            ValidateStyles(context, Senior, "weightCategories.senior");
            ValidateStyles(context, U17, "weightCategories.u17");
            ValidateStyles(context, U15, "weightCategories.u15");
            ValidateStyles(context, Olympic, "weightCategories.olympic");
        }

        private static void ValidateStyles(ValidationContext context, StyleValues<WeightCategoryList> lists, string path)
        {
            context.RequireNotNull(lists.GrecoRoman, path + ".grecoRoman")?.Validate(context, path + ".grecoRoman");
            context.RequireNotNull(lists.Freestyle, path + ".freestyle")?.Validate(context, path + ".freestyle");
            context.RequireNotNull(lists.Womens, path + ".womens")?.Validate(context, path + ".womens");
        }
    }
}
