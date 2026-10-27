using System;
using Wrestling.Core.Bouts;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Match
{
    public enum ClockPhase
    {
        /// <summary>Before the first whistle.</summary>
        NotStarted,

        /// <summary>A period is in progress (the clock may be stopped for an interruption).</summary>
        Period,

        /// <summary>The break between periods.</summary>
        Break,

        /// <summary>Regular time is over.</summary>
        Finished,
    }

    /// <summary>
    /// The bout clock at the simulation's fixed tick rate. Wrestling time only advances while the
    /// clock runs; interruptions stop it. The break runs on its own countdown (Art. 22).
    /// </summary>
    public sealed class BoutClock
    {
        public const int TicksPerSecond = 60;

        private readonly int _periodTicks;
        private readonly int _breakTicks;

        public BoutClock(BoutDuration duration)
        {
            if (duration == null)
            {
                throw new ArgumentNullException(nameof(duration));
            }

            Periods = duration.Periods;
            _periodTicks = duration.PeriodSeconds * TicksPerSecond;
            _breakTicks = duration.BreakSeconds * TicksPerSecond;
        }

        public int Periods { get; }

        public ClockPhase Phase { get; private set; } = ClockPhase.NotStarted;

        /// <summary>Current period, 1-based (the last period once finished).</summary>
        public int Period { get; private set; } = 1;

        public int TickInPeriod { get; private set; }

        public int PeriodTicks => _periodTicks;

        /// <summary>True when wrestling time is advancing.</summary>
        public bool IsRunning { get; private set; }

        public int BreakTicksRemaining { get; private set; }

        public BoutTime Now => new BoutTime(Period, TickInPeriod);

        public int TicksRemainingInPeriod => _periodTicks - TickInPeriod;

        /// <summary>Wrestling ticks elapsed across all periods.</summary>
        public int ElapsedTicks => ((Period - 1) * _periodTicks) + TickInPeriod;

        /// <summary>Scoreboard time counting down from the full bout (6:00 to 0:00 for seniors), in whole seconds rounded up.</summary>
        public int ScoreboardSecondsRemaining => CeilingSeconds((Periods * _periodTicks) - ElapsedTicks);

        public static int SecondsToTicks(int seconds) => seconds * TicksPerSecond;

        /// <summary>The referee's whistle: starts the first period, the next period after the break, or resumes after an interruption.</summary>
        public void Start()
        {
            switch (Phase)
            {
                case ClockPhase.NotStarted:
                    Phase = ClockPhase.Period;
                    break;
                case ClockPhase.Break:
                    if (BreakTicksRemaining > 0)
                    {
                        throw new InvalidOperationException("The break is not over.");
                    }

                    Phase = ClockPhase.Period;
                    Period++;
                    TickInPeriod = 0;
                    break;
                case ClockPhase.Finished:
                    throw new InvalidOperationException("The bout is over.");
            }

            IsRunning = true;
        }

        /// <summary>Stops wrestling time for an interruption.</summary>
        public void Stop()
        {
            if (Phase != ClockPhase.Period)
            {
                throw new InvalidOperationException("The clock only stops during a period.");
            }

            IsRunning = false;
        }

        /// <summary>Advances one simulation tick. Returns true when this tick ended a period.</summary>
        public bool Tick()
        {
            if (Phase == ClockPhase.Break)
            {
                if (BreakTicksRemaining > 0)
                {
                    BreakTicksRemaining--;
                }

                return false;
            }

            if (Phase != ClockPhase.Period || !IsRunning)
            {
                return false;
            }

            TickInPeriod++;
            if (TickInPeriod < _periodTicks)
            {
                return false;
            }

            IsRunning = false;
            if (Period < Periods)
            {
                Phase = ClockPhase.Break;
                BreakTicksRemaining = _breakTicks;
            }
            else
            {
                Phase = ClockPhase.Finished;
            }

            return true;
        }

        private static int CeilingSeconds(int ticks) => (ticks + TicksPerSecond - 1) / TicksPerSecond;
    }
}
