using Wrestling.Core.Bouts;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Core.Match
{
    public sealed partial class BoutEngine
    {
        private static readonly Fix64 OneThird = Fix64.FromRatio(1, 3);
        private static readonly Fix64 EscapedTorsoRotation = Fix64.FromInt(30);

        /// <summary>
        /// Danger position: each tick the bottom wrestler may bridge out, or the top wrestler may
        /// hold both shoulders down; continuous shoulder contact for the confirmation time is a fall (Art. 42).
        /// </summary>
        private void HoldDanger()
        {
            Corner top = Top!.Value;
            WrestlerRuntime upper = Wrestler(top);
            WrestlerRuntime bottom = Wrestler(top.Opponent());
            _dangerTicks++;

            Fix64 power = upper.Score((WrestlerAttribute.UpperBodyStrength, 50), (WrestlerAttribute.ParTerreTurns, 50));
            Fix64 bridge = bottom.Score((WrestlerAttribute.BridgingAndDangerDefence, 60), (WrestlerAttribute.Flexibility, 40));

            if (_random.Chance(Contest(bridge, power, _rates.DangerEscapePerSecond)))
            {
                bottom.Body.LeftShoulderOnMat = false;
                bottom.Body.RightShoulderOnMat = false;
                bottom.Body.Ground = GroundState.Prone;
                bottom.Body.TorsoRotationDegrees = EscapedTorsoRotation;
                Position = BoutPosition.ParTerre;
                _parTerreTicks = 0;
                Emit(BoutEventType.Escape, bottom.Corner, null, 0, "bridged out");
                return;
            }

            bool shouldersDown = _random.Chance(Contest(power, bridge, Tuning.PinPressure));
            bottom.Body.LeftShoulderOnMat = shouldersDown;
            bottom.Body.RightShoulderOnMat = shouldersDown;
            _shoulderTicks = shouldersDown ? _shoulderTicks + 1 : 0;

            // A fall counts only with both shoulders inside the wrestling area (Art. 42, 45).
            if (_shoulderTicks >= _fallTicks && _zones.ZoneAt(bottom.Body.Position) <= MatZone.PassivityZone)
            {
                Finish(BoutResult.Win(top, VictoryType.Fall, Sheet, Rules));
                return;
            }

            if (_dangerTicks >= _rates.DangerMaximumTicks)
            {
                // The bottom wrestler turned to the stomach: wrestling continues in par terre.
                bottom.Body.LeftShoulderOnMat = false;
                bottom.Body.RightShoulderOnMat = false;
                bottom.Body.Ground = GroundState.Prone;
                bottom.Body.TorsoRotationDegrees = Fix64.Zero;
                Position = BoutPosition.ParTerre;
                _parTerreTicks = 0;
                Emit(BoutEventType.Escape, bottom.Corner, null, 0, "turned out");
            }
        }

        private void BeginScramble(Corner origin, bool fromFailure)
        {
            Position = BoutPosition.Scramble;
            Top = null;
            Grips.Clear();
            _scrambleOrigin = origin;
            _scrambleFromFailure = fromFailure;
            _scrambleTicks = _rates.ScrambleTicks;
            Emit(BoutEventType.Scramble, origin, null, 0, fromFailure ? "after a failed attack" : string.Empty);
        }

        /// <summary>
        /// A scramble ends with both back on their feet, the original attacker recovering the
        /// takedown, or the defender on top. A defender on top after a failed attack is the risk
        /// rule: no points, restart standing (Art. 34).
        /// </summary>
        private void AdvanceScramble()
        {
            if (--_scrambleTicks > 0)
            {
                return;
            }

            Corner origin = _scrambleOrigin;
            WrestlerRuntime attacker = Wrestler(origin);
            WrestlerRuntime defender = Wrestler(origin.Opponent());
            if (_random.NextFix01() < OneThird)
            {
                Position = BoutPosition.Neutral;
                StandUp(attacker);
                StandUp(defender);
                return;
            }

            Fix64 attackerScore = attacker.Score((WrestlerAttribute.Agility, 40), (WrestlerAttribute.WrestlingIq, 30), (WrestlerAttribute.Balance, 30));
            Fix64 defenderScore = defender.Score((WrestlerAttribute.Agility, 40), (WrestlerAttribute.WrestlingIq, 30), (WrestlerAttribute.Balance, 30));
            bool attackerOnTop = _random.Chance(FixMath.Sigmoid((attackerScore - defenderScore) * _rates.InverseResolutionScale));
            Corner winner = attackerOnTop ? origin : origin.Opponent();

            if (!attackerOnTop && _scrambleFromFailure)
            {
                ApplyScore(ScoringEvaluator.Evaluate(new ActionFacts { Executor = origin, FailedAndEndedUnderneath = true }, Rules));
                Stop(RestartPosition.StandingCentre, origin, "risk: attacker ended underneath");
                return;
            }

            ApplyScore(ScoringEvaluator.Evaluate(new ActionFacts { Executor = winner, Completed = true, TakedownBehind = true }, Rules));
            if (!IsFinished)
            {
                EnterParTerre(winner);
            }
        }

        /// <summary>The referee restarts standing when the top wrestler cannot progress (Art. 44).</summary>
        private void CheckParTerreStalemate()
        {
            if (Position != BoutPosition.ParTerre)
            {
                return;
            }

            // Time counts while the top wrestler attempts holds, but the referee waits for an action to finish.
            if (++_parTerreTicks >= _rates.ParTerreStalemateTicks && ActiveTechnique == null)
            {
                Stop(RestartPosition.StandingCentre, Top ?? Corner.Red, "no progress in par terre");
            }
        }
    }
}
