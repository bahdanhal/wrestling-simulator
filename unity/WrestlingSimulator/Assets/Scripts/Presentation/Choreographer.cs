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

        private readonly MoveAnimationLibrary _library;
        private TechniqueExecution _anchored;
        private Vector2 _origin;
        private Vector2 _forward;
        private bool _mirror;

        public Choreographer(MoveAnimationLibrary library)
        {
            _library = library;
        }

        /// <summary>The active clip's id, or null when no technique is running.</summary>
        public string CurrentClip { get; private set; }

        /// <summary>This frame's contact anchor for each corner (indexed by <see cref="Corner"/>): which point of the other wrestler holds it, how strongly, and at what thickness.</summary>
        public (AnchorPoint Point, float Weight, float Contact)[] Anchors { get; } = new (AnchorPoint, float, float)[2];

        /// <summary>Replaces both frames with the clip pose while a technique runs.</summary>
        public void Apply(BoutEngine engine, ref WrestlerFrame red, ref WrestlerFrame blue, float alpha)
        {
            TechniqueExecution active = engine.ActiveTechnique;
            Anchors[0] = (AnchorPoint.None, 0f, 0f);
            Anchors[1] = (AnchorPoint.None, 0f, 0f);
            if (active == null || engine.IsStopped)
            {
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
            }

            float progress = active.PhaseLength > 0 ? (active.PhaseTick + alpha) / active.PhaseLength : 1f;
            (ActorKey executorKey, ActorKey opponentKey) = clip.Sample(progress);
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
