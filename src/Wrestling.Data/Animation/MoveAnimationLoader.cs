using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wrestling.Core.Techniques;
using Wrestling.Data.Json;

namespace Wrestling.Data.Animation
{
    /// <summary>Loads <c>data/animation/*.json</c> (one file per technique) into a validated library.</summary>
    public static class MoveAnimationLoader
    {
        public static MoveAnimationFile FromJson(string json, string sourceName) => ContentJson.Deserialize<MoveAnimationFile>(json, sourceName);

        public static MoveAnimationLibrary FromDirectory(string directory, TechniqueCatalog catalog)
        {
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"Animation directory '{directory}' does not exist.");
            }

            var files = new List<MoveAnimationFile>();
            foreach (string path in Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                string sourceName = Path.GetFileName(path);
                MoveAnimationFile file = FromJson(File.ReadAllText(path), sourceName);
                if (!string.Equals(sourceName, file.Technique + ".json", StringComparison.Ordinal))
                {
                    throw new ContentLoadException(sourceName, $"File name must match the technique id ('{file.Technique}.json').");
                }

                files.Add(file);
            }

            try
            {
                return MoveAnimationLibrary.Create(files, catalog);
            }
            catch (ArgumentException exception)
            {
                throw new ContentLoadException("animation", exception.Message, exception);
            }
        }
    }
}
