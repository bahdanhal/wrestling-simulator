using System;
using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Competition
{
    /// <summary>Event types with different weigh-in tolerances (Art. 8).</summary>
    public enum EventType
    {
        OlympicGames,

        /// <summary>World and continental championships.</summary>
        Championship,

        WorldCup,

        RankingSeries,

        InternationalTournament,
    }

    public enum WeighInDay
    {
        /// <summary>Morning of the category's first day, with medical control.</summary>
        First,

        /// <summary>Second morning: finalists and repechage wrestlers only, no tolerance.</summary>
        Second,
    }

    public enum WeighInOutcome
    {
        Passed,

        /// <summary>Heavier than the category limit plus tolerance.</summary>
        Overweight,

        /// <summary>Too light for the category: below its minimum, or more than one category up (two for senior/U23).</summary>
        TooLight,
    }

    /// <summary>Weigh-in and category eligibility (Art. 7, 8, 11).</summary>
    public static class WeighIn
    {
        public static Fix64 ToleranceKg(Ruleset rules, EventType eventType, WeighInDay day)
        {
            if (day == WeighInDay.Second)
            {
                return Fix64.FromInt(rules.WeighIn.SecondDayToleranceKg);
            }

            WeighInTolerance t = rules.WeighIn.FirstDayToleranceKg;
            int kg = eventType switch
            {
                EventType.OlympicGames => t.OlympicGames,
                EventType.Championship => t.Championships,
                EventType.WorldCup => t.WorldCup,
                EventType.RankingSeries => t.RankingSeries,
                EventType.InternationalTournament => t.InternationalTournament,
                _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, null),
            };
            return Fix64.FromInt(kg);
        }

        /// <summary>
        /// The category matching a body weight without tolerance: the lightest whose limit is not
        /// exceeded. Null when heavier than every limit or below the lightest category's minimum.
        /// </summary>
        public static WeightCategory? NaturalCategory(WeightCategoryList categories, Fix64 bodyWeightKg)
        {
            foreach (WeightCategory category in categories.Categories)
            {
                if (bodyWeightKg <= Fix64.FromInt(category.LimitKg))
                {
                    return category.Accepts(bodyWeightKg, Fix64.Zero) ? category : null;
                }
            }

            return null;
        }

        /// <summary>Categories a wrestler may enter at this body weight: the natural one, plus the next one up for age groups allowed to move up.</summary>
        public static IReadOnlyList<WeightCategory> EligibleCategories(Ruleset rules, WeightCategoryList categories, AgeCategory age, Fix64 bodyWeightKg)
        {
            IReadOnlyList<WeightCategory> all = categories.Categories;
            var eligible = new List<WeightCategory>();
            for (int i = 0; i < all.Count; i++)
            {
                if (LowerBoundSatisfied(rules, all, i, age, bodyWeightKg) && bodyWeightKg <= Fix64.FromInt(all[i].LimitKg))
                {
                    eligible.Add(all[i]);
                }
            }

            return eligible;
        }

        /// <summary>Checks a wrestler's weight for the category they are registered in.</summary>
        public static WeighInOutcome Check(
            Ruleset rules,
            WeightCategoryList categories,
            int registeredLimitKg,
            AgeCategory age,
            Fix64 bodyWeightKg,
            EventType eventType,
            WeighInDay day)
        {
            IReadOnlyList<WeightCategory> all = categories.Categories;
            int index = all.ToList().FindIndex(c => c.LimitKg == registeredLimitKg);
            if (index < 0)
            {
                throw new ArgumentException($"No {registeredLimitKg} kg category in this list.", nameof(registeredLimitKg));
            }

            if (bodyWeightKg > Fix64.FromInt(registeredLimitKg) + ToleranceKg(rules, eventType, day))
            {
                return WeighInOutcome.Overweight;
            }

            return LowerBoundSatisfied(rules, all, index, age, bodyWeightKg) ? WeighInOutcome.Passed : WeighInOutcome.TooLight;
        }

        /// <summary>
        /// A wrestler belongs in the category matching their weight; senior and U23 wrestlers may
        /// go one category higher. The heavyweight and the lightest youth categories have their own minimums.
        /// </summary>
        private static bool LowerBoundSatisfied(Ruleset rules, IReadOnlyList<WeightCategory> all, int index, AgeCategory age, Fix64 bodyWeightKg)
        {
            WeightCategory category = all[index];
            if (category.MinimumKgExclusive.HasValue && bodyWeightKg <= Fix64.FromInt(category.MinimumKgExclusive.Value))
            {
                return false;
            }

            if (category.MinimumKgInclusive.HasValue && bodyWeightKg < Fix64.FromInt(category.MinimumKgInclusive.Value))
            {
                return false;
            }

            int stepsDown = rules.WeightCategories.MayMoveUpOneCategory.Contains(age) ? 2 : 1;
            int floorIndex = index - stepsDown;
            return floorIndex < 0 || bodyWeightKg > Fix64.FromInt(all[floorIndex].LimitKg);
        }
    }
}
