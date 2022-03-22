using AlterNats.Internal;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace AlterNats;

internal sealed class NatsReadProtocolProcessor : IAsyncDisposable
{
    readonly Socket socket;
    readonly NatsConnection connection;
    readonly PipeWriter writer;
    readonly PipeReader reader;
    readonly Task receiveLoop;
    readonly Task consumeLoop;
    readonly CancellationTokenSource cancellationTokenSource;
    readonly ILogger<NatsReadProtocolProcessor> logger;

    public NatsReadProtocolProcessor(Socket socket, NatsConnection connection)
    {
        this.socket = socket;
        this.connection = connection;
        this.logger = connection.Options.LoggerFactory.CreateLogger<NatsReadProtocolProcessor>();
        this.cancellationTokenSource = new CancellationTokenSource();

        // TODO: set threshold
        var pipe = new Pipe(new PipeOptions(useSynchronizationContext: false));
        this.reader = pipe.Reader;
        this.writer = pipe.Writer;

        this.receiveLoop = Task.Run(ReciveLoopAsync);
        this.consumeLoop = Task.Run(ConsumeLoopAsync);
    }

    // receive data from socket and write to Pipe.
    async Task ReciveLoopAsync()
    {
        try
        {
            await connection.WaitForConnect.WaitAsync(cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (true)
        {
            var buffer = writer.GetMemory(connection.Options.ReaderBufferSize);
            try
            {
                logger.LogTrace("Start Socket Read"); // TODO: if-trace
                var read = await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationTokenSource.Token);
                if (read == 0)
                {
                    break; // complete.
                }

                logger.LogTrace("Receive: {0} B", read); // TODO: if-trace
                writer.Advance(read);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occured during receive loop.");
                return; // ???
            }

            var result = await writer.FlushAsync();

            if (result.IsCompleted)
            {
                break;
            }
        }

        await writer.CompleteAsync();
    }

    // read data from pipe and consume message.
    async Task ConsumeLoopAsync()
    {
        try
        {
            await connection.WaitForConnect.WaitAsync(cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (true)
        {
            try
            {
                var readResult = await reader.ReadAtLeastAsync(4);
                var buffer = readResult.Buffer;

                while (buffer.Length > 0)
                {
                    var first = buffer.First;

                    var code = (first.Length >= 4)
                        ? GetCode(first.Span)
                        : GetCode(buffer);

                    if (code == ServerOpCodes.Msg)
                    {
                        // Optimized for Msg, most frequently called type, Inline await in loop.
                        throw new NotImplementedException();
                    }
                    else
                    {
                        // Others.
                        buffer = await DispatchCommandAsync(code, buffer);
                    }
                }

                reader.AdvanceTo(buffer.End);
                if (readResult.IsCompleted)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occured during read loop.");
                // TODO:return?
                return;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int GetCode(ReadOnlySpan<byte> span)
    {
        return Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference<byte>(span));
    }

    static int GetCode(in ReadOnlySequence<byte> sequence)
    {
        Span<byte> buf = stackalloc byte[4];
        sequence.Slice(0, 4).CopyTo(buf);
        return GetCode(buf);
    }

    [AsyncMethodBuilderAttribute(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    async ValueTask<(ReadOnlySequence<byte>, SequencePosition)> ReadUntilReceiveNewLineAsync()
    {
        // TODO:how read readResult handle?
        while (true)
        {
            var result = await reader.ReadAsync();
            var position = result.Buffer.PositionOf((byte)'\n');
            if (position != null)
            {
                return (result.Buffer, position.Value);
            }

            reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
            // TODO:check result completed?
        }
    }

    [AsyncMethodBuilderAttribute(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    async ValueTask<ReadOnlySequence<byte>> DispatchCommandAsync(int code, in ReadOnlySequence<byte> buffer)
    {
        var length = (int)buffer.Length;

        if (code == ServerOpCodes.Ping)
        {
            const int PingSize = 6; // PING\r\n

            logger.LogTrace("Receive Ping");

            connection.PostPong(); // return pong

            if (length < PingSize)
            {
                // TODO:how read readResult handle?
                reader.AdvanceTo(buffer.Start, buffer.End);
                var readResult = await reader.ReadAtLeastAsync(PingSize - length);
                return readResult.Buffer.Slice(PingSize);
            }

            return buffer.Slice(PingSize);
        }
        else if (code == ServerOpCodes.Pong)
        {
            const int PongSize = 6; // PONG\r\n

            // TODO: PONG TRACE
            logger.LogTrace("Receive Pong");

            if (length < PongSize)
            {
                // TODO:how read readResult handle?
                reader.AdvanceTo(buffer.Start, buffer.End);
                var readResult = await reader.ReadAtLeastAsync(PongSize - length);
                return readResult.Buffer.Slice(PongSize);
            }

            return buffer.Slice(PongSize);
        }
        else if (code == ServerOpCodes.Error)
        {
            logger.LogTrace("Receive Error");

            // TODO: Parse Error
            throw new NotImplementedException();
        }
        else if (code == ServerOpCodes.Ok)
        {
            logger.LogTrace("Receive OK");

            const int OkSize = 5; // +OK\r\n

            if (length < OkSize)
            {
                // TODO:how read readResult handle?
                reader.AdvanceTo(buffer.Start, buffer.End);
                var readResult = await reader.ReadAtLeastAsync(OkSize - length);
                return readResult.Buffer.Slice(OkSize);
            }

            return buffer.Slice(OkSize);
        }
        else if (code == ServerOpCodes.Info)
        {
            logger.LogTrace("Receive Info");

            // TODO:log info?
            //var info = ParseInfo(buffer, out var readSize);
            //return readSize + buffer.Span.Slice(readSize).IndexOf((byte)'\n') + 1;


            // try to get \n.
            var position = buffer.PositionOf((byte)'\n');

            if (position == null)
            {
                reader.AdvanceTo(buffer.Start, buffer.End);
                var (newBuffer, newPosition) = await ReadUntilReceiveNewLineAsync();
                // TODO:log serverInfo.
                var serverInfo = ParseInfo(newBuffer);
                connection.ServerInfo = serverInfo;
                logger.LogInformation("Received ServerInfo: {0}", serverInfo);
                return newBuffer.Slice(buffer.GetPosition(1, newPosition));
            }
            else
            {
                var serverInfo = ParseInfo(buffer);
                connection.ServerInfo = serverInfo;
                logger.LogInformation("Received ServerInfo: {0}", serverInfo);
                return buffer.Slice(buffer.GetPosition(1, position.Value));
            }
        }
        else
        {
            // TODO:reaches invalid line.
            //var s = Encoding.UTF8.GetString(buffer.Span.Slice(0, 4));
            //Console.WriteLine("NANIDEMO NAI!?:" + s);

            // reaches invalid line.
            // Try to skip next '\n';
            throw new NotImplementedException();
        }
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#info
    // INFO {["option_name":option_value],...}
    internal static ServerInfo ParseInfo(ReadOnlySequence<byte> buffer)
    {
        // skip `INFO`
        var jsonReader = new Utf8JsonReader(buffer.Slice(5));

        var serverInfo = JsonSerializer.Deserialize<ServerInfo>(ref jsonReader);
        if (serverInfo == null) throw new InvalidOperationException(""); // TODO:NatsException
        return serverInfo;
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#msg
    // MSG <subject> <sid> [reply-to] <#bytes>\r\n[payload]
    internal static void ParseMessage(ReadOnlySequence<byte> data, out SequencePosition readPosition, Action<ReadOnlySequence<byte>> callback)
    {
        if (data.IsSingleSegment)
        {
            var offset = 0;
            var buffer = data.FirstSpan.Slice(4);

            offset += Split(buffer, ' ', out var subject, out buffer);
            offset += Split(buffer, ' ', out var sid, out buffer);

            // reply-to or #bytes, check \n first.
            var i = buffer.IndexOf((byte)'\n');
            var i2 = buffer.Slice(0, i).IndexOf((byte)' ');
            if (i2 == -1)
            {
                // not exists reply-to, only #bytes
                if (!Utf8Parser.TryParse(buffer.Slice(0, i - 1), out int payloadLength, out _))
                {
                    throw new Exception(); // todo
                }

                offset += i + 1;
                var payload = data.Slice(offset, payloadLength); // create slice of ReadOnlySequence
                callback(payload);

                var last = buffer.Slice(i + 1 + payloadLength).IndexOf((byte)'\n');
                readPosition = data.GetPosition(last + 1);
            }
            else
            {
                throw new NotImplementedException(); // TODO:reply-to!
            }
        }
        else
        {
            throw new NotImplementedException(); // TODO:how???
        }
    }

    static int Split(ReadOnlySpan<byte> span, char delimiter, out ReadOnlySpan<byte> left, out ReadOnlySpan<byte> right)
    {
        var i = span.IndexOf((byte)delimiter);
        if (i == -1)
        {
            // TODO:
            throw new InvalidOperationException();
        }

        left = span.Slice(0, i);
        right = span.Slice(i + 1);
        return i + 1;
    }

    public async ValueTask DisposeAsync()
    {
        cancellationTokenSource.Cancel();
        await writer.CompleteAsync();
        await reader.CompleteAsync(); // TODO:check stop behaviour
        await receiveLoop;
        await consumeLoop;
    }

    internal static class ServerOpCodes
    {
        // All sent by server commands as int(first 4 characters(includes space, newline)).

        public const int Info = 1330007625;  // Encoding.ASCII.GetBytes("INFO") |> MemoryMarshal.Read<int>
        public const int Msg = 541545293;    // Encoding.ASCII.GetBytes("MSG ") |> MemoryMarshal.Read<int>
        public const int Ping = 1196312912;  // Encoding.ASCII.GetBytes("PING") |> MemoryMarshal.Read<int>
        public const int Pong = 1196314448;  // Encoding.ASCII.GetBytes("PONG") |> MemoryMarshal.Read<int>
        public const int Ok = 223039275;     // Encoding.ASCII.GetBytes("+OK\r") |> MemoryMarshal.Read<int>
        public const int Error = 1381123373; // Encoding.ASCII.GetBytes("-ERR") |> MemoryMarshal.Read<int>
    }
}