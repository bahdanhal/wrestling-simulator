using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wrestling.Core.Wrestlers;
using Wrestling.Data.Json;

namespace Wrestling.Data.Rosters
{
    /// <summary>A roster pack: wrestlers that can be imported into a world (design section 13.7).</summary>
    public sealed class RosterFile
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; init; } = CurrentSchemaVersion;

        public string Name { get; init; } = string.Empty;

        public List<Wrestler> Wrestlers { get; init; } = new List<Wrestler>();
    }

    /// <summary>Loads and saves roster packs with full validation of every wrestler.</summary>
    public static class RosterLoader
    {
        public static RosterFile FromJson(string json, string sourceName)
        {
            RosterFile roster = ContentJson.Deserialize<RosterFile>(json, sourceName);
            var errors = new List<string>();
            if (roster.SchemaVersion != RosterFile.CurrentSchemaVersion)
            {
                errors.Add($"schemaVersion {roster.SchemaVersion} is not supported (expected {RosterFile.CurrentSchemaVersion}).");
            }

            if (string.IsNullOrWhiteSpace(roster.Name))
            {
                errors.Add("name must not be empty.");
            }

            foreach (IGrouping<string, Wrestler> duplicate in roster.Wrestlers.GroupBy(w => w.Id).Where(g => g.Count() > 1))
            {
                errors.Add($"wrestler id '{duplicate.Key}' is used {duplicate.Count()} times.");
            }

            foreach (Wrestler wrestler in roster.Wrestlers)
            {
                errors.AddRange(wrestler.Validate().Select(error => $"wrestler '{wrestler.Id}': {error}"));
            }

            if (errors.Count > 0)
            {
                throw new ContentLoadException(sourceName, "Roster is invalid:\n- " + string.Join("\n- ", errors));
            }

            return roster;
        }

        public static RosterFile FromFile(string path) => FromJson(File.ReadAllText(path), Path.GetFileName(path));

        public static string ToJson(RosterFile roster) => ContentJson.Serialize(roster);
    }
}
