using AlterNats.Internal;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlterNats.Commands;

internal sealed class ProtocolWriter
{
    const int MaxIntStringLength = 10; // int.MaxValue.ToString().Length
    const int NewLineLength = 2; // \r\n

    readonly FixedArrayBufferWriter writer; // where T : IBufferWriter<byte>

    public ProtocolWriter(FixedArrayBufferWriter writer)
    {
        this.writer = writer;
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#connect
    // CONNECT {["option_name":option_value],...}
    public void WriteConnect(ConnectOptions options)
    {
        WriteConstant(CommandConstants.ConnectWithPadding);

        var jsonWriter = new Utf8JsonWriter(writer);
        JsonSerializer.Serialize(jsonWriter, options, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        WriteConstant(CommandConstants.NewLine);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#ping-pong
    public void WritePing()
    {
        WriteConstant(CommandConstants.PingNewLine);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#ping-pong
    public void WritePong()
    {
        WriteConstant(CommandConstants.PongNewLine);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#pub
    // PUB <subject> [reply-to] <#bytes>\r\n[payload]

    public void WritePublish(NatsKey subject, NatsKey? replyTo, ReadOnlySpan<byte> payload)
    {
        var offset = 0;
        var maxLength = CommandConstants.PubWithPadding.Length
            + subject.buffer.Length
            + (replyTo == null ? 0 : replyTo.buffer.Length)
            + MaxIntStringLength
            + NewLineLength
            + payload.Length
            + NewLineLength;

        var writableSpan = writer.GetSpan(maxLength);

        CommandConstants.PubWithPadding.CopyTo(writableSpan);
        offset += CommandConstants.PubWithPadding.Length;

        subject.buffer.AsSpan().CopyTo(writableSpan.Slice(offset));
        offset += subject.buffer.Length;

        if (replyTo != null)
        {
            replyTo.buffer.AsSpan().CopyTo(writableSpan.Slice(offset));
            offset += replyTo.buffer.Length;
        }

        if (!Utf8Formatter.TryFormat(payload.Length, writableSpan.Slice(offset), out var written))
        {
            throw new Exception(); // TODO: exception
        }
        offset += written;

        CommandConstants.NewLine.CopyTo(writableSpan.Slice(offset));
        offset += CommandConstants.NewLine.Length;

        if (payload.Length == 0)
        {
            writer.Advance(offset);
            return;
        }

        payload.CopyTo(writableSpan.Slice(offset));
        offset += payload.Length;

        CommandConstants.NewLine.CopyTo(writableSpan.Slice(offset));
        offset += CommandConstants.NewLine.Length;

        writer.Advance(offset);
    }

    public void WritePublish(string subject, string? replyTo, ReadOnlySpan<byte> payload)
    {
        var offset = 0;
        var maxLength = CommandConstants.PubWithPadding.Length
            + subject.Length + 1
            + (replyTo == null ? 0 : replyTo.Length + 1)
            + MaxIntStringLength
            + NewLineLength
            + payload.Length
            + NewLineLength;

        var writableSpan = writer.GetSpan(maxLength);

        CommandConstants.PubWithPadding.CopyTo(writableSpan);
        offset += CommandConstants.PubWithPadding.Length;

        Encoding.ASCII.GetBytes(subject.AsSpan(), writableSpan.Slice(offset));
        offset += subject.Length;
        writableSpan.Slice(offset)[0] = (byte)' ';
        offset += 1;

        if (replyTo != null)
        {
            Encoding.ASCII.GetBytes(replyTo.AsSpan(), writableSpan.Slice(offset));
            offset += replyTo.Length;
            writableSpan.Slice(offset)[0] = (byte)' ';
            offset += 1;
        }

        if (!Utf8Formatter.TryFormat(payload.Length, writableSpan.Slice(offset), out var written))
        {
            throw new Exception(); // TODO: exception
        }
        offset += written;

        CommandConstants.NewLine.CopyTo(writableSpan.Slice(offset));
        offset += CommandConstants.NewLine.Length;

        if (payload.Length == 0)
        {
            writer.Advance(offset);
            return;
        }

        payload.CopyTo(writableSpan.Slice(offset));
        offset += payload.Length;

        CommandConstants.NewLine.CopyTo(writableSpan.Slice(offset));
        offset += CommandConstants.NewLine.Length;

        writer.Advance(offset);
    }

    // TODO: string subject, string replyTo

    public void WritePublish<T>(NatsKey subject, NatsKey? replyTo, T? value, INatsSerializer serializer)
    {
        var offset = 0;
        var maxLengthWithoutPayload = CommandConstants.PubWithPadding.Length
            + subject.buffer.Length
            + (replyTo == null ? 0 : replyTo.buffer.Length)
            + MaxIntStringLength
            + NewLineLength;

        var writableSpan = writer.GetSpan(maxLengthWithoutPayload);

        CommandConstants.PubWithPadding.CopyTo(writableSpan);
        offset += CommandConstants.PubWithPadding.Length;

        subject.buffer.AsSpan().CopyTo(writableSpan.Slice(offset));
        offset += subject.buffer.Length;

        if (replyTo != null)
        {
            replyTo.buffer.AsSpan().CopyTo(writableSpan.Slice(offset));
            offset += replyTo.buffer.Length;
        }

        // Advance for written.
        writer.Advance(offset);

        // preallocate range for write #bytes(write after serialized)
        var preallocatedRange = writer.PreAllocate(MaxIntStringLength);
        offset += MaxIntStringLength;

        CommandConstants.NewLine.CopyTo(writableSpan.Slice(offset));
        writer.Advance(CommandConstants.NewLine.Length);

        var payloadLength = serializer.Serialize(writer, value);
        var payloadLengthSpan = writer.GetSpanInPreAllocated(preallocatedRange);
        payloadLengthSpan.Fill((byte)' ');
        if (!Utf8Formatter.TryFormat(payloadLength, payloadLengthSpan, out var written))
        {
            throw new Exception(); // TODO: exception
        }

        WriteConstant(CommandConstants.NewLine);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#sub
    // SUB <subject> [queue group] <sid>
    public void WriteSubscribe(int subscriptionId, NatsKey subject) => WriteSubscribeCore(subscriptionId, subject, null);
    public void WriteSubscribe(int subscriptionId, string subject) => WriteSubscribeCore(subscriptionId, subject, null);
    public void WriteSubscribe(int subscriptionId, NatsKey subject, NatsKey queueGroup) => WriteSubscribeCore(subscriptionId, subject, queueGroup);
    public void WriteSubscribe(int subscriptionId, string subject, string queueGroup) => WriteSubscribeCore(subscriptionId, subject, queueGroup);

    void WriteSubscribeCore(int sid, NatsKey subject, NatsKey? queueGroup)
    {
        var offset = 0;

        var maxLength = CommandConstants.SubWithPadding.Length
            + subject.buffer.Length
            + (queueGroup == null ? 0 : queueGroup.buffer.Length)
            + MaxIntStringLength
            + NewLineLength; // newline

        var writableSpan = writer.GetSpan(maxLength);
        CommandConstants.SubWithPadding.CopyTo(writableSpan);
        offset += CommandConstants.SubWithPadding.Length;

        subject.buffer.AsSpan().CopyTo(writableSpan.Slice(offset));
        offset += subject.buffer.Length;

        if (queueGroup != null)
        {
            queueGroup.buffer.AsSpan().CopyTo(writableSpan.Slice(offset));
            offset += queueGroup.buffer.Length;
        }

        if (!Utf8Formatter.TryFormat(sid, writableSpan.Slice(offset), out var written))
        {
            throw new Exception(); // TODO: exception
        }
        offset += written;

        CommandConstants.NewLine.CopyTo(writableSpan.Slice(offset));
        offset += CommandConstants.NewLine.Length;

        writer.Advance(offset);
    }

    void WriteSubscribeCore(int sid, string subject, string? queueGroup)
    {
        var offset = 0;

        var maxLength = CommandConstants.SubWithPadding.Length
            + subject.Length + 1
            + (queueGroup == null ? 0 : queueGroup.Length + 1)
            + MaxIntStringLength
            + NewLineLength; // newline

        var writableSpan = writer.GetSpan(maxLength);
        CommandConstants.SubWithPadding.CopyTo(writableSpan);
        offset += CommandConstants.SubWithPadding.Length;

        Encoding.ASCII.GetBytes(subject.AsSpan(), writableSpan.Slice(offset));
        offset += subject.Length;
        writableSpan.Slice(offset)[0] = (byte)' ';
        offset += 1;

        if (queueGroup != null)
        {
            Encoding.ASCII.GetBytes(queueGroup.AsSpan(), writableSpan.Slice(offset));
            offset += queueGroup.Length;
            writableSpan.Slice(offset)[0] = (byte)' ';
            offset += 1;
        }

        if (!Utf8Formatter.TryFormat(sid, writableSpan.Slice(offset), out var written))
        {
            throw new Exception(); // TODO: exception
        }
        offset += written;

        CommandConstants.NewLine.CopyTo(writableSpan.Slice(offset));
        offset += CommandConstants.NewLine.Length;

        writer.Advance(offset);
    }

    // https://docs.nats.io/reference/reference-protocols/nats-protocol#unsub
    // UNSUB <sid> [max_msgs]
    public void WriteUnsubscribe(int subscriptionId, int? maxMessages)
    {
        var offset = 0;
        var maxLength = CommandConstants.UnsubWithPadding.Length
            + MaxIntStringLength
            + ((maxMessages != null) ? (1 + MaxIntStringLength) : 0)
            + NewLineLength;

        var writableSpan = writer.GetSpan(maxLength);
        CommandConstants.UnsubWithPadding.CopyTo(writableSpan);
        offset += CommandConstants.UnsubWithPadding.Length;

        if (!Utf8Formatter.TryFormat(subscriptionId, writableSpan.Slice(offset), out var written))
        {
            throw new Exception(); // TODO: exception
        }
        offset += written;

        if (maxMessages != null)
        {
            writableSpan.Slice(offset)[0] = (byte)' ';
            offset += 1;
            if (!Utf8Formatter.TryFormat(maxMessages.Value, writableSpan.Slice(offset), out written))
            {
                throw new Exception(); // TODO: exception
            }
            offset += written;
        }

        CommandConstants.NewLine.CopyTo(writableSpan.Slice(offset));
        offset += CommandConstants.NewLine.Length;

        writer.Advance(offset);
    }

    internal void WriteRaw(string protocol)
    {
        var encoded = Encoding.UTF8.GetBytes(protocol + "\r\n");
        var span = writer.GetSpan(encoded.Length);
        encoded.CopyTo(span);
        writer.Advance(encoded.Length);
    }

    void WriteConstant(ReadOnlySpan<byte> constant)
    {
        var writableSpan = writer.GetSpan(constant.Length);
        constant.CopyTo(writableSpan);
        writer.Advance(constant.Length);
    }
}
