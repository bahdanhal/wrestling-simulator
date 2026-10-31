using System;
using System.Collections.Generic;
using UnityEngine;
using Wrestling.Core.Match;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Drives an imported character rig (MakeHuman's Mixamo-named skeleton) from a <see cref="Pose"/>.
    /// At build time it measures the model's bind pose, so the pose solver works with the model's
    /// own limb lengths; each frame it turns the pose's segment orientations into bone rotations
    /// relative to the bind pose (clavicles included) and shapes each hand for its grip: thumb
    /// opposition and abduction apart from the flexion of each finger joint, wrapped to the radius
    /// of what the hand holds.
    /// </summary>
    public sealed class CharacterRig
    {
        private const string Prefix = "mixamorig:";

        private static readonly (Bone Segment, string Name)[] Segments =
        {
            (Bone.Hips, "Hips"),
            (Bone.Spine, "Spine"),
            (Bone.Chest, "Spine2"),
            (Bone.Neck, "Neck"),
            (Bone.Head, "Head"),
            (Bone.LeftShoulder, "LeftShoulder"),
            (Bone.LeftUpperArm, "LeftArm"),
            (Bone.LeftLowerArm, "LeftForeArm"),
            (Bone.LeftHand, "LeftHand"),
            (Bone.RightShoulder, "RightShoulder"),
            (Bone.RightUpperArm, "RightArm"),
            (Bone.RightLowerArm, "RightForeArm"),
            (Bone.RightHand, "RightHand"),
            (Bone.LeftUpperLeg, "LeftUpLeg"),
            (Bone.LeftLowerLeg, "LeftLeg"),
            (Bone.LeftFoot, "LeftFoot"),
            (Bone.RightUpperLeg, "RightUpLeg"),
            (Bone.RightLowerLeg, "RightLeg"),
            (Bone.RightFoot, "RightFoot"),
        };

        private static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

        private readonly Transform[] _bones = new Transform[Skeleton.BoneCount];
        private readonly Quaternion[] _bindOffsets = new Quaternion[Skeleton.BoneCount];
        private readonly Vector3[] _positions = new Vector3[Skeleton.BoneCount];
        private readonly Quaternion[] _rotations = new Quaternion[Skeleton.BoneCount];
        private readonly Transform _spine1;
        private readonly Dictionary<string, Transform> _byName = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private readonly Vector3 _pelvisToHips;
        private readonly List<FingerJoint> _leftFingers = new List<FingerJoint>();
        private readonly List<FingerJoint> _rightFingers = new List<FingerJoint>();
        private readonly SkeletonCalibration _calibration;
        private readonly Transform _jaw;
        private readonly Quaternion _jawBind;
        private readonly Transform _leftEye;
        private readonly Quaternion _leftEyeBind;
        private readonly Transform _rightEye;
        private readonly Quaternion _rightEyeBind;
        private readonly Transform _leftLidTop;
        private readonly Quaternion _leftLidTopBind;
        private readonly Transform _leftLidBottom;
        private readonly Quaternion _leftLidBottomBind;
        private readonly Transform _rightLidTop;
        private readonly Quaternion _rightLidTopBind;
        private readonly Transform _rightLidBottom;
        private readonly Quaternion _rightLidBottomBind;

        /// <param name="root">The character's root (scaled to the wrestler's height, in its bind pose).</param>
        public CharacterRig(Transform root)
        {
            var byName = _byName;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                byName[t.name.StartsWith(Prefix, StringComparison.Ordinal) ? t.name.Substring(Prefix.Length) : t.name] = t;
            }

            Transform Required(string name) =>
                byName.TryGetValue(name, out Transform bone) ? bone : throw new InvalidOperationException($"Character rig has no bone '{name}'.");

            foreach ((Bone segment, string name) in Segments)
            {
                _bones[(int)segment] = Required(name);
            }

            _spine1 = Required("Spine1");
            Transform toeL = Required("LeftToeBase");
            Transform toeR = Required("RightToeBase");
            Pose bind = MeasureBindPose(root, toeL, toeR);
            (bind.HandDirL, bind.PalmNormalL) = MeasureHand(byName, "Left", _bones[(int)Bone.LeftHand]);
            (bind.HandDirR, bind.PalmNormalR) = MeasureHand(byName, "Right", _bones[(int)Bone.RightHand]);
            Bind = bind;
            Body = BodyProportions.FromBindPose(in bind, HeightOf(root));

            // The clavicles start where the rig's clavicle bones do, held in the chest's frame; the
            // wrists' neutral is how the bind pose holds each palm on its forearm.
            Skeleton.Compute(in bind, _positions, _rotations);
            Quaternion chestInverse = Quaternion.Inverse(_rotations[(int)Bone.Chest]);
            _calibration = new SkeletonCalibration(
                chestInverse * (_bones[(int)Bone.LeftShoulder].position - bind.Neck),
                chestInverse * (_bones[(int)Bone.RightShoulder].position - bind.Neck),
                Quaternion.Inverse(_rotations[(int)Bone.LeftLowerArm]) * bind.PalmNormalL,
                Quaternion.Inverse(_rotations[(int)Bone.RightLowerArm]) * bind.PalmNormalR);
            Skeleton.Compute(in bind, _positions, _rotations, null, in _calibration);
            for (int i = 0; i < Skeleton.BoneCount; i++)
            {
                // Rig bone rotation = segment rotation × (inverse bind segment rotation × bind bone rotation).
                _bindOffsets[i] = Quaternion.Inverse(_rotations[i]) * _bones[i].rotation;
            }

            // Where the rig's hips bone sits relative to the pose pelvis, in the hips segment's frame.
            _pelvisToHips = Quaternion.Inverse(_rotations[(int)Bone.Hips]) * (_bones[(int)Bone.Hips].position - bind.Pelvis);
            CollectFingers(byName, "Left", _leftFingers, _bones[(int)Bone.LeftHand]);
            CollectFingers(byName, "Right", _rightFingers, _bones[(int)Bone.RightHand]);
            if (byName.TryGetValue("Jaw", out _jaw))
            {
                _jawBind = _jaw.localRotation;
            }

            if (byName.TryGetValue("LeftEye", out _leftEye))
            {
                _leftEyeBind = _leftEye.localRotation;
            }

            if (byName.TryGetValue("RightEye", out _rightEye))
            {
                _rightEyeBind = _rightEye.localRotation;
            }

            if (byName.TryGetValue("LeftOrbicularisTop", out _leftLidTop))
            {
                _leftLidTopBind = _leftLidTop.localRotation;
            }

            if (byName.TryGetValue("LeftOrbicularisBottom", out _leftLidBottom))
            {
                _leftLidBottomBind = _leftLidBottom.localRotation;
            }

            if (byName.TryGetValue("RightOrbicularisTop", out _rightLidTop))
            {
                _rightLidTopBind = _rightLidTop.localRotation;
            }

            if (byName.TryGetValue("RightOrbicularisBottom", out _rightLidBottom))
            {
                _rightLidBottomBind = _rightLidBottom.localRotation;
            }
        }

        /// <summary>The rig bone driven for a skeleton bone.</summary>
        public Transform BoneOf(Bone bone) => _bones[(int)bone];

        /// <summary>A bone's segment rotation (+Y along it, +Z its front, as <see cref="Skeleton"/> builds it) from the rig bone as it is now.</summary>
        public Quaternion SegmentRotation(Bone bone) => _bones[(int)bone].rotation * Quaternion.Inverse(_bindOffsets[(int)bone]);

        /// <summary>Turns a rig bone to a segment rotation (its children follow).</summary>
        public void SetSegmentRotation(Bone bone, Quaternion segment) => _bones[(int)bone].rotation = segment * _bindOffsets[(int)bone];

        /// <summary>Where this model's clavicles start and how its palms sit at rest.</summary>
        public SkeletonCalibration Calibration => _calibration;

        /// <summary>A rig bone by its Mixamo name without the prefix (for example "LeftForeArm").</summary>
        public Transform Find(string name) =>
            _byName.TryGetValue(name, out Transform bone) ? bone : throw new InvalidOperationException($"Character rig has no bone '{name}'.");

        /// <summary>The model's bind pose as joint positions (world space at build time).</summary>
        public Pose Bind { get; }

        /// <summary>Segment lengths measured from the model, for the pose solver.</summary>
        public BodyProportions Body { get; }

        /// <summary>
        /// Poses the rig. <paramref name="leftGrip"/> and <paramref name="rightGrip"/> are how firmly
        /// each hand takes the shape of its grip (0 open, 1 fully in the pose's grip style).
        /// </summary>
        public void Apply(in Pose pose, float leftGrip, float rightGrip)
        {
            Skeleton.Compute(in pose, _positions, _rotations, null, in _calibration);
            Transform hips = _bones[(int)Bone.Hips];
            hips.SetPositionAndRotation(pose.Pelvis + (_rotations[(int)Bone.Hips] * _pelvisToHips), _rotations[(int)Bone.Hips] * _bindOffsets[(int)Bone.Hips]);
            for (int i = 1; i < Skeleton.BoneCount; i++)
            {
                if (i == (int)Bone.Chest)
                {
                    // Spine1 sits between the lower spine and the chest.
                    _spine1.rotation = Quaternion.Slerp(_bones[(int)Bone.Spine].rotation, _rotations[i] * _bindOffsets[i], 0.5f);
                }

                _bones[i].rotation = _rotations[i] * _bindOffsets[i];
            }

            ApplyHandGrip(GripSide.Left, pose.LeftGripStyle, leftGrip, pose.LeftGripAperture);
            ApplyHandGrip(GripSide.Right, pose.RightGripStyle, rightGrip, pose.RightGripAperture);
            ApplyFace(in pose);
        }

        /// <summary>
        /// The face's bones: the jaw set by strain (clenched under load, opening only to breathe
        /// when <paramref name="fatigue"/> is high between exertions, at <paramref name="breath"/>'s
        /// phase; see <see cref="FacialEffort.JawOpening"/>), the lids narrowed and the eyes on the
        /// gaze target. The effort blend shapes are set by <see cref="FacialEffort"/>.
        /// </summary>
        public void ApplyFace(in Pose pose, float fatigue = 0f, float breath = 0f)
        {
            float focus = Mathf.Clamp01(pose.Focus);
            float effort = Mathf.Clamp01(pose.Effort);
            if (_jaw != null)
            {
                float jawOpen = FacialEffort.JawOpening(focus, effort, fatigue, breath);
                _jaw.localRotation = _jawBind * Quaternion.Euler(jawOpen, 0f, 0f);
            }

            float squintTop = (2.8f * focus) + (2.5f * effort);
            float squintBottom = (2.2f * focus) + (2.0f * effort);
            if (_leftLidTop != null)
            {
                _leftLidTop.localRotation = _leftLidTopBind * Quaternion.Euler(squintTop, 0f, 0f);
            }

            if (_rightLidTop != null)
            {
                _rightLidTop.localRotation = _rightLidTopBind * Quaternion.Euler(squintTop, 0f, 0f);
            }

            if (_leftLidBottom != null)
            {
                _leftLidBottom.localRotation = _leftLidBottomBind * Quaternion.Euler(-squintBottom, 0f, 0f);
            }

            if (_rightLidBottom != null)
            {
                _rightLidBottom.localRotation = _rightLidBottomBind * Quaternion.Euler(-squintBottom, 0f, 0f);
            }

            if (pose.HasGazeTarget)
            {
                AimEye(_leftEye, _leftEyeBind, pose.GazeTarget);
                AimEye(_rightEye, _rightEyeBind, pose.GazeTarget);
            }
            else
            {
                if (_leftEye != null)
                {
                    _leftEye.localRotation = _leftEyeBind;
                }

                if (_rightEye != null)
                {
                    _rightEye.localRotation = _rightEyeBind;
                }
            }
        }

        private void AimEye(Transform eye, Quaternion bindLocal, Vector3 targetWorld)
        {
            if (eye == null || eye.parent == null)
            {
                return;
            }

            Vector3 dirWorld = targetWorld - eye.position;
            if (dirWorld.sqrMagnitude < 1e-4f)
            {
                eye.localRotation = bindLocal;
                return;
            }

            Transform head = _bones[(int)Bone.Head];
            Vector3 localDir = head.InverseTransformDirection(dirWorld.normalized);
            if (localDir.z <= 0.1f)
            {
                eye.localRotation = bindLocal;
                return;
            }

            float yaw = Mathf.Clamp(Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg, -15f, 15f);
            float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(localDir.y, -1f, 1f)) * Mathf.Rad2Deg, -10f, 10f);
            eye.localRotation = bindLocal * Quaternion.Euler(pitch, yaw, 0f);
        }

        /// <summary>
        /// Shapes one hand: from the open guard towards <paramref name="style"/> by
        /// <paramref name="weight"/> (0–1). Wrapping styles bend each finger joint by its phalanx's
        /// length over the radius it wraps (<paramref name="aperture"/>, see <see cref="GripAperture"/>;
        /// zero takes the style's own); the thumb opposes, abducts and flexes on its own.
        /// </summary>
        public void ApplyHandGrip(GripSide side, HandGripStyle style, float weight, float aperture)
        {
            weight = Mathf.Clamp01(weight);
            if (weight > 0f && style == HandGripStyle.OpenGuard)
            {
                // A grip with no socket shape (recorded motion): a plain wrap round an arm.
                style = HandGripStyle.TricepsHook;
            }

            float openAperture = style == HandGripStyle.OpenGuard ? aperture : 0f;
            HandShape open = HandShape.For(HandGripStyle.OpenGuard);
            HandShape grip = HandShape.For(style);
            float radius = GripAperture.ToRadius(aperture > 0f ? aperture : GripAperture.Default(style));
            foreach (FingerJoint joint in side == GripSide.Left ? _leftFingers : _rightFingers)
            {
                (float flex, float spread, float opposition, float abduction) = open.Angles(joint, 0f, openAperture);
                if (weight > 0f)
                {
                    (float f, float s, float o, float a) = grip.Angles(joint, radius, 0f);
                    flex = Mathf.Lerp(flex, f, weight);
                    spread = Mathf.Lerp(spread, s, weight);
                    opposition = Mathf.Lerp(opposition, o, weight);
                    abduction = Mathf.Lerp(abduction, a, weight);
                }

                joint.Bone.localRotation = joint.Bind * Turn(opposition, joint.Opposition) * Turn(abduction + spread, joint.Spread) * Turn(flex, joint.Flex);
            }
        }

        /// <summary>One finger or thumb joint with its axes in its own bind frame, each signed so positive closes, spreads or opposes.</summary>
        private readonly struct FingerJoint
        {
            public FingerJoint(Transform bone, int finger, int joint, Vector3 flex, Vector3 spread, Vector3 opposition, float length)
            {
                Bone = bone;
                Bind = bone.localRotation;
                Finger = finger;
                Joint = joint;
                Flex = flex;
                Spread = spread;
                Opposition = opposition;
                Length = length;
            }

            public Transform Bone { get; }

            public Quaternion Bind { get; }

            /// <summary>0 the thumb, 1–4 index to little finger.</summary>
            public int Finger { get; }

            /// <summary>1 the knuckle (or the thumb's base), 2 and 3 the outer joints.</summary>
            public int Joint { get; }

            public Vector3 Flex { get; }

            public Vector3 Spread { get; }

            public Vector3 Opposition { get; }

            /// <summary>The phalanx's length (m).</summary>
            public float Length { get; }
        }

        /// <summary>Joint angles (degrees) of one hand shape.</summary>
        private readonly struct HandShape
        {
            /// <summary>Half a finger's thickness (m): fingers wrap with their centre line this far outside the held surface.</summary>
            private const float FingerHalfThickness = 0.009f;

            private static readonly float[] MaxFlex = { 0f, 90f, 105f, 80f };

            private readonly bool _wrap;
            private readonly float _tightness;
            private readonly float _distalShare;
            private readonly float _proximal;
            private readonly float _middle;
            private readonly float _distal;
            private readonly float _spread;
            private readonly float _thumbOpposition;
            private readonly float _thumbAbduction;
            private readonly float _thumbFlex;

            private HandShape(bool wrap, float tightness, float distalShare, float proximal, float middle, float distal, float spread, float thumbOpposition, float thumbAbduction, float thumbFlex)
            {
                _wrap = wrap;
                _tightness = tightness;
                _distalShare = distalShare;
                _proximal = proximal;
                _middle = middle;
                _distal = distal;
                _spread = spread;
                _thumbOpposition = thumbOpposition;
                _thumbAbduction = thumbAbduction;
                _thumbFlex = thumbFlex;
            }

            public static HandShape For(HandGripStyle style)
            {
                switch (style)
                {
                    case HandGripStyle.CollarCup:
                        return Wrap(0.75f, 0.8f, spread: 9f, opposition: 8f, abduction: -14f, thumbFlex: 12f);
                    case HandGripStyle.WristCClamp:
                        return Wrap(1f, 1f, spread: 1f, opposition: 62f, abduction: 6f, thumbFlex: 30f);
                    case HandGripStyle.TricepsHook:
                        return Wrap(1f, 0.85f, spread: 2f, opposition: 38f, abduction: 12f, thumbFlex: 22f);
                    case HandGripStyle.HookWrap:
                        return Wrap(1.25f, 1f, spread: 5f, opposition: 12f, abduction: -16f, thumbFlex: 18f);
                    case HandGripStyle.GableClasped:
                        return Wrap(1f, 1f, spread: 0f, opposition: 40f, abduction: -4f, thumbFlex: 35f);
                    case HandGripStyle.LegWrap:
                        return Wrap(1f, 1f, spread: 2f, opposition: 10f, abduction: -18f, thumbFlex: 12f);
                    default:
                        // Athletic open hand: fingers softly curved, thumb a little away from the palm.
                        return new HandShape(false, 0f, 0f, 16f, 20f, 10f, 6f, 18f, 22f, 10f);
                }
            }

            private static HandShape Wrap(float tightness, float distalShare, float spread, float opposition, float abduction, float thumbFlex) =>
                new HandShape(true, tightness, distalShare, 0f, 0f, 0f, spread, opposition, abduction, thumbFlex);

            /// <summary>
            /// Flexion, spread, opposition and abduction (degrees) for a joint: wrapping a cylinder of
            /// <paramref name="radius"/>, or the fixed open shape flattened by <paramref name="flatten"/> (0–1).
            /// </summary>
            public (float Flex, float Spread, float Opposition, float Abduction) Angles(FingerJoint joint, float radius, float flatten)
            {
                if (joint.Finger == 0)
                {
                    float open = 1f - (0.6f * Mathf.Clamp01(flatten));
                    return joint.Joint == 1
                        ? (0f, 0f, _thumbOpposition * open, _thumbAbduction)
                        : (_thumbFlex * open, 0f, 0f, 0f);
                }

                float spread = joint.Joint == 1 ? _spread : 0f;
                if (!_wrap)
                {
                    float open = 1f - (0.75f * Mathf.Clamp01(flatten));
                    float angle = joint.Joint == 1 ? _proximal : joint.Joint == 2 ? _middle : _distal;
                    return (angle * open, spread, 0f, 0f);
                }

                // A phalanx of length L laid round a cylinder turns by L / r radians.
                float share = joint.Joint == 3 ? _distalShare : 1f;
                float wrapped = _tightness * share * (joint.Length / (radius + FingerHalfThickness)) * Mathf.Rad2Deg;
                return (Mathf.Min(wrapped, MaxFlex[joint.Joint]), spread, 0f, 0f);
            }
        }

        /// <summary>The hand's direction (wrist to middle knuckle) and palm normal in the bind pose, or zero when the rig has no fingers.</summary>
        private static (Vector3 Direction, Vector3 Palm) MeasureHand(Dictionary<string, Transform> byName, string side, Transform hand)
        {
            if (!byName.TryGetValue(side + "HandIndex1", out Transform index1) || !byName.TryGetValue(side + "HandPinky1", out Transform pinky1)
                || !byName.TryGetValue(side + "HandMiddle1", out Transform middle1))
            {
                return (Vector3.zero, Vector3.zero);
            }

            Vector3 along = (middle1.position - hand.position).normalized;
            Vector3 across = (index1.position - pinky1.position).normalized;
            // The palm: along × across for the left hand, across × along for the right (checked
            // against the mesh: the other side is the back of the hand).
            Vector3 palm = Vector3.Cross(across, along).normalized * (side == "Left" ? 1f : -1f);
            return (along, palm);
        }

        /// <summary>
        /// Finger and thumb joints with their axes: flexion perpendicular to the phalanx and the palm
        /// normal, spread about the palm normal (away from the middle finger), and for the thumb's
        /// base opposition (across in front of the palm) and abduction (away from the index).
        /// </summary>
        private static void CollectFingers(Dictionary<string, Transform> byName, string side, List<FingerJoint> into, Transform hand)
        {
            (Vector3 along, Vector3 palm) = MeasureHand(byName, side, hand);
            if (palm == Vector3.zero)
            {
                return;
            }

            Vector3 middle = byName[side + "HandMiddle1"].position;
            Vector3 index = byName[side + "HandIndex1"].position;
            Vector3 towardsPinky = Vector3.ProjectOnPlane(byName[side + "HandPinky1"].position - index, palm).normalized;
            for (int finger = 0; finger < Fingers.Length; finger++)
            {
                string name = Fingers[finger];
                for (int joint = 1; joint <= 3; joint++)
                {
                    if (!byName.TryGetValue($"{side}Hand{name}{joint}", out Transform bone))
                    {
                        continue;
                    }

                    Transform next = PhalanxEnd(bone);
                    Vector3 direction = next != null ? next.position - bone.position : along * 0.03f;
                    float length = direction.magnitude;
                    direction = length > 1e-5f ? direction / length : along;
                    Vector3 flex = Signed(Vector3.Cross(direction, palm), direction, palm);
                    if (flex.sqrMagnitude < 1e-6f)
                    {
                        flex = Signed(Vector3.Cross(direction, towardsPinky), direction, palm);
                    }

                    Vector3 spread = Vector3.zero;
                    Vector3 opposition = Vector3.zero;
                    if (finger == 0 && joint == 1)
                    {
                        // Thumb base: abduction swings it away from the index in the palm's plane,
                        // opposition brings it round in front of the palm towards the little finger.
                        Vector3 away = Vector3.ProjectOnPlane(bone.position - index, palm);
                        spread = Signed(palm, direction, away.sqrMagnitude > 1e-8f ? away.normalized : -towardsPinky);
                        Vector3 across = ((palm * 0.75f) + (towardsPinky * 0.35f)).normalized;
                        opposition = Signed(Vector3.Cross(direction, across), direction, across);
                    }
                    else if (finger > 0 && joint == 1)
                    {
                        Vector3 outward = Vector3.ProjectOnPlane(bone.position - middle, palm);
                        if (outward.sqrMagnitude > 1e-8f)
                        {
                            spread = Signed(palm, direction, outward.normalized);
                        }
                    }

                    into.Add(new FingerJoint(bone, finger, joint, Local(bone, flex), Local(bone, spread), Local(bone, opposition), length));
                }
            }
        }

        /// <summary>The axis signed so a small positive turn of <paramref name="direction"/> about it moves the tip towards <paramref name="towards"/>.</summary>
        private static Vector3 Signed(Vector3 axis, Vector3 direction, Vector3 towards)
        {
            if (axis.sqrMagnitude < 1e-8f)
            {
                return Vector3.zero;
            }

            axis.Normalize();
            Vector3 turned = Quaternion.AngleAxis(10f, axis) * direction;
            return Vector3.Dot(turned - direction, towards) >= 0f ? axis : -axis;
        }

        private static Vector3 Local(Transform bone, Vector3 world) => world.sqrMagnitude > 1e-8f ? bone.InverseTransformDirection(world).normalized : Vector3.zero;

        /// <summary>A turn about a joint axis; a joint without that axis does not turn.</summary>
        private static Quaternion Turn(float degrees, Vector3 axis) => axis == Vector3.zero || degrees == 0f ? Quaternion.identity : Quaternion.AngleAxis(degrees, axis);

        /// <summary>The next joint along a phalanx: its first child that is a rig bone (procedural twist bones are skipped).</summary>
        private static Transform PhalanxEnd(Transform bone)
        {
            for (int i = 0; i < bone.childCount; i++)
            {
                Transform child = bone.GetChild(i);
                if (!child.name.EndsWith("Twist", StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        private Pose MeasureBindPose(Transform root, Transform toeL, Transform toeR)
        {
            Vector3 P(Bone b) => _bones[(int)b].position;
            // The model's front is where its toes point; the importer's axis conversion is not relied on.
            Vector3 forward = Vector3.ProjectOnPlane((toeL.position - P(Bone.LeftFoot)) + (toeR.position - P(Bone.RightFoot)), Vector3.up).normalized;
            var pose = new Pose
            {
                Pelvis = (P(Bone.LeftUpperLeg) + P(Bone.RightUpperLeg)) * 0.5f,
                Neck = P(Bone.Neck),
                ShoulderL = P(Bone.LeftUpperArm),
                ShoulderR = P(Bone.RightUpperArm),
                ElbowL = P(Bone.LeftLowerArm),
                ElbowR = P(Bone.RightLowerArm),
                HandL = P(Bone.LeftHand),
                HandR = P(Bone.RightHand),
                HipL = P(Bone.LeftUpperLeg),
                HipR = P(Bone.RightUpperLeg),
                KneeL = P(Bone.LeftLowerLeg),
                KneeR = P(Bone.RightLowerLeg),
                AnkleL = P(Bone.LeftFoot),
                AnkleR = P(Bone.RightFoot),
                ToeL = toeL.position,
                ToeR = toeR.position,
            };
            pose.Pelvis.y = (P(Bone.LeftUpperLeg).y + P(Bone.RightUpperLeg).y) * 0.5f;
            pose.Spine = (pose.Neck - pose.Pelvis).normalized;
            pose.Left = (pose.HipL - pose.HipR).normalized;
            pose.ChestNormal = Vector3.ProjectOnPlane(forward, pose.Spine).normalized;
            // Head joint: the head bone runs up from the skull base; its centre is a little above it.
            Vector3 headBase = P(Bone.Head);
            pose.Head = headBase + ((headBase - pose.Neck).normalized * ((headBase - pose.Neck).magnitude * 0.6f));
            return pose;
        }

        private static float HeightOf(Transform root)
        {
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            float top = float.MinValue;
            float bottom = float.MaxValue;
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Bounds bounds = renderer.bounds;
                top = Mathf.Max(top, bounds.max.y);
                bottom = Mathf.Min(bottom, bounds.min.y);
            }

            return top > bottom ? top - bottom : 1.75f;
        }
    }
}
