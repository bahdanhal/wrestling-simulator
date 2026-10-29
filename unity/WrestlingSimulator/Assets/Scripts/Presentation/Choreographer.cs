using UnityEngine;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;
using Wrestling.Data.Animation;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Plays the paired move clip of the engine's active technique phase, timed by the engine's
    /// phase progress. The clip is placed relative to where the wrestlers were when the technique
    /// started and mirrored for left-side techniques. Display only: when the phase ends the engine's
    /// result (landing, scramble, failure) takes over.
    /// </summary>
    public sealed class Choreographer
    {
        private const float StandardStance = 0.25f;

        /// <summary>Ticks over which a new technique eases in from the pose on screen.</summary>
        private const float EntryBlendTicks = 12f;

        private readonly WrestlerFrame[] _entry = new WrestlerFrame[2];
        private readonly (AnchorPoint Point, float Weight, float Contact)[] _entryAnchors = new (AnchorPoint, float, float)[2];
        private readonly (AnchorPoint Point, float Weight, float Contact)[] _lastAnchors = new (AnchorPoint, float, float)[2];

        private readonly MoveAnimationLibrary _library;
        private TechniqueExecution _anchored;
        private Vector2 _origin;
        private Vector2 _forward;
        private bool _mirror;

        public Choreographer(MoveAnimationLibrary library)
        {
            _library = library;
        }

        /// <summary>The active phase's throw, if it has one: who throws, the spec, phase progress and phase length in seconds, and whether the clip is mirrored.</summary>
        public (TechniqueExecution Technique, Corner Thrower, ThrowSpec Spec, float Progress, float PhaseSeconds, bool Mirrored)? Throw { get; private set; }

        /// <summary>The active clip's id, or null when no technique is running.</summary>
        public string CurrentClip { get; private set; }

        /// <summary>This frame's contact anchor for each corner (indexed by <see cref="Corner"/>): which point of the other wrestler holds it, how strongly, and at what thickness.</summary>
        public (AnchorPoint Point, float Weight, float Contact)[] Anchors { get; } = new (AnchorPoint, float, float)[2];

        /// <summary>
        /// Replaces both frames with the clip pose while a technique runs. A new technique (a counter
        /// interrupting a throw, say) starts from <paramref name="shown"/>, the frames on screen, and
        /// eases into its clip, including any contact hold, so the bodies never jump.
        /// </summary>
        public void Apply(BoutEngine engine, ref WrestlerFrame red, ref WrestlerFrame blue, float alpha, WrestlerFrame[] shown)
        {
            _lastAnchors[0] = Anchors[0];
            _lastAnchors[1] = Anchors[1];
            TechniqueExecution active = engine.ActiveTechnique;
            Anchors[0] = (AnchorPoint.None, 0f, 0f);
            Anchors[1] = (AnchorPoint.None, 0f, 0f);
            Throw = null;
            if (active == null || engine.IsStopped)
            {
                // Holds end with the technique; the next one eases in from the displayed pose.
                _anchored = null;
                CurrentClip = null;
                return;
            }

            MoveClip clip = _library.Get(active.Phase.Animation);
            CurrentClip = clip.Id;
            Corner executor = active.Executor;
            WrestlerFrame executorFrame = executor == Corner.Red ? red : blue;
            WrestlerFrame opponentFrame = executor == Corner.Red ? blue : red;
            if (!ReferenceEquals(_anchored, active))
            {
                Anchor(active, clip, engine, executorFrame, opponentFrame);
                _entry[0] = shown[0];
                _entry[1] = shown[1];
                _entryAnchors[0] = _lastAnchors[0];
                _entryAnchors[1] = _lastAnchors[1];
            }

            float progress = active.PhaseLength > 0 ? (active.PhaseTick + alpha) / active.PhaseLength : 1f;
            (ActorKey executorKey, ActorKey opponentKey) = clip.Sample(progress);
            if (clip.Throw.Kind == ThrowKind.Throw)
            {
                Throw = (active, executor, clip.Throw, progress, active.PhaseLength / (float)BoutClock.TicksPerSecond, _mirror);
            }
            Anchors[(int)executor] = (executorKey.Anchor, executorKey.AnchorWeight, executorKey.Contact);
            Anchors[(int)executor.Opponent()] = (opponentKey.Anchor, opponentKey.AnchorWeight, opponentKey.Contact);
            WrestlerFrame placedExecutor = Place(executorKey, executorFrame);
            WrestlerFrame placedOpponent = Place(opponentKey, opponentFrame);
            if (executor == Corner.Red)
            {
                red = placedExecutor;
                blue = placedOpponent;
            }
            else
            {
                blue = placedExecutor;
                red = placedOpponent;
            }

            float entry = Mathf.SmoothStep(0f, 1f, (engine.Tick - active.StartTick + alpha) / EntryBlendTicks);
            if (entry < 1f)
            {
                red = EaseIn(_entry[0], red, entry);
                blue = EaseIn(_entry[1], blue, entry);
                Anchors[0] = EaseIn(_entryAnchors[0], Anchors[0], entry);
                Anchors[1] = EaseIn(_entryAnchors[1], Anchors[1], entry);
            }
        }

        private static WrestlerFrame EaseIn(WrestlerFrame from, WrestlerFrame to, float t)
        {
            WrestlerFrame blended = WrestlerFrame.Lerp(from, to, t);
            // Whole-body angles can be far apart (a body mid-flip): take the short way round.
            blended.Pitch = from.Pitch + (Mathf.DeltaAngle(from.Pitch, to.Pitch) * t);
            blended.Bank = from.Bank + (Mathf.DeltaAngle(from.Bank, to.Bank) * t);
            return blended;
        }

        /// <summary>A hold fades from the old one to the new; a held point carries over while its weight fades out.</summary>
        private static (AnchorPoint Point, float Weight, float Contact) EaseIn(
            (AnchorPoint Point, float Weight, float Contact) from, (AnchorPoint Point, float Weight, float Contact) to, float t)
        {
            AnchorPoint point = to.Weight > 0f && (t >= 0.5f || from.Weight <= 0f) ? to.Point : from.Weight > 0f ? from.Point : to.Point;
            return (point, Mathf.Lerp(from.Weight, to.Weight, t), Mathf.Lerp(from.Contact, to.Contact, t));
        }

        private void Anchor(TechniqueExecution active, MoveClip clip, BoutEngine engine, WrestlerFrame executor, WrestlerFrame opponent)
        {
            _anchored = active;
            if (clip.Frame == ClipFrame.Bottom)
            {
                bool executorIsBottom = engine.Top != null && engine.Top.Value != active.Executor;
                WrestlerFrame bottom = executorIsBottom ? executor : opponent;
                WrestlerFrame top = executorIsBottom ? opponent : executor;
                _origin = bottom.Position;
                _forward = Normalised(bottom.Facing);
                // Clips put the top wrestler on the bottom wrestler's left; mirror when staged on the right.
                _mirror = Vector2.Dot(top.Position - bottom.Position, Left(_forward)) < 0f;
            }
            else
            {
                _origin = executor.Position;
                _forward = Normalised(opponent.Position - executor.Position, executor.Facing);
                _mirror = active.Side == GripSide.Left;
            }
        }

        private WrestlerFrame Place(ActorKey key, WrestlerFrame current)
        {
            float sign = _mirror ? -1f : 1f;
            Vector2 left = Left(_forward);
            Vector2 position = _origin + (_forward * key.X) + (left * (key.Z * sign));
            float heading = key.Heading * sign * Mathf.Deg2Rad;
            Vector2 facing = new Vector2(
                (Mathf.Cos(heading) * _forward.x) - (Mathf.Sin(heading) * _forward.y),
                (Mathf.Sin(heading) * _forward.x) + (Mathf.Cos(heading) * _forward.y));
            Vector2 side = Left(facing) * StandardStance;
            return new WrestlerFrame
            {
                Position = position,
                Facing = facing,
                LeftFoot = position + side,
                RightFoot = position - side,
                Level = key.Level,
                Posture = key.Posture,
                Balance = current.Balance,
                TorsoRotation = key.Roll,
                Ground = key.Ground,
                Stance = Stance.Square,
                RollSide = key.RollSide * sign,
                Lift = key.Lift,
                Pitch = key.Pitch,
                Bank = key.Bank * sign,
            };
        }

        private static Vector2 Left(Vector2 forward) => new Vector2(-forward.y, forward.x);

        private static Vector2 Normalised(Vector2 v, Vector2 fallback = default)
        {
            if (v.sqrMagnitude > 1e-6f)
            {
                return v.normalized;
            }

            return fallback.sqrMagnitude > 1e-6f ? fallback.normalized : Vector2.right;
        }
    }
}
