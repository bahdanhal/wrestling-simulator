using Wrestling.Data.Animation;
using Wrestling.Data.Json;
using Wrestling.Data.Motion;
using Wrestling.Data.Techniques;

namespace Wrestling.Data.Tests.Motion;

public class MotionImportTests
{
    /// <summary>A minimal Mixamo-named skeleton: hips at 100 cm, legs, spine, neck, head, arms, in centimetres.</summary>
    private const string Skeleton = """
        HIERARCHY
        ROOT mixamorig:Hips
        {
          OFFSET 0 0 0
          CHANNELS 6 Xposition Yposition Zposition Zrotation Xrotation Yrotation
          JOINT mixamorig:LeftUpLeg { OFFSET 10 0 0 CHANNELS 3 Zrotation Xrotation Yrotation
            JOINT mixamorig:LeftLeg { OFFSET 0 -45 0 CHANNELS 3 Zrotation Xrotation Yrotation
              JOINT mixamorig:LeftFoot { OFFSET 0 -45 0 CHANNELS 3 Zrotation Xrotation Yrotation
                JOINT mixamorig:LeftToeBase { OFFSET 0 -8 12 CHANNELS 3 Zrotation Xrotation Yrotation End Site { OFFSET 0 0 5 } } } } }
          JOINT mixamorig:RightUpLeg { OFFSET -10 0 0 CHANNELS 3 Zrotation Xrotation Yrotation
            JOINT mixamorig:RightLeg { OFFSET 0 -45 0 CHANNELS 3 Zrotation Xrotation Yrotation
              JOINT mixamorig:RightFoot { OFFSET 0 -45 0 CHANNELS 3 Zrotation Xrotation Yrotation
                JOINT mixamorig:RightToeBase { OFFSET 0 -8 12 CHANNELS 3 Zrotation Xrotation Yrotation End Site { OFFSET 0 0 5 } } } } }
          JOINT mixamorig:Spine { OFFSET 0 10 0 CHANNELS 3 Zrotation Xrotation Yrotation
            JOINT mixamorig:Neck { OFFSET 0 45 0 CHANNELS 3 Zrotation Xrotation Yrotation
              JOINT mixamorig:Head { OFFSET 0 10 0 CHANNELS 3 Zrotation Xrotation Yrotation End Site { OFFSET 0 20 0 } } }
            JOINT mixamorig:LeftArm { OFFSET 18 40 0 CHANNELS 3 Zrotation Xrotation Yrotation
              JOINT mixamorig:LeftForeArm { OFFSET 28 0 0 CHANNELS 3 Zrotation Xrotation Yrotation
                JOINT mixamorig:LeftHand { OFFSET 25 0 0 CHANNELS 3 Zrotation Xrotation Yrotation End Site { OFFSET 8 0 0 } } } }
            JOINT mixamorig:RightArm { OFFSET -18 40 0 CHANNELS 3 Zrotation Xrotation Yrotation
              JOINT mixamorig:RightForeArm { OFFSET -28 0 0 CHANNELS 3 Zrotation Xrotation Yrotation
                JOINT mixamorig:RightHand { OFFSET -25 0 0 CHANNELS 3 Zrotation Xrotation Yrotation End Site { OFFSET -8 0 0 } } } } }
        }
        MOTION
        Frames: 2
        Frame Time: 0.0333333
        """;

    private static string Bvh(string frame0, string frame1) => Skeleton + "\n" + frame0 + "\n" + frame1 + "\n";

