using AlterNats.Internal;
using Microsoft.Extensions.Logging;

namespace AlterNats.Commands;

internal interface ICommand
{
    void Return(ObjectPool pool);
    void Write(ProtocolWriter writer);
}

internal interface IBatchCommand : ICommand
{
    new int Write(ProtocolWriter writer);
}
