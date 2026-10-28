using System;
using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Match;
using Wrestling.Core.Techniques;

namespace Wrestling.Data.Animation
{
    /// <summary>Which body the clip's coordinates are measured from.</summary>
    public enum ClipFrame
    {
        /// <summary>Origin at the executor, +X towards the opponent (standing techniques and counters).</summary>
        Executor,

        /// <summary>Origin at the bottom wrestler, +X along their facing, +Z to their left (par terre).</summary>
        Bottom,
    }

    /// <summary>Point on the other wrestler's body that an anchored wrestler is held against.</summary>
    public enum AnchorPoint
    {
        None,

        /// <summary>The hip: the pivot of hip tosses and headlock throws.</summary>
        Hips,

        /// <summary>The lower back, where a thrown body lies across in a hip throw.</summary>
        LowerBack,

        /// <summary>Across the shoulders (fireman's carry).</summary>
        Shoulders,

        /// <summary>Chest to chest (body-lock throws, suplexes, lifts).</summary>
        Chest,
    }

    /// <summary>
    /// One wrestler's pose at a keyframe, in the clip's frame. Display data only: the simulation
    /// decides every outcome, and a technique's landing comes from the engine's end state.
    /// </summary>
    public sealed class ActorKey
    {
        /// <summary>Metres along the frame's forward axis.</summary>
        public float X { get; init; }

        /// <summary>Metres to the frame's left.</summary>
        public float Z { get; init; }

        /// <summary>Facing, degrees counter-clockwise from the frame's forward axis.</summary>
        public float Heading { get; init; }

        /// <summary>Hip height, 0 flat to 1 upright (as in the engine's body state).</summary>
        public float Level { get; init; }

        /// <summary>Upper body, 0 bent over to 1 upright.</summary>
        public float Posture { get; init; }

        public GroundState Ground { get; init; }

        /// <summary>Torso rotation for a body on the mat: 0° chest down, 90° on the side, 180° on the back.</summary>
        public float Roll { get; init; }

        /// <summary>+1 rolls towards the wrestler's left, -1 towards the right.</summary>
        public int RollSide { get; init; } = 1;

        /// <summary>Metres the whole body is raised (lifts and throws).</summary>
        public float Lift { get; init; }

        /// <summary>Whole-body rotation about the wrestler's left axis; positive tips the head forward and down.</summary>
        public float Pitch { get; init; }

        /// <summary>Whole-body rotation about the wrestler's forward axis; positive tips towards their left.</summary>
        public float Bank { get; init; }

        /// <summary>
        /// Point on the other wrestler that this wrestler's centre (waist) is held against. A held body
        /// keeps contact: its position and lift follow the anchor, and only its orientation is keyed.
        /// </summary>
        public AnchorPoint Anchor { get; init; }

        /// <summary>0 free (position and lift as keyed) to 1 fully held on the anchor; blends continuously.</summary>
        public float AnchorWeight { get; init; }

        /// <summary>Metres the held waist sits above the anchor point (the bodies' thickness at the contact).</summary>
        public float Contact { get; init; }
    }

    public sealed class ClipKey
    {
        /// <summary>Normalised phase time, 0–1.</summary>
        public float T { get; init; }

        public ActorKey Executor { get; init; } = new ActorKey();

        public ActorKey Opponent { get; init; } = new ActorKey();
    }

    /// <summary>The paired motion for one technique phase, sampled by phase progress.</summary>
    public sealed class MoveClip
    {
        public string Id { get; init; } = string.Empty;

        public ClipFrame Frame { get; init; }

        public IReadOnlyList<ClipKey> Keys { get; init; } = Array.Empty<ClipKey>();

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            string path = string.IsNullOrEmpty(Id) ? "clip" : Id;
            if (string.IsNullOrWhiteSpace(Id))
            {
                errors.Add("Clip id must not be empty.");
            }

            if (Keys.Count < 2)
            {
                errors.Add($"{path}: needs at least two keys.");
                return errors;
            }

