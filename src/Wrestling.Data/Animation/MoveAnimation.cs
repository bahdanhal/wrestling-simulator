using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
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

        /// <summary>
        /// Up on the carrier's left shoulder, the carrier's head outside the carried hips (a
        /// double-leg lift: shoulder in the belly, head beside the hip).
        /// </summary>
        LeftShoulder,

        /// <summary>Up on the carrier's right shoulder (see <see cref="LeftShoulder"/>).</summary>
        RightShoulder,
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

        /// <summary>Runtime construction states across the authored key interval; null on authored keys.</summary>
        [JsonIgnore] public GroundState? GroundFrom { get; init; }
        [JsonIgnore] public GroundState? GroundTo { get; init; }
        [JsonIgnore] public float GroundBlend { get; init; }
        [JsonIgnore] public bool GroundFromLying { get; init; }
        [JsonIgnore] public bool GroundToLying { get; init; }

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

    /// <summary>How the thrown wrestler moves in a phase.</summary>
    public enum ThrowKind
    {
        /// <summary>Both wrestlers follow the clip (with physical contact between them).</summary>
        None,

        /// <summary>The thrown wrestler is a physical body, pulled over a fulcrum on the thrower.</summary>
        Throw,

        /// <summary>The entry to a throw: the grips and arm wrap pull the opponent's trunk tight onto the fulcrum, without turning.</summary>
        Load,
    }

    /// <summary>Axis of a throw's rotation, relative to the thrower's facing.</summary>
    public enum ThrowAxis
    {
        /// <summary>Head over heels across the thrower's left-right axis (hip tosses, suplexes, lifts).</summary>
        Lateral,

        /// <summary>Sideways about the thrower's facing (body-lock throws, lateral drops, fireman's carry).</summary>
        Forward,

        /// <summary>
        /// Over the top of the fulcrum, away from the side the thrown wrestler is loaded on, whichever
        /// way the thrower faces (hip, headlock and shoulder throws, counters rolling the thrower
        /// across). Degrees are positive.
        /// </summary>
        Over,
    }

    /// <summary>
    /// A point of the thrown wrestler held by the thrower for the throw. Held by a forearm, it is in
    /// the thrower's hand; held by a trunk segment ("Hips", "Spine", "Spine2"), it is trapped against
    /// the thrower's body (an arm clamped under the armpit, a head locked against the chest) and turns
    /// with the thrower's trunk.
    /// </summary>
    public sealed class ThrowGrip
    {
        /// <summary>Thrower's body segment holding (for example "RightForeArm", or "Spine2" for a trap).</summary>
        public string Thrower { get; init; } = string.Empty;

        /// <summary>Thrown wrestler's segment held (for example "Head", "Spine2", "LeftUpLeg").</summary>
        public string Thrown { get; init; } = string.Empty;
    }

    /// <summary>
    /// The physics of a throw phase: the thrown wrestler's body is driven round the thrower's
    /// <see cref="Pivot"/> segment by <see cref="Degrees"/> between <see cref="From"/> and <see cref="To"/>
    /// of the phase, held by the grips, colliding with the thrower, under gravity; after
    /// <see cref="To"/> it flies free and lands. Positive degrees on the lateral axis take the thrown
    /// wrestler's top towards the thrower's front; on the forward axis, towards the thrower's right;
    /// over the fulcrum, away from the side the thrown wrestler is loaded on.
    /// </summary>
    public sealed class ThrowSpec
    {
        public static readonly ThrowSpec NoThrow = new ThrowSpec();

        public ThrowKind Kind { get; init; }

        public string Pivot { get; init; } = string.Empty;

        public ThrowAxis Axis { get; init; }

        public float Degrees { get; init; }

        public float From { get; init; }

        public float To { get; init; } = 1f;

        public IReadOnlyList<ThrowGrip> Grips { get; init; } = Array.Empty<ThrowGrip>();

        /// <summary>
        /// Where the thrown wrestler's trunk rests on the fulcrum, from 0 (level with it, on the side
        /// facing the thrown wrestler) to 1 (on top of it): about 0.5 for a hip or chest, near 1 for a
        /// body carried across the shoulders.
        /// </summary>
        public float SeatUp { get; init; } = 0.5f;
    }

    /// <summary>The paired motion for one technique phase, sampled by phase progress.</summary>
    public sealed class MoveClip
    {
        public string Id { get; init; } = string.Empty;

        public ClipFrame Frame { get; init; }

        public IReadOnlyList<ClipKey> Keys { get; init; } = Array.Empty<ClipKey>();

        /// <summary>Physics of the thrown wrestler in this phase (<see cref="ThrowKind.None"/> for most clips).</summary>
        public ThrowSpec Throw { get; init; } = ThrowSpec.NoThrow;

        /// <summary>Ragdoll segment names a throw may refer to.</summary>
        public static readonly IReadOnlyCollection<string> Segments = new HashSet<string>(StringComparer.Ordinal)
        {
            "Hips", "Spine", "Spine2", "Head", "LeftArm", "LeftForeArm", "RightArm", "RightForeArm",
            "LeftUpLeg", "LeftLeg", "RightUpLeg", "RightLeg",
        };

        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            string path = string.IsNullOrEmpty(Id) ? "clip" : Id;
            if (Throw == null)
            {
                errors.Add($"{path}: throw is missing.");
            }
            else if (Throw.Kind != ThrowKind.None)
            {
                if (!Segments.Contains(Throw.Pivot))
                {
                    errors.Add($"{path}: throw pivot '{Throw.Pivot}' is not a body segment.");
                }

                if (Throw.From < 0f || Throw.To > 1f || Throw.From >= Throw.To)
                {
                    errors.Add($"{path}: throw window must satisfy 0 <= from < to <= 1.");
                }

                if (Throw.Kind == ThrowKind.Throw && (Math.Abs(Throw.Degrees) > 300f || Throw.Degrees == 0f))
                {
                    errors.Add($"{path}: throw degrees must be non-zero and within ±300.");
                }

                if (Throw.Kind == ThrowKind.Throw && Throw.Axis == ThrowAxis.Over && Throw.Degrees < 0f)
                {
                    errors.Add($"{path}: a throw over the fulcrum turns by positive degrees.");
                }

                if (Throw.SeatUp < 0f || Throw.SeatUp > 1f)
                {
                    errors.Add($"{path}: throw seatUp must be within 0..1.");
                }

                if (Throw.Kind == ThrowKind.Load && Throw.Degrees != 0f)
                {
                    errors.Add($"{path}: a load phase does not turn (degrees must be 0).");
                }

                foreach (ThrowGrip grip in Throw.Grips)
                {
                    if (!Segments.Contains(grip.Thrower) || !Segments.Contains(grip.Thrown))
                    {
                        errors.Add($"{path}: throw grip '{grip.Thrower}' -> '{grip.Thrown}' names an unknown segment.");
                    }
                }
            }
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

        /// <summary>Continuous values and ground construction eased; categorical ground and roll side switch halfway.</summary>
        public static ActorKey Blend(ActorKey a, ActorKey b, float eased, float linear)
        {
            bool fromLying = ClipKinematics.IsLying(a.Ground, a.Roll);
            bool toLying = ClipKinematics.IsLying(b.Ground, b.Roll);
            bool transition = a.Ground != b.Ground || fromLying != toLying;
            return new ActorKey
            {
                X = Lerp(a.X, b.X, eased),
                Z = Lerp(a.Z, b.Z, eased),
                Heading = a.Heading + (DeltaAngle(a.Heading, b.Heading) * eased),
                Level = Lerp(a.Level, b.Level, eased),
                Posture = Lerp(a.Posture, b.Posture, eased),
                Ground = linear < 0.5f ? a.Ground : b.Ground,
                GroundFrom = transition ? a.Ground : (GroundState?)null,
                GroundTo = transition ? b.Ground : (GroundState?)null,
                GroundBlend = eased,
                GroundFromLying = fromLying,
                GroundToLying = toLying,
                Roll = Lerp(a.Roll, b.Roll, eased),
                RollSide = linear < 0.5f ? a.RollSide : b.RollSide,
                Lift = Lerp(a.Lift, b.Lift, eased),
                Pitch = Lerp(a.Pitch, b.Pitch, eased),
                Bank = Lerp(a.Bank, b.Bank, eased),
                Anchor = b.AnchorWeight > 0f ? b.Anchor : a.Anchor,
                AnchorWeight = Lerp(a.AnchorWeight, b.AnchorWeight, eased),
                Contact = Lerp(a.Contact, b.Contact, eased),
            };
        }

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
        /// belong to a phase of the file's technique, par terre techniques use the bottom frame, and
        /// every clip is physically feasible at its phase's minimum length (<see cref="ClipFeasibility"/>).
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
                var minimumTicks = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (PhaseDefinition phase in technique.Phases)
                {
                    minimumTicks[phase.Animation] = minimumTicks.TryGetValue(phase.Animation, out int ticks) ? Math.Min(ticks, phase.MinimumTicks) : phase.MinimumTicks;
                }

                // Clips counter windows choose for this technique's first phase belong to its file too.
                foreach (string entry in EntryClips(catalog, technique.Id))
                {
                    phaseClips.Add(entry);
                    int first = technique.Phases[0].MinimumTicks;
                    minimumTicks[entry] = minimumTicks.TryGetValue(entry, out int ticks) ? Math.Min(ticks, first) : first;
                }

                ClipFrame expectedFrame = IsParTerre(technique) ? ClipFrame.Bottom : ClipFrame.Executor;
                foreach (MoveClip clip in file.Clips)
                {
                    IReadOnlyList<string> invalid = clip.Validate();
                    errors.AddRange(invalid);
                    if (!phaseClips.Contains(clip.Id))
                    {
                        errors.Add($"{file.Technique}: clip '{clip.Id}' is not used by any of its phases.");
                    }
                    else if (invalid.Count == 0)
                    {
                        // Played at the phase's fastest, the clip must ask only for motion a body can make.
                        errors.AddRange(ClipFeasibility.Check(clip, minimumTicks[clip.Id]));
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

                    foreach (CounterWindow window in phase.CounterWindows)
                    {
                        if (window.Animation != null && !clips.ContainsKey(window.Animation))
                        {
                            errors.Add($"{technique.Id} phase '{phase.Id}' counter '{window.Technique}' has no clip '{window.Animation}' in that counter's animation file.");
                        }
                    }
                }
            }

            if (errors.Count > 0)
            {
                throw new ArgumentException("Move animations are invalid:\n- " + string.Join("\n- ", errors));
            }

            return new MoveAnimationLibrary(clips);
        }

        /// <summary>Clips that counter windows anywhere in the catalogue choose for <paramref name="counter"/>'s first phase.</summary>
        public static IEnumerable<string> EntryClips(TechniqueCatalog catalog, string counter) =>
            catalog.All.SelectMany(t => t.Phases).SelectMany(p => p.CounterWindows)
                .Where(w => w.Technique == counter && w.Animation != null)
                .Select(w => w.Animation!)
                .Distinct(StringComparer.Ordinal);

        /// <summary>Techniques that start only from par terre are animated relative to the bottom wrestler.</summary>
        public static bool IsParTerre(TechniqueDefinition technique) =>
            technique.Start.Positions.Count > 0 && technique.Start.Positions.All(p =>
                p == StartPosition.ParTerreTop || p == StartPosition.ParTerreBottom || p == StartPosition.OpponentTechnique)
            && technique.Start.Positions.Any(p => p == StartPosition.ParTerreTop || p == StartPosition.ParTerreBottom);
    }
}
