using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Physics body for one wrestler: twelve rigid segments on the character's bones, joined by
    /// motorised joints. The animation is only a target: every segment is driven towards its
    /// animated pose, and the physics solver keeps the two wrestlers and the mat from passing
    /// through each other, so limbs press against the opponent instead of sinking in. On a fall the
    /// tracking lets go: the body keeps its motion, falls under gravity, and only weak muscles pull
    /// towards the pose the engine says it lands in; then tracking strengthens again.
    /// Display only: nothing here feeds back into the simulation.
    /// </summary>
    public sealed class ActiveRagdoll
    {
        /// <summary>How hard the joint muscles pull towards the animated pose while falling (relative to full control).</summary>
        private const float FallingMuscle = 0.35f;
        private const float TrackingMuscle = 1f;
        private const float RecoverSeconds = 0.45f;

        /// <summary>Natural frequency (rad/s) of the spring pulling each segment to its animated pose.</summary>
        private const float TrackingFrequency = 2f * Mathf.PI * 7f;

        /// <summary>Most the tracking may accelerate a segment: strong, but finite, so contact with the mat and the opponent always wins.</summary>
        private const float MaxTrackingAcceleration = 120f;
        private const float MaxTrackingAngularAcceleration = 900f;
        private const float TrackingGain = 1f;

        private enum Mode
        {
            Tracking,

            /// <summary>Thrown: driven round the thrower's fulcrum, hanging from their grips, under gravity.</summary>
            Thrown,
            Falling,
            Recovering,
        }

        /// <summary>Muscle strength while thrown: the body keeps a wrestler's tension but does not steer.</summary>
        private const float ThrownMuscle = 0.55f;


        /// <summary>How fast a lag behind the throw's planned rotation is made up (1/s).</summary>
        private const float ThrowAngleGain = 6f;

        private readonly List<ConfigurableJoint> _gripJoints = new List<ConfigurableJoint>();
        private readonly HashSet<ConfigurableJoint> _traps = new HashSet<ConfigurableJoint>();

        /// <summary>Grip and arm-wrap drives (stiffness N/m, damping) and their share of the thrower's strength.</summary>
        private const float GripSpring = 6000f;
        private const float GripDamper = 400f;
        private const float GripShare = 0.35f;
        private const float LoadSpring = 5000f;
        private const float LoadDamper = 500f;
        private const float LoadShare = 0.5f;

        /// <summary>Share of the grip stiffness and of the thrower's strength the grips keep during the turn.</summary>
        private const float TurnContactShare = 0.1f;

        /// <summary>Planted feet or knees each add this share of the thrower's strength to the throw (none planted: <see cref="UnsupportedShare"/>).</summary>
        private const float PlantedShare = 0.35f;
        private const float UnsupportedShare = 0.3f;
        private const float PlantedHeight = 0.12f;

        private ConfigurableJoint _load;
        private float _seatUp = 0.5f;
        private float _lastAvailable;
        private float _lastDemand;
        private Vector3 _turnOffset;
        private bool _turning;
        private int _traceCount;
        private Quaternion _trunkRotation = Quaternion.identity;

        /// <summary>Diagnostics sink for throw drive traces (tools only; null in the game).</summary>
        public static Action<string> Trace;

        /// <summary>Centre-of-mass carry gains (1/s², 1/s) and the turn gain (1/s) with its cap (deg/s²).</summary>
        private const float CarryStiffness = 60f;
        private const float CarryDamping = 14f;
        private const float TurnGain = 12f;
        private const float MaxTurnAcceleration = 4000f;

        private ActiveRagdoll _throwerBody;
        private float _maxThrowForce;
        private Segment _pivot;
        private Vector3 _throwAxis;
        private float _throwAngle;

        private sealed class Segment
        {
            public string Name;
            public Transform Bone;
            public Rigidbody Body;
            public ConfigurableJoint Joint;
            public Segment Parent;
            public Quaternion StartLocal;
            public Vector3 TargetPosition;
            public Quaternion TargetRotation;
            public Vector3 TargetVelocity;
            public Vector3 TargetAngularVelocity;
            public bool HasTarget;
            public float Muscle;
        }

        /// <summary>Segment, parent, mass share, radius as a share of height (torso radii match the half chest depth used for contact holds), and which child bone ends the capsule.</summary>
        private static readonly (string Name, string Parent, float Mass, float Radius, string End)[] Layout =
        {
            ("Hips", null, 0.15f, 0.062f, "Spine"),
            ("Spine", "Hips", 0.13f, 0.058f, "Spine2"),
            ("Spine2", "Spine", 0.2f, 0.063f, "Neck"),
            ("Head", "Spine2", 0.08f, 0.058f, null),
            ("LeftArm", "Spine2", 0.03f, 0.028f, "LeftForeArm"),
            ("LeftForeArm", "LeftArm", 0.025f, 0.023f, "LeftHand"),
            ("RightArm", "Spine2", 0.03f, 0.028f, "RightForeArm"),
            ("RightForeArm", "RightArm", 0.025f, 0.023f, "RightHand"),
            ("LeftUpLeg", "Hips", 0.1f, 0.042f, "LeftLeg"),
            ("LeftLeg", "LeftUpLeg", 0.055f, 0.03f, "LeftFoot"),
            ("RightUpLeg", "Hips", 0.1f, 0.042f, "RightLeg"),
            ("RightLeg", "RightUpLeg", 0.055f, 0.03f, "RightFoot"),
        };

        private readonly List<Segment> _segments = new List<Segment>();
        private readonly Collider[] _colliders;
        private readonly GameObject _root;
        private Mode _mode = Mode.Tracking;
        private float _tracking = TrackingGain;

        /// <summary>Tracking strength multiplier while carrying a thrown opponent, so their weight cannot stand the thrower up.</summary>
        private const float CarryingStrength = 3f;
        private float _strength = 1f;
        private float _timer;
        private float _fallSeconds;
        private float _weight;

        public ActiveRagdoll(Func<string, Transform> bone, float heightMetres, float massKg, string name)
        {
            _root = new GameObject(name + " ragdoll");
            var byName = new Dictionary<string, Segment>(StringComparer.Ordinal);
            foreach ((string segmentName, string parent, float massShare, float radiusShare, string end) in Layout)
            {
                Transform t = bone(segmentName);
                var go = new GameObject(segmentName);
                go.transform.SetParent(_root.transform, false);
                go.transform.SetPositionAndRotation(t.position, t.rotation);
                var body = go.AddComponent<Rigidbody>();
                body.mass = massKg * massShare;
                body.isKinematic = false;
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.None;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.inertiaTensorRotation = Quaternion.identity;
                body.solverIterations = 16;
                body.solverVelocityIterations = 4;
                body.maxAngularVelocity = 25f;
                body.angularDamping = 0.6f;
                body.linearDamping = 0.05f;
                // Overlaps (a released body starts wrapped around the thrower) resolve fast but without being shot apart.
                body.maxDepenetrationVelocity = 2f;

                float radius = radiusShare * heightMetres;
                if (end == null)
                {
                    var sphere = go.AddComponent<SphereCollider>();
                    sphere.radius = radius;
                    sphere.center = t.InverseTransformVector(t.up * radius * 0.9f);
                }
                else
                {
                    // Capsule from this joint to the next, along whichever local axis points there.
                    Vector3 local = Quaternion.Inverse(t.rotation) * (bone(end).position - t.position);
                    int axis = Mathf.Abs(local.x) > Mathf.Abs(local.y) ? (Mathf.Abs(local.x) > Mathf.Abs(local.z) ? 0 : 2) : (Mathf.Abs(local.y) > Mathf.Abs(local.z) ? 1 : 2);
                    var capsule = go.AddComponent<CapsuleCollider>();
                    capsule.direction = axis;
                    capsule.radius = radius;
                    capsule.height = Mathf.Max(local.magnitude + radius, radius * 2f);
                    capsule.center = local * 0.5f;
                }

                var segment = new Segment { Name = segmentName, Bone = t, Body = body, Muscle = 1f };
                if (parent != null)
                {
                    segment.Parent = byName[parent];
                    segment.StartLocal = Quaternion.Inverse(segment.Parent.Bone.rotation) * t.rotation;
                }

                byName[segmentName] = segment;
                _segments.Add(segment);
            }

            foreach (Segment segment in _segments)
            {
                if (segment.Parent == null)
                {
                    continue;
                }

                var joint = segment.Body.gameObject.AddComponent<ConfigurableJoint>();
                joint.connectedBody = segment.Parent.Body;
                joint.autoConfigureConnectedAnchor = true;
                joint.anchor = Vector3.zero;
                joint.axis = Vector3.right;
                joint.secondaryAxis = Vector3.up;
                joint.configuredInWorldSpace = false;
                joint.xMotion = ConfigurableJointMotion.Locked;
                joint.yMotion = ConfigurableJointMotion.Locked;
                joint.zMotion = ConfigurableJointMotion.Locked;
                joint.angularXMotion = ConfigurableJointMotion.Limited;
                joint.angularYMotion = ConfigurableJointMotion.Limited;
                joint.angularZMotion = ConfigurableJointMotion.Limited;
                joint.lowAngularXLimit = new SoftJointLimit { limit = -120f };
                joint.highAngularXLimit = new SoftJointLimit { limit = 120f };
                joint.angularYLimit = new SoftJointLimit { limit = 70f };
                joint.angularZLimit = new SoftJointLimit { limit = 70f };
                joint.rotationDriveMode = RotationDriveMode.Slerp;
                joint.enableCollision = false;
                joint.enablePreprocessing = true;
                segment.Joint = joint;
            }

            // A wrestler's own segments never collide with each other; only with the mat and the opponent.
            var colliders = _root.GetComponentsInChildren<Collider>();
            _colliders = colliders;
            for (int i = 0; i < colliders.Length; i++)
            {
                for (int j = i + 1; j < colliders.Length; j++)
                {
                    Physics.IgnoreCollision(colliders[i], colliders[j]);
                }
            }
        }

        /// <summary>Deepest overlap in metres between this body's colliders and another's (diagnostics).</summary>
        public float DeepestOverlap(ActiveRagdoll other)
        {
            float deepest = 0f;
            foreach (Collider mine in _colliders)
            {
                foreach (Collider theirs in other._colliders)
                {
                    if (Physics.ComputePenetration(mine, mine.transform.position, mine.transform.rotation, theirs, theirs.transform.position, theirs.transform.rotation, out _, out float distance))
                    {
                        deepest = Mathf.Max(deepest, distance);
                    }
                }
            }

            return deepest;
        }

        /// <summary>0 when the animation is shown as is, 1 when the physics body is.</summary>
        public float Weight => _weight;

        public bool IsFalling => _mode == Mode.Falling;

        public bool IsThrown => _mode == Mode.Thrown;

        /// <summary>Braces the body while it carries a thrown opponent (stronger tracking), or relaxes it.</summary>
        public void SetCarrying(bool carrying) => _strength = carrying ? CarryingStrength : 1f;

        /// <summary>Hip height above the mat in metres (diagnostics).</summary>
        public float HipHeight => Find("Hips").Body.position.y;

        /// <summary>Hip position in metres (diagnostics).</summary>
        public Vector3 HipPosition => Find("Hips").Body.position;

        /// <summary>Diagnostics while thrown: strength available and asked for (N), and how far the trunk is from its seat on the fulcrum (m).</summary>
        public string ThrowReport
        {
            get
            {
                if (_mode != Mode.Thrown || _load == null)
                {
                    return "-";
                }

                Vector3 seat = _pivot.Body.transform.TransformPoint(_load.connectedAnchor);
                Vector3 trunk = _load.transform.TransformPoint(_load.anchor);
                return FormattableString.Invariant($"available {_lastAvailable:F0} N, demand {_lastDemand:F0} N, seat gap {(trunk - seat).magnitude:F2} m (trunk {trunk.y:F2} m, seat {seat.y:F2} m), angle {_throwAngle:F0}");
            }
        }

        /// <summary>Rotation achieved so far in the current throw, in degrees about its axis.</summary>
        public float ThrowAngle => _throwAngle;

        private Segment Find(string segmentName)
        {
            foreach (Segment segment in _segments)
            {
                if (segment.Name == segmentName)
                {
                    return segment;
                }
            }

            throw new ArgumentException($"No ragdoll segment '{segmentName}'.");
        }

        /// <summary>
        /// Starts a throw: tracking lets go, gravity acts, and the thrower's grip segments are joined to
        /// this body's held segments, and the trunk is squeezed onto the thrower's <paramref name="pivot"/>
        /// segment (the fulcrum). The turn round the fulcrum starts with <see cref="StartTurn"/>.
        /// </summary>
        public void BeginThrow(ActiveRagdoll thrower, string pivot, IEnumerable<(string Thrower, string Thrown)> grips, float maxForce, float seatUp)
        {
            EndGrips();
            _throwerBody = thrower;
            _maxThrowForce = maxForce;
            _seatUp = Mathf.Clamp01(seatUp);
            _pivot = thrower.Find(pivot);
            _trunkRotation = Find("Spine").Body.rotation;
            _throwAngle = 0f;
            foreach ((string holder, string held) in grips)
            {
                Segment mine = Find(held);
                Segment theirs = thrower.Find(holder);
                // The hand closes on the held point: a joint drive pulling it to the end of the
                // thrower's forearm, acting on both bodies equally and capped by grip strength.
                var joint = mine.Body.gameObject.AddComponent<ConfigurableJoint>();
                joint.connectedBody = theirs.Body;
                joint.autoConfigureConnectedAnchor = false;
                joint.anchor = mine.Body.transform.InverseTransformPoint(mine.Body.GetComponent<Collider>().bounds.center);
                bool trap = !holder.EndsWith("ForeArm", StringComparison.Ordinal);
                if (trap)
                {
                    // Trapped against the thrower's body: held on their surface where it touches now,
                    // fixed to that trunk segment, so it turns with the thrower.
                    Vector3 heldCentre = mine.Body.GetComponent<Collider>().bounds.center;
                    Vector3 holderCentre = theirs.Body.GetComponent<Collider>().bounds.center;
                    Vector3 towards = heldCentre - holderCentre;
                    towards = towards.sqrMagnitude > 1e-6f ? towards.normalized : Vector3.up;
                    joint.connectedAnchor = theirs.Body.transform.InverseTransformPoint(holderCentre + (towards * (Radius(theirs) + Radius(mine))));
                    _traps.Add(joint);
                }
                else
                {
                    var forearm = theirs.Body.GetComponent<CapsuleCollider>();
                    joint.connectedAnchor = forearm != null ? forearm.center * 2f : Vector3.zero;
                }

                joint.xMotion = ConfigurableJointMotion.Free;
                joint.yMotion = ConfigurableJointMotion.Free;
                joint.zMotion = ConfigurableJointMotion.Free;
                var hold = new JointDrive { positionSpring = GripSpring, positionDamper = GripDamper, maximumForce = maxForce * GripShare };
                joint.xDrive = hold;
                joint.yDrive = hold;
                joint.zDrive = hold;
                joint.targetPosition = Vector3.zero;
                joint.angularXMotion = ConfigurableJointMotion.Free;
                joint.angularYMotion = ConfigurableJointMotion.Free;
                joint.angularZMotion = ConfigurableJointMotion.Free;
                joint.enableCollision = true;
                _gripJoints.Add(joint);
            }

            // The arm wrap squeezes the trunk onto the fulcrum (hip, back, chest or shoulders), so the
            // opponent is loaded across the thrower rather than swinging beside them. The trunk is
            // seated on the fulcrum's surface, on the opponent's side and above it, never pulled into
            // its middle: a body loaded on a hip rests on top of it. The seat is fixed to the thrower's
            // segment, so as the thrower turns in and bends, the opponent goes with them.
            Segment trunk = Find("Spine");
            Vector3 trunkCentre = trunk.Body.GetComponent<Collider>().bounds.center;
            Vector3 pivotCentre = _pivot.Body.GetComponent<Collider>().bounds.center;
            Vector3 side = Vector3.ProjectOnPlane(trunkCentre - pivotCentre, Vector3.up);
            side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.ProjectOnPlane(trunkCentre - thrower.Find("Hips").Body.position, Vector3.up).normalized;
            Vector3 seat = Seat(side, trunk);
            var load = trunk.Body.gameObject.AddComponent<ConfigurableJoint>();
            load.connectedBody = _pivot.Body;
            load.autoConfigureConnectedAnchor = false;
            load.anchor = trunk.Body.transform.InverseTransformPoint(trunkCentre);
            load.connectedAnchor = _pivot.Body.transform.InverseTransformPoint(pivotCentre + seat);
            load.xMotion = ConfigurableJointMotion.Free;
            load.yMotion = ConfigurableJointMotion.Free;
            load.zMotion = ConfigurableJointMotion.Free;
            var squeeze = new JointDrive { positionSpring = LoadSpring, positionDamper = LoadDamper, maximumForce = maxForce * LoadShare };
            load.xDrive = squeeze;
            load.yDrive = squeeze;
            load.zDrive = squeeze;
            load.targetPosition = Vector3.zero;
            load.angularXMotion = ConfigurableJointMotion.Free;
            load.angularYMotion = ConfigurableJointMotion.Free;
            load.angularZMotion = ConfigurableJointMotion.Free;
            load.enableCollision = true;
            _gripJoints.Add(load);
            _load = load;

            _mode = Mode.Thrown;
            _tracking = 0f;
            SetGravity(true);
            SetMuscle(ThrownMuscle);
        }

        /// <summary>
        /// Each physics substep while loading (before the turn): the seat stays on the side of the
        /// fulcrum facing the opponent's trunk, so the squeeze draws them in tight and up onto it.
        /// </summary>
        public void DriveLoad()
        {
            if (_mode != Mode.Thrown || _load == null || _turning)
            {
                return;
            }

            Segment trunk = Find("Spine");
            Vector3 centre = _pivot.Body.GetComponent<Collider>().bounds.center;
            Vector3 side = Vector3.ProjectOnPlane(trunk.Body.GetComponent<Collider>().bounds.center - centre, Vector3.up);
            if (side.sqrMagnitude > 1e-6f)
            {
                _load.connectedAnchor = _pivot.Body.transform.InverseTransformPoint(centre + Seat(side.normalized, trunk));
            }
        }

        /// <summary>Offset from the fulcrum's centre to where the trunk rests on it, the two surfaces touching.</summary>
        private Vector3 Seat(Vector3 side, Segment trunk) => Vector3.Slerp(side, Vector3.up, _seatUp).normalized * (Radius(_pivot) + Radius(trunk));

        /// <summary>World point a grip holds on this body's segment (the middle of the segment's volume).</summary>
        public Vector3 GripPoint(string segment) => Find(segment).Body.GetComponent<Collider>().bounds.center;

        /// <summary>
        /// Each physics substep while thrown, the way a thrower applies force: one net force carries
        /// the body's centre of mass round the thrower's fulcrum along the planned turn (so they lift
        /// and carry the weight), and one couple turns the body about its own centre at the planned
        /// rate. Both come out of the thrower's strength, which shrinks without a base, and both are
        /// pushed back onto the thrower. Limbs are left to the body's own joints, so no effort is spent
        /// fighting them. Being capped, the drive can be stopped by a block or the mat.
        /// </summary>
        public void DriveThrow(float targetAngle, float targetRate, float deltaTime)
        {
            if (_mode != Mode.Thrown)
            {
                return;
            }

            float dt = Mathf.Max(deltaTime, 1e-4f);
            float turned = TurnAbout(_throwAxis);
            _throwAngle += turned;

            float spin = turned / dt;
            float mass = 0f;
            Vector3 centre = Vector3.zero;
            Vector3 momentum = Vector3.zero;
            foreach (Segment segment in _segments)
            {
                mass += segment.Body.mass;
                centre += segment.Body.worldCenterOfMass * segment.Body.mass;
                momentum += segment.Body.linearVelocity * segment.Body.mass;
            }

            centre /= mass;
            Vector3 velocity = momentum / mass;
            // Where the centre of mass should be: its offset from the fulcrum at the start of the turn,
            // turned by the planned angle. The fulcrum moves with the thrower (rising, bending, kneeling).
            Vector3 pivot = _pivot.Body.position;
            Quaternion plan = Quaternion.AngleAxis(targetAngle, _throwAxis);
            Vector3 target = pivot + (plan * _turnOffset);
            Vector3 targetVelocity = Vector3.Cross(_throwAxis * (targetRate * Mathf.Deg2Rad), target - pivot) + _pivot.Body.linearVelocity;
            Vector3 carry = mass * ((CarryStiffness * (target - centre)) + (CarryDamping * (targetVelocity - velocity)) - Physics.gravity);

            // The turn: angular acceleration that brings the spin to the planned rate (plus catching
            // up any lag), as a pure couple: each segment pushed along its circle round the centre.
            float wantedRate = targetRate + (ThrowAngleGain * (targetAngle - _throwAngle));
            float alpha = Mathf.Clamp(TurnGain * (wantedRate - spin), -MaxTurnAcceleration, MaxTurnAcceleration) * Mathf.Deg2Rad;
            float coupleEffort = 0f;
            var couple = new Vector3[_segments.Count];
            for (int i = 0; i < _segments.Count; i++)
            {
                Vector3 r = Vector3.ProjectOnPlane(_segments[i].Body.worldCenterOfMass - centre, _throwAxis);
                couple[i] = Vector3.Cross(_throwAxis * alpha, r) * _segments[i].Body.mass;
                coupleEffort += couple[i].magnitude * 0.5f;
            }

            // The thrower's whole-body strength caps the effort, and it shrinks without a base.
            float available = _maxThrowForce * _throwerBody.Support();
            float demand = carry.magnitude + coupleEffort;
            float scale = demand > available ? available / demand : 1f;
            _lastAvailable = available;
            _lastDemand = demand;
            if (Trace != null && (++_traceCount % 8) == 0)
            {
                Trace(FormattableString.Invariant($"plan {targetAngle:F0}° got {_throwAngle:F0}° spin {spin:F0}°/s want {wantedRate:F0}°/s | com err {(target - centre).magnitude:F2} m (com y {centre.y:F2}, target y {target.y:F2}) | com-pivot {centre - pivot} target-pivot {target - pivot} axis {_throwAxis} | carry {carry.magnitude:F0} N couple {coupleEffort:F0} N avail {available:F0} N scale {scale:F2}"));
            }

            Vector3 torque = Vector3.zero;
            for (int i = 0; i < _segments.Count; i++)
            {
                Rigidbody body = _segments[i].Body;
                Vector3 force = (carry * (body.mass / mass)) + couple[i];
                body.AddForce(force * scale, ForceMode.Force);
                torque += Vector3.Cross(body.worldCenterOfMass - centre, couple[i] * scale);
            }

            // Equal and opposite: the carrying force pushes the thrower down and back at the fulcrum,
            // and the turning couple twists the thrower the other way.
            _throwerBody.ReceiveReaction(-carry * scale, -torque, pivot);
        }

        /// <summary>How far (degrees) the trunk has turned about <paramref name="axis"/> since the last call: the twist part of its rotation.</summary>
        private float TurnAbout(Vector3 axis)
        {
            Quaternion now = Find("Spine").Body.rotation;
            Quaternion delta = now * Quaternion.Inverse(_trunkRotation);
            _trunkRotation = now;
            Vector3 v = new Vector3(delta.x, delta.y, delta.z);
            Vector3 along = Vector3.Project(v, axis);
            var twist = new Quaternion(along.x, along.y, along.z, delta.w);
            float length = Mathf.Sqrt((twist.x * twist.x) + (twist.y * twist.y) + (twist.z * twist.z) + (twist.w * twist.w));
            if (length < 1e-6f)
            {
                return 0f;
            }

            float w = Mathf.Clamp(twist.w / length, -1f, 1f);
            float angle = 2f * Mathf.Acos(Mathf.Abs(w)) * Mathf.Rad2Deg;
            float sign = Mathf.Sign(Vector3.Dot(along, axis)) * Mathf.Sign(twist.w);
            return angle * sign;
        }

        /// <summary>While thrown: the horizontal offset from the thrower's fulcrum to this body's centre of mass.</summary>
        public Vector3 OffsetFromFulcrum => _pivot == null ? Vector3.zero : Vector3.ProjectOnPlane(CentreOfMass() - _pivot.Body.position, Vector3.up);

        private Vector3 CentreOfMass()
        {
            float mass = 0f;
            Vector3 centre = Vector3.zero;
            foreach (Segment segment in _segments)
            {
                mass += segment.Body.mass;
                centre += segment.Body.worldCenterOfMass * segment.Body.mass;
            }

            return centre / mass;
        }

        /// <summary>Share of full strength this body can push with: more for each foot or knee planted on the mat.</summary>
        private float Support()
        {
            float support = UnsupportedShare;
            foreach (string part in new[] { "LeftLeg", "RightLeg" })
            {
                if (Find(part).Body.GetComponent<Collider>().bounds.min.y < PlantedHeight)
                {
                    support += PlantedShare;
                }
            }

            return Mathf.Min(support, 1f);
        }

        private static float Radius(Segment segment)
        {
            Collider collider = segment.Body.GetComponent<Collider>();
            float scale = segment.Body.transform.lossyScale.x;
            return collider is CapsuleCollider capsule ? capsule.radius * scale : collider is SphereCollider sphere ? sphere.radius * scale : 0f;
        }

        private void ReceiveReaction(Vector3 force, Vector3 torque, Vector3 point)
        {
            foreach (string part in new[] { "Hips", "Spine", "Spine2" })
            {
                Rigidbody body = Find(part).Body;
                body.AddForceAtPosition(force / 3f, point, ForceMode.Force);
                body.AddTorque(torque / 3f, ForceMode.Force);
            }
        }

        /// <summary>Starts the turn of a throw that has been loaded: rotation is measured from now about <paramref name="axis"/>.</summary>
        public void StartTurn(Vector3 axis)
        {
            _throwAxis = axis.normalized;
            _trunkRotation = Find("Spine").Body.rotation;
            _throwAngle = 0f;
            if (_pivot != null)
            {
                _turnOffset = CentreOfMass() - _pivot.Body.position;
                _turning = true;
                // From here the carry is what the arms and hips apply (strength-capped, pushed back on
                // the thrower); the grips and the squeeze only keep the hands and bodies in contact.
                // Left stiff, they would pin the body to where the animation puts the hands.
                var contact = new JointDrive { positionSpring = GripSpring * TurnContactShare, positionDamper = GripDamper * TurnContactShare, maximumForce = _maxThrowForce * TurnContactShare };
                foreach (ConfigurableJoint joint in _gripJoints)
                {
                    // Traps stay firm: an arm clamped under the armpit turns with the thrower's body.
                    if (joint != null && !_traps.Contains(joint))
                    {
                        joint.xDrive = contact;
                        joint.yDrive = contact;
                        joint.zDrive = contact;
                    }
                }
            }
        }

        /// <summary>Lets go: the grips release and the body flies on with its momentum, then lands.</summary>
        public void EndThrow(float fallSeconds)
        {
            if (_mode != Mode.Thrown)
            {
                return;
            }

            EndGrips();
            _mode = Mode.Tracking;
            Fall(fallSeconds);
        }

        private void EndGrips()
        {
            foreach (ConfigurableJoint joint in _gripJoints)
            {
                if (joint != null)
                {
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(joint);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(joint);
                    }
                }
            }

            _gripJoints.Clear();
            _traps.Clear();
            _load = null;
            _turning = false;
            _pivot = null;
            _throwerBody = null;
        }

        /// <summary>Puts the body exactly on the animated pose at rest (bout start, restarts, snapshots).</summary>
        public void Reset()
        {
            foreach (Segment segment in _segments)
            {
                segment.Body.position = segment.Bone.position;
                segment.Body.rotation = segment.Bone.rotation;
                segment.Body.transform.SetPositionAndRotation(segment.Bone.position, segment.Bone.rotation);
                segment.Body.linearVelocity = Vector3.zero;
                segment.Body.angularVelocity = Vector3.zero;
                segment.HasTarget = false;
            }

            EndGrips();
            _strength = 1f;
            _mode = Mode.Tracking;
            _tracking = TrackingGain;
            SetGravity(false);
            SetMuscle(TrackingMuscle);
            _weight = 0f;
        }

        /// <summary>Starts a fall: tracking lets go for <paramref name="seconds"/> and the body falls with the motion it has.</summary>
        public void Fall(float seconds)
        {
            if (_mode == Mode.Thrown)
            {
                return;
            }

            if (_mode == Mode.Falling)
            {
                _fallSeconds = Mathf.Max(_fallSeconds, _timer + seconds);
                return;
            }

            _mode = Mode.Falling;
            _timer = 0f;
            _fallSeconds = seconds;
            _tracking = 0f;
            SetGravity(true);
            SetMuscle(FallingMuscle);
        }

        /// <summary>Reads this frame's animated pose (the bones hold it now) as the tracking target and advances the fall timers.</summary>
        public void BeforeStep(float deltaTime)
        {
            float dt = Mathf.Max(deltaTime, 1e-4f);
            foreach (Segment segment in _segments)
            {
                Vector3 position = segment.Bone.position;
                Quaternion rotation = segment.Bone.rotation;
                if (segment.HasTarget)
                {
                    segment.TargetVelocity = Vector3.ClampMagnitude((position - segment.TargetPosition) / dt, 10f);
                    segment.TargetAngularVelocity = Vector3.ClampMagnitude(AngularVelocity(segment.TargetRotation, rotation, dt), 20f);
                }

                segment.TargetPosition = position;
                segment.TargetRotation = rotation;
                segment.HasTarget = true;
                if (segment.Joint != null)
                {
                    Quaternion targetLocal = Quaternion.Inverse(segment.Parent.Bone.rotation) * rotation;
                    // Joint axes match the segment's local axes, so the target is a plain local-space delta.
                    segment.Joint.targetRotation = Quaternion.Inverse(targetLocal) * segment.StartLocal;
                }
            }

            switch (_mode)
            {
                case Mode.Falling:
                    _timer += deltaTime;
                    if (_timer >= _fallSeconds)
                    {
                        _mode = Mode.Recovering;
                        _timer = 0f;
                        SetMuscle(TrackingMuscle);
                    }

                    break;
                case Mode.Recovering:
                    _timer += deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, _timer / RecoverSeconds);
                    _tracking = TrackingGain * t;
                    if (t > 0.5f)
                    {
                        SetGravity(false);
                    }

                    if (_timer >= RecoverSeconds)
                    {
                        _mode = Mode.Tracking;
                        _tracking = TrackingGain;
                    }

                    break;
            }

            _weight = 1f;
        }

        /// <summary>
        /// Before each physics substep: a damped spring accelerates every segment towards its animated
        /// pose and motion. The acceleration is capped, so where the pose would push into the mat or
        /// the opponent, the contact holds and the limb presses against it instead.
        /// </summary>
        public void Drive()
        {
            if (_tracking <= 0f)
            {
                return;
            }

            float k = TrackingFrequency * TrackingFrequency * _tracking;
            float c = 2f * TrackingFrequency * _tracking;
            foreach (Segment segment in _segments)
            {
                Rigidbody body = segment.Body;
                Vector3 linear = (k * (segment.TargetPosition - body.position)) + (c * (segment.TargetVelocity - body.linearVelocity));
                body.AddForce(Vector3.ClampMagnitude(linear, MaxTrackingAcceleration * _strength), ForceMode.Acceleration);
                Vector3 error = AngularVelocity(body.rotation, segment.TargetRotation, 1f);
                Vector3 angular = (k * error) + (c * (segment.TargetAngularVelocity - body.angularVelocity));
                body.AddTorque(Vector3.ClampMagnitude(angular, MaxTrackingAngularAcceleration * _strength), ForceMode.Acceleration);
            }
        }

        /// <summary>After the physics step: the bones take the physics body's pose (parents before children).</summary>
        public void AfterStep()
        {
            if (_weight <= 0f)
            {
                return;
            }

            foreach (Segment segment in _segments)
            {
                Quaternion rotation = Quaternion.Slerp(segment.Bone.rotation, segment.Body.rotation, _weight);
                if (segment.Parent == null)
                {
                    segment.Bone.SetPositionAndRotation(Vector3.Lerp(segment.Bone.position, segment.Body.position, _weight), rotation);
                }
                else
                {
                    segment.Bone.rotation = rotation;
                }
            }
        }

        private static Vector3 AngularVelocity(Quaternion from, Quaternion to, float seconds)
        {
            Quaternion delta = to * Quaternion.Inverse(from);
            if (delta.w < 0f)
            {
                delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            }

            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (float.IsNaN(axis.x) || float.IsInfinity(axis.x) || angle < 1e-3f)
            {
                return Vector3.zero;
            }

            return axis * (angle * Mathf.Deg2Rad / seconds);
        }

        private void SetGravity(bool on)
        {
            foreach (Segment segment in _segments)
            {
                segment.Body.useGravity = on;
            }
        }

        public void Destroy()
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_root);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
        }

        private void SetMuscle(float strength)
        {
            foreach (Segment segment in _segments)
            {
                if (segment.Joint == null)
                {
                    continue;
                }

                // Stiffness scaled by the mass the joint has to move, critically damped.
                float mass = segment.Body.mass + (segment.Parent.Body.mass * 0.5f);
                float spring = 900f * strength * mass;
                segment.Joint.slerpDrive = new JointDrive
                {
                    positionSpring = spring,
                    positionDamper = 2f * Mathf.Sqrt(spring * mass) * 0.6f,
                    maximumForce = float.MaxValue,
                };
                segment.Muscle = strength;
            }
        }
    }
}
