using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// The physically simulated body of one wrestler (display only: nothing here feeds back into the
    /// simulation). Sixteen rigid segments, hands and feet included, joined into one articulation
    /// (reduced coordinates, so joints never stretch or come apart under load), always under gravity.
    ///
    /// The animation is only what the wrestler is trying to do. Two things carry it out, and both are
    /// limited the way a person is:
    /// <list type="bullet">
    /// <item><b>Muscles</b>: every joint is driven towards its animated angle by a motor whose torque is
    /// capped at what that joint of an athlete of this size and strength can produce. Muscles are
    /// internal forces: they move the limbs but cannot move the body as a whole.</item>
    /// <item><b>Balance</b>: the body as a whole is moved by the ground pushing back, applied as one
    /// force and torque on the pelvis. It only exists while feet, knees, hands or the trunk actually
    /// touch the mat, it can only push (never pull the body down), it is limited by leg strength, its
    /// sideways part by friction, and its torque by how far the support reaches. Off the mat (thrown,
    /// lifted, falling) there is none, so the body flies, drops and lands under gravity alone.</item>
    /// </list>
    /// Holds between the wrestlers are <see cref="GripLink"/>s: strength-capped force pairs between
    /// segments, so a pull, a push or a carried weight reaches the other body and comes back as a load.
    /// </summary>
    public sealed class PhysicalBody
    {
        private enum Mode
        {
            /// <summary>Muscles and balance follow the animation.</summary>
            Active,

            /// <summary>Thrown: no balance of their own, held by the thrower's grips, carried round their fulcrum.</summary>
            Thrown,

            /// <summary>Lost control: no balance, weak muscles, gravity takes the body down.</summary>
            Falling,

            /// <summary>Balance and muscles coming back after a fall.</summary>
            Recovering,
        }

        private sealed class Segment
        {
            public string Name;
            public Transform Bone;
            public ArticulationBody Body;
            public Collider Shape;
            public Segment Parent;
            public int Index;

            /// <summary>Rotation relative to the parent when the joint is at zero (bind pose).</summary>
            public Quaternion BindLocal;

            /// <summary>Peak joint torque for this athlete (N·m) and the share of it the joint uses now.</summary>
            public float Torque;

            /// <summary>Inertia the joint moves (kg·m²), for critical damping.</summary>
            public float MovedInertia;

            /// <summary>How much this segment touching the mat supports the body (feet 0.5, knees 0.45, ...).</summary>
            public float SupportWeight;

            /// <summary>The animated rotation relative to the parent, as the muscles see it (after activation).</summary>
            public Quaternion TargetLocal = Quaternion.identity;

            public Vector3 JointTarget;
            public Vector3 JointTargetVelocity;
            public bool HasTarget;

            /// <summary>Direction along the limb in the segment's own frame (its twist axis).</summary>
            public Vector3 Axis = Vector3.up;

            // Knee flexion axis in the lower-leg frame at construction; zero is the bent stance.
            public bool Knee;
            public Vector3 KneeAxis;
            public float KneeLower;
            public float KneeUpper;

            public Vector3 CapsuleA;
            public Vector3 CapsuleB;
            public float Radius;
            public bool OnMat;
        }

        /// <summary>
        /// Segments: name, parent, rig bone giving its frame, bone its capsule runs to (null: sphere for
        /// the head, box for a foot), mass share (de Leva), radius as a share of height, peak joint
        /// torque in N·m for a 75 kg athlete of average strength, and support weight on the mat.
        /// </summary>
        private static readonly (string Name, string Parent, string Bone, string End, float Mass, float Radius, float Torque, float Support)[] Layout =
        {
            ("Hips", null, "Hips", "Spine", 0.112f, 0.062f, 0f, 0.6f),
            ("Spine", "Hips", "Spine", "Spine2", 0.163f, 0.058f, 560f, 0.6f),
            ("Spine2", "Spine", "Spine2", "Neck", 0.160f, 0.063f, 500f, 0.6f),
            ("Head", "Spine2", "Neck", null, 0.069f, 0.058f, 170f, 0.2f),
            ("LeftArm", "Spine2", "LeftArm", "LeftForeArm", 0.027f, 0.028f, 285f, 0.15f),
            ("LeftForeArm", "LeftArm", "LeftForeArm", "LeftHand", 0.016f, 0.023f, 185f, 0.25f),
            ("LeftHand", "LeftForeArm", "LeftHand", "LeftHandMiddle1", 0.006f, 0.017f, 75f, 0.35f),
            ("RightArm", "Spine2", "RightArm", "RightForeArm", 0.027f, 0.028f, 285f, 0.15f),
            ("RightForeArm", "RightArm", "RightForeArm", "RightHand", 0.016f, 0.023f, 185f, 0.25f),
            ("RightHand", "RightForeArm", "RightHand", "RightHandMiddle1", 0.006f, 0.017f, 75f, 0.35f),
            ("LeftUpLeg", "Hips", "LeftUpLeg", "LeftLeg", 0.142f, 0.042f, 480f, 0.3f),
            ("LeftLeg", "LeftUpLeg", "LeftLeg", "LeftFoot", 0.043f, 0.03f, 420f, 0.45f),
            ("LeftFoot", "LeftLeg", "LeftFoot", null, 0.014f, 0f, 240f, 0.5f),
            ("RightUpLeg", "Hips", "RightUpLeg", "RightLeg", 0.142f, 0.042f, 480f, 0.3f),
            ("RightLeg", "RightUpLeg", "RightLeg", "RightFoot", 0.043f, 0.03f, 420f, 0.45f),
            ("RightFoot", "RightLeg", "RightFoot", null, 0.014f, 0f, 240f, 0.5f),
        };

        /// <summary>Joint error (radians) at which a muscle pulls with its full torque.</summary>
        private const float FullTorqueAngle = 0.28f;

        /// <summary>Muscle activation time (s): how quickly a change in what the animation asks for reaches the muscles.</summary>
        private const float ActivationSeconds = 0.035f;

        /// <summary>Fastest the hips rise (m/s) when the animation stands a wrestler up.</summary>
        private const float MaxRiseSpeed = 1.35f;

        /// <summary>Fastest a joint target may move (degrees per second; a pitcher's elbow peaks near 2000).</summary>
        private const float MaxJointSpeed = 900f;

        /// <summary>While dropping, hips more than <see cref="DropGap"/> (m) above their place and asked down faster than <see cref="DropSpeed"/> (m/s), the legs keep this share of their strength, changing over <see cref="LegToneSeconds"/>.</summary>
        private const float DropGap = 0.06f;
        private const float DropSpeed = 0.4f;
        private const float DroppingLegTone = 0.2f;
        private const float LegToneSeconds = 0.08f;

        /// <summary>Muscle strength shares by mode.</summary>
        private const float ActiveMuscle = 1f;
        private const float ThrownMuscle = 0.55f;
        private const float FallingMuscle = 0.3f;

        /// <summary>Balance tracking: natural frequency (Hz) and damping ratio for position and orientation.</summary>
        private const float BalanceFrequency = 3f;
        private const float BalanceDamping = 1f;
        private const float TurnFrequency = 3.5f;

        /// <summary>Whole-body radius of gyration (m) for turning the body as one.</summary>
        private const float GyrationRadius = 0.3f;

        /// <summary>Most the legs can push, in body weights, at average strength, nearly straight (sprinters and lifters reach 2.5–3) and from a deep crouch or the mat.</summary>
        private const float LegCapacity = 2.8f;
        private const float DeepLegCapacity = 1.6f;

        /// <summary>
        /// Legs push hardest nearly straight and weakest deeply bent (the muscles' force–length
        /// relation): from the mat or a deep crouch a wrestler rises slowly, helped by a hand post.
        /// </summary>
        private float LegCapacityAt(float hipHeight)
        {
            float standing = 0.5f * _height;
            return Mathf.Lerp(DeepLegCapacity, LegCapacity, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f * standing, 0.8f * standing, hipHeight)));
        }

        /// <summary>Friction between shoes, knees or skin and the mat.</summary>
        private const float MatFriction = 1.05f;

        /// <summary>
        /// Torque the support can turn the body with, per newton pressing on the mat (m): the centre
        /// of pressure moving within the base (about 0.15 m) plus friction at the contacts acting at
        /// the height of the centre of mass (about 0.9 m × friction).
        /// </summary>
        private const float SupportLever = 0.95f;

        /// <summary>A segment closer than this to the mat (m) bears weight.</summary>
        private const float ContactHeight = 0.042f;

        private const float RecoverSeconds = 0.5f;

        /// <summary>A fall lasts at least this long (s), and ends early once the body rests on the mat moving slower than <see cref="SettledSpeed"/> (m/s).</summary>
        private const float MinimumFallSeconds = 0.3f;
        private const float SettledSpeed = 0.5f;

        /// <summary>Bracing while carrying a thrown opponent: more of the legs' and trunk's strength in play.</summary>
        private const float BraceShare = 1.35f;

        private static PhysicsMaterial _skin;

        private static PhysicsMaterial Skin => _skin != null ? _skin : (_skin = new PhysicsMaterial("Wrestler")
        {
            dynamicFriction = 0.7f,
            staticFriction = 0.8f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Minimum,
        });

        private readonly List<Segment> _segments = new List<Segment>();
        private readonly Dictionary<string, Segment> _byName = new Dictionary<string, Segment>(StringComparer.Ordinal);
        private readonly Collider[] _colliders;
        private readonly GameObject _root;
        private readonly Transform _spine1;
        private readonly float _mass;
        private readonly float _height;

        private Mode _mode = Mode.Active;
        private float _timer;
        private float _fallSeconds;
        private float _weight;
        private float _upperStrength = 1f;
        private float _lowerStrength = 1f;
        private float _brace = 1f;
        private float _balanceShare = 1f;
        private float _authority = 1f;
        private float _muscle = -1f;
        private float _muscleTarget = 1f;
        private float _legTone = 1f;
        private float _appliedLegTone = 1f;

        private PhysicalBody _carrier;
        private float _carried;

        private Vector3 _rootTarget;
        private Quaternion _rootTargetRotation = Quaternion.identity;
        private Vector3 _rootTargetVelocity;
        private Vector3 _rootTargetAngularVelocity;
        private bool _hasRootTarget;

        /// <summary>Diagnostics sink for throw drive traces (tools only; null in the game).</summary>
        public static Action<string> Trace;

        public PhysicalBody(Func<string, Transform> bone, float heightMetres, float massKg, string name)
        {
            _mass = massKg;
            _height = heightMetres;
            _root = new GameObject(name + " body");
            _spine1 = bone("Spine1");

            foreach ((string segmentName, string parent, string boneName, string end, float massShare, float radiusShare, float torque, float support) in Layout)
            {
                Transform t = bone(boneName);
                var go = new GameObject(segmentName);
                go.transform.SetParent(parent == null ? _root.transform : _byName[parent].Body.transform, false);
                go.transform.SetPositionAndRotation(t.position, t.rotation);
                var segment = new Segment
                {
                    Name = segmentName,
                    Bone = t,
                    Index = _segments.Count,
                    SupportWeight = support,
                    Torque = torque * (massKg / 75f),
                    Parent = parent == null ? null : _byName[parent],
                };

                segment.Shape = AddShape(go, segment, bone, end, radiusShare * heightMetres);
                Vector3 towards = end != null ? bone(end).position - t.position : (boneName == "Neck" ? bone("Head").position - t.position : bone(segmentName.Replace("Foot", "ToeBase")).position - t.position);
                segment.Axis = towards.sqrMagnitude > 1e-8f ? (Quaternion.Inverse(t.rotation) * towards).normalized : Vector3.up;
                var body = go.AddComponent<ArticulationBody>();
                body.mass = massKg * massShare;
                body.useGravity = true;
                body.linearDamping = 0.02f;
                body.angularDamping = 0.05f;
                body.jointFriction = 0.02f;
                body.maxAngularVelocity = 40f;
                body.maxDepenetrationVelocity = 2f;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                if (parent == null)
                {
                    body.solverIterations = 24;
                    body.solverVelocityIterations = 4;
                }
                else
                {
                    body.jointType = ArticulationJointType.SphericalJoint;
                    body.anchorPosition = Vector3.zero;
                    body.anchorRotation = Quaternion.identity;
                    body.matchAnchors = true;
                    body.swingYLock = ArticulationDofLock.FreeMotion;
                    body.swingZLock = ArticulationDofLock.FreeMotion;
                    body.twistLock = ArticulationDofLock.FreeMotion;
                    segment.BindLocal = Quaternion.Inverse(segment.Parent.Bone.rotation) * t.rotation;

                    if (segmentName == "LeftLeg" || segmentName == "RightLeg")
                    {
                        // The stance is already bent. The cross product points along the flexion
                        // axis in the direction that increases the measured thigh/shin angle.
                        Vector3 thigh = (t.position - segment.Parent.Bone.position).normalized;
                        Vector3 shin = (bone(end).position - t.position).normalized;
                        Vector3 hinge = Vector3.Cross(thigh, shin);
                        if (hinge.sqrMagnitude < 1e-6f)
                        {
                            throw new InvalidOperationException("Cannot calibrate a knee from a straight or degenerate construction pose.");
                        }

                        float stanceFlexion = Vector3.Angle(thigh, shin);
                        segment.Knee = true;
                        segment.KneeAxis = Quaternion.Inverse(t.rotation) * hinge.normalized;
                        // Approximate passive envelope, not an individual diagnosis: in-vivo full
                        // range measurements averaged about -4 to 150 degrees (Kono et al., 2024,
                        // https://pmc.ncbi.nlm.nih.gov/articles/PMC10850127/). A hinge omits tibial
                        // rotation and the changing anatomical flexion axis.
                        segment.KneeLower = -5f - stanceFlexion;
                        segment.KneeUpper = 150f - stanceFlexion;
                        body.jointType = ArticulationJointType.RevoluteJoint;
                        body.anchorRotation = Quaternion.FromToRotation(Vector3.right, segment.KneeAxis);
                        body.twistLock = ArticulationDofLock.LimitedMotion;
                        ArticulationDrive drive = body.xDrive;
                        drive.lowerLimit = segment.KneeLower;
                        drive.upperLimit = segment.KneeUpper;
                        body.xDrive = drive;
                    }
                }

                segment.Body = body;
                _byName[segmentName] = segment;
                _segments.Add(segment);
            }

            MeasureInertia();
            var colliders = _root.GetComponentsInChildren<Collider>();
            _colliders = colliders;
            Register();
            // A wrestler's own segments never collide with each other; only with the mat and the opponent.
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].sharedMaterial = Skin;
                for (int j = i + 1; j < colliders.Length; j++)
                {
                    Physics.IgnoreCollision(colliders[i], colliders[j]);
                }
            }

            SetMuscle(ActiveMuscle);
        }

        /// <summary>The wrestler's mass (kg).</summary>
        public float Mass => _mass;

        /// <summary>0 while the animation is shown as is (snapshots), 1 once the physical body is.</summary>
        public float Weight => _weight;

        public bool IsFalling => _mode == Mode.Falling;

        public bool IsThrown => _mode == Mode.Thrown;

        /// <summary>Share (0–1) of the body's weight the mat contacts can carry now: feet, knees, hands or trunk on the mat.</summary>
        public float Support { get; private set; }

        /// <summary>Distance (m) from the pelvis to where the animation wants it (diagnostics).</summary>
        public float TrackingError => _hasRootTarget ? (Root.Body.transform.position - _rootTarget).magnitude : 0f;

        /// <summary>Pelvis tilt from vertical, physical and animated (degrees; positive leans the trunk back), and the angle between the two orientations (diagnostics).</summary>
        public string TiltReport
        {
            get
            {
                Transform root = Root.Body.transform;
                float Tilt(Quaternion rotation) => Vector3.SignedAngle(Vector3.up, rotation * Vector3.up, rotation * Vector3.right);
                return _hasRootTarget
                    ? FormattableString.Invariant($"tilt {Tilt(root.rotation):F0}° want {Tilt(_rootTargetRotation):F0}° off {Quaternion.Angle(root.rotation, _rootTargetRotation):F0}°")
                    : "-";
            }
        }

        /// <summary>Where the animation wants the pelvis (diagnostics).</summary>
        public Vector3 PelvisTarget => _rootTarget;

        /// <summary>Ground push the balance applied in the last substep, in body weights (diagnostics).</summary>
        public float GroundPush { get; private set; }

        /// <summary>True when the balance wanted more than the support could give in the last substep (diagnostics).</summary>
        public bool Overpowered { get; private set; }

        /// <summary>Which limit the balance last hit: "" none, "legs", "friction" or "reach" (diagnostics).</summary>
        public string Limit { get; private set; } = string.Empty;

        /// <summary>Every joint's gap (degrees) from its animated angle, and the twist part of it about the limb (diagnostics).</summary>
        public IEnumerable<(string Joint, float Error, float Twist, float Target)> JointErrors()
        {
            foreach (Segment segment in _segments)
            {
                if (segment.Parent != null && segment.HasTarget)
                {
                    Vector3 actual = JointAngles(segment, segment.Parent.Body.transform.rotation, segment.Body.transform.rotation);
                    Quaternion actualRotation = segment.Knee ? Quaternion.Inverse(segment.Parent.Body.transform.rotation) * segment.Body.transform.rotation : FromAngles(segment, actual);
                    Quaternion wantedRotation = segment.Knee ? segment.TargetLocal : FromAngles(segment, segment.JointTarget);
                    Quaternion gap = Quaternion.Inverse(actualRotation) * wantedRotation;
                    yield return (segment.Name, Quaternion.Angle(actualRotation, wantedRotation), Mathf.Abs(TwistAngle(gap, segment.Axis)), segment.JointTarget.magnitude);
                }
            }
        }

        private static Quaternion FromAngles(Segment segment, Vector3 degrees) => segment.Knee ? Quaternion.AngleAxis(degrees.x, segment.KneeAxis) : degrees.sqrMagnitude < 1e-12f ? Quaternion.identity : Quaternion.AngleAxis(degrees.magnitude, degrees / degrees.magnitude);

        /// <summary>Signed angle (degrees) of the part of <paramref name="rotation"/> about <paramref name="axis"/>.</summary>
        private static float TwistAngle(Quaternion rotation, Vector3 axis)
        {
            var v = new Vector3(rotation.x, rotation.y, rotation.z);
            float along = Vector3.Dot(v, axis);
            float angle = 2f * Mathf.Atan2(along, rotation.w) * Mathf.Rad2Deg;
            return angle > 180f ? angle - 360f : angle < -180f ? angle + 360f : angle;
        }

        /// <summary>Hip height above the mat in metres (diagnostics).</summary>
        public float HipHeight => Root.Body.transform.position.y;

        /// <summary>Hip position in metres (diagnostics).</summary>
        public Vector3 HipPosition => Root.Body.transform.position;

        private Segment Root => _segments[0];

        /// <summary>A segment's physical body, by name ("Hips", "LeftHand", ...).</summary>
        public ArticulationBody Link(string segmentName) => Find(segmentName).Body;

        /// <summary>Where a hand closes on what it holds: the middle of the palm, in the hand segment's space.</summary>
        public Vector3 PalmAnchor(string handName)
        {
            Segment hand = Find(handName);
            return hand.Body.transform.InverseTransformPoint(Centre(hand));
        }

        /// <summary>Centre of a modeled foot box's sole, in its articulation's metre coordinates.
        /// This is a collision-geometry proxy, not a captured shoe-surface measurement.</summary>
        public Vector3 SoleAnchor(string footName)
        {
            Segment foot = Find(footName);
            if (!(foot.Shape is BoxCollider box)) throw new ArgumentException("A sole requires a foot box.", nameof(footName));
            Vector3 point = box.transform.TransformPoint(box.center - (Vector3.up * box.size.y * .5f));
            return foot.Body.transform.InverseTransformPoint(point);
        }

        /// <summary>Radius (m) of a segment's collision volume (half the width of a foot's box).</summary>
        public float Radius(string segmentName) => Find(segmentName).Radius;

        /// <summary>The holds of a throw on this (thrown) body: the thrower's hands and traps on its segments, and the seat on the fulcrum.</summary>
        public IReadOnlyList<GripLink> ThrowLinks => _throwLinks;

        /// <summary>World point a grip holds on a segment (the middle of its volume).</summary>
        public Vector3 GripPoint(string segment) => Centre(Find(segment));

        /// <summary>
        /// The segment of this body whose surface is nearest <paramref name="point"/>, the nearest point
        /// on it, and the distance (0 inside). Used to close a hand on whatever is under it.
        /// </summary>
        public (ArticulationBody Body, Vector3 Point, float Distance) Nearest(Vector3 point) => Nearest(point, null);

        /// <summary>
        /// The nearest point on the surface of the named segments (all of them when
        /// <paramref name="segmentNames"/> is null), with its body and distance.
        /// </summary>
        public (ArticulationBody Body, Vector3 Point, float Distance) Nearest(Vector3 point, IReadOnlyList<string> segmentNames)
        {
            Segment best = null;
            Vector3 bestPoint = point;
            float bestDistance = float.MaxValue;
            foreach (Segment segment in _segments)
            {
                if (segmentNames != null && !Contains(segmentNames, segment.Name))
                {
                    continue;
                }

                Vector3 closest = ClosestOnSurface(segment, point);
                float distance = (closest - point).magnitude;
                if (distance < bestDistance)
                {
                    best = segment;
                    bestPoint = closest;
                    bestDistance = distance;
                }
            }

            return (best.Body, bestPoint, bestDistance);
        }

        /// <summary>Nearest boundary of the named segments, including for points inside a shape.
        /// Distance is the distance to that boundary, not the volume gap used by Nearest.</summary>
        public (ArticulationBody Body, Vector3 Point, float Distance) NearestSurface(Vector3 point, IReadOnlyList<string> segmentNames = null)
        {
            Segment best = null;
            Vector3 bestPoint = point;
            float bestDistance = float.MaxValue;
            foreach (Segment segment in _segments)
            {
                if (segmentNames != null && !Contains(segmentNames, segment.Name)) continue;
                Vector3 closest = ClosestBoundary(segment, point);
                float distance = (closest - point).magnitude;
                if (distance < bestDistance)
                {
                    best = segment;
                    bestPoint = closest;
                    bestDistance = distance;
                }
            }
            return (best.Body, bestPoint, bestDistance);
        }

        /// <summary>Strength from the wrestler's attributes (0–100): arms and trunk from upper-body strength, legs from lower-body strength.</summary>
        public void SetStrength(float upperBody, float lowerBody)
        {
            _upperStrength = 0.75f + (0.5f * Mathf.Clamp01(upperBody / 100f));
            _lowerStrength = 0.75f + (0.5f * Mathf.Clamp01(lowerBody / 100f));
            _muscle = -1f;
            SetMuscle(ModeMuscle());
        }

        /// <summary>Capsules <see cref="GetContactCapsules"/> gives: one per segment.</summary>
        public const int ContactCapsules = 16;

        /// <summary>
        /// The body's parts as capsules for contact shading, at <paramref name="offset"/>: each end's
        /// world position, with the radius in w of <paramref name="ends"/> (a foot runs heel to toe
        /// along its sole, the head is a sphere). They are the physical body, which is what is shown.
        /// </summary>
        public void GetContactCapsules(Vector4[] ends, Vector4[] otherEnds, int offset)
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                Segment segment = _segments[i];
                Vector3 a;
                Vector3 b;
                if (segment.Shape is BoxCollider box)
                {
                    Transform shape = box.transform;
                    float sole = (-box.size.y * 0.5f) + segment.Radius;
                    float reach = box.size.z * 0.45f;
                    a = shape.TransformPoint(box.center + new Vector3(0f, sole, -reach));
                    b = shape.TransformPoint(box.center + new Vector3(0f, sole, reach));
                }
                else
                {
                    Transform link = segment.Body.transform;
                    a = link.TransformPoint(segment.CapsuleA);
                    b = link.TransformPoint(segment.CapsuleB);
                }

                ends[offset + i] = new Vector4(a.x, a.y, a.z, segment.Radius);
                otherEnds[offset + i] = new Vector4(b.x, b.y, b.z, 0f);
            }
        }

        /// <summary>Strength of the arms and trunk, and of the legs, relative to an average athlete (0.75–1.25).</summary>
        public float UpperStrength => _upperStrength;

        public float LowerStrength => _lowerStrength;

        /// <summary>The engine's balance (0 falling, 1 stable): an off-balance wrestler cannot push off the mat as hard.</summary>
        public void SetBalance(float balance) => _balanceShare = Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(balance));

        /// <summary>
        /// Held up by the opponent (a lift, a carry, a body on the shoulders) with a share (0–1) of their
        /// support, or not (null). The carrier's push is a force pair: it holds this body up and
        /// presses down on the carrier, whose own balance then has to bear the weight.
        /// </summary>
        public void SetCarrier(PhysicalBody carrier, float share)
        {
            _carrier = carrier;
            _carried = carrier == null ? 0f : Mathf.Clamp01(share);
        }

        /// <summary>Braces the body while it carries a thrown opponent, or relaxes it.</summary>
        public void SetCarrying(bool carrying)
        {
            _brace = carrying ? BraceShare : 1f;
            _muscle = -1f;
            SetMuscle(ModeMuscle());
        }

        /// <summary>Lets go: the body stops balancing and its muscles go slack for <paramref name="seconds"/>, so gravity and its momentum take it down; then it recovers.</summary>
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
            _authority = 0f;
            SetMuscle(FallingMuscle);
        }

        /// <summary>
        /// Puts the body exactly on the animated pose at rest (bout start, restarts, snapshots). The
        /// physics engine applies the move on its next step; <see cref="Settle"/> makes that step.
        /// </summary>
        public void Reset()
        {
            EndThrowLinks();
            Segment root = Root;
            root.Body.TeleportRoot(root.Bone.position, root.Bone.rotation);
            root.Body.linearVelocity = Vector3.zero;
            root.Body.angularVelocity = Vector3.zero;
            foreach (Segment segment in _segments)
            {
                segment.HasTarget = false;
                if (segment.Parent == null)
                {
                    continue;
                }

                Vector3 angles = JointAngles(segment, segment.Parent.Bone.rotation, segment.Bone.rotation);
                if (segment.Knee)
                {
                    angles.x = Mathf.Clamp(angles.x, segment.KneeLower, segment.KneeUpper);
                }
                Vector3 radians = angles * Mathf.Deg2Rad;
                segment.Body.jointPosition = segment.Knee ? new ArticulationReducedSpace(radians.x) : new ArticulationReducedSpace(radians.x, radians.y, radians.z);
                segment.Body.jointVelocity = segment.Knee ? new ArticulationReducedSpace(0f) : new ArticulationReducedSpace(0f, 0f, 0f);
                segment.Body.SetDriveTarget(ArticulationDriveAxis.X, angles.x);
                segment.Body.SetDriveTargetVelocity(ArticulationDriveAxis.X, 0f);
                if (!segment.Knee)
                {
                    segment.Body.SetDriveTarget(ArticulationDriveAxis.Y, angles.y);
                    segment.Body.SetDriveTarget(ArticulationDriveAxis.Z, angles.z);
                    segment.Body.SetDriveTargetVelocity(ArticulationDriveAxis.Y, 0f);
                    segment.Body.SetDriveTargetVelocity(ArticulationDriveAxis.Z, 0f);
                }
            }

            _hasRootTarget = false;
            ClearLoads();
            FinishLoads();
            _carrier = null;
            _carried = 0f;
            _brace = 1f;
            _mode = Mode.Active;
            _authority = 1f;
            SetMuscle(ActiveMuscle);
            _weight = 0f;
        }

        /// <summary>
        /// Applies resets made since the last physics step with a step too short to move anything, so
        /// the bodies are where they were put before anything collides: otherwise the next real step
        /// would start from where they were (two bodies built in one place overlap completely).
        /// </summary>
        public static void Settle() => Physics.Simulate(SettleSeconds);

        private const float SettleSeconds = 1e-5f;

        /// <summary>
        /// Reads this frame's animated pose (the rig's bones hold it now) as what the muscles and the
        /// balance aim for, and advances fall and recovery.
        /// </summary>
        public void BeforeStep(float deltaTime)
        {
            float dt = Mathf.Max(deltaTime, 1e-4f);
            ClearLoads();
            // Muscles cannot change their pull instantly: what the animation asks for reaches them
            // through their activation time, so a clip that snaps a limb or drops the hips in a frame
            // is carried out as fast as a body can, not as a kick.
            float follow = 1f - Mathf.Exp(-dt / ActivationSeconds);
            Segment root = Root;
            Vector3 rootPosition = _hasRootTarget ? Vector3.Lerp(_rootTarget, root.Bone.position, follow) : root.Bone.position;
            if (_hasRootTarget && rootPosition.y > _rootTarget.y + (MaxRiseSpeed * dt))
            {
                // Getting up takes a moment: the hips rise no faster than a wrestler stands from the mat.
                rootPosition.y = _rootTarget.y + (MaxRiseSpeed * dt);
            }

            Quaternion rootRotation = _hasRootTarget ? Quaternion.Slerp(_rootTargetRotation, root.Bone.rotation, follow) : root.Bone.rotation;
            if (_hasRootTarget)
            {
                _rootTargetVelocity = Vector3.ClampMagnitude((rootPosition - _rootTarget) / dt, 8f);
                _rootTargetAngularVelocity = Vector3.ClampMagnitude(AngularVelocity(_rootTargetRotation, rootRotation, dt), 15f);
            }

            _rootTarget = rootPosition;
            _rootTargetRotation = rootRotation;
            _hasRootTarget = true;

            foreach (Segment segment in _segments)
            {
                if (segment.Parent == null)
                {
                    continue;
                }

                Quaternion wanted = Quaternion.Inverse(segment.Parent.Bone.rotation) * segment.Bone.rotation;
                segment.TargetLocal = segment.HasTarget ? Quaternion.Slerp(segment.TargetLocal, wanted, follow) : wanted;
                Vector3 target = AnglesOf(segment, segment.TargetLocal);
                if (segment.Knee)
                {
                    target.x = Mathf.Clamp(target.x, segment.KneeLower, segment.KneeUpper);
                }
                segment.JointTargetVelocity = segment.HasTarget ? Vector3.ClampMagnitude((target - segment.JointTarget) / dt, MaxJointSpeed) : Vector3.zero;
                segment.JointTarget = target;
                segment.HasTarget = true;
                ArticulationBody body = segment.Body;
                body.SetDriveTarget(ArticulationDriveAxis.X, target.x);
                body.SetDriveTargetVelocity(ArticulationDriveAxis.X, segment.JointTargetVelocity.x);
                if (!segment.Knee)
                {
                    body.SetDriveTarget(ArticulationDriveAxis.Y, target.y);
                    body.SetDriveTarget(ArticulationDriveAxis.Z, target.z);
                    body.SetDriveTargetVelocity(ArticulationDriveAxis.Y, segment.JointTargetVelocity.y);
                    body.SetDriveTargetVelocity(ArticulationDriveAxis.Z, segment.JointTargetVelocity.z);
                }
            }

            // Dropping (a sprawl, a level change, going to the knees): the hips are above where they
            // should be and on their way down, so the legs give instead of pushing, the way a body
            // drops by letting its legs go. Kept stiff, they would push it back up off the mat.
            bool dropping = Root.Body.transform.position.y > _rootTarget.y + DropGap && _rootTargetVelocity.y < -DropSpeed;
            _legTone = Mathf.MoveTowards(_legTone, dropping ? DroppingLegTone : 1f, dt / LegToneSeconds);

            switch (_mode)
            {
                case Mode.Falling:
                    _timer += deltaTime;
                    // Over once the time is up, or sooner once the body has come down and stopped.
                    if (_timer >= _fallSeconds || (_timer >= MinimumFallSeconds && Support >= 0.6f && CentreOfMassSpeed() < SettledSpeed))
                    {
                        _mode = Mode.Recovering;
                        _timer = 0f;
                    }

                    break;
                case Mode.Recovering:
                    _timer += deltaTime;
                    float t = Mathf.SmoothStep(0f, 1f, _timer / RecoverSeconds);
                    _authority = t;
                    SetMuscle(Mathf.Lerp(FallingMuscle, ActiveMuscle, t));
                    if (_timer >= RecoverSeconds)
                    {
                        _mode = Mode.Active;
                        _authority = 1f;
                        SetMuscle(ActiveMuscle);
                    }

                    break;
            }

            SetMuscle(_muscleTarget);
            _weight = 1f;
        }

        /// <summary>
        /// Before each physics substep: finds what touches the mat and pushes the body as a whole
        /// towards its animated place with what that support allows; then the throw's holds and drive.
        /// </summary>
        public void Drive(float deltaTime)
        {
            _lastSubstep = Mathf.Max(deltaTime, 1e-4f);
            _sensedSeconds += deltaTime;
            SenseSupport();
            if (_mode == Mode.Thrown)
            {
                // Before the turn the defender still stands on their own base, giving way only as they
                // are drawn onto the thrower; once the turn starts they are off their feet.
                _authority = _turning ? 0f : ThrownResistance * (1f - Loaded());
            }

            Balance();
            if (_mode == Mode.Thrown)
            {
                foreach (GripLink link in _throwLinks)
                {
                    link.Apply(deltaTime);
                }
            }
        }

        /// <summary>After the physics steps of a frame: the loads they bore are measured and the rig shows the physical pose.</summary>
        public void AfterStep()
        {
            FinishLoads();
            ShowPose();
        }

        /// <summary>
        /// The rig's bones take the physical body's pose (parents before children). Also for a frame
        /// too short for a physics step, where the animation has just been put on the bones.
        /// </summary>
        public void ShowPose()
        {
            if (_weight <= 0f)
            {
                return;
            }

            foreach (Segment segment in _segments)
            {
                Transform link = segment.Body.transform;
                if (segment.Parent == null)
                {
                    segment.Bone.SetPositionAndRotation(link.position, link.rotation);
                    continue;
                }

                if (segment.Name == "Spine2")
                {
                    // The rig's middle spine bone sits halfway between the two physical spine segments.
                    _spine1.rotation = Quaternion.Slerp(segment.Parent.Body.transform.rotation, link.rotation, 0.5f);
                }

                segment.Bone.rotation = link.rotation;
            }
        }

        /// <summary>Stops this body and another colliding (diagnostics: tells self-made motion from contact).</summary>
        public void IgnoreContactWith(PhysicalBody other)
        {
            foreach (Collider mine in _colliders)
            {
                foreach (Collider theirs in other._colliders)
                {
                    Physics.IgnoreCollision(mine, theirs);
                }
            }
        }

        /// <summary>Deepest overlap in metres between this body's colliders and another's (diagnostics).</summary>
        public float DeepestOverlap(PhysicalBody other)
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

        /// <summary>Pairs of segments (this body's first) whose surfaces are within <paramref name="within"/> metres of another body's, with the gap in cm (diagnostics: what touches what).</summary>
        public string TouchingPairs(PhysicalBody other, float within)
        {
            var pairs = new List<string>();
            foreach (Collider mine in _colliders)
            {
                foreach (Collider theirs in other._colliders)
                {
                    float gap;
                    if (Physics.ComputePenetration(mine, mine.transform.position, mine.transform.rotation, theirs, theirs.transform.position, theirs.transform.rotation, out _, out float depth))
                    {
                        gap = -depth;
                    }
                    else
                    {
                        // Closest points by alternating projection, close enough for capsules and boxes.
                        Vector3 q = theirs.ClosestPoint(mine.bounds.center);
                        Vector3 p = mine.ClosestPoint(q);
                        q = theirs.ClosestPoint(p);
                        p = mine.ClosestPoint(q);
                        gap = Vector3.Distance(p, q);
                    }

                    if (gap <= within)
                    {
                        pairs.Add(FormattableString.Invariant($"{mine.name}-{theirs.name} {gap * 100f:F0}"));
                    }
                }
            }

            return pairs.Count == 0 ? "none" : string.Join(", ", pairs);
        }

        /// <summary>Lowest point of the feet above the mat (m): below zero means sunk into it (diagnostics).</summary>
        public float FootClearance => Mathf.Min(Lowest(Find("LeftFoot")), Lowest(Find("RightFoot")));

        /// <summary>Largest gap (degrees) between a joint and its animated angle (diagnostics).</summary>
        public float WorstJointError(out string joint)
        {
            float worst = 0f;
            joint = "-";
            foreach (Segment segment in _segments)
            {
                if (segment.Parent == null || !segment.HasTarget)
                {
                    continue;
                }

                Vector3 actual = JointAngles(segment, segment.Parent.Body.transform.rotation, segment.Body.transform.rotation);
                Quaternion actualRotation = segment.Knee ? Quaternion.Inverse(segment.Parent.Body.transform.rotation) * segment.Body.transform.rotation : FromAngles(segment, actual);
                Quaternion wantedRotation = segment.Knee ? segment.TargetLocal : FromAngles(segment, segment.JointTarget);
                float error = Quaternion.Angle(actualRotation, wantedRotation);
                if (error > worst)
                {
                    worst = error;
                    joint = segment.Name;
                }
            }

            return worst;
        }

        public void Destroy()
        {
            Unregister();
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_root);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
        }

        private static bool Contains(IReadOnlyList<string> names, string name)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] == name)
                {
                    return true;
                }
            }

            return false;
        }

        private Segment Find(string segmentName) =>
            _byName.TryGetValue(segmentName, out Segment segment) ? segment : throw new ArgumentException($"No physical segment '{segmentName}'.");

        /// <summary>
        /// The joint's angles for a child rotation relative to its parent: the rotation away from the
        /// bind pose as a rotation vector in the child's frame (degrees) for spherical joints;
        /// knee hinges use their signed flexion angle in X.
        /// </summary>
        private static Vector3 JointAngles(Segment segment, Quaternion parent, Quaternion child) => AnglesOf(segment, Quaternion.Inverse(parent) * child);

        private static Vector3 AnglesOf(Segment segment, Quaternion local)
        {
            Quaternion delta = Quaternion.Inverse(segment.BindLocal) * local;
            if (segment.Knee)
            {
                return new Vector3(TwistAngle(delta, segment.KneeAxis), 0f, 0f);
            }
            if (delta.w < 0f)
            {
                delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            }

            var axis = new Vector3(delta.x, delta.y, delta.z);
            float sine = axis.magnitude;
            if (sine < 1e-6f)
            {
                return Vector3.zero;
            }

            float angle = 2f * Mathf.Atan2(sine, delta.w) * Mathf.Rad2Deg;
            return axis * (angle / sine);
        }

        private static Vector3 AngularVelocity(Quaternion from, Quaternion to, float seconds)
        {
            Quaternion delta = to * Quaternion.Inverse(from);
            if (delta.w < 0f)
            {
                delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            }

            var axis = new Vector3(delta.x, delta.y, delta.z);
            float sine = axis.magnitude;
            if (sine < 1e-6f)
            {
                return Vector3.zero;
            }

            float angle = 2f * Mathf.Atan2(sine, delta.w);
            return axis * (angle / sine / seconds);
        }

        /// <summary>Which segments touch the mat now, and how much of the body's weight they can carry.</summary>
        private void SenseSupport()
        {
            float support = 0f;
            foreach (Segment segment in _segments)
            {
                segment.OnMat = Lowest(segment) < ContactHeight;
                if (segment.OnMat)
                {
                    support += segment.SupportWeight;
                }
            }

            Support = Mathf.Min(support, 1f);
            float limbs = 0f;
            foreach (Segment segment in _segments)
            {
                if (segment.OnMat && !IsTrunk(segment))
                {
                    limbs += segment.SupportWeight;
                }
            }

            LimbSupport = Mathf.Min(limbs, 1f);
            // Contacts chatter for a substep or two as the feet roll and shift: a base the body had a
            // moment ago still lets it push (a fast rise, a slow fade over SupportMemorySeconds).
            float fade = _lastSubstep / SupportMemorySeconds;
            HeldSupport = Mathf.Max(Support, HeldSupport - fade);
        }

        /// <summary>
        /// Share of support from feet, knees and hands only: what can push the body up. The trunk or
        /// head on the mat holds it and lets it slide and roll, but a body cannot rise off its back;
        /// getting up takes a hand post or a knee under the hips.
        /// </summary>
        public float LimbSupport { get; private set; }

        private static bool IsTrunk(Segment segment) => segment.Parent == null || segment.Name.StartsWith("Spine", StringComparison.Ordinal) || segment.Name == "Head";

        /// <summary>The support over the last moment (fades over <see cref="SupportMemorySeconds"/>), for efforts like throws that do not stop for a foot rolling.</summary>
        public float HeldSupport { get; private set; }

        private const float SupportMemorySeconds = 0.25f;
        private float _lastSubstep = 1f / 240f;

        /// <summary>
        /// The ground pushing back: the force and torque that would bring the pelvis to its animated
        /// place and turn, limited by the support. Push only (never pulled into the mat), at most the
        /// legs' strength, sideways at most friction allows, turning at most the support reaches.
        /// </summary>
        private void Balance()
        {
            GroundPush = 0f;
            Overpowered = false;
            Limit = string.Empty;
            float carried = _carrier != null && _mode != Mode.Thrown ? _carried * CarrierContact() : 0f;
            float effectiveSupport = Mathf.Max(Support, HeldSupport * 0.65f);
            float support = Mathf.Max(effectiveSupport, carried);
            float share = support * _authority * _balanceShare;
            if (!_hasRootTarget || share <= 0f)
            {
                return;
            }

            ArticulationBody root = Root.Body;
            Vector3 position = root.transform.position;
            float w = 2f * Mathf.PI * BalanceFrequency;
            Vector3 acceleration = (w * w * (_rootTarget - position)) + (2f * BalanceDamping * w * (_rootTargetVelocity - root.GetPointVelocity(position)));
            Vector3 wanted = _mass * (acceleration - Physics.gravity);

            float weight = _mass * -Physics.gravity.y;
            float groundLift = _rootTarget.y > position.y + 0.06f ? Mathf.Max(LimbSupport, effectiveSupport * 0.8f) : Mathf.Max(LimbSupport, effectiveSupport * 0.5f);
            float lifting = Mathf.Max(groundLift, carried) * _authority * _balanceShare;
            float most = lifting * weight * LegCapacityAt(position.y) * _lowerStrength * _brace;
            float up = Mathf.Clamp(wanted.y, 0f, most);
            if (wanted.y > most)
            {
                Overpowered = true;
                Limit = "legs";
            }

            // Friction comes from what presses on the mat: the push, or at least the weight resting on it.
            var sideways = new Vector3(wanted.x, 0f, wanted.z);
            float pressing = Mathf.Max(up, weight * share);
            float grip = MatFriction * pressing;
            if (sideways.magnitude > grip)
            {
                sideways *= grip / sideways.magnitude;
                Overpowered = true;
                Limit = Limit.Length == 0 ? "friction" : Limit;
            }

            var push = new Vector3(sideways.x, up, sideways.z);
            GroundPush = up / weight;

            float wt = 2f * Mathf.PI * TurnFrequency;
            float inertia = _mass * GyrationRadius * GyrationRadius;
            Vector3 turn = AngularVelocity(root.transform.rotation, _rootTargetRotation, 1f);
            Vector3 torque = inertia * ((wt * wt * turn) + (2f * BalanceDamping * wt * (_rootTargetAngularVelocity - root.angularVelocity)));
            float reach = share * pressing * SupportLever;
            if (torque.magnitude > reach)
            {
                torque *= reach / torque.magnitude;
                Overpowered = true;
                Limit = Limit.Length == 0 ? "reach" : Limit;
            }

            if (MeasureFeasibility)
            {
                MeasureContacts(push, torque, carried, weight * share);
            }

            // The push acts at the pelvis, where the legs carry the body; the turn acts on the body as
            // a whole, so it does not wrench the pelvis against the trunk and legs.
            root.AddForceAtPosition(push, position);
            TurnAsOne(torque);
            if (carried > 0f)
            {
                // The carrier's share of the push comes back on the carrier.
                float fromCarrier = carried / (carried + Support);
                ArticulationBody carrierRoot = _carrier.Root.Body;
                carrierRoot.AddForceAtPosition(-push * fromCarrier, carrierRoot.transform.position);
                _carrier.TurnAsOne(-torque * fromCarrier);
                AddOpponentImpulse(0, push * (fromCarrier * _lastSubstep));
                _carrier.AddOpponentImpulse(0, -push * (fromCarrier * _lastSubstep));
                _groundImpulse += push * ((1f - fromCarrier) * _lastSubstep);
            }
            else
            {
                _groundImpulse += push * _lastSubstep;
            }
        }

        /// <summary>
        /// Diagnostics (the physics lab): also solve what the actual contacts could give, every substep,
        /// and report it as <see cref="Feasibility"/>. Off in the game (it costs about as much as the rest).
        /// </summary>
        public static bool MeasureFeasibility;

        /// <summary>
        /// Share (0–1) of the last balance force and turn that the contacts on the mat could have made
        /// within their friction cones (1: all of it; the rest stands in for stepping and for weight
        /// leaned on the opponent). Only measured while <see cref="MeasureFeasibility"/> is set.
        /// </summary>
        public float Feasibility { get; private set; } = 1f;

        private readonly ContactSolver _solver = new ContactSolver();

        /// <summary>Weights a newton-metre of turn against a newton of force when scoring feasibility (1/m).</summary>
        private const float FeasibilityLever = 0.6f;

        private void MeasureContacts(Vector3 push, Vector3 torque, float carried, float resting)
        {
            float fromCarrier = carried > 0f ? carried / (carried + Support) : 0f;
            Vector3 force = push * (1f - fromCarrier);
            Vector3 turn = torque * (1f - fromCarrier);
            float size = force.magnitude + (turn.magnitude * FeasibilityLever);
            if (Support <= 0f || size < 1f)
            {
                Feasibility = 1f;
                return;
            }

            _solver.Clear();
            float total = 0f;
            foreach (Segment segment in _segments)
            {
                total += segment.OnMat ? segment.SupportWeight : 0f;
            }

            float rest = Mathf.Max(0f, (resting * (1f - fromCarrier)) - force.y);
            foreach (Segment segment in _segments)
            {
                if (!segment.OnMat)
                {
                    continue;
                }

                float share = rest * segment.SupportWeight / total;
                if (segment.Shape is BoxCollider box)
                {
                    // Heel and toes, so the centre of pressure can move along the foot.
                    Transform shape = box.transform;
                    float sole = -box.size.y * 0.5f;
                    float reach = box.size.z * 0.45f;
                    _solver.Add(shape.TransformPoint(box.center + new Vector3(0f, sole, -reach)), share * 0.5f);
                    _solver.Add(shape.TransformPoint(box.center + new Vector3(0f, sole, reach)), share * 0.5f);
                    continue;
                }

                Transform link = segment.Body.transform;
                Vector3 a = link.TransformPoint(segment.CapsuleA);
                Vector3 b = link.TransformPoint(segment.CapsuleB);
                Vector3 low = Mathf.Abs(a.y - b.y) < 0.02f ? (a + b) * 0.5f : (a.y < b.y ? a : b);
                _solver.Add(low - (Vector3.up * segment.Radius), share);
            }

            (Vector3 madeForce, Vector3 madeTurn) = _solver.Solve(force, turn, CentreOfMass(), MatFriction);
            float missing = (force - madeForce).magnitude + ((turn - madeTurn).magnitude * FeasibilityLever);
            Feasibility = Mathf.Clamp01(1f - (missing / size));
        }

        /// <summary>
        /// Turns the body as a whole: the torque as a pure couple, each segment pushed along its circle
        /// round the centre of mass. A torque on the pelvis alone would wrench it against the trunk and
        /// legs, making the joints kick (and, with the feet on the mat, lever the body up off it).
        /// </summary>
        private void TurnAsOne(Vector3 torque)
        {
            if (torque.sqrMagnitude < 1e-8f)
            {
                return;
            }

            Vector3 centre = CentreOfMass();
            Vector3 axis = torque.normalized;
            float inertia = 0f;
            foreach (Segment segment in _segments)
            {
                inertia += segment.Body.mass * Vector3.ProjectOnPlane(segment.Body.worldCenterOfMass - centre, axis).sqrMagnitude;
            }

            if (inertia <= 1e-4f)
            {
                return;
            }

            Vector3 alpha = torque / inertia;
            foreach (Segment segment in _segments)
            {
                ArticulationBody body = segment.Body;
                Vector3 r = Vector3.ProjectOnPlane(body.worldCenterOfMass - centre, axis);
                body.AddForceAtPosition(Vector3.Cross(alpha, r) * body.mass, body.worldCenterOfMass);
            }
        }

        /// <summary>
        /// How much the carrier can hold this body up: fully while this body's trunk rests on them,
        /// nothing once it is clear of them (a carrier cannot hold up what they do not touch).
        /// </summary>
        private float CarrierContact()
        {
            float gap = float.MaxValue;
            foreach (string part in Trunk)
            {
                Segment segment = Find(part);
                gap = Mathf.Min(gap, _carrier.Nearest(Centre(segment)).Distance - segment.Radius);
            }

            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CarryContactGap, CarryLostGap, gap));
        }

        private static readonly string[] Trunk = { "Hips", "Spine", "Spine2" };

        /// <summary>Gap (m) between the held trunk and the carrier within which the carrier holds it fully, and beyond which not at all.</summary>
        private const float CarryContactGap = 0.04f;
        private const float CarryLostGap = 0.15f;

        private float ModeMuscle() => _mode switch
        {
            Mode.Thrown => ThrownMuscle,
            Mode.Falling => FallingMuscle,
            _ => ActiveMuscle,
        };

        /// <summary>Sets every joint's muscle: stiffness so the peak torque is reached at <see cref="FullTorqueAngle"/>, critically damped for the inertia it moves.</summary>
        private void SetMuscle(float strength)
        {
            _muscleTarget = strength;
            if (Mathf.Abs(strength - _muscle) < 0.01f && Mathf.Abs(_legTone - _appliedLegTone) < 0.02f)
            {
                return;
            }

            _muscle = strength;
            _appliedLegTone = _legTone;
            foreach (Segment segment in _segments)
            {
                if (segment.Parent == null)
                {
                    continue;
                }

                bool leg = segment.Name.Contains("Leg") || segment.Name.Contains("Foot");
                float torque = segment.Torque * strength * (leg ? _lowerStrength * _legTone : _upperStrength) * _brace;
                float stiffness = torque / FullTorqueAngle;
                float damping = 2f * Mathf.Sqrt(stiffness * segment.MovedInertia);
                // Unity's rotational drives take their targets in degrees and their gains per radian.
                foreach (ArticulationDriveAxis axis in new[] { ArticulationDriveAxis.X, ArticulationDriveAxis.Y, ArticulationDriveAxis.Z })
                {
                    if (segment.Knee && axis != ArticulationDriveAxis.X)
                    {
                        continue;
                    }
                    segment.Body.SetDriveStiffness(axis, stiffness);
                    segment.Body.SetDriveDamping(axis, damping);
                    segment.Body.SetDriveForceLimit(axis, torque);
                }
            }
        }

        /// <summary>For each joint, the inertia of everything beyond it about the joint (kg·m²).</summary>
        private void MeasureInertia()
        {
            foreach (Segment segment in _segments)
            {
                if (segment.Parent == null)
                {
                    continue;
                }

                float inertia = 0f;
                Vector3 joint = segment.Body.transform.position;
                foreach (Segment other in _segments)
                {
                    if (IsWithin(other, segment))
                    {
                        float distance = (Centre(other) - joint).magnitude;
                        inertia += other.Body.mass * ((distance * distance) + (0.25f * other.Radius * other.Radius));
                    }
                }

                segment.MovedInertia = Mathf.Max(inertia, 0.002f);
            }
        }

        private static bool IsWithin(Segment segment, Segment ancestor)
        {
            for (Segment s = segment; s != null; s = s.Parent)
            {
                if (s == ancestor)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The segment's collision shape on a child object, so it can be oriented along the limb:
        /// capsules from the joint to the next, a sphere for the head, a box for each foot from heel
        /// to toes with its sole on the mat.
        /// </summary>
        private Collider AddShape(GameObject link, Segment segment, Func<string, Transform> bone, string end, float radius)
        {
            var shape = new GameObject(segment.Name + " shape");
            shape.transform.SetParent(link.transform, false);
            Vector3 from = segment.Bone.position;
            if (segment.Name.EndsWith("Foot", StringComparison.Ordinal))
            {
                string side = segment.Name.StartsWith("Left", StringComparison.Ordinal) ? "Left" : "Right";
                Vector3 toe = bone(side + "ToeBase").position;
                Vector3 forward = Vector3.ProjectOnPlane(toe - from, Vector3.up);
                forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
                float sole = Mathf.Max(0f, toe.y - (0.012f * _height));
                Vector3 heel = new Vector3(from.x, sole, from.z) - (forward * (0.035f * _height));
                Vector3 tip = new Vector3(toe.x, sole, toe.z) + (forward * (0.03f * _height));
                float top = from.y;
                shape.transform.SetPositionAndRotation(Vector3.Lerp(heel, tip, 0.5f) + (Vector3.up * ((top - sole) * 0.5f)), Quaternion.LookRotation(forward, Vector3.up));
                var box = shape.AddComponent<BoxCollider>();
                box.size = new Vector3(0.055f * _height, top - sole, (tip - heel).magnitude);
                segment.Radius = box.size.x * 0.5f;
                return box;
            }

            if (end == null)
            {
                // Head: a sphere centred in the skull, carried by the neck joint.
                Transform head = bone("Head");
                shape.transform.SetPositionAndRotation(head.position + (head.up * (radius * 0.9f)), head.rotation);
                var sphere = shape.AddComponent<SphereCollider>();
                sphere.radius = radius;
                segment.Radius = radius;
                segment.CapsuleA = shape.transform.localPosition;
                segment.CapsuleB = segment.CapsuleA;
                return sphere;
            }

            Vector3 to = bone(end).position;
            if (segment.Name.EndsWith("Hand", StringComparison.Ordinal))
            {
                // The palm runs on past the knuckles to the middle of the fingers.
                to += (to - from) * 0.6f;
            }

            Vector3 along = to - from;
            float length = along.magnitude;
            shape.transform.SetPositionAndRotation(Vector3.Lerp(from, to, 0.5f), Quaternion.FromToRotation(Vector3.up, along.sqrMagnitude > 1e-8f ? along : Vector3.up));
            var capsule = shape.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = radius;
            capsule.height = Mathf.Max(length + radius, radius * 2f);
            segment.Radius = radius;
            Vector3 half = Vector3.up * Mathf.Max(0f, (capsule.height * 0.5f) - radius);
            segment.CapsuleA = link.transform.InverseTransformPoint(shape.transform.TransformPoint(-half));
            segment.CapsuleB = link.transform.InverseTransformPoint(shape.transform.TransformPoint(half));
            return capsule;
        }

        private static Vector3 Centre(Segment segment) => segment.Shape.bounds.center;

        /// <summary>Height above the mat (m) of the segment's lowest point.</summary>
        private static float Lowest(Segment segment)
        {
            if (segment.Shape is BoxCollider)
            {
                return segment.Shape.bounds.min.y;
            }

            Transform link = segment.Body.transform;
            float a = link.TransformPoint(segment.CapsuleA).y;
            float b = link.TransformPoint(segment.CapsuleB).y;
            return Mathf.Min(a, b) - segment.Radius;
        }

        private static Vector3 ClosestOnSurface(Segment segment, Vector3 point)
        {
            if (segment.Shape is BoxCollider box)
            {
                return box.ClosestPoint(point);
            }

            Transform link = segment.Body.transform;
            Vector3 a = link.TransformPoint(segment.CapsuleA);
            Vector3 b = link.TransformPoint(segment.CapsuleB);
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
            Vector3 axis = a + (ab * t);
            Vector3 outward = point - axis;
            float distance = outward.magnitude;
            return distance <= segment.Radius ? point : axis + (outward * (segment.Radius / distance));
        }

        private static Vector3 ClosestBoundary(Segment segment, Vector3 point)
        {
            if (segment.Shape is BoxCollider box)
            {
                Transform transform = box.transform;
                Vector3 centre = transform.TransformPoint(box.center);
                Vector3 scale = transform.lossyScale;
                Vector3 half = Vector3.Scale(box.size * .5f,
                    new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                Vector3 local = Quaternion.Inverse(transform.rotation) * (point - centre);
                Vector3 closest = new Vector3(Mathf.Clamp(local.x, -half.x, half.x),
                    Mathf.Clamp(local.y, -half.y, half.y), Mathf.Clamp(local.z, -half.z, half.z));
                if (local.x >= -half.x && local.x <= half.x && local.y >= -half.y && local.y <= half.y
                    && local.z >= -half.z && local.z <= half.z)
                {
                    float x = half.x - Mathf.Abs(local.x), y = half.y - Mathf.Abs(local.y), z = half.z - Mathf.Abs(local.z);
                    if (x <= y && x <= z) closest.x = local.x >= 0f ? half.x : -half.x;
                    else if (y <= z) closest.y = local.y >= 0f ? half.y : -half.y;
                    else closest.z = local.z >= 0f ? half.z : -half.z;
                }
                return centre + (transform.rotation * closest);
            }

            Transform link = segment.Body.transform;
            Vector3 a = link.TransformPoint(segment.CapsuleA);
            Vector3 b = link.TransformPoint(segment.CapsuleB);
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
            Vector3 axis = a + (ab * t);
            Vector3 outward = point - axis;
            if (outward.sqrMagnitude < 1e-12f)
            {
                // On the axis, every radial direction is equally near; use a stable perpendicular.
                outward = ab.sqrMagnitude > 1e-8f ? Vector3.Cross(ab, link.right) : link.right;
                if (outward.sqrMagnitude < 1e-12f) outward = Vector3.Cross(ab, link.up);
            }
            return axis + (outward.normalized * segment.Radius);
        }

        // ---------------------------------------------------------------- Loads

        /// <summary>Which body and segment each physical link and collider belongs to, for holds and contacts reporting their loads.</summary>
        private static readonly Dictionary<ArticulationBody, (PhysicalBody Body, int Segment)> LinkOwners = new Dictionary<ArticulationBody, (PhysicalBody, int)>();
        private static readonly Dictionary<Collider, (PhysicalBody Body, int Segment)> ColliderOwners = new Dictionary<Collider, (PhysicalBody, int)>();
        private static bool _listening;

        private readonly Vector3[] _segmentImpulse = new Vector3[Layout.Length];
        private readonly Vector3[] _segmentForce = new Vector3[Layout.Length];
        private Vector3 _opponentImpulse;
        private Vector3 _groundImpulse;
        private float _sensedSeconds;

        /// <summary>
        /// Mean force (N) the opponent put on this body over the last frame: holds, contact between the
        /// bodies, a carry and a throw's drive. Measured, not planned (display only).
        /// </summary>
        public Vector3 OpponentForce { get; private set; }

        /// <summary>Mean force (N) the mat put on this body over the last frame: the legs' push through the support and the contacts' reaction.</summary>
        public Vector3 GroundForce { get; private set; }

        /// <summary>Mean force (N) the opponent put on one segment over the last frame.</summary>
        public Vector3 OpponentForceOn(string segmentName) => _segmentForce[Find(segmentName).Index];

        /// <summary>How much the wrestler's own control is in charge (0 thrown or falling, rising to 1 as they recover).</summary>
        public float Control => _weight <= 0f ? 0f : _mode == Mode.Active ? 1f : _mode == Mode.Recovering ? _authority : 0f;

        /// <summary>A hold's force on a physical link this substep (N·s), credited to the body it belongs to.</summary>
        internal static void AddHoldImpulse(ArticulationBody link, Vector3 impulse)
        {
            if (link != null && LinkOwners.TryGetValue(link, out (PhysicalBody Body, int Segment) owner))
            {
                owner.Body.AddOpponentImpulse(owner.Segment, impulse);
            }
        }

        private void AddOpponentImpulse(int segment, Vector3 impulse)
        {
            _segmentImpulse[segment] += impulse;
            _opponentImpulse += impulse;
        }

        private void Register()
        {
            foreach (Segment segment in _segments)
            {
                LinkOwners[segment.Body] = (this, segment.Index);
                ColliderOwners[segment.Shape] = (this, segment.Index);
                segment.Shape.providesContacts = true;
            }

            if (!_listening)
            {
                Physics.ContactEvent += OnContacts;
                _listening = true;
            }
        }

        private void Unregister()
        {
            foreach (Segment segment in _segments)
            {
                LinkOwners.Remove(segment.Body);
                ColliderOwners.Remove(segment.Shape);
            }
        }

        private void ClearLoads()
        {
            Array.Clear(_segmentImpulse, 0, _segmentImpulse.Length);
            _opponentImpulse = Vector3.zero;
            _groundImpulse = Vector3.zero;
            _sensedSeconds = 0f;
        }

        private void FinishLoads()
        {
            if (_sensedSeconds <= 0f)
            {
                OpponentForce = Vector3.zero;
                GroundForce = Vector3.zero;
                Array.Clear(_segmentForce, 0, _segmentForce.Length);
                return;
            }

            float perSecond = 1f / _sensedSeconds;
            OpponentForce = _opponentImpulse * perSecond;
            GroundForce = _groundImpulse * perSecond;
            for (int i = 0; i < _segmentForce.Length; i++)
            {
                _segmentForce[i] = _segmentImpulse[i] * perSecond;
            }
        }

        /// <summary>
        /// Contact impulses from the physics step: between the two bodies (credited to both, pushing
        /// them apart) and between a body and the mat (pushing it up).
        /// </summary>
        private static void OnContacts(PhysicsScene scene, NativeArray<ContactPairHeader>.ReadOnly headers)
        {
            for (int h = 0; h < headers.Length; h++)
            {
                ContactPairHeader header = headers[h];
                for (int p = 0; p < header.pairCount; p++)
                {
                    ref readonly ContactPair pair = ref header.GetContactPair(p);
                    Vector3 impulse = pair.impulseSum;
                    if (impulse.sqrMagnitude < 1e-12f)
                    {
                        continue;
                    }

                    bool first = ColliderOwners.TryGetValue(pair.collider, out (PhysicalBody Body, int Segment) a);
                    bool second = ColliderOwners.TryGetValue(pair.otherCollider, out (PhysicalBody Body, int Segment) b);
                    if (first && second)
                    {
                        if (a.Body == b.Body)
                        {
                            continue;
                        }

                        // Contact only pushes: the impulse on the first body points away from the second.
                        Vector3 apart = Centre(a.Body._segments[a.Segment]) - Centre(b.Body._segments[b.Segment]);
                        if (Vector3.Dot(impulse, apart) < 0f)
                        {
                            impulse = -impulse;
                        }

                        a.Body.AddOpponentImpulse(a.Segment, impulse);
                        b.Body.AddOpponentImpulse(b.Segment, -impulse);
                    }
                    else if (first || second)
                    {
                        // The mat or platform: it only pushes up.
                        PhysicalBody body = first ? a.Body : b.Body;
                        body._groundImpulse += impulse.y < 0f ? -impulse : impulse;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- Throws

        /// <summary>Grip and arm-wrap holds (stiffness N/m, damping N·s/m) and their share of the thrower's strength.</summary>
        private const float GripSpring = 6000f;
        private const float GripDamper = 150f;
        private const float GripShare = 0.35f;
        private const float LoadSpring = 5000f;
        private const float LoadDamper = 300f;
        private const float LoadShare = 0.5f;

        /// <summary>Share of the hold's stiffness and of the thrower's strength the hand grips keep during the turn (enough that the controlled arm stays visibly held).</summary>
        private const float TurnContactShare = 0.45f;

        /// <summary>Share of their own balance a defender keeps before being loaded, and the least share of the lift a throw keeps when the body is not yet loaded.</summary>
        private const float ThrownResistance = 0.7f;
        private const float MinimumLoad = 0.35f;

        /// <summary>The thrown body counts as loaded when its centre of mass is within the first distance (m) of the fulcrum horizontally, and not at all beyond the second.</summary>
        private const float LoadedDistance = 0.15f;
        private const float UnloadedDistance = 0.5f;

        /// <summary>How far (0–1) the thrown body is drawn onto the thrower's fulcrum: its centre of mass over it.</summary>
        private float Loaded()
        {
            if (_pivot == null)
            {
                return 0f;
            }

            float distance = Vector3.ProjectOnPlane(CentreOfMass() - _pivot.Body.worldCenterOfMass, Vector3.up).magnitude;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LoadedDistance, UnloadedDistance, distance));
        }

        /// <summary>Centre-of-mass carry gains (1/s², 1/s) and the turn gain (1/s) with its cap (deg/s²).</summary>
        private const float CarryStiffness = 75f;
        private const float CarryDamping = 16f;
        private const float TurnGain = 16f;
        private const float MaxTurnAcceleration = 4800f;

        /// <summary>Fastest a thrown body is turned (deg/s) and carried round the fulcrum (m/s): high-amplitude throws turn the body over in about a third of a second.</summary>
        private const float MaxThrowSpin = 620f;
        private const float MaxCarrySpeed = 4.5f;

        /// <summary>How fast a lag behind the throw's planned rotation is made up (1/s).</summary>
        private const float ThrowAngleGain = 8f;

        /// <summary>Least share of the thrower's strength that can hold the thrown body up with no base under them.</summary>
        private const float UnsupportedShare = 0.35f;

        /// <summary>Share of the thrower's strength their arms and trunk put into pulling and turning the thrown body, base or not.</summary>
        private const float ArmsShare = 0.75f;

        private readonly List<GripLink> _throwLinks = new List<GripLink>();
        private readonly HashSet<GripLink> _traps = new HashSet<GripLink>();
        private GripLink _load;
        private Vector3 _loadSide = Vector3.forward;
        private float _seatUp = 0.5f;
        private float _lastAvailable;
        private float _lastDemand;
        private Vector3 _turnOffset;
        private bool _turning;
        private int _traceCount;
        private Quaternion _trunkRotation = Quaternion.identity;
        private PhysicalBody _throwerBody;
        private float _maxThrowForce;
        private Segment _pivot;
        private Vector3 _throwAxis;
        private float _throwAngle;

        /// <summary>Diagnostics while thrown: strength available and asked for (N), and how far the trunk is from its seat on the fulcrum (m).</summary>
        public string ThrowReport => _mode != Mode.Thrown || _load == null
            ? "-"
            : FormattableString.Invariant($"available {_lastAvailable:F0} N, demand {_lastDemand:F0} N, seat gap {_load.Gap:F2} m, angle {_throwAngle:F0}");

        /// <summary>Rotation achieved so far in the current throw, in degrees about its axis.</summary>
        public float ThrowAngle => _throwAngle;

        /// <summary>While thrown: the horizontal offset from the thrower's fulcrum to this body's centre of mass.</summary>
        public Vector3 OffsetFromFulcrum => _pivot == null ? Vector3.zero : Vector3.ProjectOnPlane(CentreOfMass() - _pivot.Body.transform.position, Vector3.up);

        /// <summary>
        /// Starts a throw: this body stops balancing (it is being taken off its feet), the thrower's
        /// hands close on the held segments (a trunk segment as holder traps the limb against the
        /// thrower's body), and the trunk is squeezed onto the thrower's <paramref name="pivot"/>
        /// segment, the fulcrum. The turn round the fulcrum starts with <see cref="StartTurn"/>.
        /// </summary>
        public void BeginThrow(PhysicalBody thrower, string pivot, IEnumerable<(string Thrower, string Thrown)> grips, float maxForce, float seatUp)
        {
            EndThrowLinks();
            _throwerBody = thrower;
            _maxThrowForce = maxForce;
            _seatUp = Mathf.Clamp01(seatUp);
            _pivot = thrower.Find(pivot);
            _trunkRotation = Find("Spine").Body.transform.rotation;
            _throwAngle = 0f;
            foreach ((string holder, string held) in grips)
            {
                Segment mine = Find(held);
                bool trap = !holder.EndsWith("ForeArm", StringComparison.Ordinal);
                Vector3 heldCentre = Centre(mine);
                GripLink link;
                if (trap)
                {
                    // Trapped against the thrower's body: held on their surface where it touches now,
                    // fixed to that trunk segment, so it turns with the thrower.
                    Segment theirs = thrower.Find(holder);
                    Vector3 holderCentre = Centre(theirs);
                    Vector3 towards = heldCentre - holderCentre;
                    towards = towards.sqrMagnitude > 1e-6f ? towards.normalized : Vector3.up;
                    Vector3 surface = holderCentre + (towards * (theirs.Radius + mine.Radius));
                    link = new GripLink(theirs.Body, theirs.Body.transform.InverseTransformPoint(surface), mine.Body, mine.Body.transform.InverseTransformPoint(heldCentre), GripSpring, GripDamper, maxForce * GripShare);
                    _traps.Add(link);
                }
                else
                {
                    // In the hand: the thrower's palm closes on the held segment.
                    string handName = holder.Substring(0, holder.Length - "ForeArm".Length) + "Hand";
                    Segment hand = thrower.Find(handName);
                    link = new GripLink(hand.Body, thrower.PalmAnchor(handName), mine.Body, mine.Body.transform.InverseTransformPoint(heldCentre), GripSpring, GripDamper, maxForce * GripShare);
                }

                _throwLinks.Add(link);
            }

            // The arm wrap squeezes the trunk onto the fulcrum (hip, back, chest or shoulders), so the
            // opponent is loaded across the thrower rather than swinging beside them. The trunk is
            // seated on the fulcrum's surface, on the opponent's side and above it, never pulled into
            // its middle. The seat is fixed to the thrower's segment, so it turns and bends with them.
            Segment trunk = Find("Spine");
            Vector3 trunkCentre = Centre(trunk);
            Vector3 pivotCentre = Centre(_pivot);
            Vector3 side = Vector3.ProjectOnPlane(trunkCentre - pivotCentre, Vector3.up);
            side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.ProjectOnPlane(trunkCentre - thrower.Root.Body.transform.position, Vector3.up).normalized;
            Vector3 seat = Seat(side, trunk);
            _loadSide = side;
            _load = new GripLink(_pivot.Body, _pivot.Body.transform.InverseTransformPoint(pivotCentre + seat), trunk.Body, trunk.Body.transform.InverseTransformPoint(trunkCentre), LoadSpring, LoadDamper, maxForce * LoadShare);
            _throwLinks.Add(_load);

            _mode = Mode.Thrown;
            _authority = 0f;
            SetMuscle(ThrownMuscle);
        }

        /// <summary>Each substep while loading (before the turn): the seat stays on the side of the fulcrum facing the opponent's trunk, so the squeeze draws them in tight and up onto it.</summary>
        public void DriveLoad()
        {
            if (_mode != Mode.Thrown || _load == null || _turning)
            {
                return;
            }

            Segment trunk = Find("Spine");
            Vector3 centre = Centre(_pivot);
            Vector3 side = Vector3.ProjectOnPlane(Centre(trunk) - centre, Vector3.up);
            if (side.sqrMagnitude > 1e-6f)
            {
                _loadSide = side.normalized;
                _load.HolderAnchor = _pivot.Body.transform.InverseTransformPoint(centre + Seat(_loadSide, trunk));
            }
        }

        /// <summary>Starts the turn of a throw that has been loaded: rotation is measured from now about <paramref name="axis"/>.</summary>
        public void StartTurn(Vector3 axis)
        {
            _throwAxis = axis.normalized;
            _trunkRotation = Find("Spine").Body.transform.rotation;
            _throwAngle = 0f;
            if (_pivot == null)
            {
                return;
            }

            _turnOffset = CentreOfMass() - _pivot.Body.transform.position;
            _turning = true;
            // From here the carry is what the thrower's arms and hips apply (strength-capped, pushed
            // back on the thrower); the hand grips and load yield enough to travel over the fulcrum.
            foreach (GripLink link in _throwLinks)
            {
                if (link == _load)
                {
                    link.SetDrive(LoadSpring * TurnContactShare, LoadDamper * TurnContactShare, _maxThrowForce * LoadShare * TurnContactShare);
                }
                else if (!_traps.Contains(link))
                {
                    link.SetDrive(GripSpring * TurnContactShare, GripDamper * TurnContactShare, _maxThrowForce * TurnContactShare);
                }
                else
                {
                    link.SetDrive(GripSpring * 0.65f, GripDamper * 0.65f, _maxThrowForce * GripShare * 0.65f);
                }
            }
        }

        /// <summary>
        /// Each substep while thrown, the way a thrower applies force: one net force carries the body's
        /// centre of mass round the thrower's fulcrum along the planned turn (they lift and carry the
        /// weight), and one couple turns it about its own centre at the planned rate. Both come out of
        /// the thrower's strength, which shrinks without a base under them, and both are pushed back on
        /// the thrower, whose own balance has to hold it. Being capped, a block or the mat stops it.
        /// </summary>
        public void DriveThrow(float targetAngle, float targetRate, float deltaTime)
        {
            if (_mode != Mode.Thrown || !_turning)
            {
                return;
            }

            float dt = Mathf.Max(deltaTime, 1e-4f);
            float turned = TurnAbout(_throwAxis);
            _throwAngle += turned;
            float spin = turned / dt;

            Vector3 centre = CentreOfMass();
            Vector3 momentum = Vector3.zero;
            foreach (Segment segment in _segments)
            {
                momentum += segment.Body.linearVelocity * segment.Body.mass;
            }

            Vector3 velocity = momentum / _mass;
            // Where the centre of mass should be: its offset from the fulcrum at the start of the turn,
            // turned by the planned angle. The fulcrum moves with the thrower (rising, bending, kneeling).
            Vector3 pivot = _pivot.Body.transform.position;
            Quaternion plan = Quaternion.AngleAxis(targetAngle, _throwAxis);
            if (_load != null)
            {
                Segment trunk = Find("Spine");
                Vector3 rotatedSeat = plan * Seat(_loadSide, trunk);
                _load.HolderAnchor = _pivot.Body.transform.InverseTransformPoint(Centre(_pivot) + rotatedSeat);
            }

            Vector3 target = pivot + (plan * _turnOffset);
            Vector3 targetVelocity = Vector3.ClampMagnitude(Vector3.Cross(_throwAxis * (targetRate * Mathf.Deg2Rad), target - pivot), MaxCarrySpeed) + _pivot.Body.GetPointVelocity(pivot);
            Vector3 carry = _mass * ((CarryStiffness * (target - centre)) + (CarryDamping * (targetVelocity - velocity)) - Physics.gravity);

            // The turn: angular acceleration that brings the spin to the planned rate (plus catching
            // up any lag), as a pure couple: each segment pushed along its circle round the centre.
            // A throw that has fallen behind its plan does not whip round to catch up: no faster than a
            // real throw turns.
            float wantedRate = Mathf.Clamp(targetRate + (ThrowAngleGain * (targetAngle - _throwAngle)), -MaxThrowSpin, MaxThrowSpin);
            float alpha = Mathf.Clamp(TurnGain * (wantedRate - spin), -MaxTurnAcceleration, MaxTurnAcceleration) * Mathf.Deg2Rad;
            float coupleEffort = 0f;
            var couple = new Vector3[_segments.Count];
            for (int i = 0; i < _segments.Count; i++)
            {
                Vector3 r = Vector3.ProjectOnPlane(_segments[i].Body.worldCenterOfMass - centre, _throwAxis);
                couple[i] = Vector3.Cross(_throwAxis * alpha, r) * _segments[i].Body.mass;
                coupleEffort += couple[i].magnitude * 0.5f;
            }

            // Two efforts with different limits. Holding the body up against gravity needs a base: the
            // legs push it through the thrower's support. Pulling it round and turning it over the
            // fulcrum is the arms and trunk, which work whether or not the thrower is dropping under
            // (in a hip or headlock throw the drop is part of the technique).
            float lift = Mathf.Max(0f, carry.y);
            Vector3 pull = carry - new Vector3(0f, lift, 0f);
            float legs = _maxThrowForce * Mathf.Max(UnsupportedShare, _throwerBody.HeldSupport);
            float arms = _maxThrowForce * ArmsShare;
            // A body is lifted only once it is loaded onto the fulcrum: hauling up someone still
            // standing off to the side takes far more than a thrower has.
            legs *= Mathf.Max(MinimumLoad, Loaded());
            float liftScale = lift > legs ? legs / lift : 1f;
            float pullScale = pull.magnitude > arms ? arms / pull.magnitude : 1f;
            float coupleScale = coupleEffort > arms ? arms / coupleEffort : 1f;
            float pullDemand = pull.magnitude + coupleEffort;
            carry = (Vector3.up * (lift * liftScale)) + (pull * pullScale);
            for (int i = 0; i < couple.Length; i++)
            {
                couple[i] *= coupleScale;
            }

            float available = legs + arms;
            float demand = lift + pullDemand;
            float scale = 1f;
            _lastAvailable = available;
            _lastDemand = demand;
            if (Trace != null && (++_traceCount % 8) == 0)
            {
                Trace(FormattableString.Invariant($"plan {targetAngle:F0}° got {_throwAngle:F0}° spin {spin:F0}°/s want {wantedRate:F0}°/s | com err {(target - centre).magnitude:F2} m off fulcrum {Vector3.ProjectOnPlane(centre - pivot, _throwAxis).magnitude:F2} m, axis {_throwAxis:F2} want {target - pivot:F2} at {centre - pivot:F2} lift {lift:F0}/{legs:F0} loaded {Loaded():F2} | carry {carry.magnitude:F0} N couple {coupleEffort:F0} N avail {available:F0} N scale {scale:F2} | thrower support {_throwerBody.HeldSupport:F2} push {_throwerBody.GroundPush:F2} bw"));
            }

            Vector3 torque = Vector3.zero;
            for (int i = 0; i < _segments.Count; i++)
            {
                ArticulationBody body = _segments[i].Body;
                Vector3 force = ((carry * (body.mass / _mass)) + couple[i]) * scale;
                body.AddForceAtPosition(force, body.worldCenterOfMass);
                torque += Vector3.Cross(body.worldCenterOfMass - centre, couple[i] * scale);
            }

            // Equal and opposite: the carry pushes the thrower down and back at the fulcrum, and the
            // turning couple twists the thrower the other way.
            ArticulationBody throwerRoot = _throwerBody.Root.Body;
            _pivot.Body.AddForceAtPosition(-carry * scale, pivot);
            throwerRoot.AddTorque(-torque);
            _throwerBody.AddOpponentImpulse(_pivot.Index, -carry * (scale * dt));
            AddOpponentImpulse(0, carry * (scale * dt));
        }

        /// <summary>Lets go: the holds release and the body flies on with its momentum, then lands.</summary>
        public void EndThrow(float fallSeconds)
        {
            if (_mode != Mode.Thrown)
            {
                return;
            }

            EndThrowLinks();
            _mode = Mode.Active;
            Fall(fallSeconds);
        }

        private void EndThrowLinks()
        {
            _throwLinks.Clear();
            _traps.Clear();
            _load = null;
            _turning = false;
            _pivot = null;
            _throwerBody = null;
        }

        /// <summary>Offset from the fulcrum's centre to where the trunk rests on it, the two surfaces touching.</summary>
        private Vector3 Seat(Vector3 side, Segment trunk) => Vector3.Slerp(side, Vector3.up, _seatUp).normalized * (_pivot.Radius + trunk.Radius);

        /// <summary>How far (degrees) the trunk has turned about <paramref name="axis"/> since the last call: the twist part of its rotation.</summary>
        private float TurnAbout(Vector3 axis)
        {
            Quaternion now = Find("Spine").Body.transform.rotation;
            Quaternion delta = now * Quaternion.Inverse(_trunkRotation);
            _trunkRotation = now;
            var v = new Vector3(delta.x, delta.y, delta.z);
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

        private float CentreOfMassSpeed()
        {
            Vector3 momentum = Vector3.zero;
            foreach (Segment segment in _segments)
            {
                momentum += segment.Body.linearVelocity * segment.Body.mass;
            }

            return (momentum / _mass).magnitude;
        }

        private Vector3 CentreOfMass()
        {
            Vector3 centre = Vector3.zero;
            foreach (Segment segment in _segments)
            {
                centre += segment.Body.worldCenterOfMass * segment.Body.mass;
            }

            return centre / _mass;
        }
    }
}
