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

Run the local synchronization demo (no real network):

```bash
dotnet run --project src/DeadCellsSync.Demo
```

The demo drives a scripted "remote hero" through the full pipeline — authoritative simulation →
snapshot → simulated latency/jitter/loss → interpolation buffer → rendered position — and prints a
table comparing the authoritative position against the interpolated render position so smoothing is
directly observable:

```
 tick | serverA.X | renderA.X | |delta|
------+-----------+-----------+--------
  6.0 |    -2.844 |    -3.701 |  0.858
  6.5 |    -2.844 |    -3.602 |  0.759
  7.0 |    -2.667 |    -3.505 |  0.837
```

Tune the conditions with flags (all optional):

```bash
dotnet run --project src/DeadCellsSync.Demo -- \
  --latency 3 --jitter 2 --loss 0.2 --snapshot-hz 20 \
  --interpolation-delay-ms 150 --seconds 5 --seed 42
```

| flag | default | meaning |
|------|---------|---------|
| `--latency` | 2 | fixed latency, in simulation ticks (30 ticks/sec) |
| `--jitter` | 1 | additional random latency, in ticks |
| `--loss` | 0.1 | packet loss probability |
| `--snapshot-hz` | 20 | target snapshot broadcast rate |
| `--interpolation-delay-ms` | 150 | render delay applied by the interpolation buffer |
| `--seconds` | 5 | demo duration |
| `--seed` | 1234 | RNG seed (deterministic runs) |

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
