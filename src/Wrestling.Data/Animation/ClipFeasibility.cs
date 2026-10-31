using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// </list>
    /// </summary>
    public static class ClipFeasibility
    {
        /// <summary>How far (m) a thrower's hips must be under the opponent's at a hip or shoulder fulcrum.</summary>
        public const float FulcrumDrop = 0.14f;

        /// <summary>Trunk lean (degrees from vertical) of a thrower loading over the hip.</summary>
        public const float MinimumLoadLean = 35f;

        public const float MaximumLoadLean = 55f;

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

            return site == AnchorPoint.Shoulders ? FulcrumLoad.Carried : FulcrumLoad.None;
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
