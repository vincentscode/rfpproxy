using System;
using System.Threading;
using System.Threading.Tasks;
using RfpProxy.AaMiDe.AaMiDe.Dnm;
using RfpProxy.AaMiDe.AaMiDe.Nwk;
using RfpProxyLib.Messages;

namespace RfpProxy.Gigaset;

public class AAAInitialHandsetState
{
    public enum MessageHandling
    {
        Drop,
        Modify,
        Forward,
    }

    public enum UnexpectedMessageHandling
    {
        Ignore,
        TransitionToFailureState,
    }

    public enum StepResult
    {
        StayOnThisStep,
        GoToNextStep,
        GoToFailureState,
    }

    public abstract class Step
    {
        public virtual Task<StepResult> ProcessMessage(MessageDirection direction, DnmMessage dnm, CancellationToken cancellationToken);
    }

    public sealed class ExpectMessageStep(MessageDirection expectedDirection, Func<DnmMessage, bool> messagePredicate, MessageHandling messageHandling, UnexpectedMessageHandling unexpectedMessageHandling) : Step
    {
        public override async Task<StepResult> ProcessMessage(MessageDirection direction, DnmMessage dnm, CancellationToken cancellationToken)
        {
            // TODO: do message handling (probably in base?)
            base.ProcessMessage(direction,  dnm, cancellationToken);
            
            var matches = direction == expectedDirection && messagePredicate.Invoke(dnm);
            if (matches)
            {
                return StepResult.GoToNextStep;
            }
            
            return unexpectedMessageHandling switch
            {
                UnexpectedMessageHandling.Ignore => StepResult.StayOnThisStep,
                UnexpectedMessageHandling.TransitionToFailureState => StepResult.GoToFailureState,
                _ => throw new ArgumentOutOfRangeException(nameof(unexpectedMessageHandling), unexpectedMessageHandling, null)
            };
        }
    }
    
    private readonly Step[] _steps = [
        // PP->FP - MM: Locate Request
        new ExpectMessageStep(
            MessageDirection.FromPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkMMPayload { Type: NwkMMMessageType.LocateRequest } },
            MessageHandling.Forward,
            UnexpectedMessageHandling.Ignore
        ),
        
    ];

    private int _stepIndex = 0;
    
    public async Task Run()
    {
        // PP->FP - MM: Locate Request
        var locateRequest = await ExpectMessage(MessageDirection.FromPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkMMPayload { Type: NwkMMMessageType.LocateRequest } });
        // PP<-FP - MM: Auth Request
        var authRequest = await ExpectMessage(MessageDirection.ToPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkMMPayload { Type: NwkMMMessageType.AuthenticationRequest } });
        // PP->FP - MM: Auth Reply
        var authReply = await ExpectMessage(MessageDirection.FromPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkMMPayload { Type: NwkMMMessageType.AuthenticationReply } });
        // PP<-FP - MM: Locate Accept
        var locateAccept = await ExpectMessage(MessageDirection.ToPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkMMPayload { Type: NwkMMMessageType.LocateAccept } });

        // PP->FP - MM: Temp. Identity Assign Ack.
        var tempIdentityAssignAck = await ExpectMessage(MessageDirection.FromPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkMMPayload { Type: NwkMMMessageType.TemporaryIdentityAssignAck } });

        // PP<-FP - CC: Facility
        await InjectMessage(MessageDirection.ToPp, null!);
        // PP->FP - CISS: Register (<<ESCAPE-TO-PROPRIETARY>>: <<CISS-ACKNOWLEDGEMENT-SEQ>>)
        var cissRegister = await ExpectMessage(MessageDirection.FromPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkCISSPayload { Type: NwkCISSMessageType.CISSRegister } });
        // TODO: i think the OMM may reply with something here which we need to drop
        // PP->FP - CISS: Release Connection
        var cissReleaseCom = await ExpectMessage(MessageDirection.FromPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkCISSPayload { Type: NwkCISSMessageType.CISSReleaseCom } });

        TransitionTo(/* TODO: Idle State */);
    }

    private void TransitionTo()
    {
        throw new NotImplementedException();
    }

    private async Task InjectMessage(MessageDirection direction, object message)
    {
        throw new NotImplementedException();
    }

    private async Task<DnmMessage> ExpectMessage(MessageDirection direction, Func<DnmMessage, bool> messagePredicate)
    {
        throw new NotImplementedException();
    }
}