# DeadCellsSync 移动插值教学 Demo

这个 Demo 只做一件事：把「一个远程玩家从服务器 Snapshot 到客户端最终显示位置」的每一步拆开，让你看清中间到底发生了什么。

它**不使用任何真实网络**。用 `SimulatedNetworkChannel` 模拟延迟 / 抖动 / 丢包，用 `SnapshotInterpolationBuffer` 做插值，最后打印一张时间轴表格。

运行方式（在仓库根目录）：

```bash
# 默认：匀速向右，10 单位/秒
dotnet run --project src/DeadCellsSync.Demo

# 反向运动：0,1,2,3,2,1,0,-1（右 → 停 → 左）
dotnet run --project src/DeadCellsSync.Demo -- --scenario reversal

# 不同速度
dotnet run --project src/DeadCellsSync.Demo -- --velocity 1
dotnet run --project src/DeadCellsSync.Demo -- --velocity 5
dotnet run --project src/DeadCellsSync.Demo -- --velocity 10

# 网络条件：延迟 3 tick + 抖动 2 tick + 20% 丢包
dotnet run --project src/DeadCellsSync.Demo -- --latency 3 --jitter 2 --loss 0.2
```

所有参数（可选）：

| flag | 默认 | 含义 |
|------|------|------|
| `--scenario` | `constant` | `constant`（向右）或 `reversal`（右→左） |
| `--velocity` | 10 | 速度（单位/秒） |
| `--latency` | 0 | 固定延迟（tick，1 tick = 1/30 秒） |
| `--jitter` | 0 | 额外随机延迟（tick） |
| `--loss` | 0 | 丢包概率 |
| `--snapshot-every` | 3 | 每 N tick 产生一个 Snapshot（3 → 10 Hz） |
| `--interpolation-delay-ms` | 100 | 插值延迟（渲染落后服务器多久） |
| `--seconds` | 0.8 | 运行时长 |
| `--seed` | 1234 | 随机种子 |

---

## 1. 什么是 Snapshot

服务器不会每帧都发一次完整状态。它按固定频率（这里 10 Hz，每 3 tick 一次）把远程玩家此刻的状态打包成一个 **Snapshot**。

在 DeadCellsSync 里，Snapshot 就是 `WorldSnapshot`：

```csharp
public readonly struct WorldSnapshot
{
    public readonly uint ServerTick;   // 这个 Snapshot 对应服务器第几个 tick
    public readonly PlayerState PlayerA;
    public readonly PlayerState PlayerB;
}
```

`PlayerState` 里是一个玩家可以被模拟的全部状态：

```csharp
public readonly struct PlayerState
{
    public readonly Vector2F Position;     // 位置
    public readonly Vector2F Velocity;     // 速度
    public readonly float Health;          // 血量
    public readonly float FireCooldownRemaining; // 武器冷却
    public readonly uint LastProcessedInputSequence;
}
```

关键点：**Snapshot 是离散的**。服务器在 tick 0、3、6、9… 各发一个 Snapshot，中间那几帧（tick 1、2、4、5…）没有数据。

## 2. 为什么不能直接显示最新 Snapshot

如果客户端直接写：

```
RenderPosition = LatestSnapshot.Position
```

那远程玩家就会**跳着动**：位置在 0、1、2、3 之间瞬移，中间没有任何过渡。因为 Snapshot 是 10 Hz 的，但画面是 60 FPS，两次 Snapshot 之间有 6 帧是空的。

```
Snapshot:     0            1            2            3
Render(直接): 0 0 0 0 0 0 1 1 1 1 1 1 2 2 2 2 2 2 3 ...
              ↑ 卡住 6 帧    ↑ 再跳一下
```

所以不能「显示最新」，而要「在最近两个之间插值」。

## 3. 什么是 Snapshot Buffer

`SnapshotInterpolationBuffer` 是一个环形缓冲区，保存最近收到的若干 Snapshot（按 tick 排序）。它不急着立刻显示，而是把 Snapshot 存起来，等需要时取「夹住当前渲染时刻」的两个。

```csharp
var buffer = new SnapshotInterpolationBuffer(interpolationDelaySeconds: 0.1f);
buffer.AddSnapshot(snapshotTick, playerState);   // 收到一个就 push 一个
```

