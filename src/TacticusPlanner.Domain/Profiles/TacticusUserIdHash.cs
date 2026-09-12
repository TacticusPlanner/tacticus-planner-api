using TacticusPlanner.Domain.Common;

namespace TacticusPlanner.Domain.Profiles;

/// <summary>
/// 32-byte keyed HMAC hash of an upstream Tacticus user id, produced by the column-hashing service.
/// Compares and hashes by content rather than by <see cref="byte"/>[] reference identity, so it can be
/// used directly as a dictionary key or in LINQ predicates without a manual hex-string workaround.
/// </summary>
public readonly struct TacticusUserIdHash : IEquatable<TacticusUserIdHash>
{
    public const int Length = 32;

    private readonly byte[] value;

    private TacticusUserIdHash(byte[] value) => this.value = value;

    public static TacticusUserIdHash From(byte[] value)
    {
        if (value.Length != Length)
        {
            throw new ArgumentException($"A {nameof(TacticusUserIdHash)} must be {Length} bytes.", nameof(value));
        }

        return new TacticusUserIdHash(value);
    }

    public static TacticusUserIdHash? FromNullable(byte[]? value) => value is null ? null : From(value);

    public byte[] Value => value;

    public bool Equals(TacticusUserIdHash other) => KeyedHashEquality.Equals(value, other.value);

    public override bool Equals(object? obj) => obj is TacticusUserIdHash other && Equals(other);

    public override int GetHashCode() => KeyedHashEquality.GetHashCode(value);

    public override string ToString() => Convert.ToHexString(value);

    public static bool operator ==(TacticusUserIdHash left, TacticusUserIdHash right) => left.Equals(right);

    public static bool operator !=(TacticusUserIdHash left, TacticusUserIdHash right) => !left.Equals(right);
}
