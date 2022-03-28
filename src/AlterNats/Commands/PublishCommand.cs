using System.Threading.Tasks.Sources;

namespace AlterNats.Commands;

internal sealed class PublishCommand<T> : CommandBase<PublishCommand<T>>
{
    NatsKey? subject;
    T? value;
    INatsSerializer? serializer;

    PublishCommand()
    {
    }

    // TODO:reply-to

    public static PublishCommand<T> Create(string subject, T? value, INatsSerializer serializer)
    {
        if (!pool.TryPop(out var result))
        {
            result = new PublishCommand<T>();
        }

        result.subject = new NatsKey(subject); // TODO:use specified overload.
        result.value = value;
        result.serializer = serializer;

        return result;
    }

    public static PublishCommand<T> Create(NatsKey subject, T? value, INatsSerializer serializer)
    {
        if (!pool.TryPop(out var result))
        {
            result = new PublishCommand<T>();
        }

        result.subject = subject;
        result.value = value;
        result.serializer = serializer;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(subject!, null, value, serializer!);
    }

    public override void Reset()
    {
        subject = null;
        value = default;
        serializer = null;
    }
}

internal sealed class AsyncPublishCommand<T> : AsyncCommandBase<AsyncPublishCommand<T>>
{
    NatsKey? subject;
    T? value;
    INatsSerializer? serializer;

    AsyncPublishCommand()
    {
    }

    // TODO:reply-to

    public static AsyncPublishCommand<T> Create(string subject, T? value, INatsSerializer serializer)
    {
        if (!pool.TryPop(out var result))
        {
            result = new AsyncPublishCommand<T>();
        }

        result.subject = new NatsKey(subject); // TODO:use specified overload.
        result.value = value;
        result.serializer = serializer;

        return result;
    }

    public static AsyncPublishCommand<T> Create(NatsKey subject, T? value, INatsSerializer serializer)
    {
        if (!pool.TryPop(out var result))
        {
            result = new AsyncPublishCommand<T>();
        }

        result.subject = subject;
        result.value = value;
        result.serializer = serializer;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(subject!, null, value, serializer!);
    }

    public override void Reset()
    {
        subject = null;
        value = default;
        serializer = null;
    }
}



// TODO:Async Impl
internal sealed class PublishBytesCommand : CommandBase<PublishBytesCommand>
{
    string? stringSubject;
    string? stringReplyTo;
    NatsKey? encodedSubject;
    NatsKey? encodedReplyTo;
    ReadOnlyMemory<byte> value;

    PublishBytesCommand()
    {
    }

    public static PublishBytesCommand Create(string subject, string? replyTo, ReadOnlyMemory<byte> value)
    {
        if (!pool.TryPop(out var result))
        {
            result = new PublishBytesCommand();
        }

        result.stringSubject = subject;
        result.stringReplyTo = replyTo;
        result.value = value;

        return result;
    }

    public static PublishBytesCommand Create(NatsKey subject, NatsKey? replyTo, byte[] value, INatsSerializer serializer)
    {
        if (!pool.TryPop(out var result))
        {
            result = new PublishBytesCommand();
        }

        result.encodedSubject = subject;
        result.encodedReplyTo = replyTo;
        result.value = value;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        if (stringSubject != null)
        {
            writer.WritePublish(stringSubject, stringReplyTo, value.Span);
        }
        else
        {
            writer.WritePublish(encodedSubject!, encodedReplyTo, value.Span);
        }
    }

    public override void Reset()
    {
        stringSubject = null;
        stringReplyTo = null;
        encodedSubject = null;
        encodedReplyTo = null;
        value = default;
    }
}
