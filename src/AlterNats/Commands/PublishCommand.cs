namespace AlterNats.Commands;

internal sealed class PublishCommand<T> : CommandBase<PublishCommand<T>>
{
    NatsKey? subject;
    int subscriptionId;
    T? value;
    INatsSerializer? serializer;

    PublishCommand()
    {
    }

    // TODO:queue-group

    public static PublishCommand<T> Create(int subscriptionId, string subject)
    {
        if (!pool.TryPop(out var result))
        {
            result = new PublishCommand<T>();
        }

        result.subject = new NatsKey(subject); // TODO:use specified overload.
        result.subscriptionId = subscriptionId;
        // TODO:set serializer and T value

        return result;
    }

    public static PublishCommand<T> Create(int subscriptionId, NatsKey subject)
    {
        if (!pool.TryPop(out var result))
        {
            result = new PublishCommand<T>();
        }

        result.subject = subject;
        result.subscriptionId = subscriptionId;

        return result;
    }

    public override string WriteTraceMessage => "Write PUB Command to buffer.";

    public override void Write(ProtocolWriter writer)
    {
        // TODO: WritePublish
        //writer.WritePublish(subject,);
    }

    public override void Return()
    {
        subject = null;
        base.Return();
    }
}
