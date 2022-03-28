namespace AlterNats.Commands;

internal sealed class DirectWriteCommand : ICommand
{
    readonly string protocol;

    public DirectWriteCommand(string protocol)
    {
        this.protocol = protocol;
    }

    public void Return()
    {
    }

    public void Write(ProtocolWriter writer)
    {
        writer.WriteRaw(protocol);
    }
}
