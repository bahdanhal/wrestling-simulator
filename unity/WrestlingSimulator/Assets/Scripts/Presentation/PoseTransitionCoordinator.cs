using System;
using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Carries the pre-IK target pose through recorded phase boundaries, including exits and
    /// interruptions. This does not preserve contacts or angular velocity. Supply future raw
    /// samples when available so the incoming positional velocity can be estimated.
    /// </summary>
    public sealed class PoseTransitionCoordinator
    {
        public readonly struct Pair
        {
            public readonly Pose Red;
            public readonly Pose Blue;
            public Pair(in Pose red, in Pose blue) { Red = red; Blue = blue; }
        }

        private const float MaximumFrameGap = .25f;
        private readonly RecordedPoseTransition[] _transitions = { new RecordedPoseTransition(), new RecordedPoseTransition() };
        private readonly Pose[] _last = new Pose[2];
        private readonly Pose[] _previous = new Pose[2];
        private object _owner;
        private int _phase;
        private bool _recorded;
        private bool _hasLast;
        private float _lastTime;
        private float _previousSeconds;
        private float _switchTime;
        private bool _transitioning;
        public bool IsTransitioning => _transitioning;

        public void Reset()
        {
            _hasLast = false;
            _previousSeconds = 0f;
            _transitioning = false;
            foreach (var transition in _transitions) transition.Reset();
        }

        public Pair Sample(object phaseOwner, int phaseIndex, bool recorded, float poseTime,
            in Pose red, in Pose blue, bool hasNext, in Pose nextRed, in Pose nextBlue,
            float nextSeconds, bool reset = false)
        {
            if (float.IsNaN(poseTime) || float.IsInfinity(poseTime))
                throw new ArgumentOutOfRangeException(nameof(poseTime));
            if (hasNext && (float.IsNaN(nextSeconds) || float.IsInfinity(nextSeconds) || nextSeconds <= 0f))
                throw new ArgumentOutOfRangeException(nameof(nextSeconds));

            float elapsed = _hasLast ? poseTime - _lastTime : 0f;
            if (reset || (_hasLast && (elapsed < 0f || elapsed > MaximumFrameGap))) Reset();
            bool switched = _hasLast && (_recorded || recorded)
                && (_recorded != recorded || !ReferenceEquals(_owner, phaseOwner) || _phase != phaseIndex);
            if (switched)
            {
                // The last output was sampled at the previous render time. Advance it to this
                // time using its measured velocity, rather than holding it for one frame.
                Pose outRed = Extrapolate(in _last[0], in _previous[0], _previousSeconds, elapsed);
                Pose outBlue = Extrapolate(in _last[1], in _previous[1], _previousSeconds, elapsed);
                Pose targetRed = hasNext ? nextRed : red;
                Pose targetBlue = hasNext ? nextBlue : blue;
                float targetSeconds = hasNext ? nextSeconds : 1f;
                Pose priorRed = elapsed > 1e-5f ? _last[0] : _previous[0];
                Pose priorBlue = elapsed > 1e-5f ? _last[1] : _previous[1];
                float priorSeconds = elapsed > 1e-5f ? elapsed : _previousSeconds;
                _transitions[0].Begin(in outRed, in priorRed, priorSeconds, in red, in targetRed, targetSeconds);
                _transitions[1].Begin(in outBlue, in priorBlue, priorSeconds, in blue, in targetBlue, targetSeconds);
                _switchTime = poseTime;
                _transitioning = true;
            }
            else if (!_hasLast)
            {
                _switchTime = poseTime;
            }

            Pose resultRed = _transitions[0].Apply(in red, Mathf.Max(0f, poseTime - _switchTime));
            Pose resultBlue = _transitions[1].Apply(in blue, Mathf.Max(0f, poseTime - _switchTime));
            if (poseTime - _switchTime >= 2f) _transitioning = false;
            // Multiple evaluations at the same time must not destroy the velocity history.
            if (!_hasLast || elapsed > 0f)
            {
                _previous[0] = _last[0]; _previous[1] = _last[1];
                _previousSeconds = _hasLast ? elapsed : 0f;
                _last[0] = resultRed; _last[1] = resultBlue;
                _lastTime = poseTime;
            }
            _owner = phaseOwner; _phase = phaseIndex; _recorded = recorded; _hasLast = true;
            return new Pair(in resultRed, in resultBlue);
        }

        private static Pose Extrapolate(in Pose last, in Pose previous, float previousSeconds, float seconds)
        {
            Pose result = last;
            result.PelvisUp = last.PelvisAxis;
            result.PelvisNormal = last.PelvisFront;
            if (previousSeconds <= 1e-5f || previousSeconds > MaximumFrameGap) return result;
            for (int i = 0; i < 17; i++)
            {
                Vector3 point = RecordedPoseTransition.Point(in last, i);
                Vector3 velocity = (point - RecordedPoseTransition.Point(in previous, i)) / previousSeconds;
                RecordedPoseTransition.Set(ref result, i, point + (velocity * seconds));
            }
            return result;
        }
    }
}
