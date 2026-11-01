using System;
using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>Decays a recorded-pose switch's position and velocity gaps in source seconds.
    /// This supplies raw target positional continuity, not recorded footfalls or grip constraints.
    /// Subsequent limb IK and physical tracking can change the displayed path.</summary>
    public sealed class RecordedPoseTransition
    {
        private const float Rate = 14f;
        private readonly Vector3[] _offset = new Vector3[17];
        private readonly Vector3[] _slope = new Vector3[17];
        private bool _active;
        private Quaternion _pelvisOffset;
        private Quaternion _chestOffset;

        public void Reset() => _active = false;

        public void Begin(in Pose outgoing, in Pose previous, float previousSeconds,
            in Pose incoming, in Pose nextIncoming, float nextSeconds, float sampleOffsetSeconds = 0f)
        {
            if (!Finite(previousSeconds) || !Finite(nextSeconds) || previousSeconds < 0f || nextSeconds <= 0f
                || !Finite(sampleOffsetSeconds) || sampleOffsetSeconds < 0f || sampleOffsetSeconds > 0.25f)
                throw new ArgumentOutOfRangeException(nameof(nextSeconds));
            for (int i = 0; i < 17; i++)
            {
                Vector3 velocity = previousSeconds > 1e-5f && previousSeconds <= 0.25f
                    ? (Point(in outgoing, i) - Point(in previous, i)) / previousSeconds : Vector3.zero;
                Vector3 targetVelocity = (Point(in nextIncoming, i) - Point(in incoming, i)) / nextSeconds;
                Vector3 gap = Point(in outgoing, i) - (Point(in incoming, i) - (targetVelocity * sampleOffsetSeconds));
                _offset[i] = gap;
                _slope[i] = velocity - targetVelocity + (Rate * gap);
            }
            Quaternion PreviousFrame(Quaternion current, Quaternion next) =>
                Quaternion.SlerpUnclamped(current, next, -sampleOffsetSeconds / nextSeconds);
            _pelvisOffset = Skeleton.Orientation(outgoing.PelvisAxis, outgoing.PelvisFront)
                * Quaternion.Inverse(PreviousFrame(Skeleton.Orientation(incoming.PelvisAxis, incoming.PelvisFront),
                    Skeleton.Orientation(nextIncoming.PelvisAxis, nextIncoming.PelvisFront)));
            _chestOffset = Skeleton.Orientation(outgoing.Spine, outgoing.ChestNormal)
                * Quaternion.Inverse(PreviousFrame(Skeleton.Orientation(incoming.Spine, incoming.ChestNormal),
                    Skeleton.Orientation(nextIncoming.Spine, nextIncoming.ChestNormal)));
            _active = true;
        }

        public Pose Apply(in Pose incoming, float elapsedSeconds)
        {
            if (!Finite(elapsedSeconds) || elapsedSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (!_active || elapsedSeconds >= 2f) return incoming;
            Pose result = incoming;
            float decay = Mathf.Exp(-Rate * elapsedSeconds);
            for (int i = 0; i < 17; i++)
                Set(ref result, i, Point(in incoming, i) + ((_offset[i] + (_slope[i] * elapsedSeconds)) * decay));
            // Preserve the outgoing frame, including the pelvis frame frozen before leg IK. Never
            // reconstruct it from already-retargeted knees. Rotational offsets decay separately;
            // the C1 guarantee here is for joint positions, not angular velocities or contacts.
            float rotationKeep = (1f + (Rate * elapsedSeconds)) * decay;
            Quaternion pelvis = Quaternion.SlerpUnclamped(Quaternion.identity, _pelvisOffset, rotationKeep)
                * Skeleton.Orientation(incoming.PelvisAxis, incoming.PelvisFront);
            Quaternion chest = Quaternion.SlerpUnclamped(Quaternion.identity, _chestOffset, rotationKeep)
                * Skeleton.Orientation(incoming.Spine, incoming.ChestNormal);
            result.PelvisUp = pelvis * Vector3.up;
            result.PelvisNormal = pelvis * Vector3.forward;
            result.Spine = chest * Vector3.up;
            result.ChestNormal = chest * Vector3.forward;
            result.Left = chest * Vector3.left;
            return result;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static Vector3 Point(in Pose pose, int i)
        {
            switch (i)
            {
                case 0: return pose.Pelvis;
                case 1: return pose.Neck;
                case 2: return pose.Head;
                case 3: return pose.ShoulderL;
                case 4: return pose.ShoulderR;
                case 5: return pose.ElbowL;
                case 6: return pose.ElbowR;
                case 7: return pose.HandL;
                case 8: return pose.HandR;
                case 9: return pose.HipL;
                case 10: return pose.HipR;
                case 11: return pose.KneeL;
                case 12: return pose.KneeR;
                case 13: return pose.AnkleL;
                case 14: return pose.AnkleR;
                case 15: return pose.ToeL;
                case 16: return pose.ToeR;
                default: throw new ArgumentOutOfRangeException(nameof(i));
            }
        }

        private static void Set(ref Pose pose, int i, Vector3 value)
        {
            switch (i)
            {
                case 0: pose.Pelvis = value; break;
                case 1: pose.Neck = value; break;
                case 2: pose.Head = value; break;
                case 3: pose.ShoulderL = value; break;
                case 4: pose.ShoulderR = value; break;
                case 5: pose.ElbowL = value; break;
                case 6: pose.ElbowR = value; break;
                case 7: pose.HandL = value; break;
                case 8: pose.HandR = value; break;
                case 9: pose.HipL = value; break;
                case 10: pose.HipR = value; break;
                case 11: pose.KneeL = value; break;
                case 12: pose.KneeR = value; break;
                case 13: pose.AnkleL = value; break;
                case 14: pose.AnkleR = value; break;
                case 15: pose.ToeL = value; break;
                case 16: pose.ToeR = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(i));
            }
        }
    }
}
