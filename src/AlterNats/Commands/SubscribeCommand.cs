namespace AlterNats.Commands;

internal sealed class SubscribeCommand : CommandBase<SubscribeCommand>
{
    NatsKey? subject;
    int subscriptionId;

    SubscribeCommand()
    {
    }

    public static SubscribeCommand Create(int subscriptionId, string subject)
    {
        if (!TryRent(out var result))
        {
            result = new SubscribeCommand();
        }

        result.subject = new NatsKey(subject); // TODO:use specified overload.
        result.subscriptionId = subscriptionId;

        return result;
    }

    public static SubscribeCommand Create(int subscriptionId, NatsKey subject)
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
        writer.WriteSubscribe(subscriptionId, subject!);
    }

    protected override void Reset()
    {
        subject = null;
    }
}

internal sealed class AsyncSubscribeCommand : AsyncCommandBase<AsyncSubscribeCommand>
{
    NatsKey? subject;
    int subscriptionId;

    AsyncSubscribeCommand()
    {
    }

    public static AsyncSubscribeCommand Create(int subscriptionId, string subject)
    {
        if (!TryRent(out var result))
        {
            result = new AsyncSubscribeCommand();
        }

        result.subject = new NatsKey(subject); // TODO:use specified overload.
        result.subscriptionId = subscriptionId;

        return result;
    }

    public static AsyncSubscribeCommand Create(int subscriptionId, NatsKey subject)
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
        writer.WriteSubscribe(subscriptionId, subject!);
    }

    protected override void Reset()
    {
        subject = null;
    }
}
