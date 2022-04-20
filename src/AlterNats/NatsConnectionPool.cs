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
            var name = (options.ConnectOptions.Name == null) ? $"#{i}" : $"{options.ConnectOptions.Name}#{i}";
            connections[i] = new NatsConnection(options with { ConnectOptions = options.ConnectOptions with { Name = name } });
        }
    }

    public NatsConnection GetConnection()
    {
        var i = Interlocked.Increment(ref index);
        return connections[i % connections.Length];
    }

    public IEnumerable<NatsConnection> GetConnections()
    {
        foreach (var item in connections)
        {
            yield return item;
        }
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
