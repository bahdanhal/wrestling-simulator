using System;
using Wrestling.Core.Numerics;

namespace Wrestling.Core.Match
{
    /// <summary>
    /// Engine tuning converted once per bout into per-tick rates and tick counts, so the tick
    /// loop multiplies instead of dividing.
    /// </summary>
    internal sealed class TickRates
    {
        public TickRates(EngineTuning tuning)
        {
            Fix64 perTick = Fix64.FromRatio(1, BoutClock.TicksPerSecond);
            TickFraction = perTick;
            ActivityPerPushSecond = tuning.ActivityPerPushSecond * perTick;
            BalanceRecoveryPerSecond = tuning.BalanceRecoveryPerSecond * perTick;
            BurstRecoveryPerSecond = tuning.BurstRecoveryPerSecond * perTick;
            DangerEscapePerSecond = tuning.DangerEscapePerSecond * perTick;
            GripGainPerSecond = tuning.GripGainPerSecond * perTick;
            GripStrengthDecayPerSecond = tuning.GripStrengthDecayPerSecond * perTick;
            GripStripPerSecond = tuning.GripStripPerSecond * perTick;
            LevelChangePerSecond = tuning.LevelChangePerSecond * perTick;
            PushBalanceLoss = tuning.PushBalanceLoss * perTick;
            PushSpeed = tuning.PushSpeed * perTick;
            StaminaDrainPerSecond = tuning.StaminaDrainPerSecond * perTick;
            StaminaRecoveryPerSecond = tuning.StaminaRecoveryPerSecond * perTick;
            InverseResolutionScale = Fix64.One / tuning.ResolutionScale;
            AttackRecoveryTicks = Ticks(tuning.AttackRecoverySeconds);
            DangerMaximumTicks = Ticks(tuning.DangerMaximumSeconds);
            DeliberatePushLookbackTicks = Ticks(tuning.DeliberatePushLookbackSeconds);
            GrecoPassivityCheckTicks = Ticks(tuning.GrecoPassivityCheckSeconds);
            InactivityReportCooldownTicks = Ticks(tuning.InactivityReportCooldownSeconds);
            ParTerreStalemateTicks = Ticks(tuning.ParTerreStalemateSeconds);
            ScrambleTicks = Ticks(tuning.ScrambleSeconds);
            StoppageTicks = Ticks(tuning.StoppageSeconds);
        }

        /// <summary>1 / ticks per second.</summary>
        public Fix64 TickFraction { get; }

        public Fix64 ActivityPerPushSecond { get; }

        public Fix64 BalanceRecoveryPerSecond { get; }

        public Fix64 BurstRecoveryPerSecond { get; }

        public Fix64 DangerEscapePerSecond { get; }

        public Fix64 GripGainPerSecond { get; }

        public Fix64 GripStrengthDecayPerSecond { get; }

        public Fix64 GripStripPerSecond { get; }

        public Fix64 LevelChangePerSecond { get; }

        public Fix64 PushBalanceLoss { get; }

        public Fix64 PushSpeed { get; }

        public Fix64 StaminaDrainPerSecond { get; }

        public Fix64 StaminaRecoveryPerSecond { get; }

        public Fix64 InverseResolutionScale { get; }

        public int AttackRecoveryTicks { get; }

        public int DangerMaximumTicks { get; }

        public int DeliberatePushLookbackTicks { get; }

        public int GrecoPassivityCheckTicks { get; }

        public int InactivityReportCooldownTicks { get; }

        public int ParTerreStalemateTicks { get; }

        public int ScrambleTicks { get; }

        public int StoppageTicks { get; }

        private static int Ticks(Fix64 seconds) => Math.Max(1, (seconds * Fix64.FromInt(BoutClock.TicksPerSecond)).FloorToInt());
    }
}
