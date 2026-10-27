using System;
using System.Collections.Generic;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Wrestlers
{
    public enum AttributeGroup
    {
        Physical,
        TechnicalAttack,
        TechnicalDefence,
        Mental,
    }

    /// <summary>Trainable wrestler attributes, 1–100 (design section 4.2).</summary>
    public enum WrestlerAttribute
    {
        // Physical
        UpperBodyStrength,
        LowerBodyStrength,
        GripStrength,
        Explosiveness,
        Speed,
        Balance,
        Agility,
        Flexibility,
        AerobicEndurance,
        AnaerobicCapacity,
        Recovery,
        Durability,

        // Technical: attack families
        LegAttacks,
        TripsAndSweeps,
        UpperBodyThrows,
        BodyLockAndLifts,
        GoBehindsAndDrags,
        SnapDownsAndFrontHeadlock,
        ParTerreTurns,
        ParTerreLifts,

        // Technical: defence and position
        HandFighting,
        LegDefence,
        UpperBodyDefence,
        CounterWrestling,
        ParTerreBottomDefence,
        BridgingAndDangerDefence,
        MatAwareness,

        // Mental
        Composure,
        Aggression,
        Discipline,
        WorkRate,
        WrestlingIq,
        Determination,
        Clutch,
    }

    /// <summary>Families of attacking techniques; each has a matching family rating attribute.</summary>
    public enum TechniqueFamily
    {
        LegAttacks,
        TripsAndSweeps,
        UpperBodyThrows,
        BodyLockAndLifts,
        GoBehindsAndDrags,
        SnapDownsAndFrontHeadlock,
        ParTerreTurns,
        ParTerreLifts,
    }

    public static class WrestlerAttributes
    {
        public const int Minimum = 1;
        public const int Maximum = 100;

        public static readonly IReadOnlyList<WrestlerAttribute> All = (WrestlerAttribute[])Enum.GetValues(typeof(WrestlerAttribute));

        public static int Count => All.Count;

        public static AttributeGroup GroupOf(WrestlerAttribute attribute) => attribute switch
        {
            <= WrestlerAttribute.Durability => AttributeGroup.Physical,
            <= WrestlerAttribute.ParTerreLifts => AttributeGroup.TechnicalAttack,
            <= WrestlerAttribute.MatAwareness => AttributeGroup.TechnicalDefence,
            _ => AttributeGroup.Mental,
        };

        /// <summary>The family rating attribute of a technique family.</summary>
        public static WrestlerAttribute RatingOf(TechniqueFamily family) => family switch
        {
            TechniqueFamily.LegAttacks => WrestlerAttribute.LegAttacks,
            TechniqueFamily.TripsAndSweeps => WrestlerAttribute.TripsAndSweeps,
            TechniqueFamily.UpperBodyThrows => WrestlerAttribute.UpperBodyThrows,
            TechniqueFamily.BodyLockAndLifts => WrestlerAttribute.BodyLockAndLifts,
            TechniqueFamily.GoBehindsAndDrags => WrestlerAttribute.GoBehindsAndDrags,
            TechniqueFamily.SnapDownsAndFrontHeadlock => WrestlerAttribute.SnapDownsAndFrontHeadlock,
            TechniqueFamily.ParTerreTurns => WrestlerAttribute.ParTerreTurns,
            TechniqueFamily.ParTerreLifts => WrestlerAttribute.ParTerreLifts,
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, null),
        };

        /// <summary>
        /// Whether a technique family can be used in a style at all. Greco-Roman forbids attacking
        /// the legs and tripping (Art. 51); individual techniques may add further restrictions.
        /// </summary>
        public static bool IsLegal(TechniqueFamily family, WrestlingStyle style) =>
            style.UsesFreestyleRules() || (family != TechniqueFamily.LegAttacks && family != TechniqueFamily.TripsAndSweeps);
    }

    /// <summary>A complete set of attribute values, one per <see cref="WrestlerAttribute"/>.</summary>
    public sealed class AttributeSet
    {
        private readonly int[] _values;

        public AttributeSet(int initialValue = WrestlerAttributes.Minimum)
        {
            Check(initialValue);
            _values = new int[WrestlerAttributes.Count];
            for (int i = 0; i < _values.Length; i++)
            {
                _values[i] = initialValue;
            }
        }

        public AttributeSet(IReadOnlyDictionary<WrestlerAttribute, int> values)
            : this()
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            foreach (WrestlerAttribute attribute in WrestlerAttributes.All)
            {
                if (!values.TryGetValue(attribute, out int value))
                {
                    throw new ArgumentException($"Attribute {attribute} is missing.", nameof(values));
                }

                this[attribute] = value;
            }
        }

        public int this[WrestlerAttribute attribute]
        {
            get => _values[(int)attribute];
            set
            {
                Check(value);
                _values[(int)attribute] = value;
            }
        }

        public AttributeSet Clone()
        {
            var copy = new AttributeSet();
            Array.Copy(_values, copy._values, _values.Length);
            return copy;
        }

        public IReadOnlyDictionary<WrestlerAttribute, int> ToDictionary()
        {
            var result = new Dictionary<WrestlerAttribute, int>();
            foreach (WrestlerAttribute attribute in WrestlerAttributes.All)
            {
                result[attribute] = this[attribute];
            }

            return result;
        }

        private static void Check(int value)
        {
            if (value < WrestlerAttributes.Minimum || value > WrestlerAttributes.Maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, $"Attributes range from {WrestlerAttributes.Minimum} to {WrestlerAttributes.Maximum}.");
            }
        }
    }
}
