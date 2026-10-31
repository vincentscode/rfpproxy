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
using RfpProxy.AaMiDe.AaMiDe.Nwk.InformationElements.Proprietary;
using RfpProxyLib;
using RfpProxyLib.Messages;

namespace RfpProxy.Gigaset;

public abstract class HandsetState
{
    public abstract Task OnEnter(CancellationToken cancellationToken);
    public abstract Task<bool> ProcessMessage(DnmMessage dnm, CancellationToken cancellationToken);
}

public sealed class InitialHandsetState : HandsetState
{
    private int _messagesSinceTemporaryIdentityAssignAckSeen = -1;
    
    public static readonly InitialHandsetState Instance = new();

    private InitialHandsetState()
    {
    }

    public override Task OnEnter(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public override Task<bool> ProcessMessage(DnmMessage dnm, CancellationToken cancellationToken)
    {
        var isTemporaryIdentityAssignAck = dnm.Payload is LcDataPayload { Payload: NwkMMPayload { Type: NwkMMMessageType.TemporaryIdentityAssignAck } };
        if (isTemporaryIdentityAssignAck)
        {
            _messagesSinceTemporaryIdentityAssignAckSeen = 0;
        }

        if (!isTemporaryIdentityAssignAck && _messagesSinceTemporaryIdentityAssignAckSeen >= 0)
        {
            _messagesSinceTemporaryIdentityAssignAckSeen += 1;
            if (_messagesSinceTemporaryIdentityAssignAckSeen == 1)
            {
                var cissRequestSeqTlv = new SiemensProprietaryCissRequestSeqElement(0xe2);
                var displayTlv = new SiemensProprietaryDisplayElement(0x01, "C430@Mitel");

                byte[] notificationTlvData =
                [
                    0x2, // event notification subtype
                    14, // length of subtype
                    0x1, 0x0, // answering machine messages notifications (0x1), count
                    0x2, 0x0, // unknown (0x2), count
                    0x3, 0x0, // waiting sms (0x3), count
                    0x4, 0x0, // unknown (0x4), count
                    0x5, 0x0, // missed calls (0x5), count
                ];
                var notificationTlv = new SiemensProprietaryContent.SiemensElement(
                    SiemensProprietaryContent.SiemensType.Unnamed,
                    notificationTlvData
                );

                // unknown subtype from https://osmocom.org/projects/dect/wiki/Trace_access_rights_gigaseta140
                byte[] testTlvData =
                [
                    0x3, // subtype
                    0, // length of subtype
                ];
                var testTlv = new SiemensProprietaryContent.SiemensElement(
                    SiemensProprietaryContent.SiemensType.Unnamed,
                    notificationTlvData
                );

                var options = new HeaderOptions { NR = 0b000, NS = 0b000 };

                await SendSiemensFacilityMessage([displayTlv, cissRequestSeqTlv], rfp, dnm.MCEI, options,
                    cancellationToken);
            }
        }

        return Task.CompletedTask;
    }
}

public class HandsetStateMachine
{
    private HandsetState _currentState;

    private HandsetStateMachine()
    {
        _currentState = null!;
    }

    public static async Task<HandsetStateMachine> Create(HandsetState initialState, CancellationToken cancellationToken)
    {
        var stateMachine = new HandsetStateMachine();
        await stateMachine.TransitionTo(initialState, cancellationToken);
        return stateMachine;
    }

    public Task<bool> ProcessMessage(DnmMessage dnm, CancellationToken cancellationToken)
    {
        return _currentState.ProcessMessage(dnm, cancellationToken);
    }

    public async Task TransitionTo(HandsetState newState, CancellationToken cancellationToken)
    {
        _currentState = newState;
        await _currentState.OnEnter(cancellationToken);
    }
}

public class GigasetClient(string socket) : ProxyClient(socket)
{
    private readonly MacConnectionTracker _rfpTracker = new();
    private readonly MacConnectionTracker _ommTracker = new();

    // TODO: this is terrible and only works for a single device, but we can fix that with the connection PMID
    private int _messagesSinceCISSReleaseComSeen = -1;

    private int _act;

    private readonly Dictionary<uint, HandsetStateMachine> _stateMachinesByPMID = new();
    
