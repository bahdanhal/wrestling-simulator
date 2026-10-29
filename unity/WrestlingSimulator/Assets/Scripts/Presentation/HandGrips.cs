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
    /// is made when the palm reaches the opponent's surface, and torn off if the bodies are pulled
    /// apart past what a hand can keep. Display only: the engine decides every grip.
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

        private static readonly string[] Hands = { "LeftHand", "RightHand" };

        private readonly GripLink[] _links = new GripLink[4];
        private readonly Grip[] _grips = new Grip[4];

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
                    _links[i] = null;
                    _grips[i] = null;
                    continue;
                }

                Gripping++;
                if (_links[i] != null && (!_links[i].Holding || !ReferenceEquals(_grips[i], grip)))
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
                (ArticulationBody body, Vector3 point, float distance) = held.Nearest(palmWorld);
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
