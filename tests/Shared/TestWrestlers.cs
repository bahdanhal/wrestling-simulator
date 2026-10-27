using Wrestling.Core.Calendar;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Tests.Shared;

/// <summary>Builds valid fictional wrestlers for tests.</summary>
public static class TestWrestlers
{
    /// <summary>A wrestler who knows the given techniques at the given proficiency.</summary>
    public static Wrestler WithRepertoire(Wrestler wrestler, int proficiency, params string[] techniqueIds)
    {
        wrestler.Repertoire.Clear();
        foreach (string id in techniqueIds)
        {
            wrestler.Repertoire.Add(new RepertoireEntry { TechniqueId = id, Proficiency = proficiency, Side = TechniqueSide.Both });
        }

        return wrestler;
    }

    public static Wrestler Create(
        string id = "wr-1",
        Gender gender = Gender.Male,
        WrestlingStyle style = WrestlingStyle.Freestyle,
        int attributeValue = 50,
        Action<AttributeSet>? adjust = null)
    {
        var attributes = new AttributeSet(attributeValue);
        adjust?.Invoke(attributes);
        return new Wrestler
        {
            Id = id,
            Name = new PersonName { Given = "Arin", Family = "Kalvet" },
            Country = "EST",
            Gender = gender,
            Style = style,
            BirthDate = GameDate.Create(2004, 3, 14),
            Body = new BodyProfile
            {
                HeightCm = 176,
                WingspanCm = 181,
                LegRatio = Fix64.Parse("0.47"),
                Frame = Frame.Medium,
                NaturalWeightKg = Fix64.Parse("76.5"),
                DominantSide = Side.Right,
            },
            Attributes = attributes,
            Hidden = new HiddenTraits
            {
                Potential = new AttributeSet(90),
                InjuryProneness = Fix64.One,
                WeightCutTolerance = Fix64.Parse("1.1"),
                Professionalism = 70,
                Adaptability = 60,
                PeakAge = 27,
            },
            Repertoire =
            {
                new RepertoireEntry { TechniqueId = "technique.fs.double_leg_blast", Proficiency = 55, Side = TechniqueSide.Both, Signature = true },
                new RepertoireEntry { TechniqueId = "technique.all.arm_drag", Proficiency = 40, Side = TechniqueSide.Right },
            },
            Condition = new Condition { WalkAroundWeightKg = Fix64.Parse("78.2"), Form = 2, Morale = 64 },
        };
    }
}
