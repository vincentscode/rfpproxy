using System.Threading;
using System.Threading.Tasks;
using RfpProxy.AaMiDe.AaMiDe.Dnm;

namespace RfpProxy.Gigaset;

public abstract class HandsetState
{
    public virtual Task OnEnter(CancellationToken cancellationToken) => Task.CompletedTask;

    public abstract Task ProcessMessage(DnmMessage dnm, MessageContext ctx, CancellationToken cancellationToken);

}