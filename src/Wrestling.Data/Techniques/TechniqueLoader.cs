using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Data.Json;

namespace Wrestling.Data.Techniques
{
    /// <summary>Loads technique files (one technique per JSON file) into a validated catalogue.</summary>
    public static class TechniqueLoader
    {
        public static TechniqueDefinition FromJson(string json, string sourceName) => ContentJson.Deserialize<TechniqueDefinition>(json, sourceName);

        /// <summary>Loads every <c>*.json</c> file under <paramref name="directory"/> in ordinal path order.</summary>
        public static TechniqueCatalog FromDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"Technique directory '{directory}' does not exist.");
            }

            IEnumerable<string> files = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal);
            return FromFiles(files.Select(path => (Path.GetFileName(path), File.ReadAllText(path))));
        }

        public static TechniqueCatalog FromFiles(IEnumerable<(string SourceName, string Json)> files)
        {
            var definitions = new List<TechniqueDefinition>();
            foreach ((string sourceName, string json) in files)
            {
                TechniqueDefinition definition = FromJson(json, sourceName);
                string expectedFile = definition.Id + ".json";
                if (!string.Equals(sourceName, expectedFile, StringComparison.Ordinal))
                {
                    throw new ContentLoadException(sourceName, $"File name must match the technique id ('{expectedFile}').");
                }

                definitions.Add(definition);
            }

            try
            {
                return TechniqueCatalog.Create(definitions);
            }
            catch (RulesValidationException exception)
            {
                throw new ContentLoadException("techniques", exception.Message, exception);
            }
        }
    }
}
