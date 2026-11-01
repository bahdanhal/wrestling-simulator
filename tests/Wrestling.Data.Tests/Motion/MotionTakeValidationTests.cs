using Wrestling.Data.Motion;

namespace Wrestling.Data.Tests.Motion;

public class MotionTakeValidationTests
{
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ValidateRejectsNonfiniteFrameRate(float fps)
    {
        IReadOnlyList<string> errors = Take(fps).Validate();
        Assert.Contains(errors, error => error.Contains("framesPerSecond"));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ValidateRejectsNonfiniteActorHeight(float height)
    {
        MotionTake take = Take();
        take = new MotionTake { Id = take.Id, Source = take.Source, FramesPerSecond = take.FramesPerSecond,
            Actors = new[] { new MotionActor { HeightMetres = height }, take.Actors[1] }, Frames = take.Frames };
        IReadOnlyList<string> errors = take.Validate();
        Assert.Contains(errors, error => error.Contains("height"));
    }

    [Fact]
    public void ValidateReportsNullNestedCollectionsInsteadOfThrowing()
    {
        MotionTake valid = Take();
        MotionTake nullActors = new() { Id = "null-actors", Source = valid.Source, FramesPerSecond = 20, Actors = null!, Frames = valid.Frames };
        MotionTake nullFrame = new() { Id = "null-frame", Source = valid.Source, FramesPerSecond = 20, Actors = valid.Actors,
            Frames = new IReadOnlyList<IReadOnlyList<float>>[] { null!, valid.Frames[1] } };
        MotionTake nullActor = new() { Id = "null-actor", Source = valid.Source, FramesPerSecond = 20,
            Actors = new MotionActor[] { null!, valid.Actors[1] }, Frames = valid.Frames };
        MotionTake nullJointList = new() { Id = "null-joints", Source = valid.Source, FramesPerSecond = 20, Actors = valid.Actors,
            Frames = new IReadOnlyList<IReadOnlyList<float>>[] { new IReadOnlyList<float>[] { null!, valid.Frames[0][1] }, valid.Frames[1] } };
        MotionTake nullFrames = new() { Id = "null-frames", Source = valid.Source, FramesPerSecond = 20, Actors = valid.Actors, Frames = null! };

        Assert.Contains(nullActors.Validate(), error => error.Contains("actors must not be null"));
        Assert.Contains(nullFrame.Validate(), error => error.Contains("frame 0 must not be null"));
        Assert.Contains(nullActor.Validate(), error => error.Contains("actor 0 must not be null"));
        Assert.Contains(nullJointList.Validate(), error => error.Contains("null actor joint list"));
        Assert.Contains(nullFrames.Validate(), error => error.Contains("frames must not be null"));
    }

    [Fact]
    public void JointRejectsNonfiniteFrameAndInvalidActorOrJoint()
    {
        MotionTake take = Take();
        Assert.Throws<ArgumentOutOfRangeException>(() => take.Joint(float.NaN, 0, MotionJoint.Pelvis));
        Assert.Throws<ArgumentOutOfRangeException>(() => take.Joint(float.PositiveInfinity, 0, MotionJoint.Pelvis));
        Assert.Throws<ArgumentOutOfRangeException>(() => take.Joint(0, -1, MotionJoint.Pelvis));
        Assert.Throws<ArgumentOutOfRangeException>(() => take.Joint(0, 2, MotionJoint.Pelvis));
        Assert.Throws<ArgumentOutOfRangeException>(() => take.Joint(0, 0, (MotionJoint)(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => take.Joint(0, 0, (MotionJoint)MotionTake.JointCount));
    }

    [Fact]
    public void JointFailsClearlyWhenNestedSampleDataIsNull()
    {
        MotionTake take = Take();
        MotionTake nullFrame = new() { Id = take.Id, Source = take.Source, FramesPerSecond = take.FramesPerSecond, Actors = take.Actors,
            Frames = new IReadOnlyList<IReadOnlyList<float>>[] { null!, take.Frames[1] } };
        MotionTake nullJoints = new() { Id = take.Id, Source = take.Source, FramesPerSecond = take.FramesPerSecond, Actors = take.Actors,
            Frames = new IReadOnlyList<IReadOnlyList<float>>[] { new IReadOnlyList<float>[] { null!, take.Frames[0][1] }, take.Frames[1] } };
        Assert.Throws<InvalidOperationException>(() => nullFrame.Joint(0, 0, MotionJoint.Pelvis));
        Assert.Throws<InvalidOperationException>(() => nullJoints.Joint(0, 0, MotionJoint.Pelvis));
    }

    private static MotionTake Take(float fps = 20)
    {
        float[] actorValues = new float[MotionTake.JointCount * 3];
        IReadOnlyList<float>[] frame = { actorValues, actorValues };
        return new MotionTake
        {
            Id = "test",
            Source = new MotionSource { License = "test", Notes = "test" },
            FramesPerSecond = fps,
            Actors = new[] { new MotionActor { HeightMetres = 1.7f }, new MotionActor { HeightMetres = 1.8f } },
            Frames = new IReadOnlyList<IReadOnlyList<float>>[] { frame, frame },
        };
    }
}
