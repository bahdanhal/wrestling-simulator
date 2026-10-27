using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Wrestlers
{
    /// <summary>A descriptive label derived from attributes. It is shown to the player and has no mechanical effect.</summary>
    public enum Archetype
    {
        AllRounder,
        LegAttackSpecialist,
        Thrower,
        LiftSpecialist,
        ParTerreSpecialist,
        PressureWrestler,
        CounterWrestler,
    }

    /// <summary>
    /// Derives the archetype from weighted attribute profiles. The best-scoring profile wins when it
    /// stands out from the wrestler's average profile score by <see cref="SpecialistMargin"/>;
    /// otherwise the wrestler is an all-rounder. Leg attacks only count in Freestyle and Women's.
    /// </summary>
    public static class ArchetypeClassifier
    {
        /// <summary>Points (on the 1–100 scale) a profile must exceed the average profile by.</summary>
        public const int SpecialistMargin = 6;

        private static readonly IReadOnlyDictionary<Archetype, (WrestlerAttribute Attribute, int Weight)[]> Profiles =
            new Dictionary<Archetype, (WrestlerAttribute, int)[]>
            {
                [Archetype.LegAttackSpecialist] = new[] { (WrestlerAttribute.LegAttacks, 60), (WrestlerAttribute.Explosiveness, 20), (WrestlerAttribute.Speed, 20) },
                [Archetype.Thrower] = new[] { (WrestlerAttribute.UpperBodyThrows, 60), (WrestlerAttribute.Balance, 20), (WrestlerAttribute.Explosiveness, 20) },
                [Archetype.LiftSpecialist] = new[] { (WrestlerAttribute.BodyLockAndLifts, 60), (WrestlerAttribute.UpperBodyStrength, 25), (WrestlerAttribute.LowerBodyStrength, 15) },
                [Archetype.ParTerreSpecialist] = new[] { (WrestlerAttribute.ParTerreTurns, 35), (WrestlerAttribute.ParTerreLifts, 35), (WrestlerAttribute.UpperBodyStrength, 15), (WrestlerAttribute.GripStrength, 15) },
                [Archetype.PressureWrestler] = new[] { (WrestlerAttribute.WorkRate, 35), (WrestlerAttribute.AerobicEndurance, 35), (WrestlerAttribute.HandFighting, 30) },
                [Archetype.CounterWrestler] = new[] { (WrestlerAttribute.CounterWrestling, 50), (WrestlerAttribute.WrestlingIq, 30), (WrestlerAttribute.UpperBodyDefence, 20) },
            };

        public static Archetype Classify(AttributeSet attributes, WrestlingStyle style)
        {
            var scores = new List<(Archetype Archetype, int Score)>();
            foreach (KeyValuePair<Archetype, (WrestlerAttribute Attribute, int Weight)[]> profile in Profiles)
            {
                if (profile.Key == Archetype.LegAttackSpecialist && !style.UsesFreestyleRules())
                {
                    continue;
                }

                int score = profile.Value.Sum(p => attributes[p.Attribute] * p.Weight) / 100;
                scores.Add((profile.Key, score));
            }

            int average = scores.Sum(s => s.Score) / scores.Count;
            (Archetype best, int bestScore) = scores.OrderByDescending(s => s.Score).ThenBy(s => s.Archetype).First();
            return bestScore - average >= SpecialistMargin ? best : Archetype.AllRounder;
        }
    }
}
