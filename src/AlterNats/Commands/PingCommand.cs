namespace AlterNats.Commands;

internal sealed class PingCommand : CommandBase<PingCommand>
{
    PingCommand()
    {
    }

    public static PingCommand Create()
    {
        if (!TryRent(out var result))
        {
            result = new PingCommand();
        }
        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePing();
    }

    protected override void Reset()
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
        if (!TryRent(out var result))
        {
            result = new AsyncPingCommand();
        }
        return result;
    }

    protected override void Reset()
    {
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePing();
    }
}
