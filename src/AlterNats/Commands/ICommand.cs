using Microsoft.Extensions.Logging;

namespace AlterNats.Commands;

// https://docs.nats.io/reference/reference-protocols/nats-protocol

internal interface ICommand
{
    void Return();
    void Write(ProtocolWriter writer);
}

internal interface IBatchCommand : ICommand
{
    new int Write(ProtocolWriter writer);
}
