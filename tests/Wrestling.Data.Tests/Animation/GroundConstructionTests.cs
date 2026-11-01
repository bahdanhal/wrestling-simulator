using System.Numerics;
using Wrestling.Core.Match;
using Wrestling.Data.Animation;
using Wrestling.Data.Json;

namespace Wrestling.Data.Tests.Animation;

public class GroundConstructionTests
{
    [Fact]
    public void RuntimeConstructionMetadataIsNotPartOfAuthoredContent()
    {
        ActorKey sampled = Transition(GroundState.Standing, GroundState.Prone).Sample(0.25f).Executor;
        string json = ContentJson.Serialize(sampled);
        Assert.DoesNotContain("groundFrom", json);
        Assert.DoesNotContain("groundTo", json);
        Assert.DoesNotContain("groundBlend", json);
        ActorKey reloaded = ContentJson.Deserialize<ActorKey>(json, "sample");
        Assert.Null(reloaded.GroundFrom);
        Assert.Null(reloaded.GroundTo);
    }

    [Fact]
    public void ShippedGroundTransitionsHaveNoMidpointOrKeyBoundaryJump()
    {
        foreach (string file in Directory.GetFiles(TestContent.PathOf("animation"), "*.json"))
        {
            foreach (MoveClip clip in MoveAnimationLoader.FromJson(File.ReadAllText(file), file).Clips)
            {
                for (int i = 1; i < clip.Keys.Count; i++)
                {
                    float midpoint = (clip.Keys[i - 1].T + clip.Keys[i].T) * 0.5f;
                    Continuous(clip, midpoint);
                    if (i < clip.Keys.Count - 1) Continuous(clip, clip.Keys[i].T);
                }
            }
        }
    }

    private static void Continuous(MoveClip clip, float t)
    {
        var before = clip.Sample(t - 0.000001f);
        var after = clip.Sample(t + 0.000001f);
        foreach (var pair in new[] { (before.Executor, after.Executor), (before.Opponent, after.Opponent) })
        {
            var a = ClipKinematics.Place(pair.Item1, ClipKinematics.Body.Reference, false);
            var b = ClipKinematics.Place(pair.Item2, ClipKinematics.Body.Reference, false);
            Assert.True(Vector3.Distance(a.Pelvis, b.Pelvis) < 0.0005f, clip.Id + " pelvis at " + t);
            Assert.True(Vector3.Distance(a.Neck, b.Neck) < 0.0005f, clip.Id + " neck at " + t);
        }
    }

    [Fact]
    public void GroundConstructionChangesContinuouslyAcrossTheAuthoredInterval()
    {
        var clip = Transition(GroundState.Standing, GroundState.Prone);
        var before = Place(clip, 0.49999f);
        var after = Place(clip, 0.50001f);
        Assert.True(Vector3.Distance(before.Pelvis, after.Pelvis) < 0.0001f);
        Assert.True(Vector3.Distance(before.Neck, after.Neck) < 0.0001f);
        float torso = ClipKinematics.Body.Reference.Torso;
        for (int i = 0; i <= 100; i++)
        {
            var trunk = Place(clip, i / 100f);
            Assert.InRange((trunk.Neck - trunk.Pelvis).Length(), torso - 0.00001f, torso + 0.00001f);
            Assert.InRange(Math.Abs(Vector3.Dot(trunk.Spine, trunk.Front)), 0f, 0.00001f);
        }
    }

    [Fact]
    public void KneelingRollDoesNotSwitchConstructionAtFortyFiveDegrees()
    {
        var clip = new MoveClip
        {
            Id = "roll",
            Keys = new[]
            {
                new ClipKey { T = 0f, Executor = new ActorKey { Ground = GroundState.BothKnees, Roll = 0f, Level = 0.3f, Posture = 0.5f } },
                new ClipKey { T = 1f, Executor = new ActorKey { Ground = GroundState.BothKnees, Roll = 90f, Level = 0.3f, Posture = 0.5f } },
            },
        };
        var before = Place(clip, 0.49999f);
        var after = Place(clip, 0.50001f);
        Assert.True(Vector3.Distance(before.Pelvis, after.Pelvis) < 0.0001f);
        Assert.True(Vector3.Distance(before.Neck, after.Neck) < 0.0001f);
    }

    [Fact]
    public void InterpolatedPathDiagnosticFindsDropsHiddenBetweenValidKeys()
    {
        var clip = Transition(GroundState.Standing, GroundState.Prone);
        // The endpoints allow enough time for the total fall. Smoothstep's early acceleration
        // still exceeds free fall between them, which endpoint-only checks cannot establish.
        Assert.Empty(ClipFeasibility.Check(clip, 22));
        Assert.NotEmpty(ClipFeasibility.CheckSampledPath(clip, 22));
        Assert.Empty(ClipFeasibility.CheckSampledPath(clip, 120));
    }

    private static MoveClip Transition(GroundState from, GroundState to) => new MoveClip
    {
        Id = "construction",
        Keys = new[]
        {
            new ClipKey { T = 0f, Executor = new ActorKey { Ground = from, Level = 0.6f, Posture = 0.6f } },
            new ClipKey { T = 1f, Executor = new ActorKey { Ground = to, Level = 0.6f, Posture = 0.6f } },
        },
    };

    private static ClipKinematics.Trunk Place(MoveClip clip, float t) =>
        ClipKinematics.Place(clip.Sample(t).Executor, ClipKinematics.Body.Reference, false);
}
