namespace AlterNats.Commands;

internal sealed class ConnectCommand : CommandBase<ConnectCommand>
{
    ConnectOptions? connectOptions;

    ConnectCommand()
    {
    }

    public static ConnectCommand Create(ConnectOptions connectOptions)
    {
        if (!pool.TryPop(out var result))
        {
            result = new ConnectCommand();
        }

        result.connectOptions = connectOptions;

        return result;
    }

    public override void Return()
    {
        connectOptions = null;
        base.Return();
    }


    public override void Write(ProtocolWriter writer)
    {
        writer.WriteConnect(connectOptions!);
    }
}
