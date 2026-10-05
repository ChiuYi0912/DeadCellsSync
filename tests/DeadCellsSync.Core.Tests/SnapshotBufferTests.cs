using DeadCellsSync.Core.Snapshot;
using DeadCellsSync.Core.Tests.Assertions;
using Xunit;

namespace DeadCellsSync.Core.Tests;

public class SnapshotBufferTests
{
    [Fact]
    public void TryGetInterpolation_BetweenTwoSnapshots_ComputesAlpha()
    {
        var buffer = new SnapshotBuffer<double>(interpolationDelaySeconds: 0.0);
        buffer.AddSnapshot(1.0, 10.0);
        buffer.AddSnapshot(1.1, 20.0);

        var ok = buffer.TryGetInterpolation(1.05, out var prev, out var next, out var alpha);

        Assert.True(ok);
        Approx.Equal(10.0, prev, 0.0001);
        Approx.Equal(20.0, next, 0.0001);
        Approx.Equal(0.5f, (float)alpha, 0.001f);
    }

    [Fact]
    public void TryGetInterpolation_Empty_ReturnsFalse()
    {
        var buffer = new SnapshotBuffer<double>(interpolationDelaySeconds: 0.0);

        Assert.False(buffer.TryGetInterpolation(1.0, out _, out _, out _));
    }

    [Fact]
    public void TryGetInterpolation_PastLastSnapshot_HoldsLast()
    {
        var buffer = new SnapshotBuffer<double>(interpolationDelaySeconds: 0.0);
        buffer.AddSnapshot(0.0, 1.0);
        buffer.AddSnapshot(0.1, 2.0);

        buffer.TryGetInterpolation(10.0, out var prev, out var next, out var alpha);

        Approx.Equal(2.0, prev, 0.0001);
        Approx.Equal(2.0, next, 0.0001);
        Approx.Equal(0f, (float)alpha, 0.001f);
    }

    [Fact]
    public void AddSnapshot_OutOfOrder_IsInsertedInSortedPosition()
    {
        var buffer = new SnapshotBuffer<double>(interpolationDelaySeconds: 0.0);
        buffer.AddSnapshot(0.0, 0.0);
        buffer.AddSnapshot(0.2, 2.0);
        buffer.AddSnapshot(0.1, 1.0); // arrives late, for an earlier time

        buffer.TryGetInterpolation(0.05, out var prev, out var next, out var alpha);

        Approx.Equal(0.0, prev, 0.0001);
        Approx.Equal(1.0, next, 0.0001);
        Approx.Equal(0.5f, (float)alpha, 0.001f);
    }

    [Fact]
    public void AddSnapshot_DuplicateTime_IsIgnored()
    {
        var buffer = new SnapshotBuffer<double>(interpolationDelaySeconds: 0.0);
        buffer.AddSnapshot(0.0, 0.0);
        buffer.AddSnapshot(0.1, 1.0);
        buffer.AddSnapshot(0.1, 999.0); // duplicate timestamp with different payload: ignored

        buffer.TryGetInterpolation(0.1, out _, out var next, out _);

        Approx.Equal(1.0, next, 0.0001);
    }

    [Fact]
    public void TryGetInterpolation_AppliesInterpolationDelay()
    {
        var buffer = new SnapshotBuffer<double>(interpolationDelaySeconds: 0.05);
        buffer.AddSnapshot(0.0, 0.0);
        buffer.AddSnapshot(0.1, 1.0);

        // localTime 0.1 -> renderTime 0.05 -> halfway between 0.0 and 0.1
        buffer.TryGetInterpolation(0.1, out _, out _, out var alpha);

        Approx.Equal(0.5f, (float)alpha, 0.001f);
    }
}
