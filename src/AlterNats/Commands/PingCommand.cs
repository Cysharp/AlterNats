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

internal sealed class AsyncPingCommand : AsyncCommandBase<AsyncPingCommand, TimeSpan>
{
    public DateTimeOffset? WriteTime { get; private set; }
    NatsConnection? connection;

    AsyncPingCommand()
    {
    }

    public static AsyncPingCommand Create(NatsConnection connection)
    {
        if (!TryRent(out var result))
        {
            result = new AsyncPingCommand();
        }
        result.connection = connection;

        return result;
    }

    protected override void Reset()
    {
        WriteTime = null;
        connection = null;
    }

    public override void Write(ProtocolWriter writer)
    {
        WriteTime = DateTimeOffset.UtcNow;
        connection!.EnqueuePing(this);
        writer.WritePing();
    }
}
