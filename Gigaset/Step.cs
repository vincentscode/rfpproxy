using System;
using System.Threading;
using System.Threading.Tasks;
using RfpProxy.AaMiDe.AaMiDe.Dnm;

namespace RfpProxy.Gigaset;

public enum StepResult
{
    StayOnThisStep,
    GoToNextStep,
    GoToFailureState,
}

public abstract class Step
{
    public abstract Task<StepResult> ProcessMessage(DnmMessage dnm, MessageContext messageContext, CancellationToken cancellationToken);
}
