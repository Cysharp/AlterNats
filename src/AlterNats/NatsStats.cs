namespace AlterNats;

public readonly record struct NatsStats
(
    long SentBytes,
    long ReceivedBytes,
    long PendingMessages,
    long SentMessages,
    long ReceivedMessages,
    long SubscriptionCount
);

internal sealed class ConnectionStatsCounter
{
    public long SentBytes; // TODO:write this
    public long SentMessages; // TODO:write this
    public long PendingMessages; // TODO:decrement after reader.TryRead
    public long ReceivedBytes;
    public long ReceivedMessages;
    public long SubscriptionCount;

    public void Increment(ref long field)
    {
        Interlocked.Increment(ref field);
    }

    public void Decrement(ref long field)
    {
        Interlocked.Decrement(ref field);
    }

    public void Add(ref long field, long value)
    {
        Interlocked.Add(ref field, value);
    }

    public NatsStats ToStats()
    {
        return new NatsStats(SentBytes, ReceivedBytes, PendingMessages, SentMessages, ReceivedMessages, SubscriptionCount);
    }
}
