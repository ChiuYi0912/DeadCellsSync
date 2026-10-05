using DeadCellsSync.Core;
using DeadCellsSync.Core.Client;
using DeadCellsSync.Core.Tests.Assertions;
using Xunit;

namespace DeadCellsSync.Core.Tests;

public class ClientPredictorTests
{
    [Fact]
    public void ApplyInput_MovesPredictedStateImmediately_BeforeAnyServerResponse()
    {
        var predictor = new ClientPredictor(PlayerState.Spawn(Vector2F.Zero));

        predictor.ApplyInput(1, new Vector2F(1, 0), new Vector2F(1, 0), fire: false);

        Assert.True(predictor.PredictedState.Position.X > 0f);
        Assert.Equal(1, predictor.PendingInputCount);
    }

    [Fact]
    public void Reconcile_WhenServerAgreesWithPrediction_ReportsNoMisprediction()
    {
        var predictor = new ClientPredictor(PlayerState.Spawn(Vector2F.Zero));
        var input = predictor.ApplyInput(1, new Vector2F(1, 0), new Vector2F(1, 0), fire: false);

        // Server simulated the exact same input the same way (the happy path: zero latency,
        // zero loss) and reports back the exact position the client already predicted.
        var authoritative = PlayerMovement
            .Step(PlayerState.Spawn(Vector2F.Zero), input, SimulationConfig.FixedDeltaTime)
            .WithLastProcessedInputSequence(input.Sequence);

        var mispredicted = predictor.Reconcile(authoritative);

        Assert.False(mispredicted);
        Assert.Equal(0, predictor.MispredictionCount);
        Assert.Empty(predictor.PendingInputs);
    }

    [Fact]
    public void Reconcile_PrunesAcknowledgedInputsButKeepsUnacknowledgedOnes()
    {
        var predictor = new ClientPredictor(PlayerState.Spawn(Vector2F.Zero));
        predictor.ApplyInput(1, new Vector2F(1, 0), new Vector2F(1, 0), false); // seq 0
        predictor.ApplyInput(2, new Vector2F(1, 0), new Vector2F(1, 0), false); // seq 1
        predictor.ApplyInput(3, new Vector2F(1, 0), new Vector2F(1, 0), false); // seq 2

        Assert.Equal(3, predictor.PendingInputCount);

        // Server has only processed up through sequence 0 so far.
        var authoritative = PlayerState.Spawn(new Vector2F(0.2f, 0f)).WithLastProcessedInputSequence(0);
        predictor.Reconcile(authoritative);

        Assert.Equal(2, predictor.PendingInputCount); // sequences 1 and 2 remain unacknowledged
    }

    [Fact]
    public void Reconcile_WithDivergentServerState_CorrectsPredictionAndReportsMisprediction()
    {
        var predictor = new ClientPredictor(PlayerState.Spawn(Vector2F.Zero));
        predictor.ApplyInput(1, new Vector2F(1, 0), new Vector2F(1, 0), false);

        // Server disagrees wildly (e.g. client was hit by knockback it didn't predict).
        var authoritative = new PlayerState(new Vector2F(50f, 50f), Vector2F.Zero, 100f, 0f, 0);

        var mispredicted = predictor.Reconcile(authoritative);

        Assert.True(mispredicted);
        Assert.Equal(1, predictor.MispredictionCount);
        // After reconciliation the predicted state must reflect the server's correction, not
        // the client's stale guess.
        Approx.Equal(50f, predictor.PredictedState.Position.X, 0.1f);
    }

    [Fact]
    public void Reconcile_ReplaysUnacknowledgedInputsOnTopOfServerCorrection()
    {
        var predictor = new ClientPredictor(PlayerState.Spawn(Vector2F.Zero));
        predictor.ApplyInput(1, new Vector2F(1, 0), new Vector2F(1, 0), false); // seq 0, acked below
        predictor.ApplyInput(2, new Vector2F(1, 0), new Vector2F(1, 0), false); // seq 1, replayed

        // Server acknowledges only seq 0 and reports a position different from what the
        // client originally guessed for that tick.
        var authoritative = new PlayerState(new Vector2F(1f, 0f), Vector2F.Zero, 100f, 0f, 0);

        predictor.Reconcile(authoritative);

        var expectedX = 1f + SimulationConfig.MoveSpeed * SimulationConfig.FixedDeltaTime;
        Approx.Equal(expectedX, predictor.PredictedState.Position.X, 0.001f);
        Assert.Single(predictor.PendingInputs);
    }
}
