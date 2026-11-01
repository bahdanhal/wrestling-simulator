using Wrestling.Data.Json;
using Wrestling.Data;
using Wrestling.Data.Motion;

namespace Wrestling.Data.Tests.Motion;

public class MotionContactTests
{
    private static MotionTake Take() => new() { Id = "recording", FramesPerSecond = 30, Frames = Enumerable.Repeat<IReadOnlyList<IReadOnlyList<float>>>(Array.Empty<IReadOnlyList<float>>(), 11).ToArray() };
    private static MotionContact Contact(int from = 1, int to = 9) => new() { SourceActor = 0, SourceJoint = MotionJoint.HandL, SourceSide = ContactSide.Left, Point = ContactPoint.Palm, TargetKind = ContactTargetKind.Actor, TargetActor = 1, TargetSurface = ContactSurface.Forearm, TargetSide = ContactSide.Right, Anchor = new ContactVector { X = 0.2f, Y = 0.3f, Z = 0.4f }, FromFrame = from, ToFrame = to, ActivationFrames = 2, ReleaseFrames = 2, Origin = ContactOrigin.Inferred, Evidence = "Video obscures the palm", Confidence = 0.4f };
    private static MotionContactSet Set(params MotionContact[] contacts) => new() { SchemaVersion = 1, Take = "recording", FrameCount = 11, FramesPerSecond = 30, Contacts = contacts };

    [Fact]
    public void SamplingMapsMirrorAndActorOrderAndFades()
    {
        MotionContactSet set = Set(Contact());
        Assert.Empty(set.Validate(Take()));
        Assert.Empty(set.Sample(0));
        MotionContactSample sample = Assert.Single(set.Sample(2, mirrored: true, reverseActors: true));
        Assert.Equal(0.5f, sample.Weight);
        Assert.Equal(1, sample.SourceActor);
        Assert.Equal(0, sample.TargetActor);
        Assert.Equal(MotionJoint.HandR, sample.SourceJoint);
        Assert.Equal(ContactSide.Right, sample.SourceSide);
        Assert.Equal(ContactSide.Left, sample.TargetSide);
        Assert.Equal(0.2f, sample.Anchor.X);
        Assert.Equal(0.5f, Assert.Single(set.Sample(8)).Weight);
    }

    [Fact]
    public void RejectsMismatchedTakeOverlapsAndInvalidEvidence()
    {
        MotionContact bad = new() { SourceActor = 0, SourceJoint = MotionJoint.HandL, SourceSide = ContactSide.Left, Point = ContactPoint.Wrist, TargetKind = ContactTargetKind.Actor, TargetActor = 0, TargetSurface = ContactSurface.Torso, TargetSide = ContactSide.Centre, Anchor = new ContactVector { X = float.NaN }, FromFrame = 3, ToFrame = 8, Origin = ContactOrigin.Manual, Evidence = "", Confidence = 2 };
        var errors = Set(Contact(), bad).Validate(Take());
        Assert.Contains(errors, e => e.Contains("target"));
        Assert.Contains(errors, e => e.Contains("anchor"));
        Assert.Contains(errors, e => e.Contains("evidence"));
        Assert.Contains(errors, e => e.Contains("overlaps"));
        Assert.Contains(new MotionContactSet { SchemaVersion = 1, Take = "other", FrameCount = 11, FramesPerSecond = 30 }.Validate(Take()), e => e.Contains("match"));
    }


    [Fact]
    public void WrongRateAndOverlappingSameChannelAreRejectedButAdjacentIntervalsAreAllowed()
    {
        var wrongRate = new MotionContactSet { SchemaVersion = 1, Take = "recording", FrameCount = 11, FramesPerSecond = 29.97f };
        Assert.Contains(wrongRate.Validate(Take()), e => e.Contains("frame rate"));
        Assert.Contains(Set(Contact(1, 7), Contact(6, 9)).Validate(Take()), e => e.Contains("overlaps"));
        Assert.Empty(Set(Contact(1, 5), Contact(5, 9)).Validate(Take()));
    }

