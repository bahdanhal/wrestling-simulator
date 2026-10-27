using System;
using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Match
{
    /// <summary>Grip and tie types (design section 5.4).</summary>
    public enum GripType
    {
        CollarTie,
        WristControl,
        TwoOnOne,
        Underhook,
        Overhook,

        /// <summary>Double underhooks locked around the torso.</summary>
        BodyLock,
        FrontHeadlock,
        HeadAndArm,
        WaistFromBehind,
        GutWrenchLock,
        SingleLeg,
        DoubleLeg,
        Ankle,
        Crotch,

        /// <summary>Ankle lace in par terre; uses the legs as well as the arms.</summary>
        LegLace,
    }

    public enum GripSide
    {
        Left,
        Right,
    }

    public static class GripRules
    {
        /// <summary>Hands a grip occupies: one-handed grips use the hand on their side, two-handed grips both.</summary>
        public static int HandsUsed(GripType type) => type switch
        {
            GripType.CollarTie or GripType.WristControl or GripType.Underhook or GripType.Overhook or GripType.Ankle or GripType.Crotch => 1,
            _ => 2,
        };

        /// <summary>Grips below the belt line or using the legs, forbidden in Greco-Roman (Art. 51).</summary>
        public static bool IsBelowBeltOrLegs(GripType type) =>
            type == GripType.SingleLeg || type == GripType.DoubleLeg || type == GripType.Ankle || type == GripType.Crotch || type == GripType.LegLace;

        public static bool IsLegal(GripType type, WrestlingStyle style) => style.UsesFreestyleRules() || !IsBelowBeltOrLegs(type);
    }

    /// <summary>One wrestler's hold on the other.</summary>
    public sealed class Grip
    {
        private Fix64 _quality;
        private Fix64 _strength;

        internal Grip(Corner owner, GripType type, GripSide side, Fix64 quality, Fix64 strength, int sinceTick)
        {
            Owner = owner;
            Type = type;
            Side = side;
            Quality = quality;
            Strength = strength;
            SinceTick = sinceTick;
        }

        public Corner Owner { get; }

        public GripType Type { get; }

        /// <summary>The gripping arm for one-handed grips; the attacked side for two-handed grips.</summary>
        public GripSide Side { get; }

        /// <summary>How well placed the grip is, 0–1.</summary>
        public Fix64 Quality
        {
            get => _quality;
            set => _quality = Fix64.Clamp(value, Fix64.Zero, Fix64.One);
        }

        /// <summary>Current holding strength, 0–1; decays with grip fatigue.</summary>
        public Fix64 Strength
        {
            get => _strength;
            set => _strength = Fix64.Clamp(value, Fix64.Zero, Fix64.One);
        }

        /// <summary>Simulation tick when the grip was gained.</summary>
        public int SinceTick { get; }

        public bool UsesHand(GripSide hand) => GripRules.HandsUsed(Type) == 2 || Side == hand;

        public override string ToString() => $"{Owner} {Type} ({Side})";
    }

    /// <summary>All grips both wrestlers hold. Enforces hand availability and style legality.</summary>
    public sealed class GripSet
    {
        private readonly List<Grip> _grips = new List<Grip>();
        private readonly WrestlingStyle _style;

        public GripSet(WrestlingStyle style)
        {
            _style = style;
        }

        public IReadOnlyList<Grip> All => _grips;

        public IEnumerable<Grip> Of(Corner owner) => _grips.Where(g => g.Owner == owner);

        public bool Has(Corner owner, GripType type) => _grips.Any(g => g.Owner == owner && g.Type == type);

        public Grip? Find(Corner owner, GripType type) => _grips.FirstOrDefault(g => g.Owner == owner && g.Type == type);

        public bool IsHandFree(Corner owner, GripSide hand) => !_grips.Any(g => g.Owner == owner && g.UsesHand(hand));

        /// <summary>Whether a grip can be taken now; <paramref name="reason"/> explains a refusal.</summary>
        public bool CanTake(Corner owner, GripType type, GripSide side, out string reason)
        {
            if (!GripRules.IsLegal(type, _style))
            {
                reason = $"{type} is illegal in {_style}.";
                return false;
            }

            bool handsFree = GripRules.HandsUsed(type) == 2
                ? IsHandFree(owner, GripSide.Left) && IsHandFree(owner, GripSide.Right)
                : IsHandFree(owner, side);
            if (!handsFree)
            {
                reason = "The hands needed for this grip are in use.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public Grip Take(Corner owner, GripType type, GripSide side, Fix64 quality, Fix64 strength, int tick)
        {
            if (!CanTake(owner, type, side, out string reason))
            {
                throw new InvalidOperationException(reason);
            }

            var grip = new Grip(owner, type, side, quality, strength, tick);
            _grips.Add(grip);
            return grip;
        }

        public void Release(Grip grip)
        {
            if (!_grips.Remove(grip))
            {
                throw new ArgumentException("Grip is not held.", nameof(grip));
            }
        }

        public void ReleaseAll(Corner owner) => _grips.RemoveAll(g => g.Owner == owner);

        public void Clear() => _grips.Clear();
    }
}
