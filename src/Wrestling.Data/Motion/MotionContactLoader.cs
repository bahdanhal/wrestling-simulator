using System;
using System.IO;
using Wrestling.Data.Json;

namespace Wrestling.Data.Motion
{
    /// <summary>Loads optional contact sidecars independently of the recorded joint data.</summary>
    public static class MotionContactLoader
    {
        public const string ContactsFolder = "contacts";
        public static MotionContactSet FromJson(string json, string sourceName, MotionTake take)
        {
            MotionContactSet set = ContentJson.Deserialize<MotionContactSet>(json, sourceName);
            var errors = set.Validate(take);
            if (errors.Count != 0) throw new ContentLoadException(sourceName, string.Join("\n", errors));
            return set;
        }
        public static MotionContactSet? FromDirectory(string directory, MotionTake take)
        {
            if (take == null) throw new ArgumentNullException(nameof(take));
            string path = Path.Combine(directory, ContactsFolder, take.Id + ".json");
            return File.Exists(path) ? FromJson(File.ReadAllText(path), Path.GetFileName(path), take) : null;
        }
    }
}
