using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Wrestling.Core.Match;

namespace Wrestling.Data.Animation
{
    /// <summary>
    /// Checks that a keyed clip asks only for motion a human body can make, measured on the
    /// reference body with <see cref="ClipKinematics"/> at the fastest the engine plays the phase
    /// (its minimum ticks):
    /// <list type="bullet">
    /// <item>from wherever the executor is at rest (the phase's start, the top of a rise) their
    /// hips never drop faster than free fall (a body's legs can only push it up), unless the other
    /// wrestler carries them or, once a throw turns, the falling thrown body pulls them down
    /// through the holds;</item>
    /// <item>no lift on a body that is on its knees or lying on the mat, except in a throw;</item>
    /// <item>while a throw over the hips loads (and through the first half of its turn) the
    /// thrower's hips are well under the opponent's and the trunk is bent forward over them; a
    /// shoulder carry has the carrier's hips under the carried body's.</item>
    /// <item>while one body is held on the other (before a throw turns), the two trunks touch
    /// but do not pass through each other: chest to chest, chest to back or across the
    /// shoulders. The viewer only pushes free bodies apart, so a held pose that crosses would
    /// leave the physical bodies fighting the clip.</item>
    /// </list>
    /// </summary>
    public static class ClipFeasibility
    {
        /// <summary>How far (m) a thrower's hips must be under the opponent's at a hip or shoulder fulcrum.</summary>
        public const float FulcrumDrop = 0.14f;

        /// <summary>Trunk lean (degrees from vertical) of a thrower loading over the hip.</summary>
        public const float MinimumLoadLean = 35f;

        public const float MaximumLoadLean = 55f;

        /// <summary>
        /// How far (m) two held trunks may press into each other: soft tissue compressed between
        /// chests or chest and back, and the trunk's rounding where a capsule overstates it. The
        /// neutral tie-up a hold starts from measures 0.07 m at the collar (the capsules ignore the
        /// heads passing side by side), so the allowance covers it; trunks that cross measure
        /// 0.11 m and more.
        /// </summary>
        public const float HeldCompression = 0.08f;

        /// <summary>Samples per clip for the held-trunk clearance check (plus every key).</summary>
        private const int ClearanceSamples = 50;

        /// <summary>Hip drops smaller than this (m) are posture changes, not falls.</summary>
        private const float SmallestDrop = 0.02f;

        private static readonly HashSet<string> HipPivots = new HashSet<string>(StringComparer.Ordinal) { "Hips", "Spine", "LeftUpLeg", "RightUpLeg" };

        /// <summary>Problems with <paramref name="clip"/> played in <paramref name="minimumTicks"/> engine ticks (empty when it is feasible).</summary>
        public static IReadOnlyList<string> Check(MoveClip clip, int minimumTicks)
        {
            var errors = new List<string>();
            if (clip.Keys.Count < 2 || minimumTicks <= 0)
            {
                return errors;
            }

            ClipKinematics.Body body = ClipKinematics.Body.Reference;
            float seconds = minimumTicks / (float)BoutClock.TicksPerSecond;
            bool executorBottom = clip.Frame == ClipFrame.Bottom && IsBottom(clip, executor: true);
            bool opponentBottom = clip.Frame == ClipFrame.Bottom && !executorBottom;
            var executor = new ClipKinematics.Trunk[clip.Keys.Count];
            var opponent = new ClipKinematics.Trunk[clip.Keys.Count];
            for (int i = 0; i < clip.Keys.Count; i++)
            {
                (executor[i], opponent[i]) = Place(clip.Keys[i], body, executorBottom, opponentBottom);
            }

            bool throwing = clip.Throw.Kind != ThrowKind.None;
            for (int i = 0; i < clip.Keys.Count; i++)
            {
                ClipKey key = clip.Keys[i];
                if (!throwing)
                {
                    GroundedLift(key.Executor, clip.Id, i, "executor", errors);
                    GroundedLift(key.Opponent, clip.Id, i, "opponent", errors);
                }
            }

            // Free fall: the body is at rest at the start of the phase and at every key where its
            // hips stop rising (a top); from there to any later key the hips can drop no further
            // than a free fall in that time. Keys in the middle of a drop are not rests: the body
            // may be passing through them at speed.
            // Once a throw turns, the thrower goes down with the thrown body, pulled through the
            // holds by its fall; before that (and in every other clip) they drop under their own
            // weight alone. The opponent's drops are what the move does to them: the executor
            // drives them down with hands and legs braced on the mat, which can be faster than a
            // fall. A held body goes where its carrier takes it.
            for (int rest = 0; rest < clip.Keys.Count - 1; rest++)
            {
                if (rest > 0 && executor[rest - 1].Pelvis.Y > executor[rest].Pelvis.Y + SmallestDrop)
                {
                    continue;
                }

                for (int later = rest + 1; later < clip.Keys.Count; later++)
                {
                    ClipKey from = clip.Keys[rest];
                    ClipKey to = clip.Keys[later];
                    bool pulled = clip.Throw.Kind == ThrowKind.Throw && clip.Throw.Grips.Count > 0 && to.T > clip.Throw.From;
                    if (pulled || Held(from.Executor) || Held(to.Executor))
                    {
                        break;
                    }

                    FreeFall(executor[rest].Pelvis.Y, executor[later].Pelvis.Y, (to.T - from.T) * seconds, clip.Id, rest, later, errors);
                }
            }

            if (throwing)
            {
                Fulcrum(clip, executor, opponent, errors);
            }

            HeldClearance(clip, body, executorBottom, opponentBottom, errors);
            return errors;
        }

