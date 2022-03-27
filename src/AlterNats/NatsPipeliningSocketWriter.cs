using AlterNats.Commands;
using AlterNats.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace AlterNats;

internal sealed class NatsPipeliningSocketWriter : IAsyncDisposable
{
    readonly Socket socket;
    readonly FixedArrayBufferWriter bufferWriter;
    readonly Channel<ICommand> channel;
    readonly Task writeLoop;
    readonly NatsOptions options;
    readonly Stopwatch stopwatch = new Stopwatch();

    public NatsPipeliningSocketWriter(Socket socket, NatsOptions options)
    {
        this.socket = socket;
        this.options = options;
        this.bufferWriter = new FixedArrayBufferWriter();
        this.channel = Channel.CreateUnbounded<ICommand>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false, // always should be in async loop.
            SingleWriter = false,
            SingleReader = true,
        });
        this.writeLoop = Task.Run(WriteLoopAsync);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Post(ICommand command)
    {
        channel.Writer.TryWrite(command);
    }

    async Task WriteLoopAsync()
    {
        var reader = channel.Reader;
        var protocolWriter = new ProtocolWriter(bufferWriter);
        var logger = options.LoggerFactory.CreateLogger<NatsPipeliningSocketWriter>();
        var writerBufferSize = options.WriterBufferSize;
        var promiseList = new List<IPromise>(100);
        var isEnabledTraceLogging = logger.IsEnabled(LogLevel.Trace);

        try
        {
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                try
                {
                    while (bufferWriter.WrittenCount < writerBufferSize && reader.TryRead(out var command))
                    {
                        command.Write(protocolWriter);

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
                        stopwatch.Restart();
                        await socket.SendAsync(bufferWriter.WrittenMemory, SocketFlags.None).ConfigureAwait(false);
                        stopwatch.Stop();
                        if (isEnabledTraceLogging)
                        {
                            logger.LogTrace("Socket.SendAsync. Size: {0} Elapsed: {1}ms", bufferWriter.WrittenCount, stopwatch.Elapsed.TotalMilliseconds);
                        }

                        bufferWriter.Reset();

                        foreach (var item in promiseList)
                        {
                            item.SetResult();
                        }
                    }
                    catch (Exception ex)
                    {
                        // flush failed
                        foreach (var promise in promiseList)
                        {
                            promise.SetException(ex);
                        }
                    }
                    finally
                    {
                        promiseList.Clear();
                    }
                }
                catch (Exception ex)
                {
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
        channel.Writer.Complete();
        await writeLoop.ConfigureAwait(false);
    }
}
