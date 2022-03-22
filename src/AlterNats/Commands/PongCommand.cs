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

    public override void Write(ILogger logger, ProtocolWriter writer)
    {
        logger.LogTrace("Write PONG Command to buffer.");
        writer.WritePong();
    }
}