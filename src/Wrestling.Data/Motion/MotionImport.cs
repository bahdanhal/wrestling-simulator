using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Wrestling.Data.Motion
{
    /// <summary>One recorded person: joint world positions per frame, by joint name as the source named them.</summary>
    public sealed class RecordedActor
    {
        public RecordedActor(string name, float framesPerSecond, IReadOnlyDictionary<string, (double X, double Y, double Z)[]> joints, IReadOnlyDictionary<string, string> parents)
        {
            Name = name;
            FramesPerSecond = framesPerSecond;
            Joints = joints;
            Parents = parents;
        }

        public string Name { get; }

        public float FramesPerSecond { get; }

        /// <summary>World positions (metres, right-handed, Y up) per frame, by source joint name.</summary>
        public IReadOnlyDictionary<string, (double X, double Y, double Z)[]> Joints { get; }

        /// <summary>Parent of each joint (empty for the root), for end-site lookups.</summary>
        public IReadOnlyDictionary<string, string> Parents { get; }

        public int FrameCount => Joints.Count == 0 ? 0 : Joints.Values.First().Length;
    }

    /// <summary>
    /// Reads BVH motion capture files (the common export of motion-capture and video-to-motion
    /// tools) into joint world positions by forward kinematics.
    /// </summary>
    public static class BvhReader
    {
        private sealed class Node
        {
            public string Name = string.Empty;
            public Node? Parent;
            public double[] Offset = new double[3];
            public List<string> Channels = new List<string>();
            public List<Node> Children = new List<Node>();
            public bool EndSite;
        }

        /// <param name="unitsToMetres">Scale of the file's units (0.01 for centimetres).</param>
        public static RecordedActor Read(string text, string name, double unitsToMetres)
        {
            var tokens = new Queue<string>(text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            string Next() => tokens.Count > 0 ? tokens.Dequeue() : throw new FormatException("BVH ended unexpectedly.");
            void Expect(string token)
            {
                string found = Next();
                if (!string.Equals(found, token, StringComparison.OrdinalIgnoreCase))
                {
                    throw new FormatException($"BVH: expected '{token}', found '{found}'.");
                }
            }

            double Number() => double.Parse(Next(), NumberStyles.Float, CultureInfo.InvariantCulture);

            Expect("HIERARCHY");
            Expect("ROOT");
            var nodes = new List<Node>();
            Node ReadJoint(string jointName, Node? parent, bool endSite)
            {
                var node = new Node { Name = jointName, Parent = parent, EndSite = endSite };
                nodes.Add(node);
                Expect("{");
                while (true)
                {
                    string token = Next();
                    switch (token.ToUpperInvariant())
                    {
                        case "OFFSET":
                            node.Offset = new[] { Number(), Number(), Number() };
                            break;
                        case "CHANNELS":
                            int count = int.Parse(Next(), CultureInfo.InvariantCulture);
                            for (int i = 0; i < count; i++)
                            {
                                node.Channels.Add(Next());
                            }

                            break;
                        case "JOINT":
                            node.Children.Add(ReadJoint(Next(), node, false));
                            break;
                        case "END":
                            Expect("Site");
                            node.Children.Add(ReadJoint(node.Name + "_End", node, true));
                            break;
                        case "}":
                            return node;
                        default:
                            throw new FormatException($"BVH: unexpected '{token}' in joint '{jointName}'.");
                    }
                }
            }

            Node root = ReadJoint(Next(), null, false);
            Expect("MOTION");
            Expect("Frames:");
            int frames = int.Parse(Next(), CultureInfo.InvariantCulture);
            Expect("Frame");
            Expect("Time:");
            double frameTime = Number();
            if (frames < 1 || frameTime <= 0)
            {
                throw new FormatException("BVH: needs at least one frame and a positive frame time.");
            }

            var positions = nodes.ToDictionary(n => n.Name, _ => new (double X, double Y, double Z)[frames], StringComparer.Ordinal);
            var values = new double[nodes.Sum(n => n.Channels.Count)];
            for (int f = 0; f < frames; f++)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = Number();
                }

                int cursor = 0;
                void Solve(Node node, double[] parentPosition, double[] parentRotation)
                {
                    double[] translation = { node.Offset[0], node.Offset[1], node.Offset[2] };
                    double[] rotation = Identity();
                    foreach (string channel in node.Channels)
                    {
                        double value = values[cursor++];
                        switch (channel.ToLowerInvariant())
                        {
                            case "xposition": translation[0] += value; break;
                            case "yposition": translation[1] += value; break;
                            case "zposition": translation[2] += value; break;
                            case "xrotation": rotation = Multiply(rotation, Axis(0, value)); break;
                            case "yrotation": rotation = Multiply(rotation, Axis(1, value)); break;
                            case "zrotation": rotation = Multiply(rotation, Axis(2, value)); break;
                            default: throw new FormatException($"BVH: unknown channel '{channel}'.");
                        }
                    }

                    double[] offset = Apply(parentRotation, translation);
                    double[] position = { parentPosition[0] + offset[0], parentPosition[1] + offset[1], parentPosition[2] + offset[2] };
                    double[] world = Multiply(parentRotation, rotation);
                    positions[node.Name][f] = (position[0] * unitsToMetres, position[1] * unitsToMetres, position[2] * unitsToMetres);
                    foreach (Node child in node.Children)
                    {
                        Solve(child, position, world);
                    }
                }

                Solve(root, new double[3], Identity());
            }

            var parents = nodes.ToDictionary(n => n.Name, n => n.Parent?.Name ?? string.Empty, StringComparer.Ordinal);
            return new RecordedActor(name, (float)(1.0 / frameTime), positions, parents);
        }

        public static RecordedActor ReadFile(string path, string name, double unitsToMetres) =>
            Read(File.ReadAllText(path), name, unitsToMetres);

        private static double[] Identity() => new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };

        /// <summary>Rotation about axis 0/1/2 (x/y/z) by degrees, right-handed, column vectors.</summary>
        private static double[] Axis(int axis, double degrees)
        {
            double r = degrees * Math.PI / 180.0;
            double c = Math.Cos(r);
            double s = Math.Sin(r);
            switch (axis)
            {
                case 0: return new double[] { 1, 0, 0, 0, c, -s, 0, s, c };
                case 1: return new double[] { c, 0, s, 0, 1, 0, -s, 0, c };
                default: return new double[] { c, -s, 0, s, c, 0, 0, 0, 1 };
            }
        }

        private static double[] Multiply(double[] a, double[] b)
        {
            var m = new double[9];
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    m[(row * 3) + col] = (a[row * 3] * b[col]) + (a[(row * 3) + 1] * b[3 + col]) + (a[(row * 3) + 2] * b[6 + col]);
                }
            }

            return m;
        }

        private static double[] Apply(double[] m, double[] v) => new[]
        {
            (m[0] * v[0]) + (m[1] * v[1]) + (m[2] * v[2]),
            (m[3] * v[0]) + (m[4] * v[1]) + (m[5] * v[2]),
            (m[6] * v[0]) + (m[7] * v[1]) + (m[8] * v[2]),
        };
    }

    /// <summary>
    /// Reads joint positions exported by video-to-3D tools as JSON:
    /// <c>{ "fps": 30, "actors": [ { "name": "A", "joints": { "left_hip": [[x, y, z], ...], ... } }, ... ] }</c>
    /// (metres, right-handed, Y up).
    /// </summary>
    public static class KeypointReader
    {
        public static IReadOnlyList<RecordedActor> Read(string json)
        {
            JObject root = JObject.Parse(json);
            float fps = (float?)root["fps"] ?? throw new FormatException("Keypoints: 'fps' is required.");
            var actors = new List<RecordedActor>();
            foreach (JObject actor in (JArray?)root["actors"] ?? throw new FormatException("Keypoints: 'actors' is required."))
            {
                var joints = new Dictionary<string, (double, double, double)[]>(StringComparer.Ordinal);
                foreach (JProperty joint in ((JObject?)actor["joints"] ?? throw new FormatException("Keypoints: actor 'joints' is required.")).Properties())
                {
                    joints[joint.Name] = ((JArray)joint.Value).Select(p => ((double)p[0]!, (double)p[1]!, (double)p[2]!)).ToArray();
                }

                if (joints.Values.Select(v => v.Length).Distinct().Count() > 1)
                {
                    throw new FormatException($"Keypoints: actor '{actor["name"]}' joints have different frame counts.");
                }

                actors.Add(new RecordedActor((string?)actor["name"] ?? $"actor{actors.Count}", fps, joints, new Dictionary<string, string>()));
            }

            return actors;
        }
    }

    /// <summary>Builds a <see cref="MotionTake"/> from two recorded actors, mapping their joint names onto <see cref="MotionJoint"/>.</summary>
    public static class MotionImporter
    {
        /// <summary>Accepted source names per joint, most specific first (normalised: lower case, no separators or rig prefixes).</summary>
        private static readonly Dictionary<MotionJoint, string[]> Aliases = new Dictionary<MotionJoint, string[]>
        {
            [MotionJoint.Neck] = new[] { "neck", "neck1", "neckbase" },
            [MotionJoint.Head] = new[] { "head" },
            [MotionJoint.ShoulderL] = new[] { "leftarm", "leftupperarm", "lupperarm", "larm", "leftshoulder", "lshoulder" },
            [MotionJoint.ShoulderR] = new[] { "rightarm", "rightupperarm", "rupperarm", "rarm", "rightshoulder", "rshoulder" },
            [MotionJoint.ElbowL] = new[] { "leftforearm", "leftlowerarm", "lforearm", "leftelbow", "lelbow" },
            [MotionJoint.ElbowR] = new[] { "rightforearm", "rightlowerarm", "rforearm", "rightelbow", "relbow" },
            [MotionJoint.HandL] = new[] { "lefthand", "leftwrist", "lhand", "lwrist" },
            [MotionJoint.HandR] = new[] { "righthand", "rightwrist", "rhand", "rwrist" },
            [MotionJoint.HipL] = new[] { "leftupleg", "leftupperleg", "leftthigh", "lthigh", "lefthip", "lhip" },
            [MotionJoint.HipR] = new[] { "rightupleg", "rightupperleg", "rightthigh", "rthigh", "righthip", "rhip" },
            [MotionJoint.KneeL] = new[] { "leftleg", "leftlowerleg", "leftshin", "lshin", "leftknee", "lknee", "lcalf" },
            [MotionJoint.KneeR] = new[] { "rightleg", "rightlowerleg", "rightshin", "rshin", "rightknee", "rknee", "rcalf" },
            // Mixamo and CMU call the ankle "foot"; SMPL-style output calls the toe "foot" and has an
            // explicit "ankle". An explicit ankle wins, and a joint is never used twice.
            [MotionJoint.AnkleL] = new[] { "leftankle", "lankle", "leftfoot", "lfoot" },
            [MotionJoint.AnkleR] = new[] { "rightankle", "rankle", "rightfoot", "rfoot" },
            [MotionJoint.ToeL] = new[] { "lefttoebase", "lefttoe", "lefttoes", "ltoebase", "ltoe", "leftfootend", "lfootend", "leftfoot", "lfoot", "leftankleend" },
            [MotionJoint.ToeR] = new[] { "righttoebase", "righttoe", "righttoes", "rtoebase", "rtoe", "rightfootend", "rfootend", "rightfoot", "rfoot", "rightankleend" },
        };

        private static readonly string[] Prefixes = { "mixamorig", "bip01", "bip001", "characterbones", "skeleton", "def" };

        public static string Normalise(string name)
        {
            string lower = new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            foreach (string prefix in Prefixes)
            {
                if (lower.StartsWith(prefix, StringComparison.Ordinal) && lower.Length > prefix.Length)
                {
                    lower = lower.Substring(prefix.Length);
                }
            }

            return lower;
        }

        /// <param name="heights">Each actor's standing height in metres, or null to estimate it from the first frame.</param>
        public static MotionTake Build(string id, MotionSource source, RecordedActor a, RecordedActor b, float?[] heights)
        {
            if (Math.Abs(a.FramesPerSecond - b.FramesPerSecond) > 0.01f || a.FrameCount != b.FrameCount)
            {
                throw new ArgumentException("Both actors need the same frame rate and frame count.");
            }

            var maps = new[] { Map(a), Map(b) };
            var frames = new List<IReadOnlyList<IReadOnlyList<float>>>(a.FrameCount);
            for (int f = 0; f < a.FrameCount; f++)
            {
                var actors = new List<IReadOnlyList<float>>(2);
                for (int actor = 0; actor < 2; actor++)
                {
                    var values = new float[MotionTake.JointCount * 3];
                    for (int j = 0; j < MotionTake.JointCount; j++)
                    {
                        (double x, double y, double z) = maps[actor][j](f);
                        values[(j * 3) + 0] = (float)x;
                        values[(j * 3) + 1] = (float)y;
                        values[(j * 3) + 2] = (float)z;
                    }

                    actors.Add(values);
                }

                frames.Add(actors);
            }

            var take = new MotionTake
            {
                SchemaVersion = 1,
                Id = id,
                Source = source,
                FramesPerSecond = a.FramesPerSecond,
                Actors = new[]
                {
                    new MotionActor { Name = a.Name, HeightMetres = heights[0] ?? EstimateHeight(frames[0][0]) },
                    new MotionActor { Name = b.Name, HeightMetres = heights[1] ?? EstimateHeight(frames[0][1]) },
                },
                Frames = frames,
            };
            IReadOnlyList<string> errors = take.Validate();
            if (errors.Count > 0)
            {
                throw new ArgumentException("Imported take is invalid:\n- " + string.Join("\n- ", errors));
            }

            return take;
        }

        /// <summary>For each <see cref="MotionJoint"/>, a function giving its position per frame from the actor's joints.</summary>
        private static Func<int, (double X, double Y, double Z)>[] Map(RecordedActor actor)
        {
            var byNormalised = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in actor.Joints.Keys)
            {
                string key = Normalise(name);
                if (!byNormalised.ContainsKey(key))
                {
                    byNormalised.Add(key, name);
                }
            }

            var used = new HashSet<string>(StringComparer.Ordinal);
            var found = new Dictionary<MotionJoint, string>();
            string Find(MotionJoint joint)
            {
                if (found.TryGetValue(joint, out string? known))
                {
                    return known;
                }

                foreach (string alias in Aliases[joint])
                {
                    if (byNormalised.TryGetValue(alias, out string? name) && used.Add(name))
                    {
                        found[joint] = name;
                        return name;
                    }
                }

                throw new ArgumentException($"Actor '{actor.Name}': no joint for {joint} (tried {string.Join(", ", Aliases[joint])}).");
            }

            var map = new Func<int, (double, double, double)>[MotionTake.JointCount];
            foreach (MotionJoint joint in Aliases.Keys)
            {
                (double, double, double)[] track = actor.Joints[Find(joint)];
                map[(int)joint] = f => track[f];
            }

            (double X, double Y, double Z)[] hipL = actor.Joints[Find(MotionJoint.HipL)];
            (double X, double Y, double Z)[] hipR = actor.Joints[Find(MotionJoint.HipR)];
            map[(int)MotionJoint.Pelvis] = f => ((hipL[f].X + hipR[f].X) / 2, (hipL[f].Y + hipR[f].Y) / 2, (hipL[f].Z + hipR[f].Z) / 2);

            // The head joint of most rigs sits at the base of the skull; the centre is halfway to its end site.
            string head = Find(MotionJoint.Head);
            string? headEnd = actor.Parents.FirstOrDefault(p => p.Value == head && actor.Joints.ContainsKey(p.Key)).Key;
            if (headEnd != null)
            {
                (double X, double Y, double Z)[] baseTrack = actor.Joints[head];
                (double X, double Y, double Z)[] endTrack = actor.Joints[headEnd];
                map[(int)MotionJoint.Head] = f => ((baseTrack[f].X + endTrack[f].X) / 2, (baseTrack[f].Y + endTrack[f].Y) / 2, (baseTrack[f].Z + endTrack[f].Z) / 2);
            }

            return map;
        }

        /// <summary>Head-top estimate: head centre plus a head radius above the lowest foot point.</summary>
        private static float EstimateHeight(IReadOnlyList<float> joints)
        {
            float head = joints[((int)MotionJoint.Head * 3) + 1];
            float neck = joints[((int)MotionJoint.Neck * 3) + 1];
            float foot = Math.Min(Math.Min(joints[((int)MotionJoint.AnkleL * 3) + 1], joints[((int)MotionJoint.AnkleR * 3) + 1]),
                Math.Min(joints[((int)MotionJoint.ToeL * 3) + 1], joints[((int)MotionJoint.ToeR * 3) + 1]));
            return (head - foot) + Math.Max(0.08f, (head - neck) * 0.9f) + 0.03f;
        }
    }
}
