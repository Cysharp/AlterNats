namespace AlterNats.Commands;

internal sealed class AsyncConnectCommand : AsyncCommandBase<AsyncConnectCommand>
{
    ConnectOptions? connectOptions;

    AsyncConnectCommand()
    {
    }

    public static AsyncConnectCommand Create(ConnectOptions connectOptions)
    {
        if (!TryRent(out var result))
        {
            result = new AsyncConnectCommand();
        }

        result.connectOptions = connectOptions;

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
