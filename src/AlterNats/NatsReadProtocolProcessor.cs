using AlterNats.Internal;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Buffers.Text;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;

namespace AlterNats;

internal sealed class NatsReadProtocolProcessor : IAsyncDisposable
{
    readonly Socket socket;
    readonly NatsConnection connection;
    readonly SocketReader socketReader;
    readonly Task readLoop;
    readonly CancellationTokenSource cancellationTokenSource;
    readonly ILogger<NatsReadProtocolProcessor> logger;
    readonly bool isEnabledTraceLogging;

    public NatsReadProtocolProcessor(Socket socket, NatsConnection connection)
    {
        this.socket = socket;
        this.connection = connection;
        this.logger = connection.Options.LoggerFactory.CreateLogger<NatsReadProtocolProcessor>();
        this.cancellationTokenSource = new CancellationTokenSource();
        this.isEnabledTraceLogging = logger.IsEnabled(LogLevel.Trace);
        this.socketReader = new SocketReader(socket, connection.Options.ReaderBufferSize, cancellationTokenSource.Token);
        this.readLoop = Task.Run(ReadLoopAsync);
    }

    async Task ReadLoopAsync()
    {
        try
        {
            await connection.WaitForConnect.WaitAsync(cancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (true)
        {
            try
            {
                // when read buffer is complete, ReadFully.
                var buffer = await socketReader.ReadAtLeastAsync(4).ConfigureAwait(false);

                // parse messages from buffer without additional socket read
                // Note: in this loop, use socketReader.Read "must" requires socketReader.AdvanceTo
                //       because buffer-sequence and reader's sequence state is not synced to optimize performance.
                while (buffer.Length > 0)
                {
                    var first = buffer.First;

                    int code;
                    if (first.Length >= 4)
                    {
                        code = GetCode(first.Span);
                    }
                    else
                    {
                        if (buffer.Length < 4)
                        {
                            // try get additional buffer to require read Code
                            socketReader.AdvanceTo(buffer.Start);
                            buffer = await socketReader.ReadAtLeastAsync(4 - (int)buffer.Length).ConfigureAwait(false);
                        }
                        code = GetCode(buffer);
                    }

                    // Optimize for Msg parsing, Inline async code
                    if (code == ServerOpCodes.Msg)
                    {
                        if (isEnabledTraceLogging)
                        {
                            logger.LogTrace("Receive Msg");
                        }

                        // https://docs.nats.io/reference/reference-protocols/nats-protocol#msg
                        // MSG <subject> <sid> [reply-to] <#bytes>\r\n[payload]

                        // Try to find before \n
                        var positionBeforePayload = buffer.PositionOf((byte)'\n');
                        if (positionBeforePayload == null)
                        {
                            socketReader.AdvanceTo(buffer.Start);
                            buffer = await socketReader.ReadUntilReceiveNewLineAsync().ConfigureAwait(false);
                            positionBeforePayload = buffer.PositionOf((byte)'\n')!;
                        }

                        var msgHeader = buffer.Slice(0, positionBeforePayload.Value);
                        var (subscriptionId, payloadLength) = ParseMessageHeader(msgHeader);

                        var payloadBegin = buffer.GetPosition(1, positionBeforePayload.Value);
                        var payloadSlice = buffer.Slice(payloadBegin);

                        if (payloadSlice.Length < (payloadLength + 2)) // slice required \r\n
                        {
                            socketReader.AdvanceTo(payloadBegin);
                            buffer = await socketReader.ReadAtLeastAsync(payloadLength + 2); // payload + \r\n
                            payloadSlice = buffer.Slice(0, payloadLength);
                        }
                        else
                        {
                            payloadSlice = payloadSlice.Slice(0, payloadLength); // TODO:reduce slice count?
                        }

                        connection.PublishToClientHandlers(subscriptionId, payloadSlice);

                        buffer = buffer.Slice(buffer.GetPosition(2, payloadSlice.End)); // payload + \r\n
                    }
                    else
                    {
                        buffer = await DispatchCommandAsync(code, buffer).ConfigureAwait(false);
                    }
                }

                // Length == 0, AdvanceTo End.
                socketReader.AdvanceTo(buffer.End);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occured during read loop.");
                // TODO:return? should disconnect socket.
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int GetInt32(ReadOnlySpan<byte> span)
    {
        if (!Utf8Parser.TryParse(span, out int value, out var consumed))
        {
            throw new Exception(); // throw...
        }
        return value;
    }

    static int GetInt32(in ReadOnlySequence<byte> sequence)
    {
        if (sequence.IsSingleSegment || sequence.FirstSpan.Length <= 10)
        {
            return GetInt32(sequence.FirstSpan);
        }

        Span<byte> buf = stackalloc byte[Math.Min((int)sequence.Length, 10)];
        sequence.Slice(buf.Length).CopyTo(buf);
        return GetInt32(buf);
    }

    [AsyncMethodBuilderAttribute(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    async ValueTask<ReadOnlySequence<byte>> DispatchCommandAsync(int code, in ReadOnlySequence<byte> buffer)
    {
        var length = (int)buffer.Length;

        if (code == ServerOpCodes.Ping)
        {
            const int PingSize = 6; // PING\r\n

            if (isEnabledTraceLogging)
            {
                logger.LogTrace("Receive Ping");
            }

            connection.PostPong(); // return pong

            if (length < PingSize)
            {
                socketReader.AdvanceTo(buffer.Start);
                var readResult = await socketReader.ReadAtLeastAsync(PingSize - length).ConfigureAwait(false);
                return readResult.Slice(PingSize);
            }

            return buffer.Slice(PingSize);
        }
        else if (code == ServerOpCodes.Pong)
        {
            const int PongSize = 6; // PONG\r\n

            if (isEnabledTraceLogging)
            {
                logger.LogTrace("Receive Pong");
            }

            if (length < PongSize)
            {
                socketReader.AdvanceTo(buffer.Start);
                var readResult = await socketReader.ReadAtLeastAsync(PongSize - length).ConfigureAwait(false);
                return readResult.Slice(PongSize);
            }

            return buffer.Slice(PongSize);
        }
        else if (code == ServerOpCodes.Error)
        {
            if (isEnabledTraceLogging)
            {
                logger.LogTrace("Receive Error");
            }

            // TODO: Parse Error
            throw new NotImplementedException();
        }
        else if (code == ServerOpCodes.Ok)
        {
            if (isEnabledTraceLogging)
            {
                logger.LogTrace("Receive OK");
            }

            const int OkSize = 5; // +OK\r\n

            if (length < OkSize)
            {
                socketReader.AdvanceTo(buffer.Start);
                var readResult = await socketReader.ReadAtLeastAsync(OkSize - length).ConfigureAwait(false);
                return readResult.Slice(OkSize);
            }

            return buffer.Slice(OkSize);
        }
        else if (code == ServerOpCodes.Info)
        {
            if (isEnabledTraceLogging)
            {
                logger.LogTrace("Receive Info");
            }

            // try to get \n.
            var position = buffer.PositionOf((byte)'\n');

            if (position == null)
            {
                socketReader.AdvanceTo(buffer.Start);
                var newBuffer = await socketReader.ReadUntilReceiveNewLineAsync().ConfigureAwait(false);
                var newPosition = newBuffer.PositionOf((byte)'\n');

                var serverInfo = ParseInfo(newBuffer);
                connection.ServerInfo = serverInfo;
                logger.LogInformation("Received ServerInfo: {0}", serverInfo);
                return newBuffer.Slice(buffer.GetPosition(1, newPosition!.Value));
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
            // TODO:reaches invalid line, log warn and try to get newline and go to nextloop.
            throw new NotImplementedException();
        }
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#info
    // INFO {["option_name":option_value],...}
    internal static ServerInfo ParseInfo(in ReadOnlySequence<byte> buffer)
    {
        // skip `INFO`
        var jsonReader = new Utf8JsonReader(buffer.Slice(5));

        var serverInfo = JsonSerializer.Deserialize<ServerInfo>(ref jsonReader);
        if (serverInfo == null) throw new InvalidOperationException(""); // TODO:NatsException
        return serverInfo;
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#msg
    // MSG <subject> <sid> [reply-to] <#bytes>\r\n[payload]
    static (int subscriptionId, int payloadLength) ParseMessageHeader(ReadOnlySpan<byte> msgHeader)
    {
        msgHeader = msgHeader.Slice(4);
        Split(msgHeader, out var subject, out msgHeader); // subject string no use
        Split(msgHeader, out var sid, out msgHeader);
        Split(msgHeader, out var replyToOrBytes, out msgHeader);
        if (msgHeader.Length == 0)
        {
            var subscriptionId = GetInt32(sid);
            var payloadLength = GetInt32(replyToOrBytes);
            return (subscriptionId, payloadLength);
        }
        else
        {
            // TODO:impl this
            var replyTo = msgHeader;
            var bytesSlice = msgHeader;
            throw new NotImplementedException();
        }
    }

    unsafe static (int subscriptionId, int payloadLength) ParseMessageHeader(in ReadOnlySequence<byte> msgHeader)
    {
        if (msgHeader.IsSingleSegment)
        {
            return ParseMessageHeader(msgHeader.FirstSpan);
        }

        // header parsing use Slice frequently so ReadOnlySequence is high cost, should use Span.
        // msgheader is not too long, ok to use stackalloc.
        Span<byte> buffer = stackalloc byte[(int)msgHeader.Length];
        msgHeader.CopyTo(buffer);
        return ParseMessageHeader(buffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Split(ReadOnlySpan<byte> span, out ReadOnlySpan<byte> left, out ReadOnlySpan<byte> right)
    {
        var i = span.IndexOf((byte)' ');
        if (i == -1)
        {
            left = span;
            right = default;
            return;
        }

        left = span.Slice(0, i);
        right = span.Slice(i + 1);
    }

    public async ValueTask DisposeAsync()
    {
        cancellationTokenSource.Cancel();
        await readLoop;
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
