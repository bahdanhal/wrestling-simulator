using Wrestling.Data.Motion;

namespace Wrestling.Data.Tests.Motion;

public class PairedMotionSearchTests
{
    private static MotionTake Take(string id, float fps = 10, float speed = 1)
    {
        var frames = new List<IReadOnlyList<IReadOnlyList<float>>>();
        for (int f = 0; f < 4; f++)
        {
            var actors = new List<IReadOnlyList<float>>();
            for (int a = 0; a < 2; a++)
            {
                var values = new float[MotionTake.JointCount * 3];
                for (int j = 0; j < MotionTake.JointCount; j++)
                {
                    values[j * 3] = a * 2 + j * .03f + (j == (int)MotionJoint.HandL ? f * speed / fps : 0);
                    values[j * 3 + 1] = j * .04f;
                    values[j * 3 + 2] = j * .02f;
                }
                actors.Add(values);
            }
            frames.Add(actors);
        }
        return new MotionTake { Id = id, Source = new MotionSource { License = "test", Notes = "test" }, FramesPerSecond = fps, Actors = new[] { new MotionActor { HeightMetres = 1.7f }, new MotionActor { HeightMetres = 1.7f } }, Frames = frames };
    }

    private static PairedMotionRange Range(MotionTake t) => new() { Take = t, FromFrame = 0, ToFrame = 3 };

    [Fact]
    public void FindsIdentityAndMirrorAsOnePair()
    {
        var take = Take("a");
        var search = new PairedMotionSearch(new[] { Range(take) });
        var identity = search.Find(new PairedMotionSample(take, 1))!;
        Assert.Equal(1, identity.Frame);
        Assert.False(identity.Mirrored);
        Assert.Equal(0, identity.Score, 8);
        var mirror = search.Find(new PairedMotionSample(take, 1, mirrored: true))!;
        Assert.Equal(1, mirror.Frame);
        Assert.True(mirror.Mirrored);
        Assert.Equal(0, mirror.Score, 8);
        Assert.Equal(.2f, mirror.RemainingDurationSeconds, 5);
    }

    [Fact]
    public void VelocityUsesSecondsAndContinuityKeepsSourceTime()
    {
        var slow = Take("a", 10);
        var fast = Take("b", 20);
        var search = new PairedMotionSearch(new[] { Range(fast), Range(slow) });
        var match = search.Find(new PairedMotionSample(slow, 1), previous: new PairedMotionSample(slow, 0), expectedAdvanceSeconds: .1f)!;
        Assert.Same(slow, match.Range.Take);
        Assert.Equal(1, match.Frame);
        Assert.Equal(.1f, match.SourceTimeSeconds, 5);
        var sameSpeed = new PairedMotionSearch(new[] { Range(fast) }).Find(new PairedMotionSample(slow, 0))!;
        Assert.Equal(0, sameSpeed.Score, 8);
    }

    [Fact]
    public void RequiresExplicitMetadataAndUsesStableTies()
    {
        var a = Take("a", speed: 0);
        var b = Take("b", speed: 0);
        var search = new PairedMotionSearch(new[] { Range(b), Range(a) });
        Assert.Null(search.Find(new PairedMotionSample(a, 0), contact: "grip"));
        Assert.Null(search.Find(new PairedMotionSample(a, 0), state: "standing"));
        var match = search.Find(new PairedMotionSample(a, 0))!;
        Assert.Equal("a", match.Range.Take.Id);
        Assert.Equal(0, match.Frame);
        Assert.False(match.Mirrored);
    }

    [Fact]
    public void RegistersTranslationAndYawAndRequiresRemainingTime()
    {
        var original = Take("a");
        var rotatedFrames = original.Frames.Select(frame => (IReadOnlyList<IReadOnlyList<float>>)frame.Select(actor =>
        {
            var copy = actor.ToArray();
            for (int j = 0; j < MotionTake.JointCount; j++)
            {
                float x = copy[j * 3], z = copy[j * 3 + 2];
                copy[j * 3] = -z + 4;
                copy[j * 3 + 2] = x - 3;
            }
            return (IReadOnlyList<float>)copy;
        }).ToArray()).ToArray();
        var rotated = new MotionTake { Id = "rotated", Source = original.Source, FramesPerSecond = original.FramesPerSecond, Actors = original.Actors, Frames = rotatedFrames };
        var search = new PairedMotionSearch(new[] { Range(original) });
        var match = search.Find(new PairedMotionSample(rotated, 1), minimumRemainingSeconds: .15f)!;
        Assert.Equal(1, match.Frame);
        Assert.True(match.Score < 1e-9);
        Assert.Null(search.Find(new PairedMotionSample(rotated, 1), minimumRemainingSeconds: 1));
    }

