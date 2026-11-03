using Wrestling.Core.Match;
using Wrestling.Core.Techniques;
using Wrestling.Data.Animation;
using Wrestling.Data.Techniques;

namespace Wrestling.Data.Tests.Animation;

/// <summary>Every keyed clip asks only for motion a human body can make (see <see cref="ClipFeasibility"/>).</summary>
public class AnimationBiomechanicalTests
{
    private static readonly TechniqueCatalog Catalog = TechniqueLoader.FromDirectory(TestContent.PathOf("techniques"));

    private static readonly MoveAnimationFile[] Files = Directory.GetFiles(TestContent.PathOf("animation"), "*.json")
        .OrderBy(f => f, StringComparer.Ordinal)
        .Select(f => MoveAnimationLoader.FromJson(File.ReadAllText(f), Path.GetFileName(f)))
        .ToArray();

    [Fact]
    public void EveryShippedClipIsFeasibleAtItsPhasesFastest()
    {
        Assert.Equal(Catalog.Count, Files.Length);
        int clips = 0;
        foreach (TechniqueDefinition technique in Catalog.All)
        {
            MoveAnimationFile file = Files.Single(f => f.Technique == technique.Id);
            foreach (PhaseDefinition phase in technique.Phases)
            {
                MoveClip clip = file.Clips.Single(c => c.Id == phase.Animation);
                Assert.Empty(ClipFeasibility.Check(clip, phase.MinimumTicks));
                clips++;
            }
        }

        Assert.Equal(Catalog.All.Sum(t => t.Phases.Count), clips);
    }

    [Fact]
    public void HipThrowsLoadUnderTheOpponentWithTheTrunkBentOverThem()
    {
        foreach (string name in new[] { "headlock_throw", "hip_toss_overhook", "arm_throw", "underhook_throw" })
        {
            MoveClip entry = Clip($"anim.{name}.entry");
            ClipKey loaded = entry.Keys[^1];
            (ClipKinematics.Trunk thrower, ClipKinematics.Trunk thrown) = ClipFeasibility.Place(loaded, ClipKinematics.Body.Reference, false, false);
            Assert.True(thrown.Pelvis.Y - thrower.Pelvis.Y >= ClipFeasibility.FulcrumDrop, name);
            Assert.InRange(thrower.LeanDegrees, ClipFeasibility.MinimumLoadLean, ClipFeasibility.MaximumLoadLean);
        }
    }

    [Fact]
    public void FiremansCarrySeatsTheBodyHighAcrossTheShoulders()
    {
        ClipKey carried = Clip("anim.fireman_carry.drop").Keys[^1];
        (ClipKinematics.Trunk carrier, ClipKinematics.Trunk body) = ClipFeasibility.Place(carried, ClipKinematics.Body.Reference, false, false);
        Assert.Equal(AnchorPoint.Shoulders, carried.Opponent.Anchor);
        // Across the shoulders the carried hips are well above the carrier's neck.
        Assert.True(body.Pelvis.Y > carrier.Neck.Y + 0.15f);
    }

    [Fact]
    public void SprawlDropsUnderGravityWithoutLiftingOffTheMat()
    {
        MoveClip sprawl = Clip("anim.sprawl.sprawl");
        Assert.All(sprawl.Keys, k => Assert.Equal(0f, k.Executor.Lift));
        Assert.Equal(GroundState.Prone, sprawl.Keys[^1].Executor.Ground);
        Assert.True(sprawl.Keys[^1].Executor.X < -0.3f);
    }

    [Fact]
    public void SeatRadiiFollowTheAnatomyOfEachSite()
    {
        // Spine to held waist for a 1.75 m carrier holding a body 0.11 m thick at the contact.
        Assert.Equal(0.24f, SeatRadius(AnchorPoint.Hips), 2);
        Assert.Equal(0.24f, SeatRadius(AnchorPoint.LowerBack), 2);
        Assert.Equal(0.26f, SeatRadius(AnchorPoint.Chest), 2);
        Assert.Equal(0.30f, SeatRadius(AnchorPoint.Shoulders), 2);
        Assert.Equal(0.35f, SeatRadius(AnchorPoint.LeftShoulder), 2);
        Assert.Equal(0.35f, SeatRadius(AnchorPoint.RightShoulder), 2);
    }

