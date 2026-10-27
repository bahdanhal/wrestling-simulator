using Newtonsoft.Json.Linq;
using Wrestling.Core.Wrestlers;
using Wrestling.Data.Json;
using Wrestling.Data.Rosters;
using Wrestling.Tests.Shared;

namespace Wrestling.Data.Tests.Rosters;

public class RosterLoaderTests
{
    private static string ValidRosterJson() => RosterLoader.ToJson(new RosterFile
    {
        Name = "Test roster",
        Wrestlers = { TestWrestlers.Create("wr-1"), TestWrestlers.Create("wr-2") },
    });

    [Fact]
    public void RoundTripsEveryField()
    {
        string json = ValidRosterJson();
        RosterFile roster = RosterLoader.FromJson(json, "roster.json");

        Assert.Equal(2, roster.Wrestlers.Count);
        Wrestler first = roster.Wrestlers[0];
        Assert.Equal("2004-03-14", first.BirthDate.ToString());
        Assert.Equal(50, first.Attributes[WrestlerAttribute.WrestlingIq]);
        Assert.Equal("76.5", first.Body.NaturalWeightKg.ToString());
        Assert.True(first.Repertoire[0].Signature);
        Assert.Equal(json, RosterLoader.ToJson(roster));
    }

    [Fact]
    public void AttributesUseCamelCaseNames()
    {
        JObject json = JObject.Parse(ValidRosterJson());
        JObject attributes = (JObject)json["wrestlers"]![0]!["attributes"]!;
        Assert.Equal(WrestlerAttributes.Count, attributes.Count);
        Assert.NotNull(attributes["wrestlingIq"]);
        Assert.NotNull(attributes["parTerreBottomDefence"]);
    }

    [Fact]
    public void RejectsMissingAndUnknownAttributes()
    {
        JObject missing = JObject.Parse(ValidRosterJson());
        ((JObject)missing["wrestlers"]![0]!["attributes"]!).Remove("speed");
        Assert.Contains("speed", Assert.Throws<ContentLoadException>(() => RosterLoader.FromJson(missing.ToString(), "r.json")).Message);

        JObject unknown = JObject.Parse(ValidRosterJson());
        unknown["wrestlers"]![0]!["attributes"]!["sped"] = 40;
        Assert.Contains("sped", Assert.Throws<ContentLoadException>(() => RosterLoader.FromJson(unknown.ToString(), "r.json")).Message);

        JObject outOfRange = JObject.Parse(ValidRosterJson());
        outOfRange["wrestlers"]![0]!["attributes"]!["speed"] = 140;
        Assert.Throws<ContentLoadException>(() => RosterLoader.FromJson(outOfRange.ToString(), "r.json"));
    }

    [Fact]
    public void RejectsInvalidDates()
    {
        JObject json = JObject.Parse(ValidRosterJson());
        json["wrestlers"]![0]!["birthDate"] = "2004-02-30";
        Assert.Throws<ContentLoadException>(() => RosterLoader.FromJson(json.ToString(), "r.json"));
    }

    [Fact]
    public void ReportsDuplicateIdsAndInvalidWrestlers()
    {
        JObject json = JObject.Parse(ValidRosterJson());
        json["wrestlers"]![1]!["id"] = "wr-1";
        json["wrestlers"]![0]!["condition"]!["morale"] = 150;
        json["wrestlers"]![0]!["style"] = "womens";

        string message = Assert.Throws<ContentLoadException>(() => RosterLoader.FromJson(json.ToString(), "r.json")).Message;
        Assert.Contains("used 2 times", message);
        Assert.Contains("morale", message);
        Assert.Contains("style", message);
    }
}
