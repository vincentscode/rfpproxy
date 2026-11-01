using System.Threading;
using System.Threading.Tasks;
using RfpProxy.AaMiDe.AaMiDe.Dnm;

namespace RfpProxy.Gigaset.States;

public sealed class ErrorHandsetState(string error, HandsetStateMachine parentStateMachine) : HandsetState
{
    public string Error { get; } = error;
    
    public override string ToString() => $"{nameof(ErrorHandsetState)}({Error})";

    public override async Task ProcessMessage(DnmMessage dnm, MessageContext ctx, CancellationToken cancellationToken)
    {
        // just forward everything as is, forever
        await parentStateMachine.GigasetClient.WriteAsync(ctx.Direction, ctx.MessageId, ctx.Rfp, ctx.Data, cancellationToken);
    }

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}