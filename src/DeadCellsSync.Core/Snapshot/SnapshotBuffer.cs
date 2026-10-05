using System;
using System.Collections.Generic;

namespace DeadCellsSync.Core.Snapshot;

/// <summary>
/// A generic, timestamp-based snapshot buffer for interpolating any state type
/// <typeparamref name="T"/>. Unlike <see cref="SnapshotInterpolationBuffer"/> (which is hard-wired
/// to the arena's <see cref="PlayerState"/> and a fixed 30 Hz tick), this buffer keys snapshots by
/// a <c>double</c> timestamp (seconds) and only computes the bracketing snapshots plus the
/// interpolation alpha. The type-specific Lerp is left to the caller, because it depends on how the
/// caller's state is laid out (e.g. Dead Cells double pixel coordinates).
/// </summary>
public sealed class SnapshotBuffer<T>
{
    private readonly List<(double time, T state)> _buffer = new();
    private readonly double _interpolationDelaySeconds;

    public SnapshotBuffer(double interpolationDelaySeconds)
    {
        _interpolationDelaySeconds = interpolationDelaySeconds;
    }

    public int Count => _buffer.Count;

    /// <summary>
    /// Ingests a snapshot for time <paramref name="time"/>. Out-of-order arrivals are inserted in
    /// sorted position; an exact duplicate timestamp is ignored.
    /// </summary>
    public void AddSnapshot(double time, T state)
    {
        if (_buffer.Count > 0 && time <= _buffer[^1].time)
        {
            var insertionIndex = _buffer.FindIndex(entry => entry.time >= time);
            if (_buffer[insertionIndex].time == time)
            {
                return; // duplicate timestamp, ignore
            }

            _buffer.Insert(insertionIndex, (time, state));
            return;
        }

        _buffer.Add((time, state));
    }

    public void Clear() => _buffer.Clear();

    /// <summary>
    /// Finds the two snapshots that bracket the render time for <paramref name="localTimeSeconds"/>
    /// (render time = localTime - interpolation delay) and returns them plus the interpolation
    /// alpha between them. Returns <c>false</c> only when the buffer is empty. When the render time
    /// falls before the first snapshot or past the last one, <paramref name="previous"/> and
    /// <paramref name="next"/> are both the nearest snapshot and alpha is 0 (hold, do not
    /// extrapolate). Also prunes snapshots that are now fully in the past.
    /// </summary>
    public bool TryGetInterpolation(double localTimeSeconds, out T previous, out T next, out double alpha)
    {
        if (_buffer.Count == 0)
        {
            previous = default!;
            next = default!;
            alpha = 0;
            return false;
        }

        var renderTime = localTimeSeconds - _interpolationDelaySeconds;

        if (renderTime <= _buffer[0].time)
        {
            previous = next = _buffer[0].state;
            alpha = 0;
            return true;
        }

        for (var i = 0; i < _buffer.Count - 1; i++)
        {
            var (timeA, stateA) = _buffer[i];
            var (timeB, stateB) = _buffer[i + 1];

            if (renderTime >= timeA && renderTime <= timeB)
            {
                PruneBefore(i);
                previous = stateA;
                next = stateB;
                alpha = timeB == timeA ? 0 : (renderTime - timeA) / (timeB - timeA);
                return true;
            }
        }

        // Render time is ahead of every snapshot (buffer underrun): hold the last known state.
        var last = _buffer[^1];
        PruneBefore(_buffer.Count - 1);
        previous = next = last.state;
        alpha = 0;
        return true;
    }

    private void PruneBefore(int keepFromIndex)
    {
        if (keepFromIndex > 0)
        {
            _buffer.RemoveRange(0, keepFromIndex);
        }
    }
}
