using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// A hold between two physical bodies: a damped spring pulling a point on one segment to a point
    /// on another, applied to both as equal and opposite forces (so it moves neither pair's total
    /// momentum) and capped at the strength of the hold. A hand closing on a wrist, an arm clamped
    /// under an armpit, a trunk squeezed onto a hip all work this way. Past its break distance the
    /// hold is torn off and stays off until it is made again. Display only.
    /// </summary>
    public sealed class GripLink
    {
        /// <summary>Largest explicit spring and damper (relative to the lighter segment's mass and the substep) that stay stable.</summary>
        private const float StableSpring = 0.5f;
        private const float StableDamper = 1f;

        private readonly ArticulationBody _holder;
        private readonly ArticulationBody _held;
        private float _spring;
        private float _damper;
        private float _maxForce;
        private readonly float _breakDistance;

        /// <param name="holder">Segment that holds (a hand, or the thrower's trunk for a trap).</param>
        /// <param name="holderAnchor">Point on the holder, in its local space.</param>
        /// <param name="held">Segment held.</param>
        /// <param name="heldAnchor">Point on the held segment, in its local space.</param>
        public GripLink(ArticulationBody holder, Vector3 holderAnchor, ArticulationBody held, Vector3 heldAnchor, float spring, float damper, float maxForce, float breakDistance = float.PositiveInfinity)
        {
            _holder = holder;
            HolderAnchor = holderAnchor;
            _held = held;
            HeldAnchor = heldAnchor;
            _spring = spring;
            _damper = damper;
            _maxForce = maxForce;
            _breakDistance = breakDistance;
        }

        /// <summary>Point on the holder, in its local space (movable: a seat that slides round a fulcrum).</summary>
        public Vector3 HolderAnchor { get; set; }

        /// <summary>Point on the held segment, in its local space.</summary>
        public Vector3 HeldAnchor { get; set; }

        public ArticulationBody Holder => _holder;

        public ArticulationBody Held => _held;

        private bool _holding = true;

        /// <summary>False once the hold has been torn off (or either body is gone).</summary>
        public bool Holding
        {
            get => _holding && _holder != null && _held != null;
            private set => _holding = value;
        }

        /// <summary>Distance between the two anchor points (m).</summary>
        public float Gap => (HolderPoint - HeldPoint).magnitude;

        /// <summary>Size of the force the hold applied in the last step (N).</summary>
        public float Load { get; private set; }

        public Vector3 HolderPoint => _holder.transform.TransformPoint(HolderAnchor);

        public Vector3 HeldPoint => _held.transform.TransformPoint(HeldAnchor);

        public float MaxForce => _maxForce;

        public void SetDrive(float spring, float damper, float maxForce)
        {
            _spring = spring;
            _damper = damper;
            _maxForce = maxForce;
        }

        /// <summary>
        /// Applies one physics substep of <paramref name="deltaTime"/> seconds. The spring and damper are
        /// explicit forces, so they are kept within what the lighter segment can take in one substep
        /// without overshooting. Returns false once the hold has been torn off.
        /// </summary>
        public bool Apply(float deltaTime)
        {
            if (!Holding || _holder == null || _held == null)
            {
                Holding = false;
                return false;
            }

            Vector3 a = HolderPoint;
            Vector3 b = HeldPoint;
            Vector3 gap = a - b;
            if (gap.magnitude > _breakDistance)
            {
                Holding = false;
                Load = 0f;
                return false;
            }

            float dt = Mathf.Max(deltaTime, 1e-4f);
            float lighter = Mathf.Min(_holder.mass, _held.mass);
            float spring = Mathf.Min(_spring, StableSpring * lighter / (dt * dt));
            float damper = Mathf.Min(_damper, StableDamper * lighter / dt);
            Vector3 closing = _holder.GetPointVelocity(a) - _held.GetPointVelocity(b);
            Vector3 force = Vector3.ClampMagnitude((spring * gap) + (damper * closing), _maxForce);
            _held.AddForceAtPosition(force, b);
            _holder.AddForceAtPosition(-force, a);
            PhysicalBody.AddHoldImpulse(_held, force * dt);
            PhysicalBody.AddHoldImpulse(_holder, -force * dt);
            Load = force.magnitude;
            return true;
        }
    }
}
