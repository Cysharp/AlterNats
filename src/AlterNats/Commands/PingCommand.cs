namespace AlterNats.Commands;

internal sealed class PingCommand : CommandBase<PingCommand>
{
    PingCommand()
    {
    }

    public static PingCommand Create()
    {
        if (!pool.TryPop(out var result))
        {
            result = new PingCommand();
        }
        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePing();
    }

    public override void Reset()
    {
    }
}

internal sealed class AsyncPingCommand : AsyncCommandBase<AsyncPingCommand>
{
    AsyncPingCommand()
    {
    }

    public static AsyncPingCommand Create()
    {
        if (!pool.TryPop(out var result))
        {
            result = new AsyncPingCommand();
        }
        return result;
    }

    public override void Reset()
    {
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePing();
    }
}
