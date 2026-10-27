using System;

namespace Wrestling.Core.Rules
{
    /// <summary>One value per style. Using explicit properties keeps content files complete and typo-safe.</summary>
    public sealed class StyleValues<T>
    {
        public T GrecoRoman { get; init; } = default!;

        public T Freestyle { get; init; } = default!;

        public T Womens { get; init; } = default!;

        public T For(WrestlingStyle style) => style switch
        {
            WrestlingStyle.GrecoRoman => GrecoRoman,
            WrestlingStyle.Freestyle => Freestyle,
            WrestlingStyle.Womens => Womens,
            _ => throw new ArgumentOutOfRangeException(nameof(style), style, null),
        };
    }

    /// <summary>One value per age category.</summary>
    public sealed class AgeValues<T>
    {
        public T U15 { get; init; } = default!;

        public T U17 { get; init; } = default!;

        public T U20 { get; init; } = default!;

        public T U23 { get; init; } = default!;

        public T Senior { get; init; } = default!;

        public T Veterans { get; init; } = default!;

        public T For(AgeCategory category) => category switch
        {
            AgeCategory.U15 => U15,
            AgeCategory.U17 => U17,
            AgeCategory.U20 => U20,
            AgeCategory.U23 => U23,
            AgeCategory.Senior => Senior,
            AgeCategory.Veterans => Veterans,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
        };
    }
}
