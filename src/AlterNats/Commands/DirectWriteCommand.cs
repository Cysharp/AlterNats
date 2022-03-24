namespace AlterNats.Commands;

internal sealed class DirectWriteCommand : ICommand
{
    public string WriteTraceMessage => "Write DIRECT Command to buffer for Debugging.";

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
