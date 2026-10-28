using Wrestling.Core.Bouts;
using Wrestling.Core.Match;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Data.Localization
{
    /// <summary>Turns bout events into short play-by-play lines, shared by the CLI and the game's event feed.</summary>
    public static class BoutCommentary
    {
        /// <summary>Events worth a line in the play-by-play; the rest (grips, phases) are too frequent.</summary>
        public static bool IsNotable(BoutEvent e) => e.Type is BoutEventType.TechniqueCompleted or BoutEventType.PointsAwarded or BoutEventType.CounterStarted
            or BoutEventType.StepOut or BoutEventType.PassivityCall or BoutEventType.ActivityPeriodStarted or BoutEventType.Warning
            or BoutEventType.Caution or BoutEventType.Escape or BoutEventType.PeriodStarted or BoutEventType.PeriodEnded or BoutEventType.BoutEnded;

        public static string Describe(BoutEvent e, GameContent content, Wrestler red, Wrestler blue)
        {
            string who = e.Wrestler == null ? string.Empty : (e.Wrestler == Corner.Red ? $"RED {red.Name.Family}" : $"BLUE {blue.Name.Family}");
            string technique = e.TechniqueId == null ? string.Empty : TechniqueName(e.TechniqueId, content);
            switch (e.Type)
            {
                case BoutEventType.PointsAwarded:
                    return $"{who} +{e.Points} ({e.Detail})";
                case BoutEventType.TechniqueCompleted:
                    return $"{who} {technique} -> {e.Detail}";
                case BoutEventType.CounterStarted:
                    return content.Techniques.Get(e.TechniqueId!).Role == TechniqueRole.Defence ? $"{who} defends: {technique}" : $"{who} counters: {technique}";
                default:
                    return $"{e.Type} {who} {e.Detail}".Trim();
            }
        }

        /// <summary>How the bout was won, for example "Points (tie-break: highest-value hold)".</summary>
        public static string ResultLine(BoutResult result, GameContent content)
        {
            if (result.VictoryType == null)
            {
                return result.Code;
            }

            string line = content.English.Get("result.victory." + result.VictoryType.Value);
            return result.DecidedBy == null ? line : $"{line} ({content.English.Get("result.tiebreak." + result.DecidedBy.Value)})";
        }

        public static string TechniqueName(string techniqueId, GameContent content) => content.English.Get(content.Techniques.Get(techniqueId).NameKey);

        /// <summary>Elapsed bout time as "P2 4:13".</summary>
        public static string Clock(BoutTime time, BoutClock clock)
        {
            int seconds = (((time.Period - 1) * clock.PeriodTicks) + time.TickInPeriod) / BoutClock.TicksPerSecond;
            return $"P{time.Period} {seconds / 60}:{seconds % 60:00}";
        }
    }
}
