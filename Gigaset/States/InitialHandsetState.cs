using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using RfpProxy.AaMiDe.AaMiDe.Dnm;
using RfpProxy.AaMiDe.AaMiDe.Nwk;
using RfpProxyLib.Messages;

namespace RfpProxy.Gigaset.States;

public class InitialHandsetState(HandsetStateMachine parentStateMachine) : HandsetState
{
    private Task _task;
    private CancellationTokenSource _taskCancellationTokenSource;

    private readonly Channel<(DnmMessage, MessageContext)> _messageChannel = Channel.CreateUnbounded<(DnmMessage, MessageContext)>(new UnboundedChannelOptions
    {
        SingleWriter = false,
        SingleReader = false,
        AllowSynchronousContinuations = true,
    });

    public override Task OnEnter(CancellationToken cancellationToken)
    {
        base.OnEnter(cancellationToken);
        
        _taskCancellationTokenSource = new CancellationTokenSource();
        _task = Task.Run(async () =>
        {
            HandsetState? newState;
            try
            {
                newState = await RunUntilTransitionToOtherState(_taskCancellationTokenSource.Token);
            }
            catch (Exception exception)
            {
                newState = new ErrorHandsetState($"Exception: {exception}", parentStateMachine);
            }

            _task.Dispose();
            _taskCancellationTokenSource.Dispose();
            
            await parentStateMachine.TransitionTo(newState, cancellationToken);
        }, _taskCancellationTokenSource.Token);

        return Task.CompletedTask;
    }

    public override async Task ProcessMessage(DnmMessage dnm, MessageContext ctx, CancellationToken cancellationToken)
    {
        await _messageChannel.Writer.WriteAsync((dnm, ctx), cancellationToken);
    }
    
    private async Task InjectMessage(MessageDirection direction, object message)
    {
        await parentStateMachine.GigasetClient.WriteAsync(ctx.Direction, ctx.MessageId, ctx.Rfp, ctx.Data, cancellationToken);
    }

    private async Task<DnmMessage> ExpectMessage(MessageDirection expectedDirection, Func<DnmMessage, bool> messagePredicate, CancellationToken cancellationToken)
    {
        var (dnm, ctx) = await _messageChannel.Reader.ReadAsync(cancellationToken);

        // forward the original message
        await parentStateMachine.GigasetClient.WriteAsync(ctx.Direction, ctx.MessageId, ctx.Rfp, ctx.Data, cancellationToken);
        
        var matches = ctx.Direction == expectedDirection && messagePredicate.Invoke(dnm);
        if (matches)
        {
            return dnm;
        }
        
        // TODO: handle unexpected messages properly
        throw new InvalidOperationException("unexpected message");
    }

    private async Task<HandsetState> RunUntilTransitionToOtherState(CancellationToken cancellationToken)
    {
        // PP->FP - MM: Locate Request
        var locateRequest = await ExpectMessage(
            MessageDirection.FromPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkMMPayload { Type: NwkMMMessageType.LocateRequest } },
            cancellationToken
        );
        // PP<-FP - MM: Auth Request
        var authRequest = await ExpectMessage(
            MessageDirection.ToPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkMMPayload { Type: NwkMMMessageType.AuthenticationRequest } },
            cancellationToken
        );
        // PP->FP - MM: Auth Reply
        var authReply = await ExpectMessage(
            MessageDirection.FromPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkMMPayload { Type: NwkMMMessageType.AuthenticationReply } },
            cancellationToken
        );
        // PP<-FP - MM: Locate Accept
        var locateAccept = await ExpectMessage(
            MessageDirection.ToPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkMMPayload { Type: NwkMMMessageType.LocateAccept } },
            cancellationToken
        );

        // PP->FP - MM: Temp. Identity Assign Ack.
        var tempIdentityAssignAck = await ExpectMessage(
            MessageDirection.FromPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkMMPayload { Type: NwkMMMessageType.TemporaryIdentityAssignAck } },
            cancellationToken
        );

        // PP<-FP - CC: Facility
        await InjectMessage(MessageDirection.ToPp, null!);
        // PP->FP - CISS: Register (<<ESCAPE-TO-PROPRIETARY>>: <<CISS-ACKNOWLEDGEMENT-SEQ>>)
        var cissRegister = await ExpectMessage(MessageDirection.FromPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkCISSPayload { Type: NwkCISSMessageType.CISSRegister } },
            cancellationToken);
        // TODO: i think the OMM may reply with something here which we need to drop
        // PP->FP - CISS: Release Connection
        var cissReleaseCom = await ExpectMessage(MessageDirection.FromPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkCISSPayload { Type: NwkCISSMessageType.CISSReleaseCom } },
            cancellationToken);

        return new IdleHandsetState(parentStateMachine);
    }
}