#pragma warning disable VSTHRD200 // Use "Async" suffix for async methods

using AlterNats.Internal;
using Microsoft.Extensions.Logging;

namespace AlterNats.Commands;

internal interface ICommand
{
    bool IsCanceled { get; }
    void SetCancellationTimer(CancellationTimer timer);
    void Return(ObjectPool pool);
    void Write(ProtocolWriter writer);
}

internal interface IAsyncCommand : ICommand
{
    ValueTask AsValueTask();
}

internal interface IAsyncCommand<T> : ICommand
{
    ValueTask<T> AsValueTask();
}

internal interface IBatchCommand : ICommand
{
    new int Write(ProtocolWriter writer);
}
