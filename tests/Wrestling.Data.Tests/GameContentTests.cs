namespace Wrestling.Data.Tests;

public class GameContentTests
{
    [Fact]
    public void LoadsAllShippedContent()
    {
        GameContent content = GameContent.Load(TestContent.PathOf(string.Empty));
        Assert.Equal(GameContent.DefaultRulesetId, content.Rules.Id);
        Assert.Empty(content.Tuning.Validate());
        Assert.True(content.Techniques.Count >= 50);
        Assert.Equal("en", content.English.Language);
        Assert.Equal(GameContent.DefaultNamePoolId, content.Names.Id);
        // One clip per phase, plus the clips counter windows choose for a counter's first phase.
        int entries = content.Techniques.All.Sum(t => Wrestling.Data.Animation.MoveAnimationLibrary.EntryClips(content.Techniques, t.Id).Count());
        Assert.Equal(content.Techniques.All.Sum(t => t.Phases.Count) + entries, content.Animations.Count);
    }

    [Fact]
    public void FindsTheDataFolderFromBelow()
    {
        string deep = Path.Combine(AppContext.BaseDirectory, "data", "techniques");
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "data"), GameContent.FindDataDirectory(deep));
        Assert.Null(GameContent.FindDataDirectory(Path.GetTempPath()));
    }

    [Fact]
    public void ReportsAMissingFolder()
    {
        Assert.Throws<DirectoryNotFoundException>(() => GameContent.Load(Path.Combine(Path.GetTempPath(), "no-such-wrestling-data")));
    }
}
