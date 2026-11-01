using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using RfpProxy.AaMiDe.AaMiDe.Dnm;
using RfpProxy.AaMiDe.AaMiDe.Nwk;
using RfpProxy.Gigaset.Helpers;
using RfpProxyLib.Messages;

namespace RfpProxy.Gigaset.States;

public sealed class InitialHandsetState(HandsetStateMachine parentStateMachine) : HandsetState
{
    private Task? _task;
    private CancellationTokenSource? _taskCancellationTokenSource;

    private readonly Channel<(DnmMessage, MessageContext)> _messageChannel = Channel.CreateUnbounded<(DnmMessage, MessageContext)>(new UnboundedChannelOptions
    {
        SingleWriter = false,
        SingleReader = false,
        AllowSynchronousContinuations = true,
    });

    private (DnmMessage dnm, MessageContext ctx) _lastMessageWithContext;

    public override string ToString() => $"{nameof(InitialHandsetState)}()";

    public override Task OnEnter(CancellationToken cancellationToken)
    {
        base.OnEnter(cancellationToken);
     
        Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} [InitialHandsetState({parentStateMachine.ConnectionPMID})] OnEnter");
        
        _taskCancellationTokenSource = new CancellationTokenSource();
        _task = Task.Run(async () =>
        {
            HandsetState? newState;
            try
            {
                newState = await RunUntilTransitionToOtherState(_taskCancellationTokenSource.Token);
                Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} [InitialHandsetState({parentStateMachine.ConnectionPMID})] _task -> newState");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} [InitialHandsetState({parentStateMachine.ConnectionPMID})] _task -> OperationCanceledException");
                // just die please
                return;
            }
            catch (Exception exception)
            {
                Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} [InitialHandsetState({parentStateMachine.ConnectionPMID})] _task -> Exception");
                newState = new ErrorHandsetState($"Exception: {exception.Message}", parentStateMachine);
            }

            // TODO: we do not await this, as transitionto wants to dispose this step and this func needs to be done for that to happen
            _ = parentStateMachine.TransitionTo(newState, cancellationToken);
            Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} [InitialHandsetState({parentStateMachine.ConnectionPMID})] _task -> end of func");
        }, _taskCancellationTokenSource.Token);

        return Task.CompletedTask;
    }

    public override async Task ProcessMessage(DnmMessage dnm, MessageContext ctx, CancellationToken cancellationToken)
    {
        await _messageChannel.Writer.WriteAsync(_lastMessageWithContext = (dnm, ctx), cancellationToken);
    }
    
    private async Task InjectMessage(MessageDirection direction, Func<ReadOnlyMemory<byte>> dataFactory, CancellationToken cancellationToken)
    {
        Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} [InitialHandsetState({parentStateMachine.ConnectionPMID})] InjectMessage");

        await parentStateMachine.GigasetClient.WriteAsync(direction, 0, _lastMessageWithContext.ctx.Rfp, dataFactory.Invoke(), cancellationToken);
        
        // TODO: expect own message
    }

    private async Task<DnmMessage> ExpectMessage(MessageDirection expectedDirection, Func<DnmMessage, bool> messagePredicate, CancellationToken cancellationToken)
    {
        Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} [InitialHandsetState({parentStateMachine.ConnectionPMID})] ExpectMessage");
        
        // TODO: a timeout, maybe?
        while (true)
        {
            var (dnm, ctx) = await _messageChannel.Reader.ReadAsync(cancellationToken);

            // forward the original message
            await parentStateMachine.GigasetClient.WriteAsync(ctx.Direction, ctx.MessageId, ctx.Rfp, ctx.Data, cancellationToken);

            var matches = ctx.Direction == expectedDirection && messagePredicate.Invoke(dnm);
            if (matches)
            {
                return dnm;
            }

            // fully ignore non-nwk and empty nwk messages for now
            if (!dnm.IsNonEmptyNwkMessage())
            {
                continue;
            }
            
            // TODO: handle unexpected messages properly, but let's just ignore them for now
            Console.Write($"{DateTimeOffset.Now:HH:mm:ss.fff} [InitialHandsetState({parentStateMachine.ConnectionPMID})] ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(@"/!\ UNEXPECTED MESSAGE /!\");
            Console.ResetColor();
            Console.Write("  ");
            Console.Write((dnm.Payload as LcDataPayload)!.Payload.GetType());
            dnm.Log(Console.Out, true);
            Console.WriteLine();
        }
    }

    private async Task<HandsetState> RunUntilTransitionToOtherState(CancellationToken cancellationToken)
    {
        Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} [InitialHandsetState({parentStateMachine.ConnectionPMID})] RunUntilTransitionToOtherState");
        
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
        await InjectMessage(
            MessageDirection.ToPp, 
            () => SiemensMessageGenerator.GenerateFacilityDisplayMessage("meow! :3", _lastMessageWithContext.dnm.MCEI, 0x01),
            cancellationToken
        );
        // PP->FP - CISS: Register (<<ESCAPE-TO-PROPRIETARY>>: <<CISS-ACKNOWLEDGEMENT-SEQ>>)
        await ExpectMessage(
            MessageDirection.FromPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkCISSPayload { Type: NwkCISSMessageType.CISSRegister } },
            cancellationToken
        );
        // PP->FP - CISS: Release Connection
        await ExpectMessage(
            MessageDirection.FromPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkCISSPayload { Type: NwkCISSMessageType.CISSReleaseCom } },
            cancellationToken
        );

        
        // PP<-FP - CC: Facility
        var dateTime = new DateTime(DateOnly.FromDateTime(DateTime.Now), new TimeOnly(13, 37, 00));
        await InjectMessage(
            MessageDirection.ToPp, 
            () => SiemensMessageGenerator.GenerateFacilityDateTimeMessage(dateTime, _lastMessageWithContext.dnm.MCEI, 0x01),
            cancellationToken
        );
        // PP->FP - CISS: Register (<<ESCAPE-TO-PROPRIETARY>>: <<CISS-ACKNOWLEDGEMENT-SEQ>>)
        await ExpectMessage(
            MessageDirection.FromPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkCISSPayload { Type: NwkCISSMessageType.CISSRegister } },
            cancellationToken
        );
        // PP->FP - CISS: Release Connection
        await ExpectMessage(
            MessageDirection.FromPp,
            dnm => dnm.Payload is LcDataPayload { Payload: NwkCISSPayload { Type: NwkCISSMessageType.CISSReleaseCom } },
            cancellationToken
        );

        return new IdleHandsetState(parentStateMachine);
    }

    public override async ValueTask DisposeAsync()
    {
        Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} [InitialHandsetState({parentStateMachine.ConnectionPMID})] DisposeAsync");
        if (!(_taskCancellationTokenSource?.IsCancellationRequested ?? true))
        {
            Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} [InitialHandsetState({parentStateMachine.ConnectionPMID})] DisposeAsync -> Cancel");
            await _taskCancellationTokenSource.CancelAsync();
            await _task!.WaitAsync(CancellationToken.None);
        }
        Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} [InitialHandsetState({parentStateMachine.ConnectionPMID})] DisposeAsync -> Dispose1");
        _task?.Dispose();
        Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} [InitialHandsetState({parentStateMachine.ConnectionPMID})] DisposeAsync -> Dispose2");
        _taskCancellationTokenSource?.Dispose();
        Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} [InitialHandsetState({parentStateMachine.ConnectionPMID})] DisposeAsync -> Dispose3");
        
        // forward stray messages
        Console.WriteLine("Forwarding stray messages:");
        while (_messageChannel.Reader.TryRead(out var msg))
        {
            Console.WriteLine(" > msg");
            var (_, ctx) = msg;
            await parentStateMachine.GigasetClient.WriteAsync(ctx.Direction, ctx.MessageId, ctx.Rfp, ctx.Data, CancellationToken.None);
        }
    }
}