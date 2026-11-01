using Wrestling.Core.Match;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Tests.Shared;

/// <summary>Small, complete technique definitions used as test fixtures (not shipped content).</summary>
public static class TestTechniques
{
    public static readonly WrestlingStyle[] AllStyles = { WrestlingStyle.GrecoRoman, WrestlingStyle.Freestyle, WrestlingStyle.Womens };

    public static TechniqueDefinition HipToss(string counterId = "technique.all.hip_block", string? counterAnimation = null) => new()
    {
        SchemaVersion = 1,
        Id = "technique.all.hip_toss_overhook",
        NameKey = "tech.hip_toss_overhook.name",
        Role = TechniqueRole.Attack,
        Family = TechniqueFamily.UpperBodyThrows,
        Styles = AllStyles,
        Amplitude = Amplitude.GrandAmplitude,
        Start = new StartRequirements
        {
            Positions = new[] { StartPosition.NeutralTied },
            RequiredGrips = new[] { new GripRequirement { Type = GripType.Overhook, Side = GripSideRequirement.Same } },
            OptionalGrips = new[] { new GripBonus { Type = GripType.HeadAndArm, Bonus = Fix64.Parse("0.1") } },
            RelativePositions = new[] { RelativePosition.Front },
            MinimumOwnLevel = Fix64.Zero,
            MaximumOwnLevel = Fix64.One,
        },
        Phases = new[]
        {
            Phase("entry", new[] { (WrestlerAttribute.Explosiveness, 40), (WrestlerAttribute.UpperBodyThrows, 40), (WrestlerAttribute.Balance, 20) },
                new[] { new CounterWindow { Technique = counterId, FromTick = 0, ToTick = 10, Animation = counterAnimation } }, endStates: null),
            Phase("flight", new[] { (WrestlerAttribute.UpperBodyStrength, 50), (WrestlerAttribute.UpperBodyThrows, 50) },
                System.Array.Empty<CounterWindow>(), new[] { EndState.DangerDirect, EndState.Flank, EndState.Scramble }),
        },
        Gesture = new GestureDefinition { Family = GestureFamily.Arc },
    };

    public static TechniqueDefinition HipBlock() => new()
    {
        SchemaVersion = 1,
        Id = "technique.all.hip_block",
        NameKey = "tech.hip_block.name",
        Role = TechniqueRole.Defence,
        Family = null,
        Styles = AllStyles,
        Start = new StartRequirements { Positions = new[] { StartPosition.OpponentTechnique }, MinimumOwnLevel = Fix64.Zero, MaximumOwnLevel = Fix64.One },
        Phases = new[]
        {
            Phase("block", new[] { (WrestlerAttribute.UpperBodyDefence, 70), (WrestlerAttribute.LowerBodyStrength, 30) },
                System.Array.Empty<CounterWindow>(), new[] { EndState.Neutral, EndState.Scramble }),
        },
        Gesture = new GestureDefinition { Family = GestureFamily.SwipeAway },
    };

    public static TechniqueDefinition DoubleLeg() => new()
    {
        SchemaVersion = 1,
        Id = "technique.fs.double_leg_blast",
        NameKey = "tech.double_leg_blast.name",
        Role = TechniqueRole.Attack,
        Family = TechniqueFamily.LegAttacks,
        Styles = new[] { WrestlingStyle.Freestyle, WrestlingStyle.Womens },
        Start = new StartRequirements { Positions = new[] { StartPosition.NeutralApart, StartPosition.NeutralTied }, MinimumOwnLevel = Fix64.Zero, MaximumOwnLevel = Fix64.Parse("0.7") },
        Phases = new[]
        {
            Phase("finish", new[] { (WrestlerAttribute.LegAttacks, 60), (WrestlerAttribute.Explosiveness, 40) },
                System.Array.Empty<CounterWindow>(), new[] { EndState.ControlBehind, EndState.Scramble }),
        },
        Amplitude = Amplitude.None,
        Gesture = new GestureDefinition { Family = GestureFamily.StraightSwipeDown },
    };

    public static TechniqueDefinition GutWrench() => new()
    {
        SchemaVersion = 1,
        Id = "technique.all.gut_wrench",
        NameKey = "tech.gut_wrench.name",
        Role = TechniqueRole.Attack,
        Family = TechniqueFamily.ParTerreTurns,
        Styles = AllStyles,
        Amplitude = Amplitude.None,
        Start = new StartRequirements { Positions = new[] { StartPosition.ParTerreTop }, RelativePositions = new[] { RelativePosition.Behind }, MinimumOwnLevel = Fix64.Zero, MaximumOwnLevel = Fix64.One },
        Phases = new[]
        {
            Phase("turn", new[] { (WrestlerAttribute.ParTerreTurns, 60), (WrestlerAttribute.UpperBodyStrength, 40) },
                System.Array.Empty<CounterWindow>(), new[] { EndState.DangerDelayed, EndState.Neutral }),
        },
        Gesture = new GestureDefinition { Family = GestureFamily.Circle },
    };

    public static TechniqueDefinition StandUp() => new()
    {
        SchemaVersion = 1,
        Id = "technique.all.stand_up",
        NameKey = "tech.stand_up.name",
        Role = TechniqueRole.Defence,
        Family = null,
        Styles = AllStyles,
        Amplitude = Amplitude.None,
        Start = new StartRequirements { Positions = new[] { StartPosition.ParTerreBottom, StartPosition.OpponentTechnique }, MinimumOwnLevel = Fix64.Zero, MaximumOwnLevel = Fix64.One },
        Phases = new[]
        {
            Phase("rise", new[] { (WrestlerAttribute.ParTerreBottomDefence, 60), (WrestlerAttribute.Explosiveness, 40) },
                System.Array.Empty<CounterWindow>(), new[] { EndState.Neutral }),
        },
        Gesture = new GestureDefinition { Family = GestureFamily.SwipeAway },
    };

    /// <summary>A small catalogue covering neutral attacks, a counter, par terre and an escape.</summary>
    public static Wrestling.Core.Techniques.TechniqueCatalog EngineCatalog(string? hipBlockAnimation = null) =>
        Wrestling.Core.Techniques.TechniqueCatalog.Create(new[] { HipToss(counterAnimation: hipBlockAnimation), HipBlock(), DoubleLeg(), GutWrench(), StandUp() });

    private static PhaseDefinition Phase(string id, (WrestlerAttribute Attribute, int Percent)[] weights, CounterWindow[] counters, EndState[]? endStates) => new()
    {
        Id = id,
        MinimumTicks = 12,
        MaximumTicks = 24,
        Attributes = weights.Select(w => new AttributeWeight { Attribute = w.Attribute, Percent = w.Percent }).ToArray(),
        Steepness = Fix64.One,
        StaminaCost = Fix64.Parse("0.6"),
        BurstCost = Fix64.FromInt(3),
        Outcomes = new[] { PhaseOutcome.Clean, PhaseOutcome.Stalled, PhaseOutcome.Blocked },
        CounterWindows = counters,
        EndStates = endStates ?? System.Array.Empty<EndState>(),
        Animation = $"anim.test.{id}",
    };
}
