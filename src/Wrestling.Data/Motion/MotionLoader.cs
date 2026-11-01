using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wrestling.Data.Animation;
using Wrestling.Data.Json;

namespace Wrestling.Data.Motion
{
    /// <summary>Loads <c>data/motion/index.json</c> and <c>data/motion/takes/*.json</c> into a validated library.</summary>
    public static class MotionLoader
    {
        public const string IndexFile = "index.json";
        public const string TakesFolder = "takes";

        public static MotionTake TakeFromJson(string json, string sourceName) => ContentJson.Deserialize<MotionTake>(json, sourceName);

        public static MotionLibrary FromDirectory(string directory, MoveAnimationLibrary animations)
        {
            string indexPath = Path.Combine(directory, IndexFile);
            if (!File.Exists(indexPath))
            {
                throw new FileNotFoundException($"Motion index '{indexPath}' does not exist.");
            }

            MotionIndexFile index = ContentJson.Deserialize<MotionIndexFile>(File.ReadAllText(indexPath), IndexFile);
            var takes = new List<MotionTake>();
            string takesPath = Path.Combine(directory, TakesFolder);
            if (Directory.Exists(takesPath))
            {
                foreach (string path in Directory.GetFiles(takesPath, "*.json").OrderBy(p => p, StringComparer.Ordinal))
                {
                    string name = Path.GetFileName(path);
                    MotionTake take = TakeFromJson(File.ReadAllText(path), name);
                    if (!string.Equals(name, take.Id + ".json", StringComparison.Ordinal))
                    {
                        throw new ContentLoadException(name, $"File name must match the take id ('{take.Id}.json').");
                    }

                    takes.Add(take);
                }
            }

            var contacts = new List<MotionContactSet>();
            string contactsPath = Path.Combine(directory, MotionContactLoader.ContactsFolder);
            if (Directory.Exists(contactsPath))
            {
                foreach (string path in Directory.GetFiles(contactsPath, "*.json").OrderBy(p => p, StringComparer.Ordinal))
                {
                    string name = Path.GetFileName(path);
                    MotionContactSet set = ContentJson.Deserialize<MotionContactSet>(File.ReadAllText(path), name);
                    if (!string.Equals(name, set.Take + ".json", StringComparison.Ordinal))
                        throw new ContentLoadException(name, $"File name must match the contact take id ('{set.Take}.json').");
                    contacts.Add(set);
                }
            }

            try
            {
                return MotionLibrary.Create(index, takes, new HashSet<string>(animations.All.Select(c => c.Id), StringComparer.Ordinal), contacts);
            }
            catch (ArgumentException exception)
            {
                throw new ContentLoadException("motion", exception.Message, exception);
            }
        }

        public static void WriteTake(MotionTake take, string directory)
        {
            string folder = Path.Combine(directory, TakesFolder);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, take.Id + ".json"), ContentJson.Serialize(take) + "\n");
        }

        /// <summary>Adds or replaces the recorded motion of one phase in the index.</summary>
        public static void SetPhase(string directory, MotionPhase phase)
        {
            string indexPath = Path.Combine(directory, IndexFile);
            MotionIndexFile index = ContentJson.Deserialize<MotionIndexFile>(File.ReadAllText(indexPath), IndexFile);
            var phases = index.Phases.Where(p => p.Clip != phase.Clip).Append(phase).OrderBy(p => p.Clip, StringComparer.Ordinal).ToList();
            File.WriteAllText(indexPath, ContentJson.Serialize(new MotionIndexFile { SchemaVersion = index.SchemaVersion, Phases = phases }) + "\n");
        }
    }
}
