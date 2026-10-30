using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Drives an imported character rig (MakeHuman's Mixamo-named skeleton) from a <see cref="Pose"/>.
    /// At build time it measures the model's bind pose, so the pose solver works with the model's
    /// own limb lengths; each frame it turns the pose's segment orientations into bone rotations
    /// relative to the bind pose, and curls the fingers for grips.
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
            (Bone.LeftUpperArm, "LeftArm"),
            (Bone.LeftLowerArm, "LeftForeArm"),
            (Bone.LeftHand, "LeftHand"),
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
        private readonly List<(Transform Bone, Quaternion Bind, Vector3 Axis, float Scale)> _leftFingers = new List<(Transform, Quaternion, Vector3, float)>();
        private readonly List<(Transform Bone, Quaternion Bind, Vector3 Axis, float Scale)> _rightFingers = new List<(Transform, Quaternion, Vector3, float)>();
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
            Bind = bind;
            Body = BodyProportions.FromBindPose(in bind, HeightOf(root));

            Skeleton.Compute(in bind, _positions, _rotations);
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

        /// <summary>A rig bone by its Mixamo name without the prefix (for example "LeftForeArm").</summary>
        public Transform Find(string name) =>
            _byName.TryGetValue(name, out Transform bone) ? bone : throw new InvalidOperationException($"Character rig has no bone '{name}'.");

        /// <summary>The model's bind pose as joint positions (world space at build time).</summary>
        public Pose Bind { get; }

        /// <summary>Segment lengths measured from the model, for the pose solver.</summary>
        public BodyProportions Body { get; }

        /// <summary>Poses the rig. <paramref name="leftGrip"/> and <paramref name="rightGrip"/> curl the fingers (0 relaxed, 1 closed).</summary>
        public void Apply(in Pose pose, float leftGrip, float rightGrip)
        {
            Skeleton.Compute(in pose, _positions, _rotations);
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

            Curl(_leftFingers, leftGrip);
            Curl(_rightFingers, rightGrip);
            ApplyFace(in pose);
        }

        public void ApplyFace(in Pose pose)
        {
            float focus = Mathf.Clamp01(pose.Focus);
            float effort = Mathf.Clamp01(pose.Effort);
            if (_jaw != null)
            {
                float jawOpen = (1.5f * focus) + (5.5f * effort);
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

        private static void Curl(List<(Transform Bone, Quaternion Bind, Vector3 Axis, float Scale)> fingers, float amount)
        {
            float relaxed = 0.25f;
            float curl = Mathf.Lerp(relaxed, 1f, Mathf.Clamp01(amount));
            foreach ((Transform bone, Quaternion bind, Vector3 axis, float scale) in fingers)
            {
                bone.localRotation = bind * Quaternion.AngleAxis(curl * scale, axis);
            }
        }

        /// <summary>
        /// Finger bones with their curl axis: perpendicular to the finger and the palm normal, signed
        /// so positive angles close the hand towards the palm.
        /// </summary>
        private static void CollectFingers(Dictionary<string, Transform> byName, string side, List<(Transform, Quaternion, Vector3, float)> into, Transform hand)
        {
            if (!byName.TryGetValue(side + "HandIndex1", out Transform index1) || !byName.TryGetValue(side + "HandPinky1", out Transform pinky1))
            {
                return;
            }

            Vector3 along = (byName[side + "HandMiddle1"].position - hand.position).normalized;
            Vector3 across = (index1.position - pinky1.position).normalized;
            // The palm faces the side the fingers curl to; for both hands this is along × across with handedness by side.
            Vector3 palm = Vector3.Cross(across, along).normalized * (side == "Left" ? -1f : 1f);
            foreach (string finger in Fingers)
            {
                for (int joint = 1; joint <= 3; joint++)
                {
                    if (!byName.TryGetValue($"{side}Hand{finger}{joint}", out Transform bone))
                    {
                        continue;
                    }

                    Vector3 direction = bone.childCount > 0 ? (bone.GetChild(0).position - bone.position).normalized : along;
                    Vector3 axisWorld = Vector3.Cross(direction, palm).normalized;
                    Vector3 axisLocal = bone.InverseTransformDirection(axisWorld);
                    float scale = finger == "Thumb" ? 35f : (joint == 1 ? 75f : 80f);
                    into.Add((bone, bone.localRotation, axisLocal, scale));
                }
            }
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
