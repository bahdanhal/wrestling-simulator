using System.Collections.Generic;
using UnityEngine;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;

namespace Wrestling.Presentation
{
    /// <summary>
    /// The engine's grips as physical holds: while a hand is in use for a grip (a collar tie, a wrist,
    /// an underhook, a body lock, a leg), that hand's palm is held on the part of the opponent's body
    /// under it by a <see cref="GripLink"/>, as strong as the grip is in the engine. So a collar tie
    /// pulls the opponent's head, a snap moves them, and the opponent's movement drags the arm. A hold
    /// is made when the palm reaches a part of the opponent's body the grip can be on (the head or
    /// upper back for a collar tie, an arm for wrist control), never whatever else the hand brushes on
    /// the way there, and torn off if the bodies are pulled apart past what a hand can keep. Display
    /// only: the engine decides every grip.
    /// </summary>
    public sealed class HandGrips
    {
        /// <summary>Hand-hold stiffness (N/m) and damping (N·s/m).</summary>
        private const float Spring = 2500f;
        private const float Damper = 60f;

        /// <summary>Most force (N) a full-strength one-handed hold carries, and the share a spent grip keeps.</summary>
        private const float FullHold = 650f;
        private const float SpentShare = 0.35f;

        /// <summary>The palm takes hold within this distance (m) of the opponent's surface, and loses it past the break distance.</summary>
        private const float ReachDistance = 0.06f;
        private const float BreakDistance = 0.3f;

        /// <summary>
        /// Engine ticks a hold is kept after the engine lets its grip go. A grip re-taken within this
        /// (the engine re-gripping in the same place) keeps the same hold, rather than the hand letting
        /// go and travelling back through the opponent's arms for a grip it never really lost.
        /// </summary>
        private const int GraceTicks = 15;

        private static readonly string[] Hands = { "LeftHand", "RightHand" };

        private readonly GripLink[] _links = new GripLink[4];
        private readonly Grip[] _grips = new Grip[4];
        private readonly int[] _lastGripped = new int[4];

        /// <summary>Holds currently made (0–4).</summary>
        public int Holding
        {
            get
            {
                int count = 0;
                foreach (GripLink link in _links)
                {
                    count += link != null && link.Holding ? 1 : 0;
                }

                return count;
            }
        }

        /// <summary>Hands the engine has in a grip now (0–4).</summary>
        public int Gripping { get; private set; }

        public void Clear()
        {
            for (int i = 0; i < _links.Length; i++)
            {
                _links[i] = null;
                _grips[i] = null;
            }

            Gripping = 0;
        }

        /// <summary>
        /// Once per frame: makes, keeps or drops each hand's hold to match the engine's grips.
        /// <paramref name="suspended"/> (a throw's own holds or recorded motion in charge) drops them all.
        /// </summary>
        public void Update(BoutEngine engine, PhysicalBody red, PhysicalBody blue, bool suspended)
        {
            Gripping = 0;
            for (int i = 0; i < _links.Length; i++)
            {
                var corner = (Corner)(i / 2);
                var side = (GripSide)(i % 2);
                Grip grip = suspended ? null : GripOn(engine, corner, side);
                if (grip == null)
                {
                    bool lingering = !suspended && _links[i] != null && _links[i].Holding && engine.Tick - _lastGripped[i] <= GraceTicks;
                    if (!lingering)
                    {
                        _links[i] = null;
                        _grips[i] = null;
                    }

                    continue;
                }

                Gripping++;
                _lastGripped[i] = engine.Tick;
                if (_links[i] != null && !ReferenceEquals(_grips[i], grip))
                {
                    if (_grips[i] != null && _grips[i].Type == grip.Type && _grips[i].Side == grip.Side)
                    {
                        // The same grip re-taken: the hand never let go.
                        _grips[i] = grip;
                    }
                    else
                    {
                        _links[i] = null;
                    }
                }

                if (_links[i] != null && !_links[i].Holding)
                {
                    _links[i] = null;
                }

                float hold = FullHold * Mathf.Lerp(SpentShare, 1f, grip.Strength.ToFloat());
                if (_links[i] != null)
                {
                    _links[i].SetDrive(Spring, Damper, hold);
                    continue;
                }

                PhysicalBody holder = corner == Corner.Red ? red : blue;
                PhysicalBody held = corner == Corner.Red ? blue : red;
                string handName = Hands[(int)side];
                ArticulationBody hand = holder.Link(handName);
                Vector3 palm = holder.PalmAnchor(handName);
                Vector3 palmWorld = hand.transform.TransformPoint(palm);
                (ArticulationBody body, Vector3 point, float distance) = held.Nearest(palmWorld, HeldParts(grip, side));
                if (distance <= ReachDistance)
                {
                    _links[i] = new GripLink(hand, palm, body, body.transform.InverseTransformPoint(point), Spring, Damper, hold, BreakDistance);
                    _grips[i] = grip;
                }
            }
        }

