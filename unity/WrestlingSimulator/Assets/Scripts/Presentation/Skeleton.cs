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
        LeftUpperArm,
        LeftLowerArm,
        LeftHand,
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
    /// Bone hierarchy and the mapping from joint positions to bone transforms. Each bone's +Y runs
    /// along the bone and its +Z faces the front of the body, so the mesh generator and the runtime
    /// figure use one convention.
    /// </summary>
    public static class Skeleton
    {
        public const int BoneCount = 17;

        private static readonly Bone?[] Parents =
        {
            null,
            Bone.Hips,
            Bone.Spine,
            Bone.Chest,
            Bone.Neck,
            Bone.Chest,
            Bone.LeftUpperArm,
            Bone.LeftLowerArm,
            Bone.Chest,
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

        /// <summary>World position and rotation of every bone for a pose (array indexed by <see cref="Bone"/>).</summary>
        public static void Compute(in Pose pose, Vector3[] positions, Quaternion[] rotations, float[] lengths = null)
        {
            Vector3 waist = pose.Waist;
            Vector3 chest = pose.Chest;
            Vector3 front = pose.ChestNormal;
            Vector3 spineUp = (pose.Neck - pose.Pelvis).normalized;
            Vector3 left = pose.Left.sqrMagnitude > 1e-6f ? pose.Left.normalized : Vector3.Cross(front, spineUp).normalized;
            Set(Bone.Hips, pose.Pelvis, waist, front);
            Set(Bone.Spine, waist, chest, front);
            Set(Bone.Chest, chest, pose.Neck, front);
            Vector3 headAxis = pose.Head - pose.Neck;
            Set(Bone.Neck, pose.Neck, pose.Neck + (headAxis * 0.45f), front);
            Set(Bone.Head, pose.Head, pose.Head + headAxis, front);

            Vector3 bindArmL = ((left * 0.88f) - (spineUp * 0.47f)).normalized;
            Vector3 upperArmDirL = SafeDir(pose.ElbowL - pose.ShoulderL, bindArmL);
            Vector3 forearmDirL = SafeDir(pose.HandL - pose.ElbowL, upperArmDirL);
            Vector3 upperFacingL = TransportFacing(bindArmL, upperArmDirL, front);
            Vector3 forearmFacingL = TransportFacing(upperArmDirL, forearmDirL, upperFacingL);
            Set(Bone.LeftUpperArm, pose.ShoulderL, pose.ElbowL, upperFacingL);
            Set(Bone.LeftLowerArm, pose.ElbowL, pose.HandL, forearmFacingL);
            Set(Bone.LeftHand, pose.HandL, pose.HandL + (pose.HandL - pose.ElbowL), forearmFacingL);

            Vector3 bindArmR = ((-left * 0.88f) - (spineUp * 0.47f)).normalized;
            Vector3 upperArmDirR = SafeDir(pose.ElbowR - pose.ShoulderR, bindArmR);
            Vector3 forearmDirR = SafeDir(pose.HandR - pose.ElbowR, upperArmDirR);
            Vector3 upperFacingR = TransportFacing(bindArmR, upperArmDirR, front);
            Vector3 forearmFacingR = TransportFacing(upperArmDirR, forearmDirR, upperFacingR);
            Set(Bone.RightUpperArm, pose.ShoulderR, pose.ElbowR, upperFacingR);
            Set(Bone.RightLowerArm, pose.ElbowR, pose.HandR, forearmFacingR);
            Set(Bone.RightHand, pose.HandR, pose.HandR + (pose.HandR - pose.ElbowR), forearmFacingR);

            Vector3 bindLeg = -spineUp;
            Vector3 thighDirL = SafeDir(pose.KneeL - pose.HipL, bindLeg);
            Vector3 shinDirL = SafeDir(pose.AnkleL - pose.KneeL, thighDirL);
            Vector3 thighFacingL = TransportFacing(bindLeg, thighDirL, front);
            Vector3 shinFacingL = TransportFacing(thighDirL, shinDirL, thighFacingL);
            Set(Bone.LeftUpperLeg, pose.HipL, pose.KneeL, thighFacingL);
            Set(Bone.LeftLowerLeg, pose.KneeL, pose.AnkleL, shinFacingL);
            Set(Bone.LeftFoot, pose.AnkleL, pose.ToeL, Vector3.Cross(pose.Left, pose.ToeL - pose.AnkleL));

            Vector3 thighDirR = SafeDir(pose.KneeR - pose.HipR, bindLeg);
            Vector3 shinDirR = SafeDir(pose.AnkleR - pose.KneeR, thighDirR);
            Vector3 thighFacingR = TransportFacing(bindLeg, thighDirR, front);
            Vector3 shinFacingR = TransportFacing(thighDirR, shinDirR, thighFacingR);
            Set(Bone.RightUpperLeg, pose.HipR, pose.KneeR, thighFacingR);
            Set(Bone.RightLowerLeg, pose.KneeR, pose.AnkleR, shinFacingR);
            Set(Bone.RightFoot, pose.AnkleR, pose.ToeR, Vector3.Cross(pose.Left, pose.ToeR - pose.AnkleR));

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
