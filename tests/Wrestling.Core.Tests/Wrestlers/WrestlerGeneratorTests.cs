using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Core.Wrestlers;
using Wrestling.Data.Techniques;

namespace Wrestling.Core.Tests.Wrestlers;

public class WrestlerGeneratorTests
{
    private static readonly TechniqueCatalog Catalog = TechniqueLoader.FromDirectory(Path.Combine(AppContext.BaseDirectory, "data", "techniques"));

    private static readonly NamePool Names = new()
    {
        SchemaVersion = 1,
        Id = "test",
        Male = new[] { "Arin" },
        Female = new[] { "Aida" },
        Family = new[] { "Aras", "Borov" },
    };

    [Theory]
    [InlineData(WrestlingStyle.Freestyle)]
    [InlineData(WrestlingStyle.GrecoRoman)]
    [InlineData(WrestlingStyle.Womens)]
    public void GeneratesValidWrestlersWithTheStyleRepertoire(WrestlingStyle style)
    {
        Wrestler wrestler = WrestlerGenerator.Generate("w", style, 60, 12, Catalog, Names, new Pcg32(1, 1));
        Assert.Empty(wrestler.Validate());
        Assert.Equal(style, wrestler.Style);
        Assert.Equal(style == WrestlingStyle.Womens ? Gender.Female : Gender.Male, wrestler.Gender);
        Assert.Equal(style == WrestlingStyle.Womens ? "Aida" : "Arin", wrestler.Name.Given);
        Assert.Equal(Catalog.AllowedFor(style, AgeCategory.Senior).Count(), wrestler.Repertoire.Count);
        Assert.All(WrestlerAttributes.All, a => Assert.InRange(wrestler.Attributes[a], 48, 72));
    }

    [Fact]
    public void IsDeterministicForTheSameRandomState()
    {
        Wrestler a = WrestlerGenerator.Generate("w", WrestlingStyle.Freestyle, 55, 10, Catalog, Names, new Pcg32(7, 3));
        Wrestler b = WrestlerGenerator.Generate("w", WrestlingStyle.Freestyle, 55, 10, Catalog, Names, new Pcg32(7, 3));
        Assert.Equal(a.Name.Full, b.Name.Full);
        Assert.Equal(a.Body.HeightCm, b.Body.HeightCm);
        Assert.All(WrestlerAttributes.All, attribute => Assert.Equal(a.Attributes[attribute], b.Attributes[attribute]));
        Assert.Equal(a.Repertoire.Select(r => r.Proficiency), b.Repertoire.Select(r => r.Proficiency));
    }

    [Fact]
    public void RejectsOutOfRangeArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WrestlerGenerator.Generate("w", WrestlingStyle.Freestyle, 0, 10, Catalog, Names, new Pcg32(1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => WrestlerGenerator.Generate("w", WrestlingStyle.Freestyle, 96, 10, Catalog, Names, new Pcg32(1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => WrestlerGenerator.Generate("w", WrestlingStyle.Freestyle, 50, -1, Catalog, Names, new Pcg32(1, 1)));
    }
}
