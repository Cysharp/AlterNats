using System.Text;

namespace AlterNats;

/// <summary>
/// Represents Subject/QueueGroup of NATS
/// </summary>
public sealed class NatsKey : IEquatable<NatsKey>
{
    public readonly string Key;

    readonly int hashCode;
    internal readonly byte[] buffer; // subject with space padding.

    public NatsKey(string key)
    {
        this.Key = key;
        this.hashCode = key.GetHashCode();
        this.buffer = Encoding.ASCII.GetBytes(key + " ");
    }

    public override int GetHashCode()
    {
        return hashCode;
    }

    public bool Equals(NatsKey? other)
    {
        if (other == null) return false;
        return buffer.AsSpan().SequenceEqual(other.buffer);
    }
}