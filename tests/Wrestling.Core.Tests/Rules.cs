using Wrestling.Core.Rules;
using Wrestling.Data.Rulesets;

namespace Wrestling.Core.Tests;

/// <summary>The shipped ruleset, loaded once for all tests.</summary>
internal static class Rules
{
    private static readonly Lazy<Ruleset> CurrentRuleset = new Lazy<Ruleset>(
        () => RulesetLoader.FromFile(Path.Combine(AppContext.BaseDirectory, "data", "rulesets", "uww-2026-01.json")));

    public static Ruleset Current => CurrentRuleset.Value;
}
