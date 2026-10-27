using System;
using System.Collections.Generic;
using Wrestling.Core.Numerics;

namespace Wrestling.Core.Rules
{
    /// <summary>Areas of the mat (Art. 4).</summary>
    public enum MatZone
    {
        /// <summary>Inside the 7 m circle.</summary>
        CentralSurface,

        /// <summary>The 1 m orange band; part of the wrestling area.</summary>
        PassivityZone,

        /// <summary>The 1.5 m border outside the wrestling area; entering it with a foot is stepping out.</summary>
        ProtectionArea,

        /// <summary>Beyond the protection area.</summary>
        OffMat,
    }

    /// <summary>Mat dimensions in metres (Art. 4).</summary>
    public sealed class MatGeometry
    {
        public Fix64 WrestlingAreaDiameter { get; init; }

        public Fix64 CentralSurfaceDiameter { get; init; }

        public Fix64 PassivityZoneWidth { get; init; }

        public Fix64 ProtectionAreaWidth { get; init; }

        public Fix64 CentralCircleDiameter { get; init; }

        /// <summary>Radius of the whole wrestling area including the orange zone.</summary>
        public Fix64 WrestlingAreaRadius => WrestlingAreaDiameter / Fix64.FromInt(2);

        /// <summary>Radius of the central wrestling surface (inside the orange zone).</summary>
        public Fix64 CentralSurfaceRadius => CentralSurfaceDiameter / Fix64.FromInt(2);

        /// <summary>Outer radius of the protection area.</summary>
        public Fix64 OuterRadius => WrestlingAreaRadius + ProtectionAreaWidth;

        /// <summary>Radius of the central circle where the bout starts.</summary>
        public Fix64 CentralCircleRadius => CentralCircleDiameter / Fix64.FromInt(2);

        /// <summary>
        /// Zone of a point on the mat, measured from the centre (Art. 4, 45). Boundaries belong to
        /// the inner zone: a point exactly on the 9 m line is still in the passivity zone.
        /// </summary>
        public MatZone ZoneAt(FixVector2 position)
        {
            Fix64 distanceSquared = position.LengthSquared;
            if (distanceSquared <= CentralSurfaceRadius * CentralSurfaceRadius)
            {
                return MatZone.CentralSurface;
            }

            if (distanceSquared <= WrestlingAreaRadius * WrestlingAreaRadius)
            {
                return MatZone.PassivityZone;
            }

            return distanceSquared <= OuterRadius * OuterRadius ? MatZone.ProtectionArea : MatZone.OffMat;
        }

        internal void Validate(ValidationContext context)
        {
            context.RequirePositive(WrestlingAreaDiameter, "mat.wrestlingAreaDiameter");
            context.RequirePositive(CentralSurfaceDiameter, "mat.centralSurfaceDiameter");
            context.RequirePositive(PassivityZoneWidth, "mat.passivityZoneWidth");
            context.RequirePositive(ProtectionAreaWidth, "mat.protectionAreaWidth");
            context.RequirePositive(CentralCircleDiameter, "mat.centralCircleDiameter");
            if (CentralSurfaceDiameter + PassivityZoneWidth * Fix64.FromInt(2) != WrestlingAreaDiameter)
            {
                context.Error("mat: central surface plus two passivity zone widths must equal the wrestling area diameter.");
            }
        }
    }

    /// <summary>Who may enter an age category, by age reached in the competition year (Art. 7).</summary>
    public sealed class AgeEligibility
    {
        public int MinimumAge { get; init; }

        /// <summary>No upper limit when null.</summary>
        public int? MaximumAge { get; init; }

        /// <summary>Youngest age allowed with medical and parental authorization; null when not applicable.</summary>
        public int? MinimumAgeWithAuthorization { get; init; }

        public bool IsEligible(int ageInYear, bool hasAuthorization)
        {
            if (MaximumAge.HasValue && ageInYear > MaximumAge.Value)
            {
                return false;
            }

            if (ageInYear >= MinimumAge)
            {
                return true;
            }

            return hasAuthorization && MinimumAgeWithAuthorization.HasValue && ageInYear >= MinimumAgeWithAuthorization.Value;
        }

        internal void Validate(ValidationContext context, string path)
        {
            context.RequireRange(MinimumAge, 1, 100, path + ".minimumAge");
            if (MaximumAge.HasValue && MaximumAge.Value < MinimumAge)
            {
                context.Error(path + ".maximumAge must not be below minimumAge.");
            }

            if (MinimumAgeWithAuthorization.HasValue && MinimumAgeWithAuthorization.Value >= MinimumAge)
            {
                context.Error(path + ".minimumAgeWithAuthorization must be below minimumAge.");
            }
        }
    }

