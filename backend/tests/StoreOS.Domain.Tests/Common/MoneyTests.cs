using FluentAssertions;
using StoreOS.Domain.Common;

namespace StoreOS.Domain.Tests.Common;

public class MoneyTests
{
    [Fact]
    public void Round2_HalfUp_AwayFromZero_Rounds_Up_When_Midpoint_Positive()
    {
        Money.Round2(10.005m).Should().Be(10.01m);
    }

    [Fact]
    public void Round2_BelowMidpoint_Rounds_Down_When_Positive()
    {
        Money.Round2(10.004m).Should().Be(10.00m);
    }

    [Fact]
    public void Round2_OddCent_Midpoint_Rounds_Up()
    {
        Money.Round2(10.015m).Should().Be(10.02m);
    }

    [Fact]
    public void Round2_Negative_HalfUp_AwayFromZero_Rounds_AwayFromZero()
    {
        Money.Round2(-10.005m).Should().Be(-10.01m);
    }

    [Fact]
    public void Round2_Zero_Returns_Zero()
    {
        Money.Round2(0m).Should().Be(0.00m);
    }
}
