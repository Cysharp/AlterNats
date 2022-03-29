namespace AlterNats.Commands;

internal sealed class SubscribeCommand : CommandBase<SubscribeCommand>
{
    NatsKey subject;
    NatsKey? queueGroup;
    int subscriptionId;

    SubscribeCommand()
    {
    }

    public static SubscribeCommand Create(int subscriptionId, in NatsKey subject, in NatsKey? queueGroup)
    {
        if (!TryRent(out var result))
        {
            result = new SubscribeCommand();
        }

        result.subject = subject;
        result.subscriptionId = subscriptionId;
        result.queueGroup = queueGroup;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WriteSubscribe(subscriptionId, subject, queueGroup);
    }

    protected override void Reset()
    {
        subject = default;
        queueGroup = default;
        subscriptionId = 0;
    }
}

internal sealed class AsyncSubscribeCommand : AsyncCommandBase<AsyncSubscribeCommand>
{
    NatsKey subject;
    NatsKey? queueGroup;
    int subscriptionId;

    AsyncSubscribeCommand()
    {
    }

    public static AsyncSubscribeCommand Create(int subscriptionId, in NatsKey subject, in NatsKey? queueGroup)
    {
        if (!TryRent(out var result))
        {
            result = new AsyncSubscribeCommand();
        }

        result.subject = subject;
        result.subscriptionId = subscriptionId;
        result.queueGroup = queueGroup;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WriteSubscribe(subscriptionId, subject, queueGroup);
    }

    protected override void Reset()
    {
        subject = default;
        queueGroup = default;
        subscriptionId = 0;
    }
}
