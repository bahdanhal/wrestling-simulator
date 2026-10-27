using System.Collections.Generic;

namespace Wrestling.Core.Rules
{
    /// <summary>
    /// A complete, versioned set of competition rules. All rule values come from content
    /// (for example <c>data/rulesets/uww-2026-01.json</c>); code only decides which rule applies.
    /// </summary>
    public sealed class Ruleset
    {
        public int SchemaVersion { get; init; }

        public string Id { get; init; } = string.Empty;

        /// <summary>Document the values were taken from.</summary>
        public string Source { get; init; } = string.Empty;

        public MatGeometry Mat { get; init; } = new MatGeometry();

        public AgeValues<AgeEligibility> AgeEligibility { get; init; } = new AgeValues<AgeEligibility>();

        public AgeValues<BoutDuration> BoutDuration { get; init; } = new AgeValues<BoutDuration>();

        public WeightCategoryRules WeightCategories { get; init; } = new WeightCategoryRules();

        /// <summary>Point lead that ends the bout (8 in Greco-Roman, 10 in Freestyle and Women's).</summary>
        public StyleValues<int> TechnicalSuperiority { get; init; } = new StyleValues<int>();

        public TechnicalPointValues TechnicalPoints { get; init; } = new TechnicalPointValues();

        public PenaltyValues Penalties { get; init; } = new PenaltyValues();

        public PassivityRules Passivity { get; init; } = new PassivityRules();

        public ChallengeRules Challenge { get; init; } = new ChallengeRules();

        public BoutProcedureRules Procedure { get; init; } = new BoutProcedureRules();

        public WeighInRules WeighIn { get; init; } = new WeighInRules();

        public ClassificationPointsTable ClassificationPoints { get; init; } = new ClassificationPointsTable();

        public TeamRankingRules TeamRanking { get; init; } = new TeamRankingRules();

        /// <summary>Returns every validation problem; empty when the ruleset is usable.</summary>
        public IReadOnlyList<string> Validate()
        {
            var context = new ValidationContext();
            context.RequireRange(SchemaVersion, 1, 1, "schemaVersion");
            context.RequireNotEmpty(Id, "id");
            context.RequireNotEmpty(Source, "source");

            context.RequireNotNull(Mat, "mat")?.Validate(context);
            ValidateAgeValues(context);
            context.RequireNotNull(WeightCategories, "weightCategories")?.Validate(context);
            context.RequireStyleRange(TechnicalSuperiority, 1, 50, "technicalSuperiority");
            context.RequireNotNull(TechnicalPoints, "technicalPoints")?.Validate(context);
            context.RequireNotNull(Penalties, "penalties")?.Validate(context);
            context.RequireNotNull(Passivity?.GrecoRoman, "passivity.grecoRoman")?.Validate(context);
            context.RequireNotNull(Passivity?.ActivityClock, "passivity.activityClock")?.Validate(context);
            context.RequireNotNull(Challenge, "challenge")?.Validate(context);
            context.RequireNotNull(Procedure, "procedure")?.Validate(context);
            context.RequireNotNull(WeighIn, "weighIn")?.Validate(context);
            context.RequireNotNull(ClassificationPoints, "classificationPoints")?.Validate(context);
            context.RequireNotNull(TeamRanking, "teamRanking")?.Validate(context);
            return context.Errors;
        }

        /// <summary>Throws <see cref="RulesValidationException"/> listing all problems when invalid.</summary>
        public Ruleset EnsureValid()
        {
            IReadOnlyList<string> errors = Validate();
            if (errors.Count > 0)
            {
                throw new RulesValidationException($"Ruleset '{Id}'", errors);
            }

            return this;
        }

        private void ValidateAgeValues(ValidationContext context)
        {
            foreach (AgeCategory category in new[] { AgeCategory.U15, AgeCategory.U17, AgeCategory.U20, AgeCategory.U23, AgeCategory.Senior, AgeCategory.Veterans })
            {
                string name = category.ToString().ToLowerInvariant();
                context.RequireNotNull(AgeEligibility?.For(category), "ageEligibility." + name)?.Validate(context, "ageEligibility." + name);
                context.RequireNotNull(BoutDuration?.For(category), "boutDuration." + name)?.Validate(context, "boutDuration." + name);
            }
        }
    }
}
