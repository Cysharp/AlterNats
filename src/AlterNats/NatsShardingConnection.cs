namespace AlterNats;

public sealed class NatsShardingConnection
{
    readonly NatsConnectionPool[] pools;

    public NatsShardingConnection(int poolSize, NatsOptions options, string[] urls)
    {
        poolSize = Math.Max(1, poolSize);
        pools = new NatsConnectionPool[urls.Length];
        for (int i = 0; i < poolSize; i++)
        {
            pools[i] = new NatsConnectionPool(poolSize, options with { Url = urls[i] });
        }
    }

    public ShadringNatsCommand GetCommand(in NatsKey key)
    {
        var hash = key.GetHashCode();
        var pool = pools[hash % pools.Length];
        return new ShadringNatsCommand(pool.GetConnection(), key);
    }

    public ShadringNatsCommand GetCommand(string key)
    {
        var hash = key.GetHashCode();
        var pool = pools[hash % pools.Length];
        return new ShadringNatsCommand(pool.GetConnection(), new NatsKey(key, true));
    }
}

public readonly struct ShadringNatsCommand
{
    readonly NatsConnection connection;
    readonly NatsKey key;

    public ShadringNatsCommand(NatsConnection connection, NatsKey key)
    {
        this.connection = connection;
        this.key = key;
    }

    public IObservable<T> AsObservable<T>() => connection.AsObservable<T>(key);
    public ValueTask FlushAsync() => connection.FlushAsync();
    public ValueTask<TimeSpan> PingAsync() => connection.PingAsync();
    public void PostPing() => connection.PostPing();
    public void PostPublish() => connection.PostPublish(key);
    public void PostPublish(byte[] value) => connection.PostPublish(key, value);
    public void PostPublish(ReadOnlyMemory<byte> value) => connection.PostPublish(key, value);
    public void PostPublish<T>(T value) => connection.PostPublish(key, value);
    public ValueTask PublishAsync() => connection.PublishAsync(key);
    public ValueTask PublishAsync(byte[] value) => connection.PublishAsync(key, value);
    public ValueTask PublishAsync(ReadOnlyMemory<byte> value) => connection.PublishAsync(key, value);
    public ValueTask PublishAsync<T>(T value) => connection.PublishAsync(key, value);
    public ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey queueGroup, Action<T> handler) => connection.QueueSubscribeAsync(key, queueGroup, handler);
    public ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey queueGroup, Func<T, Task> asyncHandler) => connection.QueueSubscribeAsync(key, queueGroup, asyncHandler);
    public ValueTask<IDisposable> QueueSubscribeAsync<T>(string queueGroup, Action<T> handler) => connection.QueueSubscribeAsync(key.Key, queueGroup, handler);
    public ValueTask<IDisposable> QueueSubscribeAsync<T>(string queueGroup, Func<T, Task> asyncHandler) => connection.QueueSubscribeAsync(key.Key, queueGroup, asyncHandler);
    public ValueTask<TResponse?> RequestAsync<TRequest, TResponse>(TRequest request) => connection.RequestAsync<TRequest, TResponse>(key, request);
    public ValueTask<IDisposable> SubscribeAsync(Action handler) => connection.SubscribeAsync(key, handler);
    public ValueTask<IDisposable> SubscribeAsync<T>(Action<T> handler) => connection.SubscribeAsync<T>(key, handler);
    public ValueTask<IDisposable> SubscribeAsync<T>(Func<T, Task> asyncHandler) => connection.SubscribeAsync<T>(key, asyncHandler);
    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(Func<TRequest, Task<TResponse>> requestHandler) => connection.SubscribeRequestAsync<TRequest, TResponse>(key, requestHandler);
    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(Func<TRequest, TResponse> requestHandler) => connection.SubscribeRequestAsync<TRequest, TResponse>(key, requestHandler);
}
