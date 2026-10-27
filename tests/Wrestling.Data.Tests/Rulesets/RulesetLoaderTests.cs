using Newtonsoft.Json.Linq;
using Wrestling.Core.Rules;
using Wrestling.Data.Json;
using Wrestling.Data.Rulesets;

namespace Wrestling.Data.Tests.Rulesets;

/// <summary>The loader must reject incomplete or mistyped content instead of silently using defaults.</summary>
public class RulesetLoaderTests
{
    [Fact]
    public void RejectsMissingProperty()
    {
        JObject json = JObject.Parse(TestContent.ReadRulesetJson());
        ((JObject)json["technicalPoints"]!).Remove("takedown");

        var exception = Assert.Throws<ContentLoadException>(() => RulesetLoader.FromJson(json.ToString(), "test.json"));
        Assert.Contains("takedown", exception.Message);
        Assert.Equal("test.json", exception.SourceName);
    }

    [Fact]
    public void RejectsUnknownProperty()
    {
        JObject json = JObject.Parse(TestContent.ReadRulesetJson());
        ((JObject)json["technicalPoints"]!)["takedwon"] = 2;

        var exception = Assert.Throws<ContentLoadException>(() => RulesetLoader.FromJson(json.ToString(), "test.json"));
        Assert.Contains("takedwon", exception.Message);
    }

    [Fact]
    public void RejectsUnknownEnumValue()
    {
        JObject json = JObject.Parse(TestContent.ReadRulesetJson());
        json["weightCategories"]!["mayMoveUpOneCategory"] = new JArray("senior", "juniors");

        Assert.Throws<ContentLoadException>(() => RulesetLoader.FromJson(json.ToString(), "test.json"));
    }

    [Fact]
    public void RejectsNullForRequiredObject()
    {
        JObject json = JObject.Parse(TestContent.ReadRulesetJson());
        json["challenge"] = null;

        Assert.Throws<ContentLoadException>(() => RulesetLoader.FromJson(json.ToString(), "test.json"));
    }

    [Fact]
    public void ReportsAllValidationErrorsTogether()
    {
        JObject json = JObject.Parse(TestContent.ReadRulesetJson());
        json["technicalSuperiority"]!["grecoRoman"] = 0;
        json["weightCategories"]!["senior"]!["freestyle"]!["limitsKg"] = new JArray(57, 61, 61);
        json["mat"]!["centralSurfaceDiameter"] = 6.5;

        var exception = Assert.Throws<ContentLoadException>(() => RulesetLoader.FromJson(json.ToString(), "test.json"));
        Assert.Contains("technicalSuperiority.grecoRoman", exception.Message);
        Assert.Contains("weightCategories.senior.freestyle.limitsKg must be strictly increasing", exception.Message);
        Assert.Contains("mat:", exception.Message);
    }

    [Fact]
    public void ReadsFixedPointValuesExactly()
    {
        JObject json = JObject.Parse(TestContent.ReadRulesetJson());
        json["procedure"]!["fallConfirmationSeconds"] = 0.1;

        Ruleset rules = RulesetLoader.FromJson(json.ToString(), "test.json");
        Assert.Equal(429496730L, rules.Procedure.FallConfirmationSeconds.Raw);
    }

    [Fact]
    public void RoundTripsThroughSerialization()
    {
        Ruleset original = TestContent.LoadCurrentRuleset();
        string json = ContentJson.Serialize(original);
        Ruleset reloaded = RulesetLoader.FromJson(json, "roundtrip.json");

        Assert.Equal(ContentJson.Serialize(original), ContentJson.Serialize(reloaded));
    }
}