        /// <summary>
        /// Gap (m) between two trunks, each a capsule along its spine as deep as the body's
        /// chest: negative when they overlap.
        /// </summary>
        public static float TrunkClearance(in ClipKinematics.Trunk a, in ClipKinematics.Trunk b, ClipKinematics.Body body) =>
            SegmentDistance(a.Pelvis, a.Neck, b.Pelvis, b.Neck) - (2f * body.TorsoHalfDepth);

        private static void HeldClearance(MoveClip clip, ClipKinematics.Body body, bool executorBottom, bool opponentBottom, List<string> errors)
        {
            var times = new List<float>();
            for (int s = 0; s <= ClearanceSamples; s++)
            {
                times.Add(s / (float)ClearanceSamples);
            }

            foreach (ClipKey key in clip.Keys)
            {
                times.Add(key.T);
            }

            times.Sort();
            float worst = float.MaxValue;
            float worstAt = 0f;
            foreach (float t in times)
            {
                if (clip.Throw.Kind == ThrowKind.Throw && t > clip.Throw.From)
                {
                    // Once the throw turns, the physical carry places the thrown body, not the keys.
                    break;
                }

                (ActorKey e, ActorKey o) = clip.Sample(t);
                if (!Held(e) && !Held(o))
                {
                    continue;
                }

                (ClipKinematics.Trunk te, ClipKinematics.Trunk to) = Place(new ClipKey { T = t, Executor = e, Opponent = o }, body, executorBottom, opponentBottom);
                float clearance = TrunkClearance(in te, in to, body);
                if (clearance < worst)
                {
                    worst = clearance;
                    worstAt = t;
                }
            }

            if (worst < -HeldCompression)
            {
                errors.Add($"{clip.Id}@{F(worstAt)}: the held trunks overlap by {F(-worst)} m; held bodies may press in by at most {F(HeldCompression)} m (they touch, they do not pass through each other).");
            }
        }

        /// <summary>Distance between two segments (Ericson, Real-Time Collision Detection 5.1.9).</summary>
        private static float SegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1;
            Vector3 d2 = q2 - p2;
            Vector3 r = p1 - p2;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);
            float s;
            float t;
            if (a <= 1e-8f && e <= 1e-8f)
            {
                return r.Length();
            }

            if (a <= 1e-8f)
            {
                s = 0f;
                t = Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-8f)
                {
                    t = 0f;
                    s = Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denominator = (a * e) - (b * b);
                    s = denominator > 1e-8f ? Clamp01(((b * f) - (c * e)) / denominator) : 0f;
                    t = ((b * s) + f) / e;
                    if (t < 0f)
                    {
                        t = 0f;
                        s = Clamp01(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = Clamp01((b - c) / a);
                    }
                }
            }

