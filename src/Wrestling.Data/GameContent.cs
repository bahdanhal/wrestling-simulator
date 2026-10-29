using System.IO;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Core.Wrestlers;
using Wrestling.Data.Animation;
using Wrestling.Data.Motion;
using Wrestling.Data.Engine;
using Wrestling.Data.Localization;
using Wrestling.Data.Names;
using Wrestling.Data.Rulesets;
using Wrestling.Data.Techniques;

namespace Wrestling.Data
{
    /// <summary>All content the simulation needs, loaded and validated from a <c>data/</c> folder.</summary>
    public sealed class GameContent
    {
        public const string DefaultRulesetId = "uww-2026-01";

        public const string DefaultNamePoolId = "fictional";

        private GameContent(Ruleset rules, EngineTuning tuning, TechniqueCatalog techniques, LocalizationTable english, NamePool names, MoveAnimationLibrary animations, MotionLibrary motion)
        {
            Rules = rules;
            Tuning = tuning;
            Techniques = techniques;
            English = english;
            Names = names;
            Animations = animations;
            Motion = motion;
        }

        public Ruleset Rules { get; }

        public EngineTuning Tuning { get; }

        public TechniqueCatalog Techniques { get; }

        public LocalizationTable English { get; }

        /// <summary>Names for generated fictional wrestlers.</summary>
        public NamePool Names { get; }

        /// <summary>Paired move clips for every technique phase (display only).</summary>
        public MoveAnimationLibrary Animations { get; }

        /// <summary>Recorded motion (motion capture or extracted from video) for technique phases that have it.</summary>
        public MotionLibrary Motion { get; }

        public static GameContent Load(string dataDirectory, string rulesetId = DefaultRulesetId)
        {
            if (!Directory.Exists(dataDirectory))
            {
                throw new DirectoryNotFoundException($"Content folder '{dataDirectory}' does not exist.");
            }

            TechniqueCatalog techniques = TechniqueLoader.FromDirectory(Path.Combine(dataDirectory, "techniques"));
            MoveAnimationLibrary animations = MoveAnimationLoader.FromDirectory(Path.Combine(dataDirectory, "animation"), techniques);
            return new GameContent(
                RulesetLoader.FromFile(Path.Combine(dataDirectory, "rulesets", rulesetId + ".json")),
                TuningLoader.FromFile(Path.Combine(dataDirectory, "engine", "tuning.json")),
                techniques,
                LocalizationTable.FromFile(Path.Combine(dataDirectory, "localization", "en.json")),
                NamePoolLoader.FromFile(Path.Combine(dataDirectory, "names", DefaultNamePoolId + ".json")),
                animations,
                MotionLoader.FromDirectory(Path.Combine(dataDirectory, "motion"), animations));
        }

        /// <summary>Finds the repository's <c>data/</c> folder from <paramref name="start"/> or its parents.</summary>
        public static string? FindDataDirectory(string start)
        {
            DirectoryInfo? directory = new DirectoryInfo(start);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "data");
                if (File.Exists(Path.Combine(candidate, "engine", "tuning.json")))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }
    }
}
