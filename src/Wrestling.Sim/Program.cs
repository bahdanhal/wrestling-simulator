using System.Globalization;
using Wrestling.Core.Diagnostics;

namespace Wrestling.Sim;

/// <summary>Headless command-line runner for simulation, balancing and platform checks.</summary>
public static class Program
{
    private const string Usage = """
        Usage: wrestling-sim <command> [options]

        Commands:
          determinism-probe [--iterations N]   Print the determinism hash for this machine.
                                               Every platform must print the same value.
          help                                 Show this help.
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
            return args[0] switch
            {
                "determinism-probe" => RunDeterminismProbe(args.AsSpan(1)),
                _ => Fail($"Unknown command '{args[0]}'."),
            };
        }
        catch (ArgumentException exception)
        {
            return Fail(exception.Message);
        }
    }

    private static int RunDeterminismProbe(ReadOnlySpan<string> options)
    {
        int iterations = DeterminismProbe.DefaultIterations;
        for (int i = 0; i < options.Length; i++)
        {
            if (options[i] == "--iterations" && i + 1 < options.Length)
            {
                if (!int.TryParse(options[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out iterations) || iterations <= 0)
                {
                    throw new ArgumentException("--iterations must be a positive integer.");
                }
            }
            else
            {
                throw new ArgumentException($"Unknown option '{options[i]}'.");
            }
        }

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

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine(Usage);
        return 1;
    }
}
