using System.Collections.Generic;
using System.IO;
using Wrestling.Core.Wrestlers;
using Wrestling.Data.Json;

namespace Wrestling.Data.Names
{
    /// <summary>Loads and validates a name pool from <c>data/names/</c>.</summary>
    public static class NamePoolLoader
    {
        public static NamePool FromJson(string json, string sourceName)
        {
            NamePool pool = ContentJson.Deserialize<NamePool>(json, sourceName);
            IReadOnlyList<string> errors = pool.Validate();
            if (errors.Count > 0)
            {
                throw new ContentLoadException(sourceName, "Name pool is invalid:\n- " + string.Join("\n- ", errors));
            }

            return pool;
        }

        public static NamePool FromFile(string path) => FromJson(File.ReadAllText(path), Path.GetFileName(path));
    }
}
