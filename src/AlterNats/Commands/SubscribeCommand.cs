using Microsoft.Extensions.Logging;

namespace AlterNats.Commands;

internal sealed class SubscribeCommand : CommandBase<SubscribeCommand>
{
    NatsKey? subject;
    int subscriptionId;

    SubscribeCommand()
    {
    }

    public static SubscribeCommand Create(int subscriptionId, NatsKey subject)
    {
        if (!pool.TryPop(out var result))
        {
            result = new SubscribeCommand();
        }

        result.subject = subject;
        result.subscriptionId = subscriptionId;

        return result;
    }

    public override string WriteTraceMessage => throw new NotImplementedException();

    public override void Write(ProtocolWriter writer)
    {
        // logger.LogTrace("Write SUB Command to buffer.");
        writer.WriteSubscribe(subscriptionId, subject!);
    }

    public override void Return()
    {
        subject = null;
        base.Return();
    }
}
