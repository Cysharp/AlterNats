using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AlterNats;

internal sealed class NatsStreamReader : IAsyncDisposable
{
    readonly Stream stream;
    readonly NatsConnection connection;
    readonly PipeReader pipeReader;
    readonly Task readLoop;
    readonly CancellationTokenSource cancellationTokenSource; // TODO:no need it?
    readonly ILogger<NatsStreamReader> logger;

    public NatsStreamReader(Stream stream, NatsConnection connection)
    {
        this.stream = stream;
        this.connection = connection;
        this.logger = connection.Options.LoggerFactory.CreateLogger<NatsStreamReader>();
        this.cancellationTokenSource = new CancellationTokenSource();
        this.pipeReader = PipeReader.Create(stream);
        this.readLoop = Task.Run(ReadLoop);
    }

    async Task ReadLoop()
    {
        while (true)
        {
            // TODO:read more if buffer is not filled???
            var readResult = await pipeReader.ReadAsync();
            if (readResult.IsCompleted)
            {
                return;
            }

            var buffer = readResult.Buffer;
            var code = (buffer.FirstSpan.Length >= 4)
                ? GetCode(buffer.FirstSpan)
                : GetCode(buffer);

            var position = DispatchCommand(code, buffer);
            pipeReader.AdvanceTo(position);
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int GetCode(ReadOnlySpan<byte> span)
    {
        return Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference<byte>(span));
    }

    static int GetCode(ReadOnlySequence<byte> sequence)
    {
        Span<byte> buf = stackalloc byte[4];
        sequence.Slice(0, 4).CopyTo(buf);
        return GetCode(buf);
    }

    SequencePosition DispatchCommand(int code, ReadOnlySequence<byte> buffer)
    {
        if (code == ServerOpCodes.Msg)
        {
            logger.LogTrace("Receive Msg");

            // TODO:no.
        }
        else if (code == ServerOpCodes.Ping)
        {
            logger.LogTrace("Receive Ping");

            connection.Pong(); // return pong
            return buffer.GetPosition(6); // PING\r\n
        }
        else if (code == ServerOpCodes.Pong)
        {
            logger.LogTrace("Receive Pong");

            return buffer.GetPosition(6); // PONG\r\n
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
            return buffer.GetPosition(5); // +OK\r\n
        }
        else if (code == ServerOpCodes.Info)
        {
            logger.LogTrace("Receive Info");

            var info = ParseInfo(buffer, out var position);
            

            buffer = buffer.Slice(position);
        }
        else
        {
            // TODO:reaches invalid line.

            var s = Encoding.UTF8.GetString(buffer.Slice(0, 4).FirstSpan);
            Console.WriteLine("NANIDEMO NAI!?:" + s);
        }

        var finalPosition = buffer.PositionOf((byte)'\n');
        if (finalPosition == null)
        {
            throw new Exception(); // TODO
        }

        return buffer.GetPosition(1, finalPosition.Value);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#info
    // INFO {["option_name":option_value],...}
    internal static ServerInfo ParseInfo(ReadOnlySequence<byte> data, out SequencePosition readPosition)
    {
        // skip `INFO`
        var jsonReader = new Utf8JsonReader(data.Slice(5));

        var serverInfo = JsonSerializer.Deserialize<ServerInfo>(ref jsonReader);
        if (serverInfo == null) throw new InvalidOperationException(""); // TODO:NatsException

        readPosition = data.GetPosition(4 + jsonReader.BytesConsumed);
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
        await this.pipeReader.CompleteAsync().ConfigureAwait(false);
        await readLoop.ConfigureAwait(false);
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