            return ((p1 + (d1 * s)) - (p2 + (d2 * t))).Length();
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

        /// <summary>
        /// Checks executor hip drops along the interpolated path, not just at authored keys.
        /// This diagnostic intentionally retains the same gravity and held-body exceptions as
        /// <see cref="Check"/>. It can expose legacy clips whose endpoints pass while their eased
        /// motion does not; passing it establishes only this physical bound, not human realism.
        /// </summary>
        public static IReadOnlyList<string> CheckSampledPath(MoveClip clip, int minimumTicks)
        {
            var errors = new List<string>();
            if (clip.Keys.Count < 2 || minimumTicks <= 0) return errors;
            ClipKinematics.Body body = ClipKinematics.Body.Reference;
            bool executorBottom = clip.Frame == ClipFrame.Bottom && IsBottom(clip, true);
            bool opponentBottom = clip.Frame == ClipFrame.Bottom && !executorBottom;
            float seconds = minimumTicks / (float)BoutClock.TicksPerSecond;
            // Include quarter-tick samples, matching the presentation's 240 Hz physics targets.
            int samples = minimumTicks * 4;
            for (int rest = 0; rest < clip.Keys.Count - 1; rest++)
            {
                ClipKey from = clip.Keys[rest];
                var fromTrunk = Place(from, body, executorBottom, opponentBottom).Executor;
                if (rest > 0)
                {
                    var before = Place(clip.Keys[rest - 1], body, executorBottom, opponentBottom).Executor;
                    if (before.Pelvis.Y > fromTrunk.Pelvis.Y + SmallestDrop) continue;
                }

                for (int sample = 1; sample <= samples; sample++)
                {
                    float t = sample / (float)samples;
                    if (t <= from.T) continue;
                    (ActorKey executor, ActorKey opponent) = clip.Sample(t);
                    bool pulled = clip.Throw.Kind == ThrowKind.Throw && clip.Throw.Grips.Count > 0 && t > clip.Throw.From;
                    if (pulled || Held(from.Executor) || Held(executor)) break;
                    var key = new ClipKey { T = t, Executor = executor, Opponent = opponent };
                    float height = Place(key, body, executorBottom, opponentBottom).Executor.Pelvis.Y;
                    float drop = fromTrunk.Pelvis.Y - height;
                    if (drop < SmallestDrop) continue;
                    float span = (t - from.T) * seconds;
                    float soonest = (float)Math.Sqrt(2f * drop / ClipKinematics.Gravity);
                    if (span < soonest)
                    {
                        errors.Add($"{clip.Id}.keys[{rest}..sample@{F(t)}].executor: hips drop {F(drop)} m in {F(span)} s from rest along the sampled path, faster than free fall ({F(soonest)} s).");
                        break;
                    }
                }
            }

            return errors;
        }

        /// <summary>Both trunks at a key, the held one seated on its carrier.</summary>
        public static (ClipKinematics.Trunk Executor, ClipKinematics.Trunk Opponent) Place(ClipKey key, ClipKinematics.Body body, bool executorBottom, bool opponentBottom)
        {
            ClipKinematics.Trunk executor = ClipKinematics.Place(key.Executor, body, executorBottom);
            ClipKinematics.Trunk opponent = ClipKinematics.Place(key.Opponent, body, opponentBottom);
            if (Held(key.Opponent))
            {
                ClipKinematics.Hold(ref opponent, key.Opponent, in executor, body);
            }
            else if (Held(key.Executor))
            {
                ClipKinematics.Hold(ref executor, key.Executor, in opponent, body);
            }

            return (executor, opponent);
        }

        /// <summary>In a par terre clip, whether the executor is the bottom wrestler (the one the clip's frame starts on).</summary>
        public static bool IsBottom(MoveClip clip, bool executor)
        {
            ActorKey e = clip.Keys[0].Executor;
            ActorKey o = clip.Keys[0].Opponent;
            bool executorAtOrigin = ((e.X * e.X) + (e.Z * e.Z)) <= ((o.X * o.X) + (o.Z * o.Z));
            return executor ? executorAtOrigin : !executorAtOrigin;
        }

        private static bool Held(ActorKey key) => key.AnchorWeight > 0f && key.Anchor != AnchorPoint.None;

