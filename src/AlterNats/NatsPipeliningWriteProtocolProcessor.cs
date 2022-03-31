using AlterNats.Commands;
using AlterNats.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Channels;

namespace AlterNats;

internal sealed class NatsPipeliningWriteProtocolProcessor : IAsyncDisposable
{
    readonly PhysicalConnection socket;
    readonly WriterState state;
    readonly FixedArrayBufferWriter bufferWriter;
    readonly Channel<ICommand> channel;
    readonly NatsOptions options;
    readonly Task writeLoop;
    readonly Stopwatch stopwatch = new Stopwatch();
    readonly CancellationTokenSource cancellationTokenSource;

    public NatsPipeliningWriteProtocolProcessor(PhysicalConnection socket, WriterState state)
    {
        this.socket = socket;
        this.state = state;
        this.bufferWriter = state.BufferWriter;
        this.channel = state.CommandBuffer;
        this.options = state.Options;
        this.cancellationTokenSource = new CancellationTokenSource();
        this.writeLoop = Task.Run(WriteLoopAsync);
    }

    async Task WriteLoopAsync()
    {
        var reader = channel.Reader;
        var protocolWriter = new ProtocolWriter(bufferWriter);
        var logger = options.LoggerFactory.CreateLogger<NatsPipeliningWriteProtocolProcessor>();
        var writerBufferSize = options.WriterBufferSize;
        var promiseList = new List<IPromise>(100);
        var isEnabledTraceLogging = logger.IsEnabled(LogLevel.Trace);

        try
        {
            // at first, send priority lane(initial command).
            {
                var firstCommands = state.PriorityCommands;
                if (firstCommands.Count != 0)
                {
                    var tempBuffer = new FixedArrayBufferWriter(1024);
                    var tempWriter = new ProtocolWriter(tempBuffer);
                    foreach (var command in firstCommands)
                    {
                        command.Write(tempWriter);

                        if (command is IPromise p)
                        {
                            promiseList.Add(p);
                        }
                        else
                        {
                            command.Return();
                        }
                    }
                    state.PriorityCommands.Clear();

                    try
                    {
                        var memory = tempBuffer.WrittenMemory;
                        do
                        {
                            var sent = await socket.SendAsync(memory, SocketFlags.None).ConfigureAwait(false);
                            memory = memory.Slice(sent);
                        } while (memory.Length > 0);
                    }
                    catch (Exception ex)
                    {
                        socket.SignalDisconnected(ex);
                        foreach (var item in promiseList)
                        {
                            item.SetException(ex); // signal failed
                        }
                        return; // when socket closed, finish writeloop.
                    }

                    foreach (var item in promiseList)
                    {
                        item.SetResult();
                    }
                    promiseList.Clear();
                }
            }

            // main writer loop,
            while ((bufferWriter.WrittenCount != 0) || (await reader.WaitToReadAsync(cancellationTokenSource.Token).ConfigureAwait(false)))
            {
                try
                {
                    var count = 0;
                    while (bufferWriter.WrittenCount < writerBufferSize && reader.TryRead(out var command))
                    {
                        if (command is IBatchCommand batch)
                        {
                            count += batch.Write(protocolWriter);
                        }
                        else
                        {
                            command.Write(protocolWriter);
                            count++;
                        }

                        if (command is IPromise p)
                        {
                            promiseList.Add(p);
                        }
                        else
                        {
                            command.Return();
                        }
                    }

                    try
                    {
                        // SendAsync(ReadOnlyMemory) is very efficient, internally using AwaitableAsyncSocketEventArgs
                        // should use cancellation token?, currently no, wait for flush complete.
                        if (isEnabledTraceLogging)
                        {
                            logger.LogTrace("SocketWriter Start to send BatchSize: {0}", count);
                        }

                        var memory = bufferWriter.WrittenMemory;
                        do
                        {
                            stopwatch.Restart();
                            var sent = await socket.SendAsync(memory, SocketFlags.None).ConfigureAwait(false);
                            stopwatch.Stop();
                            if (isEnabledTraceLogging)
                            {
                                logger.LogTrace("Socket.SendAsync. Size: {0} Elapsed: {1}ms", sent, stopwatch.Elapsed.TotalMilliseconds);
                            }
                            if (sent == 0)
                            {
                                throw new SocketClosedException(null);
                            }

                            memory = memory.Slice(sent);
                        }
                        while (memory.Length != 0);

                        bufferWriter.Reset();
                        foreach (var item in promiseList)
                        {
                            item.SetResult();
                        }
                    }
                    catch (Exception ex) // may receive from socket.SendAsync
                    {
                        // TODO: require reset bufferWriter and copy memory if size is not matched.

                        // TODO:for reconnect, don't signal task
                        foreach (var promise in promiseList)
                        {
                            promise.SetException(ex);
                        }

                        socket.SignalDisconnected(ex);
                        return; // when socket closed, finish writeloop.

                    }
                    finally
                    {
                        // TODO:if not signal, don't clear
                        promiseList.Clear();
                    }
                }
                catch (Exception ex)
                {
                    if (ex is SocketClosedException)
                    {
                        return;
                    }
                    try
                    {
                        logger.LogError(ex, "Internal error occured on WriteLoop.");
                    }
                    catch { }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            try
            {
                if (bufferWriter.WrittenMemory.Length != 0)
                {
                    await socket.SendAsync(bufferWriter.WrittenMemory, SocketFlags.None).ConfigureAwait(false);
                }
            }
            catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        // TODO:state
        cancellationTokenSource.Cancel();
        await writeLoop.ConfigureAwait(false); // wait for drain writer
    }
}
