using DeadCellsSync.Core;
using DeadCellsSync.Core.Tests.Assertions;
using Xunit;

namespace DeadCellsSync.Core.Tests;

public class WeaponSystemTests
{
    private static PlayerState At(Vector2F position) => PlayerState.Spawn(position);

    [Fact]
    public void TryFire_TargetInRangeAndAligned_ProducesHit()
    {
        var shooter = At(new Vector2F(0, 0));
        var target = At(new Vector2F(5, 0));
        var input = new PlayerInput(1, 1, Vector2F.Zero, new Vector2F(1, 0), fire: true);

        var (updatedShooter, hit) = WeaponSystem.TryFire(
            PlayerId.PlayerA, shooter, PlayerId.PlayerB, target, input, tick: 1);

        Assert.NotNull(hit);
        Assert.Equal(SimulationConfig.WeaponDamage, hit!.Value.Damage);
        Approx.Equal(SimulationConfig.WeaponCooldownSeconds, updatedShooter.FireCooldownRemaining, 0.0001f);
    }

    [Fact]
    public void TryFire_TargetOutsideAimCone_Misses()
    {
        var shooter = At(new Vector2F(0, 0));
        var target = At(new Vector2F(0, 5)); // directly above
        var input = new PlayerInput(1, 1, Vector2F.Zero, new Vector2F(1, 0), fire: true); // aiming right

        var (_, hit) = WeaponSystem.TryFire(
            PlayerId.PlayerA, shooter, PlayerId.PlayerB, target, input, tick: 1);

        Assert.Null(hit);
    }

    [Fact]
    public void TryFire_TargetOutOfRange_Misses()
    {
        var shooter = At(new Vector2F(0, 0));
        var target = At(new Vector2F(SimulationConfig.WeaponRange + 5, 0));
        var input = new PlayerInput(1, 1, Vector2F.Zero, new Vector2F(1, 0), fire: true);

        var (_, hit) = WeaponSystem.TryFire(
            PlayerId.PlayerA, shooter, PlayerId.PlayerB, target, input, tick: 1);

        Assert.Null(hit);
    }

    [Fact]
    public void TryFire_WhileOnCooldown_DoesNotFireAgain()
    {
        var shooter = new PlayerState(new Vector2F(0, 0), Vector2F.Zero, 100f, 0.2f, 0);
        var target = At(new Vector2F(3, 0));
        var input = new PlayerInput(1, 1, Vector2F.Zero, new Vector2F(1, 0), fire: true);

        var (updatedShooter, hit) = WeaponSystem.TryFire(
            PlayerId.PlayerA, shooter, PlayerId.PlayerB, target, input, tick: 1);

        Assert.Null(hit);
        Approx.Equal(0.2f, updatedShooter.FireCooldownRemaining, 0.0001f); // unchanged, still on cooldown
    }

    [Fact]
    public void TryFire_WithoutFireInput_NeverProducesHitOrResetsCooldown()
    {
        var shooter = At(new Vector2F(0, 0));
        var target = At(new Vector2F(3, 0));
        var input = new PlayerInput(1, 1, Vector2F.Zero, new Vector2F(1, 0), fire: false);

        var (updatedShooter, hit) = WeaponSystem.TryFire(
            PlayerId.PlayerA, shooter, PlayerId.PlayerB, target, input, tick: 1);

        Assert.Null(hit);
        Approx.Equal(0f, updatedShooter.FireCooldownRemaining, 0.0001f);
    }

    [Fact]
    public void TryFire_AgainstDeadTarget_NeverProducesHit()
    {
        var shooter = At(new Vector2F(0, 0));
        var deadTarget = new PlayerState(new Vector2F(3, 0), Vector2F.Zero, 0f, 0f, 0);
        var input = new PlayerInput(1, 1, Vector2F.Zero, new Vector2F(1, 0), fire: true);

        var (_, hit) = WeaponSystem.TryFire(
            PlayerId.PlayerA, shooter, PlayerId.PlayerB, deadTarget, input, tick: 1);

        Assert.Null(hit);
    }

    [Fact]
    public void ApplyHit_ReducesHealthAndNeverGoesNegative()
    {
        var target = new PlayerState(Vector2F.Zero, Vector2F.Zero, 10f, 0f, 0);
        var hit = new HitEvent(PlayerId.PlayerA, PlayerId.PlayerB, SimulationConfig.WeaponDamage, 1);

        var result = WeaponSystem.ApplyHit(target, hit);

        Assert.Equal(0f, result.Health);
        Assert.False(result.IsAlive);
    }
}
