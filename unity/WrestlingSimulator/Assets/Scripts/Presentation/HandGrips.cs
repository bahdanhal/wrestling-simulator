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
    /// the way there, and torn off if the bodies are pulled apart past what a hand can keep. It also
    /// gives the posed hands their grip sockets (<see cref="Sockets"/>). Display only: the engine
    /// decides every grip.
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
        private const float ReachDistance = 0.09f;
        private const float BreakDistance = 0.34f;

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

        /// <summary>Radii of the opponent's body where hands hold, as shares of their height.</summary>
        private const float NeckRadius = 0.034f;
        private const float WristRadius = 0.017f;
        private const float UpperArmRadius = 0.026f;
        private const float KneeRadius = 0.036f;
        private const float ThighRadius = 0.048f;
        private const float AnkleRadius = 0.02f;
        private const float TorsoDepth = 0.065f;
        private const float ShoulderRadius = 0.035f;

        /// <summary>Clasped hands wrap each other: about this radius (m).</summary>
        private const float ClaspRadius = 0.026f;

        /// <summary>From the wrist joint to the middle of the palm, as a share of the holder's height; and the palm's thickness (m).</summary>
        private const float PalmReach = 0.042f;
        private const float PalmThickness = 0.016f;

        /// <summary>
        /// The grip sockets a grip puts the owner's hands in (null for a hand the grip leaves free):
        /// for each hand the wrist position that seats the palm on the surface of the held neck, arm,
        /// trunk or leg, the hand's direction round it, the palm facing its centre, the hand shape and
        /// the aperture (the held segment's radius). The owner's left hand works on the opponent's
        /// right side when facing each other.
        /// </summary>
        public static (GripSocket? Left, GripSocket? Right) Sockets(Grip grip, in Pose self, BodyProportions body, in Pose opponent)
        {
            var seat = new Seat(in self, body, in opponent);
            bool leftHand = grip.Side == GripSide.Left;
            Vector3 up = opponent.Spine;
            Vector3 front = opponent.ChestNormal;
            Vector3 hips = opponent.PelvisFront;
            // The opponent's side the gripping hand works on.
            Vector3 handSide = leftHand ? -opponent.Left : opponent.Left;
            Vector3 shoulder = leftHand ? self.ShoulderL : self.ShoulderR;
            switch (grip.Type)
            {
                case GripType.CollarTie:
                    return One(leftHand, seat.Limb(opponent.Neck, opponent.Head, 0.3f, NeckRadius, (-HeadFront(in opponent)) + (handSide * 0.45f), shoulder, HandGripStyle.CollarCup));
                case GripType.WristControl:
                    return One(leftHand, seat.Wrist(in opponent, !leftHand, shoulder));
                case GripType.TwoOnOne:
                {
                    bool theirRight = leftHand;
                    Vector3 armShoulder = theirRight ? opponent.ShoulderR : opponent.ShoulderL;
                    Vector3 armElbow = theirRight ? opponent.ElbowR : opponent.ElbowL;
                    GripSocket wrist = seat.Wrist(in opponent, !theirRight, self.ShoulderL);
                    GripSocket triceps = seat.Limb(armShoulder, armElbow, 0.75f, UpperArmRadius, (self.ShoulderR - armElbow) + (Vector3.up * 0.1f), self.ShoulderR, HandGripStyle.TricepsHook);
                    return (wrist, triceps);
                }

                case GripType.Underhook:
                {
                    // Under the arm, the palm on the back of the shoulder, fingers hooking up over it.
                    Vector3 armShoulder = leftHand ? opponent.ShoulderR : opponent.ShoulderL;
                    Vector3 point = Vector3.Lerp(armShoulder, opponent.Neck, 0.25f) - (up * (seat.Height * 0.04f)) - (front * (seat.Height * TorsoDepth * 0.95f));
                    Vector3 fingers = up + ((opponent.Neck - armShoulder).normalized * 0.3f);
                    return One(leftHand, seat.Surface(point, -front, fingers, shoulder, ShoulderRadius, HandGripStyle.HookWrap));
                }

                case GripType.Overhook:
                {
                    // Over the arm, the hand clamping the triceps from under and inside.
                    Vector3 armShoulder = leftHand ? opponent.ShoulderR : opponent.ShoulderL;
                    Vector3 armElbow = leftHand ? opponent.ElbowR : opponent.ElbowL;
                    return One(leftHand, seat.Limb(armShoulder, armElbow, 0.6f, UpperArmRadius, (-up * 0.7f) + (handSide * 0.5f), shoulder, HandGripStyle.TricepsHook));
                }

                case GripType.BodyLock:
                {
                    Vector3 trunk = Vector3.Slerp(hips, front, 0.5f);
                    Vector3 clasp = opponent.SpinePoint(0.45f) - (trunk * ((seat.Height * TorsoDepth) + 0.03f));
                    return seat.Gable(clasp, up, -self.Left);
                }

                case GripType.GutWrenchLock:
                {
                    // Round the trunk from the side, locked under the chest.
                    Vector3 clasp = opponent.SpinePoint(0.55f) + (front * ((seat.Height * TorsoDepth) + 0.03f));
                    return seat.Gable(clasp, up, -self.Left);
                }

                case GripType.WaistFromBehind:
                {
                    Vector3 clasp = opponent.Waist + (hips * ((seat.Height * TorsoDepth) + 0.03f));
                    return seat.Gable(clasp, up, -self.Left);
                }

                case GripType.FrontHeadlock:
                {
                    // Arm round the neck, hands locked at the throat.
                    Vector3 centre = Vector3.Lerp(opponent.Neck, opponent.Head, 0.3f);
                    Vector3 clasp = centre + (HeadFront(in opponent) * ((seat.Height * NeckRadius) + 0.025f));
                    return seat.Gable(clasp, opponent.Head - opponent.Neck, -self.Left);
                }

                case GripType.HeadAndArm:
                {
                    // One arm round the neck, the other hand on the near arm above the elbow.
                    GripSocket neck = seat.Limb(opponent.Neck, opponent.Head, 0.25f, NeckRadius, -HeadFront(in opponent), leftHand ? self.ShoulderL : self.ShoulderR, HandGripStyle.HookWrap);
                    Vector3 armShoulder = leftHand ? opponent.ShoulderL : opponent.ShoulderR;
                    Vector3 armElbow = leftHand ? opponent.ElbowL : opponent.ElbowR;
                    Vector3 holder = leftHand ? self.ShoulderR : self.ShoulderL;
                    GripSocket arm = seat.Limb(armShoulder, armElbow, 0.85f, UpperArmRadius, holder - armElbow, holder, HandGripStyle.TricepsHook);
                    return leftHand ? (neck, arm) : (arm, neck);
                }

                case GripType.SingleLeg:
                {
                    Vector3 hip = leftHand ? opponent.HipR : opponent.HipL;
                    Vector3 knee = leftHand ? opponent.KneeR : opponent.KneeL;
                    GripSocket l = seat.Limb(hip, knee, 0.82f, KneeRadius, (-hips * 0.6f) + (self.Left * 0.8f), self.ShoulderL, HandGripStyle.LegWrap);
                    GripSocket r = seat.Limb(hip, knee, 0.82f, KneeRadius, (-hips * 0.6f) - (self.Left * 0.8f), self.ShoulderR, HandGripStyle.LegWrap);
                    return (l, r);
                }

                case GripType.DoubleLeg:
                {
                    // Each hand behind a knee, wrapping from the outside.
                    GripSocket l = seat.Limb(opponent.HipR, opponent.KneeR, 0.85f, KneeRadius, -hips - (opponent.Left * 0.5f), self.ShoulderL, HandGripStyle.LegWrap);
                    GripSocket r = seat.Limb(opponent.HipL, opponent.KneeL, 0.85f, KneeRadius, -hips + (opponent.Left * 0.5f), self.ShoulderR, HandGripStyle.LegWrap);
                    return (l, r);
                }

                case GripType.Ankle:
                {
                    Vector3 knee = leftHand ? opponent.KneeR : opponent.KneeL;
                    Vector3 ankle = leftHand ? opponent.AnkleR : opponent.AnkleL;
                    Vector3 approach = (shoulder - ankle).normalized + (Vector3.up * 0.6f);
                    return One(leftHand, seat.Limb(knee, ankle, 0.85f, AnkleRadius, approach, shoulder, HandGripStyle.WristCClamp));
                }

                case GripType.Crotch:
                {
                    // Through the legs, the palm on the back of the thigh.
                    Vector3 hip = leftHand ? opponent.HipL : opponent.HipR;
                    Vector3 knee = leftHand ? opponent.KneeL : opponent.KneeR;
                    return One(leftHand, seat.Limb(hip, knee, 0.3f, ThighRadius, -hips, shoulder, HandGripStyle.LegWrap));
                }

                case GripType.LegLace:
                {
                    Vector3 clasp = ((opponent.AnkleL + opponent.AnkleR) * 0.5f) + (Vector3.up * 0.06f);
                    return seat.Gable(clasp, Vector3.up, -self.Left);
                }

                default:
                    return (null, null);
            }
        }

        private static (GripSocket? Left, GripSocket? Right) One(bool leftHand, GripSocket socket) =>
            leftHand ? ((GripSocket?)socket, (GripSocket?)null) : (null, socket);

        private static Vector3 HeadFront(in Pose pose) => pose.HeadNormal.sqrMagnitude > 1e-6f ? pose.HeadNormal.normalized : pose.ChestNormal;

        /// <summary>Builds sockets on one opponent for one holder (sizes from the two bodies).</summary>
        private readonly struct Seat
        {
            public Seat(in Pose self, BodyProportions body, in Pose opponent)
            {
                float mine = (self.ShoulderL - self.ShoulderR).magnitude;
                float theirs = (opponent.ShoulderL - opponent.ShoulderR).magnitude;
                float scale = mine > 1e-3f && theirs > 1e-3f ? Mathf.Clamp(theirs / mine, 0.7f, 1.4f) : 1f;
                Height = body.Height * scale;
                Reach = body.Height * PalmReach;
            }

            /// <summary>The opponent's estimated height (m).</summary>
            public float Height { get; }

            /// <summary>Wrist to palm centre of the holder's hand (m).</summary>
            private float Reach { get; }

            /// <summary>
            /// Round a limb segment from <paramref name="a"/> to <paramref name="b"/> at share
            /// <paramref name="t"/>: the palm on the side <paramref name="approach"/> points to, the
            /// fingers wrapping on round it away from the holder's shoulder.
            /// </summary>
            public GripSocket Limb(Vector3 a, Vector3 b, float t, float radiusShare, Vector3 approach, Vector3 shoulder, HandGripStyle style)
            {
                float radius = Height * radiusShare;
                Vector3 axis = b - a;
                axis = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.up;
                Vector3 centre = Vector3.Lerp(a, b, t);
                Vector3 outward = Vector3.ProjectOnPlane(approach, axis);
                if (outward.sqrMagnitude < 1e-6f)
                {
                    outward = Vector3.ProjectOnPlane(shoulder - centre, axis);
                }

                if (outward.sqrMagnitude < 1e-6f)
                {
                    outward = Vector3.ProjectOnPlane(Vector3.up, axis).sqrMagnitude > 1e-6f ? Vector3.ProjectOnPlane(Vector3.up, axis) : Vector3.ProjectOnPlane(Vector3.forward, axis);
                }

                outward.Normalize();
                Vector3 palm = -outward;
                Vector3 contact = centre + (outward * radius);
                Vector3 round = Vector3.Cross(axis, palm).normalized;
                if (Vector3.Dot(round, contact - shoulder) < 0f)
                {
                    round = -round;
                }

                return Place(contact, round, palm, style, radius);
            }

            /// <summary>On the wrist end of the opponent's forearm (their left arm or right).</summary>
            public GripSocket Wrist(in Pose opponent, bool theirLeft, Vector3 shoulder)
            {
                Vector3 elbow = theirLeft ? opponent.ElbowL : opponent.ElbowR;
                Vector3 hand = theirLeft ? opponent.HandL : opponent.HandR;
                Vector3 approach = (shoulder - hand).normalized + (Vector3.up * 0.6f);
                return Limb(elbow, hand, 0.86f, WristRadius, approach, shoulder, HandGripStyle.WristCClamp);
            }

            /// <summary>Flat on a trunk surface at <paramref name="contact"/>, fingers towards <paramref name="fingers"/>.</summary>
            public GripSocket Surface(Vector3 contact, Vector3 outward, Vector3 fingers, Vector3 shoulder, float radiusShare, HandGripStyle style)
            {
                Vector3 palm = -outward.normalized;
                Vector3 direction = Vector3.ProjectOnPlane(fingers, palm);
                if (direction.sqrMagnitude < 1e-6f)
                {
                    direction = Vector3.ProjectOnPlane(contact - shoulder, palm);
                }

                return Place(contact, direction.normalized, palm, style, Height * radiusShare);
            }

            /// <summary>
            /// Hands locked palm to palm at <paramref name="clasp"/>: the left palm faces along
            /// <paramref name="normal"/>, the right palm back against it, the hands overlapping from
            /// either side along <paramref name="wrap"/> (the left hand's way round).
            /// </summary>
            public (GripSocket? Left, GripSocket? Right) Gable(Vector3 clasp, Vector3 normal, Vector3 wrap)
            {
                Vector3 n = normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.up;
                Vector3 w = Vector3.ProjectOnPlane(wrap, n);
                if (w.sqrMagnitude < 1e-6f)
                {
                    w = Vector3.ProjectOnPlane(Vector3.forward, n).sqrMagnitude > 1e-6f ? Vector3.ProjectOnPlane(Vector3.forward, n) : Vector3.ProjectOnPlane(Vector3.right, n);
                }

                w.Normalize();
                GripSocket left = Place(clasp, w, n, HandGripStyle.GableClasped, ClaspRadius);
                GripSocket right = Place(clasp, -w, -n, HandGripStyle.GableClasped, ClaspRadius);
                return (left, right);
            }

            /// <summary>A socket with the palm's middle on <paramref name="contact"/>: the wrist sits back along the hand, off the surface by the palm's thickness.</summary>
            private GripSocket Place(Vector3 contact, Vector3 direction, Vector3 palm, HandGripStyle style, float radius)
            {
                Vector3 wrist = contact - (palm * PalmThickness) - (direction * Reach);
                return new GripSocket(wrist, contact, direction, palm, style, GripAperture.FromRadius(radius));
            }
        }

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
