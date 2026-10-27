namespace Wrestling.Core.Rules
{
    public enum WrestlingStyle
    {
        GrecoRoman,
        Freestyle,
        Womens,
    }

    public enum AgeCategory
    {
        U15,
        U17,
        U20,
        U23,
        Senior,
        Veterans,
    }

    /// <summary>Singlet colour and mat corner. Red is on the left, blue on the right.</summary>
    public enum Corner
    {
        Red,
        Blue,
    }

    /// <summary>How a bout ended, before classification points are applied (Art. 29, 41).</summary>
    public enum VictoryType
    {
        Fall,
        Injury,
        ThreeCautions,
        Forfeit,
        Disqualification,
        TechnicalSuperiority,
        Points,
    }

    /// <summary>Bout endings with no winner (Art. 41).</summary>
    public enum DoubleLossType
    {
        DoubleDisqualification,
        DoubleInjury,
        DoubleForfeit,
    }

    public static class CornerExtensions
    {
        public static Corner Opponent(this Corner corner) => corner == Corner.Red ? Corner.Blue : Corner.Red;
    }

    public static class WrestlingStyleExtensions
    {
        /// <summary>Freestyle and Women's wrestling share the freestyle rule set (Women's adds a double nelson ban).</summary>
        public static bool UsesFreestyleRules(this WrestlingStyle style) => style != WrestlingStyle.GrecoRoman;
    }
}
