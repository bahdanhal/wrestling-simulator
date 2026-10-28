using Wrestling.Core.Wrestlers;
using Wrestling.Data.Json;
using Wrestling.Data.Names;

namespace Wrestling.Data.Tests.Names;

public class NamePoolLoaderTests
{
    [Fact]
    public void LoadsTheShippedPool()
    {
        NamePool pool = NamePoolLoader.FromFile(TestContent.PathOf("names/fictional.json"));
        Assert.Equal("fictional", pool.Id);
        Assert.NotEmpty(pool.Male);
        Assert.NotEmpty(pool.Female);
        Assert.NotEmpty(pool.Family);
    }

    [Fact]
    public void RejectsAnEmptyList()
    {
        const string json = """{ "schemaVersion": 1, "id": "x", "male": [], "female": ["A"], "family": ["B"] }""";
        Assert.Throws<ContentLoadException>(() => NamePoolLoader.FromJson(json, "x.json"));
    }

    [Fact]
    public void RejectsABlankName()
    {
        const string json = """{ "schemaVersion": 1, "id": "x", "male": ["A"], "female": [" "], "family": ["B"] }""";
        Assert.Throws<ContentLoadException>(() => NamePoolLoader.FromJson(json, "x.json"));
    }

    [Fact]
    public void RejectsUnknownFields()
    {
        const string json = """{ "schemaVersion": 1, "id": "x", "male": ["A"], "female": ["C"], "family": ["B"], "extra": 1 }""";
        Assert.Throws<ContentLoadException>(() => NamePoolLoader.FromJson(json, "x.json"));
    }
}