    [Fact]
    public void ASingleShoulderSeatPutsTheHeldHipsOutBeyondTheCarriersShoulder()
    {
        ClipKinematics.Body body = ClipKinematics.Body.Reference;
        ClipKinematics.Trunk carrier = ClipKinematics.Place(Standing(0.8f, 1f), body, false);
        foreach ((AnchorPoint point, float sign) in new[] { (AnchorPoint.LeftShoulder, 1f), (AnchorPoint.RightShoulder, -1f) })
        {
            System.Numerics.Vector3 seat = carrier.SeatFor(point, 0.11f, body.Height) - carrier.Neck;
            float outward = System.Numerics.Vector3.Dot(seat, carrier.Left) * sign;
            Assert.True(outward > body.ShoulderHalfWidth, $"{point}: {outward} m out");
            Assert.True(System.Numerics.Vector3.Dot(seat, carrier.Spine) > 0f, $"{point}: above the shoulder line");
        }
    }

    [Fact]
    public void HeldTrunksMayPressTogetherButNotCross()
    {
        // A body lock: the held body seated chest to chest. Both leaning in at the stance's default
        // (about 34°) the trunks cross; both upright they meet.
        ActorKey Held(float pitch) => new() { X = 0.62f, Heading = 180f, Ground = GroundState.Standing, Level = 0.75f, Posture = 0.85f, RollSide = 1, Pitch = pitch, Anchor = AnchorPoint.Chest, AnchorWeight = 1f, Contact = 0.11f };
        ActorKey Lifter(float pitch) => new() { X = 0.22f, Ground = GroundState.Standing, Level = 0.75f, Posture = 0.85f, RollSide = 1, Pitch = pitch };
        MoveClip crossed = Make(ThrowSpec.NoThrow, Key(0f, Lifter(0f), Held(0f)), Key(1f, Lifter(0f), Held(0f)));
        MoveClip upright = Make(ThrowSpec.NoThrow, Key(0f, Lifter(-34f), Held(-30f)), Key(1f, Lifter(-34f), Held(-30f)));
        Assert.Contains(ClipFeasibility.Check(crossed, 20), e => e.Contains("held trunks overlap", StringComparison.Ordinal));
        Assert.DoesNotContain(ClipFeasibility.Check(upright, 20), e => e.Contains("held trunks overlap", StringComparison.Ordinal));
    }

    [Fact]
    public void CompactBaseSitsLowerThanHandsAndKnees()
    {
        ClipKinematics.Body body = ClipKinematics.Body.Reference;
        var defending = new ActorKey { Ground = GroundState.BothKnees, Level = 0.4f, Posture = 0.3f, RollSide = 1 };
        float compact = ClipKinematics.Place(defending, body, compactBase: true).Pelvis.Y;
        float quadruped = ClipKinematics.Place(defending, body, compactBase: false).Pelvis.Y;
        Assert.InRange(compact, 0.16f, 0.28f);
        Assert.True(quadruped - compact > 0.15f);
        // Rising out of it (posture over 0.5) is back on hands and knees.
        Assert.Equal(0f, ClipKinematics.CompactBase(0.55f));
        Assert.Equal(1f, ClipKinematics.CompactBase(0.3f));
    }

    [Fact]
    public void RejectsADropFasterThanFreeFall()
    {
        // From stance to the mat in a sixth of a second.
        MoveClip clip = Make(ThrowSpec.NoThrow, Key(0f, Standing(0.75f)), Key(1f, new ActorKey { Ground = GroundState.Prone, Level = 0.2f, Posture = 1f, RollSide = 1 }));
        Assert.Contains(ClipFeasibility.Check(clip, 10), e => e.Contains("free fall"));
        Assert.Empty(ClipFeasibility.Check(clip, 24));
    }

    [Fact]
    public void AFallMayPassThroughAKeyAtSpeed()
    {
        // One continuous drop over three keys: the middle key is not a rest, so only the whole drop counts.
        MoveClip clip = Make(ThrowSpec.NoThrow, Key(0f, Standing(0.75f)), Key(0.5f, Standing(0.55f)), Key(1f, Standing(0.3f)));
        Assert.Empty(ClipFeasibility.Check(clip, 16));
        // Rising to a new top and falling again: the second fall starts from rest at the top.
        MoveClip twice = Make(ThrowSpec.NoThrow, Key(0f, Standing(0.75f)), Key(0.5f, Standing(0.9f)), Key(0.6f, Standing(0.3f)), Key(1f, Standing(0.3f)));
        Assert.Contains(ClipFeasibility.Check(twice, 30), e => e.Contains("keys[1..2]"));
    }

