# DeadCellsSync

**A pure, engine-agnostic synchronization core for Dead Cells MultiplayerX: client-side prediction, server reconciliation, and snapshot interpolation — with no network transport.**

DeadCellsSync is the synchronization brain extracted from a 2-player arena prototype. It deliberately has
**no sockets, no packets, and no network protocol**. The real transport will be provided by
DeadCellsMultiplayerX; this library only owns the synchronization algorithms that sit between
"simulation state" and "what gets rendered".

## What it is (and isn't)

`DeadCellsSync.Core` is pure C# with zero engine or socket dependencies. The same deterministic
`ArenaSimulation.Step` runs on both the predicting client and the authoritative simulation, which is
what makes reconciliation trustworthy: given identical `(state, input)`, both sides produce identical
output.

The pipeline is:

```
Simulation (deterministic)
      ↓
Game State (PlayerState / PlayerInput)
      ↓
Snapshot (WorldSnapshot)
      ↓
Snapshot Buffer (SnapshotInterpolationBuffer)
      ↓
Interpolation / Prediction / Reconciliation
      ↓
Render State
```

The transport that would normally sit between *Snapshot* and *Snapshot Buffer* is replaced here by
`SimulatedNetworkChannel<T>` — a deterministic, in-process stand-in for a lossy link that lets tests
and the demo inject latency, jitter, and packet loss without opening a socket.

### The three core algorithms

- **Client-side prediction** (`ClientPredictor`) — the local player moves instantly on input, before
  any round trip.
- **Server reconciliation** (`ClientPredictor.Reconcile`) — when an authoritative snapshot arrives,
  the client rewinds to it and replays the inputs the server hasn't acknowledged yet, so prediction
  errors self-correct instead of accumulating.
- **Snapshot interpolation** (`SnapshotInterpolationBuffer`) — the remote player is rendered by
  interpolating between two buffered snapshots on a small delay, which absorbs jitter without ever
  guessing (extrapolating) through a wall.

### Design decisions worth knowing

- The client predicts its own movement and weapon cooldown for instant feedback, but never predicts
  *damage* — health only changes when an authoritative snapshot says so.
- If a player's input queue runs dry (loss/latency spike), the simulation holds that player in place
  rather than extrapolating them through geometry — the same "don't guess past what you know"
  philosophy used by the interpolation buffer on the render side.

## Project layout

```
src/DeadCellsSync.Core/        Pure sync core: simulation, snapshot, interpolation, prediction,
                               reconciliation, and SimulatedNetworkChannel (simulated transport).
src/DeadCellsSync.Demo/        Local synchronization demo (no real network).
tests/DeadCellsSync.Core.Tests/ xUnit suite — runs fully offline.
```

## Quick start

Requires the .NET 10 SDK. No network, Docker, or game engine is needed to build or test.

```bash
# Restore, build everything (Core + Demo + Tests)
dotnet build

# Run the full offline test suite (prediction, reconciliation under injected latency/loss,
# snapshot interpolation, weapon system, movement).
dotnet test
```

Run the movement interpolation teaching demo (no real network):

```bash
dotnet run --project src/DeadCellsSync.Demo
```

The demo prints a per-frame timeline (render time, previous/next snapshot, alpha, server vs render
position) so you can watch a remote player's position go from discrete server snapshots to a smooth
interpolated render. A full walkthrough — what a snapshot is, why render time lags, how alpha is
computed — lives in [src/DeadCellsSync.Demo/README.md](src/DeadCellsSync.Demo/README.md).

```
 renderT | serverT |  prevT |  nextT | alpha | serverX |  prevX |  nextX | renderX | serverV | renderV
---------+---------+--------+--------+-------+---------+--------+--------+---------+---------+---------
  0.050 |   0.133 |  0.000 |  0.100 |  0.50 |    1.33 |   0.00 |   1.00 |    0.50 |   10.00 |   10.00
  0.067 |   0.167 |  0.000 |  0.100 |  0.67 |    1.67 |   0.00 |   1.00 |    0.67 |   10.00 |   10.00
```

Tune the scenario, speed, and network conditions with flags (all optional):

```bash
dotnet run --project src/DeadCellsSync.Demo -- \
  --scenario reversal --velocity 10 \
  --latency 3 --jitter 2 --loss 0.2 \
  --snapshot-every 3 --interpolation-delay-ms 100 --seconds 1
```

## Testing approach

`ReconciliationUnderNetworkConditionsTests` is the centerpiece: it drives a full
client-predicts / authoritative / client-reconciles loop through `SimulatedNetworkChannel<T>` with
real injected latency and packet loss, then asserts the client's predicted position converges back to
the authoritative position once the network drains. `SnapshotInterpolationBufferTests` cover
deterministic interpolation (position, velocity, health, cooldown), out-of-order and duplicate
snapshots, unstable intervals, and buffer-underrun holding. All tests are reproducible — seeded RNG,
no wall-clock waits, no sockets.

## Dead Cells MultiplayerX integration

DeadCellsSync is designed to be fed by DeadCellsMultiplayerX, which will supply the real transport:

```
Network (DeadCellsMultiplayerX)
      ↓
Received Snapshot
      ↓
DeadCellsSync (buffer → interpolation → render)
```

The state primitives (`PlayerState`, `PlayerInput`) are intentionally small and portable so they can
grow into a `HeroState` (position, velocity, direction, grounded, animation state, weapon state,
attack state) without changing the synchronization architecture. That migration is future work — this
repo intentionally stops at a clean, tested sync core.

## License

MIT — see [LICENSE](LICENSE).