它的内部是 `List<(uint tick, PlayerState state)>`，按 tick 递增排列。乱序到达的 Snapshot 会被插到正确位置，重复的会被忽略。

## 4. 什么是 Render Time

**Render Time（渲染时刻）** 是客户端这一帧想要渲染的「游戏世界时刻」。

```
renderTime = localTime - interpolationDelay
```

`localTime` 是客户端自己的时钟（现在）；`renderTime` 是故意往回拨之后的时刻。客户端渲染的不是「现在」，而是「过去的某个时刻」。

## 5. 为什么 Render Time 要落后服务器时间

这是整件事最核心的一步。

假设客户端永远渲染「最新」的 Snapshot：

```
A 到了 → 渲染 A
       ↓ 等下一个 Snapshot…
B 到了 → 渲染 B
```

在「等 B」的那段时间里，Buffer 里只有一个 Snapshot（A），没法插值，只能干等，结果还是卡顿。

所以让 Render Time **故意落后**一段 `interpolationDelay`：

```
Server Time = 2.000
Interpolation Delay = 100ms
Render Time  = 1.900
```

这样 Buffer 里通常同时存在「早于 renderTime 的」和「晚于 renderTime 的」两个 Snapshot：

```
Snapshot A  ←  renderTime  →  Snapshot B
```

于是每一帧都能稳定地「在 A 和 B 之间插值」。

> 一句话：**用一点点延迟，换稳定平滑。** 延迟只要不超过某个阈值，玩家感觉不到；但如果网络抖动、丢包，这个缓冲就是你的保护垫。

## 6. 什么是 Previous Snapshot

**Previous Snapshot** 是「tick ≤ renderTime」中最大的那个 Snapshot —— 渲染时刻**左边**最近的那个。

```
…  A(tick 3)   renderTime(4.5)   B(tick 6)  …
             ↑ previous
```

## 7. 什么是 Next Snapshot

**Next Snapshot** 是「tick ≥ renderTime」中最小的那个 Snapshot —— 渲染时刻**右边**最近的那个。

```
…  A(tick 3)   renderTime(4.5)   B(tick 6)  …
                                ↑ next
```

插值就是在这两个之间做 `Lerp`。Demo 里新增了一个只读方法 `GetInterpolationSample`，专门把这两个 Snapshot 和 alpha 暴露出来：

```csharp
var info = buffer.GetInterpolationSample(localTime);
// info.PreviousTick / info.NextTick / info.PreviousState / info.NextState / info.Alpha
```

## 8. Alpha 怎么计算

Alpha 表示「renderTime 在 previous 和 next 之间的百分比位置」：

```
alpha = (renderTime - previousTime) / (nextTime - previousTime)
```

例如：

```
previous: time = 1.0
next:     time = 1.1
renderTime = 1.05

alpha = (1.05 - 1.0) / (1.1 - 1.0) = 0.5
```

alpha 永远是 `[0, 1]` 之间的数：`0` 表示正好在 previous 上，`1` 表示正好在 next 上。

## 9. Position 怎么插值

拿到 alpha 之后就是标准线性插值（Lerp）：

```
renderPosition = Lerp(previous.Position, next.Position, alpha)
              = previous.Position + (next.Position - previous.Position) * alpha
```

完整例子（也就是 Demo 里会实际打印出来的那个「worked example」）：

```
previous snapshot: time = 0.000  position = 0.00
next snapshot    : time = 0.100  position = 1.00
render time      : 0.050

alpha = (0.050 - 0.000) / (0.100 - 0.000) = 0.50
renderPosition = Lerp(0.00, 1.00, 0.50) = 0.50
```

于是服务器给的是离散的 `0, 1, 2, 3`，客户端渲染出来的是连续的 `0.00, 0.17, 0.33, 0.50, 0.67, 0.83, 1.00…`。

> DeadCellsSync 的插值同时做了 **Position 和 Velocity** 的 Lerp（`SnapshotInterpolationBuffer.Interpolate`），所以 Demo 里的 `renderV` 也会跟着平滑。当前 `PlayerState` 没有 Rotation 字段，所以这个 Demo 只演示 Position / Velocity 插值。

