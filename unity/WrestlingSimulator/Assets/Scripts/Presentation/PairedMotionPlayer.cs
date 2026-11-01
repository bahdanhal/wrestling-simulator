using System;
using UnityEngine;
using Wrestling.Data.Motion;

namespace Wrestling.Presentation
{
    /// <summary>Native-time paired playback and explicit matched switches. This does not assign
    /// takes to engine phases or infer contacts; production callers must supply reviewed ranges.</summary>
    public sealed class PairedMotionPlayer
    {
        private MotionPlayback _playback;
        private PairedMotionRange _range;
        private bool _mirrored;
        private float _firstFrame;
        private double _seconds;
        private float _lastDelta;
        private PairedMotionSample _source;
        private readonly RecordedPoseTransition[] _transition = { new RecordedPoseTransition(), new RecordedPoseTransition() };
        private (Pose First, Pose Second) _current;
        private (Pose First, Pose Second) _previous;

        public bool IsStarted => _playback != null;
        public bool IsFinished => IsStarted && SourceFrame >= _range.ToFrame;
        public float SourceFrame => IsStarted ? Mathf.Min(_range.ToFrame, _firstFrame + (float)(_seconds * _range.Take.FramesPerSecond)) : 0f;
        public (Pose First, Pose Second) Current => IsStarted ? _current : throw new InvalidOperationException("Start a recorded pair first.");

        public void Begin(PairedMotionRange range, float frame, bool mirrored, in Pose first, in Pose second,
            float firstHeight, float secondHeight)
        {
            var source = PairedMotionSample.FromRange(range, frame, mirrored);
            Start(range, frame, mirrored, in first, in second, firstHeight, secondHeight);
            _source = source;
            _transition[0].Reset(); _transition[1].Reset();
            _current = Raw(0f); _previous = _current;
            _lastDelta = 0f;
        }

        public (Pose First, Pose Second) Advance(float seconds)
        {
            if (!IsStarted) throw new InvalidOperationException("Start a recorded pair first.");
            if (!Finite(seconds) || seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
            _seconds += seconds;
            var raw = Raw((float)_seconds);
            var next = (_transition[0].Apply(in raw.First, (float)_seconds), _transition[1].Apply(in raw.Second, (float)_seconds));
            if (seconds > 0f)
            {
                _previous = _current;
                _lastDelta = seconds;
            }
            _current = next;
            return _current;
        }

        public PairedMotionMatch Find(PairedMotionSearch search, string state = null, string contact = null,
            float minimumRemainingSeconds = 0.25f)
        {
            if (!IsStarted) throw new InvalidOperationException("Start a recorded pair first.");
            if (search == null) throw new ArgumentNullException(nameof(search));
            // Search the registered source pose. While inertializing, this is not yet the visible
            // blended pose; callers should wait for settling before another pose-based selection.
            var query = _source.AtFrame(SourceFrame);
            return search.Find(query, state: state, contact: contact, minimumRemainingSeconds: minimumRemainingSeconds);
        }

        public void Switch(PairedMotionMatch match, float firstHeight, float secondHeight)
        {
            if (!IsStarted) throw new InvalidOperationException("Start a recorded pair first.");
            if (match == null) throw new ArgumentNullException(nameof(match));
            var outgoing = _current;
            var previous = _previous;
            float previousSeconds = _lastDelta;
            Start(match.Range, match.Frame, match.Mirrored, in outgoing.First, in outgoing.Second, firstHeight, secondHeight);
            _source = match.Sample;
            var incoming = Raw(0f);
            float sampleSeconds = Mathf.Min(1f / _range.Take.FramesPerSecond, match.RemainingDurationSeconds);
            if (sampleSeconds <= 0f)
            {
                // A final-frame match has no forward velocity sample. A stationary target is
                // explicit here; normal candidate selection requires a remaining-time horizon.
                sampleSeconds = 1f / _range.Take.FramesPerSecond;
            }
            var next = Raw(sampleSeconds);
            _transition[0].Begin(in outgoing.First, in previous.First, previousSeconds, in incoming.First, in next.First, sampleSeconds);
            _transition[1].Begin(in outgoing.Second, in previous.Second, previousSeconds, in incoming.Second, in next.Second, sampleSeconds);
            _current = outgoing;
            _previous = outgoing;
            _lastDelta = 0f;
        }

        private void Start(PairedMotionRange range, float frame, bool mirrored, in Pose first, in Pose second, float firstHeight, float secondHeight)
        {
            if (!Finite(firstHeight) || !Finite(secondHeight) || firstHeight <= 0f || secondHeight <= 0f)
                throw new ArgumentOutOfRangeException(nameof(firstHeight));
            _range = range; _mirrored = mirrored;
            _firstFrame = frame; _seconds = 0;
            _playback = new MotionPlayback();
            var phase = new MotionPhase { Clip = "explicit paired playback", Take = range.Take.Id,
                FromFrame = range.FromFrame, ToFrame = range.ToFrame, Executor = range.FirstActor };
            // Alignment must use the matched frame, not the beginning of its candidate range.
            _playback.BeginAtFrame(range.Take, phase, frame, in first, in second, firstHeight, secondHeight, mirrored);
        }

        private (Pose First, Pose Second) Raw(float seconds)
        {
            float frame = Mathf.Min(_range.ToFrame, _firstFrame + (seconds * _range.Take.FramesPerSecond));
            var pose = _playback.Sample((frame - _range.FromFrame) / (_range.ToFrame - _range.FromFrame));
            return (pose.Executor, pose.Opponent);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
