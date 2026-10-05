using DeadCellsSync.Core;
using DeadCellsSync.Core.Snapshot;
using DeadCellsSync.Core.Tests.Assertions;
using Xunit;

namespace DeadCellsSync.Core.Tests;

public class SnapshotInterpolationBufferTests
{
    private static PlayerState StateAt(float x) =>
        new(new Vector2F(x, 0), Vector2F.Zero, SimulationConfig.MaxHealth, 0f, 0);

    [Fact]
    public void Sample_BetweenTwoSnapshots_LerpsPosition()
    {
        var buffer = new SnapshotInterpolationBuffer(interpolationDelaySeconds: 0f);
        buffer.AddSnapshot(0, StateAt(0f));
        buffer.AddSnapshot(10, StateAt(10f));

        // Halfway between tick 0 and tick 10, at 30 ticks/sec => tick 5's time.
        var halfwayTime = 5f * SimulationConfig.FixedDeltaTime;
        var sampled = buffer.Sample(halfwayTime);

        Approx.Equal(5f, sampled.Position.X, 0.01f);
    }

    [Fact]
    public void Sample_BeforeFirstSnapshot_ReturnsFirstSnapshot()
    {
        var buffer = new SnapshotInterpolationBuffer(interpolationDelaySeconds: 0f);
        buffer.AddSnapshot(10, StateAt(10f));
        buffer.AddSnapshot(20, StateAt(20f));

        var sampled = buffer.Sample(localTimeSeconds: 0f);

        Approx.Equal(10f, sampled.Position.X, 0.01f);
    }

    [Fact]
    public void Sample_PastLastSnapshot_HoldsLastKnownPositionInsteadOfExtrapolating()
    {
        var buffer = new SnapshotInterpolationBuffer(interpolationDelaySeconds: 0f);
        buffer.AddSnapshot(0, StateAt(0f));
        buffer.AddSnapshot(10, StateAt(10f));

        var farFutureTime = 1000f * SimulationConfig.FixedDeltaTime;
        var sampled = buffer.Sample(farFutureTime);

        Approx.Equal(10f, sampled.Position.X, 0.01f);
    }

    [Fact]
    public void AddSnapshot_OutOfOrderArrival_IsInsertedInSortedPosition()
    {
        var buffer = new SnapshotInterpolationBuffer(interpolationDelaySeconds: 0f);
        buffer.AddSnapshot(0, StateAt(0f));
        buffer.AddSnapshot(20, StateAt(20f));
        buffer.AddSnapshot(10, StateAt(10f)); // arrives late, but for an earlier tick

        var halfwayTime = 5f * SimulationConfig.FixedDeltaTime;
        var sampled = buffer.Sample(halfwayTime);

        Approx.Equal(5f, sampled.Position.X, 0.01f);
    }

    [Fact]
    public void AddSnapshot_DuplicateTick_IsIgnored()
    {
        var buffer = new SnapshotInterpolationBuffer(interpolationDelaySeconds: 0f);
        buffer.AddSnapshot(0, StateAt(0f));
        buffer.AddSnapshot(10, StateAt(10f));
        buffer.AddSnapshot(10, StateAt(999f)); // duplicate tick with different payload: ignored

        var sampled = buffer.Sample(10f * SimulationConfig.FixedDeltaTime);

        Approx.Equal(10f, sampled.Position.X, 0.01f);
    }

    [Fact]
    public void Sample_AppliesConfiguredInterpolationDelay()
    {
        var buffer = new SnapshotInterpolationBuffer(interpolationDelaySeconds: SimulationConfig.InterpolationDelaySeconds);
        buffer.AddSnapshot(0, StateAt(0f));
        buffer.AddSnapshot(10, StateAt(10f));

        // At localTime = 10 ticks worth of time, render time should be pulled back by the
        // interpolation delay, landing us before tick 10 rather than exactly on it.
        var localTime = 10f * SimulationConfig.FixedDeltaTime;
        var sampled = buffer.Sample(localTime);

        Assert.True(sampled.Position.X < 10f);
    }

    [Fact]
    public void Sample_InterpolatesVelocityBetweenTwoSnapshots()
    {
        var buffer = new SnapshotInterpolationBuffer(interpolationDelaySeconds: 0f);
        var a = new PlayerState(new Vector2F(0f, 0f), Vector2F.Zero, SimulationConfig.MaxHealth, 0f, 0);
        var b = new PlayerState(new Vector2F(10f, 0f), new Vector2F(10f, 0f), SimulationConfig.MaxHealth, 0f, 0);
        buffer.AddSnapshot(0, a);
        buffer.AddSnapshot(10, b);

        var sampled = buffer.Sample(5f * SimulationConfig.FixedDeltaTime);

        Approx.Equal(5f, sampled.Velocity.X, 0.01f);
        Approx.Equal(0f, sampled.Velocity.Y, 0.01f);
    }

    [Fact]
    public void Sample_InterpolatesHealthAndCooldown()
    {
        var buffer = new SnapshotInterpolationBuffer(interpolationDelaySeconds: 0f);
        var a = new PlayerState(new Vector2F(0f, 0f), Vector2F.Zero, 100f, 0f, 0);
        var b = new PlayerState(new Vector2F(10f, 0f), Vector2F.Zero, 50f, 1f, 5);
        buffer.AddSnapshot(0, a);
        buffer.AddSnapshot(10, b);

        var sampled = buffer.Sample(5f * SimulationConfig.FixedDeltaTime);

        Approx.Equal(75f, sampled.Health, 0.01f);
        Approx.Equal(0.5f, sampled.FireCooldownRemaining, 0.01f);
    }

    [Fact]
    public void AddSnapshot_UnstableIntervals_InterpolatesCorrectly()
    {
        var buffer = new SnapshotInterpolationBuffer(interpolationDelaySeconds: 0f);
        buffer.AddSnapshot(0, StateAt(0f));
        buffer.AddSnapshot(12, StateAt(12f));
        buffer.AddSnapshot(25, StateAt(25f));

        var betweenFirstTwo = buffer.Sample(6f * SimulationConfig.FixedDeltaTime);
        Approx.Equal(6f, betweenFirstTwo.Position.X, 0.01f);

        var betweenLastTwo = buffer.Sample(20f * SimulationConfig.FixedDeltaTime);
        Approx.Equal(20f, betweenLastTwo.Position.X, 0.01f);
    }
}
