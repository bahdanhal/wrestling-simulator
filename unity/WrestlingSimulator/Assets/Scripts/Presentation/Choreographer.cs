using UnityEngine;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;
using Wrestling.Data.Animation;
using Wrestling.Data.Motion;

namespace Wrestling.Presentation
{
    /// <summary>
    /// Plays the paired move clip of the engine's active technique phase, timed by the engine's
    /// phase progress. The clip is placed relative to where the wrestlers were when the technique
    /// started and mirrored for left-side techniques. Display only: when the phase ends the engine's
    /// result (landing, scramble, failure) takes over.
    ///
    /// Where one clip hands over to the next (a new technique, or the next phase of one) the switch
    /// is inertialized: the gap between the figure on screen and the new clip, and the gap in their
    /// velocities, decay together as a critically damped spring, so the motion keeps its speed
    /// through the switch and settles onto the clip at the same rate at any frame rate. The hips
    /// lead the clip's turns: the feet are set where the clip's heading will be a moment later.
    /// </summary>
    public sealed class Choreographer
    {
        private const float StandardStance = 0.25f;

        /// <summary>Ticks over which a new technique's contact holds, and recorded motion, ease in from what was on screen.</summary>
        private const float EntryBlendTicks = 12f;

        /// <summary>Rate (1/s) at which a switch's leftover offset dies away; settled in about 4 / rate.</summary>
        private const float SettleRate = 14f;

        /// <summary>Offsets larger than this (m) are a restaging, not a switch, and are not carried over.</summary>
        private const float MaxCarriedOffset = 1.5f;

        /// <summary>Longest frame gap (s) a velocity is measured over; longer gaps carry no velocity.</summary>
        private const float MaxVelocityGap = 0.25f;

        /// <summary>How far ahead (s) the hips take the clip's heading, and the most they lead it (degrees).</summary>
        private const float HipLeadSeconds = 0.12f;
        private const float MaxHipLead = 50f;

        private readonly FrameOffset[] _offset = new FrameOffset[2];
        private readonly FrameOffset[] _offsetVelocity = new FrameOffset[2];
        private readonly WrestlerFrame[] _previousShown = new WrestlerFrame[2];
        private float _previousTime = -1f;
        private float _switchTime;
        private TechniqueExecution _phaseTechnique;
        private int _phaseIndex = -1;
        private readonly (AnchorPoint Point, float Weight, float Contact)[] _entryAnchors = new (AnchorPoint, float, float)[2];
        private readonly (AnchorPoint Point, float Weight, float Contact)[] _lastAnchors = new (AnchorPoint, float, float)[2];

        private readonly MoveAnimationLibrary _library;
        private readonly MotionLibrary _motion;
        private readonly MotionPlayback _playback = new MotionPlayback();
        private TechniqueExecution _motionTechnique;
        private int _motionPhase = -1;
        private TechniqueExecution _anchored;
        private Vector2 _origin;
        private Vector2 _forward;
        private bool _mirror;

        public Choreographer(MoveAnimationLibrary library, MotionLibrary motion)
        {
            _library = library;
            _motion = motion;
        }

        /// <summary>
        /// This frame's recorded poses (motion capture or video) when the active phase has them, by
        /// corner, with how far the entry has eased in (0 the pose on screen, 1 the recording).
        /// </summary>
        public (Pose Red, Pose Blue, float Entry)? Recorded { get; private set; }

        /// <summary>
        /// Looks up recorded motion for the active phase; on a new phase it aligns the take to the
        /// poses on screen. Call after <see cref="Apply"/>.
        /// </summary>
        public void ApplyRecorded(BoutEngine engine, float alpha, Pose[] shownPoses, float[] heights)
        {
            Recorded = null;
            TechniqueExecution active = engine.ActiveTechnique;
            if (active == null || engine.IsStopped || !_motion.TryGet(active.Phase.Animation, out MotionPhase phase, out MotionTake take))
            {
                _motionTechnique = null;
                _motionPhase = -1;
                return;
            }

            int executor = (int)active.Executor;
            if (!ReferenceEquals(_motionTechnique, active) || _motionPhase != active.PhaseIndex)
            {
                _playback.Begin(take, phase, in shownPoses[executor], in shownPoses[1 - executor], heights[executor], heights[1 - executor], active.Side == GripSide.Left);
                _motionTechnique = active;
                _motionPhase = active.PhaseIndex;
                _recordedStartTick = engine.Tick;
            }

            float progress = active.PhaseLength > 0 ? (active.PhaseTick + alpha) / active.PhaseLength : 1f;
            (Pose executorPose, Pose opponentPose) = _playback.Sample(progress);
            float entry = Mathf.SmoothStep(0f, 1f, (engine.Tick - _recordedStartTick + alpha) / EntryBlendTicks);
            Recorded = active.Executor == Corner.Red ? (executorPose, opponentPose, entry) : (opponentPose, executorPose, entry);
        }

