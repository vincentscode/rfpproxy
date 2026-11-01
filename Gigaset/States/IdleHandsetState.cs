using System;
using System.Threading;
using System.Threading.Tasks;
using RfpProxy.AaMiDe.AaMiDe.Dnm;

namespace RfpProxy.Gigaset.States;

public sealed class IdleHandsetState(HandsetStateMachine parentStateMachine) : HandsetState
{
    public override string ToString() => $"{nameof(IdleHandsetState)}()";
    
    public override async Task ProcessMessage(DnmMessage dnm, MessageContext ctx, CancellationToken cancellationToken)
    {
        // just forward everything as is, for now
        Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} [IdleHandsetState] Forwarding message");
        await parentStateMachine.GigasetClient.WriteAsync(ctx.Direction, ctx.MessageId, ctx.Rfp, ctx.Data, cancellationToken);
    }

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}