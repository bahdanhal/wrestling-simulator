using System;
using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Bouts;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Core.Match
{
    /// <summary>
    /// The bout simulation (design section 5): a deterministic, fixed-rate tick loop. Each tick
    /// it collects intents, resolves hand-fighting and pressure, advances techniques and
    /// positions, applies the officiating rules and updates fatigue. Play, Coach and Sim modes
    /// all run this same engine; only the controllers differ.
    /// </summary>
    public sealed partial class BoutEngine : IBoutView
    {
        private const int MaximumTicks = 500_000;
        private static readonly Corner[] Corners = { Corner.Red, Corner.Blue };

        private readonly BoutSetup _setup;
        private readonly Pcg32 _random;
        private readonly WrestlerRuntime[] _wrestlers;
        private readonly List<BoutEvent> _events = new List<BoutEvent>();
        private readonly ActivityClockProcedure? _activityClock;
        private readonly Fix64 _activityDecayPerTick;
        private readonly int _fallTicks;

        private int _stoppageTicks;
        private RestartPosition _pendingRestart;
        private Corner _pendingTop;
        private int _scrambleTicks;
        private Corner _scrambleOrigin;
        private bool _scrambleFromFailure;
        private int _parTerreTicks;
        private int _dangerTicks;
        private int _shoulderTicks;
        private int _lastGrecoCheckElapsed;
        private readonly int[] _lastInactivityReport = new int[2];
        private readonly bool[] _zoneCalled = new bool[2];

        public BoutEngine(BoutSetup setup)
        {
            _setup = setup ?? throw new ArgumentNullException(nameof(setup));
            setup.Validate();
            _random = new Pcg32(setup.Seed, 0x6B6F7574UL);
            _wrestlers = new[]
            {
                new WrestlerRuntime(Corner.Red, setup.Red, setup.Tuning),
                new WrestlerRuntime(Corner.Blue, setup.Blue, setup.Tuning),
            };
            Clock = new BoutClock(setup.Rules.BoutDuration.For(setup.Age));
            Sheet = new BoutScoreSheet();
            Grips = new GripSet(setup.Style);
            if (setup.Style.UsesFreestyleRules())
            {
                _activityClock = new ActivityClockProcedure(setup.Rules, setup.Age);
            }

            // Exponential decay with the configured half-life: factor = 1 - ln 2 / (half-life in ticks).
            Fix64 halfLifeTicks = setup.Tuning.ActivityHalfLifeSeconds * Fix64.FromInt(BoutClock.TicksPerSecond);
            _activityDecayPerTick = Fix64.One - (Fix64.FromRatio(693147, 1_000_000) / halfLifeTicks);
            _fallTicks = Math.Max(1, (setup.Rules.Procedure.FallConfirmationSeconds * Fix64.FromInt(BoutClock.TicksPerSecond)).FloorToInt());
            for (int i = 0; i < 2; i++)
            {
                _lastInactivityReport[i] = int.MinValue / 2;
            }

            RestartStanding();
        }

        public int Tick { get; private set; }

        public Ruleset Rules => _setup.Rules;

        public EngineTuning Tuning => _setup.Tuning;

        public TechniqueCatalog Catalog => _setup.Catalog;

        public WrestlingStyle Style => _setup.Style;

        public AgeCategory Age => _setup.Age;

        public BoutClock Clock { get; }

        public BoutScoreSheet Sheet { get; }

        public GripSet Grips { get; }

        public BoutPosition Position { get; private set; }

        public Corner? Top { get; private set; }

        public TechniqueExecution? ActiveTechnique { get; private set; }

        public bool IsStopped => _stoppageTicks > 0 || Clock.Phase != ClockPhase.Period;

        public bool IsFinished => Result != null;

        public BoutResult? Result { get; private set; }

        public IReadOnlyList<BoutEvent> Events => _events;

        public WrestlerRuntime Wrestler(Corner corner) => _wrestlers[(int)corner];

        /// <summary>Runs ticks until the bout ends and returns the result.</summary>
        public BoutResult RunToEnd()
        {
            while (!IsFinished)
            {
                if (Tick >= MaximumTicks)
                {
                    throw new InvalidOperationException("The bout did not finish within the tick limit.");
                }

                Step();
            }

            return Result!;
        }

        /// <summary>Advances the bout by one tick.</summary>
        public void Step()
        {
            if (IsFinished)
            {
                return;
            }

            Tick++;
            if (_stoppageTicks > 0)
            {
                _stoppageTicks--;
                if (_stoppageTicks == 0)
                {
                    ApplyRestart();
                }

                return;
            }

            switch (Clock.Phase)
            {
                case ClockPhase.NotStarted:
                    Clock.Start();
                    Emit(BoutEventType.PeriodStarted, null, null, 0, "1");
                    break;
                case ClockPhase.Break:
                    Clock.Tick();
                    if (Clock.BreakTicksRemaining == 0)
                    {
                        RestartStanding();
                        Clock.Start();
                        Emit(BoutEventType.PeriodStarted, null, null, 0, Clock.Period.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }

                    return;
            }

            bool periodEnded = Clock.Tick();

            Decide();
            switch (Position)
            {
                case BoutPosition.Neutral:
                case BoutPosition.ParTerre:
                    if (ActiveTechnique == null)
                    {
                        StartRequestedTechniques();
                    }

                    if (ActiveTechnique == null)
                    {
                        HandFight();
                        if (Position == BoutPosition.Neutral)
                        {
                            Move();
                        }
                    }

                    break;
                case BoutPosition.Danger:
                    HoldDanger();
                    break;
                case BoutPosition.Scramble:
                    AdvanceScramble();
                    break;
            }

            if (!IsFinished && ActiveTechnique != null)
            {
                AdvanceTechnique();
            }

            UpdateEnergyAndActivity();
            if (!IsFinished && _stoppageTicks == 0)
            {
                Officiate();
            }

            if (periodEnded && !IsFinished)
            {
                EndPeriod();
            }
        }

        private void Decide()
        {
            foreach (WrestlerRuntime wrestler in _wrestlers)
            {
                if (Tick < wrestler.NextDecisionTick)
                {
                    continue;
                }

                wrestler.Intent = _setup.ControllerOf(wrestler.Corner).Decide(this, wrestler.Corner) ?? Intent.Idle;
                wrestler.TechniqueIntentUsed = false;
                wrestler.CounterIntentUsed = false;
                wrestler.NextDecisionTick = Tick + DecisionInterval(wrestler);
            }
        }

        private int DecisionInterval(WrestlerRuntime wrestler)
        {
            int iq = wrestler.Wrestler.Attributes[WrestlerAttribute.WrestlingIq];
            int range = Tuning.SlowestDecisionTicks - Tuning.FastestDecisionTicks;
            return Tuning.SlowestDecisionTicks - ((range * (iq - 1)) / 99);
        }

        /// <summary>Stops the bout; wrestling resumes after the stoppage time in the given position.</summary>
        private void Stop(RestartPosition restart, Corner top, string reason)
        {
            ActiveTechnique = null;
            if (Clock.Phase == ClockPhase.Period && Clock.IsRunning)
            {
                Clock.Stop();
            }

            _pendingRestart = restart == RestartPosition.Continue ? RestartPosition.StandingCentre : restart;
            _pendingTop = top;
            _stoppageTicks = Math.Max(1, (Tuning.StoppageSeconds * Fix64.FromInt(BoutClock.TicksPerSecond)).FloorToInt());
            Emit(BoutEventType.Stoppage, null, null, 0, reason);
        }

        private void ApplyRestart()
        {
            if (_pendingRestart == RestartPosition.ParTerre)
            {
                RestartParTerre(_pendingTop);
            }
            else
            {
                RestartStanding();
            }

            if (Clock.Phase == ClockPhase.Period && !Clock.IsRunning)
            {
                Clock.Start();
            }

            Emit(BoutEventType.Restart, _pendingRestart == RestartPosition.ParTerre ? _pendingTop : (Corner?)null, null, 0, _pendingRestart.ToString());
        }

        private void RestartStanding()
        {
            Grips.Clear();
            ActiveTechnique = null;
            Position = BoutPosition.Neutral;
            Top = null;
            Fix64 half = Tuning.ContactDistance / Fix64.FromInt(2);
            Fix64 level = Tuning.NeutralLevel.For(Style);
            SetBody(Wrestler(Corner.Red).Body, new FixVector2(-half, Fix64.Zero), FixVector2.UnitX, GroundState.Standing, level);
            SetBody(Wrestler(Corner.Blue).Body, new FixVector2(half, Fix64.Zero), -FixVector2.UnitX, GroundState.Standing, level);
        }

        /// <summary>Ordered par terre position (Art. 44): bottom flat in the centre, top kneeling beside.</summary>
        private void RestartParTerre(Corner top)
        {
            Grips.Clear();
            ActiveTechnique = null;
            Position = BoutPosition.ParTerre;
            Top = top;
            _parTerreTicks = 0;
            SetBody(Wrestler(top.Opponent()).Body, FixVector2.Zero, FixVector2.UnitX, GroundState.Prone, Fix64.FromRatio(2, 10));
            SetBody(Wrestler(top).Body, new FixVector2(Fix64.FromRatio(-3, 10), Fix64.Zero), FixVector2.UnitX, GroundState.BothKnees, Fix64.FromRatio(4, 10));
        }

        private static void SetBody(BodyState body, FixVector2 position, FixVector2 facing, GroundState ground, Fix64 level)
        {
            body.Position = position;
            body.Facing = facing;
            body.Momentum = FixVector2.Zero;
            body.Ground = ground;
            body.Level = level;
            body.Posture = Fix64.One;
            body.Balance = Fix64.One;
            body.TorsoRotationDegrees = Fix64.Zero;
            body.LeftShoulderOnMat = false;
            body.RightShoulderOnMat = false;
            body.Stance = Stance.Square;
        }

        /// <summary>The gong: an unfinished action is not valid (Art. 27, 34).</summary>
        private void EndPeriod()
        {
            ActiveTechnique = null;
            Emit(BoutEventType.PeriodEnded, null, null, 0, Clock.Period.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (Clock.Phase == ClockPhase.Finished)
            {
                Finish(BoutResult.AtEndOfRegularTime(Sheet, Rules));
                return;
            }

            // The break: both wrestlers recover part of their missing stamina.
            foreach (WrestlerRuntime wrestler in _wrestlers)
            {
                wrestler.Stamina += (wrestler.StaminaMax - wrestler.Stamina) * Tuning.BreakRecoveryFraction;
                wrestler.Burst = wrestler.BurstMax;
            }

            RestartStanding();
        }

        private void Finish(BoutResult result)
        {
            Result = result;
            ActiveTechnique = null;
            Emit(BoutEventType.BoutEnded, result.Winner, null, 0, result.Code);
        }

        private void Emit(BoutEventType type, Corner? wrestler, string? technique, int points, string detail) =>
            _events.Add(new BoutEvent(Tick, Clock.Now, type, wrestler, technique, points, detail));

        private int Ticks(Fix64 seconds) => Math.Max(1, (seconds * Fix64.FromInt(BoutClock.TicksPerSecond)).FloorToInt());

        private Fix64 PerTick(Fix64 perSecond) => FixMath.PerTick(perSecond, BoutClock.TicksPerSecond);

        /// <summary>Sigmoid contest: chance that a side with <paramref name="score"/> beats <paramref name="opposition"/>, scaled to <paramref name="rate"/> at even strength.</summary>
        private Fix64 Contest(Fix64 score, Fix64 opposition, Fix64 rate) =>
            Fix64.Min(Fix64.One, rate * Fix64.FromInt(2) * FixMath.Sigmoid((score - opposition) / Tuning.ResolutionScale));

        IReadOnlyList<(TechniqueDefinition Technique, GripSide Side)> IBoutView.StartableTechniques(Corner corner) => StartableTechniques(corner);

        IReadOnlyList<TechniqueDefinition> IBoutView.OpenCounters(Corner corner) => OpenCounters(corner);

        public IReadOnlyList<(TechniqueDefinition Technique, GripSide Side)> StartableTechniques(Corner corner)
        {
            var result = new List<(TechniqueDefinition, GripSide)>();
            if (IsStopped || ActiveTechnique != null || (Position != BoutPosition.Neutral && Position != BoutPosition.ParTerre))
            {
                return result;
            }

            foreach (RepertoireEntry entry in Wrestler(corner).Wrestler.Repertoire)
            {
                TechniqueDefinition technique = Catalog.Get(entry.TechniqueId);
                foreach (GripSide side in SidesOf(entry.Side))
                {
                    if (CanStart(corner, technique, side))
                    {
                        result.Add((technique, side));
                    }
                }
            }

            return result;
        }

        public IReadOnlyList<TechniqueDefinition> OpenCounters(Corner corner)
        {
            var result = new List<TechniqueDefinition>();
            TechniqueExecution? active = ActiveTechnique;
            if (active == null || active.Executor == corner)
            {
                return result;
            }

            foreach (CounterWindow window in active.Phase.CounterWindows)
            {
                if (active.PhaseTick >= window.FromTick && active.PhaseTick <= window.ToTick && CanCounter(corner, window.Technique))
                {
                    result.Add(Catalog.Get(window.Technique));
                }
            }

            return result;
        }

        private static IEnumerable<GripSide> SidesOf(TechniqueSide side) => side switch
        {
            TechniqueSide.Left => new[] { GripSide.Left },
            TechniqueSide.Right => new[] { GripSide.Right },
            _ => new[] { GripSide.Left, GripSide.Right },
        };
    }
}