    /// <summary>Periods and break (Art. 22).</summary>
    public sealed class BoutDuration
    {
        public int Periods { get; init; }

        public int PeriodSeconds { get; init; }

        public int BreakSeconds { get; init; }

        public int TotalWrestlingSeconds => Periods * PeriodSeconds;

        internal void Validate(ValidationContext context, string path)
        {
            context.RequireRange(Periods, 1, 10, path + ".periods");
            context.RequireRange(PeriodSeconds, 1, 3600, path + ".periodSeconds");
            context.RequireRange(BreakSeconds, 0, 3600, path + ".breakSeconds");
        }
    }

    /// <summary>Values of technical actions (Art. 38). Which situation applies is decided by the scoring evaluator.</summary>
    public sealed class TechnicalPointValues
    {
        public int StepOut { get; init; }

        public int Reversal { get; init; }

        public int Takedown { get; init; }

        public int ThrowToStomachOrFlank { get; init; }

        public int Exposure { get; init; }

        public int BlockAttackerInDanger { get; init; }

        public int StandingHoldToDanger { get; init; }

        public int ShortAmplitudeLiftToDanger { get; init; }

        public int GrandAmplitudeWithoutDirectDanger { get; init; }

        public int CompleteLiftLandingOnChestOrArms { get; init; }

        public int InstantaneousFallFromStanding { get; init; }

        public int GrandAmplitudeToDirectDanger { get; init; }

        public int ParTerreGrandAmplitudeToDirectDanger { get; init; }

        public int InjuryStoppageWithoutVisibleInjury { get; init; }

        internal void Validate(ValidationContext context)
        {
            const string path = "technicalPoints.";
            context.RequireRange(StepOut, 1, 10, path + "stepOut");
            context.RequireRange(Reversal, 1, 10, path + "reversal");
            context.RequireRange(Takedown, 1, 10, path + "takedown");
            context.RequireRange(ThrowToStomachOrFlank, 1, 10, path + "throwToStomachOrFlank");
            context.RequireRange(Exposure, 1, 10, path + "exposure");
            context.RequireRange(BlockAttackerInDanger, 1, 10, path + "blockAttackerInDanger");
            context.RequireRange(StandingHoldToDanger, 1, 10, path + "standingHoldToDanger");
            context.RequireRange(ShortAmplitudeLiftToDanger, 1, 10, path + "shortAmplitudeLiftToDanger");
            context.RequireRange(GrandAmplitudeWithoutDirectDanger, 1, 10, path + "grandAmplitudeWithoutDirectDanger");
            context.RequireRange(CompleteLiftLandingOnChestOrArms, 1, 10, path + "completeLiftLandingOnChestOrArms");
            context.RequireRange(InstantaneousFallFromStanding, 1, 10, path + "instantaneousFallFromStanding");
            context.RequireRange(GrandAmplitudeToDirectDanger, 1, 10, path + "grandAmplitudeToDirectDanger");
            context.RequireRange(ParTerreGrandAmplitudeToDirectDanger, 1, 10, path + "parTerreGrandAmplitudeToDirectDanger");
            context.RequireRange(InjuryStoppageWithoutVisibleInjury, 1, 10, path + "injuryStoppageWithoutVisibleInjury");
        }
    }

    /// <summary>Points awarded to the opponent for infringements (Art. 26, 44, 46–51).</summary>
    public sealed class PenaltyValues
    {
        public int FleeingHold { get; init; }

        public int FleeingMat { get; init; }

        public int FleeingMatInDanger { get; init; }

        public StyleValues<int> NegativeWrestling { get; init; } = new StyleValues<int>();

        /// <summary>Defender's illegal action that prevents or hinders a hold.</summary>
        public StyleValues<int> DefenderIllegalAction { get; init; } = new StyleValues<int>();

        /// <summary>Attacker's repeated illegal hold, after the first "attention".</summary>
        public int AttackerRepeatedIllegalHold { get; init; }

        /// <summary>Bottom wrestler refusing the ordered par terre position a second time.</summary>
        public StyleValues<int> RefuseParTerrePosition { get; init; } = new StyleValues<int>();

        /// <summary>Greco-Roman leg foul by the attacker, second offence.</summary>
        public int GrecoAttackerLegFoul { get; init; }

        /// <summary>Greco-Roman leg foul by the defender.</summary>
        public int GrecoDefenderLegFoul { get; init; }

        /// <summary>Stalling, fake injury, interrupting the bout to change position (second time).</summary>
        public int Stalling { get; init; }

