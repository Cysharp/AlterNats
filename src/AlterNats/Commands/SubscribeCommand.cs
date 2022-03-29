namespace AlterNats.Commands;

internal sealed class SubscribeCommand : CommandBase<SubscribeCommand>
{
    NatsKey subject;
    int subscriptionId;

    SubscribeCommand()
    {
    }

    public static SubscribeCommand Create(int subscriptionId, in NatsKey subject)
    {
        if (!TryRent(out var result))
        {
            result = new SubscribeCommand();
        }

        result.subject = subject;
        result.subscriptionId = subscriptionId;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        // TODO:QueueGroup?
        writer.WriteSubscribe(subscriptionId, subject, null);
    }

    protected override void Reset()
    {
        subject = default;
        subscriptionId = 0;
    }
}

internal sealed class AsyncSubscribeCommand : AsyncCommandBase<AsyncSubscribeCommand>
{
    NatsKey subject;
    int subscriptionId;

    AsyncSubscribeCommand()
    {
    }

    public static AsyncSubscribeCommand Create(int subscriptionId, in NatsKey subject)
    {
        if (!TryRent(out var result))
        {
            result = new AsyncSubscribeCommand();
        }

        result.subject = subject;
        result.subscriptionId = subscriptionId;

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WriteSubscribe(subscriptionId, subject, null);
    }

    protected override void Reset()
    {
        subject = default;
        subscriptionId = 0;
    }
}
