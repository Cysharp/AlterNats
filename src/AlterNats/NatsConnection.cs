using AlterNats.Commands;
using AlterNats.Internal;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Text;
using System.Threading.Channels;

namespace AlterNats;

public enum NatsConnectionState
{
    Closed,
    Open,
    Connecting,
    Reconnecting,
}

// This writer state is reused when reconnecting.
internal sealed class WriterState
{
    public FixedArrayBufferWriter BufferWriter { get; }
    public Channel<ICommand> CommandBuffer { get; }
    public NatsOptions Options { get; }
    public List<ICommand> PriorityCommands { get; }

    public WriterState(NatsOptions options)
    {
        Options = options;
        BufferWriter = new FixedArrayBufferWriter();
        CommandBuffer = Channel.CreateUnbounded<ICommand>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false, // always should be in async loop.
            SingleWriter = false,
            SingleReader = true,
        });
        PriorityCommands = new List<ICommand>();
    }
}

public class NatsConnection : IAsyncDisposable
{
    readonly object gate = new object();
    readonly WriterState writerState;
    readonly ChannelWriter<ICommand> commandWriter;
    readonly SubscriptionManager subscriptionManager;
    readonly RequestResponseManager requestResponseManager;
    readonly ILogger<NatsConnection> logger;
    internal readonly ReadOnlyMemory<byte> indBoxPrefix;

    Task? reconnectLoop;
    bool isDisposed;

    // when reconnect, make new instance.
    PhysicalConnection? socket;
    NatsReadProtocolProcessor? socketReader;
    NatsPipeliningWriteProtocolProcessor? socketWriter;

    public NatsOptions Options { get; }
    public NatsConnectionState ConnectionState { get; private set; }
    public ServerInfo? ServerInfo { get; internal set; } // server info is set when received INFO

    public NatsConnection()
        : this(NatsOptions.Default)
    {
    }

    public NatsConnection(NatsOptions options)
    {
        this.Options = options;
        this.ConnectionState = NatsConnectionState.Closed;
        this.writerState = new WriterState(options);
        this.commandWriter = writerState.CommandBuffer.Writer;
        this.subscriptionManager = new SubscriptionManager(this);
        this.requestResponseManager = new RequestResponseManager(this);
        this.indBoxPrefix = Encoding.ASCII.GetBytes($"{options.InboxPrefix}{Guid.NewGuid()}.");
        this.logger = options.LoggerFactory.CreateLogger<NatsConnection>();
    }

    /// <summary>
    /// Connect socket and write CONNECT command to nats server.
    /// </summary>
    public ValueTask ConnectAsync()
    {
        if (this.ConnectionState == NatsConnectionState.Open) return default;
        return InitialConnectAsync();
    }

    async ValueTask InitialConnectAsync()
    {
        // Only Closed(initial) state, can run.
        lock (gate)
        {
            ThrowIfDisposed();
            if (ConnectionState != NatsConnectionState.Closed) return;
            ConnectionState = NatsConnectionState.Connecting;
        }

        try
        {
            // TODO:foreach and retry
            var conn = new PhysicalConnection();
            await conn.ConnectAsync(Options.Host, Options.Port, CancellationToken.None); // TODO:CancellationToken
            this.socket = conn;
        }
        catch
        {
            this.ConnectionState = NatsConnectionState.Closed;
            throw; // throw for can't connect.
        }

        // Connected completely but still ConnnectionState is Connecting(require after receive INFO).

        // add CONNECT command to priority lane
        var connectCommand = AsyncConnectCommand.Create(Options.ConnectOptions);
        writerState.PriorityCommands.Add(connectCommand);

        // Run Reader/Writer LOOP start
        var waitForInfoSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        this.socketWriter = new NatsPipeliningWriteProtocolProcessor(socket, writerState);
        this.socketReader = new NatsReadProtocolProcessor(socket, this, waitForInfoSignal);

        try
        {
            await connectCommand.AsValueTask().ConfigureAwait(false);
            await waitForInfoSignal.Task.ConfigureAwait(false);
        }
        catch
        {
            // can not start reader/writer
            await socketWriter!.DisposeAsync();
            await socketReader!.DisposeAsync();
            await socket!.DisposeAsync();
            socket = null;
            socketWriter = null;
            socketReader = null;
            ConnectionState = NatsConnectionState.Closed;
            throw;
        }

        // After INFO received, reconnect server list has been get.
        this.ConnectionState = NatsConnectionState.Open;
        reconnectLoop = Task.Run(ReconnectLoopAsync);
    }