        internal void Validate(ValidationContext context)
        {
            const string path = "penalties.";
            context.RequireRange(FleeingHold, 1, 10, path + "fleeingHold");
            context.RequireRange(FleeingMat, 1, 10, path + "fleeingMat");
            context.RequireRange(FleeingMatInDanger, 1, 10, path + "fleeingMatInDanger");
            context.RequireStyleRange(NegativeWrestling, 1, 10, path + "negativeWrestling");
            context.RequireStyleRange(DefenderIllegalAction, 1, 10, path + "defenderIllegalAction");
            context.RequireRange(AttackerRepeatedIllegalHold, 1, 10, path + "attackerRepeatedIllegalHold");
            context.RequireStyleRange(RefuseParTerrePosition, 1, 10, path + "refuseParTerrePosition");
            context.RequireRange(GrecoAttackerLegFoul, 1, 10, path + "grecoAttackerLegFoul");
            context.RequireRange(GrecoDefenderLegFoul, 1, 10, path + "grecoDefenderLegFoul");
            context.RequireRange(Stalling, 1, 10, path + "stalling");
        }
    }

    /// <summary>Greco-Roman passivity (Art. 46): a point and a choice of position for the first calls.</summary>
    public sealed class GrecoRomanPassivityRules
    {
        /// <summary>Points to the active wrestler for the 1st, 2nd, ... call of the bout; calls beyond the list use the last value.</summary>
        public IReadOnlyList<int> PointsByCall { get; init; } = Array.Empty<int>();

        public bool ActiveWrestlerChoosesPosition { get; init; }

        /// <summary>Points for the given call number (1-based).</summary>
        public int PointsForCall(int callNumber)
        {
            if (callNumber < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(callNumber), "Call numbers start at 1.");
            }

            int index = Math.Min(callNumber, PointsByCall.Count) - 1;
            return PointsByCall[index];
        }

