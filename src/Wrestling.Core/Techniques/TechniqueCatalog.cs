using System;
using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Techniques
{
    /// <summary>
    /// The full set of technique definitions, validated as a whole: unique ids, every counter
    /// window points at an existing counter or defence that is legal in the same styles.
    /// </summary>
    public sealed class TechniqueCatalog
    {
        private readonly Dictionary<string, TechniqueDefinition> _byId;

        private TechniqueCatalog(Dictionary<string, TechniqueDefinition> byId)
        {
            _byId = byId;
        }

        public IReadOnlyCollection<TechniqueDefinition> All => _byId.Values;

        public int Count => _byId.Count;

        public TechniqueDefinition Get(string id) =>
            _byId.TryGetValue(id, out TechniqueDefinition? definition) ? definition : throw new KeyNotFoundException($"Unknown technique '{id}'.");

        public bool Contains(string id) => _byId.ContainsKey(id);

        /// <summary>Techniques usable in a style and age category, in id order.</summary>
        public IEnumerable<TechniqueDefinition> AllowedFor(WrestlingStyle style, AgeCategory age) =>
            _byId.Values.Where(t => t.IsAllowed(style, age)).OrderBy(t => t.Id, StringComparer.Ordinal);

        /// <summary>Builds a catalogue; throws <see cref="RulesValidationException"/> listing every problem.</summary>
        public static TechniqueCatalog Create(IEnumerable<TechniqueDefinition> definitions)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            var errors = new List<string>();
            var byId = new Dictionary<string, TechniqueDefinition>(StringComparer.Ordinal);
            foreach (TechniqueDefinition definition in definitions)
            {
                errors.AddRange(definition.Validate());
                if (byId.ContainsKey(definition.Id))
                {
                    errors.Add($"{definition.Id}: defined twice.");
                    continue;
                }

                byId[definition.Id] = definition;
            }

            foreach (TechniqueDefinition definition in byId.Values)
            {
                foreach (PhaseDefinition phase in definition.Phases)
                {
                    foreach (CounterWindow window in phase.CounterWindows)
                    {
                        if (!byId.TryGetValue(window.Technique, out TechniqueDefinition? counter))
                        {
                            errors.Add($"{definition.Id}.phases[{phase.Id}]: counter '{window.Technique}' does not exist.");
                            continue;
                        }

                        if (counter.Role == TechniqueRole.Attack)
                        {
                            errors.Add($"{definition.Id}.phases[{phase.Id}]: '{counter.Id}' is an attack, not a counter or defence.");
                        }

                        foreach (WrestlingStyle style in definition.Styles.Where(s => !counter.Styles.Contains(s)))
                        {
                            // A counter missing a style simply is not offered there; flag it so content stays intentional.
                            errors.Add($"{definition.Id}.phases[{phase.Id}]: counter '{counter.Id}' is not available in {style}.");
                        }
                    }
                }
            }

            if (errors.Count > 0)
            {
                throw new RulesValidationException("Technique catalogue", errors);
            }

            return new TechniqueCatalog(byId);
        }
    }
}