    async Task ReconnectLoopAsync()
    {
        //await socket.WaitForClosed.ConfigureAwait(false);

        //this.ConnectionState = NatsConnectionState.Reconnecting;

        //// Cleanup current reader/writer
        //{
        //    // reader is not share state, can dispose asynchronously.
        //    var reader = socketReader;
        //    _ = Task.Run(async () =>
        //    {
        //        try
        //        {
        //            await reader.DisposeAsync().ConfigureAwait(false);
        //        }
        //        catch (Exception ex)
        //        {
        //            logger.LogError(ex, "Error occured when disposing socket reader loop.");
        //        }
        //    });

        //    // writer's internal buffer/channel is not thread-safe, must wait complete.
        //    await socketWriter.DisposeAsync();
        //}

        //// TODO:when disposed, don't do this.

        //// Dispose current and create new
        //await socket.DisposeAsync();

        //socket = new PhysicalConnection();
        //waitForConnectSource = new TaskCompletionSource();
        //waitForInfoSource = new TaskCompletionSource();
        //socketReader = new NatsReadProtocolProcessor(socket, this);
        //socketWriter = new NatsPipeliningSocketWriter(socket, Options);

        //// TODO:try-catch
        //try
        //{
        //    await socket.ConnectAsync(Options.Host, Options.Port, CancellationToken.None); // TODO:CancellationToken?
        //}
        //catch (Exception ex)
        //{
        //    Console.WriteLine(ex); // TODO:loop
        //}

        //var command = AsyncConnectCommand.Create(Options.ConnectOptions);
        //commandWriter.TryWrite(command);
        //await command.AsValueTask();

        //this.ConnectionState = NatsConnectionState.Open;

        //waitForConnectSource.TrySetResult(); // signal connected to NatsReadProtocolProcessor loop

        //// Wait Info receive
        //await waitForInfoSource.Task.ConfigureAwait(false); // TODO:timeout? 

        //// Resubscribe
        //// TODO:BatchRequest.
        //foreach (var item in subscriptionManager.GetExistingSubscriptions())
        //{
        //}
    }

    // Public APIs
    // ***Async or Post***Async(fire-and-forget)

    public void PostPing()
    {
        commandWriter.TryWrite(PingCommand.Create());
    }

    public ValueTask PingAsync()
    {
        var command = AsyncPingCommand.Create();
        commandWriter.TryWrite(command);
        return command.AsValueTask();
    }

    public ValueTask PublishAsync<T>(in NatsKey key, T value)
    {
        if (ConnectionState != NatsConnectionState.Open)
        {
        }

        var command = AsyncPublishCommand<T>.Create(key, value, Options.Serializer);
        commandWriter.TryWrite(command);
        return command.AsValueTask();
    }

    public ValueTask PublishAsync<T>(string key, T value)
    {
        return PublishAsync<T>(new NatsKey(key, true), value);
    }

    public ValueTask PublishAsync(in NatsKey key, byte[] value)
    {
        var command = AsyncPublishBytesCommand.Create(key, value);
        commandWriter.TryWrite(command);
        return command.AsValueTask();
    }

    public ValueTask PublishAsync(string key, byte[] value)
    {
        return PublishAsync(new NatsKey(key, true), value);
    }

