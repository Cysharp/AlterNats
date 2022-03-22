using Microsoft.Extensions.Logging;

namespace AlterNats.Commands;

// https://docs.nats.io/reference/reference-protocols/nats-protocol

internal interface ICommand
{
    void Return();
    void Write(ProtocolWriter writer);
    string WriteTraceMessage { get; }
}
