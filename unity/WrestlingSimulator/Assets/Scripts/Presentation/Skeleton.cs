using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>Bones of the wrestler rig, named after Unity's Humanoid bones so imported animation can map onto it.</summary>
    public enum Bone
    {
        Hips,
        Spine,
        Chest,
        Neck,
        Head,
        LeftShoulder,
        LeftUpperArm,
        LeftLowerArm,
        LeftHand,
        RightShoulder,
        RightUpperArm,
        RightLowerArm,
        RightHand,
        LeftUpperLeg,
        LeftLowerLeg,
        LeftFoot,
        RightUpperLeg,
        RightLowerLeg,
        RightFoot,
    }

    /// <summary>
    /// What a character's bind pose tells the skeleton: where the clavicles start (the
    /// sternoclavicular joints, in the chest bone's frame relative to the neck joint) and how each
    /// hand's palm sits on its forearm at rest (in the forearm bone's frame), the wrist's neutral.
    /// When unset the skeleton estimates both.
    /// </summary>
    public readonly struct SkeletonCalibration
    {
        public SkeletonCalibration(Vector3 clavicleLeft, Vector3 clavicleRight, Vector3 palmLeft, Vector3 palmRight)
        {
            ClavicleLeft = clavicleLeft;
            ClavicleRight = clavicleRight;
            PalmLeft = palmLeft;
            PalmRight = palmRight;
            IsSet = true;
        }

        public Vector3 ClavicleLeft { get; }

        public Vector3 ClavicleRight { get; }

        public Vector3 PalmLeft { get; }

        public Vector3 PalmRight { get; }

        public bool IsSet { get; }
    }

    /// <summary>
    /// Bone hierarchy and the mapping from a pose to bone transforms. Each bone's +Y runs along the
    /// bone and its +Z faces the front of its body part: the pelvis, spine, chest and head each have
    /// their own front, so the spine can twist; a hand's +Z is its palm.
    /// </summary>
    public static class Skeleton
    {
        public const int BoneCount = 19;

        /// <summary>Share of the turn between pelvis and chest taken at the lumbar spine.</summary>
        private const float LumbarShare = 0.45f;

        /// <summary>Share of the head's turn taken at the neck.</summary>
        private const float NeckShare = 0.4f;

        /// <summary>Wrist limits (degrees): flexion, extension, deviation either way, and forearm turn either way from neutral.</summary>
        private const float MaxFlexion = 75f;
        private const float MaxExtension = 70f;
        private const float MaxDeviation = 28f;
        private const float MaxForearmTurn = 85f;

        /// <summary>
        /// Extension (degrees) a wrist takes passively with the body's weight on a palm flat on the
        /// mat (a hand posted under a near-vertical forearm), past what it reaches actively.
        /// </summary>
        public const float MaxLoadedExtension = 90f;

        /// <summary>A palm facing this far down (the y of its normal) with the wrist this low (m) bears weight on the mat.</summary>
        private const float LoadedPalm = -0.85f;
        private const float LoadedWrist = 0.12f;

        /// <summary>A foot whose ankle is this low (m) is planted: its sole lies on the mat.</summary>
        private const float PlantedAnkle = 0.11f;
        private const float LiftedAnkle = 0.17f;

        private static readonly Bone?[] Parents =
        {
            null,
            Bone.Hips,
            Bone.Spine,
            Bone.Chest,
            Bone.Neck,
            Bone.Chest,
            Bone.LeftShoulder,
            Bone.LeftUpperArm,
            Bone.LeftLowerArm,
            Bone.Chest,
            Bone.RightShoulder,
            Bone.RightUpperArm,
            Bone.RightLowerArm,
            Bone.Hips,
            Bone.LeftUpperLeg,
            Bone.LeftLowerLeg,
            Bone.Hips,
            Bone.RightUpperLeg,
            Bone.RightLowerLeg,
        };

        public static Bone? ParentOf(Bone bone) => Parents[(int)bone];

        /// <summary>World position and rotation of every bone for a pose (arrays indexed by <see cref="Bone"/>).</summary>
        public static void Compute(in Pose pose, Vector3[] positions, Quaternion[] rotations, float[] lengths = null, in SkeletonCalibration calibration = default)
        {
            Vector3 waist = pose.Waist;
            Vector3 chest = pose.Chest;
            Vector3 spineUp = SafeDir(pose.Neck - pose.Pelvis, Vector3.up);
            Vector3 left = pose.Left.sqrMagnitude > 1e-6f ? pose.Left.normalized : Vector3.left;
            Vector3 front = pose.ChestNormal.sqrMagnitude > 1e-6f ? pose.ChestNormal.normalized : Vector3.Cross(spineUp, left).normalized;
            Vector3 pelvisFront = pose.PelvisFront;
            Vector3 headFront = pose.HeadNormal.sqrMagnitude > 1e-6f ? pose.HeadNormal.normalized : front;

            Set(Bone.Hips, pose.Pelvis, waist, pelvisFront);
            Set(Bone.Spine, waist, chest, Vector3.Slerp(pelvisFront, front, LumbarShare));
            Set(Bone.Chest, chest, pose.Neck, front);
            Vector3 headAxis = pose.Head - pose.Neck;
            Set(Bone.Neck, pose.Neck, pose.Neck + (headAxis * 0.45f), Vector3.Slerp(front, headFront, NeckShare));
            Set(Bone.Head, pose.Head, pose.Head + headAxis, headFront);

            Quaternion chestRotation = rotations[(int)Bone.Chest];
            Vector3 rootL = calibration.IsSet ? pose.Neck + (chestRotation * calibration.ClavicleLeft) : Vector3.Lerp(pose.Neck, pose.ShoulderL, 0.18f);
            Vector3 rootR = calibration.IsSet ? pose.Neck + (chestRotation * calibration.ClavicleRight) : Vector3.Lerp(pose.Neck, pose.ShoulderR, 0.18f);
            Set(Bone.LeftShoulder, rootL, pose.ShoulderL, front);
            Set(Bone.RightShoulder, rootR, pose.ShoulderR, front);

            Vector3 bindArmL = ((left * 0.88f) - (spineUp * 0.47f)).normalized;
            Vector3 upperArmDirL = SafeDir(pose.ElbowL - pose.ShoulderL, bindArmL);
            Vector3 forearmDirL = SafeDir(pose.HandL - pose.ElbowL, upperArmDirL);
            Vector3 upperFacingL = TransportFacing(bindArmL, upperArmDirL, front);
            Vector3 forearmFacingL = TransportFacing(upperArmDirL, forearmDirL, upperFacingL);
            Set(Bone.LeftUpperArm, pose.ShoulderL, pose.ElbowL, upperFacingL);
            Set(Bone.LeftLowerArm, pose.ElbowL, pose.HandL, forearmFacingL);
            Vector3 neutralL = NeutralPalm(forearmDirL, forearmFacingL, true, in calibration);
            (Vector3 handDirL, Vector3 palmL) = Wrist(forearmDirL, neutralL, pose.HandDirL, pose.PalmNormalL, ExtensionLimit(pose.HandL, pose.PalmNormalL));
            Set(Bone.LeftHand, pose.HandL, pose.HandL + (handDirL * ((pose.HandL - pose.ElbowL).magnitude * 0.4f)), palmL);

            Vector3 bindArmR = ((-left * 0.88f) - (spineUp * 0.47f)).normalized;
            Vector3 upperArmDirR = SafeDir(pose.ElbowR - pose.ShoulderR, bindArmR);
            Vector3 forearmDirR = SafeDir(pose.HandR - pose.ElbowR, upperArmDirR);
            Vector3 upperFacingR = TransportFacing(bindArmR, upperArmDirR, front);
            Vector3 forearmFacingR = TransportFacing(upperArmDirR, forearmDirR, upperFacingR);
            Set(Bone.RightUpperArm, pose.ShoulderR, pose.ElbowR, upperFacingR);
            Set(Bone.RightLowerArm, pose.ElbowR, pose.HandR, forearmFacingR);
            Vector3 neutralR = NeutralPalm(forearmDirR, forearmFacingR, false, in calibration);
            (Vector3 handDirR, Vector3 palmR) = Wrist(forearmDirR, neutralR, pose.HandDirR, pose.PalmNormalR, ExtensionLimit(pose.HandR, pose.PalmNormalR));
            Set(Bone.RightHand, pose.HandR, pose.HandR + (handDirR * ((pose.HandR - pose.ElbowR).magnitude * 0.4f)), palmR);

            // The legs hang from the pelvis, so their twist is measured from the pelvis' frame.
            Vector3 bindLeg = -SafeDir(waist - pose.Pelvis, spineUp);
            Vector3 thighDirL = SafeDir(pose.KneeL - pose.HipL, bindLeg);
            Vector3 shinDirL = SafeDir(pose.AnkleL - pose.KneeL, thighDirL);
            Vector3 thighFacingL = TransportFacing(bindLeg, thighDirL, pelvisFront);
            Vector3 shinFacingL = TransportFacing(thighDirL, shinDirL, thighFacingL);
            Set(Bone.LeftUpperLeg, pose.HipL, pose.KneeL, thighFacingL);
            Set(Bone.LeftLowerLeg, pose.KneeL, pose.AnkleL, shinFacingL);
            Set(Bone.LeftFoot, pose.AnkleL, pose.ToeL, FootTop(pose.AnkleL, pose.ToeL, shinDirL, shinFacingL, pose.UnconstrainedFeet));

            Vector3 thighDirR = SafeDir(pose.KneeR - pose.HipR, bindLeg);
            Vector3 shinDirR = SafeDir(pose.AnkleR - pose.KneeR, thighDirR);
            Vector3 thighFacingR = TransportFacing(bindLeg, thighDirR, pelvisFront);
            Vector3 shinFacingR = TransportFacing(thighDirR, shinDirR, thighFacingR);
            Set(Bone.RightUpperLeg, pose.HipR, pose.KneeR, thighFacingR);
            Set(Bone.RightLowerLeg, pose.KneeR, pose.AnkleR, shinFacingR);
            Set(Bone.RightFoot, pose.AnkleR, pose.ToeR, FootTop(pose.AnkleR, pose.ToeR, shinDirR, shinFacingR, pose.UnconstrainedFeet));

            void Set(Bone bone, Vector3 from, Vector3 to, Vector3 facing)
            {
                positions[(int)bone] = from;
                rotations[(int)bone] = Orientation(to - from, facing);
                if (lengths != null)
                {
                    lengths[(int)bone] = (to - from).magnitude;
                }
            }
        }

        /// <summary>
        /// The palm of a relaxed wrist on a forearm frame: as the character's bind pose holds it, or,
        /// uncalibrated, facing the body's midline with the thumb forward.
        /// </summary>
        public static Vector3 NeutralPalm(Vector3 forearmDir, Vector3 forearmFacing, bool left, in SkeletonCalibration calibration)
        {
            Vector3 local = left ? calibration.PalmLeft : calibration.PalmRight;
            if (calibration.IsSet && local.sqrMagnitude > 1e-6f)
            {
                return Vector3.ProjectOnPlane(Orientation(forearmDir, forearmFacing) * local, forearmDir).normalized;
            }

            return (left ? Vector3.Cross(forearmFacing, forearmDir) : Vector3.Cross(forearmDir, forearmFacing)).normalized;
        }

        /// <summary>
        /// The hand's direction and palm: the wanted ones, brought within what a wrist and forearm can
        /// do from the forearm's neutral (turn of the forearm, then flexion or extension and
        /// deviation). With nothing wanted the wrist is neutral.
        /// </summary>
        public static (Vector3 Direction, Vector3 Palm) Wrist(Vector3 forearm, Vector3 neutral, Vector3 wantDir, Vector3 wantPalm, float maxExtension = MaxExtension)
        {
            if (wantDir.sqrMagnitude < 1e-8f && wantPalm.sqrMagnitude < 1e-8f)
            {
                return (forearm, neutral);
            }

            Vector3 direction = wantDir.sqrMagnitude > 1e-8f ? wantDir.normalized : forearm;
            Vector3 palm = wantPalm.sqrMagnitude > 1e-8f ? wantPalm.normalized : neutral;

            // Undo the wanted bend to read the forearm turn the palm asks for, then limit it.
            Vector3 palmOnAxis = Vector3.ProjectOnPlane(Quaternion.FromToRotation(direction, forearm) * palm, forearm);
            Vector3 turned = neutral;
            if (palmOnAxis.sqrMagnitude > 1e-8f)
            {
                palmOnAxis.Normalize();
                float angle = Vector3.Angle(neutral, palmOnAxis);
                Quaternion turn = Vector3.Dot(neutral, palmOnAxis) < -0.999f
                    ? Quaternion.AngleAxis(180f, forearm)
                    : Quaternion.FromToRotation(neutral, palmOnAxis);
                if (angle > MaxForearmTurn)
                {
                    turn = Quaternion.Slerp(Quaternion.identity, turn, MaxForearmTurn / angle);
                }

                turned = Vector3.ProjectOnPlane(turn * neutral, forearm).normalized;
            }

            // Flexion bends the fingers towards the palm; deviation towards the thumb or little finger.
            Vector3 thumbSide = Vector3.Cross(forearm, turned);
            float along = Vector3.Dot(direction, forearm);
            float flexion = Mathf.Clamp(Mathf.Atan2(Vector3.Dot(direction, turned), along) * Mathf.Rad2Deg, -maxExtension, MaxFlexion);
            float deviation = Mathf.Clamp(Mathf.Atan2(Vector3.Dot(direction, thumbSide), Mathf.Max(along, 0.05f)) * Mathf.Rad2Deg, -MaxDeviation, MaxDeviation);
            // Flexion turns the hand from the forearm's line towards the palm side, deviation then
            // swings it in the hand's own plane (rotations, so a loaded wrist bent to 90° is exact).
            Vector3 flexed = Quaternion.AngleAxis(flexion, Vector3.Cross(forearm, turned)) * forearm;
            Vector3 hand = (Quaternion.AngleAxis(deviation, Vector3.Cross(flexed, thumbSide)) * flexed).normalized;
            Vector3 handPalm = Quaternion.FromToRotation(forearm, hand) * turned;
            return (hand, handPalm);
        }

        /// <summary>How far a wrist may extend: further with a palm flat on the mat bearing weight.</summary>
        private static float ExtensionLimit(Vector3 wrist, Vector3 palm) =>
            palm.sqrMagnitude > 1e-6f && palm.normalized.y < LoadedPalm && wrist.y < LoadedWrist ? MaxLoadedExtension : MaxExtension;

        /// <summary>
        /// Which way the fingers of a hand flat on the mat point: along the forearm's line projected
        /// onto the mat, turning smoothly towards the chest's facing as the forearm stands up
        /// vertical (where its line on the mat vanishes), so a posted wrist never twists across.
        /// </summary>
        public static Vector3 PostedHandDirection(Vector3 forearm, Vector3 chestForward)
        {
            Vector3 facing = Vector3.ProjectOnPlane(chestForward, Vector3.up);
            facing = facing.sqrMagnitude > 1e-6f ? facing.normalized : Vector3.forward;
            Vector3 flat = Vector3.ProjectOnPlane(forearm, Vector3.up);
            if (flat.sqrMagnitude < 1e-8f)
            {
                return facing;
            }

            float upright = Mathf.Abs(Vector3.Dot(forearm.normalized, Vector3.up));
            float towardsChest = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 0.95f, upright));
            Vector3 direction = Vector3.Slerp(flat.normalized, facing, towardsChest);
            return direction.sqrMagnitude > 1e-6f ? direction.normalized : facing;
        }

        /// <summary>
        /// The top of the foot: a planted foot lies flat on the mat (no roll about its length); a
        /// lifted one follows the knee's hinge.
        /// </summary>
        private static Vector3 FootTop(Vector3 ankle, Vector3 toe, Vector3 shinDir, Vector3 shinFacing, bool unconstrained)
        {
            Vector3 along = SafeDir(toe - ankle, Vector3.forward);
            Vector3 hinge = Vector3.Cross(shinDir, shinFacing);
            Vector3 lifted = Vector3.Cross(hinge, along);
            if (unconstrained)
            {
                return lifted;
            }

            Vector3 flat = Vector3.ProjectOnPlane(Vector3.up, along);
            float planted = Mathf.Clamp01((LiftedAnkle - ankle.y) / (LiftedAnkle - PlantedAnkle));
            if (flat.sqrMagnitude < 1e-6f || lifted.sqrMagnitude < 1e-6f || planted <= 0f)
            {
                return lifted;
            }

            // The foot lies flat on the side already facing the mat: the sole when standing, the
            // instep when kneeling. A foot on its edge is left as the leg holds it, so the target
            // never jumps half a turn from one side to the other.
            lifted.Normalize();
            flat.Normalize();
            float facing = Vector3.Dot(lifted, flat);
            float settle = planted * Mathf.SmoothStep(0f, 1f, Mathf.Abs(facing) / 0.5f);
            return Vector3.Slerp(lifted, facing >= 0f ? flat : -flat, settle);
        }

        private static Vector3 SafeDir(Vector3 v, Vector3 fallback) =>
            v.sqrMagnitude > 1e-8f ? v.normalized : fallback;

        private static Vector3 TransportFacing(Vector3 fromDir, Vector3 toDir, Vector3 facing)
        {
            if (Vector3.Dot(fromDir, toDir) < -0.99f)
            {
                Vector3 axis = Vector3.Cross(fromDir, facing);
                if (axis.sqrMagnitude < 1e-6f)
                {
                    axis = Vector3.up;
                }

                return Quaternion.AngleAxis(180f, axis.normalized) * facing;
            }

            return Quaternion.FromToRotation(fromDir, toDir) * facing;
        }

        /// <summary>Rotation with +Y along <paramref name="axis"/> and +Z as close to <paramref name="facing"/> as possible.</summary>
        public static Quaternion Orientation(Vector3 axis, Vector3 facing)
        {
            if (axis.sqrMagnitude < 1e-10f)
            {
                axis = Vector3.up;
            }

            Vector3 depth = Vector3.ProjectOnPlane(facing, axis);
            if (depth.sqrMagnitude < 1e-8f)
            {
                depth = Vector3.ProjectOnPlane(Vector3.forward, axis);
                if (depth.sqrMagnitude < 1e-8f)
                {
                    depth = Vector3.ProjectOnPlane(Vector3.right, axis);
                }
            }

            return Quaternion.LookRotation(depth.normalized, axis.normalized);
        }
    }
}
