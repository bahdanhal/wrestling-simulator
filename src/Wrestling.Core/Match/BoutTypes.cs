using System;
using System.Collections.Generic;
using System.Linq;
using Wrestling.Core.Bouts;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Core.Match
{
    /// <summary>Macro position of the bout (design section 5.5).</summary>
    public enum BoutPosition
    {
        Neutral,
        ParTerre,
        Danger,
        Scramble,
    }

    /// <summary>
    /// What a wrestler wants to do until the next decision (design section 5.6). The AI, the
    /// player's input and coach instructions all produce intents; the engine resolves them identically.
    /// </summary>
    public sealed class Intent
    {
        public static readonly Intent Idle = new Intent();

        /// <summary>Direction to push or move in (normalised by the engine).</summary>
        public FixVector2 PushDirection { get; init; }

        /// <summary>0 relaxed to 1 full effort.</summary>
        public Fix64 PushIntensity { get; init; }

        public GripType? GripGoal { get; init; }

        public GripSide GripGoalSide { get; init; }

        /// <summary>Fight to break the opponent's grips.</summary>
        public bool StripGrips { get; init; }

        /// <summary>Desired level (0 flat, 1 upright).</summary>
        public Fix64 TargetLevel { get; init; } = Fix64.One;

        /// <summary>Technique to start, if its requirements are met.</summary>
        public string? Technique { get; init; }

        public GripSide TechniqueSide { get; init; }

        /// <summary>Requested execution quality 0–1; the engine clamps it to what the wrestler's skill allows.</summary>
        public Fix64 ExecutionQuality { get; init; } = Fix64.Half;

        /// <summary>Counter or defence to start inside the opponent's technique, if a window is open.</summary>
        public string? Counter { get; init; }
    }

    /// <summary>Decides for one wrestler: AI, player input or a coached AI.</summary>
    public interface IBoutController
    {
        /// <summary>Called at the wrestler's decision interval. Must not modify the bout state.</summary>
        Intent Decide(IBoutView view, Corner self);

        /// <summary>After an opponent's Greco-Roman passivity call: true to continue in par terre on top, false for standing.</summary>
        bool ChoosesParTerre(IBoutView view, Corner self);
    }

    /// <summary>Read-only access to the bout for controllers, the renderer and commentary.</summary>
    public interface IBoutView
    {
        int Tick { get; }

        Ruleset Rules { get; }

        EngineTuning Tuning { get; }

        TechniqueCatalog Catalog { get; }

        WrestlingStyle Style { get; }

        AgeCategory Age { get; }

        BoutClock Clock { get; }

        BoutScoreSheet Sheet { get; }

        GripSet Grips { get; }

        BoutPosition Position { get; }

        /// <summary>The top wrestler in par terre or danger position.</summary>
        Corner? Top { get; }

        TechniqueExecution? ActiveTechnique { get; }

        /// <summary>True while the bout is stopped for a stoppage, break or before the start.</summary>
        bool IsStopped { get; }

        bool IsFinished { get; }

        WrestlerRuntime Wrestler(Corner corner);

        /// <summary>Repertoire techniques the wrestler could start right now, with the side to use.</summary>
        IReadOnlyList<(TechniqueDefinition Technique, GripSide Side)> StartableTechniques(Corner corner);

        /// <summary>Counters and defences the wrestler knows that are open in the opponent's current phase.</summary>
        IReadOnlyList<TechniqueDefinition> OpenCounters(Corner corner);
    }

    /// <summary>Everything needed to run one bout.</summary>
    public sealed class BoutSetup
    {
        public Ruleset Rules { get; init; } = null!;

        public EngineTuning Tuning { get; init; } = null!;

        public TechniqueCatalog Catalog { get; init; } = null!;

        public WrestlingStyle Style { get; init; }

        public AgeCategory Age { get; init; } = AgeCategory.Senior;

        public Wrestler Red { get; init; } = null!;

        public Wrestler Blue { get; init; } = null!;

        public IBoutController RedController { get; init; } = null!;

        public IBoutController BlueController { get; init; } = null!;

        public ulong Seed { get; init; }

        public Wrestler WrestlerOf(Corner corner) => corner == Corner.Red ? Red : Blue;

        public IBoutController ControllerOf(Corner corner) => corner == Corner.Red ? RedController : BlueController;

        public void Validate()
        {
            if (Rules == null || Tuning == null || Catalog == null || Red == null || Blue == null || RedController == null || BlueController == null)
            {
                throw new ArgumentException("Rules, tuning, catalogue, both wrestlers and both controllers are required.");
            }

            var errors = new List<string>(Tuning.Validate());
            foreach (Corner corner in new[] { Corner.Red, Corner.Blue })
            {
                Wrestler wrestler = WrestlerOf(corner);
                errors.AddRange(wrestler.Validate().Select(e => $"{corner}: {e}"));
                if (wrestler.Style != Style)
                {
                    errors.Add($"{corner}: wrestler style {wrestler.Style} does not match the bout style {Style}.");
                }

                errors.AddRange(wrestler.Repertoire.Where(r => !Catalog.Contains(r.TechniqueId)).Select(r => $"{corner}: unknown technique '{r.TechniqueId}'."));
            }

            if (errors.Count > 0)
            {
                throw new ArgumentException("Bout setup is invalid:\n- " + string.Join("\n- ", errors));
            }
        }
    }

    public enum BoutEventType
    {
        PeriodStarted,
        PeriodEnded,
        GripGained,
        GripLost,
        TechniqueStarted,
        PhaseSucceeded,
        TechniqueCompleted,
        TechniqueFailed,
        CounterStarted,
        PointsAwarded,
        StepOut,
        ZoneCall,
        Warning,
        Caution,
        PassivityCall,
        ActivityPeriodStarted,
        DangerPosition,
        Escape,
        Scramble,
        Stoppage,
        Restart,
        BoutEnded,
    }

    /// <summary>Something that happened in the bout, for commentary, statistics, replays and rendering.</summary>
    public sealed class BoutEvent
    {
        public BoutEvent(int tick, BoutTime time, BoutEventType type, Corner? wrestler, string? techniqueId, int points, string detail)
        {
            Tick = tick;
            Time = time;
            Type = type;
            Wrestler = wrestler;
            TechniqueId = techniqueId;
            Points = points;
            Detail = detail;
        }

        /// <summary>Engine tick (including stopped time).</summary>
        public int Tick { get; }

        /// <summary>Bout clock time.</summary>
        public BoutTime Time { get; }

        public BoutEventType Type { get; }

        public Corner? Wrestler { get; }

        public string? TechniqueId { get; }

        public int Points { get; }

        public string Detail { get; }

        public override string ToString() => $"{Time} {Type} {Wrestler} {TechniqueId} {(Points > 0 ? Points.ToString() : string.Empty)} {Detail}".Trim();
    }
}