    // 6 root values + 3 per rotating joint (17 joints with channels besides the root).
    private static string Rest(double x = 0, double y = 100, double z = 0, double leftHipX = 0) =>
        string.Join(" ", new[] { x, y, z, 0, 0, 0, 0, leftHipX, 0 }.Concat(Enumerable.Repeat(0.0, 3 * 16)).Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void BvhForwardKinematicsPlacesJoints()
    {
        RecordedActor actor = BvhReader.Read(Bvh(Rest(), Rest(x: 50, leftHipX: 90)), "a", 0.01);
        Assert.Equal(30f, actor.FramesPerSecond, 1);
        Assert.Equal(2, actor.FrameCount);
        (double X, double Y, double Z) knee0 = actor.Joints["mixamorig:LeftLeg"][0];
        Assert.Equal(0.10, knee0.X, 6);
        Assert.Equal(0.55, knee0.Y, 6);
        Assert.Equal(0.0, knee0.Z, 6);

        // Frame 1: root moved 50 cm along x and the left hip turned 90° about x, which swings the thigh to -z.
        (double X, double Y, double Z) knee1 = actor.Joints["mixamorig:LeftLeg"][1];
        Assert.Equal(0.60, knee1.X, 6);
        Assert.Equal(1.00, knee1.Y, 6);
        Assert.Equal(-0.45, knee1.Z, 6);
    }

    [Fact]
    public void ImporterMapsMixamoNamesAndComputesPelvisAndHead()
    {
        RecordedActor a = BvhReader.Read(Bvh(Rest(), Rest()), "a", 0.01);
        RecordedActor b = BvhReader.Read(Bvh(Rest(x: 80), Rest(x: 80)), "b", 0.01);
        MotionTake take = MotionImporter.Build("test", Source(), a, b, new float?[] { 1.75f, null });
        Assert.Empty(take.Validate());
        Assert.Equal(2, take.FrameCount);

        (float X, float Y, float Z) pelvis = take.Joint(0, 0, MotionJoint.Pelvis);
        Assert.Equal(0f, pelvis.X, 4);
        Assert.Equal(1f, pelvis.Y, 4);
        (float X, float Y, float Z) head = take.Joint(0, 0, MotionJoint.Head);
        Assert.Equal(1.75f, head.Y, 4); // halfway between the head joint (1.65) and its end site (1.85)
        (float X, float Y, float Z) shoulder = take.Joint(0, 0, MotionJoint.ShoulderL);
        Assert.Equal(0.18f, shoulder.X, 4); // LeftArm, not the clavicle
        Assert.Equal(1.75f, take.Actors[0].HeightMetres);
        Assert.InRange(take.Actors[1].HeightMetres, 1.8f, 2.0f);
        Assert.Equal(0.8f, take.Joint(0, 1, MotionJoint.Pelvis).X, 4);
    }

    [Fact]
    public void KeypointReaderAcceptsSmplStyleNames()
    {
        static string Track(double x, double y, double z) => FormattableString.Invariant($"[[{x},{y},{z}],[{x},{y},{z}]]");
        string Actor(string name, double dx) => $$"""
            { "name": "{{name}}", "joints": {
              "left_hip": {{Track(0.1 + dx, 1, 0)}}, "right_hip": {{Track(-0.1 + dx, 1, 0)}},
              "left_knee": {{Track(0.1 + dx, 0.55, 0)}}, "right_knee": {{Track(-0.1 + dx, 0.55, 0)}},
              "left_ankle": {{Track(0.1 + dx, 0.1, 0)}}, "right_ankle": {{Track(-0.1 + dx, 0.1, 0)}},
              "left_foot": {{Track(0.1 + dx, 0.02, 0.12)}}, "right_foot": {{Track(-0.1 + dx, 0.02, 0.12)}},
              "neck": {{Track(dx, 1.5, 0)}}, "head": {{Track(dx, 1.68, 0)}},
              "left_shoulder": {{Track(0.18 + dx, 1.45, 0)}}, "right_shoulder": {{Track(-0.18 + dx, 1.45, 0)}},
              "left_elbow": {{Track(0.46 + dx, 1.45, 0)}}, "right_elbow": {{Track(-0.46 + dx, 1.45, 0)}},
              "left_wrist": {{Track(0.71 + dx, 1.45, 0)}}, "right_wrist": {{Track(-0.71 + dx, 1.45, 0)}}
            } }
            """;
        string json = $$"""{ "fps": 25, "actors": [ {{Actor("a", 0)}}, {{Actor("b", 0.8)}} ] }""";
        IReadOnlyList<RecordedActor> actors = KeypointReader.Read(json);
        MotionTake take = MotionImporter.Build("kp", Source(), actors[0], actors[1], new float?[] { 1.8f, 1.8f });
        Assert.Empty(take.Validate());
        Assert.Equal(25f, take.FramesPerSecond);
        Assert.Equal(0.12f, take.Joint(1, 0, MotionJoint.ToeL).Z, 4);
        Assert.Equal(0.71f, take.Joint(0, 0, MotionJoint.HandL).X, 4);
    }

    [Fact]
    public void MissingJointsAreReported()
    {
        string json = """{ "fps": 30, "actors": [ { "name": "a", "joints": { "left_hip": [[0,1,0]] } }, { "name": "b", "joints": { "left_hip": [[0,1,0]] } } ] }""";
        IReadOnlyList<RecordedActor> actors = KeypointReader.Read(json);
        var error = Assert.Throws<ArgumentException>(() => MotionImporter.Build("x", Source(), actors[0], actors[1], new float?[] { 1.8f, 1.8f }));
        Assert.Contains("no joint for", error.Message);
    }

    [Fact]
    public void LibraryValidatesIndexAgainstTakesAndClips()
    {
        TechniqueCatalogFixture fixture = new();
        RecordedActor a = BvhReader.Read(Bvh(Rest(), Rest()), "a", 0.01);
        RecordedActor b = BvhReader.Read(Bvh(Rest(x: 80), Rest(x: 80)), "b", 0.01);
        MotionTake take = MotionImporter.Build("t1", Source(), a, b, new float?[] { 1.75f, 1.75f });
        var clips = new HashSet<string>(fixture.Animations.All.Select(c => c.Id));
        string clip = fixture.Animations.All.First().Id;

        MotionLibrary library = MotionLibrary.Create(new MotionIndexFile { SchemaVersion = 1, Phases = new[] { Phase(clip, "t1", 0, 1) } }, new[] { take }, clips);
        Assert.True(library.TryGet(clip, out MotionPhase phase, out MotionTake found));
        Assert.Same(take, found);
        Assert.Equal(1, phase.ToFrame);

        Assert.Throws<ArgumentException>(() => MotionLibrary.Create(new MotionIndexFile { SchemaVersion = 1, Phases = new[] { Phase(clip, "missing", 0, 1) } }, new[] { take }, clips));
        Assert.Throws<ArgumentException>(() => MotionLibrary.Create(new MotionIndexFile { SchemaVersion = 1, Phases = new[] { Phase("anim.nope", "t1", 0, 1) } }, new[] { take }, clips));
        Assert.Throws<ArgumentException>(() => MotionLibrary.Create(new MotionIndexFile { SchemaVersion = 1, Phases = new[] { Phase(clip, "t1", 0, 5) } }, new[] { take }, clips));
    }

    [Fact]
    public void TakesRoundTripThroughJson()
    {
        RecordedActor a = BvhReader.Read(Bvh(Rest(), Rest()), "a", 0.01);
        RecordedActor b = BvhReader.Read(Bvh(Rest(x: 80), Rest(x: 80)), "b", 0.01);
        MotionTake take = MotionImporter.Build("rt", Source(), a, b, new float?[] { 1.75f, 1.75f });
        MotionTake back = MotionLoader.TakeFromJson(ContentJson.Serialize(take), "rt.json");
        Assert.Empty(back.Validate());
        Assert.Equal(take.Joint(1, 1, MotionJoint.HandR), back.Joint(1, 1, MotionJoint.HandR));
        Assert.Equal("CC-BY-SA-4.0", back.Source.License);
    }

    [Fact]
    public void ShippedMotionDataLoads()
    {
        GameContent content = GameContent.Load(TestContent.PathOf(string.Empty));
        Assert.True(content.Motion.PhaseCount >= 0);
        Assert.All(content.Motion.Takes, t => Assert.Empty(t.Validate()));
    }

    private static MotionSource Source() => new() { Title = "test", Url = "https://example.org/clip", Author = "tester", License = "CC-BY-SA-4.0" };

    private static MotionPhase Phase(string clip, string take, int from, int to) => new() { Clip = clip, Take = take, FromFrame = from, ToFrame = to };

    private sealed class TechniqueCatalogFixture
    {
        public MoveAnimationLibrary Animations { get; } = MoveAnimationLoader.FromDirectory(
            TestContent.PathOf("animation"), TechniqueLoader.FromDirectory(TestContent.PathOf("techniques")));
    }
}
