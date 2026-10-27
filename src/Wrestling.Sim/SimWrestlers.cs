using Wrestling.Core.Calendar;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Techniques;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Sim;

/// <summary>Random wrestlers for balancing runs: attributes around a level with a spread, the full style repertoire.</summary>
public static class SimWrestlers
{
    private static readonly string[] GivenNames = { "Arin", "Bekzat", "Dario", "Emil", "Farid", "Goran", "Hiro", "Ivo", "Jaro", "Kenan", "Levan", "Mato" };
    private static readonly string[] WomenNames = { "Aida", "Bela", "Dana", "Elin", "Fatma", "Gala", "Hana", "Ines", "Jana", "Kira", "Lea", "Mira" };
    private static readonly string[] FamilyNames = { "Aras", "Borov", "Cerny", "Dalan", "Erdem", "Falk", "Gorin", "Holm", "Ivers", "Juric", "Kaya", "Lind" };

    public static Wrestler Generate(string id, WrestlingStyle style, int level, int spread, TechniqueCatalog catalog, Pcg32 random)
    {
        Gender gender = style == WrestlingStyle.Womens ? Gender.Female : Gender.Male;
        var attributes = new AttributeSet();
        foreach (WrestlerAttribute attribute in WrestlerAttributes.All)
        {
            attributes[attribute] = Math.Clamp(level + random.NextInt(-spread, spread + 1), 1, 95);
        }

        var wrestler = new Wrestler
        {
            Id = id,
            Name = new PersonName
            {
                Given = (gender == Gender.Female ? WomenNames : GivenNames)[random.NextInt(0, GivenNames.Length)],
                Family = FamilyNames[random.NextInt(0, FamilyNames.Length)],
            },
            Country = "SIM",
            Gender = gender,
            Style = style,
            BirthDate = GameDate.Create(2000, 1, 1),
            Body = new BodyProfile
            {
                HeightCm = 170 + random.NextInt(0, 15),
                WingspanCm = 172 + random.NextInt(0, 16),
                LegRatio = Fix64.FromRatio(46 + random.NextInt(0, 4), 100),
                Frame = Frame.Medium,
                NaturalWeightKg = Fix64.FromInt(74),
                DominantSide = random.NextUInt(2) == 0 ? Side.Left : Side.Right,
            },
            Attributes = attributes,
            Hidden = new HiddenTraits { Potential = new AttributeSet(WrestlerAttributes.Maximum) },
            Condition = new Condition { WalkAroundWeightKg = Fix64.FromInt(76) },
        };

        foreach (TechniqueDefinition technique in catalog.AllowedFor(style, AgeCategory.Senior))
        {
            wrestler.Repertoire.Add(new RepertoireEntry
            {
                TechniqueId = technique.Id,
                Proficiency = Math.Clamp(level - 20 + random.NextInt(0, 41), 1, 100),
                Side = TechniqueSide.Both,
            });
        }

        return wrestler.EnsureValid();
    }
}
