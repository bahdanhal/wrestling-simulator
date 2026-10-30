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
                    return $"{who} +{e.Points} ({DetailLabel(e.Detail)})";
                case BoutEventType.TechniqueCompleted:
                    return $"{who}: {technique} ({DetailLabel(e.Detail)})";
                case BoutEventType.CounterStarted:
                    return content.Techniques.Get(e.TechniqueId!).Role == TechniqueRole.Defence ? $"{who} defends: {technique}" : $"{who} counters: {technique}";
                case BoutEventType.StepOut:
                    return string.IsNullOrEmpty(e.Detail) ? $"{who} steps out of bounds" : $"{who} steps out ({DetailLabel(e.Detail)})";
                case BoutEventType.PassivityCall:
                    return $"{who} passivity call #{e.Detail}";
                case BoutEventType.ActivityPeriodStarted:
                    return $"{who} on 30s activity clock";
                case BoutEventType.Warning:
                    return string.IsNullOrEmpty(e.Detail) ? $"{who} warned" : $"{who} warned ({DetailLabel(e.Detail)})";
                case BoutEventType.Caution:
                    return string.IsNullOrEmpty(e.Detail) ? $"{who} cautioned" : $"{who} cautioned ({DetailLabel(e.Detail)})";
                case BoutEventType.Escape:
                    return string.IsNullOrEmpty(e.Detail) ? $"{who} returns to standing" : $"{who} escapes ({DetailLabel(e.Detail)})";
                case BoutEventType.PeriodStarted:
                    return $"Period {e.Detail} begins";
                case BoutEventType.PeriodEnded:
                    return $"End of period {e.Detail}";
                case BoutEventType.BoutEnded:
                    return string.IsNullOrEmpty(who) ? $"Bout ended ({e.Detail})" : $"{who} wins by {e.Detail}";
                default:
                    return $"{e.Type} {who} {DetailLabel(e.Detail)}".Trim();
            }
        }

        private static string DetailLabel(string? detail)
        {
            if (string.IsNullOrEmpty(detail))
            {
                return string.Empty;
            }

            switch (detail)
            {
                case "ControlBehind":
                    return "control behind";
                case "DangerDirect":
                    return "to danger";
                case "DangerDelayed":
                    return "exposure";
                case "GrandAmplitudeToDanger":
                    return "grand amplitude to danger";
                case "GrandAmplitudeThroughDanger":
                case "GrandAmplitude":
                    return "grand amplitude";
                case "StandingReset":
                    return "standing";
                case "Takedown":
                    return "takedown";
                case "Exposure":
                    return "exposure";
                case "Reversal":
                    return "reversal";
                case "StepOut":
                    return "step-out";
                case "ActivityClock":
                    return "activity clock";
                case "Passivity":
                    return "passivity";
                case "Caution":
                    return "caution";
                case "DeliberatePushOut":
                    return "deliberate push-out";
                case "FleeingTheMat":
                    return "fleeing the mat";
                case "FleeingTheHold":
                    return "fleeing the hold";
                case "IllegalHold":
                    return "illegal hold";
                default:
                    return detail!;
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
