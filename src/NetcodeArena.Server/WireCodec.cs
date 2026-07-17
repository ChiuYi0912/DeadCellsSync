using System.IO;
using NetcodeArena.Core;
using NetcodeArena.Core.Networking;

namespace NetcodeArena.Server;

/// <summary>
/// Deliberately tiny, deliberately unversioned binary wire format for the headless server's
/// UDP socket. This is intentionally the ONLY place in the whole solution that does byte-level
/// I/O - <see cref="NetcodeArena.Core"/> stays pure C# with zero knowledge of sockets, which is
/// what keeps <c>NetcodeArena.Core.Tests</c> runnable with no network. A real production codec
/// would add a version byte and length-prefixing for forward compatibility; kept out here to
/// keep the demo readable (see README stretch goals).
/// </summary>
public static class WireCodec
{
    public const byte InputPacketType = 1;
    public const byte SnapshotPacketType = 2;

    public static byte[] EncodeInput(PlayerInput input)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(InputPacketType);
        writer.Write(input.Sequence);
        writer.Write(input.ClientTick);
        writer.Write(input.Move.X);
        writer.Write(input.Move.Y);
        writer.Write(input.AimDirection.X);
        writer.Write(input.AimDirection.Y);
        writer.Write(input.Fire);
        return stream.ToArray();
    }

    public static PlayerInput DecodeInput(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadByte(); // packet type, already dispatched on by the caller
        var sequence = reader.ReadUInt32();
        var clientTick = reader.ReadUInt32();
        var move = new Vector2F(reader.ReadSingle(), reader.ReadSingle());
        var aim = new Vector2F(reader.ReadSingle(), reader.ReadSingle());
        var fire = reader.ReadBoolean();
        return new PlayerInput(sequence, clientTick, move, aim, fire);
    }

    public static byte[] EncodeSnapshot(WorldSnapshot snapshot)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(SnapshotPacketType);
        writer.Write(snapshot.ServerTick);
        WritePlayerState(writer, snapshot.PlayerA);
        WritePlayerState(writer, snapshot.PlayerB);
        return stream.ToArray();
    }

    public static WorldSnapshot DecodeSnapshot(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadByte();
        var serverTick = reader.ReadUInt32();
        var a = ReadPlayerState(reader);
        var b = ReadPlayerState(reader);
        return new WorldSnapshot(serverTick, a, b);
    }

    private static void WritePlayerState(BinaryWriter writer, PlayerState state)
    {
        writer.Write(state.Position.X);
        writer.Write(state.Position.Y);
        writer.Write(state.Velocity.X);
        writer.Write(state.Velocity.Y);
        writer.Write(state.Health);
        writer.Write(state.FireCooldownRemaining);
        writer.Write(state.LastProcessedInputSequence);
    }

    private static PlayerState ReadPlayerState(BinaryReader reader)
    {
        var position = new Vector2F(reader.ReadSingle(), reader.ReadSingle());
        var velocity = new Vector2F(reader.ReadSingle(), reader.ReadSingle());
        var health = reader.ReadSingle();
        var cooldown = reader.ReadSingle();
        var lastSequence = reader.ReadUInt32();
        return new PlayerState(position, velocity, health, cooldown, lastSequence);
    }
}
