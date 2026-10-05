using System;

namespace DeadCellsSync.Core;

/// <summary>
/// Minimal deterministic 2D float vector. We avoid System.Numerics.Vector2 only to keep
/// the simulation trivially portable/deterministic across runtimes (no SIMD variance) and
/// to avoid a Unity-engine dependency in the pure .NET core.
/// </summary>
public readonly struct Vector2F : IEquatable<Vector2F>
{
    public readonly float X;
    public readonly float Y;

    public Vector2F(float x, float y)
    {
        X = x;
        Y = y;
    }

    public static readonly Vector2F Zero = new(0f, 0f);

    public float LengthSquared => X * X + Y * Y;
    public float Length => MathF.Sqrt(LengthSquared);

    public Vector2F Normalized()
    {
        var len = Length;
        return len < 1e-6f ? Zero : new Vector2F(X / len, Y / len);
    }

    public Vector2F ClampMagnitude(float maxLength)
    {
        var lenSq = LengthSquared;
        if (lenSq <= maxLength * maxLength || lenSq < 1e-12f)
        {
            return this;
        }

        var scale = maxLength / MathF.Sqrt(lenSq);
        return new Vector2F(X * scale, Y * scale);
    }

    public static Vector2F operator +(Vector2F a, Vector2F b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2F operator -(Vector2F a, Vector2F b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2F operator *(Vector2F a, float s) => new(a.X * s, a.Y * s);
    public static Vector2F operator *(float s, Vector2F a) => new(a.X * s, a.Y * s);

    public static float Distance(Vector2F a, Vector2F b) => (a - b).Length;

    public static Vector2F Lerp(Vector2F a, Vector2F b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Vector2F(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    public bool Equals(Vector2F other) => X.Equals(other.X) && Y.Equals(other.Y);
    public override bool Equals(object? obj) => obj is Vector2F other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X:F3}, {Y:F3})";
}
