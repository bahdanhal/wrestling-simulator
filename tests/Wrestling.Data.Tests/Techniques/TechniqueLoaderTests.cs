using Newtonsoft.Json.Linq;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Data.Json;
using Wrestling.Data.Techniques;
using Wrestling.Tests.Shared;

namespace Wrestling.Data.Tests.Techniques;

public class TechniqueLoaderTests
{
    private static (string, string) File(TechniqueDefinition definition) => (definition.Id + ".json", ContentJson.Serialize(definition));

    private static (string, string) File(JObject json) => ((string)json["id"]! + ".json", json.ToString());

    [Fact]
    public void LoadsAValidCatalogue()
    {
        TechniqueCatalog catalog = TechniqueLoader.FromFiles(new[] { File(TestTechniques.HipToss()), File(TestTechniques.HipBlock()), File(TestTechniques.DoubleLeg()) });

        Assert.Equal(3, catalog.Count);
        TechniqueDefinition hipToss = catalog.Get("technique.all.hip_toss_overhook");
        Assert.Equal(2, hipToss.Phases.Count);
        Assert.Equal("technique.all.hip_block", hipToss.Phases[0].CounterWindows[0].Technique);
        Assert.Null(catalog.Get("technique.all.hip_block").Family);
    }

    [Fact]
    public void AllowedFor_FiltersByStyleAndAge()
    {
        TechniqueCatalog catalog = TechniqueCatalog.Create(new[] { TestTechniques.HipToss(), TestTechniques.HipBlock(), TestTechniques.DoubleLeg() });
        Assert.DoesNotContain(catalog.AllowedFor(WrestlingStyle.GrecoRoman, AgeCategory.Senior), t => t.Id.Contains("double_leg"));
        Assert.Contains(catalog.AllowedFor(WrestlingStyle.Womens, AgeCategory.U17), t => t.Id.Contains("double_leg"));
    }

    [Fact]
    public void FileNameMustMatchId()
    {
        var files = new[] { ("wrong.json", ContentJson.Serialize(TestTechniques.HipBlock())) };
        Assert.Contains("must match", Assert.Throws<ContentLoadException>(() => TechniqueLoader.FromFiles(files)).Message);
    }

    [Fact]
    public void MissingCounterIsReported()
    {
        var files = new[] { File(TestTechniques.HipToss("technique.all.does_not_exist")) };
        Assert.Contains("does not exist", Assert.Throws<ContentLoadException>(() => TechniqueLoader.FromFiles(files)).Message);
    }

    [Fact]
    public void CounterWindowMustPointAtACounterOrDefence()
    {
        var files = new[] { File(TestTechniques.HipToss("technique.fs.double_leg_blast")), File(TestTechniques.DoubleLeg()) };
        string message = Assert.Throws<ContentLoadException>(() => TechniqueLoader.FromFiles(files)).Message;
        Assert.Contains("is an attack", message);
    }

    [Fact]
    public void GrecoCannotUseLegFamiliesOrLegGrips()
    {
        JObject legs = JObject.FromObject(JObject.Parse(ContentJson.Serialize(TestTechniques.DoubleLeg())));
        legs["styles"] = new JArray("grecoRoman");
        legs["start"]!["requiredGrips"] = new JArray(new JObject { ["type"] = "doubleLeg", ["side"] = "any" });

        string message = Assert.Throws<ContentLoadException>(() => TechniqueLoader.FromFiles(new[] { File(legs) })).Message;
        Assert.Contains("family LegAttacks is illegal in GrecoRoman", message);
        Assert.Contains("required grip DoubleLeg is illegal in GrecoRoman", message);
    }

    [Fact]
    public void PhaseRulesAreEnforced()
    {
        JObject json = JObject.Parse(ContentJson.Serialize(TestTechniques.HipToss()));
        JObject entry = (JObject)json["phases"]![0]!;
        entry["attributes"]![0]!["percent"] = 50;         // percentages now sum to 110
        entry["endStates"] = new JArray("flank");          // end states on a non-final phase
        entry["counterWindows"]![0]!["toTick"] = 99;       // window past the phase
        ((JObject)json["phases"]![1]!)["outcomes"] = new JArray("stalled");

        string message = Assert.Throws<ContentLoadException>(() => TechniqueLoader.FromFiles(new[] { File(json), File(TestTechniques.HipBlock()) })).Message;
        Assert.Contains("sum to", message);
        Assert.Contains("only the final phase has end states", message);
        Assert.Contains("must lie within the phase", message);
        Assert.Contains("outcomes must include clean", message);
    }

    [Fact]
    public void DefencesMustStartInsideTheOpponentsTechnique()
    {
        JObject json = JObject.Parse(ContentJson.Serialize(TestTechniques.HipBlock()));
        json["start"]!["positions"] = new JArray("neutralTied");
        Assert.Contains("must be able to start inside", Assert.Throws<ContentLoadException>(() => TechniqueLoader.FromFiles(new[] { File(json) })).Message);
    }

    [Fact]
    public void DuplicateIdsAreRejected()
    {
        Assert.Throws<RulesValidationException>(() => TechniqueCatalog.Create(new[] { TestTechniques.HipBlock(), TestTechniques.HipBlock() }));
    }

    [Fact]
    public void ForbiddenAges()
    {
        TechniqueDefinition definition = TestTechniques.HipBlock();
        var restricted = new TechniqueDefinition
        {
            SchemaVersion = 1,
            Id = "technique.all.double_nelson_side",
            NameKey = "x",
            Role = TechniqueRole.Attack,
            Family = Wrestling.Core.Wrestlers.TechniqueFamily.ParTerreTurns,
            Styles = new[] { WrestlingStyle.Freestyle, WrestlingStyle.GrecoRoman },
            ForbiddenForAges = new[] { AgeCategory.U15, AgeCategory.U17 },
            Start = new StartRequirements { Positions = new[] { StartPosition.ParTerreTop }, MinimumOwnLevel = Wrestling.Core.Numerics.Fix64.Zero, MaximumOwnLevel = Wrestling.Core.Numerics.Fix64.One },
            Phases = definition.Phases,
            Gesture = definition.Gesture,
        };

        Assert.Empty(restricted.Validate());
        Assert.False(restricted.IsAllowed(WrestlingStyle.Freestyle, AgeCategory.U17));
        Assert.True(restricted.IsAllowed(WrestlingStyle.Freestyle, AgeCategory.Senior));
        Assert.False(restricted.IsAllowed(WrestlingStyle.Womens, AgeCategory.Senior));
    }
}
