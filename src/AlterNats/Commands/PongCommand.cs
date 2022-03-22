using Microsoft.Extensions.Logging;

namespace AlterNats.Commands;

internal sealed class PongCommand : CommandBase<PongCommand>
{
    PongCommand()
    {
    }

    public static PongCommand Create()
    {
        if (!pool.TryPop(out var result))
        {
            result = new PongCommand();
        }
        return result;
    }

    public override string WriteTraceMessage => "Write PONG Command to buffer.";

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePong();
    }
}