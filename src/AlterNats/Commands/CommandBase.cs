using AlterNats.Internal;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace AlterNats.Commands;

internal abstract class CommandBase<TSelf> : ICommand, IObjectPoolNode<TSelf>
    where TSelf : class, IObjectPoolNode<TSelf>
{
    protected static ObjectPool<TSelf> pool;

    TSelf? nextNode;
    public ref TSelf? NextNode => ref nextNode;

    public virtual void Return()
    {
        pool.TryPush(Unsafe.As<TSelf>(this));
    }

    public abstract void Write(ILogger logger, ProtocolWriter writer);
}
