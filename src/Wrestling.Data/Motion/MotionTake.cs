using System;
using System.Collections.Generic;
using System.Linq;

namespace Wrestling.Data.Motion
{
    /// <summary>
    /// The joints a motion take records for each wrestler, in this order. They match the
    /// presentation's pose (pelvis is the midpoint of the hips; hands are wrists; toes are the
    /// ball of the foot).
    /// </summary>
    public enum MotionJoint
    {
        Pelvis,
        Neck,
        Head,
        ShoulderL,
        ShoulderR,
        ElbowL,
        ElbowR,
        HandL,
        HandR,
        HipL,
        HipR,
        KneeL,
        KneeR,
        AnkleL,
        AnkleR,
        ToeL,
        ToeR,
    }

    /// <summary>Where a take came from, for attribution and licence records.</summary>
    public sealed class MotionSource
    {
        public string Title { get; init; } = string.Empty;

        public string Url { get; init; } = string.Empty;

        public string Author { get; init; } = string.Empty;

        /// <summary>Licence identifier, for example "CC-BY-SA-4.0", or "permission" with the date it was granted in <see cref="Notes"/>.</summary>
        public string License { get; init; } = string.Empty;

        public string Notes { get; init; } = string.Empty;
    }

    public sealed class MotionActor
    {
        public string Name { get; init; } = string.Empty;

        /// <summary>Standing height of the recorded person in metres, used to scale the take to each wrestler.</summary>
        public float HeightMetres { get; init; }
    }

    /// <summary>
    /// Recorded motion of two wrestlers (<c>data/motion/takes/&lt;id&gt;.json</c>): per frame and
    /// actor, the world position of each <see cref="MotionJoint"/> in metres, Y up. Taken from motion
    /// capture or extracted from video; display data only.
    /// </summary>
    public sealed class MotionTake
    {
        public static readonly int JointCount = Enum.GetValues(typeof(MotionJoint)).Length;

        public int SchemaVersion { get; init; }

        public string Id { get; init; } = string.Empty;

        public MotionSource Source { get; init; } = new MotionSource();

        public float FramesPerSecond { get; init; }

        public IReadOnlyList<MotionActor> Actors { get; init; } = Array.Empty<MotionActor>();

        /// <summary>frames[frame][actor] = x, y, z for each joint in <see cref="MotionJoint"/> order.</summary>
        public IReadOnlyList<IReadOnlyList<IReadOnlyList<float>>> Frames { get; init; } = Array.Empty<IReadOnlyList<IReadOnlyList<float>>>();

        public int FrameCount => Frames?.Count ?? 0;

        /// <summary>Position of a joint (x, y, z), linearly interpolated at a fractional frame (clamped to the take).</summary>
        public (float X, float Y, float Z) Joint(float frame, int actor, MotionJoint joint)
        {
            if (float.IsNaN(frame) || float.IsInfinity(frame))
            {
                throw new ArgumentOutOfRangeException(nameof(frame), "Frame must be finite.");
            }

            if (Actors == null || actor < 0 || actor >= Actors.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(actor), "Actor index is outside the take.");
            }

            int jointIndex = (int)joint;
            if (jointIndex < 0 || jointIndex >= JointCount)
            {
                throw new ArgumentOutOfRangeException(nameof(joint), "Joint is not a defined motion joint.");
            }

            if (Frames == null || Frames.Count == 0)
            {
                throw new InvalidOperationException("Take has no frames to sample.");
            }

            float clamped = Math.Max(0f, Math.Min(FrameCount - 1, frame));
            int a = (int)Math.Floor(clamped);
            int b = Math.Min(a + 1, FrameCount - 1);
            float t = clamped - a;
            IReadOnlyList<float> fa = GetJointValues(a, actor);
            IReadOnlyList<float> fb = GetJointValues(b, actor);
            int i = jointIndex * 3;
            return (Lerp(fa[i], fb[i], t), Lerp(fa[i + 1], fb[i + 1], t), Lerp(fa[i + 2], fb[i + 2], t));
        }

        private IReadOnlyList<float> GetJointValues(int frame, int actor)
        {
            IReadOnlyList<IReadOnlyList<float>> actors = Frames[frame];
            if (actors == null || actor >= actors.Count || actors[actor] == null || actors[actor].Count != JointCount * 3)
            {
                throw new InvalidOperationException($"Frame {frame} does not contain valid joint values for actor {actor}.");
            }

            return actors[actor];
        }

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            string path = string.IsNullOrEmpty(Id) ? "take" : Id;
            if (string.IsNullOrWhiteSpace(Id))
            {
                errors.Add("Take id must not be empty.");
            }

            if (Source == null || string.IsNullOrWhiteSpace(Source.License) || string.IsNullOrWhiteSpace(Source.Url) && string.IsNullOrWhiteSpace(Source.Notes))
            {
                errors.Add($"{path}: source needs a licence and a URL or notes (where it came from).");
            }

            if (float.IsNaN(FramesPerSecond) || float.IsInfinity(FramesPerSecond) || FramesPerSecond <= 0f || FramesPerSecond > 1000f)
            {
                errors.Add($"{path}: framesPerSecond must be in (0, 1000].");
            }

            if (Actors == null)
            {
                errors.Add($"{path}: actors must not be null.");
            }
            else if (Actors.Count != 2)
            {
                errors.Add($"{path}: a take records exactly two actors.");
            }

