using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Wrestling.Data.Motion
{
    /// <summary>Explicit annotations for a range. Null means unknown, not absence of contact.</summary>
    public sealed class PairedMotionRange
    {
        public MotionTake Take { get; init; } = null!;
        public int FromFrame { get; init; }
        public int ToFrame { get; init; }
        public int FirstActor { get; init; }
        public string? State { get; init; }
        public string? Contact { get; init; }
    }

    /// <summary>A paired sample in recorded coordinates. Mirroring reflects X and exchanges left/right joints.</summary>
    public sealed class PairedMotionSample
    {
        public PairedMotionSample(MotionTake take, float frame, int firstActor = 0, bool mirrored = false)
        {
            Take = take ?? throw new ArgumentNullException(nameof(take));
            if (take.Validate().Count != 0 || !Finite(take.FramesPerSecond) || firstActor < 0 || firstActor > 1 || !Finite(frame) || frame < 0 || frame > take.FrameCount - 1)
                throw new ArgumentException("Invalid take, frame or actor.");
            Frame = frame;
            FirstActor = firstActor;
            Mirrored = mirrored;
            FromFrame = 0;
            ToFrame = take.FrameCount - 1;
        }

        internal PairedMotionSample(PairedMotionRange range, float frame, bool mirrored)
        {
            Take = range.Take;
            Frame = frame;
            FirstActor = range.FirstActor;
            Mirrored = mirrored;
            FromFrame = range.FromFrame;
            ToFrame = range.ToFrame;
        }

        internal int FromFrame { get; }
        internal int ToFrame { get; }
        /// <summary>Validate a range once and create a sample whose velocity stays within it.</summary>
        public static PairedMotionSample FromRange(PairedMotionRange range, float frame, bool mirrored = false)
        {
            if (range == null) throw new ArgumentNullException(nameof(range));
            if (range.Take == null || range.Take.Validate().Count != 0 || !Finite(range.Take.FramesPerSecond) ||
                range.FromFrame < 0 || range.ToFrame >= range.Take.FrameCount || range.FromFrame >= range.ToFrame ||
                range.FirstActor < 0 || range.FirstActor > 1 || !Finite(frame) || frame < range.FromFrame || frame > range.ToFrame)
                throw new ArgumentException("Invalid range or sample frame.", nameof(range));
            return new PairedMotionSample(range, frame, mirrored);
        }

        /// <summary>Advance or reposition a validated sample while preserving its velocity bounds.</summary>
        public PairedMotionSample AtFrame(float frame)
        {
            if (!Finite(frame) || frame < FromFrame || frame > ToFrame)
                throw new ArgumentOutOfRangeException(nameof(frame));
            return new PairedMotionSample(Take, frame, FirstActor, Mirrored, FromFrame, ToFrame);
        }

        private PairedMotionSample(MotionTake take, float frame, int firstActor, bool mirrored, int fromFrame, int toFrame)
        {
            Take = take;
            Frame = frame;
            FirstActor = firstActor;
            Mirrored = mirrored;
            FromFrame = fromFrame;
            ToFrame = toFrame;
        }

        public MotionTake Take { get; }
        public float Frame { get; }
        public int FirstActor { get; }
        public bool Mirrored { get; }
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class PairedMotionMatch
    {
        internal PairedMotionMatch(PairedMotionRange range, int frame, bool mirrored, double score)
        {
            Range = range;
            Frame = frame;
            Mirrored = mirrored;
            Score = score;
        }
        public PairedMotionRange Range { get; }
        public int Frame { get; }
        public bool Mirrored { get; }
        public double Score { get; }
        /// <summary>The matched pair, usable as a subsequent query or continuity reference.</summary>
        public PairedMotionSample Sample => new PairedMotionSample(Range, Frame, Mirrored);
        public float SourceTimeSeconds => Frame / Range.Take.FramesPerSecond;
        public float RemainingDurationSeconds => (Range.ToFrame - Frame) / Range.Take.FramesPerSecond;
    }

    /// <summary>Relative weights for squared pose, velocity, and pair geometry differences.</summary>
    public sealed class PairedMotionWeights
    {
        public double Pose { get; init; } = 1d;
        public double Velocity { get; init; } = 1d;
        public double Pair { get; init; } = 1d;
    }

    /// <summary>
    /// Searches paired ranges without inferring state or contacts. Scores are squared metres and
    /// metres/second in a pair-relative yaw frame, with an optional source-time continuity penalty.
    /// This is presentation data, not a simulation decision or a review of recording quality.
    /// </summary>
    public sealed class PairedMotionSearch
    {
        private readonly PairedMotionRange[] _ranges;
        private readonly double[] _weights;
        private readonly Dictionary<PairedMotionRange, float[][][]> _features = new Dictionary<PairedMotionRange, float[][][]>();

        public PairedMotionSearch(IEnumerable<PairedMotionRange> ranges, PairedMotionWeights? weights = null)
        {
            if (ranges == null) throw new ArgumentNullException(nameof(ranges));
            weights = weights ?? new PairedMotionWeights();
            if (!ValidWeight(weights.Pose) || !ValidWeight(weights.Velocity) || !ValidWeight(weights.Pair))
                throw new ArgumentException("Weights must be finite and nonnegative.", nameof(weights));
            // Feature order: 16 joint position/velocity triples, root velocity, facing, height, per actor; then pair separation.
            var featureWeights = new List<double>();
            for (int actor = 0; actor < 2; actor++)
            {
                for (int joint = 1; joint < MotionTake.JointCount; joint++)
                {
                    for (int i = 0; i < 3; i++) featureWeights.Add(weights.Pose);
                    for (int i = 0; i < 3; i++) featureWeights.Add(weights.Velocity);
                }
                for (int i = 0; i < 3; i++) featureWeights.Add(weights.Velocity);
                for (int i = 0; i < 3; i++) featureWeights.Add(weights.Pair);
                featureWeights.Add(weights.Pose);
            }
            for (int i = 0; i < 3; i++) featureWeights.Add(weights.Pair);
            _weights = featureWeights.ToArray();
            _ranges = ranges.ToArray();
            var validated = new HashSet<MotionTake>();
            foreach (PairedMotionRange range in _ranges)
            {
                if (range == null || range.Take == null || (validated.Add(range.Take) && range.Take.Validate().Count != 0) || !PairedMotionSample.Finite(range.Take.FramesPerSecond) || range.FromFrame < 0 || range.ToFrame >= range.Take.FrameCount || range.FromFrame >= range.ToFrame || range.FirstActor < 0 || range.FirstActor > 1)
                    throw new ArgumentException("Invalid paired motion range.", nameof(ranges));
            }
            _ranges = _ranges.OrderBy(r => r.Take.Id, StringComparer.Ordinal).ThenBy(r => r.FromFrame).ThenBy(r => r.ToFrame).ThenBy(r => r.FirstActor).ToArray();
            foreach (PairedMotionRange range in _ranges)
            {
                var frames = new float[range.ToFrame - range.FromFrame + 1][][];
                for (int f = range.FromFrame; f <= range.ToFrame; f++)
                    frames[f - range.FromFrame] = new[] { Features(range.Take, f, range.FirstActor, false, range.FromFrame, range.ToFrame), Features(range.Take, f, range.FirstActor, true, range.FromFrame, range.ToFrame) };
                _features[range] = frames;
            }
        }

        /// <summary>Create a sample from an indexed range without revalidating its entire take.
        /// Velocity features use only frames inside the range.</summary>
        public PairedMotionSample Sample(PairedMotionRange range, float frame, bool mirrored = false)
        {
            if (range == null) throw new ArgumentNullException(nameof(range));
            if (!_features.ContainsKey(range) || !PairedMotionSample.Finite(frame) || frame < range.FromFrame || frame > range.ToFrame)
                throw new ArgumentException("Sample must be inside an indexed range.");
            return new PairedMotionSample(range, frame, mirrored);
        }

        /// <param name="state">Required explicit state, or null to accept any state.</param>
        /// <param name="contact">Required explicit contact, or null to accept any contact. Unknown never matches a requirement.</param>
        /// <param name="previous">Previous source sample; expectedAdvanceSeconds is in source seconds.</param>
        public PairedMotionMatch? Find(PairedMotionSample query, string? state = null, string? contact = null, PairedMotionSample? previous = null, float expectedAdvanceSeconds = 0f, double continuityWeight = 1d, bool allowMirror = true, float minimumRemainingSeconds = 0f)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (!PairedMotionSample.Finite(minimumRemainingSeconds) || minimumRemainingSeconds < 0 || !PairedMotionSample.Finite(expectedAdvanceSeconds) || expectedAdvanceSeconds < 0 || double.IsNaN(continuityWeight) || double.IsInfinity(continuityWeight) || continuityWeight < 0)
                throw new ArgumentOutOfRangeException(nameof(expectedAdvanceSeconds));
            float[] target = Features(query.Take, query.Frame, query.FirstActor, query.Mirrored, query.FromFrame, query.ToFrame);
            PairedMotionMatch? best = null;
            foreach (PairedMotionRange range in _ranges)
            {
                if (state != null && !string.Equals(state, range.State, StringComparison.Ordinal) || contact != null && !string.Equals(contact, range.Contact, StringComparison.Ordinal)) continue;
                for (int frame = range.FromFrame; frame <= range.ToFrame; frame++)
                {
                    if ((range.ToFrame - frame) / (double)range.Take.FramesPerSecond < minimumRemainingSeconds) continue;
                    for (int m = 0; m <= (allowMirror ? 1 : 0); m++)
                    {
                        bool mirror = m == 1;
                        float[] features = _features[range][frame - range.FromFrame][m];
                        double score = 0;
                        for (int i = 0; i < target.Length; i++) { double d = target[i] - features[i]; score += _weights[i] * d * d; }
                        if (previous != null)
                        {
                            if (ReferenceEquals(previous.Take, range.Take) && previous.FirstActor == range.FirstActor && previous.Mirrored == mirror)
                            {
                                double delta = (frame / (double)range.Take.FramesPerSecond) - (previous.Frame / previous.Take.FramesPerSecond) - expectedAdvanceSeconds;
                                score += continuityWeight * delta * delta;
                            }
                            else score += continuityWeight;
                        }
                        if (best == null || score < best.Score) best = new PairedMotionMatch(range, frame, mirror, score);
                    }
                }
            }
            return best;
        }

        private static bool ValidWeight(double weight) => !double.IsNaN(weight) && !double.IsInfinity(weight) && weight >= 0;

        private static float[] Features(MotionTake take, float frame, int first, bool mirror, int fromFrame, int toFrame)
        {
            var result = new List<float>();
            Vector3 axis = Point(take, frame, first, MotionJoint.ShoulderR, mirror) - Point(take, frame, first, MotionJoint.ShoulderL, mirror);
            axis.Y = 0;
            if (axis.LengthSquared() < 1e-10f)
            {
                axis = Point(take, frame, 1 - first, MotionJoint.Pelvis, mirror) - Point(take, frame, first, MotionJoint.Pelvis, mirror);
                axis.Y = 0;
            }
            axis = axis.LengthSquared() > 1e-10f ? Vector3.Normalize(axis) : Vector3.UnitX;
            Vector3 Forward = new Vector3(-axis.Z, 0, axis.X);
            Vector3 Local(Vector3 v) => new Vector3(Vector3.Dot(v, axis), v.Y, Vector3.Dot(v, Forward));
            float before = Math.Max(fromFrame, frame - 1), after = Math.Min(toFrame, frame + 1);
            float dt = (after - before) / take.FramesPerSecond;
            for (int actor = 0; actor < 2; actor++)
            {
                int a = actor == 0 ? first : 1 - first;
                Vector3 root = Point(take, frame, a, MotionJoint.Pelvis, mirror);
                Vector3 rootBefore = Point(take, before, a, MotionJoint.Pelvis, mirror);
                Vector3 rootAfter = Point(take, after, a, MotionJoint.Pelvis, mirror);
                for (int j = 1; j < MotionTake.JointCount; j++)
                {
                    MotionJoint joint = (MotionJoint)j;
                    Add(result, Local(Point(take, frame, a, joint, mirror) - root));
                    Add(result, Local(((Point(take, after, a, joint, mirror) - rootAfter) - (Point(take, before, a, joint, mirror) - rootBefore)) / dt));
                }
                Add(result, Local((rootAfter - rootBefore) / dt));
                // Shoulder axis supplies facing information without assuming anatomical forward sign.
                Vector3 shoulders = Point(take, frame, a, MotionJoint.ShoulderR, mirror) - Point(take, frame, a, MotionJoint.ShoulderL, mirror);
                shoulders.Y = 0;
                Add(result, Local(shoulders.LengthSquared() > 1e-10f ? Vector3.Normalize(shoulders) : Vector3.Zero));
                // The take uses Y=0 as its estimated ground reference.
                result.Add(root.Y);
            }
            Add(result, Local(Point(take, frame, 1 - first, MotionJoint.Pelvis, mirror) - Point(take, frame, first, MotionJoint.Pelvis, mirror)));
            return result.ToArray();
        }

        private static Vector3 Point(MotionTake take, float frame, int actor, MotionJoint joint, bool mirror)
        {
            if (mirror && (int)joint >= (int)MotionJoint.ShoulderL) joint = (MotionJoint)((int)joint + (((int)joint % 2 == 1) ? 1 : -1));
            var p = take.Joint(frame, actor, joint);
            return new Vector3(mirror ? -p.X : p.X, p.Y, p.Z);
        }
        private static void Add(List<float> values, Vector3 v) { values.Add(v.X); values.Add(v.Y); values.Add(v.Z); }
    }
}