    [Fact]
    public void RejectsLiftingABodyOnTheMatOutsideAThrow()
    {
        var lifted = new ActorKey { Ground = GroundState.Prone, Level = 0.2f, Posture = 1f, Lift = 0.1f, RollSide = 1 };
        MoveClip clip = Make(ThrowSpec.NoThrow, Key(0f, Standing(0.75f), lifted), Key(1f, Standing(0.75f), lifted));
        Assert.Contains(ClipFeasibility.Check(clip, 20), e => e.Contains("lift"));
        var throwing = new ThrowSpec { Kind = ThrowKind.Throw, Pivot = "Spine2", Axis = ThrowAxis.Lateral, Degrees = 90f, Grips = new[] { new ThrowGrip { Thrower = "LeftForeArm", Thrown = "Spine2" } } };
        Assert.DoesNotContain(ClipFeasibility.Check(Make(throwing, clip.Keys.ToArray()), 20), e => e.Contains("lift"));
    }

    [Fact]
    public void RejectsAHipThrowLoadedFromAbove()
    {
        var load = new ThrowSpec { Kind = ThrowKind.Load, Pivot = "Hips", From = 0f, To = 1f };
        ActorKey opponent = Standing(0.75f);
        // Upright and as high as the opponent: no fulcrum under them.
        MoveClip upright = Make(load, Key(0f, Standing(0.75f, 0.85f), opponent), Key(1f, Standing(0.75f, 0.85f), opponent));
        IReadOnlyList<string> errors = ClipFeasibility.Check(upright, 30);
        Assert.Contains(errors, e => e.Contains("under the opponent"));
        Assert.Contains(errors, e => e.Contains("leans"));
        // Hips sunk under theirs, trunk bent over them.
        MoveClip loaded = Make(load, Key(0f, Standing(0.3f, 0.45f), opponent), Key(1f, Standing(0.3f, 0.45f), opponent));
        Assert.Empty(ClipFeasibility.Check(loaded, 30));
    }

    [Fact]
    public void AThrowerFallingWithTheThrownBodyIsPulledDown()
    {
        var throwing = new ThrowSpec { Kind = ThrowKind.Throw, Pivot = "Spine2", Axis = ThrowAxis.Lateral, Degrees = 120f, Grips = new[] { new ThrowGrip { Thrower = "LeftForeArm", Thrown = "Spine2" } } };
        ActorKey down = new() { Ground = GroundState.BothKnees, Level = 0.2f, Posture = 0.2f, RollSide = 1 };
        Assert.Empty(ClipFeasibility.Check(Make(throwing, Key(0f, Standing(0.75f)), Key(1f, down)), 10));
        Assert.NotEmpty(ClipFeasibility.Check(Make(ThrowSpec.NoThrow, Key(0f, Standing(0.75f)), Key(1f, down)), 10));
    }

    private static float SeatRadius(AnchorPoint point)
    {
        (float _, float _, float _, float _, float depth) = ClipKinematics.Seat(point);
        return (depth * 1.75f) + 0.11f;
    }

    private static MoveClip Clip(string id) => Files.SelectMany(f => f.Clips).Single(c => c.Id == id);

    private static MoveClip Make(ThrowSpec spec, params ClipKey[] keys) => new() { Id = "anim.test", Frame = ClipFrame.Executor, Keys = keys, Throw = spec };

    private static ActorKey Standing(float level, float posture = 0.8f) => new() { Ground = GroundState.Standing, Level = level, Posture = posture, RollSide = 1 };

    private static ClipKey Key(float t, ActorKey executor, ActorKey? opponent = null) => new()
    {
        T = t,
        Executor = executor,
        Opponent = opponent ?? new ActorKey { X = 0.8f, Heading = 180f, Ground = GroundState.Standing, Level = 0.75f, Posture = 0.8f, RollSide = 1 },
    };
}