        private int _recordedStartTick;

        /// <summary>The active phase's throw, if it has one: who throws, the spec, phase progress and phase length in seconds, and whether the clip is mirrored.</summary>
        public (TechniqueExecution Technique, Corner Thrower, ThrowSpec Spec, float Progress, float PhaseSeconds, bool Mirrored)? Throw { get; private set; }

        /// <summary>The active clip's id, or null when no technique is running.</summary>
        public string CurrentClip { get; private set; }

        /// <summary>This frame's contact anchor for each corner (indexed by <see cref="Corner"/>): which point of the other wrestler holds it, how strongly, and at what thickness.</summary>
        public (AnchorPoint Point, float Weight, float Contact)[] Anchors { get; } = new (AnchorPoint, float, float)[2];

        /// <summary>
        /// Replaces both frames with the clip pose while a technique runs. At a switch to a new clip
        /// (a counter interrupting a throw, or the next phase) the frames on screen,
        /// <paramref name="shown"/>, carry over as a decaying offset (see the class notes), and contact
        /// holds ease in from the ones before, so the bodies never jump.
        /// </summary>
        public void Apply(BoutEngine engine, ref WrestlerFrame red, ref WrestlerFrame blue, float alpha, WrestlerFrame[] shown)
        {
            _lastAnchors[0] = Anchors[0];
            _lastAnchors[1] = Anchors[1];
            float now = (engine.Tick + alpha) / BoutClock.TicksPerSecond;
            float gap = now - _previousTime;
            bool timed = _previousTime >= 0f && gap > 1e-4f && gap < MaxVelocityGap;
            FrameOffset shownVelocityRed = timed ? FrameOffset.Between(_previousShown[0], shown[0]) * (1f / gap) : default;
            FrameOffset shownVelocityBlue = timed ? FrameOffset.Between(_previousShown[1], shown[1]) * (1f / gap) : default;
            if (now < _previousTime)
            {
                // A new bout (or time rewound): nothing carries over.
                _phaseTechnique = null;
                _phaseIndex = -1;
            }

            _previousShown[0] = shown[0];
            _previousShown[1] = shown[1];
            _previousTime = now;

            TechniqueExecution active = engine.ActiveTechnique;
            Anchors[0] = (AnchorPoint.None, 0f, 0f);
            Anchors[1] = (AnchorPoint.None, 0f, 0f);
            Throw = null;
            if (active == null || engine.IsStopped)
            {
                // Holds end with the technique; the next one eases in from the displayed pose.
                _anchored = null;
                _phaseTechnique = null;
                _phaseIndex = -1;
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
                _entryAnchors[0] = _lastAnchors[0];
                _entryAnchors[1] = _lastAnchors[1];
            }

            float progress = active.PhaseLength > 0 ? (active.PhaseTick + alpha) / active.PhaseLength : 1f;
            float phaseSeconds = Mathf.Max(active.PhaseLength, 1) / (float)BoutClock.TicksPerSecond;
            (ActorKey executorKey, ActorKey opponentKey) = clip.Sample(progress);
            if (clip.Throw.Kind != ThrowKind.None)
            {
                Throw = (active, executor, clip.Throw, progress, active.PhaseLength / (float)BoutClock.TicksPerSecond, _mirror);
            }

            Anchors[(int)executor] = (executorKey.Anchor, executorKey.AnchorWeight, executorKey.Contact);
            Anchors[(int)executor.Opponent()] = (opponentKey.Anchor, opponentKey.AnchorWeight, opponentKey.Contact);
            (WrestlerFrame placedExecutor, WrestlerFrame placedOpponent) = Placed(clip, progress, phaseSeconds, executorFrame, opponentFrame);
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

            if (!ReferenceEquals(_phaseTechnique, active) || _phaseIndex != active.PhaseIndex)
            {
                // A switch: carry the gap to the frames on screen, and the gap in velocity, from here.
                const float step = 0.02f;
                float ahead = Mathf.Min(1f, progress + step);
                (WrestlerFrame nextExecutor, WrestlerFrame nextOpponent) = Placed(clip, ahead, phaseSeconds, executorFrame, opponentFrame);
                float perSecond = ahead > progress ? 1f / ((ahead - progress) * phaseSeconds) : 0f;
                WrestlerFrame nextRed = executor == Corner.Red ? nextExecutor : nextOpponent;
                WrestlerFrame nextBlue = executor == Corner.Red ? nextOpponent : nextExecutor;
                Carry(0, shown[0], red, nextRed, perSecond, shownVelocityRed);
                Carry(1, shown[1], blue, nextBlue, perSecond, shownVelocityBlue);
                _switchTime = now;
                _phaseTechnique = active;
                _phaseIndex = active.PhaseIndex;
            }

            // Critically damped decay of the carried offset x0 and velocity v0: (x0 + (v0 + k x0) t) e^(-k t).
            float t = Mathf.Max(0f, now - _switchTime);
            float decay = Mathf.Exp(-SettleRate * t);
            float carryOffset = (1f + (SettleRate * t)) * decay;
            float carryVelocity = t * decay;
            red = ((_offset[0] * carryOffset) + (_offsetVelocity[0] * carryVelocity)).AddTo(red);
            blue = ((_offset[1] * carryOffset) + (_offsetVelocity[1] * carryVelocity)).AddTo(blue);

            float entry = Mathf.SmoothStep(0f, 1f, (engine.Tick - active.StartTick + alpha) / EntryBlendTicks);
            if (entry < 1f)
            {
                Anchors[0] = EaseIn(_entryAnchors[0], Anchors[0], entry);
                Anchors[1] = EaseIn(_entryAnchors[1], Anchors[1], entry);
            }
        }

