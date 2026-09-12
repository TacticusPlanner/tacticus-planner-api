namespace TacticusPlanner.Domain.Common;

/// <summary>
/// Content equality/hashing for fixed-length keyed-hash byte arrays (see <see cref="Profiles.TacticusUserIdHash"/>
/// and <see cref="Guilds.TacticusGuildIdHash"/>) — <see cref="byte"/>[] itself compares by reference, which is
/// wrong for values that represent the same hash produced twice.
/// </summary>
internal static class KeyedHashEquality
{
    public static bool Equals(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    public static int GetHashCode(byte[]? value)
    {
        if (value is null)
        {
            return 0;
        }

        var hash = new HashCode();
        hash.AddBytes(value);
        return hash.ToHashCode();
    }
}
