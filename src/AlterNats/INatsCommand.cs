using AlterNats.Commands;

namespace AlterNats;

// ***Async or Post***Async(fire-and-forget)
public interface INatsCommand
{
    IObservable<T> AsObservable<T>(in NatsKey key);
    IObservable<T> AsObservable<T>(string key);
    ValueTask FlushAsync();
    ValueTask<TimeSpan> PingAsync();
    void PostDirectWrite(byte[] protocol);
    void PostDirectWrite(DirectWriteCommand command);
    void PostDirectWrite(string protocol, int repeatCount = 1);
    void PostPing();
    void PostPublish(in NatsKey key);
    void PostPublish(in NatsKey key, byte[] value);
    void PostPublish(in NatsKey key, ReadOnlyMemory<byte> value);
    void PostPublish(string key);
    void PostPublish(string key, byte[] value);
    void PostPublish(string key, ReadOnlyMemory<byte> value);
    void PostPublish<T>(in NatsKey key, T value);
    void PostPublish<T>(string key, T value);
    void PostPublishBatch<T>(IEnumerable<(NatsKey, T?)> values);
    void PostPublishBatch<T>(IEnumerable<(string, T?)> values);
    ValueTask PublishAsync(in NatsKey key);
    ValueTask PublishAsync(in NatsKey key, byte[] value);
    ValueTask PublishAsync(in NatsKey key, ReadOnlyMemory<byte> value);
    ValueTask PublishAsync(string key);
    ValueTask PublishAsync(string key, byte[] value);
    ValueTask PublishAsync(string key, ReadOnlyMemory<byte> value);
    ValueTask PublishAsync<T>(in NatsKey key, T value);
    ValueTask PublishAsync<T>(string key, T value);
    ValueTask PublishBatchAsync<T>(IEnumerable<(NatsKey, T?)> values);
    ValueTask PublishBatchAsync<T>(IEnumerable<(string, T?)> values);
    ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Action<NatsKey,T> handler);
    ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Func<NatsKey,T, Task> asyncHandler);
    ValueTask<IDisposable> QueueSubscribeAsync<T>(string key, string queueGroup, Action<NatsKey,T> handler);
    ValueTask<IDisposable> QueueSubscribeAsync<T>(string key, string queueGroup, Func<NatsKey,T, Task> asyncHandler);
    ValueTask<TResponse?> RequestAsync<TRequest, TResponse>(NatsKey key, TRequest request, CancellationToken cancellationToken = default);
    ValueTask<TResponse?> RequestAsync<TRequest, TResponse>(string key, TRequest request, CancellationToken cancellationToken = default);
    
    ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Action<T> handler);
    ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Func<T, Task> asyncHandler);
    ValueTask<IDisposable> QueueSubscribeAsync<T>(string key, string queueGroup, Action<T> handler);
    ValueTask<IDisposable> QueueSubscribeAsync<T>(string key, string queueGroup, Func<T, Task> asyncHandler);
    
    ValueTask<IDisposable> SubscribeAsync(in NatsKey key, Action<NatsKey> handler);
    ValueTask<IDisposable> SubscribeAsync(string key, Action<NatsKey> handler);
    ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Action<NatsKey,T> handler);
    ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Func<NatsKey,T, Task> asyncHandler);
    ValueTask<IDisposable> SubscribeAsync<T>(string key, Action<NatsKey,T> handler);
    ValueTask<IDisposable> SubscribeAsync<T>(string key, Func<NatsKey,T, Task> asyncHandler);
    
    ValueTask<IDisposable> SubscribeAsync(in NatsKey key, Action handler);
    ValueTask<IDisposable> SubscribeAsync(string key, Action handler);
    ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Action<T> handler);
    ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Func<T, Task> asyncHandler);
    ValueTask<IDisposable> SubscribeAsync<T>(string key, Action<T> handler);
    ValueTask<IDisposable> SubscribeAsync<T>(string key, Func<T, Task> asyncHandler);
    ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, Task<TResponse>> requestHandler);
    ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, TResponse> requestHandler);
    ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, Task<TResponse>> requestHandler);
    ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, TResponse> requestHandler);
}
