using System;
using Wrestling.Core.Bouts;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Match
{
    public enum PassivityDecision
    {
        /// <summary>Nothing happens (for example the wrestler is already on the activity clock).</summary>
        None,

        /// <summary>First offence: brief stop and "Attention" (V).</summary>
        VerbalWarning,

        /// <summary>The passive wrestler is designated and the activity period starts.</summary>
        ActivityPeriodStarted,
    }

    /// <summary>
    /// Freestyle and Women's inactivity procedure (Art. 46): a verbal warning, then a 30-second
    /// activity period in which the passive wrestler must score or the opponent gets a point.
    /// A scoreless bout forces a designation partway through the first period. Periods never
    /// start or end in the middle of a viable action.
    /// Interpretation: an activity period still running when the period ends lapses without a point.
    /// </summary>
    public sealed class ActivityClockProcedure
    {
        private readonly ActivityClockRules _rules;
        private readonly bool _verbalWarningFirst;
        private readonly int _scorelessDesignationTicks;
        private readonly bool[] _warned = new bool[2];
        private int _passivePointsAtStart;
        private int _periodStartTick;
        private int _periodNumber;
        private bool _pendingStart;
        private Corner _pendingPassive;

        public ActivityClockProcedure(Ruleset rules, AgeCategory age)
        {
            _rules = (rules ?? throw new ArgumentNullException(nameof(rules))).Passivity.ActivityClock;
            _verbalWarningFirst = _rules.VerbalWarningFirst.For(age);
            _scorelessDesignationTicks = BoutClock.SecondsToTicks(_rules.ScorelessDesignationAtSeconds.For(age));
        }

        /// <summary>The wrestler currently on the activity clock, if any.</summary>
        public Corner? Passive { get; private set; }

        public bool ScorelessDesignationDone { get; private set; }

        /// <summary>Wrestling ticks left in the running activity period, or 0.</summary>
        public int TicksRemaining(BoutClock clock) =>
            Passive.HasValue ? Math.Max(0, BoutClock.SecondsToTicks(_rules.ActivitySeconds) - (clock.ElapsedTicks - _periodStartTick)) : 0;

        /// <summary>The officials agree a wrestler is blocking, interlocking fingers or avoiding wrestling.</summary>
        public PassivityDecision ReportInactivity(Corner wrestler, BoutScoreSheet sheet, BoutClock clock, bool actionInProgress)
        {
            if (Passive.HasValue || _pendingStart)
            {
                return PassivityDecision.None;
            }

            if (_verbalWarningFirst && !_warned[(int)wrestler])
            {
                _warned[(int)wrestler] = true;
                sheet.Warn(wrestler, WarningReason.Passivity, clock.Now);
                return PassivityDecision.VerbalWarning;
            }

            return Designate(wrestler, sheet, clock, actionInProgress);
        }

        /// <summary>
        /// Called every tick. Starts a deferred designation once the action is over, forces the
        /// scoreless designation, and settles an expired activity period. Returns the point awarded, if any.
        /// </summary>
        /// <param name="lessActive">The wrestler the officials would designate if the bout is still scoreless.</param>
        public PointsEntry? Update(BoutScoreSheet sheet, BoutClock clock, bool actionInProgress, Corner lessActive)
        {
            if (_pendingStart && !actionInProgress)
            {
                _pendingStart = false;
                Start(_pendingPassive, sheet, clock);
            }

            if (!ScorelessDesignationDone && clock.Period == 1 && clock.TickInPeriod >= _scorelessDesignationTicks
                && sheet.Points(Corner.Red) == 0 && sheet.Points(Corner.Blue) == 0 && !Passive.HasValue && !_pendingStart)
            {
                ScorelessDesignationDone = true;
                Designate(lessActive, sheet, clock, actionInProgress);
            }

            if (!Passive.HasValue)
            {
                return null;
            }

            Corner passive = Passive.Value;
            if (clock.Period != _periodNumber || clock.Phase != ClockPhase.Period)
            {
                // The period ended while the activity period ran: it lapses.
                Passive = null;
                return null;
            }

            if (sheet.Points(passive) > _passivePointsAtStart)
            {
                // The passive wrestler scored: the activity period is satisfied.
                Passive = null;
                return null;
            }

            if (TicksRemaining(clock) > 0 || actionInProgress)
            {
                return null;
            }

            Passive = null;
            return sheet.AwardPoints(passive.Opponent(), _rules.PointIfNoScore, PointSource.ActivityClock, clock.Now);
        }

        /// <summary>
        /// Evading or blocking in the final seconds of a period, agreed by all three officials:
        /// caution and a point to the opponent. Returns false outside the window.
        /// </summary>
        public bool PenaliseFinalSecondsEvasion(Corner wrestler, BoutScoreSheet sheet, BoutClock clock)
        {
            if (clock.Phase != ClockPhase.Period || clock.TicksRemainingInPeriod > BoutClock.SecondsToTicks(_rules.FinalSecondsEvasionWindow))
            {
                return false;
            }

            sheet.GiveCaution(wrestler, CautionReason.FinalSecondsEvasion, clock.Now);
            sheet.AwardPoints(wrestler.Opponent(), _rules.FinalSecondsEvasionPoints, PointSource.Infringement, clock.Now);
            return true;
        }

        private PassivityDecision Designate(Corner wrestler, BoutScoreSheet sheet, BoutClock clock, bool actionInProgress)
        {
            if (actionInProgress)
            {
                _pendingStart = true;
                _pendingPassive = wrestler;
                return PassivityDecision.ActivityPeriodStarted;
            }

            Start(wrestler, sheet, clock);
            return PassivityDecision.ActivityPeriodStarted;
        }

        private void Start(Corner wrestler, BoutScoreSheet sheet, BoutClock clock)
        {
            sheet.CallPassivity(wrestler, clock.Now);
            Passive = wrestler;
            _passivePointsAtStart = sheet.Points(wrestler);
            _periodStartTick = clock.ElapsedTicks;
            _periodNumber = clock.Period;
        }
    }

    /// <summary>Result of a Greco-Roman passivity call.</summary>
    public sealed class GrecoPassivityCall
    {
        public GrecoPassivityCall(PassivityEntry entry, PointsEntry? point, bool activeChoosesPosition)
        {
            Entry = entry;
            Point = point;
            ActiveChoosesPosition = activeChoosesPosition;
        }

        public PassivityEntry Entry { get; }

        /// <summary>The point to the active wrestler; null from the third call on.</summary>
        public PointsEntry? Point { get; }

        /// <summary>The active wrestler now chooses to continue standing or in par terre.</summary>
        public bool ActiveChoosesPosition { get; }
    }

    /// <summary>
    /// Greco-Roman passivity (Art. 46): the bout stops; on the first and second call of the bout
    /// (whichever wrestler) the active wrestler gets a point; the active wrestler chooses standing
    /// or par terre on every call. Calls need the mat chairman's confirmation, given by the caller.
    /// </summary>
    public static class GrecoPassivityProcedure
    {
        public static GrecoPassivityCall Call(Corner passive, BoutScoreSheet sheet, BoutTime time, Ruleset rules)
        {
            GrecoRomanPassivityRules passivity = rules.Passivity.GrecoRoman;
            PassivityEntry entry = sheet.CallPassivity(passive, time);
            int points = passivity.PointsForCall(entry.CallNumber);
            PointsEntry? point = points > 0 ? sheet.AwardPoints(passive.Opponent(), points, PointSource.Passivity, time) : null;
            return new GrecoPassivityCall(entry, point, passivity.ActiveWrestlerChoosesPosition);
        }
    }

    public enum ChallengeRefusal
    {
        None,
        NoChallengeLeft,
        OutsideWindow,
        PassivityCannotBeChallenged,
        FallCannotBeChallenged,
        BoutDecided,
    }

    /// <summary>
    /// Video challenge (Art. 31): one per wrestler; lost challenges give the opponent a point and
    /// are used up, successful ones are kept. Requests must come within the window after the call
    /// or the return to neutral; passivity and falls (unless the fall followed an illegal action)
    /// cannot be challenged. When the video cannot be reviewed, the original call stands and the
    /// challenge is kept: the caller simply does not call <see cref="Resolve"/>.
    /// </summary>
    public sealed class ChallengeProcedure
    {
        private readonly ChallengeRules _rules;
        private readonly int[] _remaining = new int[2];

        public ChallengeProcedure(Ruleset rules)
        {
            _rules = (rules ?? throw new ArgumentNullException(nameof(rules))).Challenge;
            _remaining[0] = _rules.PerBout;
            _remaining[1] = _rules.PerBout;
        }

        public int Remaining(Corner corner) => _remaining[(int)corner];

        /// <summary>Whether a challenge would be accepted now.</summary>
        /// <param name="ticksSinceCallOrNeutral">Wrestling-clock ticks since the points were published or the wrestlers returned to neutral.</param>
        public ChallengeRefusal CanRequest(Corner corner, int ticksSinceCallOrNeutral, bool isPassivityCall, bool isFall, bool fallFollowedIllegalAction, bool winnerDeclared)
        {
            if (winnerDeclared)
            {
                return ChallengeRefusal.BoutDecided;
            }

            if (_remaining[(int)corner] <= 0)
            {
                return ChallengeRefusal.NoChallengeLeft;
            }

            if (isPassivityCall)
            {
                return ChallengeRefusal.PassivityCannotBeChallenged;
            }

            if (isFall && !fallFollowedIllegalAction)
            {
                return ChallengeRefusal.FallCannotBeChallenged;
            }

            return ticksSinceCallOrNeutral > BoutClock.SecondsToTicks(_rules.RequestWindowSeconds) ? ChallengeRefusal.OutsideWindow : ChallengeRefusal.None;
        }

        /// <summary>
        /// Applies the review. When the decision changes, the caller corrects the sheet and the
        /// challenge is kept; otherwise it is used up and the opponent receives the lost-challenge point.
        /// </summary>
        public PointsEntry? Resolve(Corner challenger, bool decisionChanged, BoutScoreSheet sheet, BoutTime time)
        {
            if (_remaining[(int)challenger] <= 0)
            {
                throw new InvalidOperationException("No challenge left to resolve.");
            }

            if (decisionChanged && _rules.SuccessfulChallengeIsKept)
            {
                return null;
            }

            _remaining[(int)challenger]--;
            if (decisionChanged || _rules.LostChallengePoints == 0)
            {
                return null;
            }

            return sheet.AwardPoints(challenger.Opponent(), _rules.LostChallengePoints, PointSource.LostChallenge, time);
        }
    }
}