        internal void Validate(ValidationContext context)
        {
            if (PointsByCall.Count == 0)
            {
                context.Error("passivity.grecoRoman.pointsByCall must not be empty.");
            }

            foreach (int points in PointsByCall)
            {
                context.RequireRange(points, 0, 10, "passivity.grecoRoman.pointsByCall[]");
            }
        }
    }

    /// <summary>Freestyle and Women's activity clock (Art. 46).</summary>
    public sealed class ActivityClockRules
    {
        public int ActivitySeconds { get; init; }

        public int PointIfNoScore { get; init; }

        /// <summary>Age categories with a verbal warning before the first activity period.</summary>
        public AgeValues<bool> VerbalWarningFirst { get; init; } = new AgeValues<bool>();

        /// <summary>Elapsed seconds of the first period at which a scoreless bout forces an inactivity designation.</summary>
        public AgeValues<int> ScorelessDesignationAtSeconds { get; init; } = new AgeValues<int>();

        /// <summary>Length of the end-of-period window where evading or blocking gives the opponent a point and a caution.</summary>
        public int FinalSecondsEvasionWindow { get; init; }

        public int FinalSecondsEvasionPoints { get; init; }

        internal void Validate(ValidationContext context)
        {
            const string path = "passivity.activityClock.";
            context.RequireRange(ActivitySeconds, 1, 300, path + "activitySeconds");
            context.RequireRange(PointIfNoScore, 1, 10, path + "pointIfNoScore");
            context.RequireAgeRange(ScorelessDesignationAtSeconds, 1, 300, path + "scorelessDesignationAtSeconds");
            context.RequireRange(FinalSecondsEvasionWindow, 1, 300, path + "finalSecondsEvasionWindow");
            context.RequireRange(FinalSecondsEvasionPoints, 1, 10, path + "finalSecondsEvasionPoints");
        }
    }

    public sealed class PassivityRules
    {
        public GrecoRomanPassivityRules GrecoRoman { get; init; } = new GrecoRomanPassivityRules();

        /// <summary>Applies to Freestyle and Women's wrestling.</summary>
        public ActivityClockRules ActivityClock { get; init; } = new ActivityClockRules();
    }

    /// <summary>Video challenge (Art. 31).</summary>
    public sealed class ChallengeRules
    {
        public int PerBout { get; init; }

        public int LostChallengePoints { get; init; }

        public int RequestWindowSeconds { get; init; }

        /// <summary>A challenge that changes the decision is kept for later use.</summary>
        public bool SuccessfulChallengeIsKept { get; init; }

        internal void Validate(ValidationContext context)
        {
            context.RequireRange(PerBout, 0, 10, "challenge.perBout");
            context.RequireRange(LostChallengePoints, 0, 10, "challenge.lostChallengePoints");
            context.RequireRange(RequestWindowSeconds, 1, 60, "challenge.requestWindowSeconds");
        }
    }

    /// <summary>Weight tolerance on the first weigh-in by event type (Art. 8).</summary>
    public sealed class WeighInTolerance
    {
        public int OlympicGames { get; init; }

        public int Championships { get; init; }

        public int WorldCup { get; init; }

        public int RankingSeries { get; init; }

        public int InternationalTournament { get; init; }
    }

    /// <summary>Weigh-in procedure (Art. 8, 11).</summary>
    public sealed class WeighInRules
    {
        public int FirstDayDurationMinutes { get; init; }

        public int SecondDayDurationMinutes { get; init; }

        public WeighInTolerance FirstDayToleranceKg { get; init; } = new WeighInTolerance();

        /// <summary>Weight tolerance on the second morning (always 0 under current rules).</summary>
        public int SecondDayToleranceKg { get; init; }

        internal void Validate(ValidationContext context)
        {
            context.RequireRange(FirstDayDurationMinutes, 1, 240, "weighIn.firstDayDurationMinutes");
            context.RequireRange(SecondDayDurationMinutes, 1, 240, "weighIn.secondDayDurationMinutes");
            context.RequireRange(SecondDayToleranceKg, 0, 10, "weighIn.secondDayToleranceKg");
            WeighInTolerance t = FirstDayToleranceKg;
            context.RequireRange(t.OlympicGames, 0, 10, "weighIn.firstDayToleranceKg.olympicGames");
            context.RequireRange(t.Championships, 0, 10, "weighIn.firstDayToleranceKg.championships");
            context.RequireRange(t.WorldCup, 0, 10, "weighIn.firstDayToleranceKg.worldCup");
            context.RequireRange(t.RankingSeries, 0, 10, "weighIn.firstDayToleranceKg.rankingSeries");
            context.RequireRange(t.InternationalTournament, 0, 10, "weighIn.firstDayToleranceKg.internationalTournament");
        }
    }

    /// <summary>Bout timing outside the clock (Art. 23, 26).</summary>
    public sealed class BoutProcedureRules
    {
        public int CautionsForLoss { get; init; }

        public int MinimumRestBetweenBoutsMinutes { get; init; }

        public int CallsBeforeForfeit { get; init; }

        public int SecondsBetweenCalls { get; init; }

        public int BleedingTreatmentSecondsPerBout { get; init; }

        /// <summary>Continuous shoulder contact under control needed to confirm a fall. The rulebook says "long enough to confirm control"; this is the calibrated default.</summary>
        public Fix64 FallConfirmationSeconds { get; init; }

        internal void Validate(ValidationContext context)
        {
            context.RequireRange(CautionsForLoss, 1, 10, "procedure.cautionsForLoss");
            context.RequireRange(MinimumRestBetweenBoutsMinutes, 0, 240, "procedure.minimumRestBetweenBoutsMinutes");
            context.RequireRange(CallsBeforeForfeit, 1, 10, "procedure.callsBeforeForfeit");
            context.RequireRange(SecondsBetweenCalls, 1, 600, "procedure.secondsBetweenCalls");
            context.RequireRange(BleedingTreatmentSecondsPerBout, 0, 3600, "procedure.bleedingTreatmentSecondsPerBout");
            context.RequirePositive(FallConfirmationSeconds, "procedure.fallConfirmationSeconds");
        }
    }
}

namespace Wrestling.Core.Rules
{
    /// <summary>Team ranking points by individual placing (Art. 32).</summary>
    public sealed class TeamRankingRules
    {
        /// <summary>Points for rank 1, 2, 3, ...; ranks beyond the list score nothing.</summary>
        public System.Collections.Generic.IReadOnlyList<int> PointsByRank { get; init; } = System.Array.Empty<int>();

        public int PointsFor(int rank)
        {
            if (rank < 1)
            {
                throw new System.ArgumentOutOfRangeException(nameof(rank), "Ranks start at 1.");
            }

            return rank <= PointsByRank.Count ? PointsByRank[rank - 1] : 0;
        }

        internal void Validate(ValidationContext context)
        {
            if (PointsByRank.Count == 0)
            {
                context.Error("teamRanking.pointsByRank must not be empty.");
            }

            for (int i = 0; i < PointsByRank.Count; i++)
            {
                context.RequireRange(PointsByRank[i], 0, 1000, "teamRanking.pointsByRank[]");
                if (i > 0 && PointsByRank[i] > PointsByRank[i - 1])
                {
                    context.Error("teamRanking.pointsByRank must not increase with rank.");
                }
            }
        }
    }
}
