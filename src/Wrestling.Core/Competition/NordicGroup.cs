using System;
using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Bouts;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Competition
{
    /// <summary>
    /// Round robin ("Nordic system", Art. 8): everyone wrestles everyone. Ranking: most wins;
    /// two wrestlers level on wins are separated by their bout; any other tie by classification
    /// points, falls, superiority wins, technical points scored, fewest conceded, lowest seed,
    /// lowest draw number. Used alone for up to 5 wrestlers and as the pools of 6–7.
    /// </summary>
    public sealed class NordicGroup : ICompetitionFormat
    {
        public const int MaximumStandaloneEntrants = 5;

        private readonly BoutLedger _ledger;
        private readonly List<string> _members;

        private NordicGroup(BoutLedger ledger, IEnumerable<string> members, BracketStage stage, string name)
        {
            _ledger = ledger;
            _members = members.ToList();
            Name = name;
            Schedule(stage);
        }

        public string Name { get; }

        public IReadOnlyList<string> Members => _members;

        public IReadOnlyList<BracketBout> Bouts => _ledger.Bouts.Where(IsOwnBout).ToList();

        public IEnumerable<Entrant> Entrants => _members.Select(id => _ledger.Entrants[id]);

        /// <summary>Number of rounds; the last round is wrestled on the second day.</summary>
        public int Rounds { get; private set; }

        public bool IsComplete => Bouts.All(b => b.IsDecided);

        public static NordicGroup Create(IReadOnlyList<Entrant> entrants, Ruleset rules)
        {
            if (entrants == null)
            {
                throw new ArgumentNullException(nameof(entrants));
            }

            if (entrants.Count < 2 || entrants.Count > MaximumStandaloneEntrants)
            {
                throw new ArgumentException($"A Nordic group on its own takes 2–{MaximumStandaloneEntrants} wrestlers.", nameof(entrants));
            }

            Seeding.Validate(entrants, entrants.Count);
            var ledger = new BoutLedger(rules, entrants);
            var group = new NordicGroup(ledger, entrants.OrderBy(e => e.DrawNumber).Select(e => e.Id), BracketStage.Pool, "Nordic");
            ledger.Advance();
            return group;
        }

        internal static NordicGroup CreatePool(BoutLedger ledger, IEnumerable<string> members, string name) =>
            new NordicGroup(ledger, members, BracketStage.Pool, name);

        public EntrantStatus StatusOf(string entrantId) => _ledger.StatusOf(entrantId);

        public IEnumerable<BracketBout> ReadyBouts() => Bouts.Where(b => b.IsReadyToWrestle);

        public void RecordResult(int boutId, BoutResult result)
        {
            if (!IsOwnBout(_ledger.Bout(boutId)))
            {
                throw new ArgumentException($"Bout #{boutId} is not part of {Name}.", nameof(boutId));
            }

            _ledger.RecordResult(boutId, result);
            _ledger.Advance();
        }

        public void Withdraw(string entrantId, EntrantStatus status)
        {
            _ledger.Withdraw(entrantId, status);
            _ledger.Advance();
        }

        /// <summary>Group members in ranking order, unranked entrants excluded.</summary>
        public IReadOnlyList<string> Ranking()
        {
            if (!IsComplete)
            {
                throw new InvalidOperationException($"{Name} is not complete.");
            }

            List<BracketBout> bouts = Bouts.ToList();
            List<string> ranked = _members.Where(id => _ledger.StatusOf(id) != EntrantStatus.Unranked).ToList();
            Dictionary<string, int> wins = ranked.ToDictionary(id => id, id => bouts.Count(b => b.Winner == id));

            var order = new List<string>();
            foreach (IGrouping<int, string> level in ranked.GroupBy(id => wins[id]).OrderByDescending(g => g.Key))
            {
                List<string> tied = level.ToList();
                if (tied.Count == 2)
                {
                    BracketBout? direct = bouts.FirstOrDefault(b => b.Involves(tied[0]) && b.Involves(tied[1]));
                    if (direct?.Winner != null)
                    {
                        order.Add(direct.Winner);
                        order.Add(direct.Winner == tied[0] ? tied[1] : tied[0]);
                        continue;
                    }
                }

                order.AddRange(StandingCriteria.Order(tied, bouts, _ledger.Entrants));
            }

            return order;
        }

        /// <summary>Ranks 1..n; only one bronze medal exists in this system.</summary>
        public IReadOnlyList<Placement> Standings()
        {
            var standings = new StandingsBuilder(_ledger);
            int rank = 1;
            foreach (string id in Ranking())
            {
                standings.Place(id, rank++);
            }

            return standings.Build();
        }

        private bool IsOwnBout(BracketBout bout) => bout.Stage == BracketStage.Pool && bout.Top != null && bout.Bottom != null && _members.Contains(bout.Top) && _members.Contains(bout.Bottom);

        private void Schedule(BracketStage stage)
        {
            Rounds = RoundRobin.RoundCount(_members.Count);
            foreach (RoundRobinPairing<string> pairing in RoundRobin.Schedule(_members))
            {
                BracketBout bout = _ledger.AddBout(stage, pairing.Round, Name, SlotSource.Entrant(pairing.Home), SlotSource.Entrant(pairing.Away));
                bout.Top = pairing.Home;
                bout.Bottom = pairing.Away;
            }
        }
    }
}