    public ValueTask PublishAsync(in NatsKey key, ReadOnlyMemory<byte> value)
    {
        var command = AsyncPublishBytesCommand.Create(key, value);
        commandWriter.TryWrite(command);
        return command.AsValueTask();
    }

    public ValueTask PublishAsync(string key, ReadOnlyMemory<byte> value)
    {
        return PublishAsync(new NatsKey(key, true), value);
    }

    public void PostPublish<T>(in NatsKey key, T value)
    {
        var command = PublishCommand<T>.Create(key, value, Options.Serializer);
        commandWriter.TryWrite(command);
    }

    public void PostPublish<T>(string key, T value)
    {
        PostPublish<T>(new NatsKey(key, true), value);
    }

    public void PostPublish(in NatsKey key, byte[] value)
    {
        var command = PublishBytesCommand.Create(key, value);
        commandWriter.TryWrite(command);
    }

    public void PostPublish(string key, byte[] value)
    {
        PostPublish(new NatsKey(key, true), value);
    }

    public void PostPublish(in NatsKey key, ReadOnlyMemory<byte> value)
    {
        var command = PublishBytesCommand.Create(key, value);
        commandWriter.TryWrite(command);
    }

    public void PostPublish(string key, ReadOnlyMemory<byte> value)
    {
        PostPublish(new NatsKey(key, true), value);
    }

    public ValueTask PublishBatchAsync<T>(IEnumerable<(NatsKey, T?)> values)
    {
        var command = AsyncPublishBatchCommand<T>.Create(values, Options.Serializer);
        commandWriter.TryWrite(command);
        return command.AsValueTask();
    }

    public ValueTask PublishBatchAsync<T>(IEnumerable<(string, T?)> values)
    {
        var command = AsyncPublishBatchCommand<T>.Create(values, Options.Serializer);
        commandWriter.TryWrite(command);
        return command.AsValueTask();
    }

    public ValueTask<TResponse> RequestAsync<TRequest, TResponse>(in NatsKey key, TRequest request)
    {
        return requestResponseManager.AddAsync<TRequest, TResponse>(key, indBoxPrefix, request)!; // NOTE:return nullable?
    }

