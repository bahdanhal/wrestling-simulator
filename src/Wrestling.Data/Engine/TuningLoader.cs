using System.Collections.Generic;
using System.IO;
using Wrestling.Core.Match;
using Wrestling.Data.Json;

namespace Wrestling.Data.Engine
{
    /// <summary>Loads and validates <c>data/engine/tuning.json</c>.</summary>
    public static class TuningLoader
    {
        public static EngineTuning FromJson(string json, string sourceName)
        {
            EngineTuning tuning = ContentJson.Deserialize<EngineTuning>(json, sourceName);
            IReadOnlyList<string> errors = tuning.Validate();
            if (errors.Count > 0)
            {
                throw new ContentLoadException(sourceName, "Engine tuning is invalid:\n- " + string.Join("\n- ", errors));
            }

            return tuning;
        }

        public static EngineTuning FromFile(string path) => FromJson(File.ReadAllText(path), Path.GetFileName(path));
    }
}
