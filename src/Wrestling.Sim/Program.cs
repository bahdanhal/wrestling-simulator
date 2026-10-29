using System.Diagnostics;
using System.Globalization;
using Wrestling.Core.AI;
using Wrestling.Core.Bouts;
using Wrestling.Core.Competition;
using Wrestling.Core.Diagnostics;
using Wrestling.Core.Match;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Wrestlers;
using Wrestling.Data;
using Wrestling.Data.Localization;

namespace Wrestling.Sim;

/// <summary>Headless command-line runner for simulation, balancing and platform checks.</summary>
public static class Program
{
    private const string Usage = """
        Usage: wrestling-sim <command> [options]

        Commands:
          determinism-probe [--iterations N]       Print this machine's determinism hash.
          bout [--style fs|gr|ww] [--seed N]       Simulate one AI bout and print its events.
          balance [--style fs|gr|ww] [--bouts N]   Simulate many AI bouts and report statistics.
                  [--seed N] [--level N] [--spread N]
          tournament [--style fs|gr|ww]            Simulate a weight category (any size) and print standings.
                  [--entrants N] [--seed N]
          motion-import --id ID --out-data DIR      Build a two-person motion take from BVH files
                  (--bvh-a FILE --bvh-b FILE [--units-to-metres 0.01]) or a keypoint JSON (--keypoints FILE),
                  [--height-a M --height-b M], --source-url URL --source-author NAME --source-license ID
                  [--source-title TEXT --source-notes TEXT].
          motion-assign --clip ANIM_ID --take ID     Play frames of a take for a technique phase.
                  --from N --to N [--executor 0|1] [--left-side true|false]
          help                                     Show this help.

        Common options:
          --data DIR    Content folder (default: the repository's data/ folder).
        """;

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            Options options = Options.Parse(args.AsSpan(1));
            return args[0] switch
            {
                "determinism-probe" => RunDeterminismProbe(options),
                "bout" => RunBout(options),
                "balance" => RunBalance(options),
                "tournament" => RunTournament(options),
                "motion-import" => RunMotionImport(options),
                "motion-assign" => RunMotionAssign(options),
                _ => Fail($"Unknown command '{args[0]}'."),
            };
        }
        catch (ArgumentException exception)
        {
            return Fail(exception.Message);
        }
    }

    private static int RunDeterminismProbe(Options options)
    {
        int iterations = options.Int("iterations", DeterminismProbe.DefaultIterations, 1);
        options.EnsureAllUsed();
        ulong hash = DeterminismProbe.Run(iterations);
        Console.WriteLine($"iterations={iterations} hash=0x{hash:X16}");
        if (iterations != DeterminismProbe.DefaultIterations)
        {
            return 0;
        }

        bool matches = hash == DeterminismProbe.ReferenceHash;
        Console.WriteLine(matches ? "matches reference" : $"MISMATCH: reference is 0x{DeterminismProbe.ReferenceHash:X16}");
        return matches ? 0 : 2;
    }

    private static int RunBout(Options options)
    {
        GameContent content = LoadContent(options);
        WrestlingStyle style = options.Style();
        ulong seed = (ulong)options.Int("seed", 1, 0);
        options.EnsureAllUsed();

        var random = new Pcg32(seed, 17);
        Wrestler red = WrestlerGenerator.Generate("red", style, 60, 12, content.Techniques, content.Names, random);
        Wrestler blue = WrestlerGenerator.Generate("blue", style, 60, 12, content.Techniques, content.Names, random);
        BoutEngine engine = CreateEngine(content, style, red, blue, seed);
        BoutResult result = engine.RunToEnd();

        Console.WriteLine($"{style}: RED {red.Name} vs BLUE {blue.Name} (seed {seed})");
        foreach (BoutEvent e in engine.Events.Where(BoutCommentary.IsNotable))
        {
            Console.WriteLine($"  {BoutCommentary.Clock(e.Time, engine.Clock)}  {BoutCommentary.Describe(e, content, red, blue)}");
        }

        string winner = result.Winner == Corner.Red ? red.Name.Full : blue.Name.Full;
        Console.WriteLine($"Result: {winner} wins {result.RedPoints}-{result.BluePoints} by {result.VictoryType} ({result.Code}, {result.RedClassificationPoints}:{result.BlueClassificationPoints})");
        return 0;
    }

    private static int RunBalance(Options options)
    {
        GameContent content = LoadContent(options);
        WrestlingStyle style = options.Style();
        int bouts = options.Int("bouts", 300, 1);
        ulong seed = (ulong)options.Int("seed", 1, 0);
        int level = options.Int("level", 55, 1);
        int spread = options.Int("spread", 12, 0);
        options.EnsureAllUsed();

        var victories = new Dictionary<VictoryType, int>();
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        int points = 0;
        int attempts = 0;
        int successes = 0;
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < bouts; i++)
        {
            ulong boutSeed = seed + (ulong)i;
            var random = new Pcg32(boutSeed, 17);
            Wrestler red = WrestlerGenerator.Generate("red", style, level, spread, content.Techniques, content.Names, random);
            Wrestler blue = WrestlerGenerator.Generate("blue", style, level, spread, content.Techniques, content.Names, random);
            BoutEngine engine = CreateEngine(content, style, red, blue, boutSeed);
            BoutResult result = engine.RunToEnd();
            BoutStatistics statistics = BoutStatistics.From(engine);

            victories[result.VictoryType!.Value] = victories.GetValueOrDefault(result.VictoryType.Value) + 1;
            points += result.RedPoints + result.BluePoints;
            foreach (WrestlerBoutStatistics side in new[] { statistics.Red, statistics.Blue })
            {
                attempts += side.Attempts;
                successes += side.Successes;
                foreach ((string reason, int value) in side.PointsByReason)
                {
                    reasons[reason] = reasons.GetValueOrDefault(reason) + value;
                }
            }
        }

        double milliseconds = stopwatch.Elapsed.TotalMilliseconds / bouts;
        Console.WriteLine($"{style}: {bouts} bouts, level {level} ± {spread}, {milliseconds:F1} ms per bout");
        foreach ((VictoryType type, int count) in victories.OrderByDescending(v => v.Value))
        {
            Console.WriteLine($"  {type,-22} {100.0 * count / bouts,5:F1}%");
        }

        Console.WriteLine($"  points per bout        {(double)points / bouts,5:F1}");
        Console.WriteLine($"  attempts per bout      {(double)attempts / bouts,5:F1}");
        Console.WriteLine($"  attack success         {(attempts == 0 ? 0 : 100.0 * successes / attempts),5:F1}%");
        Console.WriteLine("  points by reason       " + string.Join(", ", reasons.OrderByDescending(r => r.Value).Select(r => $"{r.Key} {100.0 * r.Value / Math.Max(1, points):F0}%")));
        return 0;
    }

    private static int RunTournament(Options options)
    {
        GameContent content = LoadContent(options);
        WrestlingStyle style = options.Style();
        int count = options.Int("entrants", 20, 2);
        ulong seed = (ulong)options.Int("seed", 1, 0);
        options.EnsureAllUsed();

        var random = new Pcg32(seed, 23);
        var wrestlers = new Dictionary<string, Wrestler>();
        var entrants = new List<Entrant>();
        for (int i = 1; i <= count; i++)
        {
            string id = $"W{i:00}";
            wrestlers[id] = WrestlerGenerator.Generate(id, style, 40 + random.NextInt(0, 40), 10, content.Techniques, content.Names, random);
            entrants.Add(new Entrant(id, i));
        }

        ICompetitionFormat format = CompetitionSystems.Create(entrants, content.Rules);
        int boutNumber = 0;
        IReadOnlyList<Placement> standings = CategoryRunner.Run(format, bout =>
        {
            boutNumber++;
            BoutEngine engine = CreateEngine(content, style, wrestlers[bout.Top!], wrestlers[bout.Bottom!], seed * 1000 + (ulong)boutNumber);
            BoutResult result = engine.RunToEnd();
            Console.WriteLine($"  {bout.Name,-13} {Label(wrestlers[bout.Top!]),-22} {result.RedPoints,2}-{result.BluePoints,-2} {Label(wrestlers[bout.Bottom!]),-22} {result.Code}");
            return result;
        });

        Console.WriteLine($"{style}: {count} entrants, {CompetitionSystems.ForEntrantCount(count)}, {boutNumber} bouts");
        foreach (Placement placement in standings)
        {
            Console.WriteLine($"  {placement.Rank?.ToString(CultureInfo.InvariantCulture) ?? "-",3}  {Label(wrestlers[placement.EntrantId])}");
        }

        return 0;
    }

    private static int RunMotionImport(Options options)
    {
        string id = options.String("id") ?? throw new ArgumentException("--id is required.");
        string data = options.String("out-data") ?? DataDirectory(options);
        var source = new Wrestling.Data.Motion.MotionSource
        {
            Title = options.String("source-title") ?? string.Empty,
            Url = options.String("source-url") ?? string.Empty,
            Author = options.String("source-author") ?? throw new ArgumentException("--source-author is required."),
            License = options.String("source-license") ?? throw new ArgumentException("--source-license is required."),
            Notes = options.String("source-notes") ?? string.Empty,
        };
        float?[] heights = { Float(options.String("height-a")), Float(options.String("height-b")) };
        IReadOnlyList<Wrestling.Data.Motion.RecordedActor> actors;
        string? keypoints = options.String("keypoints");
        if (keypoints != null)
        {
            actors = Wrestling.Data.Motion.KeypointReader.Read(File.ReadAllText(keypoints));
        }
        else
        {
            double units = Float(options.String("units-to-metres")) ?? 0.01;
            string a = options.String("bvh-a") ?? throw new ArgumentException("--bvh-a (or --keypoints) is required.");
            string b = options.String("bvh-b") ?? throw new ArgumentException("--bvh-b is required.");
            actors = new[]
            {
                Wrestling.Data.Motion.BvhReader.ReadFile(a, Path.GetFileNameWithoutExtension(a), units),
                Wrestling.Data.Motion.BvhReader.ReadFile(b, Path.GetFileNameWithoutExtension(b), units),
            };
        }

        options.EnsureAllUsed();
        if (actors.Count != 2)
        {
            throw new ArgumentException($"A take needs exactly two actors; found {actors.Count}.");
        }

        Wrestling.Data.Motion.MotionTake take = Wrestling.Data.Motion.MotionImporter.Build(id, source, actors[0], actors[1], heights);
        Wrestling.Data.Motion.MotionLoader.WriteTake(take, Path.Combine(data, "motion"));
        Console.WriteLine($"Wrote take '{id}': {take.FrameCount} frames at {take.FramesPerSecond:F1} fps, actors {take.Actors[0].HeightMetres:F2} m and {take.Actors[1].HeightMetres:F2} m.");
        return 0;
    }

    private static int RunMotionAssign(Options options)
    {
        string data = DataDirectory(options);
        var phase = new Wrestling.Data.Motion.MotionPhase
        {
            Clip = options.String("clip") ?? throw new ArgumentException("--clip is required."),
            Take = options.String("take") ?? throw new ArgumentException("--take is required."),
            FromFrame = options.Int("from", -1, 0),
            ToFrame = options.Int("to", -1, 1),
            Executor = options.Int("executor", 0, 0),
            LeftSide = (options.String("left-side") ?? "false") == "true",
        };
        options.EnsureAllUsed();
        string indexPath = Path.Combine(data, "motion", Wrestling.Data.Motion.MotionLoader.IndexFile);
        string previous = File.ReadAllText(indexPath);
        Wrestling.Data.Motion.MotionLoader.SetPhase(Path.Combine(data, "motion"), phase);
        GameContent content;
        try
        {
            content = GameContent.Load(data);
        }
        catch (Exception)
        {
            File.WriteAllText(indexPath, previous);
            throw;
        }

        Console.WriteLine($"{phase.Clip} now plays take '{phase.Take}' frames {phase.FromFrame}–{phase.ToFrame}; {content.Motion.PhaseCount} phases have recorded motion.");
        return 0;
    }

    private static float? Float(string? text) =>
        text == null ? null : float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string DataDirectory(Options options) =>
        options.String("data") ?? GameContent.FindDataDirectory(Directory.GetCurrentDirectory()) ?? GameContent.FindDataDirectory(AppContext.BaseDirectory)
        ?? throw new ArgumentException("Could not find the data/ folder; pass --data DIR.");

    private static BoutEngine CreateEngine(GameContent content, WrestlingStyle style, Wrestler red, Wrestler blue, ulong seed) => new(new BoutSetup
    {
        Rules = content.Rules,
        Tuning = content.Tuning,
        Catalog = content.Techniques,
        Style = style,
        Red = red,
        Blue = blue,
        RedController = new AiController(Tactics.Default, seed * 2),
        BlueController = new AiController(Tactics.Default, (seed * 2) + 1),
        Seed = seed,
    });

    private static GameContent LoadContent(Options options)
    {
        string? data = options.String("data") ?? GameContent.FindDataDirectory(Directory.GetCurrentDirectory()) ?? GameContent.FindDataDirectory(AppContext.BaseDirectory);
        if (data == null)
        {
            throw new ArgumentException("Could not find the data/ folder; pass --data DIR.");
        }

        return GameContent.Load(data);
    }

    private static string Label(Wrestler wrestler) => $"{wrestler.Id} {wrestler.Name.Family}";

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine(Usage);
        return 1;
    }

    /// <summary>Minimal "--name value" option parser that rejects unknown or unused options.</summary>
    private sealed class Options
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);

        public static Options Parse(ReadOnlySpan<string> args)
        {
            var options = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
                {
                    throw new ArgumentException($"Expected '--name value', got '{args[i]}'.");
                }

                options._values[args[i][2..]] = args[++i];
            }

            return options;
        }

        public string? String(string name)
        {
            _used.Add(name);
            return _values.TryGetValue(name, out string? value) ? value : null;
        }

        public int Int(string name, int fallback, int minimum)
        {
            string? text = String(name);
            if (text == null)
            {
                return fallback;
            }

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < minimum)
            {
                throw new ArgumentException($"--{name} must be an integer of at least {minimum}.");
            }

            return value;
        }

        public WrestlingStyle Style() => (String("style") ?? "fs") switch
        {
            "fs" => WrestlingStyle.Freestyle,
            "gr" => WrestlingStyle.GrecoRoman,
            "ww" => WrestlingStyle.Womens,
            string other => throw new ArgumentException($"Unknown style '{other}'; use fs, gr or ww."),
        };

        public void EnsureAllUsed()
        {
            string? unknown = _values.Keys.FirstOrDefault(k => !_used.Contains(k));
            if (unknown != null)
            {
                throw new ArgumentException($"Unknown option '--{unknown}'.");
            }
        }
    }
}
