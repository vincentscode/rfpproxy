using System;
using System.Threading;
using System.Threading.Tasks;
using RfpProxy.AaMiDe.AaMiDe.Dnm;

namespace RfpProxy.Gigaset;

public class HandsetStateMachine
{
    public GigasetClient GigasetClient { get; }
    
    private HandsetState _currentState;

    private HandsetStateMachine(GigasetClient gigasetClient)
    {
        GigasetClient = gigasetClient;
        
        _currentState = null!;
    }

    public static async Task<HandsetStateMachine> Create(Func<HandsetStateMachine, HandsetState> initialStateFactory, GigasetClient gigasetClient, CancellationToken cancellationToken)
    {
        var stateMachine = new HandsetStateMachine(gigasetClient);
        await stateMachine.TransitionTo(initialStateFactory.Invoke(stateMachine), cancellationToken);
        return stateMachine;
    }

    public Task ProcessMessage(DnmMessage dnm, MessageContext messageContext, CancellationToken cancellationToken)
    {
        return _currentState.ProcessMessage(dnm, messageContext, cancellationToken);
    }

    public async Task TransitionTo(HandsetState newState, CancellationToken cancellationToken)
    {
        _currentState = newState;
        await _currentState.OnEnter(cancellationToken);
    }
}