    protected override async Task OnMessageAsync(MessageDirection direction, uint messageId, RfpIdentifier rfp, Memory<byte> data, CancellationToken cancellationToken)
    {
        // do not do further processing on empty messages
        if (data.IsEmpty)
        {
            // we are a handler, so we must forward all messages that we do not want to modify as they are
            await WriteAsync(direction, messageId, rfp, data, cancellationToken);
            return;
        }

        // log and get the message
        var dnmConnPair = HandleDefaultsAndGetMessage(direction, rfp, data);
        if (dnmConnPair is not var (dnm, connection))
        {
            // we are a handler, so we must forward all messages that we do not want to modify as they are
            await WriteAsync(direction, messageId, rfp, data, cancellationToken);
            return;
        }

        Console.WriteLine(" > {0}", connection);

        // get the state machine for the given portable device
        // TODO: do we need to incorporate MCEI as well?
        var stateMachine = _stateMachinesByPMID.GetValueOrDefault(connection.PMID);
        if (stateMachine is null)
        {
            stateMachine = _stateMachinesByPMID[connection.PMID] = await HandsetStateMachine.Create(InitialHandsetState.Instance, cancellationToken);
        }

        // let the state machine handle the message or forward it ourselves
        var handled = await stateMachine.ProcessMessage(dnm, cancellationToken);
        if (!handled)
        {
            // we are a handler, so we must forward all messages that we do not want to modify as they are
            await WriteAsync(direction, messageId, rfp, data, cancellationToken);
        }

        // TODO: actually ensure that we are only messing with Siemens devices (EMC 0x0002)
        if (dnm.Payload is LcDataPayload { Payload: NwkMMPayload { Type: NwkMMMessageType.LocateAccept } nwkMmPayload })
        {
            var portableIdentity = (NwkIePortableIdentity) nwkMmPayload.InformationElements.Single(x => x is NwkIePortableIdentity);
            Debug.Assert(portableIdentity.IdentityType == NwkIePortableIdentity.PortableIdentityType.TPUI);
            Debug.Assert(portableIdentity.TPUIType == NwkIePortableIdentity.TPUITypeCoding.WithoutNumber);

        }

        // custom injections
        try
        {
            var isCISSReleaseCom = dnm.Payload is LcDataPayload { Payload: NwkCISSPayload { Type: NwkCISSMessageType.CISSReleaseCom } };
            if (isCISSReleaseCom)
            {
                Console.WriteLine("==== MEOW OBSERVE 2 ====");
                _messagesSinceCISSReleaseComSeen = 0;
            }

            // TODO: everything about this is terrible (act exactly 1 after CISSReleaseCom)
            if (!isCISSReleaseCom && _messagesSinceCISSReleaseComSeen >= 0)
            {
                _messagesSinceCISSReleaseComSeen += 1;
                if (_messagesSinceCISSReleaseComSeen == 1 && _act == 1)
                {
                    Console.WriteLine("==== MEOW ACT 2 ====");
                    _act = (_act + 1) % 2;

                    var dateTime = new DateTime(new DateOnly(2000, 1, 1), new TimeOnly(13, 37, 00));
                    var timeDateTlv = new SiemensProprietaryTimeDateElement(dateTime, 0x03, 0x00, 0x04);
                    var cissRequestSeqTlv = new SiemensProprietaryCissRequestSeqElement(0xe3);

                    var options = new HeaderOptions { NR = 0b000, NS = 0b001 };
                    await SendSiemensFacilityMessage([timeDateTlv, cissRequestSeqTlv], rfp, dnm.MCEI, options, cancellationToken);
                }
            }

            // TODO: after sending our FACILITYs we get a CISS connection from the PP, handle the ACK properly
        }
        catch (Exception ex)
        {
            Console.WriteLine("Custom Stuff failed. :(");
            Console.WriteLine(ex);
        }
    }

    private record HeaderOptions
    {
        /// <summary>
        /// N(S) field, 3 bits inside the control field of the DLC frame
        /// </summary>
        public byte NS
        {
            get;
            init
            {
                Debug.Assert(value <= 0b111);
                field = value;
            }
        } = 0b000;

        /// <summary>
        /// N(R) field, 3 bits inside the control field of the DLC frame
        /// </summary>
        public byte NR
        {
            get;
            init
            {
                Debug.Assert(value <= 0b111);
                field = value;
            }
        } = 0b000;

        public bool LastSegment { get; init; } = false;
        public bool FinalOctet { get; init; } = true;
    }
    
    private async Task SendSiemensFacilityMessage(List<SiemensProprietaryContent.SiemensElement> elements, RfpIdentifier rfp, byte mcei, HeaderOptions options, CancellationToken cancellationToken)
    {
        byte[] siemensEMC = [0x00, 0x02];
        List<byte> escape2ProprietaryIeBody =
        [
            (byte) NwkIeEscape2Proprietary.DiscriminatorType.EMC,
            ..siemensEMC, // discriminator
        ];

        foreach (var element in elements)
        {
            escape2ProprietaryIeBody.AddRange(element.Serialize());
        }
        
        List<byte> escape2ProprietaryIe =
        [
            (byte) NwkVariableLengthElementType.Escape2Proprietary,
            (byte)escape2ProprietaryIeBody.Count,
            ..escape2ProprietaryIeBody
        ];
        
        List<byte> nwkFrame =
        [
            (byte) ((0b0110 << 4) + NwkProtocolDiscriminator.CC),
            (byte) NwkCCMessageType.Facility,

            // IEs
            ..escape2ProprietaryIe,
        ];
        
        // TODO: fully build payload using nice classes etc.
        byte[] identifier = [0x03, 0x01];
        byte[] length = [0x00, (byte)(nwkFrame.Count+3+5)];
        List<byte> newData =
        [
            // RFP/OMM protocol header
            ..identifier,
            ..length,
            // DECT-over-Ethernet frame
            (byte)DnmLayer.Lc,
            (byte)DnmType.LcDataReq,
            mcei,
            0x10, // subfield
            (byte) (nwkFrame.Count+3),
            // DECT DLC frame
            0x13, // address = (1 bit NLF, 3 bit LLN: A1, 2 bit SAPI: COS, 1 bit C/R, 1 bit blank?),
            (byte)((options.NR << 5) + (options.NS << 1) + 0b0), // control = (3 bit N(R), 3 bit N(S), 1 bit frame type)
            (byte)(((byte)nwkFrame.Count << 2) + ((options.LastSegment ? 0b1 : 0b0) << 1) + ((options.FinalOctet ? 0b1 : 0b0) << 0)),
            // DECT NWK frame
            ..nwkFrame
        ];

        // send our additional facility message
        await WriteAsync(MessageDirection.FromOmm, 0, rfp, newData.ToArray(), cancellationToken);
    }

    private (DnmMessage, MacConnection)? HandleDefaultsAndGetMessage(MessageDirection direction, RfpIdentifier rfp, Memory<byte> data)
    {
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