        /// <summary>Records one corner's switch: the offset from the new clip to the frame on screen, and their velocity gap.</summary>
        private void Carry(int corner, WrestlerFrame shown, WrestlerFrame target, WrestlerFrame nextTarget, float perSecond, FrameOffset shownVelocity)
        {
            FrameOffset offset = FrameOffset.Between(target, shown);
            if (shown.Facing.sqrMagnitude < 1e-6f || offset.Position.magnitude > MaxCarriedOffset)
            {
                _offset[corner] = default;
                _offsetVelocity[corner] = default;
                return;
            }

            FrameOffset targetVelocity = FrameOffset.Between(target, nextTarget) * perSecond;
            _offset[corner] = offset;
            _offsetVelocity[corner] = (shownVelocity + (targetVelocity * -1f)).Limited();
        }

        /// <summary>Both wrestlers placed from the clip at <paramref name="progress"/>, the feet set by the heading a moment ahead.</summary>
        private (WrestlerFrame Executor, WrestlerFrame Opponent) Placed(MoveClip clip, float progress, float phaseSeconds, WrestlerFrame executor, WrestlerFrame opponent)
        {
            (ActorKey executorKey, ActorKey opponentKey) = clip.Sample(progress);
            (ActorKey executorLead, ActorKey opponentLead) = clip.Sample(Mathf.Min(1f, progress + (HipLeadSeconds / phaseSeconds)));
            return (Place(executorKey, executorLead, executor), Place(opponentKey, opponentLead, opponent));
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

        private WrestlerFrame Place(ActorKey key, ActorKey lead, WrestlerFrame current)
        {
            float sign = _mirror ? -1f : 1f;
            Vector2 left = Left(_forward);
            Vector2 position = _origin + (_forward * key.X) + (left * (key.Z * sign));
            Vector2 facing = Rotate(_forward, key.Heading * sign);
            // The feet square to where the hips are turning: the clip's heading a moment ahead.
            float hipLead = Mathf.Clamp(Mathf.DeltaAngle(key.Heading, lead.Heading), -MaxHipLead, MaxHipLead) * sign;
            Vector2 side = Left(Rotate(facing, hipLead)) * StandardStance;
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

        /// <summary>Turns a mat direction counter-clockwise by <paramref name="degrees"/>.</summary>
        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2((cos * v.x) - (sin * v.y), (sin * v.x) + (cos * v.y));
        }

        /// <summary>The difference between two frames' continuous values (or its rate of change), applied by adding it to a frame.</summary>
        private struct FrameOffset
        {
            public Vector2 Position;
            public Vector2 LeftFoot;
            public Vector2 RightFoot;
            public float Facing;
            public float Level;
            public float Posture;
            public float TorsoRotation;
            public float Lift;
            public float Pitch;
            public float Bank;

            /// <summary>What takes <paramref name="from"/> to <paramref name="to"/> (angles the short way round).</summary>
            public static FrameOffset Between(WrestlerFrame from, WrestlerFrame to) => new FrameOffset
            {
                Position = to.Position - from.Position,
                LeftFoot = to.LeftFoot - from.LeftFoot,
                RightFoot = to.RightFoot - from.RightFoot,
                Facing = from.Facing.sqrMagnitude > 1e-6f && to.Facing.sqrMagnitude > 1e-6f ? Vector2.SignedAngle(from.Facing, to.Facing) : 0f,
                Level = to.Level - from.Level,
                Posture = to.Posture - from.Posture,
                TorsoRotation = to.TorsoRotation - from.TorsoRotation,
                Lift = to.Lift - from.Lift,
                Pitch = Mathf.DeltaAngle(from.Pitch, to.Pitch),
                Bank = Mathf.DeltaAngle(from.Bank, to.Bank),
            };

            public static FrameOffset operator *(FrameOffset a, float k) => new FrameOffset
            {
                Position = a.Position * k,
                LeftFoot = a.LeftFoot * k,
                RightFoot = a.RightFoot * k,
                Facing = a.Facing * k,
                Level = a.Level * k,
                Posture = a.Posture * k,
                TorsoRotation = a.TorsoRotation * k,
                Lift = a.Lift * k,
                Pitch = a.Pitch * k,
                Bank = a.Bank * k,
            };

            public static FrameOffset operator +(FrameOffset a, FrameOffset b) => new FrameOffset
            {
                Position = a.Position + b.Position,
                LeftFoot = a.LeftFoot + b.LeftFoot,
                RightFoot = a.RightFoot + b.RightFoot,
                Facing = a.Facing + b.Facing,
                Level = a.Level + b.Level,
                Posture = a.Posture + b.Posture,
                TorsoRotation = a.TorsoRotation + b.TorsoRotation,
                Lift = a.Lift + b.Lift,
                Pitch = a.Pitch + b.Pitch,
                Bank = a.Bank + b.Bank,
            };

            /// <summary>A velocity kept within what a body moves at, so one bad sample cannot fling it.</summary>
            public FrameOffset Limited() => new FrameOffset
            {
                Position = Vector2.ClampMagnitude(Position, 6f),
                LeftFoot = Vector2.ClampMagnitude(LeftFoot, 8f),
                RightFoot = Vector2.ClampMagnitude(RightFoot, 8f),
                Facing = Mathf.Clamp(Facing, -720f, 720f),
                Level = Mathf.Clamp(Level, -6f, 6f),
                Posture = Mathf.Clamp(Posture, -6f, 6f),
                TorsoRotation = Mathf.Clamp(TorsoRotation, -720f, 720f),
                Lift = Mathf.Clamp(Lift, -6f, 6f),
                Pitch = Mathf.Clamp(Pitch, -900f, 900f),
                Bank = Mathf.Clamp(Bank, -900f, 900f),
            };

            public WrestlerFrame AddTo(WrestlerFrame frame)
            {
                frame.Position += Position;
                frame.LeftFoot += LeftFoot;
                frame.RightFoot += RightFoot;
                frame.Facing = Rotate(frame.Facing, Facing);
                frame.Level = Mathf.Clamp01(frame.Level + Level);
                frame.Posture = Mathf.Clamp01(frame.Posture + Posture);
                frame.TorsoRotation += TorsoRotation;
                frame.Lift = Mathf.Max(0f, frame.Lift + Lift);
                frame.Pitch += Pitch;
                frame.Bank += Bank;
                return frame;
            }
        }

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
