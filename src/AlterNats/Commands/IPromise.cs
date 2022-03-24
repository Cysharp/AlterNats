namespace AlterNats.Commands;

internal interface IPromise
{
    void SetCanceled(CancellationToken cancellationToken);
    void SetException(Exception exception);
}
