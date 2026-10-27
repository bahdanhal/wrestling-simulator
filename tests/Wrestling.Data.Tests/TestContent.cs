using Wrestling.Core.Rules;
using Wrestling.Data.Rulesets;

namespace Wrestling.Data.Tests;

/// <summary>Access to the real content files copied next to the test assembly.</summary>
public static class TestContent
{
    public const string CurrentRulesetId = "uww-2026-01";

    public static string PathOf(string relativePath) => Path.Combine(AppContext.BaseDirectory, "data", relativePath);

    public static string ReadRulesetJson() => File.ReadAllText(PathOf($"rulesets/{CurrentRulesetId}.json"));

    public static Ruleset LoadCurrentRuleset() => RulesetLoader.FromFile(PathOf($"rulesets/{CurrentRulesetId}.json"));
}
