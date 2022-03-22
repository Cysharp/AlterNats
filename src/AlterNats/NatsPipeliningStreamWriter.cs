using AlterNats.Commands;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace AlterNats;

internal sealed class NatsPipeliningStreamWriter : IAsyncDisposable
{
    readonly Stream stream;
    readonly PipeWriter pipeWriter;
    readonly Channel<ICommand> channel;
    readonly Task writeLoop;
    // TODO:no need if not using Task.Delay etc..
    readonly CancellationTokenSource cancellationTokenSource;
    readonly NatsOptions options;

    public NatsPipeliningStreamWriter(Stream stream, NatsOptions options)
    {
        this.stream = stream;
        this.options = options;
        this.pipeWriter = PipeWriter.Create(stream);
        this.cancellationTokenSource = new CancellationTokenSource();
        this.channel = Channel.CreateUnbounded<ICommand>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false, // always should be in async loop.
            SingleWriter = false,
            SingleReader = true,
        });
        this.writeLoop = Task.Run(WriteLoop);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Post(ICommand command)
    {
        channel.Writer.TryWrite(command);
    }

    async Task WriteLoop()
    {
        var reader = channel.Reader;
        var protocolWriter = new ProtocolWriter(pipeWriter);
        var logger = options.LoggerFactory.CreateLogger<NatsPipeliningStreamWriter>();
        var promiseList = new List<IPromise>(options.MaxBatchCount);
        try
        {
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                try
                {
                    // TODO:buffer
                    while (reader.TryRead(out var command))
                    {
                        command.Write(logger, protocolWriter);

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
                        logger.LogTrace("Write loop start Flush.");
                        var flushResult = await pipeWriter.FlushAsync().ConfigureAwait(false);
                        logger.LogTrace("Write loop complete Flush.");
                    }
                    catch (Exception ex)
                    {
                        // flush failed
                        foreach (var promise in promiseList)
                        {
                            promise.SetException(ex);
                        }
                    }
                    promiseList.Clear();
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
                await pipeWriter.FlushAsync().ConfigureAwait(false);
            }
            catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        channel.Writer.Complete();
        cancellationTokenSource.Cancel();
        await this.pipeWriter.CompleteAsync().ConfigureAwait(false);
        await writeLoop.ConfigureAwait(false);
    }
}
