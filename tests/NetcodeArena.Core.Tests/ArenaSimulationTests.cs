using System.Collections.Generic;
using NetcodeArena.Core;
using NetcodeArena.Core.Tests.Assertions;
using Xunit;

namespace NetcodeArena.Core.Tests;

public class ArenaSimulationTests
{
    [Fact]
    public void Step_IsDeterministic_SameInputsProduceSameOutput()
    {
        var state = ArenaSimulation.InitialState();
        var inputA = new PlayerInput(1, 1, new Vector2F(1, 0), new Vector2F(1, 0), false);
        var inputB = new PlayerInput(1, 1, new Vector2F(-1, 0), new Vector2F(-1, 0), false);

        var resultOne = ArenaSimulation.Step(state, inputA, inputB, 1);
        var resultTwo = ArenaSimulation.Step(state, inputA, inputB, 1);

        Assert.Equal(resultOne.A.Position, resultTwo.A.Position);
        Assert.Equal(resultOne.B.Position, resultTwo.B.Position);
    }

    [Fact]
    public void Step_BothPlayersMoveTowardEachOther_ClosesDistanceOverManyTicks()
    {
        var state = ArenaSimulation.InitialState();
        var initialDistance = Vector2F.Distance(state.A.Position, state.B.Position);

        for (uint tick = 1; tick <= 30; tick++)
        {
            var inputA = new PlayerInput(tick, tick, new Vector2F(1, 0), new Vector2F(1, 0), false);
            var inputB = new PlayerInput(tick, tick, new Vector2F(-1, 0), new Vector2F(-1, 0), false);
            state = ArenaSimulation.Step(state, inputA, inputB, tick);
        }

        var finalDistance = Vector2F.Distance(state.A.Position, state.B.Position);
        Assert.True(finalDistance < initialDistance);
    }

    [Fact]
    public void Step_WhenAFiresAndHits_BLosesHealthWithinSameTick()
    {
        var a = PlayerState.Spawn(new Vector2F(0, 0));
        var b = PlayerState.Spawn(new Vector2F(3, 0));
        var state = new ArenaSimulation.WorldState(a, b);

        var inputA = new PlayerInput(1, 1, Vector2F.Zero, new Vector2F(1, 0), fire: true);
        var inputB = PlayerInput.None(1, 1);

        var events = new List<HitEvent>();
        var result = ArenaSimulation.Step(state, inputA, inputB, tick: 1, hitEventsOut: events);

        Assert.Single(events);
        Approx.Equal(SimulationConfig.MaxHealth - SimulationConfig.WeaponDamage, result.B.Health, 0.001f);
    }

    [Fact]
    public void Step_BothPlayersFireSimultaneouslyAtLethalRange_BothTakeDamage()
    {
        var a = PlayerState.Spawn(new Vector2F(0, 0));
        var b = PlayerState.Spawn(new Vector2F(3, 0));
        var state = new ArenaSimulation.WorldState(a, b);

        var inputA = new PlayerInput(1, 1, Vector2F.Zero, new Vector2F(1, 0), fire: true);
        var inputB = new PlayerInput(1, 1, Vector2F.Zero, new Vector2F(-1, 0), fire: true);

        var events = new List<HitEvent>();
        var result = ArenaSimulation.Step(state, inputA, inputB, tick: 1, hitEventsOut: events);

        Assert.Equal(2, events.Count);
        Approx.Equal(SimulationConfig.MaxHealth - SimulationConfig.WeaponDamage, result.A.Health, 0.001f);
        Approx.Equal(SimulationConfig.MaxHealth - SimulationConfig.WeaponDamage, result.B.Health, 0.001f);
    }

    [Fact]
    public void Step_TracksLastProcessedInputSequencePerPlayer()
    {
        var state = ArenaSimulation.InitialState();
        var inputA = new PlayerInput(42, 1, Vector2F.Zero, new Vector2F(1, 0), false);
        var inputB = new PlayerInput(7, 1, Vector2F.Zero, new Vector2F(-1, 0), false);

        var result = ArenaSimulation.Step(state, inputA, inputB, 1);

        Assert.Equal(42u, result.A.LastProcessedInputSequence);
        Assert.Equal(7u, result.B.LastProcessedInputSequence);
    }
}