        private static void GroundedLift(ActorKey key, string clip, int index, string actor, List<string> errors)
        {
            bool grounded = key.Ground == GroundState.Prone || key.Ground == GroundState.Supine || key.Ground == GroundState.Bridge
                || key.Ground == GroundState.BothKnees || key.Ground == GroundState.Seated;
            if (grounded && key.Lift > 0f)
            {
                errors.Add($"{clip}.keys[{index}].{actor}: lift {F(key.Lift)} m on a body that is {key.Ground} (only a throw lifts a grounded body).");
            }
        }

        private static void FreeFall(float fromHeight, float toHeight, float span, string clip, int from, int to, List<string> errors)
        {
            float drop = fromHeight - toHeight;
            if (drop < SmallestDrop)
            {
                return;
            }

            // From rest, falling freely covers the drop in sqrt(2h/g) at the soonest.
            float soonest = (float)Math.Sqrt(2f * drop / ClipKinematics.Gravity);
            if (span < soonest)
            {
                errors.Add($"{clip}.keys[{from}..{to}].executor: hips drop {F(drop)} m in {F(span)} s from rest at the phase's minimum length, faster than free fall ({F(soonest)} s).");
            }
        }

        /// <summary>How a throw's fulcrum is loaded at phase time <paramref name="t"/>.</summary>
        public enum FulcrumLoad
        {
            /// <summary>Outside the loading window, or a fulcrum the thrower faces (chest, lifts).</summary>
            None,

            /// <summary>The thrower's back turned in under the opponent: hips under theirs, trunk bent over.</summary>
            OverHips,

            /// <summary>Carried across the shoulders: the carrier's hips under the carried body's.</summary>
            Carried,
        }

        /// <summary>
        /// Whether the throw is loading its fulcrum at <paramref name="t"/>: through a load phase's
        /// window and the first half of a throw's turn, over the hips or lower back (or a hip or leg
        /// pivot with no anchor) or across the shoulders.
        /// </summary>
        public static FulcrumLoad Loading(ThrowSpec spec, float t, AnchorPoint seat, float seatWeight)
        {
            if (spec.Kind == ThrowKind.None)
            {
                return FulcrumLoad.None;
            }

            float end = spec.Kind == ThrowKind.Load ? spec.To : spec.From + ((spec.To - spec.From) * 0.5f);
            if (t < spec.From || t > end)
            {
                return FulcrumLoad.None;
            }

            AnchorPoint site = seatWeight > 0f ? seat : AnchorPoint.None;
            if (site == AnchorPoint.Hips || site == AnchorPoint.LowerBack || (site == AnchorPoint.None && HipPivots.Contains(spec.Pivot)))
            {
                return FulcrumLoad.OverHips;
            }

            return ClipKinematics.OnShoulders(site) ? FulcrumLoad.Carried : FulcrumLoad.None;
        }

        private static void Fulcrum(MoveClip clip, ClipKinematics.Trunk[] executor, ClipKinematics.Trunk[] opponent, List<string> errors)
        {
            for (int i = 0; i < clip.Keys.Count; i++)
            {
                ClipKey key = clip.Keys[i];
                FulcrumLoad load = Loading(clip.Throw, key.T, key.Opponent.Anchor, key.Opponent.AnchorWeight);
                if (load == FulcrumLoad.None)
                {
                    continue;
                }

                float under = opponent[i].Pelvis.Y - executor[i].Pelvis.Y;
                if (under < FulcrumDrop)
                {
                    errors.Add($"{clip.Id}.keys[{i}]: the thrower's hips are {F(under)} m under the opponent's at the fulcrum; they must be at least {F(FulcrumDrop)} m under.");
                }

                if (load == FulcrumLoad.OverHips && key.Executor.Ground == GroundState.Standing)
                {
                    float lean = executor[i].LeanDegrees;
                    if (lean < MinimumLoadLean || lean > MaximumLoadLean)
                    {
                        errors.Add($"{clip.Id}.keys[{i}]: the thrower's trunk leans {F(lean)}° over the hip fulcrum; it must lean {F(MinimumLoadLean)}–{F(MaximumLoadLean)}°.");
                    }
                }
            }
        }

        private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
