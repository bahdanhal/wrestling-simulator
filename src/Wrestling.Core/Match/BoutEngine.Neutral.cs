using System.Linq;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Core.Match
{
    public sealed partial class BoutEngine
    {
        private static readonly Fix64 Hundredth = Fix64.FromRatio(1, 100);
        private static readonly Fix64 BalanceResistShare = Fix64.FromRatio(3, 10);
        private static readonly Fix64 LowEffort = Fix64.FromRatio(3, 10);
        private static readonly Fix64 GripReach = Fix64.FromRatio(12, 10);

        /// <summary>Gaining, breaking and holding grips (design section 5.4).</summary>
        private void HandFight()
        {
            WrestlerRuntime red = Wrestler(Corner.Red);
            WrestlerRuntime blue = Wrestler(Corner.Blue);
            bool inReach = Position == BoutPosition.ParTerre
                || FixVector2.DistanceSquared(red.Body.Position, blue.Body.Position) <= (Tuning.ContactDistance * GripReach) * (Tuning.ContactDistance * GripReach);

            foreach (WrestlerRuntime self in _wrestlers)
            {
                WrestlerRuntime other = Wrestler(self.Corner.Opponent());
                Intent intent = self.Intent;
                Fix64 own = self.Effective(WrestlerAttribute.HandFighting);
                Fix64 theirs = other.Effective(WrestlerAttribute.HandFighting);

                if (inReach && intent.GripGoal.HasValue && Grips.CanTake(self.Corner, intent.GripGoal.Value, intent.GripGoalSide, out _)
                    && _random.Chance(Contest(own, theirs, _rates.GripGainPerSecond)))
                {
                    Fix64 quality = Fix64.FromRatio(4, 10) + (Fix64.FromRatio(6, 10) * _random.NextFix01());
                    Grip grip = Grips.Take(self.Corner, intent.GripGoal.Value, intent.GripGoalSide, quality, Fix64.One, Tick);
                    Emit(BoutEventType.GripGained, self.Corner, null, 0, grip.Type.ToString());
                }

                if (intent.StripGrips)
                {
                    for (int i = Grips.All.Count - 1; i >= 0; i--)
                    {
                        Grip grip = Grips.All[i];
                        if (grip.Owner == other.Corner && _random.Chance(Contest(own, theirs * grip.Strength, _rates.GripStripPerSecond)))
                        {
                            Grips.Release(grip);
                            Emit(BoutEventType.GripLost, other.Corner, null, 0, grip.Type.ToString());
                        }
                    }
                }
            }

            // Grips weaken over time; strong hands keep them longer.
            for (int i = Grips.All.Count - 1; i >= 0; i--)
            {
                Grip grip = Grips.All[i];
                WrestlerRuntime owner = Wrestler(grip.Owner);
                Fix64 resistance = Fix64.One - (owner.Effective(WrestlerAttribute.GripStrength) / Fix64.FromInt(200));
                grip.Strength -= _rates.GripStrengthDecayPerSecond * resistance;
                if (grip.Strength == Fix64.Zero)
                {
                    Grips.Release(grip);
                    Emit(BoutEventType.GripLost, grip.Owner, null, 0, grip.Type.ToString());
                }
            }
        }

        /// <summary>Approach, pressure and balance in neutral (design section 5.6, step 3).</summary>
        private void Move()
        {
            WrestlerRuntime red = Wrestler(Corner.Red);
            WrestlerRuntime blue = Wrestler(Corner.Blue);
            FixVector2 between = blue.Body.Position - red.Body.Position;
            FixVector2 direction = between.Normalized();
            if (direction == FixVector2.Zero)
            {
                direction = FixVector2.UnitX;
            }

            bool contact = between.LengthSquared <= Tuning.ContactDistance * Tuning.ContactDistance || Grips.All.Count > 0;
            FixVector2 redBefore = red.Body.Position;
            FixVector2 blueBefore = blue.Body.Position;

            if (!contact)
            {
                foreach (WrestlerRuntime self in _wrestlers)
                {
                    Fix64 speed = Tuning.ApproachSpeed * (self.Effective(WrestlerAttribute.Speed) * Hundredth) * self.Intent.PushIntensity;
                    self.Body.Position += self.PushUnit * PerTick(speed);
                }

                // Never pass through each other: stop at contact distance.
                FixVector2 gap = blue.Body.Position - red.Body.Position;
                if (gap.LengthSquared < Tuning.ContactDistance * Tuning.ContactDistance)
                {
                    FixVector2 centre = (red.Body.Position + blue.Body.Position) * Fix64.Half;
                    Separate(red, blue, centre, direction);
                }
            }
            else
            {
                FixVector2 redForce = Force(red);
                FixVector2 blueForce = Force(blue);
                FixVector2 displacement = (redForce + blueForce) * _rates.PushSpeed;
                FixVector2 centre = ((red.Body.Position + blue.Body.Position) * Fix64.Half) + displacement;
                Separate(red, blue, centre, direction);
                UpdateBalance(red, blueForce, redForce, direction);
                UpdateBalance(blue, redForce, blueForce, -direction);
                AddPressureActivity(red, redForce, direction);
                AddPressureActivity(blue, blueForce, -direction);
            }

            red.Body.SetUnitFacing(direction);
            blue.Body.SetUnitFacing(-direction);
            red.Body.Momentum = (red.Body.Position - redBefore) * Fix64.FromInt(BoutClock.TicksPerSecond);
            blue.Body.Momentum = (blue.Body.Position - blueBefore) * Fix64.FromInt(BoutClock.TicksPerSecond);
            foreach (WrestlerRuntime self in _wrestlers)
            {
                self.Body.Level = FixMath.MoveTowards(self.Body.Level, self.Intent.TargetLevel, _rates.LevelChangePerSecond);
            }
        }

        private void Separate(WrestlerRuntime red, WrestlerRuntime blue, FixVector2 centre, FixVector2 direction)
        {
            FixVector2 offset = direction * (Tuning.ContactDistance * Fix64.Half);
            red.Body.Position = centre - offset;
            blue.Body.Position = centre + offset;
        }

        /// <summary>Pushing force: intended direction × effort × leg-driven strength.</summary>
        private static FixVector2 Force(WrestlerRuntime self)
        {
            Fix64 strength = self.Score((WrestlerAttribute.LowerBodyStrength, 60), (WrestlerAttribute.UpperBodyStrength, 40)) * Hundredth;
            return self.PushUnit * (self.Intent.PushIntensity * strength);
        }

        /// <summary>Being driven back faster than one can resist costs balance; otherwise balance recovers.</summary>
        private void UpdateBalance(WrestlerRuntime self, FixVector2 opponentForce, FixVector2 ownForce, FixVector2 towardOpponent)
        {
            Fix64 pushedBack = Fix64.Max(Fix64.Zero, FixVector2.Dot(opponentForce, -towardOpponent));
            Fix64 resisting = Fix64.Max(Fix64.Zero, FixVector2.Dot(ownForce, towardOpponent)) + ((self.Effective(WrestlerAttribute.Balance) * Hundredth) * BalanceResistShare);
            Fix64 excess = pushedBack - resisting;
            if (excess > Fix64.Zero)
            {
                self.Body.Balance -= excess * _rates.PushBalanceLoss;
            }
        }

        private void AddPressureActivity(WrestlerRuntime self, FixVector2 force, FixVector2 towardOpponent)
        {
            if (FixVector2.Dot(force, towardOpponent) > Fix64.Zero)
            {
                self.Activity += self.Intent.PushIntensity * _rates.ActivityPerPushSecond;
            }
        }

        /// <summary>Stamina, burst, balance recovery and activity decay (design section 5.12).</summary>
        private void UpdateEnergyAndActivity()
        {
            foreach (WrestlerRuntime self in _wrestlers)
            {
                Fix64 effort = Position == BoutPosition.Neutral ? self.Intent.PushIntensity : Fix64.Half;
                Fix64 drain = effort * _rates.StaminaDrainPerSecond;
                int held = 0;
                for (int i = 0; i < Grips.All.Count; i++)
                {
                    if (Grips.All[i].Owner == self.Corner)
                    {
                        held++;
                    }
                }

                drain += Fix64.FromInt(held) * _rates.StaminaDrainPerSecond / Fix64.FromInt(2);
                if (Position == BoutPosition.Danger && Top.HasValue && self.Corner != Top.Value)
                {
                    drain += _rates.StaminaDrainPerSecond * Fix64.FromInt(2);
                }

                self.Stamina = Fix64.Max(Fix64.Zero, self.Stamina - drain);
                if (effort < LowEffort && ActiveTechnique == null)
                {
                    Fix64 recovery = _rates.StaminaRecoveryPerSecond * (Fix64.FromInt(self.Wrestler.Attributes[WrestlerAttribute.Recovery]) * Hundredth);
                    self.Stamina = Fix64.Min(self.StaminaMax, self.Stamina + recovery);
                }

                Fix64 burstRecovery = _rates.BurstRecoveryPerSecond * (Fix64.FromInt(self.Wrestler.Attributes[WrestlerAttribute.AnaerobicCapacity]) * Hundredth);
                self.Burst = Fix64.Min(self.BurstMax, self.Burst + burstRecovery);
                self.Body.Balance += _rates.BalanceRecoveryPerSecond * (self.Effective(WrestlerAttribute.Balance) * Hundredth);
                self.Activity *= _activityDecayPerTick;
            }
        }
    }
}