    public ValueTask<TResponse> RequestAsync<TRequest, TResponse>(string key, TRequest request)
    {
        return RequestAsync<TRequest, TResponse>(new NatsKey(key, true), request);
    }

    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, TResponse> requestHandler)
    {
        return subscriptionManager.AddRequestHandlerAsync(key.Key, requestHandler);
    }

    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, TResponse> requestHandler)
    {
        return subscriptionManager.AddRequestHandlerAsync(key, requestHandler);
    }

    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(in NatsKey key, Func<TRequest, Task<TResponse>> requestHandler)
    {
        return subscriptionManager.AddRequestHandlerAsync(key.Key, requestHandler);
    }

    public ValueTask<IDisposable> SubscribeRequestAsync<TRequest, TResponse>(string key, Func<TRequest, Task<TResponse>> requestHandler)
    {
        return subscriptionManager.AddRequestHandlerAsync(key, requestHandler);
    }

    public ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Action<T> handler)
    {
        return subscriptionManager.AddAsync(key.Key, null, handler);
    }

    public ValueTask<IDisposable> SubscribeAsync<T>(string key, Action<T> handler)
    {
        return subscriptionManager.AddAsync(key, null, handler);
    }

    public ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, Func<T, Task> asyncHandler)
    {
        return SubscribeAsync(key.Key, asyncHandler);
    }

    public ValueTask<IDisposable> SubscribeAsync<T>(string key, Func<T, Task> asyncHandler)
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

    public ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Action<T> handler)
    {
        return subscriptionManager.AddAsync(key.Key, queueGroup, handler);
    }

    public ValueTask<IDisposable> SubscribeAsync<T>(string key, string queueGroup, Action<T> handler)
    {
        return subscriptionManager.AddAsync(key, new NatsKey(queueGroup, true), handler);
    }

    public ValueTask<IDisposable> SubscribeAsync<T>(in NatsKey key, in NatsKey queueGroup, Func<T, Task> asyncHandler)
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

    public ValueTask<IDisposable> SubscribeAsync<T>(string key, string queueGroup, Func<T, Task> asyncHandler)
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

    public IObservable<T> AsObservable<T>(string key)
    {
        return AsObservable<T>(new NatsKey(key, true));
    }

    public IObservable<T> AsObservable<T>(in NatsKey key)
    {
        return new NatsObservable<T>(this, key);
    }

    // internal commands.

    internal void PostPong()
    {
        commandWriter.TryWrite(PongCommand.Create());
    }

    internal ValueTask SubscribeAsync(int subscriptionId, string subject, in NatsKey? queueGroup)
    {
        var command = AsyncSubscribeCommand.Create(subscriptionId, new NatsKey(subject, true), queueGroup);
        commandWriter.TryWrite(command);
        return command.AsValueTask();
    }

    internal void PostUnsubscribe(int subscriptionId)
    {
        commandWriter.TryWrite(UnsubscribeCommand.Create(subscriptionId));
    }

    internal void PostCommand(ICommand command)
    {
        commandWriter.TryWrite(command);
    }

    internal void PublishToClientHandlers(int subscriptionId, in ReadOnlySequence<byte> buffer)
    {
        subscriptionManager.PublishToClientHandlers(subscriptionId, buffer);
    }

    internal void PublishToRequestHandler(int subscriptionId, in NatsKey replyTo, in ReadOnlySequence<byte> buffer)
    {
        subscriptionManager.PublishToRequestHandler(subscriptionId, replyTo, buffer);
    }

    internal void PublishToResponseHandler(int requestId, in ReadOnlySequence<byte> buffer)
    {
        requestResponseManager.PublishToResponseHandler(requestId, buffer);
    }

    internal void PostDirectWrite(string protocol)
    {
        commandWriter.TryWrite(new DirectWriteCommand(protocol));
    }

    public async ValueTask DisposeAsync()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            await socketWriter.DisposeAsync().ConfigureAwait(false);
            await socketReader.DisposeAsync().ConfigureAwait(false);
            subscriptionManager.Dispose();

            await socket.DisposeAsync();
        }
    }

    void ThrowIfDisposed()
    {
        if (isDisposed) throw new ObjectDisposedException(null);
    }

    // static Cache operations.

    public static int MaxCommandCacheSize { get; set; } = int.MaxValue;

    public static int GetPublishCommandCacheSize<T>(bool async)
    {
        if (typeof(T) == typeof(byte[]) || typeof(T) == typeof(ReadOnlyMemory<byte>))
        {
            if (async)
            {
                // TODO:AsyncPublishBytes
                // return PublishBytesCommand<T>.GetCacheCount;
                return 0;
            }
            else
            {
                return PublishBytesCommand.GetCacheCount;
            }
        }
        else
        {
            if (async)
            {
                return AsyncPublishCommand<T>.GetCacheCount;
            }
            else
            {
                return PublishCommand<T>.GetCacheCount;
            }
        }
    }

    public static void CachePublishCommand<T>(int cacheCount)
    {
        if (typeof(T) == typeof(byte[]) || typeof(T) == typeof(ReadOnlyMemory<byte>))
        {
            var array = new PublishBytesCommand[cacheCount];
            for (int i = 0; i < cacheCount; i++)
            {
                array[i] = PublishBytesCommand.Create(default, default);
            }
            for (int i = 0; i < cacheCount; i++)
            {
                (array[i] as ICommand).Return();
            }
        }
        else
        {
            var array = new PublishCommand<T>[cacheCount];
            for (int i = 0; i < cacheCount; i++)
            {
                array[i] = PublishCommand<T>.Create(default, default!, default!);
            }
            for (int i = 0; i < cacheCount; i++)
            {
                (array[i] as ICommand).Return();
            }
        }
    }
}
