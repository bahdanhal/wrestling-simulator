using System.Collections.Generic;
using System.IO;
using Wrestling.Core.Rules;
using Wrestling.Data.Json;

namespace Wrestling.Data.Rulesets
{
    /// <summary>Loads and validates ruleset content files.</summary>
    public static class RulesetLoader
    {
        public static Ruleset FromJson(string json, string sourceName)
        {
            Ruleset ruleset = ContentJson.Deserialize<Ruleset>(json, sourceName);
            IReadOnlyList<string> errors = ruleset.Validate();
            if (errors.Count > 0)
            {
                throw new ContentLoadException(sourceName, new RulesValidationException($"Ruleset '{ruleset.Id}'", errors).Message);
            }

            return ruleset;
        }

        public static Ruleset FromFile(string path) => FromJson(File.ReadAllText(path), Path.GetFileName(path));
    }
}
