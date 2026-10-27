using System;
using System.Collections.Generic;
using Wrestling.Core.Numerics;

namespace Wrestling.Core.Rules
{
    /// <summary>Collects all validation errors so content authors see every problem at once.</summary>
    internal sealed class ValidationContext
    {
        private readonly List<string> _errors = new List<string>();

        public IReadOnlyList<string> Errors => _errors;

        public void Error(string message) => _errors.Add(message);

        public void RequireRange(int value, int min, int max, string path)
        {
            if (value < min || value > max)
            {
                _errors.Add($"{path} is {value}; expected {min}..{max}.");
            }
        }

        public void RequirePositive(Fix64 value, string path)
        {
            if (value <= Fix64.Zero)
            {
                _errors.Add($"{path} must be positive.");
            }
        }

        public void RequireNotEmpty(string? value, string path)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _errors.Add($"{path} must not be empty.");
            }
        }

        public T? RequireNotNull<T>(T? value, string path)
            where T : class
        {
            if (value == null)
            {
                _errors.Add($"{path} is missing.");
            }

            return value;
        }

        public void RequireStyleRange(StyleValues<int> values, int min, int max, string path)
        {
            RequireRange(values.GrecoRoman, min, max, path + ".grecoRoman");
            RequireRange(values.Freestyle, min, max, path + ".freestyle");
            RequireRange(values.Womens, min, max, path + ".womens");
        }

        public void RequireAgeRange(AgeValues<int> values, int min, int max, string path)
        {
            foreach (AgeCategory category in (AgeCategory[])Enum.GetValues(typeof(AgeCategory)))
            {
                RequireRange(values.For(category), min, max, path + "." + category.ToString().ToLowerInvariant());
            }
        }
    }

    /// <summary>Thrown when content fails validation; lists every problem found.</summary>
    public sealed class RulesValidationException : Exception
    {
        public RulesValidationException(string subject, IReadOnlyList<string> errors)
            : base($"{subject} is invalid:{Environment.NewLine}- " + string.Join(Environment.NewLine + "- ", errors))
        {
            Errors = errors;
        }

        public IReadOnlyList<string> Errors { get; }
    }
}
