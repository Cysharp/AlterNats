namespace AlterNats.Commands;

internal sealed class PongCommand : CommandBase<PongCommand>
{
    PongCommand()
    {
    }

    public static PongCommand Create()
    {
        if (!pool.TryDequeue(out var result))
        {
            result = new PongCommand();
        }
        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePong();
    }

    public override void Reset()
    {
    }
}
