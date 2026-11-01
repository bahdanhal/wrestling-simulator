using Wrestling.Core.Match;
using Wrestling.Core.Techniques;
using Wrestling.Data.Animation;
using Wrestling.Data.Json;
using Wrestling.Data.Techniques;

namespace Wrestling.Data.Tests.Animation;

public class MoveAnimationTests
{
    private static readonly TechniqueCatalog Catalog = TechniqueLoader.FromDirectory(TestContent.PathOf("techniques"));
    private static readonly MoveAnimationLibrary Library = MoveAnimationLoader.FromDirectory(TestContent.PathOf("animation"), Catalog);

    [Fact]
    public void EveryTechniquePhaseHasItsClip()
    {
        int phases = Catalog.All.Sum(t => t.Phases.Count);
        int entries = Catalog.All.Sum(t => MoveAnimationLibrary.EntryClips(Catalog, t.Id).Count());
        Assert.Equal(phases + entries, Library.Count);
        foreach (TechniqueDefinition technique in Catalog.All)
        {
            foreach (PhaseDefinition phase in technique.Phases)
            {
                Assert.Equal(phase.Animation, Library.Get(phase.Animation).Id);
            }
        }
    }

    [Fact]
    public void ParTerreTechniquesUseTheBottomFrame()
    {
        foreach (TechniqueDefinition technique in Catalog.All)
        {
            ClipFrame expected = MoveAnimationLibrary.IsParTerre(technique) ? ClipFrame.Bottom : ClipFrame.Executor;
            Assert.All(technique.Phases, phase => Assert.Equal(expected, Library.Get(phase.Animation).Frame));
        }

        Assert.True(MoveAnimationLibrary.IsParTerre(Catalog.Get("technique.all.gut_wrench")));
        Assert.True(MoveAnimationLibrary.IsParTerre(Catalog.Get("technique.all.stand_up")));
        Assert.False(MoveAnimationLibrary.IsParTerre(Catalog.Get("technique.fs.sprawl")));
    }

    [Fact]
    public void ThrowPhasesLoadTheirOwnThrowSpecs()
    {
        MoveClip headlock = Library.Get(Catalog.Get("technique.all.headlock_throw").Phases[1].Animation);
        MoveClip entry = Library.Get(Catalog.Get("technique.all.headlock_throw").Phases[0].Animation);
        MoveClip fireman = Library.Get(Catalog.Get("technique.fs.fireman_carry").Phases[1].Animation);
        Assert.Equal(ThrowKind.Throw, headlock.Throw.Kind);
        Assert.Equal(ThrowAxis.Over, headlock.Throw.Axis);
        Assert.Contains(headlock.Throw.Grips, g => g.Thrown == "Head");
        Assert.Equal(ThrowKind.Load, entry.Throw.Kind);
        Assert.Equal(0f, entry.Throw.Degrees);
        Assert.Equal(ThrowAxis.Forward, fireman.Throw.Axis);
        Assert.NotSame(headlock.Throw, fireman.Throw);
        Assert.Equal(ThrowKind.None, ThrowSpec.NoThrow.Kind);
        // The head is locked against the thrower's body, not only held in a hand.
        Assert.Contains(headlock.Throw.Grips, g => g.Thrower == "Spine2" && g.Thrown == "Head");
        // Carried across the shoulders: seated on top of the fulcrum.
        Assert.True(fireman.Throw.SeatUp > 0.8f);
        Assert.Equal(0.5f, ThrowSpec.NoThrow.SeatUp);
    }

