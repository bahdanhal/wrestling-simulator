using System;
using System.Collections.Generic;
using System.IO;
using Wrestling.Data.Json;

namespace Wrestling.Data.Localization
{
    /// <summary>A language's user-facing strings, by key (<c>data/localization/&lt;language&gt;.json</c>).</summary>
    public sealed class LocalizationFile
    {
        public int SchemaVersion { get; init; }

        /// <summary>Language code, for example "en".</summary>
        public string Language { get; init; } = string.Empty;

        public Dictionary<string, string> Strings { get; init; } = new Dictionary<string, string>();
    }

    /// <summary>Loaded strings for one language.</summary>
    public sealed class LocalizationTable
    {
        private readonly Dictionary<string, string> _strings;

        private LocalizationTable(string language, Dictionary<string, string> strings)
        {
            Language = language;
            _strings = strings;
        }

        public string Language { get; }

        public int Count => _strings.Count;

        public bool Contains(string key) => _strings.ContainsKey(key);

        /// <summary>The string for <paramref name="key"/>; throws for unknown keys so missing text is caught in testing.</summary>
        public string Get(string key) =>
            _strings.TryGetValue(key, out string? value) ? value : throw new KeyNotFoundException($"No '{Language}' string for '{key}'.");

        public static LocalizationTable FromJson(string json, string sourceName)
        {
            LocalizationFile file = ContentJson.Deserialize<LocalizationFile>(json, sourceName);
            var errors = new List<string>();
            if (file.SchemaVersion != 1)
            {
                errors.Add($"schemaVersion {file.SchemaVersion} is not supported.");
            }

            if (string.IsNullOrWhiteSpace(file.Language))
            {
                errors.Add("language must not be empty.");
            }

            foreach (KeyValuePair<string, string> entry in file.Strings)
            {
                if (string.IsNullOrWhiteSpace(entry.Value))
                {
                    errors.Add($"'{entry.Key}' has no text.");
                }
            }

            if (errors.Count > 0)
            {
                throw new ContentLoadException(sourceName, string.Join(Environment.NewLine, errors));
            }

            return new LocalizationTable(file.Language, new Dictionary<string, string>(file.Strings, StringComparer.Ordinal));
        }

        public static LocalizationTable FromFile(string path) => FromJson(File.ReadAllText(path), Path.GetFileName(path));
    }
}
