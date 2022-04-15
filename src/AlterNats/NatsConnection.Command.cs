using AlterNats.Commands;
using AlterNats.Internal;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace AlterNats;

public partial class NatsConnection : INatsCommand
{
    public void PostPing()
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            commandWriter.TryWrite(PingCommand.Create(pool));
        }
        else
        {
            WithConnect(static self => self.commandWriter.TryWrite(PingCommand.Create(self.pool)));
        }
    }

    /// <summary>
    /// Send PING command and await PONG. Return value is similar as Round trip time.
    /// </summary>
    public ValueTask<TimeSpan> PingAsync()
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPingCommand.Create(this, pool);
            commandWriter.TryWrite(command);
            return command.AsValueTask();
        }
        else
        {
            return WithConnectAsync(static self =>
            {
                var command = AsyncPingCommand.Create(self, self.pool);
                self.commandWriter.TryWrite(command);
                return command.AsValueTask();
            });
        }
    }

    public ValueTask PublishAsync<T>(in NatsKey key, T value)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPublishCommand<T>.Create(pool, key, value, Options.Serializer);
            commandWriter.TryWrite(command);
            return command.AsValueTask();
        }
        else
        {
            return WithConnectAsync(key, value, static (self, k, v) =>
            {
                var command = AsyncPublishCommand<T>.Create(self.pool, k, v, self.Options.Serializer);
                self.commandWriter.TryWrite(command);
                return command.AsValueTask();
            });
        }
    }

    /// <summary>Publish empty message.</summary>
    public ValueTask PublishAsync(string key)
    {
        return PublishAsync(key, Array.Empty<byte>());
    }

    /// <summary>Publish empty message.</summary>
    public ValueTask PublishAsync(in NatsKey key)
    {
        return PublishAsync(key, Array.Empty<byte>());
    }

    public ValueTask PublishAsync<T>(string key, T value)
    {
        return PublishAsync<T>(new NatsKey(key, true), value);
    }

    public ValueTask PublishAsync(in NatsKey key, byte[] value)
    {
        return PublishAsync(key, new ReadOnlyMemory<byte>(value));
    }

    public ValueTask PublishAsync(string key, byte[] value)
    {
        return PublishAsync(new NatsKey(key, true), value);
    }

    public ValueTask PublishAsync(in NatsKey key, ReadOnlyMemory<byte> value)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPublishBytesCommand.Create(pool, key, value);
            commandWriter.TryWrite(command);
            return command.AsValueTask();
        }
        else
        {
            return WithConnectAsync(key, value, static (self, k, v) =>
            {
                var command = AsyncPublishBytesCommand.Create(self.pool, k, v);
                self.commandWriter.TryWrite(command);
                return command.AsValueTask();
            });
        }
    }

    public ValueTask PublishAsync(string key, ReadOnlyMemory<byte> value)
    {
        return PublishAsync(new NatsKey(key, true), value);
    }

    /// <summary>Publish empty message.</summary>
    public void PostPublish(in NatsKey key)
    {
        PostPublish(key, Array.Empty<byte>());
    }

    /// <summary>Publish empty message.</summary>
    public void PostPublish(string key)
    {
        PostPublish(key, Array.Empty<byte>());
    }

    public void PostPublish<T>(in NatsKey key, T value)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = PublishCommand<T>.Create(pool, key, value, Options.Serializer);
            commandWriter.TryWrite(command);
        }
        else
        {
            WithConnect(key, value, static (self, k, v) =>
            {
                var command = PublishCommand<T>.Create(self.pool, k, v, self.Options.Serializer);
                self.commandWriter.TryWrite(command);
            });
        }
    }

    public void PostPublish<T>(string key, T value)
    {
        PostPublish<T>(new NatsKey(key, true), value);
    }

    public void PostPublish(in NatsKey key, byte[] value)
    {
        PostPublish(key, new ReadOnlyMemory<byte>(value));
    }

    public void PostPublish(string key, byte[] value)
    {
        PostPublish(new NatsKey(key, true), value);
    }

    public void PostPublish(in NatsKey key, ReadOnlyMemory<byte> value)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = PublishBytesCommand.Create(pool, key, value);
            commandWriter.TryWrite(command);
        }
        else
        {
            WithConnect(key, value, static (self, k, v) =>
            {
                var command = PublishBytesCommand.Create(self.pool, k, v);
                self.commandWriter.TryWrite(command);
            });
        }
    }

    public void PostPublish(string key, ReadOnlyMemory<byte> value)
    {
        PostPublish(new NatsKey(key, true), value);
    }

    public ValueTask PublishBatchAsync<T>(IEnumerable<(NatsKey, T?)> values)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPublishBatchCommand<T>.Create(pool, values, Options.Serializer);
            commandWriter.TryWrite(command);
            return command.AsValueTask();
        }
        else
        {
            return WithConnectAsync(values, static (self, v) =>
            {
                var command = AsyncPublishBatchCommand<T>.Create(self.pool, v, self.Options.Serializer);
                self.commandWriter.TryWrite(command);
                return command.AsValueTask();
            });
        }
    }

    public ValueTask PublishBatchAsync<T>(IEnumerable<(string, T?)> values)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncPublishBatchCommand<T>.Create(pool, values, Options.Serializer);
            commandWriter.TryWrite(command);
            return command.AsValueTask();
        }
        else
        {
            return WithConnectAsync(values, static (self, values) =>
            {
                var command = AsyncPublishBatchCommand<T>.Create(self.pool, values, self.Options.Serializer);
                self.commandWriter.TryWrite(command);
                return command.AsValueTask();
            });
        }
    }

    public void PostPublishBatch<T>(IEnumerable<(NatsKey, T?)> values)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = PublishBatchCommand<T>.Create(pool, values, Options.Serializer);
            commandWriter.TryWrite(command);
        }
        else
        {
            WithConnect(values, static (self, v) =>
            {
                var command = PublishBatchCommand<T>.Create(self.pool, v, self.Options.Serializer);
                self.commandWriter.TryWrite(command);
            });
        }
    }

    public void PostPublishBatch<T>(IEnumerable<(string, T?)> values)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = PublishBatchCommand<T>.Create(pool, values, Options.Serializer);
            commandWriter.TryWrite(command);
        }
        else
        {
            WithConnect(values, static (self, v) =>
            {
                var command = PublishBatchCommand<T>.Create(self.pool, v, self.Options.Serializer);
                self.commandWriter.TryWrite(command);
            });
        }
    }

    public void PostDirectWrite(string protocol, int repeatCount = 1)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            commandWriter.TryWrite(new DirectWriteCommand(protocol, repeatCount));
        }
        else
        {
            WithConnect(protocol, repeatCount, static (self, protocol, repeatCount) =>
            {
                self.commandWriter.TryWrite(new DirectWriteCommand(protocol, repeatCount));
            });
        }
    }

    public void PostDirectWrite(byte[] protocol)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            commandWriter.TryWrite(new DirectWriteCommand(protocol));
        }
        else
        {
            WithConnect(protocol, static (self, protocol) =>
            {
                self.commandWriter.TryWrite(new DirectWriteCommand(protocol));
            });
        }
    }

    public void PostDirectWrite(DirectWriteCommand command)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            commandWriter.TryWrite(command);
        }
        else
        {
            WithConnect(command, static (self, command) =>
            {
                self.commandWriter.TryWrite(command);
            });
        }
    }

    public ValueTask<TResponse?> RequestAsync<TRequest, TResponse>(in NatsKey key, TRequest request)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return requestResponseManager.AddAsync<TRequest, TResponse>(key, indBoxPrefix, request);
        }
        else
        {
            return WithConnectAsync(key, request, static (self, key, request) =>
            {
                return self.requestResponseManager.AddAsync<TRequest, TResponse>(key, self.indBoxPrefix, request);
            });
        }
    }

    public ValueTask<TResponse?> RequestAsync<TRequest, TResponse>(string key, TRequest request)
    {
        return RequestAsync<TRequest, TResponse>(new NatsKey(key, true), request);
    }

    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, TResponse> requestHandler)
    {
        return SubscribeRequestAsync(key.Key, requestHandler);
    }

    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, TResponse> requestHandler)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return subscriptionManager.AddRequestHandlerAsync(key, requestHandler);
        }
        else
        {
            return WithConnectAsync(key, requestHandler, static (self, key, requestHandler) =>
            {
                return self.subscriptionManager.AddRequestHandlerAsync(key, requestHandler);
            });
        }
    }

    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, Task<TResponse>> requestHandler)
    {
        return SubscribeRequestAsync(key.Key, requestHandler);
    }

    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, Task<TResponse>> requestHandler)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return subscriptionManager.AddRequestHandlerAsync(key, requestHandler);
        }
        else
        {
            return WithConnectAsync(key, requestHandler, static (self, key, requestHandler) =>
            {
                return self.subscriptionManager.AddRequestHandlerAsync(key, requestHandler);
            });
        }
    }

    public ValueTask<IDisposable> SubscribeAsync(in NatsKey key, Action handler)
    {
        return SubscribeAsync<byte[]>(key, _ => handler());
    }

    public ValueTask<IDisposable> SubscribeAsync(string key, Action handler)
    {
        return SubscribeAsync<byte[]>(key, _ => handler());
    }

    public ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Action<T> handler)
    {
        return SubscribeAsync(key.Key, handler);
    }

    public ValueTask<IDisposable> SubscribeAsync<T>(string key, Action<T> handler)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return subscriptionManager.AddAsync(key, null, handler);
        }
        else
        {
            return WithConnectAsync(key, handler, static (self, key, handler) =>
            {
                return self.subscriptionManager.AddAsync(key, null, handler);
            });
        }
    }

    public ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Func<T, Task> asyncHandler)
    {
        return SubscribeAsync(key.Key, asyncHandler);
    }

    public ValueTask<IDisposable> SubscribeAsync<T>(string key, Func<T, Task> asyncHandler)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return subscriptionManager.AddAsync<T>(key, null, async x =>
            {
                try
                {
                    await asyncHandler(x).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error occured during subscribe message.");
                }
            });
        }
        else
        {
            return WithConnectAsync(key, asyncHandler, static (self, key, asyncHandler) =>
            {
                return self.subscriptionManager.AddAsync<T>(key, null, async x =>
                {
                    try
                    {
                        await asyncHandler(x).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        self.logger.LogError(ex, "Error occured during subscribe message.");
                    }
                });
            });
        }
    }

    public ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Action<T> handler)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return subscriptionManager.AddAsync(key.Key, queueGroup, handler);
        }
        else
        {
            return WithConnectAsync(key, queueGroup, handler, static (self, key, queueGroup, handler) =>
            {
                return self.subscriptionManager.AddAsync(key.Key, queueGroup, handler);
            });
        }
    }

    public ValueTask<IDisposable> QueueSubscribeAsync<T>(string key, string queueGroup, Action<T> handler)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return subscriptionManager.AddAsync(key, new NatsKey(queueGroup, true), handler);
        }
        else
        {
            return WithConnectAsync(key, queueGroup, handler, static (self, key, queueGroup, handler) =>
            {
                return self.subscriptionManager.AddAsync(key, new NatsKey(queueGroup, true), handler);
            });
        }
    }

    public ValueTask<IDisposable> QueueSubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Func<T, Task> asyncHandler)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return subscriptionManager.AddAsync<T>(key.Key, queueGroup, async x =>
            {
                try
                {
                    await asyncHandler(x).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error occured during subscribe message.");
                }
            });
        }
        else
        {
            return WithConnectAsync(key, queueGroup, asyncHandler, static (self, key, queueGroup, asyncHandler) =>
            {
                return self.subscriptionManager.AddAsync<T>(key.Key, queueGroup, async x =>
                {
                    try
                    {
                        await asyncHandler(x).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        self.logger.LogError(ex, "Error occured during subscribe message.");
                    }
                });
            });
        }
    }

    public ValueTask<IDisposable> QueueSubscribeAsync<T>(string key, string queueGroup, Func<T, Task> asyncHandler)
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            return subscriptionManager.AddAsync<T>(key, new NatsKey(queueGroup, true), async x =>
            {
                try
                {
                    await asyncHandler(x).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error occured during subscribe message.");
                }
            });
        }
        else
        {
            return WithConnectAsync(key, queueGroup, asyncHandler, static (self, key, queueGroup, asyncHandler) =>
            {
                return self.subscriptionManager.AddAsync<T>(key, new NatsKey(queueGroup, true), async x =>
                {
                    try
                    {
                        await asyncHandler(x).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        self.logger.LogError(ex, "Error occured during subscribe message.");
                    }
                });
            });
        }
    }

    public IObservable<T> AsObservable<T>(string key)
    {
        return AsObservable<T>(new NatsKey(key, true));
    }

    public IObservable<T> AsObservable<T>(in NatsKey key)
    {
        return new NatsObservable<T>(this, key);
    }

    public ValueTask FlushAsync()
    {
        if (ConnectionState == NatsConnectionState.Open)
        {
            var command = AsyncFlushCommand.Create(pool);
            commandWriter.TryWrite(command);
            return command.AsValueTask();
        }
        else
        {
            return WithConnectAsync(static self =>
            {
                var command = AsyncFlushCommand.Create(self.pool);
                self.commandWriter.TryWrite(command);
                return command.AsValueTask();
            });
        }
    }
}
