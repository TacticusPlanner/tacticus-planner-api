using TacticusPlanner.Domain.Common;

namespace TacticusPlanner.Domain.Guilds;

/// <summary>
/// 32-byte keyed HMAC hash of an upstream Tacticus guild id, produced by the same column-hashing
/// service as <see cref="Profiles.TacticusUserIdHash"/>. Compares and hashes by content rather than by
/// <see cref="byte"/>[] reference identity.
/// </summary>
public readonly struct TacticusGuildIdHash : IEquatable<TacticusGuildIdHash>
{
    public const int Length = 32;

    private readonly byte[] value;

    private TacticusGuildIdHash(byte[] value) => this.value = value;

    public static TacticusGuildIdHash From(byte[] value)
    {
        if (value.Length != Length)
        {
            throw new ArgumentException($"A {nameof(TacticusGuildIdHash)} must be {Length} bytes.", nameof(value));
        }

        return new TacticusGuildIdHash((byte[])value.Clone());
    }

    public static TacticusGuildIdHash? FromNullable(byte[]? value) => value is null ? null : From(value);

    public byte[] Value => (byte[])value.Clone();

    public bool Equals(TacticusGuildIdHash other) => KeyedHashEquality.Equals(value, other.value);

    public override bool Equals(object? obj) => obj is TacticusGuildIdHash other && Equals(other);

    public override int GetHashCode() => KeyedHashEquality.GetHashCode(value);

    public override string ToString() => Convert.ToHexString(value);

    public static bool operator ==(TacticusGuildIdHash left, TacticusGuildIdHash right) => left.Equals(right);

    public static bool operator !=(TacticusGuildIdHash left, TacticusGuildIdHash right) => !left.Equals(right);
}
