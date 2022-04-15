namespace AlterNats;

public sealed class NatsConnectionPool : IAsyncDisposable
{
    readonly NatsConnection[] connections;
    int index = -1;

    public NatsConnectionPool()
        : this(Environment.ProcessorCount / 2, NatsOptions.Default)
    {
    }

    public NatsConnectionPool(int poolSize)
        : this(poolSize, NatsOptions.Default)
    {
    }

    public NatsConnectionPool(NatsOptions options)
        : this(Environment.ProcessorCount / 2, options)
    {

    }

    public NatsConnectionPool(int poolSize, NatsOptions options)
    {
        poolSize = Math.Max(1, poolSize);
        connections = new NatsConnection[poolSize];
        for (int i = 0; i < connections.Length; i++)
        {
            connections[i] = new NatsConnection(options);
        }
    }

    public NatsConnection GetConnection()
    {
        var i = Interlocked.Increment(ref index);
        return connections[i % connections.Length];
    }

    public INatsCommand GetCommand()
    {
        return GetConnection();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var item in connections)
        {
            await item.DisposeAsync().ConfigureAwait(false);
        }
    }
}
