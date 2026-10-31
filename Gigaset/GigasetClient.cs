using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RfpProxy.AaMiDe;
using RfpProxy.AaMiDe.AaMiDe;
using RfpProxy.AaMiDe.AaMiDe.Dnm;
using RfpProxy.AaMiDe.AaMiDe.Mac;
using RfpProxy.AaMiDe.AaMiDe.Nwk;
using RfpProxy.AaMiDe.AaMiDe.Nwk.InformationElements;
using RfpProxy.Gigaset.States;
using RfpProxyLib;
using RfpProxyLib.Messages;

namespace RfpProxy.Gigaset;

public class GigasetClient(string socket) : ProxyClient(socket)
{
    private readonly MacConnectionTracker _rfpTracker = new();
    private readonly MacConnectionTracker _ommTracker = new();

    private readonly Dictionary<uint, HandsetStateMachine> _stateMachinesByPMID = new();
    
    protected override async Task OnMessageAsync(MessageDirection direction, uint messageId, RfpIdentifier rfp, Memory<byte> data, CancellationToken cancellationToken)
    {
        // extract and log the dnm message & connection if it is one
        var dnmConnPair = HandleDefaultsAndGetMessage(direction, rfp, data);
        if (dnmConnPair is not var (dnm, connection))
        {
            // otherwise just forward as is
            await WriteAsync(direction, messageId, rfp, data, cancellationToken);
            return;
        }
    
        // get or create the state machine for the given pp
        var stateMachine = _stateMachinesByPMID.GetValueOrDefault(connection.PMID);
        if (stateMachine is null)
        {
            stateMachine = _stateMachinesByPMID[connection.PMID] = await HandsetStateMachine.Create(psm => new InitialHandsetState(psm), this, cancellationToken);
        }

        // let the state machine handle the message
        var context = new MessageContext
        {
            Direction = direction,
            MessageId = messageId,
            Rfp = rfp,
            Data = data,
            Connection = connection,
        };
        
        await stateMachine.ProcessMessage(dnm, context, cancellationToken);
    }

    private (DnmMessage, MacConnection)? HandleDefaultsAndGetMessage(MessageDirection direction, RfpIdentifier rfp, Memory<byte> data)
    {
        // do not do further processing on empty messages
        if (data.IsEmpty)
        {
            return null;
        }
        
        DnmMessage dnm;
        MacConnection connection;
        try
        {
            var macConnectionTracker = direction == MessageDirection.FromOmm ? _ommTracker : _rfpTracker;
            var rfpMacConnectionTracker = macConnectionTracker.Get(rfp);
            var fromPrefix = direction == MessageDirection.FromOmm ? "OMM:" : "RFP:";

            var message = AaMiDeMessage.Create(data, rfpMacConnectionTracker);

            // we are only interested in proper DECToE not the internal communication between RFP and OMM
            if (message is not DnmMessage dnmMessage) return null;
            dnm = dnmMessage;
            connection = rfpMacConnectionTracker.Get(dnm.MCEI);

            Console.ForegroundColor = direction == MessageDirection.FromOmm ? ConsoleColor.Blue : ConsoleColor.Green;
            Console.Write($"{fromPrefix} ");
            Console.ResetColor();

            dnm.Log(Console.Out, true);
            Console.WriteLine();

            // track connections
            var otherMacConnectionTracker = direction == MessageDirection.FromOmm ? _rfpTracker : _ommTracker;
            var rfpOtherMacConnectionTracker = otherMacConnectionTracker.Get(rfp);
            switch (dnm.Payload)
            {
                case MacConIndPayload macConInd:
                    rfpOtherMacConnectionTracker.Get(dnm.MCEI).Open(macConInd);
                    break;
                case MacConExtIndPayload macConExtInd:
                    Console.WriteLine("TPUI({0}) -> PMID({1})", macConExtInd.TPUI.ToHex(), MacConExtIndPayload.TPUI2PMID[macConExtInd.TPUI.ToHex()]);
                    rfpOtherMacConnectionTracker.Get(dnm.MCEI).Open(macConExtInd);
                    break;
                case LcDataPayload { Payload: NwkMMPayload { Type: NwkMMMessageType.LocateAccept } nwkMmPayload }:
                {
                    var portableIdentity = (NwkIePortableIdentity) nwkMmPayload.InformationElements.Single(x => x is NwkIePortableIdentity);
                    Debug.Assert(portableIdentity.IdentityType == NwkIePortableIdentity.PortableIdentityType.TPUI);
                    Debug.Assert(portableIdentity.TPUIType == NwkIePortableIdentity.TPUITypeCoding.WithoutNumber);

                    Console.WriteLine("Setting TPUI for connection:");
                    Console.WriteLine("Identity (TPUI): {0}", portableIdentity.Identity.ToArray().ToHex());
                    Console.WriteLine("PMID for MCEI: {0}", rfpOtherMacConnectionTracker.Get(dnm.MCEI).PMID);

                    MacConExtIndPayload.TPUI2PMID[portableIdentity.Identity.ToHex()] = rfpOtherMacConnectionTracker.Get(dnm.MCEI).PMID;
                    break;
                }
            }

            if (dnm.DnmType is DnmType.MacDisInd or DnmType.MacDisReq)
            {
                rfpOtherMacConnectionTracker.Get(dnm.MCEI).Close();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to handle message:");
            Console.WriteLine(ex);
            return null;
        }

        return (dnm, connection);
    }
}