            if (Keys[0].T != 0f || Keys[Keys.Count - 1].T != 1f)
            {
                errors.Add($"{path}: keys must start at t=0 and end at t=1.");
            }

            for (int i = 1; i < Keys.Count; i++)
            {
                if (Keys[i].T <= Keys[i - 1].T)
                {
                    errors.Add($"{path}: key times must increase (key {i}).");
                }
            }

            for (int i = 0; i < Keys.Count; i++)
            {
                Check(Keys[i].Executor, $"{path}.keys[{i}].executor", errors);
                Check(Keys[i].Opponent, $"{path}.keys[{i}].opponent", errors);
            }

            return errors;
        }

        /// <summary>Both poses at phase progress <paramref name="t"/> (clamped to 0–1), eased between keys.</summary>
        public (ActorKey Executor, ActorKey Opponent) Sample(float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            int next = 1;
            while (next < Keys.Count - 1 && Keys[next].T < t)
            {
                next++;
            }

            ClipKey a = Keys[next - 1];
            ClipKey b = Keys[next];
            float local = (t - a.T) / (b.T - a.T);
            float eased = local * local * (3f - (2f * local));
            return (Blend(a.Executor, b.Executor, eased, local), Blend(a.Opponent, b.Opponent, eased, local));
        }

        /// <summary>Continuous values eased; discrete ones (ground, roll side) switch halfway between keys.</summary>
        public static ActorKey Blend(ActorKey a, ActorKey b, float eased, float linear) => new ActorKey
        {
            X = Lerp(a.X, b.X, eased),
            Z = Lerp(a.Z, b.Z, eased),
            Heading = a.Heading + (DeltaAngle(a.Heading, b.Heading) * eased),
            Level = Lerp(a.Level, b.Level, eased),
            Posture = Lerp(a.Posture, b.Posture, eased),
            Ground = linear < 0.5f ? a.Ground : b.Ground,
            Roll = Lerp(a.Roll, b.Roll, eased),
            RollSide = linear < 0.5f ? a.RollSide : b.RollSide,
            Lift = Lerp(a.Lift, b.Lift, eased),
            Pitch = Lerp(a.Pitch, b.Pitch, eased),
            Bank = Lerp(a.Bank, b.Bank, eased),
            Anchor = b.AnchorWeight > 0f ? b.Anchor : a.Anchor,
            AnchorWeight = Lerp(a.AnchorWeight, b.AnchorWeight, eased),
            Contact = Lerp(a.Contact, b.Contact, eased),
        };

        private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

        /// <summary>Shortest signed difference between two angles in degrees.</summary>
        public static float DeltaAngle(float from, float to)
        {
            float delta = (to - from) % 360f;
            if (delta > 180f)
            {
                delta -= 360f;
            }
            else if (delta < -180f)
            {
                delta += 360f;
            }

            return delta;
        }

