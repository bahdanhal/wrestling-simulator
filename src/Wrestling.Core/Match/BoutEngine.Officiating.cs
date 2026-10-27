using Wrestling.Core.Bouts;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Match
{
    public sealed partial class BoutEngine
    {
        private static readonly Fix64 DeliberatePushIntensity = Fix64.Half;

        /// <summary>
        /// The officials' view of the bout (design section 5.9). This officiating crew sees the true
        /// state exactly; observation noise and wrong calls belong to the officials model.
        /// </summary>
        private void Officiate()
        {
            bool inAction = ActiveTechnique != null || Position == BoutPosition.Danger || Position == BoutPosition.Scramble;
            if (Position == BoutPosition.Neutral && ActiveTechnique == null)
            {
                (bool redOut, bool redZone) = FootZones(Wrestler(Corner.Red));
                (bool blueOut, bool blueZone) = FootZones(Wrestler(Corner.Blue));
                if (CheckStepOut(redOut, blueOut))
                {
                    return;
                }

                CheckZone(Corner.Red, redZone);
                CheckZone(Corner.Blue, blueZone);
            }

            CheckParTerreStalemate();
            if (_stoppageTicks > 0 || IsFinished)
            {
                return;
            }

            if (Style.UsesFreestyleRules())
            {
                OfficiateActivityClock(inAction);
            }
            else
            {
                OfficiateGrecoPassivity();
            }

            if (!IsFinished && !inAction)
            {
                CheckEndConditions();
            }
        }

        private bool FootInProtectionArea(WrestlerRuntime wrestler)
        {
            (FixVector2 left, FixVector2 right) = wrestler.Body.FootPositions();
            return _zones.ZoneAt(left) >= MatZone.ProtectionArea || _zones.ZoneAt(right) >= MatZone.ProtectionArea;
        }

        /// <summary>Whether a foot is in the protection area, and whether a foot is in the passivity zone.</summary>
        private (bool InProtectionArea, bool InPassivityZone) FootZones(WrestlerRuntime wrestler)
        {
            (FixVector2 left, FixVector2 right) = wrestler.Body.FootPositions();
            MatZone leftZone = _zones.ZoneAt(left);
            MatZone rightZone = _zones.ZoneAt(right);
            return (leftZone >= MatZone.ProtectionArea || rightZone >= MatZone.ProtectionArea,
                leftZone == MatZone.PassivityZone || rightZone == MatZone.PassivityZone);
        }

        /// <summary>One entire foot in the protection area without a hold (Art. 38, 45).</summary>
        private bool CheckStepOut(bool redOut, bool blueOut)
        {
            WrestlerRuntime red = Wrestler(Corner.Red);
            WrestlerRuntime blue = Wrestler(Corner.Blue);
            if (!redOut && !blueOut)
            {
                return false;
            }

            Corner steppedOut;
            if (redOut && blueOut)
            {
                // Both at once: the one being driven out stepped first.
                Fix64 redPush = red.Intent.PushIntensity;
                Fix64 bluePush = blue.Intent.PushIntensity;
                steppedOut = redPush == bluePush ? (_random.NextUInt(2) == 0 ? Corner.Red : Corner.Blue) : redPush < bluePush ? Corner.Red : Corner.Blue;
            }
            else
            {
                steppedOut = redOut ? Corner.Red : Corner.Blue;
            }

            WrestlerRuntime pusher = Wrestler(steppedOut.Opponent());
            bool deliberate = Tick - pusher.LastAttackTick > _rates.DeliberatePushLookbackTicks
                && pusher.Intent.PushIntensity >= DeliberatePushIntensity;
            if (deliberate && !Wrestler(steppedOut).Body.IsGrounded)
            {
                Sheet.Warn(pusher.Corner, WarningReason.DeliberatePushOut, Clock.Now);
                Emit(BoutEventType.Warning, pusher.Corner, null, 0, WarningReason.DeliberatePushOut.ToString());
            }

            Emit(BoutEventType.StepOut, steppedOut, null, 0, deliberate ? "deliberate push" : string.Empty);
            ApplyScore(ScoringEvaluator.StepOut(steppedOut, deliberate, Wrestler(steppedOut).Body.IsGrounded, Rules));
            if (!IsFinished)
            {
                Stop(RestartPosition.StandingCentre, steppedOut.Opponent(), "step out");
            }

            return true;
        }

        /// <summary>The referee calls "zone" the first time a wrestler steps into the passivity zone (Art. 49).</summary>
        private void CheckZone(Corner corner, bool inPassivityZone)
        {
            if (inPassivityZone && !_zoneCalled[(int)corner])
            {
                _zoneCalled[(int)corner] = true;
                Emit(BoutEventType.ZoneCall, corner, null, 0, string.Empty);
            }
        }

        private void OfficiateActivityClock(bool inAction)
        {
            ActivityClockProcedure procedure = _activityClock!;
            WrestlerRuntime red = Wrestler(Corner.Red);
            WrestlerRuntime blue = Wrestler(Corner.Blue);
            Corner lessActive = red.Activity <= blue.Activity ? Corner.Red : Corner.Blue;

            if (!inAction && Position == BoutPosition.Neutral && procedure.Passive == null)
            {
                foreach (WrestlerRuntime wrestler in _wrestlers)
                {
                    WrestlerRuntime other = Wrestler(wrestler.Corner.Opponent());
                    bool inactive = wrestler.Activity < Tuning.InactivityThreshold && wrestler.Activity * Fix64.FromInt(2) < other.Activity;
                    if (!inactive || Tick - _lastInactivityReport[(int)wrestler.Corner] < _rates.InactivityReportCooldownTicks)
                    {
                        continue;
                    }

                    _lastInactivityReport[(int)wrestler.Corner] = Tick;
                    PassivityDecision decision = procedure.ReportInactivity(wrestler.Corner, Sheet, Clock, inAction);
                    if (decision == PassivityDecision.VerbalWarning)
                    {
                        Emit(BoutEventType.Warning, wrestler.Corner, null, 0, WarningReason.Passivity.ToString());
                    }
                    else if (decision == PassivityDecision.ActivityPeriodStarted)
                    {
                        Emit(BoutEventType.ActivityPeriodStarted, wrestler.Corner, null, 0, string.Empty);
                    }

                    break;
                }
            }

            Corner? passiveBefore = procedure.Passive;
            PointsEntry? point = procedure.Update(Sheet, Clock, inAction, lessActive);
            if (procedure.Passive.HasValue && passiveBefore == null)
            {
                Emit(BoutEventType.ActivityPeriodStarted, procedure.Passive, null, 0, "scoreless bout");
            }

            if (point != null)
            {
                _parTerreTicks = 0;
                Emit(BoutEventType.PointsAwarded, point.Wrestler, null, point.Points, PointSource.ActivityClock.ToString());
                CheckEndConditions();
            }
        }

        /// <summary>
        /// Greco-Roman passivity (Art. 46): at regular intervals the officials call the wrestler
        /// blocking active wrestling: at 0–0, when level with one clearly more active, or when the
        /// leader is too defensive. The active wrestler then chooses standing or par terre.
        /// </summary>
        private void OfficiateGrecoPassivity()
        {
            if (Position != BoutPosition.Neutral || ActiveTechnique != null)
            {
                return;
            }

            if (Clock.ElapsedTicks - _lastGrecoCheckElapsed < _rates.GrecoPassivityCheckTicks)
            {
                return;
            }

            _lastGrecoCheckElapsed = Clock.ElapsedTicks;
            WrestlerRuntime red = Wrestler(Corner.Red);
            WrestlerRuntime blue = Wrestler(Corner.Blue);
            int redPoints = Sheet.Points(Corner.Red);
            int bluePoints = Sheet.Points(Corner.Blue);
            Fix64 gap = red.Activity - blue.Activity;
            Corner? passive = null;

            if (redPoints == bluePoints)
            {
                if (redPoints == 0 && gap == Fix64.Zero)
                {
                    passive = _random.NextUInt(2) == 0 ? Corner.Red : Corner.Blue;
                }
                else if (redPoints == 0 || Fix64.Abs(gap) >= Tuning.GrecoActivityDifference)
                {
                    passive = gap < Fix64.Zero ? Corner.Red : Corner.Blue;
                }
            }
            else
            {
                Corner leader = redPoints > bluePoints ? Corner.Red : Corner.Blue;
                Fix64 leaderLead = leader == Corner.Red ? gap : -gap;
                if (-leaderLead >= Tuning.GrecoActivityDifference)
                {
                    passive = leader;
                }
            }

            if (passive == null)
            {
                return;
            }

            GrecoPassivityCall call = GrecoPassivityProcedure.Call(passive.Value, Sheet, Clock.Now, Rules);
            Emit(BoutEventType.PassivityCall, passive, null, call.Point?.Points ?? 0, call.Entry.CallNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (call.Point != null)
            {
                Emit(BoutEventType.PointsAwarded, call.Point.Wrestler, null, call.Point.Points, PointSource.Passivity.ToString());
            }

            CheckEndConditions();
            if (IsFinished)
            {
                return;
            }

            Corner active = passive.Value.Opponent();
            bool parTerre = call.ActiveChoosesPosition && _setup.ControllerOf(active).ChoosesParTerre(this, active);
            Stop(parTerre ? RestartPosition.ParTerre : RestartPosition.StandingCentre, active, "passivity");
        }

        /// <summary>Technical superiority and three cautions end the bout (Art. 29, 43).</summary>
        private void CheckEndConditions()
        {
            if (IsFinished)
            {
                return;
            }

            Corner? cautioned = BoutEndConditions.LostByCautions(Sheet, Rules);
            if (cautioned.HasValue)
            {
                Finish(BoutResult.Win(cautioned.Value.Opponent(), VictoryType.ThreeCautions, Sheet, Rules));
                return;
            }

            if (ActiveTechnique != null || Position == BoutPosition.Danger || Position == BoutPosition.Scramble)
            {
                // Technical superiority is final only once the action is over (Art. 18, 27).
                return;
            }

            Corner? superior = BoutEndConditions.TechnicalSuperiority(Sheet, Rules, Style);
            if (superior.HasValue)
            {
                Finish(BoutResult.Win(superior.Value, VictoryType.TechnicalSuperiority, Sheet, Rules));
            }
        }
    }
}
