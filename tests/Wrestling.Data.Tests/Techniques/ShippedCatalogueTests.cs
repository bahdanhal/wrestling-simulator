using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Core.Wrestlers;
using Wrestling.Data.Localization;
using Wrestling.Data.Techniques;

namespace Wrestling.Data.Tests.Techniques;

/// <summary>Checks the technique content that ships with the game.</summary>
public class ShippedCatalogueTests
{
    private static readonly TechniqueCatalog Catalog = TechniqueLoader.FromDirectory(TestContent.PathOf("techniques"));
    private static readonly LocalizationTable English = LocalizationTable.FromFile(TestContent.PathOf("localization/en.json"));

    [Fact]
    public void LoadsWithoutErrors()
    {
        Assert.True(Catalog.Count >= 50);
    }

    [Fact]
    public void EveryTechniqueHasAnEnglishName()
    {
        Assert.All(Catalog.All, t => Assert.True(English.Contains(t.NameKey), $"Missing English name for {t.Id}."));
    }

    [Fact]
    public void GrecoRomanHasNoLegAttacksOrTrips()
    {
        IEnumerable<TechniqueDefinition> greco = Catalog.AllowedFor(WrestlingStyle.GrecoRoman, AgeCategory.Senior);
        Assert.DoesNotContain(greco, t => t.Family == TechniqueFamily.LegAttacks || t.Family == TechniqueFamily.TripsAndSweeps);
    }

    [Theory]
    [InlineData(WrestlingStyle.GrecoRoman)]
    [InlineData(WrestlingStyle.Freestyle)]
    [InlineData(WrestlingStyle.Womens)]
    public void EveryStyleCoversEverySituation(WrestlingStyle style)
    {
        List<TechniqueDefinition> allowed = Catalog.AllowedFor(style, AgeCategory.Senior).ToList();
        Assert.Contains(allowed, t => t.Role == TechniqueRole.Attack && t.Start.Positions.Contains(StartPosition.NeutralTied));
        Assert.Contains(allowed, t => t.Role == TechniqueRole.Attack && t.Start.Positions.Contains(StartPosition.ParTerreTop) && t.Family == TechniqueFamily.ParTerreTurns);
        Assert.Contains(allowed, t => t.Role == TechniqueRole.Attack && t.Family == TechniqueFamily.ParTerreLifts);
        Assert.Contains(allowed, t => t.Start.Positions.Contains(StartPosition.ParTerreBottom));
        Assert.Contains(allowed, t => t.Role == TechniqueRole.Defence && t.Start.Positions.SequenceEqual(new[] { StartPosition.OpponentTechnique }));
        Assert.Contains(allowed, t => t.Role == TechniqueRole.Counter);
        Assert.Contains(allowed, t => t.Amplitude == Amplitude.GrandAmplitude);
    }

    [Fact]
    public void WomensWrestlingHasNoDoubleNelson()
    {
        Assert.DoesNotContain(Catalog.AllowedFor(WrestlingStyle.Womens, AgeCategory.Senior), t => t.Id.Contains("double_nelson"));
        Assert.DoesNotContain(Catalog.AllowedFor(WrestlingStyle.Freestyle, AgeCategory.U17), t => t.Id.Contains("double_nelson"));
        Assert.Contains(Catalog.AllowedFor(WrestlingStyle.Freestyle, AgeCategory.Senior), t => t.Id.Contains("double_nelson"));
    }

    [Fact]
    public void EveryCounterAndStandingDefenceIsUsedByAtLeastOneTechnique()
    {
        var referenced = new HashSet<string>(Catalog.All.SelectMany(t => t.Phases).SelectMany(p => p.CounterWindows).Select(w => w.Technique));
        IEnumerable<TechniqueDefinition> reactive = Catalog.All.Where(t => t.Role != TechniqueRole.Attack && !t.Start.Positions.Contains(StartPosition.ParTerreBottom));
        Assert.All(reactive, t => Assert.Contains(t.Id, referenced));
    }

    [Fact]
    public void AnimationKeysFollowTheNamingConvention()
    {
        foreach (TechniqueDefinition technique in Catalog.All)
        {
            string shortName = technique.Id.Split('.').Last();
            Assert.All(technique.Phases, p => Assert.Equal($"anim.{shortName}.{p.Id}", p.Animation));
        }
    }
}
