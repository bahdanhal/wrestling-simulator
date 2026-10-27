using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Wrestlers;
using Wrestling.Tests.Shared;

namespace Wrestling.Core.Tests.Wrestlers;

public class WrestlerTests
{
    [Fact]
    public void ValidWrestler_HasNoErrors()
    {
        Assert.Empty(TestWrestlers.Create().Validate());
    }

    [Fact]
    public void AttributeGroups_CoverEveryAttribute()
    {
        Assert.Equal(34, WrestlerAttributes.Count);
        Assert.Equal(12, WrestlerAttributes.All.Count(a => WrestlerAttributes.GroupOf(a) == AttributeGroup.Physical));
        Assert.Equal(8, WrestlerAttributes.All.Count(a => WrestlerAttributes.GroupOf(a) == AttributeGroup.TechnicalAttack));
        Assert.Equal(7, WrestlerAttributes.All.Count(a => WrestlerAttributes.GroupOf(a) == AttributeGroup.TechnicalDefence));
        Assert.Equal(7, WrestlerAttributes.All.Count(a => WrestlerAttributes.GroupOf(a) == AttributeGroup.Mental));
    }

    [Fact]
    public void EveryTechniqueFamilyHasARatingAttribute()
    {
        foreach (TechniqueFamily family in Enum.GetValues<TechniqueFamily>())
        {
            Assert.Equal(AttributeGroup.TechnicalAttack, WrestlerAttributes.GroupOf(WrestlerAttributes.RatingOf(family)));
        }
    }

    [Theory]
    [InlineData(TechniqueFamily.LegAttacks, WrestlingStyle.GrecoRoman, false)]
    [InlineData(TechniqueFamily.TripsAndSweeps, WrestlingStyle.GrecoRoman, false)]
    [InlineData(TechniqueFamily.BodyLockAndLifts, WrestlingStyle.GrecoRoman, true)]
    [InlineData(TechniqueFamily.LegAttacks, WrestlingStyle.Womens, true)]
    [InlineData(TechniqueFamily.ParTerreLifts, WrestlingStyle.Freestyle, true)]
    public void FamilyLegality(TechniqueFamily family, WrestlingStyle style, bool legal)
    {
        Assert.Equal(legal, WrestlerAttributes.IsLegal(family, style));
    }

    [Fact]
    public void AttributeSet_RejectsOutOfRangeAndMissingValues()
    {
        var set = new AttributeSet();
        Assert.Throws<ArgumentOutOfRangeException>(() => set[WrestlerAttribute.Speed] = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => set[WrestlerAttribute.Speed] = 101);
        Assert.Throws<ArgumentException>(() => new AttributeSet(new Dictionary<WrestlerAttribute, int> { [WrestlerAttribute.Speed] = 10 }));

        AttributeSet clone = new AttributeSet(40).Clone();
        clone[WrestlerAttribute.Speed] = 41;
        Assert.Equal(40, clone[WrestlerAttribute.Agility]);
    }

    [Theory]
    [InlineData(Gender.Female, WrestlingStyle.Freestyle)]
    [InlineData(Gender.Female, WrestlingStyle.GrecoRoman)]
    [InlineData(Gender.Male, WrestlingStyle.Womens)]
    public void StyleMustMatchGender(Gender gender, WrestlingStyle style)
    {
        Assert.Contains(TestWrestlers.Create(gender: gender, style: style).Validate(), e => e.Contains("style"));
    }

    [Fact]
    public void AttributesCannotExceedPotential()
    {
        Wrestler wrestler = TestWrestlers.Create(attributeValue: 95);
        Assert.Contains(wrestler.Validate(), e => e.Contains("exceeds its potential"));
    }

    [Fact]
    public void ReportsDataProblems()
    {
        Wrestler wrestler = TestWrestlers.Create();
        wrestler.Repertoire.Add(new RepertoireEntry { TechniqueId = "technique.all.arm_drag", Proficiency = 0 });
        wrestler.Repertoire.Add(new RepertoireEntry { TechniqueId = "a", Proficiency = 10, Signature = true });
        wrestler.Repertoire.Add(new RepertoireEntry { TechniqueId = "b", Proficiency = 10, Signature = true });
        wrestler.Repertoire.Add(new RepertoireEntry { TechniqueId = "c", Proficiency = 10, Signature = true });
        wrestler.Condition.Morale = 120;
        wrestler.Condition.WalkAroundWeightKg = Fix64.FromInt(10);

        IReadOnlyList<string> errors = wrestler.Validate();
        Assert.Contains(errors, e => e.Contains("twice"));
        Assert.Contains(errors, e => e.Contains("signature"));
        Assert.Contains(errors, e => e.Contains("morale"));
        Assert.Contains(errors, e => e.Contains("walkAroundWeightKg"));
        Assert.Throws<RulesValidationException>(() => wrestler.EnsureValid());
    }

    [Fact]
    public void Archetype_DefaultsToAllRounderForEvenProfiles()
    {
        Assert.Equal(Archetype.AllRounder, TestWrestlers.Create().Archetype);
    }

    [Fact]
    public void Archetype_LegAttackSpecialistOnlyInFreestyleRules()
    {
        void Shooter(AttributeSet a)
        {
            a[WrestlerAttribute.LegAttacks] = 85;
            a[WrestlerAttribute.Explosiveness] = 75;
            a[WrestlerAttribute.Speed] = 75;
        }

        Assert.Equal(Archetype.LegAttackSpecialist, TestWrestlers.Create(adjust: Shooter).Archetype);
        Assert.NotEqual(Archetype.LegAttackSpecialist, TestWrestlers.Create(style: WrestlingStyle.GrecoRoman, adjust: Shooter).Archetype);
    }

    [Fact]
    public void Archetype_GrecoLifter()
    {
        Wrestler lifter = TestWrestlers.Create(style: WrestlingStyle.GrecoRoman, adjust: a =>
        {
            a[WrestlerAttribute.BodyLockAndLifts] = 85;
            a[WrestlerAttribute.UpperBodyStrength] = 80;
        });
        Assert.Equal(Archetype.LiftSpecialist, lifter.Archetype);
    }
}
