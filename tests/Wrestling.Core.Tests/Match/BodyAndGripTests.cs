using Wrestling.Core.Match;
using Wrestling.Core.Numerics;
using Wrestling.Core.Rules;

namespace Wrestling.Core.Tests.Match;

public class BodyAndGripTests
{
    [Fact]
    public void ContinuousValuesAreClamped()
    {
        var body = new BodyState { Level = Fix64.FromInt(2), Balance = -Fix64.One, BaseWidth = Fix64.FromInt(5), TorsoRotationDegrees = Fix64.FromInt(400) };
        Assert.Equal(Fix64.One, body.Level);
        Assert.Equal(Fix64.Zero, body.Balance);
        Assert.Equal(Fix64.Parse("1.2"), body.BaseWidth);
        Assert.Equal(Fix64.FromInt(180), body.TorsoRotationDegrees);
        Assert.Throws<ArgumentException>(() => body.Facing = FixVector2.Zero);
    }

    [Theory]
    [InlineData(GroundState.Standing, 150, false)]
    [InlineData(GroundState.BothKnees, 90, false)] // the neutral point is not danger
    [InlineData(GroundState.Supine, 91, true)]
    [InlineData(GroundState.Bridge, 170, true)]
    [InlineData(GroundState.Prone, 10, false)]
    public void DangerPosition_Article35(GroundState ground, int rotation, bool danger)
    {
        var body = new BodyState { Ground = ground, TorsoRotationDegrees = Fix64.FromInt(rotation) };
        Assert.Equal(danger, body.IsInDangerPosition);
    }

    [Fact]
    public void OneKneeDown_CountsAsParTerre()
    {
        Assert.True(new BodyState { Ground = GroundState.OneKnee }.IsGrounded);
        Assert.False(new BodyState().IsGrounded);
    }

    [Fact]
    public void FeetSpreadAcrossTheBaseAndLeadFootIsForward()
    {
        var body = new BodyState { Position = FixVector2.Zero, Facing = FixVector2.UnitX, BaseWidth = Fix64.One };
        (FixVector2 left, FixVector2 right) = body.FootPositions();
        Assert.Equal(new FixVector2(Fix64.Zero, Fix64.Half), left);
        Assert.Equal(new FixVector2(Fix64.Zero, -Fix64.Half), right);

        body.Stance = Stance.StaggeredLeftLead;
        (left, right) = body.FootPositions();
        Assert.True(left.X > Fix64.Zero);
        Assert.True(right.X < Fix64.Zero);
    }

    [Fact]
    public void GrecoForbidsLegGrips()
    {
        var greco = new GripSet(WrestlingStyle.GrecoRoman);
        Assert.False(greco.CanTake(Corner.Red, GripType.DoubleLeg, GripSide.Left, out string reason));
        Assert.Contains("illegal", reason);
        Assert.True(greco.CanTake(Corner.Red, GripType.BodyLock, GripSide.Left, out _));

        var freestyle = new GripSet(WrestlingStyle.Womens);
        Assert.True(freestyle.CanTake(Corner.Red, GripType.SingleLeg, GripSide.Right, out _));
    }

    [Fact]
    public void HandsLimitSimultaneousGrips()
    {
        var grips = new GripSet(WrestlingStyle.Freestyle);
        grips.Take(Corner.Red, GripType.CollarTie, GripSide.Right, Fix64.Half, Fix64.One, 0);
        Assert.True(grips.CanTake(Corner.Red, GripType.WristControl, GripSide.Left, out _));
        Assert.False(grips.CanTake(Corner.Red, GripType.Underhook, GripSide.Right, out _));
        Assert.False(grips.CanTake(Corner.Red, GripType.DoubleLeg, GripSide.Left, out _));

        grips.Take(Corner.Red, GripType.WristControl, GripSide.Left, Fix64.Half, Fix64.One, 1);
        Assert.Throws<InvalidOperationException>(() => grips.Take(Corner.Red, GripType.Overhook, GripSide.Left, Fix64.Half, Fix64.One, 2));

        // The opponent's hands are independent.
        Assert.True(grips.CanTake(Corner.Blue, GripType.BodyLock, GripSide.Left, out _));

        grips.ReleaseAll(Corner.Red);
        Assert.Empty(grips.Of(Corner.Red));
    }

    [Fact]
    public void GripValuesAreClamped()
    {
        var grips = new GripSet(WrestlingStyle.Freestyle);
        Grip grip = grips.Take(Corner.Blue, GripType.Underhook, GripSide.Left, Fix64.FromInt(3), -Fix64.One, 5);
        Assert.Equal(Fix64.One, grip.Quality);
        Assert.Equal(Fix64.Zero, grip.Strength);
        Assert.True(grips.Has(Corner.Blue, GripType.Underhook));
        grips.Release(grip);
        Assert.Throws<ArgumentException>(() => grips.Release(grip));
    }
}
