using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Physics body for one wrestler: twelve rigid segments on the character's bones, joined by
    /// motorised joints that pull towards the animated pose. While the wrestler is in control it
    /// follows the animation exactly (kinematic). On a fall it turns dynamic: it keeps the motion it
    /// had, falls under gravity onto the mat and against the other wrestler, while its muscles keep
    /// pulling towards the pose the engine says it lands in; then it blends back to the animation.
    /// Display only: nothing here feeds back into the simulation.
    /// </summary>
    public sealed class ActiveRagdoll
    {
        /// <summary>How hard the muscles pull towards the animated pose while falling (relative to full control).</summary>
        private const float FallingMuscle = 0.35f;
        private const float RecoveringMuscle = 1f;
        private const float RecoverSeconds = 0.45f;

        private enum Mode
        {
            Animated,
            Falling,
            Recovering,
        }

        private sealed class Segment
        {
            public string Name;
            public Transform Bone;
            public Rigidbody Body;
            public ConfigurableJoint Joint;
            public Segment Parent;
            public Quaternion StartLocal;
            public Vector3 PreviousPosition;
            public Quaternion PreviousRotation;
            public float Muscle;
        }

        /// <summary>Segment, parent, mass share, radius as a share of height, and which child bone ends the capsule.</summary>
        private static readonly (string Name, string Parent, float Mass, float Radius, string End)[] Layout =
        {
            ("Hips", null, 0.15f, 0.075f, "Spine"),
            ("Spine", "Hips", 0.13f, 0.07f, "Spine2"),
            ("Spine2", "Spine", 0.2f, 0.078f, "Neck"),
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

        /// <summary>A thrown body starts its fall wrapped around the thrower; it passes through them this long before they collide.</summary>
        private const float OpponentGraceSeconds = 0.2f;

        private readonly List<Segment> _segments = new List<Segment>();
        private readonly Collider[] _colliders;
        private ActiveRagdoll _ignoring;
        private float _ignoreUntil;
        private readonly GameObject _root;
        private Mode _mode = Mode.Animated;
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
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.solverIterations = 16;
                body.solverVelocityIterations = 4;
                body.maxAngularVelocity = 25f;
                body.angularDamping = 0.6f;
                body.linearDamping = 0.05f;
                // Bodies that start a fall in contact separate gently instead of being shot apart.
                body.maxDepenetrationVelocity = 0.6f;

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

        /// <summary>0 when the animation is shown as is, 1 when the physics body is.</summary>
        public float Weight => _weight;

        public bool IsFalling => _mode == Mode.Falling;

        /// <summary>
        /// Starts a fall: the body goes dynamic for <paramref name="seconds"/>, carrying its current
        /// motion. <paramref name="opponent"/> (if given) does not collide with it at first, because a
        /// thrown body is released from inside the thrower's grip.
        /// </summary>
        public void Fall(float seconds, float deltaTime, ActiveRagdoll opponent = null)
        {
            if (opponent != null)
            {
                SetIgnore(opponent, true);
                _ignoring = opponent;
                _ignoreUntil = OpponentGraceSeconds;
            }

            if (_mode == Mode.Falling)
            {
                _fallSeconds = Mathf.Max(_fallSeconds, _timer + seconds);
                return;
            }

            float dt = Mathf.Max(deltaTime, 1e-3f);
            foreach (Segment segment in _segments)
            {
                Vector3 velocity = (segment.Bone.position - segment.PreviousPosition) / dt;
                Quaternion delta = segment.Bone.rotation * Quaternion.Inverse(segment.PreviousRotation);
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f)
                {
                    angle -= 360f;
                }

                segment.Body.isKinematic = false;
                segment.Body.position = segment.Bone.position;
                segment.Body.rotation = segment.Bone.rotation;
                segment.Body.linearVelocity = Vector3.ClampMagnitude(velocity, 7f);
                segment.Body.angularVelocity = float.IsNaN(axis.x) ? Vector3.zero : Vector3.ClampMagnitude(axis * (angle * Mathf.Deg2Rad / dt), 12f);
            }

            _mode = Mode.Falling;
            _timer = 0f;
            _fallSeconds = seconds;
            SetMuscle(FallingMuscle);
        }

        /// <summary>
        /// Before the physics step: in control, the body copies the animated bones; otherwise the
        /// joint motors target the animated pose (the bones hold it at this point).
        /// </summary>
        public void BeforeStep(float deltaTime)
        {
            switch (_mode)
            {
                case Mode.Animated:
                    foreach (Segment segment in _segments)
                    {
                        segment.Body.position = segment.Bone.position;
                        segment.Body.rotation = segment.Bone.rotation;
                        segment.Body.transform.SetPositionAndRotation(segment.Bone.position, segment.Bone.rotation);
                    }

                    _weight = 0f;
                    return;
                case Mode.Falling:
                    _timer += deltaTime;
                    if (_ignoring != null && _timer >= _ignoreUntil)
                    {
                        SetIgnore(_ignoring, false);
                        _ignoring = null;
                    }

                    if (_timer >= _fallSeconds)
                    {
                        _mode = Mode.Recovering;
                        _timer = 0f;
                        SetMuscle(RecoveringMuscle);
                    }

                    _weight = 1f;
                    break;
                case Mode.Recovering:
                    _timer += deltaTime;
                    _weight = 1f - Mathf.SmoothStep(0f, 1f, _timer / RecoverSeconds);
                    if (_timer >= RecoverSeconds)
                    {
                        _mode = Mode.Animated;
                        foreach (Segment segment in _segments)
                        {
                            segment.Body.isKinematic = true;
                        }

                        _weight = 0f;
                        return;
                    }

                    break;
            }

            foreach (Segment segment in _segments)
            {
                if (segment.Joint != null)
                {
                    Quaternion targetLocal = Quaternion.Inverse(segment.Parent.Bone.rotation) * segment.Bone.rotation;
                    // Joint axes match the segment's local axes, so the target is a plain local-space delta.
                    segment.Joint.targetRotation = Quaternion.Inverse(targetLocal) * segment.StartLocal;
                }
            }
        }

        /// <summary>After the physics step: blends the bones from the animation towards the physics body.</summary>
        public void AfterStep()
        {
            if (_weight <= 0f)
            {
                foreach (Segment segment in _segments)
                {
                    segment.PreviousPosition = segment.Bone.position;
                    segment.PreviousRotation = segment.Bone.rotation;
                }

                return;
            }

            // Parents before children (layout order), so each bone's world pose is set once.
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

                segment.PreviousPosition = segment.Bone.position;
                segment.PreviousRotation = segment.Bone.rotation;
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

        private void SetIgnore(ActiveRagdoll other, bool ignore)
        {
            foreach (Collider mine in _colliders)
            {
                foreach (Collider theirs in other._colliders)
                {
                    Physics.IgnoreCollision(mine, theirs, ignore);
                }
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
