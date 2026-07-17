using System;
using Xunit;

namespace NetcodeArena.Core.Tests.Assertions;

/// <summary>
/// xUnit's <c>Assert.Equal(double, double, int)</c> and <c>Assert.Equal(float, float, float)</c>
/// overloads are ambiguous when called with float literals/precision ints, so the whole test
/// suite routes float comparisons through this single, unambiguous helper instead.
/// </summary>
public static class Approx
{
    public static void Equal(float expected, float actual, float epsilon = 0.001f)
    {
        var difference = MathF.Abs(expected - actual);
        Assert.True(
            difference <= epsilon,
            $"Expected {actual} to be within {epsilon} of {expected}, but differed by {difference}.");
    }
}
