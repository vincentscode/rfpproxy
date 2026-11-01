using System;
using System.Threading;
using System.Threading.Tasks;
using RfpProxy.AaMiDe.AaMiDe.Dnm;

namespace RfpProxy.Gigaset;

public sealed class HandsetStateMachine : IAsyncDisposable
{
    public GigasetClient GigasetClient { get; }
    public readonly uint ConnectionPMID;

    private HandsetState? _currentState;

    private HandsetStateMachine(GigasetClient gigasetClient, uint connectionPMID)
    {
        GigasetClient = gigasetClient;
        ConnectionPMID = connectionPMID;
        _currentState = null!;
    }

    public static async Task<HandsetStateMachine> Create(Func<HandsetStateMachine, HandsetState> initialStateFactory, GigasetClient gigasetClient, uint connectionPMID, CancellationToken cancellationToken)
    {
        Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} [HandsetStateMachine({connectionPMID})] Create");
        var stateMachine = new HandsetStateMachine(gigasetClient, connectionPMID);
        await stateMachine.TransitionTo(initialStateFactory.Invoke(stateMachine), cancellationToken);
        return stateMachine;
    }

    public Task ProcessMessage(DnmMessage dnm, MessageContext messageContext, CancellationToken cancellationToken)
    {
        return _currentState!.ProcessMessage(dnm, messageContext, cancellationToken);
    }

    public async Task TransitionTo(HandsetState newState, CancellationToken cancellationToken)
    {
        Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} [HandsetStateMachine({ConnectionPMID})] TransitionTo: {newState}");
        if (_currentState != null)
        {
            await _currentState.DisposeAsync();
        }
        _currentState = newState;
        await _currentState.OnEnter(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_currentState != null)
        {
            await _currentState.DisposeAsync();
        }
    }
}