        private static void Check(ActorKey key, string path, List<string> errors)
        {
            if (key == null)
            {
                errors.Add($"{path} is missing.");
                return;
            }

            void Range(float value, float min, float max, string field)
            {
                if (float.IsNaN(value) || value < min || value > max)
                {
                    errors.Add($"{path}.{field} is {value}; expected {min}..{max}.");
                }
            }

            Range(key.X, -3f, 3f, "x");
            Range(key.Z, -3f, 3f, "z");
            Range(key.Heading, -360f, 360f, "heading");
            Range(key.Level, 0f, 1f, "level");
            Range(key.Posture, 0f, 1f, "posture");
            Range(key.Roll, 0f, 180f, "roll");
            Range(key.Lift, 0f, 1.5f, "lift");
            Range(key.Pitch, -300f, 300f, "pitch");
            Range(key.Bank, -300f, 300f, "bank");
            Range(key.AnchorWeight, 0f, 1f, "anchorWeight");
            Range(key.Contact, 0f, 0.4f, "contact");
            if (key.AnchorWeight > 0f && key.Anchor == AnchorPoint.None)
            {
                errors.Add($"{path}: anchorWeight needs an anchor point.");
            }
            if (key.RollSide != 1 && key.RollSide != -1)
            {
                errors.Add($"{path}.rollSide must be 1 or -1.");
            }
        }
    }

    /// <summary>The clips for one technique (<c>data/animation/&lt;technique id&gt;.json</c>).</summary>
    public sealed class MoveAnimationFile
    {
        public int SchemaVersion { get; init; }

        public string Technique { get; init; } = string.Empty;

        public IReadOnlyList<MoveClip> Clips { get; init; } = Array.Empty<MoveClip>();
    }

    /// <summary>All move clips by id, cross-checked against the technique catalogue.</summary>
    public sealed class MoveAnimationLibrary
    {
        private readonly Dictionary<string, MoveClip> _clips;

        private MoveAnimationLibrary(Dictionary<string, MoveClip> clips) => _clips = clips;

        public int Count => _clips.Count;

        public IEnumerable<MoveClip> All => _clips.Values;

        public MoveClip Get(string id) =>
            _clips.TryGetValue(id, out MoveClip? clip) ? clip : throw new KeyNotFoundException($"No move clip '{id}'.");

        /// <summary>
        /// Builds the library. Every phase of every technique must have its clip, every clip must
        /// belong to a phase of the file's technique, and par terre techniques use the bottom frame.
        /// </summary>
        public static MoveAnimationLibrary Create(IEnumerable<MoveAnimationFile> files, TechniqueCatalog catalog)
        {
            var errors = new List<string>();
            var clips = new Dictionary<string, MoveClip>(StringComparer.Ordinal);
            var techniquesSeen = new HashSet<string>(StringComparer.Ordinal);
            foreach (MoveAnimationFile file in files)
            {
                if (!catalog.Contains(file.Technique))
                {
                    errors.Add($"Animation file for unknown technique '{file.Technique}'.");
                    continue;
                }

                if (!techniquesSeen.Add(file.Technique))
                {
                    errors.Add($"Technique '{file.Technique}' has more than one animation file.");
                }

                TechniqueDefinition technique = catalog.Get(file.Technique);
                var phaseClips = new HashSet<string>(technique.Phases.Select(p => p.Animation), StringComparer.Ordinal);
                ClipFrame expectedFrame = IsParTerre(technique) ? ClipFrame.Bottom : ClipFrame.Executor;
                foreach (MoveClip clip in file.Clips)
                {
                    errors.AddRange(clip.Validate());
                    if (!phaseClips.Contains(clip.Id))
                    {
                        errors.Add($"{file.Technique}: clip '{clip.Id}' is not used by any of its phases.");
                    }

                    if (clip.Frame != expectedFrame)
                    {
                        errors.Add($"{clip.Id}: frame must be {expectedFrame}.");
                    }

                    if (!clips.ContainsKey(clip.Id))
                    {
                        clips.Add(clip.Id, clip);
                    }
                    else
                    {
                        errors.Add($"Duplicate clip '{clip.Id}'.");
                    }
                }
            }

            foreach (TechniqueDefinition technique in catalog.All)
            {
                foreach (PhaseDefinition phase in technique.Phases)
                {
                    if (!clips.ContainsKey(phase.Animation))
                    {
                        errors.Add($"{technique.Id} phase '{phase.Id}' has no clip '{phase.Animation}'.");
                    }
                }
            }

            if (errors.Count > 0)
            {
                throw new ArgumentException("Move animations are invalid:\n- " + string.Join("\n- ", errors));
            }

            return new MoveAnimationLibrary(clips);
        }

        /// <summary>Techniques that start only from par terre are animated relative to the bottom wrestler.</summary>
        public static bool IsParTerre(TechniqueDefinition technique) =>
            technique.Start.Positions.Count > 0 && technique.Start.Positions.All(p =>
                p == StartPosition.ParTerreTop || p == StartPosition.ParTerreBottom || p == StartPosition.OpponentTechnique)
            && technique.Start.Positions.Any(p => p == StartPosition.ParTerreTop || p == StartPosition.ParTerreBottom);
    }
}
