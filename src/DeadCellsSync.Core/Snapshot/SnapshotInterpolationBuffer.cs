using System;
using System.Collections.Generic;

namespace DeadCellsSync.Core.Snapshot;

/// <summary>
/// Buffers incoming remote-player states (one entry per server snapshot) and reconstructs a
/// smooth position for rendering by interpolating between the two snapshots that bracket a
/// deliberately-delayed "render time". Delaying render time by
/// <see cref="SimulationConfig.InterpolationDelaySeconds"/> behind the latest received
/// snapshot is what absorbs network jitter: as long as jitter stays under the delay, there
/// are always two real snapshots to interpolate between instead of guessing (extrapolating).
/// </summary>
public sealed class SnapshotInterpolationBuffer
{
    private readonly List<(uint tick, PlayerState state)> _buffer = new();
    private readonly float _interpolationDelaySeconds;

    public SnapshotInterpolationBuffer(float interpolationDelaySeconds = SimulationConfig.InterpolationDelaySeconds)
    {
        _interpolationDelaySeconds = interpolationDelaySeconds;
    }

    public int Count => _buffer.Count;

    /// <summary>
    /// Ingests a freshly-arrived snapshot for the remote player. Snapshots that arrive after
    /// we've already sampled past their tick (too-late stragglers) are dropped rather than
    /// causing the render position to jump backwards. Duplicate ticks are ignored.
    /// </summary>
    public void AddSnapshot(uint tick, PlayerState state)
    {
        if (_buffer.Count > 0 && tick <= _buffer[^1].tick)
        {
            // Out-of-order arrival relative to what we already hold. Insert in sorted
            // position (there is always one, since the last entry has tick >= this one),
            // unless it is an exact duplicate of a tick we already have.
            var insertionIndex = _buffer.FindIndex(entry => entry.tick >= tick);
            if (_buffer[insertionIndex].tick == tick)
            {
                return; // duplicate, ignore
            }

            _buffer.Insert(insertionIndex, (tick, state));
            return;
        }

        _buffer.Add((tick, state));
    }

    /// <summary>
    /// Returns the interpolated (or extrapolated, if the buffer has run dry) remote player
    /// state to render at <paramref name="localTimeSeconds"/>. Also prunes snapshots that are
    /// now fully in the past and can never be sampled again.
    /// </summary>
    public PlayerState Sample(float localTimeSeconds)
    {
        if (_buffer.Count == 0)
        {
            throw new InvalidOperationException("Cannot sample an empty interpolation buffer.");
        }

        var renderTime = localTimeSeconds - _interpolationDelaySeconds;
        var renderTick = renderTime / SimulationConfig.FixedDeltaTime;

        if (renderTick <= _buffer[0].tick)
        {
            return _buffer[0].state;
        }

        for (var i = 0; i < _buffer.Count - 1; i++)
        {
            var (tickA, stateA) = _buffer[i];
            var (tickB, stateB) = _buffer[i + 1];

            if (renderTick >= tickA && renderTick <= tickB)
            {
                PruneBefore(i);
                if (tickB == tickA)
                {
                    return stateB;
                }

                var t = (renderTick - tickA) / (tickB - tickA);
                return Interpolate(stateA, stateB, t);
            }
        }

        // Render time is ahead of every snapshot we have (buffer underrun): hold last known
        // position rather than extrapolating blindly through walls/obstacles.
        var last = _buffer[^1];
        PruneBefore(_buffer.Count - 1);
        return last.state;
    }

    /// <summary>
    /// Read-only mirror of <see cref="Sample"/>: returns the two buffered snapshots that bracket
    /// the render time for <paramref name="localTimeSeconds"/> plus the interpolation alpha
    /// between them, without pruning. Intended for teaching/debugging the interpolation timeline
    /// (previous/next snapshot + alpha); <see cref="Sample"/> remains the way to get the final
    /// interpolated state.
    /// </summary>
    public InterpolationSample GetInterpolationSample(float localTimeSeconds)
    {
        if (_buffer.Count == 0)
        {
            throw new InvalidOperationException("Cannot sample an empty interpolation buffer.");
        }

        var renderTick = (localTimeSeconds - _interpolationDelaySeconds) / SimulationConfig.FixedDeltaTime;

        if (renderTick <= _buffer[0].tick)
        {
            return new InterpolationSample(
                _buffer[0].tick, _buffer[0].tick, _buffer[0].state, _buffer[0].state, 0f);
        }

        for (var i = 0; i < _buffer.Count - 1; i++)
        {
            var (tickA, stateA) = _buffer[i];
            var (tickB, stateB) = _buffer[i + 1];

            if (renderTick >= tickA && renderTick <= tickB)
            {
                var alpha = tickB == tickA ? 0f : (renderTick - tickA) / (tickB - tickA);
                return new InterpolationSample(tickA, tickB, stateA, stateB, alpha);
            }
        }

        // Render time is ahead of every snapshot (buffer underrun): hold the last known one.
        var last = _buffer[^1];
        return new InterpolationSample(last.tick, last.tick, last.state, last.state, 0f);
    }

    private void PruneBefore(int keepFromIndex)
    {
        if (keepFromIndex <= 0)
        {
            return;
        }

        _buffer.RemoveRange(0, keepFromIndex);
    }

    private static PlayerState Interpolate(in PlayerState a, in PlayerState b, float t)
    {
        var position = Vector2F.Lerp(a.Position, b.Position, t);
        var velocity = Vector2F.Lerp(a.Velocity, b.Velocity, t);
        var health = a.Health + (b.Health - a.Health) * t;
        var cooldown = a.FireCooldownRemaining + (b.FireCooldownRemaining - a.FireCooldownRemaining) * t;
        return new PlayerState(position, velocity, health, cooldown, b.LastProcessedInputSequence);
    }
}

/// <summary>
/// The two buffered snapshots that bracket a render time, plus the interpolation alpha between
/// them. Produced by <see cref="SnapshotInterpolationBuffer.GetInterpolationSample"/> for
/// teaching/debugging the interpolation timeline.
/// </summary>
public readonly record struct InterpolationSample(
    uint PreviousTick,
    uint NextTick,
    PlayerState PreviousState,
    PlayerState NextState,
    float Alpha);