    [Fact]
    public void RejectsThrowSeatOutsideTheFulcrum()
    {
        var throwing = new ThrowSpec { Kind = ThrowKind.Throw, Pivot = "Hips", Degrees = 180f, SeatUp = 1.2f };
        var clip = new MoveClip { Id = "anim.test", Keys = new[] { Key(0f), Key(1f) }, Throw = throwing };
        Assert.Contains(clip.Validate(), e => e.Contains("seatUp"));
        var seated = new MoveClip { Id = "anim.test", Keys = new[] { Key(0f), Key(1f) }, Throw = new ThrowSpec { Kind = ThrowKind.Throw, Pivot = "Hips", Degrees = 180f, SeatUp = 0.9f } };
        Assert.Empty(seated.Validate());
        var backwards = new MoveClip { Id = "anim.test", Keys = new[] { Key(0f), Key(1f) }, Throw = new ThrowSpec { Kind = ThrowKind.Throw, Pivot = "Hips", Axis = ThrowAxis.Over, Degrees = -120f } };
        Assert.Contains(backwards.Validate(), e => e.Contains("positive degrees"));
    }

    [Fact]
    public void SamplingHitsKeysAndEasesBetweenThem()
    {
        MoveClip clip = Clip(Key(0f, x: 0f, heading: 170f, ground: GroundState.Standing), Key(1f, x: 1f, heading: -170f, ground: GroundState.Prone));
        Assert.Equal(0f, clip.Sample(0f).Executor.X);
        Assert.Equal(1f, clip.Sample(1f).Executor.X);
        Assert.Equal(0.5f, clip.Sample(0.5f).Executor.X, 3);
        Assert.True(clip.Sample(0.25f).Executor.X < 0.25f); // eased in
        Assert.Equal(GroundState.Standing, clip.Sample(0.49f).Executor.Ground);
        Assert.Equal(GroundState.Prone, clip.Sample(0.51f).Executor.Ground);
        // Heading takes the short way round through 180°.
        Assert.Equal(180f, Math.Abs(clip.Sample(0.5f).Executor.Heading), 3);
        Assert.Equal(1f, clip.Sample(7f).Executor.X);
    }

    [Fact]
    public void RejectsBadClips()
    {
        Assert.NotEmpty(Clip(Key(0f)).Validate());
        Assert.NotEmpty(Clip(Key(0.1f), Key(1f)).Validate());
        Assert.NotEmpty(Clip(Key(0f), Key(0f), Key(1f)).Validate());
        Assert.NotEmpty(Clip(Key(0f, level: 1.5f), Key(1f)).Validate());
        Assert.Empty(Clip(Key(0f), Key(1f)).Validate());
    }

    [Fact]
    public void LibraryRejectsMissingAndForeignClips()
    {
        MoveAnimationFile[] files = Library.All
            .GroupBy(c => Catalog.All.First(t => t.Phases.Any(p => p.Animation == c.Id) || MoveAnimationLibrary.EntryClips(Catalog, t.Id).Contains(c.Id)).Id)
            .Select(g => new MoveAnimationFile { SchemaVersion = 1, Technique = g.Key, Clips = g.ToList() })
            .ToArray();
        Assert.Equal(Library.Count, MoveAnimationLibrary.Create(files, Catalog).Count);
        Assert.Throws<ArgumentException>(() => MoveAnimationLibrary.Create(files.Skip(1), Catalog));

        MoveAnimationFile first = files[0];
        var foreign = new MoveAnimationFile { SchemaVersion = 1, Technique = first.Technique, Clips = first.Clips.Append(Clip(Key(0f), Key(1f))).ToList() };
        Assert.Throws<ArgumentException>(() => MoveAnimationLibrary.Create(files.Skip(1).Append(foreign), Catalog));
    }

    [Fact]
    public void LoaderRejectsUnknownFields()
    {
        const string json = """{ "schemaVersion": 1, "technique": "technique.fs.sprawl", "clips": [], "speed": 2 }""";
        Assert.Throws<ContentLoadException>(() => MoveAnimationLoader.FromJson(json, "technique.fs.sprawl.json"));
    }