    [Fact]
    public void MatAnchorStaysInTakeWorldSpaceAcrossMirroringAndReversal()
    {
        MotionContact mat = new() { SourceActor = 1, SourceJoint = MotionJoint.AnkleR, SourceSide = ContactSide.Right, Point = ContactPoint.Sole, TargetKind = ContactTargetKind.Mat, TargetSurface = ContactSurface.Mat, TargetSide = ContactSide.Centre, Anchor = new ContactVector { X = 7, Y = 0, Z = -3 }, FromFrame = 1, ToFrame = 9, ActivationFrames = 2, ReleaseFrames = 2, Origin = ContactOrigin.Authored, Evidence = "Artist-specified post", Confidence = 1 };
        MotionContactSet set = Set(mat);
        Assert.Empty(set.Validate(Take()));
        MotionContactSample sample = Assert.Single(set.Sample(5, mirrored: true, reverseActors: true));
        Assert.Equal(0, sample.SourceActor);
        Assert.Null(sample.TargetActor);
        Assert.Equal(MotionJoint.AnkleL, sample.SourceJoint);
        Assert.Equal(7, sample.Anchor.X);
        Assert.Equal(-3, sample.Anchor.Z);
        Assert.Equal(1, sample.Weight);
        Assert.Equal(0, Assert.Single(set.Sample(1)).Weight);
        Assert.Equal(0, Assert.Single(set.Sample(9)).Weight);
        Assert.Empty(set.Sample(9.1f));
    }

    [Fact]
    public void LoaderRejectsInvalidSidecarAndAllowsAbsentOne()
    {
        string json = ContentJson.Serialize(Set(Contact(1, 20)));
        Assert.Throws<ContentLoadException>(() => MotionContactLoader.FromJson(json, "contact.json", Take()));
        Assert.Null(MotionContactLoader.FromDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), Take()));
    }
    [Fact]
    public void SharedBoundaryRequiresAtLeastOneZeroWeightEndpoint()
    {
        MotionContact NoFade(int from, int to) => new() { SourceActor = 0, SourceJoint = MotionJoint.HandL, SourceSide = ContactSide.Left, Point = ContactPoint.Wrist, TargetKind = ContactTargetKind.Mat, TargetSurface = ContactSurface.Mat, TargetSide = ContactSide.Centre, Anchor = new ContactVector(), FromFrame = from, ToFrame = to, Origin = ContactOrigin.Authored, Evidence = "Artist post", Confidence = 1 };
        Assert.Contains(Set(NoFade(1, 5), NoFade(5, 9)).Validate(Take()), e => e.Contains("overlaps"));
        Assert.Empty(Set(NoFade(1, 5), Contact(5, 9)).Validate(Take()));
        Assert.Single(Set(NoFade(1, 5), Contact(5, 9)).Sample(5), c => c.Weight > 0);
    }

    [Fact]
    public void DirectoryLoaderLoadsAndCrossChecksAllContactSidecars()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, "takes"));
        Directory.CreateDirectory(Path.Combine(root, "contacts"));
        try
        {
            var joints = Enumerable.Repeat(0f, MotionTake.JointCount * 3).ToArray();
            var frame = new IReadOnlyList<float>[] { joints, joints };
            var take = new MotionTake { Id = "recording", FramesPerSecond = 30, Source = new MotionSource { License = "permission", Notes = "test" }, Actors = new[] { new MotionActor { HeightMetres = 1.7f }, new MotionActor { HeightMetres = 1.8f } }, Frames = Enumerable.Repeat<IReadOnlyList<IReadOnlyList<float>>>(frame, 11).ToArray() };
            File.WriteAllText(Path.Combine(root, "index.json"), ContentJson.Serialize(new MotionIndexFile { SchemaVersion = 1 }));
            File.WriteAllText(Path.Combine(root, "takes", "recording.json"), ContentJson.Serialize(take));
            string contactPath = Path.Combine(root, "contacts", "recording.json");
            File.WriteAllText(contactPath, ContentJson.Serialize(Set(Contact())));
            var animations = GameContent.Load(TestContent.PathOf("")).Animations;
            var library = MotionLoader.FromDirectory(root, animations);
            Assert.True(library.TryGetContacts("recording", out MotionContactSet contacts));
            Assert.Single(contacts.Contacts);
            Assert.False(library.TryGetContacts("missing", out _));

            File.Move(contactPath, Path.Combine(root, "contacts", "wrong.json"));
            Assert.Throws<ContentLoadException>(() => MotionLoader.FromDirectory(root, animations));
            File.Delete(Path.Combine(root, "contacts", "wrong.json"));
            File.WriteAllText(Path.Combine(root, "contacts", "unknown.json"), ContentJson.Serialize(new MotionContactSet { SchemaVersion = 1, Take = "unknown", FrameCount = 11, FramesPerSecond = 30 }));
            Assert.Throws<ContentLoadException>(() => MotionLoader.FromDirectory(root, animations));
            File.Delete(Path.Combine(root, "contacts", "unknown.json"));
            File.WriteAllText(contactPath, ContentJson.Serialize(new MotionContactSet { SchemaVersion = 1, Take = "recording", FrameCount = 11, FramesPerSecond = 29 }));
            Assert.Throws<ContentLoadException>(() => MotionLoader.FromDirectory(root, animations));
        }
        finally { Directory.Delete(root, true); }
    }

}
