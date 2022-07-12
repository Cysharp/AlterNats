using AlterNats.Internal;

namespace AlterNats.Commands;

internal sealed class AsyncConnectCommand : AsyncCommandBase<AsyncConnectCommand>
{
    ConnectOptions? connectOptions;

    AsyncConnectCommand()
    {
    }

    public static AsyncConnectCommand Create(ObjectPool pool, ConnectOptions connectOptions, CancellationTimer timer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new AsyncConnectCommand();
        }

        result.connectOptions = connectOptions;
        result.SetCancellationTimer(timer);

        return result;
    }

    protected override void Reset()
    {
        connectOptions = null;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WriteConnect(connectOptions!);
    }
}