    [Fact]
    public void CounterWindowClips_LiveInTheCountersFileAndMustExist()
    {
        MoveAnimationFile[] files = Library.All
            .GroupBy(c => Catalog.All.First(t => t.Phases.Any(p => p.Animation == c.Id) || MoveAnimationLibrary.EntryClips(Catalog, t.Id).Contains(c.Id)).Id)
            .Select(g => new MoveAnimationFile { SchemaVersion = 1, Technique = g.Key, Clips = g.ToList() })
            .ToArray();
        const string chest = "anim.hip_block.block_chest";
        Assert.Contains(chest, MoveAnimationLibrary.EntryClips(Catalog, "technique.all.hip_block"));

        // Missing from the counter's file.
        MoveAnimationFile[] missing = files.Select(f => f.Technique != "technique.all.hip_block" ? f
            : new MoveAnimationFile { SchemaVersion = 1, Technique = f.Technique, Clips = f.Clips.Where(c => c.Id != chest).ToList() }).ToArray();
        ArgumentException noClip = Assert.Throws<ArgumentException>(() => MoveAnimationLibrary.Create(missing, Catalog));
        Assert.Contains(chest, noClip.Message);

        // Put in the attack's file instead of the counter's.
        MoveClip moved = Library.Get(chest);
        MoveAnimationFile[] foreign = missing.Select(f => f.Technique != "technique.all.body_lock_throw" ? f
            : new MoveAnimationFile { SchemaVersion = 1, Technique = f.Technique, Clips = f.Clips.Append(moved).ToList() }).ToArray();
        Assert.Throws<ArgumentException>(() => MoveAnimationLibrary.Create(foreign, Catalog));
    }

    [Fact]
    public void HipBlockStartsFacingTheAttackerAsTheAttackLeftThem()
    {
        // A hip block against a turned-in hip throw is behind the attacker's hips; against a
        // chest-to-chest lift it stays chest to chest. The counter's first key must face the
        // attacker as the attack's clip does at the end of the window (at the phase's fastest).
        int checkedWindows = 0;
        foreach (TechniqueDefinition attack in Catalog.All)
        {
            foreach (PhaseDefinition phase in attack.Phases)
            {
                foreach (CounterWindow window in phase.CounterWindows.Where(w => w.Technique == "technique.all.hip_block"))
                {
                    (ActorKey attacker, ActorKey defender) = Library.Get(phase.Animation).Sample(Math.Min(1f, window.ToTick / (float)phase.MinimumTicks));
                    float attackFacing = Wrap(attacker.Heading - defender.Heading);
                    MoveClip counter = Library.Get(window.Animation ?? Catalog.Get(window.Technique).Phases[0].Animation);
                    ClipKey first = counter.Keys[0];
                    float counterFacing = Wrap(first.Opponent.Heading - first.Executor.Heading);
                    Assert.True(Math.Abs(Wrap(attackFacing - counterFacing)) <= 30f,
                        $"{attack.Id} {phase.Id}: attacker faces {attackFacing:F0} degrees from the defender, {counter.Id} starts at {counterFacing:F0}.");
                    checkedWindows++;
                }
            }
        }

        Assert.True(checkedWindows >= 8);
    }

    /// <summary>An angle wrapped to (-180, 180] degrees.</summary>
    private static float Wrap(float degrees)
    {
        float wrapped = ((degrees % 360f) + 360f) % 360f;
        return wrapped > 180f ? wrapped - 360f : wrapped;
    }

    private static MoveClip Clip(params ClipKey[] keys) => new() { Id = "anim.test", Frame = ClipFrame.Executor, Keys = keys };

    private static ClipKey Key(float t, float x = 0f, float heading = 0f, float level = 0.75f, GroundState ground = GroundState.Standing) => new()
    {
        T = t,
        Executor = new ActorKey { X = x, Heading = heading, Level = level, Posture = 0.8f, Ground = ground, RollSide = 1 },
        Opponent = new ActorKey { X = 0.8f, Heading = 180f, Level = 0.75f, Posture = 0.8f, RollSide = 1 },
    };
}