            if (Actors != null)
            {
                for (int i = 0; i < Actors.Count; i++)
                {
                    MotionActor actor = Actors[i];
                    if (actor == null)
                    {
                        errors.Add($"{path}: actor {i} must not be null.");
                        continue;
                    }

                    if (float.IsNaN(actor.HeightMetres) || float.IsInfinity(actor.HeightMetres) || actor.HeightMetres < 1.2f || actor.HeightMetres > 2.3f)
                    {
                        errors.Add($"{path}: actor '{actor.Name}' height {actor.HeightMetres} m is not a plausible adult height.");
                    }
                }
            }

            if (Frames == null)
            {
                errors.Add($"{path}: frames must not be null.");
                return errors;
            }

            if (Frames.Count < 2)
            {
                errors.Add($"{path}: needs at least two frames.");
            }

            for (int f = 0; f < Frames.Count; f++)
            {
                IReadOnlyList<IReadOnlyList<float>> frame = Frames[f];
                if (frame == null)
                {
                    errors.Add($"{path}: frame {f} must not be null.");
                    continue;
                }

                if (Actors != null && frame.Count != Actors.Count)
                {
                    errors.Add($"{path}: frame {f} has {frame.Count} actors.");
                    continue;
                }

                foreach (IReadOnlyList<float> joints in frame)
                {
                    if (joints == null)
                    {
                        errors.Add($"{path}: frame {f} has a null actor joint list.");
                        continue;
                    }

                    if (joints.Count != JointCount * 3)
                    {
                        errors.Add($"{path}: frame {f} has {joints.Count} values per actor; expected {JointCount * 3}.");
                        continue;
                    }

                    if (joints.Any(v => float.IsNaN(v) || float.IsInfinity(v) || Math.Abs(v) > 100f))
                    {
                        errors.Add($"{path}: frame {f} has an invalid coordinate.");
                        continue;
                    }
                }
            }

            return errors;
        }

        private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
    }

    /// <summary>One technique phase played from a take: its frame range and which actor executes the technique.</summary>
    public sealed class MotionPhase
    {
        /// <summary>The phase's animation id (a technique phase's <c>animation</c>).</summary>
        public string Clip { get; init; } = string.Empty;

        public string Take { get; init; } = string.Empty;

        public int FromFrame { get; init; }

        public int ToFrame { get; init; }

        /// <summary>Index (0 or 1) of the actor who performs the technique.</summary>
        public int Executor { get; init; }

        /// <summary>True when the recorded technique is done to the executor's left side (the game mirrors it as needed).</summary>
        public bool LeftSide { get; init; }
    }

    /// <summary>Which technique phases have recorded motion (<c>data/motion/index.json</c>).</summary>
    public sealed class MotionIndexFile
    {
        public int SchemaVersion { get; init; }

        public IReadOnlyList<MotionPhase> Phases { get; init; } = Array.Empty<MotionPhase>();
    }

    /// <summary>Recorded motion by technique phase, cross-checked against the takes and the move clips.</summary>
    public sealed class MotionLibrary
    {
        private readonly Dictionary<string, (MotionPhase Phase, MotionTake Take)> _byClip;

        private MotionLibrary(Dictionary<string, (MotionPhase, MotionTake)> byClip, IReadOnlyList<MotionTake> takes)
        {
            _byClip = byClip;
            Takes = takes;
        }

        public IReadOnlyList<MotionTake> Takes { get; }

        public int PhaseCount => _byClip.Count;

        /// <summary>The recorded motion for a phase's clip, if any.</summary>
        public bool TryGet(string clip, out MotionPhase phase, out MotionTake take)
        {
            if (_byClip.TryGetValue(clip, out (MotionPhase Phase, MotionTake Take) entry))
            {
                phase = entry.Phase;
                take = entry.Take;
                return true;
            }

            phase = null!;
            take = null!;
            return false;
        }

        public static MotionLibrary Create(MotionIndexFile index, IEnumerable<MotionTake> takes, ISet<string> clipIds)
        {
            var errors = new List<string>();
            var byId = new Dictionary<string, MotionTake>(StringComparer.Ordinal);
            foreach (MotionTake take in takes)
            {
                errors.AddRange(take.Validate());
                if (!byId.ContainsKey(take.Id))
                {
                    byId.Add(take.Id, take);
                }
                else
                {
                    errors.Add($"Duplicate take '{take.Id}'.");
                }
            }

            var byClip = new Dictionary<string, (MotionPhase, MotionTake)>(StringComparer.Ordinal);
            foreach (MotionPhase phase in index.Phases)
            {
                if (!clipIds.Contains(phase.Clip))
                {
                    errors.Add($"Motion for unknown clip '{phase.Clip}'.");
                    continue;
                }

                if (!byId.TryGetValue(phase.Take, out MotionTake? take))
                {
                    errors.Add($"{phase.Clip}: unknown take '{phase.Take}'.");
                    continue;
                }

                if (phase.FromFrame < 0 || phase.ToFrame >= take.FrameCount || phase.FromFrame >= phase.ToFrame)
                {
                    errors.Add($"{phase.Clip}: frames {phase.FromFrame}–{phase.ToFrame} are outside take '{take.Id}' (0–{take.FrameCount - 1}).");
                }

                if (phase.Executor != 0 && phase.Executor != 1)
                {
                    errors.Add($"{phase.Clip}: executor must be 0 or 1.");
                }

                if (byClip.ContainsKey(phase.Clip))
                {
                    errors.Add($"{phase.Clip}: more than one recorded motion.");
                    continue;
                }

                byClip.Add(phase.Clip, (phase, take));
            }

            if (errors.Count > 0)
            {
                throw new ArgumentException("Motion data is invalid:\n- " + string.Join("\n- ", errors));
            }

            return new MotionLibrary(byClip, byId.Values.ToList());
        }
    }
}