        /// <summary>The point a hand is holding on the opponent's physical body, or null when it holds nothing.</summary>
        public Vector3? HeldPoint(Corner corner, GripSide side)
        {
            GripLink link = _links[((int)corner * 2) + (int)side];
            return link != null && link.Holding ? link.HeldPoint : (Vector3?)null;
        }

        /// <summary>Each physics substep: every hold pulls palm and held point together.</summary>
        public void Apply(float deltaTime)
        {
            foreach (GripLink link in _links)
            {
                link?.Apply(deltaTime);
            }
        }

        /// <summary>
        /// The opponent's segments a hand may hold for a grip: the head and upper back for a neck grip,
        /// the trunk (and the arm it goes under or over) for a body grip, the arms for an arm grip, the
        /// legs for a leg grip. A neck or body grip never takes hold of an arm the hand brushes on its
        /// way, which would tie the two wrestlers' arms together.
        /// </summary>
        private static IReadOnlyList<string> HeldParts(Grip grip, GripSide hand)
        {
            bool armHand = hand != grip.Side;
            switch (grip.Type)
            {
                case GripType.CollarTie:
                case GripType.FrontHeadlock:
                    return NeckParts;
                case GripType.WristControl:
                case GripType.TwoOnOne:
                    return ArmParts;
                case GripType.Underhook:
                case GripType.Overhook:
                    return grip.Side == GripSide.Left ? HookRightParts : HookLeftParts;
                case GripType.BodyLock:
                case GripType.GutWrenchLock:
                case GripType.WaistFromBehind:
                    return TrunkParts;
                case GripType.HeadAndArm:
                    // One hand round the neck, the other on an arm.
                    return armHand ? ArmParts : NeckParts;
                case GripType.Crotch:
                    return CrotchParts;
                default:
                    return LegParts;
            }
        }

        private static readonly string[] NeckParts = { "Head", "Spine2" };
        private static readonly string[] TrunkParts = { "Hips", "Spine", "Spine2" };
        private static readonly string[] ArmParts = { "LeftHand", "LeftForeArm", "LeftArm", "RightHand", "RightForeArm", "RightArm" };

        // An underhook or overhook goes under or over the opponent's arm on the facing side (a left
        // hook works on the right arm) and ends on their back or shoulder.
        private static readonly string[] HookRightParts = { "Spine", "Spine2", "RightArm" };
        private static readonly string[] HookLeftParts = { "Spine", "Spine2", "LeftArm" };
        private static readonly string[] CrotchParts = { "Hips", "LeftUpLeg", "RightUpLeg" };
        private static readonly string[] LegParts = { "Hips", "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot" };

        private static Grip GripOn(BoutEngine engine, Corner corner, GripSide side)
        {
            foreach (Grip grip in engine.Grips.Of(corner))
            {
                if (grip.UsesHand(side))
                {
                    return grip;
                }
            }

            return null;
        }
    }
}
