using System;
using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Calendar;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Wrestlers
{
    public enum Gender
    {
        Male,
        Female,
    }

    public enum Frame
    {
        Light,
        Medium,
        Heavy,
    }

    public enum Side
    {
        Left,
        Right,
    }

    public enum TechniqueSide
    {
        Left,
        Right,
        Both,
    }

    public enum BodyPart
    {
        Neck,
        Shoulder,
        Elbow,
        HandAndFingers,
        Ribs,
        Back,
        Knee,
        Ankle,
        Ear,
    }

    public sealed class PersonName
    {
        public string Given { get; init; } = string.Empty;

        public string Family { get; init; } = string.Empty;

        public string Full => $"{Given} {Family}";

        public override string ToString() => Full;
    }

    /// <summary>Physical build (design section 4.1). Fixed after growth ends, except natural weight.</summary>
    public sealed class BodyProfile
    {
        public int HeightCm { get; init; }

        public int WingspanCm { get; init; }

        /// <summary>Leg length as a fraction of height (about 0.42–0.52).</summary>
        public Fix64 LegRatio { get; init; }

        public Frame Frame { get; init; }

        /// <summary>Healthy body weight from frame and training; changes slowly over a career.</summary>
        public Fix64 NaturalWeightKg { get; set; }

        public Side DominantSide { get; init; }
    }

    /// <summary>Traits the player never sees directly (design section 4.2, "Hidden").</summary>
    public sealed class HiddenTraits
    {
        /// <summary>Development ceiling per attribute; current attributes never exceed it.</summary>
        public AttributeSet Potential { get; init; } = new AttributeSet(WrestlerAttributes.Maximum);

        /// <summary>Injury risk multiplier, 0.5 (robust) to 2.0 (fragile).</summary>
        public Fix64 InjuryProneness { get; init; } = Fix64.One;

        /// <summary>How well performance survives a weight cut, 0.5 to 1.5.</summary>
        public Fix64 WeightCutTolerance { get; init; } = Fix64.One;

        public int Professionalism { get; init; } = 50;

        public int Adaptability { get; init; } = 50;

        /// <summary>Age of peak physical attributes.</summary>
        public int PeakAge { get; init; } = 27;
    }

    /// <summary>A known technique and how well the wrestler executes it (design section 4.3).</summary>
    public sealed class RepertoireEntry
    {
        public string TechniqueId { get; init; } = string.Empty;

        public int Proficiency { get; set; }

        public TechniqueSide Side { get; init; }

        public bool Signature { get; set; }
    }

    public sealed class Injury
    {
        public BodyPart BodyPart { get; init; }

        /// <summary>1 (minor) to 5 (season-ending).</summary>
        public int Severity { get; init; }

        public GameDate Since { get; init; }

        public GameDate ExpectedRecovery { get; set; }

        public bool IsActiveOn(GameDate date) => date < ExpectedRecovery;
    }

    /// <summary>State that changes week to week (design section 4.4).</summary>
    public sealed class Condition
    {
        public Fix64 WalkAroundWeightKg { get; set; }

        /// <summary>-10 to +10.</summary>
        public int Form { get; set; }

        /// <summary>0 to 100.</summary>
        public int Morale { get; set; } = 60;

        public List<Injury> Injuries { get; init; } = new List<Injury>();
    }

    /// <summary>A wrestler: identity, body, attributes, hidden traits, repertoire and condition.</summary>
    public sealed class Wrestler
    {
        public const int MaximumSignatureTechniques = 3;

        public string Id { get; init; } = string.Empty;

        public PersonName Name { get; init; } = new PersonName();

        /// <summary>Three-letter country code as used by UWW (for example "TUR", "USA").</summary>
        public string Country { get; init; } = string.Empty;

        public Gender Gender { get; init; }

        public WrestlingStyle Style { get; set; }

        public GameDate BirthDate { get; init; }

        public BodyProfile Body { get; init; } = new BodyProfile();

        public AttributeSet Attributes { get; init; } = new AttributeSet();

        public HiddenTraits Hidden { get; init; } = new HiddenTraits();

        public List<RepertoireEntry> Repertoire { get; init; } = new List<RepertoireEntry>();

        public Condition Condition { get; init; } = new Condition();

        public Archetype Archetype => ArchetypeClassifier.Classify(Attributes, Style);

        public static IReadOnlyList<WrestlingStyle> StylesFor(Gender gender) => gender == Gender.Female
            ? new[] { WrestlingStyle.Womens }
            : new[] { WrestlingStyle.GrecoRoman, WrestlingStyle.Freestyle };

        public RepertoireEntry? Technique(string techniqueId) => Repertoire.FirstOrDefault(r => r.TechniqueId == techniqueId);

        /// <summary>Every problem with this wrestler's data; empty when valid.</summary>
        public IReadOnlyList<string> Validate()
        {
            var context = new ValidationContext();
            context.RequireNotEmpty(Id, "id");
            context.RequireNotEmpty(Name?.Given, "name.given");
            context.RequireNotEmpty(Name?.Family, "name.family");
            if (Country == null || Country.Length != 3 || Country.Any(c => c < 'A' || c > 'Z'))
            {
                context.Error("country must be a three-letter upper-case code.");
            }

            if (!StylesFor(Gender).Contains(Style))
            {
                context.Error($"style {Style} is not open to {Gender} wrestlers.");
            }

            if (Body == null || Attributes == null || Hidden == null || Repertoire == null || Condition == null)
            {
                context.Error("body, attributes, hidden, repertoire and condition are required.");
                return context.Errors;
            }

            context.RequireRange(Body.HeightCm, 130, 220, "body.heightCm");
            context.RequireRange(Body.WingspanCm, 120, 240, "body.wingspanCm");
            if (Body.LegRatio < Fix64.Parse("0.40") || Body.LegRatio > Fix64.Parse("0.55"))
            {
                context.Error("body.legRatio must be between 0.40 and 0.55.");
            }

            RequireWeight(context, Body.NaturalWeightKg, "body.naturalWeightKg");
            RequireWeight(context, Condition.WalkAroundWeightKg, "condition.walkAroundWeightKg");

            foreach (WrestlerAttribute attribute in WrestlerAttributes.All)
            {
                if (Attributes[attribute] > Hidden.Potential[attribute])
                {
                    context.Error($"attributes.{attribute} ({Attributes[attribute]}) exceeds its potential ({Hidden.Potential[attribute]}).");
                }
            }

            if (Hidden.InjuryProneness < Fix64.Half || Hidden.InjuryProneness > Fix64.FromInt(2))
            {
                context.Error("hidden.injuryProneness must be between 0.5 and 2.0.");
            }

            if (Hidden.WeightCutTolerance < Fix64.Half || Hidden.WeightCutTolerance > Fix64.Parse("1.5"))
            {
                context.Error("hidden.weightCutTolerance must be between 0.5 and 1.5.");
            }

            context.RequireRange(Hidden.Professionalism, 1, 100, "hidden.professionalism");
            context.RequireRange(Hidden.Adaptability, 1, 100, "hidden.adaptability");
            context.RequireRange(Hidden.PeakAge, 22, 32, "hidden.peakAge");

            if (Repertoire.Select(r => r.TechniqueId).Distinct().Count() != Repertoire.Count)
            {
                context.Error("repertoire contains a technique twice.");
            }

            foreach (RepertoireEntry entry in Repertoire)
            {
                context.RequireNotEmpty(entry.TechniqueId, "repertoire[].techniqueId");
                context.RequireRange(entry.Proficiency, 1, 100, $"repertoire[{entry.TechniqueId}].proficiency");
            }

            if (Repertoire.Count(r => r.Signature) > MaximumSignatureTechniques)
            {
                context.Error($"at most {MaximumSignatureTechniques} signature techniques are allowed.");
            }

            context.RequireRange(Condition.Form, -10, 10, "condition.form");
            context.RequireRange(Condition.Morale, 0, 100, "condition.morale");
            foreach (Injury injury in Condition.Injuries)
            {
                context.RequireRange(injury.Severity, 1, 5, "condition.injuries[].severity");
                if (injury.ExpectedRecovery < injury.Since)
                {
                    context.Error("condition.injuries[]: expected recovery is before the injury date.");
                }
            }

            return context.Errors;
        }

        public Wrestler EnsureValid()
        {
            IReadOnlyList<string> errors = Validate();
            if (errors.Count > 0)
            {
                throw new RulesValidationException($"Wrestler '{Id}'", errors);
            }

            return this;
        }

        private static void RequireWeight(ValidationContext context, Fix64 weight, string path)
        {
            if (weight < Fix64.FromInt(25) || weight > Fix64.FromInt(200))
            {
                context.Error($"{path} must be between 25 and 200 kg.");
            }
        }
    }
}
