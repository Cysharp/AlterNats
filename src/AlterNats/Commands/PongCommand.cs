﻿using AlterNats.Internal;

namespace AlterNats.Commands;

internal sealed class PongCommand : CommandBase<PongCommand>
{
    PongCommand()
    {
    }

    public static PongCommand Create(ObjectPool pool, CancellationTimer timer)
    {
        if (!TryRent(pool, out var result))
        {
            result = new PongCommand();
        }
        result.SetCancellationTimer(timer);

        return result;
    }

    public override void Write(ProtocolWriter writer)
    {
        writer.WritePong();
    }

    protected override void Reset()
    {
    }
}
