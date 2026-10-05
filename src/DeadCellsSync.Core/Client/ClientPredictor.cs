using System.Collections.Generic;

namespace DeadCellsSync.Core.Client;

/// <summary>
/// Client-side prediction + server reconciliation for the LOCAL player only.
///
/// Design decision (documented in README): the client predicts its own movement and weapon
/// cooldown immediately on input, for zero-latency-feeling controls, but it never predicts
/// damage to itself or its opponent - health changes are applied only when they arrive in an
/// authoritative snapshot. This mirrors how real competitive shooters treat "hit confirmation"
/// as server-only truth while still predicting movement locally.
///
/// Algorithm:
///  1. Every local tick, <see cref="ApplyInput"/> advances the predicted state with
///     <see cref="PlayerMovement.Step"/> and appends the input to a history buffer.
///  2. Whenever a snapshot arrives, <see cref="Reconcile"/> discards history up to the acked
///     input sequence, snaps the predicted state to the server's authoritative state for this
///     player, then REPLAYS every input still in the history (i.e. sent after the ack) through
///     the same movement function to fast-forward back to "now".
/// </summary>
public sealed class ClientPredictor
{
    private readonly List<PlayerInput> _pendingInputs = new();
    private PlayerState _predictedState;
    private uint _nextSequence;

    /// <summary>How many reconciliations produced a final state different from what had
    /// already been predicted before the correction - i.e. a real, visible correction.</summary>
    public int MispredictionCount { get; private set; }

    public int ReconciliationCount { get; private set; }
    public PlayerState PredictedState => _predictedState;
    public int PendingInputCount => _pendingInputs.Count;
    public IReadOnlyList<PlayerInput> PendingInputs => _pendingInputs;

    public ClientPredictor(PlayerState initialState)
    {
        _predictedState = initialState;
    }

    /// <summary>
    /// Builds and applies the next input tick locally (optimistic prediction), returning the
    /// input so the caller can send it to the server unmodified.
    /// </summary>
    public PlayerInput ApplyInput(uint clientTick, Vector2F move, Vector2F aimDirection, bool fire)
    {
        var input = new PlayerInput(_nextSequence++, clientTick, move, aimDirection, fire);
        _predictedState = Predict(_predictedState, input);
        _pendingInputs.Add(input);
        return input;
    }

    /// <summary>
    /// Reconciles local prediction against an authoritative snapshot for this player. Rewinds
    /// to the server's state, discards acknowledged inputs, and replays the remainder. Returns
    /// true if the replay produced a final MOVEMENT (position/velocity) different from what
    /// had already been predicted - a visible correction to the thing the client actually
    /// predicts. Health is deliberately excluded from this comparison: the client never
    /// predicts damage locally (see class doc), so a health change arriving from the server is
    /// expected, correct behavior, not a prediction bug, and must not be counted as one.
    /// </summary>
    public bool Reconcile(PlayerState authoritativeState)
    {
        ReconciliationCount++;
        var ackedSequence = authoritativeState.LastProcessedInputSequence;
        var predictionBeforeCorrection = _predictedState;

        _pendingInputs.RemoveAll(i => i.Sequence <= ackedSequence);

        var rewound = authoritativeState;
        foreach (var input in _pendingInputs)
        {
            rewound = Predict(rewound, input);
        }

        _predictedState = rewound;

        var mispredicted = !MovementApproximatelyEquals(predictionBeforeCorrection, rewound);
        if (mispredicted)
        {
            MispredictionCount++;
        }

        return mispredicted;
    }

    private static bool MovementApproximatelyEquals(in PlayerState a, in PlayerState b, float epsilon = 0.01f)
    {
        return Vector2F.Distance(a.Position, b.Position) <= epsilon
            && Vector2F.Distance(a.Velocity, b.Velocity) <= epsilon;
    }

    private static PlayerState Predict(in PlayerState state, in PlayerInput input)
    {
        var moved = PlayerMovement.Step(state, input, SimulationConfig.FixedDeltaTime);
        moved = moved.WithLastProcessedInputSequence(input.Sequence);

        // Predict only our own cooldown timer locally (affects UI/feel); never predict damage.
        if (input.Fire && moved.FireCooldownRemaining <= 0f)
        {
            moved = new PlayerState(
                moved.Position,
                moved.Velocity,
                moved.Health,
                SimulationConfig.WeaponCooldownSeconds,
                moved.LastProcessedInputSequence);
        }

        return moved;
    }
}
