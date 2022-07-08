using AlterNats.Internal;

namespace AlterNats.Commands;

internal sealed class PingCommand : CommandBase<PingCommand>
{
    PingCommand()
    {
    }

    public static PingCommand Create(ObjectPool pool, CancellationTimerPool timer, CancellationToken cancellationToken)
    {
        if (!TryRent(pool, out var result))
        {
            result = new PingCommand();
        }
        result.SetCanceler(timer, cancellationToken);
        
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

    public static AsyncPingCommand Create(NatsConnection connection, ObjectPool pool, CancellationTimerPool timer, CancellationToken cancellationToken)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncPingCommand();
        }
        result.connection = connection;
        result.SetCanceler(timer, cancellationToken);

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