    [Fact]
    public void HeightWeightsAndRangeBoundariesAreRespected()
    {
        var original = Take("original");
        var liftedFrames = original.Frames.Select(frame => (IReadOnlyList<IReadOnlyList<float>>)frame.Select(actor =>
        {
            var copy = actor.ToArray();
            for (int j = 0; j < MotionTake.JointCount; j++) copy[(j * 3) + 1] += 1;
            return (IReadOnlyList<float>)copy;
        }).ToArray()).ToArray();
        var lifted = new MotionTake { Id = "lifted", Source = original.Source, FramesPerSecond = original.FramesPerSecond, Actors = original.Actors, Frames = liftedFrames };
        var range = new PairedMotionRange { Take = original, FromFrame = 1, ToFrame = 2 };
        var search = new PairedMotionSearch(new[] { range });
        var boundary = search.Sample(range, 1);
        Assert.Equal(0, search.Find(boundary)!.Score, 8);
        Assert.True(search.Find(new PairedMotionSample(lifted, 1))!.Score > 1.9);
        var noPose = new PairedMotionSearch(new[] { range }, new PairedMotionWeights { Pose = 0 });
        Assert.Equal(0, noPose.Find(new PairedMotionSample(lifted, 1))!.Score, 8);
        Assert.Throws<ArgumentException>(() => search.Sample(range, 0));
        Assert.Throws<ArgumentException>(() => new PairedMotionSearch(new[] { range }, new PairedMotionWeights { Velocity = double.NaN }));
    }

    [Fact]
    public void RangeVelocityIgnoresUnlabelledFrames()
    {
        var original = Take("original");
        var changedFrames = original.Frames.Select((frame, index) => (IReadOnlyList<IReadOnlyList<float>>)frame.Select(actor =>
        {
            var copy = actor.ToArray();
            if (index == 0) copy[(int)MotionJoint.HandL * 3] += 10;
            return (IReadOnlyList<float>)copy;
        }).ToArray()).ToArray();
        var changed = new MotionTake { Id = "changed", Source = original.Source, FramesPerSecond = original.FramesPerSecond, Actors = original.Actors, Frames = changedFrames };
        var a = new PairedMotionRange { Take = original, FromFrame = 1, ToFrame = 2 };
        var b = new PairedMotionRange { Take = changed, FromFrame = 1, ToFrame = 2 };
        var queryIndex = new PairedMotionSearch(new[] { b });
        var search = new PairedMotionSearch(new[] { a });
        Assert.Equal(0, search.Find(queryIndex.Sample(b, 1))!.Score, 8);
    }

    [Fact]
    public void StandaloneBoundedSampleCanAdvanceWithoutLosingRange()
    {
        var take = Take("a");
        var range = new PairedMotionRange { Take = take, FromFrame = 1, ToFrame = 2 };
        var sample = PairedMotionSample.FromRange(range, 1, true).AtFrame(2);
        Assert.True(sample.Mirrored);
        Assert.Equal(2, sample.Frame);
        Assert.Equal(0, new PairedMotionSearch(new[] { range }).Find(sample)!.Score, 8);
        Assert.Throws<ArgumentOutOfRangeException>(() => sample.AtFrame(3));
        Assert.Throws<ArgumentException>(() => PairedMotionSample.FromRange(range, float.NaN));
        Assert.Throws<ArgumentException>(() => PairedMotionSample.FromRange(new PairedMotionRange { Take = take, FromFrame = 2, ToFrame = 1 }, 1));
    }

    [Fact]
    public void ActorOrderAndExplicitAnnotationsArePreserved()
    {
        var take = Take("a");
        var range = new PairedMotionRange { Take = take, FromFrame = 0, ToFrame = 3, FirstActor = 1, State = "standing", Contact = "grip" };
        var search = new PairedMotionSearch(new[] { range });
        var match = search.Find(new PairedMotionSample(take, 1, firstActor: 1), state: "standing", contact: "grip")!;
        Assert.Same(range, match.Range);
        Assert.Equal(1, match.Range.FirstActor);
        Assert.Equal(0, match.Score, 8);
        Assert.Null(search.Find(new PairedMotionSample(take, 1), contact: "no grip"));
    }

    [Fact]
    public void RejectsInvalidInputs()
    {
        var take = Take("a");
        Assert.Throws<ArgumentException>(() => new PairedMotionSample(take, float.NaN));
        Assert.Throws<ArgumentException>(() => new PairedMotionSearch(new[] { new PairedMotionRange { Take = take, FromFrame = 2, ToFrame = 1 } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PairedMotionSearch(new[] { Range(take) }).Find(new PairedMotionSample(take, 0), continuityWeight: -1));
    }

    [Fact]
    public void SearchesEachRealTake()
    {
        string folder = TestContent.PathOf("motion/takes");
        var takes = Directory.GetFiles(folder, "*.json").Select(p => MotionLoader.TakeFromJson(File.ReadAllText(p), p)).ToArray();
        Assert.Equal(3, takes.Length);
        foreach (var take in takes)
        {
            var range = new PairedMotionRange { Take = take, FromFrame = 0, ToFrame = Math.Min(5, take.FrameCount - 1) };
            var match = new PairedMotionSearch(new[] { range }).Find(new PairedMotionSample(take, 2))!;
            Assert.Equal(2, match.Frame);
            Assert.Equal(0, match.Score, 8);
        }
    }
}
