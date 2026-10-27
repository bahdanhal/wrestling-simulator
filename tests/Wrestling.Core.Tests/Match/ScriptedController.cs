using Wrestling.Core.Match;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Tests.Match;

/// <summary>A predictable controller for engine tests: fixed effort, one grip goal, attacks whenever possible.</summary>
internal sealed class ScriptedController : IBoutController
{
    public Fix64 Effort { get; init; } = Fix64.FromRatio(6, 10);

    /// <summary>Push toward the opponent (true) or stand still.</summary>
    public bool Pressure { get; init; } = true;

    public GripType? GripGoal { get; init; }

    public bool Attack { get; init; } = true;

    public string? Counter { get; init; }

    public bool ParTerreChoice { get; init; } = true;

    public Fix64 Quality { get; init; } = Fix64.One;

    public static ScriptedController Idle() => new() { Pressure = false, Attack = false, Effort = Fix64.Zero };

    public Intent Decide(IBoutView view, Corner self)
    {
        WrestlerRuntime me = view.Wrestler(self);
        WrestlerRuntime opponent = view.Wrestler(self.Opponent());
        string? technique = null;
        GripSide side = GripSide.Right;
        if (Attack)
        {
            var startable = view.StartableTechniques(self);
            if (startable.Count > 0)
            {
                technique = startable[0].Technique.Id;
                side = startable[0].Side;
            }
        }

        return new Intent
        {
            PushDirection = Pressure ? opponent.Body.Position - me.Body.Position : FixVector2.Zero,
            PushIntensity = Pressure ? Effort : Fix64.Zero,
            GripGoal = GripGoal,
            GripGoalSide = GripSide.Right,
            TargetLevel = view.Tuning.NeutralLevel.For(view.Style),
            Technique = technique,
            TechniqueSide = side,
            ExecutionQuality = Quality,
            Counter = Counter,
        };
    }

    public bool ChoosesParTerre(IBoutView view, Corner self) => ParTerreChoice;
}
