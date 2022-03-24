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

    public override string WriteTraceMessage => "Write PUB Command to buffer.";

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePublish(subject!, null, value, serializer!);
    }

    public override void Return()
    {
        subject = null;
        value = default;
        serializer = null;
        base.Return();
    }
}