## 10. 网络延迟、抖动、丢包对插值有什么影响

先说清楚：**插值不能消除延迟，也不会让数据变得更多。** 它只是把「已经收到的离散状态」在时间轴上平滑地摊开。

- **延迟（latency）**：Snapshot 到得晚，Buffer 里可用的 Snapshot 整体偏旧。渲染会落后服务器更多，但依然平滑。
- **抖动（jitter）**：Snapshot 到的时间忽快忽慢。只要抖动不超过 `interpolationDelay`，Render Time 就始终能夹在两个 Snapshot 之间；抖动太大导致 Render Time 跑到「最新 Snapshot 之后」时，Buffer 会**停在最后一个位置**（不瞎猜、不穿墙）。
- **丢包（packet loss）**：少一个 Snapshot 只是插值区间变长了一点（从「tick 3 → tick 6」变成「tick 3 → tick 9」），插值仍然连续。

跑一下对比就能看到差别：

```bash
# 干净：alpha 稳定地从 0 走到 1
dotnet run --project src/DeadCellsSync.Demo

# 有丢包/延迟：能看到 Buffer 偶尔「hold」（prev == next，alpha = 0）
dotnet run --project src/DeadCellsSync.Demo -- --latency 3 --jitter 2 --loss 0.2
```

## 11. DeadCellsMultiplayerX 将来如何接入

现在 `DeadCellsSync` 只负责「Snapshot 到达之后」的部分。将来 DeadCellsMultiplayerX 提供真实网络，把收到的 Snapshot 交给它：

```
Network (DeadCellsMultiplayerX)
        ↓
Receive Snapshot
        ↓
DeadCellsSync.PushSnapshot(snapshot)        // buffer.AddSnapshot
        ↓
DeadCellsSync.Update(renderTime)            // buffer.Sample(renderTime)
        ↓
interpolatedState
        ↓
RemoteHero
        ↓
Hero.pos
```

`DeadCellsSync` 需要的三个动作，对应真实 API：

1. **PushSnapshot** → `buffer.AddSnapshot(tick, state)` —— 每收到一个网络 Snapshot 调一次。
2. **Update(renderTime)** → `buffer.Sample(renderTime)` —— 每帧调一次，传入这一帧的渲染时刻。
3. **GetInterpolatedState** → `Sample` 的返回值 —— 就是插值后的 `PlayerState`，取 `.Position` 放到 RemoteHero 上。

---

## 最小使用示例

下面是最小、可运行、且**使用真实存在的 API** 的示例（不是伪代码，是 DeadCellsSync 现在的接口）：

```csharp
using DeadCellsSync.Core;
using DeadCellsSync.Core.Snapshot;

// 1. 每个远程玩家一个 buffer。delay 建议 >= 一个 Snapshot 间隔。
var buffer = new SnapshotInterpolationBuffer(interpolationDelaySeconds: 0.1f);

// 2. 网络收到一个 Snapshot 时（tick 是服务器 tick，state 是那个玩家的 PlayerState）
buffer.AddSnapshot(snapshotTick, remotePlayerState);

// 3. 每帧渲染时，传入当前渲染时刻（秒），拿到插值后的状态
var renderTime = currentTimeSeconds;
var state = buffer.Sample(renderTime);

// 4. 把插值结果摆到画面上
remoteHero.Position = state.Position;   // 平滑的连续位置
remoteHero.Velocity = state.Velocity;

// （可选）想教学/调试，看这一帧到底用了哪两个 Snapshot、alpha 是多少：
var info = buffer.GetInterpolationSample(renderTime);
// info.PreviousTick, info.NextTick, info.PreviousState, info.NextState, info.Alpha
```

对应到 DeadCellsMultiplayerX 里，就是：

- **从哪里拿 Snapshot**：你将来在真实网络层收到 `WorldSnapshot`（或未来的 `HeroState`）的地方。
- **什么时候 PushSnapshot**：网络回调 / 收包循环里，每收到一个就 `AddSnapshot`。
- **什么时候取 Interpolated State**：渲染循环（每帧）里，`Sample(renderTime)`，把结果贴到 RemoteHero。
