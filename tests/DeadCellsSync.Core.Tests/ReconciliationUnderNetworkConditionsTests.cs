using DeadCellsSync.Core;
using DeadCellsSync.Core.Tests.Harness;
using Xunit;

namespace DeadCellsSync.Core.Tests;

/// <summary>
/// The centerpiece test suite: proves the client-prediction / server-reconciliation loop
/// converges to the server's authoritative truth even when the link is degraded, by actually
/// running the full client+server pipeline over a <see cref="DeadCellsSync.Core.SimulatedNetworkChannel{T}"/>
/// with injected latency and packet loss, then asserting no permanent desync remains once the
/// network has drained.
/// </summary>
public class ReconciliationUnderNetworkConditionsTests
{
    [Fact]
    public void PerfectNetwork_ProducesNoMispredictions()
    {
        var harness = new NetworkedMatchHarness(seed: 1, packetLossProbability: 0.0, minLatencyTicks: 0, maxLatencyTicks: 0);

        harness.StepMany(180);
        harness.DrainNetwork();

        Assert.Equal(0, harness.MispredictionCount);
        AssertConverged(harness);
    }

    [Theory]
    [InlineData(0.0, 2, 6)] // pure latency, no loss
    [InlineData(0.1, 0, 0)] // pure packet loss, no latency
    [InlineData(0.15, 1, 8)] // realistic degraded connection: both loss and jittery latency
    [InlineData(0.3, 3, 12)] // harsh connection
    public void DegradedNetwork_StillConvergesToServerTruthAfterDraining(
        double packetLoss, int minLatencyTicks, int maxLatencyTicks)
    {
        var harness = new NetworkedMatchHarness(seed: 42, packetLoss, minLatencyTicks, maxLatencyTicks);

        harness.StepMany(300);
        harness.DrainNetwork();

        AssertConverged(harness);
    }

    [Fact]
    public void DegradedNetwork_ActuallyExercisesReconciliationAndCorrections()
    {
        // Sanity check on the test harness itself: with real loss and latency injected, the
        // client's prediction MUST diverge from the server at least once and get corrected -
        // otherwise this whole suite would be vacuously true (network conditions not exercised).
        var harness = new NetworkedMatchHarness(seed: 7, packetLossProbability: 0.2, minLatencyTicks: 2, maxLatencyTicks: 10);

        harness.StepMany(300);

        Assert.True(harness.ReconciliationCount > 0);
        Assert.True(harness.MispredictionCount > 0, "expected the degraded link to cause at least one real misprediction+correction");
    }

    [Fact]
    public void DegradedNetwork_ClientNeverPermanentlyDrifts_AcrossRepeatedReconciliationWindows()
    {
        var harness = new NetworkedMatchHarness(seed: 55, packetLossProbability: 0.2, minLatencyTicks: 1, maxLatencyTicks: 9);

        // Check convergence repeatedly at intervals, not just once at the very end, to prove
        // the client is continuously being kept honest rather than happening to land close by
        // coincidence on a single final sample.
        for (var window = 0; window < 5; window++)
        {
            harness.StepMany(60);
            harness.DrainNetwork();
            AssertConverged(harness, epsilon: 0.05f);
        }
    }

    private static void AssertConverged(NetworkedMatchHarness harness, float epsilon = 0.02f)
    {
        var client = harness.ClientPredictedStateForA;
        var server = harness.ServerAuthoritativeStateForA;

        var distance = Vector2F.Distance(client.Position, server.Position);
        Assert.True(
            distance <= epsilon,
            $"client predicted position {client.Position} diverged from server authoritative " +
            $"position {server.Position} by {distance:F4} units after network drain");
    }
}
