using System;
using Wrestling.Core.Numerics;

namespace Wrestling.Core.Match
{
    public enum Stance
    {
        Square,
        StaggeredLeftLead,
        StaggeredRightLead,
    }

    /// <summary>How the wrestler is supported (design section 5.3).</summary>
    public enum GroundState
    {
        Standing,
        OneKnee,
        BothKnees,
        Seated,

        /// <summary>Lying chest-down.</summary>
        Prone,

        /// <summary>Lying on the back.</summary>
        Supine,

        /// <summary>Back off the mat, supported on head and feet.</summary>
        Bridge,
    }

    /// <summary>
    /// Physical state of one wrestler, updated every tick. Continuous values are clamped to their
    /// ranges when set, so small overshoots in the simulation never produce invalid states.
    /// </summary>
    public sealed class BodyState
    {
        private static readonly Fix64 MinimumBaseWidth = Fix64.Parse("0.15");
        private static readonly Fix64 MaximumBaseWidth = Fix64.Parse("1.2");
        private static readonly Fix64 StaggerFraction = Fix64.Parse("0.35");
        private static readonly Fix64 NinetyDegrees = Fix64.FromInt(90);
        private static readonly Fix64 HalfTurnDegrees = Fix64.FromInt(180);

        private FixVector2 _facing = FixVector2.UnitX;
        private Fix64 _level = Fix64.One;
        private Fix64 _posture = Fix64.One;
        private Fix64 _balance = Fix64.One;
        private Fix64 _baseWidth = Fix64.Parse("0.5");
        private Fix64 _torsoRotation;

        /// <summary>Centre of mass projected on the mat, metres from the centre.</summary>
        public FixVector2 Position { get; set; }

        /// <summary>Unit vector the chest faces. Setting normalises the value.</summary>
        public FixVector2 Facing
        {
            get => _facing;
            set
            {
                FixVector2 normalised = value.Normalized();
                _facing = normalised == FixVector2.Zero ? throw new ArgumentException("Facing cannot be the zero vector.") : normalised;
            }
        }

        /// <summary>Carried velocity in metres per second.</summary>
        public FixVector2 Momentum { get; set; }

        public Stance Stance { get; set; } = Stance.Square;

        /// <summary>Hip and head height: 0 flat to the mat, 1 fully upright.</summary>
        public Fix64 Level
        {
            get => _level;
            set => _level = Fix64.Clamp(value, Fix64.Zero, Fix64.One);
        }

        /// <summary>Upper body: 0 bent over, 1 upright.</summary>
        public Fix64 Posture
        {
            get => _posture;
            set => _posture = Fix64.Clamp(value, Fix64.Zero, Fix64.One);
        }

        /// <summary>Centre of mass over the base: 1 stable, 0 falling.</summary>
        public Fix64 Balance
        {
            get => _balance;
            set => _balance = Fix64.Clamp(value, Fix64.Zero, Fix64.One);
        }

        /// <summary>Distance between the feet in metres.</summary>
        public Fix64 BaseWidth
        {
            get => _baseWidth;
            set => _baseWidth = Fix64.Clamp(value, MinimumBaseWidth, MaximumBaseWidth);
        }

        public GroundState Ground { get; set; } = GroundState.Standing;

        /// <summary>
        /// Rotation of the torso relative to the mat: 0° chest flat down, 90° on the side (the
        /// "neutral point"), 180° flat on the back. Only meaningful when not standing.
        /// </summary>
        public Fix64 TorsoRotationDegrees
        {
            get => _torsoRotation;
            set => _torsoRotation = Fix64.Clamp(value, Fix64.Zero, HalfTurnDegrees);
        }

        public bool LeftShoulderOnMat { get; set; }

        public bool RightShoulderOnMat { get; set; }

        /// <summary>
        /// Par terre for scoring purposes: at least one knee (or more of the body) on the mat (Art. 34).
        /// </summary>
        public bool IsGrounded => Ground != GroundState.Standing;

        /// <summary>
        /// Danger position (Art. 35): the back or shoulder line below 90° to the mat with the back
        /// toward it. Exactly 90° is the neutral point and not danger.
        /// </summary>
        public bool IsInDangerPosition => IsGrounded && TorsoRotationDegrees > NinetyDegrees;

        public bool BothShouldersOnMat => LeftShoulderOnMat && RightShoulderOnMat;

        /// <summary>Foot positions from the centre of mass, facing, stance and base width.</summary>
        public (FixVector2 Left, FixVector2 Right) FootPositions()
        {
            FixVector2 sideways = Facing.Perpendicular() * (BaseWidth / Fix64.FromInt(2)); // towards the left foot
            FixVector2 stagger = Facing * (BaseWidth * StaggerFraction);
            FixVector2 left = Position + sideways;
            FixVector2 right = Position - sideways;
            switch (Stance)
            {
                case Stance.StaggeredLeftLead:
                    left += stagger;
                    right -= stagger;
                    break;
                case Stance.StaggeredRightLead:
                    right += stagger;
                    left -= stagger;
                    break;
            }

            return (left, right);
        }

        public BodyState Clone() => (BodyState)MemberwiseClone();
    }
}
