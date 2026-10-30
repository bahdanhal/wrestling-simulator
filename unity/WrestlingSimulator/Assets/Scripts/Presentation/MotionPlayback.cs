using UnityEngine;
using Wrestling.Data.Motion;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Plays recorded two-person motion (motion capture or extracted from video) for a technique
    /// phase. The take is placed where the wrestlers are when the phase starts: the executor's pelvis
    /// on the executor, facing the opponent; scaled from the recorded people's height to the
    /// wrestlers'; mirrored when the technique is done to the other side. Takes are right-handed and
    /// Y up; Unity is left-handed, so z is flipped on the way in.
    /// </summary>
    public sealed class MotionPlayback
    {
        private MotionTake _take;
        private MotionPhase _phase;
        private Vector3 _takeOrigin;
        private Quaternion _turn;
        private Vector3 _origin;
        private float _scale;
        private bool _mirror;
        private Vector3 _mirrorNormal;

        /// <summary>Aligns a take to the wrestlers' current poses (call once when the phase starts).</summary>
        public void Begin(MotionTake take, MotionPhase phase, in Pose executor, in Pose opponent, float executorHeight, float opponentHeight, bool leftSide)
        {
            _take = take;
            _phase = phase;
            int recordedExecutor = phase.Executor;
            Vector3 takeExecutor = Read(phase.FromFrame, recordedExecutor, MotionJoint.Pelvis);
            Vector3 takeOpponent = Read(phase.FromFrame, 1 - recordedExecutor, MotionJoint.Pelvis);
            _takeOrigin = new Vector3(takeExecutor.x, 0f, takeExecutor.z);
            Vector3 takeForward = Flat(takeOpponent - takeExecutor);
            Vector3 forward = Flat(opponent.Pelvis - executor.Pelvis);
            _turn = Quaternion.FromToRotation(takeForward, forward);
            _mirrorNormal = Vector3.Cross(Vector3.up, takeForward).normalized;
            _origin = new Vector3(executor.Pelvis.x, 0f, executor.Pelvis.z);
            float recorded = (take.Actors[0].HeightMetres + take.Actors[1].HeightMetres) * 0.5f;
            _scale = ((executorHeight + opponentHeight) * 0.5f) / recorded;
            _mirror = phase.LeftSide != leftSide;
#if UNITY_EDITOR
            Debug.Log($"[motion] {phase.Clip}: take {take.Id} frames {phase.FromFrame}-{phase.ToFrame}, scale {_scale:F2}, mirrored {_mirror}");
#endif
        }

        /// <summary>Both wrestlers' poses at phase progress <paramref name="progress"/> (0–1).</summary>
        public (Pose Executor, Pose Opponent) Sample(float progress)
        {
            float frame = Mathf.Lerp(_phase.FromFrame, _phase.ToFrame, Mathf.Clamp01(progress));
            return (Build(frame, _phase.Executor), Build(frame, 1 - _phase.Executor));
        }

        private Pose Build(float frame, int actor)
        {
            Vector3 J(MotionJoint joint)
            {
                // Mirroring swaps the sides of the body as well as reflecting across the facing.
                MotionJoint source = _mirror ? Swap(joint) : joint;
                Vector3 local = (Read(frame, actor, source) - _takeOrigin) * _scale;
                if (_mirror)
                {
                    local = Vector3.Reflect(local, _mirrorNormal);
                }

                return _origin + (_turn * local);
            }

            var pose = new Pose
            {
                Pelvis = J(MotionJoint.Pelvis),
                Neck = J(MotionJoint.Neck),
                Head = J(MotionJoint.Head),
                ShoulderL = J(MotionJoint.ShoulderL),
                ShoulderR = J(MotionJoint.ShoulderR),
                ElbowL = J(MotionJoint.ElbowL),
                ElbowR = J(MotionJoint.ElbowR),
                HandL = J(MotionJoint.HandL),
                HandR = J(MotionJoint.HandR),
                HipL = J(MotionJoint.HipL),
                HipR = J(MotionJoint.HipR),
                KneeL = J(MotionJoint.KneeL),
                KneeR = J(MotionJoint.KneeR),
                AnkleL = J(MotionJoint.AnkleL),
                AnkleR = J(MotionJoint.AnkleR),
                ToeL = J(MotionJoint.ToeL),
                ToeR = J(MotionJoint.ToeR),
            };
            pose.Spine = (pose.Neck - pose.Pelvis).normalized;
            // The chest faces square to the recorded shoulder line and the pelvis square to the hip
            // line, so the recorded twist of the spine is kept.
            Vector3 shoulders = Vector3.ProjectOnPlane(pose.ShoulderL - pose.ShoulderR, pose.Spine);
            if (shoulders.sqrMagnitude < 1e-8f)
            {
                shoulders = Vector3.ProjectOnPlane(pose.HipL - pose.HipR, pose.Spine);
            }

            pose.Left = shoulders.normalized;
            pose.ChestNormal = Vector3.Cross(pose.Spine, pose.Left).normalized;
            Vector3 hips = pose.HipL - pose.HipR;
            Vector3 pelvisAxis = pose.PelvisAxis;
            pose.PelvisNormal = hips.sqrMagnitude > 1e-8f && pelvisAxis.sqrMagnitude > 1e-6f
                ? Vector3.Cross(pelvisAxis, hips.normalized).normalized
                : pose.ChestNormal;
            return pose;
        }

        private Vector3 Read(float frame, int actor, MotionJoint joint)
        {
            (float x, float y, float z) = _take.Joint(frame, actor, joint);
            return new Vector3(x, y, -z);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        private static MotionJoint Swap(MotionJoint joint)
        {
            switch (joint)
            {
                case MotionJoint.ShoulderL: return MotionJoint.ShoulderR;
                case MotionJoint.ShoulderR: return MotionJoint.ShoulderL;
                case MotionJoint.ElbowL: return MotionJoint.ElbowR;
                case MotionJoint.ElbowR: return MotionJoint.ElbowL;
                case MotionJoint.HandL: return MotionJoint.HandR;
                case MotionJoint.HandR: return MotionJoint.HandL;
                case MotionJoint.HipL: return MotionJoint.HipR;
                case MotionJoint.HipR: return MotionJoint.HipL;
                case MotionJoint.KneeL: return MotionJoint.KneeR;
                case MotionJoint.KneeR: return MotionJoint.KneeL;
                case MotionJoint.AnkleL: return MotionJoint.AnkleR;
                case MotionJoint.AnkleR: return MotionJoint.AnkleL;
                case MotionJoint.ToeL: return MotionJoint.ToeR;
                case MotionJoint.ToeR: return MotionJoint.ToeL;
                default: return joint;
            }
        }
    }
}
