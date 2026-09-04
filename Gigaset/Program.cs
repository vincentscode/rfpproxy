using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Mono.Options;
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

public static class Program
{
    public static async Task Main(string[] args)
    {
        var showHelp = false;
        var socketname = "client.sock";
        var options = new OptionSet
        {
            {"s|socket=", "socket path", x => socketname = x},
            {"h|help", "show help", x => showHelp = x != null},
        };
        try
        {
            if (options.Parse(args).Count > 0)
            {
                showHelp = true;
            }
        }
        catch (OptionException ex)
        {
            await Console.Error.WriteAsync("Parsing arguments failed.");
            await Console.Error.WriteLineAsync(ex.Message);
            await Console.Error.WriteLineAsync("Try 'dotnet gigaset.dll --help' for more information");
            return;
        }
        if (showHelp)
        {
            options.WriteOptionDescriptions(Console.Error);
            return;
        }

        Console.WriteLine("MEOW Listening...");
        try
        {
            using var cts = new CancellationTokenSource();
            using var client = new GigasetClient(socketname);

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                client.Stop();
            };

            const string mac = "000000000000";
            const string rfpMmask = "000000000000";
            const string filter = "0301";
            const string filterMask = "ffff";
            
            // TODO: switch to handle so we can also intercept and modify for example ReleaseCom / ReleaseReason : Reason(AaMiDeUnsupportedCallClass)
            await client.AddListenAsync(mac, rfpMmask, filter, filterMask, cts.Token).ConfigureAwait(false);
            await client.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
        {
            // ignored
        }
    }
}

public class GigasetClient(string socket) : ProxyClient(socket)
{
    private readonly MacConnectionTracker _rfpTracker = new();
    private readonly MacConnectionTracker _ommTracker = new();

    // TODO: this is terrible and only works for a single device
    private int messagesSinceTemporaryIdentityAssignAckSeen = -1;

    protected override async Task OnMessageAsync(MessageDirection direction, uint messageId, RfpIdentifier rfp, Memory<byte> data, CancellationToken cancellationToken)
    {
        // do not do further processing on empty messages
        if (data.IsEmpty) return;

        // proper reassembly
        DnmMessage dnm;
        try
        {
            var macConnectionTracker = (direction == MessageDirection.FromOmm) ? _ommTracker : _rfpTracker;
            var rfpMacConnectionTracker = macConnectionTracker.Get(rfp);
            var fromPrefix = (direction == MessageDirection.FromOmm) ? "OMM:" : "RFP:";

            var message = AaMiDeMessage.Create(data, rfpMacConnectionTracker);
                
            // we are only interested in proper DECToE not the internal communication between RFP and OMM
            if (message is not DnmMessage dnmMessage) return;
            dnm = dnmMessage;

            Console.ForegroundColor = (direction == MessageDirection.FromOmm) ? ConsoleColor.Blue : ConsoleColor.Green;
            Console.Write($"{fromPrefix} ");
            Console.ResetColor();

            dnm.Log(Console.Out, true);
            Console.WriteLine();

            // properly track connections
            // TODO: not sure why we invert the tracker selection for this, taken from RfpProxy.Log
            // TODO: not sure why we do this at all, it seems like the tracker already does that handling (see DnmPayload.CreateMac)
            var otherMacConnectionTracker = (direction == MessageDirection.FromOmm) ? _rfpTracker : _ommTracker;
            var rfpOtherMacConnectionTracker = otherMacConnectionTracker.Get(rfp);
            if (dnm.Payload is MacConIndPayload macConInd)
            {
                var connection = rfpOtherMacConnectionTracker.Get(dnm.MCEI);
                connection.Open(macConInd);
            }
                
            if (dnm.DnmType == DnmType.MacDisInd || dnm.DnmType == DnmType.MacDisReq)
            {
                var connection = rfpOtherMacConnectionTracker.Get(dnm.MCEI);
                connection.Close();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to handle message:");
            Console.WriteLine(ex);
            return;
        }

        // custom stuff
        try
        {
            var isTemporaryIdentityAssignAck = dnm.Payload is LcDataPayload { Payload: NwkMMPayload { Type: NwkMMMessageType.TemporaryIdentityAssignAck } };
            if (isTemporaryIdentityAssignAck)
            {
                Console.WriteLine("==== MEOW 1 ====");
                messagesSinceTemporaryIdentityAssignAckSeen = 0;
            }

            // original capture fritzbox<->gigaset (fritzbox_22.08.26_2023_gigaset_connecting_dtrace)
            // OMM/RFP --> PP [I], N(R)=0, N(S)=0(NWK) FACILITY
            // PP --> OMM/RFP [S], func=RR, N(R)=1
            // PP --> OMM/RFP [I], N(R)=1, N(S)=0(NWK) CISS-REGISTER
            // OMM/RFP --> PP [S], func=RR, N(R)=1
            // OMM/RFP --> PP [I], N(R)=1, N(S)=1(NWK) FACILITY
            // PP --> OMM/RFP [I], N(R)=1, N(S)=1(NWK) CISS-RELEASE-COM
            // OMM/RFP --> PP [S], func=RR, N(R)=0
            // PP --> OMM/RFP [S], func=RR, N(R)=0
            // PP --> OMM/RFP [I], N(R)=0, N(S)=0(NWK) CISS-REGISTER
            // OMM/RFP --> PP [S], func=RR, N(R)=1
            // OMM/RFP --> PP [I], N(R)=1, N(S)=0(NWK) FACILITY
            // PP --> OMM/RFP [I], N(R)=0, N(S)=1(NWK) CISS-RELEASE-COM
            // OMM/RFP --> PP [S], func=RR, N(R)=0
            // PP --> OMM/RFP [S], func=RR, N(R)=1
            // PP --> OMM/RFP [I], N(R)=1, N(S)=0(NWK) CISS-REGISTER
            // OMM/RFP --> PP [S], func=RR, N(R)=1
            // PP --> OMM/RFP [I], N(R)=1, N(S)=1(NWK) CISS-RELEASE-COM

            // TODO: everything about this is terrible
            if (!isTemporaryIdentityAssignAck && messagesSinceTemporaryIdentityAssignAckSeen >= 0)
            {
                messagesSinceTemporaryIdentityAssignAckSeen += 1;
                if (messagesSinceTemporaryIdentityAssignAckSeen == 1)
                {
                    Console.WriteLine("==== MEOW time first ====");
                    // should be 
                    // OMM: DNM                   (   8) Layer(Lc ) Type(LcDataReq           )
                    // MCEI(0x00) B(0) Channel(Cs) Length(  3) Command(1) SAPI(0) LLN(1) NLF(False) Type(RR) P/F(False) Nr(0) N(1) M(0) L(0)
                    Debug.Assert(direction == MessageDirection.FromOmm);
                    Debug.Assert(dnm.DnmType == DnmType.LcDataReq);

                    byte[] length = [0x00, 31-5]; // full newData.length - 4 (the RFP/OMM protocol header size)
                    var mcei = dnm.MCEI;
                    byte subfield = 0x10;
                    var alsoLength = (byte) (length[1] - 0x05); // length minus the DECToE header (i.e. -5 bytes)
                    byte address = 0x13; // (1 bit NLF, 3 bit LLN: A1, 2 bit SAPI: COS, 1 bit C/R, 1 bit blank?)
                    byte control = 0x00; // (3 bit N(R), 3 bit N(S), 1 bit frame type)
                    var innerLength = (byte)(((length[1] - 0x5 - 0x03) << 2) + (0b0 << 1) + (0b1 << 0)); // (6 bit len, 1 bit last segment, 1 bit final octet)
                    var newData = new byte[]
                    {
                        // RFP/OMM protocol header
                        0x03, 0x01,
                        length[0], length[1],
                        // DECT-over-Ethernet frame
                        (byte)DnmLayer.Lc,
                        (byte)DnmType.LcDataReq,
                        mcei,
                        subfield,
                        alsoLength,
                        // DECT DLC frame
                        address,
                        control,
                        innerLength,
                        // DECT NWK frame
                        (byte) ((0b0110 << 4) + NwkProtocolDiscriminator.CC),
                        (byte) NwkCCMessageType.Facility,

                        (byte) NwkVariableLengthElementType.Escape2Proprietary,
                        14, // length of all bytes after this one
                        (byte) NwkIeEscape2Proprietary.DiscriminatorType.EMC, // discriminator type: EMC
                        0x00, 0x02, // discriminator (i.e., EMC): 0x0002 -> Siemens
                            
                        // TLVs
                        (byte)SiemensProprietaryContent.SiemensType.TimeDate, // <<TIME-DATE>>
                        9, // content len (every byte after this one)
                        0x03, // ???
                        0x03, 0x09, 0x26, // date (dd, MM, yy) (BCD)
                        0x00, 0x04, // ???
                        0x22, 0x36, 0x00, // time (hh, mm, ss (maybe also 12/24h time flag))

                        // final element???
                        (byte)SiemensProprietaryContent.SiemensType.CissRequestSeq, // CissRequestSeq
                        0x01, // len = 1
                        0xe2, // seqnum
                    };

                    // TODO: build equivalent payload using nice classes etc.
                    var newData2 = new DnmMessage(
                        DnmLayer.Lc,
                        DnmType.LcDataReq,
                        dnm.MCEI,
                        subfield,
                        new LcDataPayload(
                            address,
                            control,
                            new NwkCCPayload(
                                0b0110, // ??
                                false, // ??
                                NwkCCMessageType.Facility,
                                [
                                    new NwkIeEscape2Proprietary(
                                        NwkIeEscape2Proprietary.DiscriminatorType.EMC,
                                        0x0002, // EMC
                                        new SiemensProprietaryContent(
                                            new SiemensProperietaryTimeDateContent(0x03, 3, 9, 26, 0x00, 0x04, 22, 36, 0x00),
                                            new SiemensProperietaryCissRequestSeqContent(0xe2)
                                        )
                                    )
                                ]
                            )
                        )
                    );
                    // TODO: actually make this result in the same bytes

                    // we already forwarded that one and immediately after (so now) send the FACILITY message
                    await WriteAsync(MessageDirection.FromOmm, 0, rfp, newData, cancellationToken);
                }
                // TODO: after sending this we get a CISS connection from the PP, maybe we need to wait with more facilities until after that, seems like an ACK

                // TODO this is ugly and copy pasted from above to try more shit
                if (messagesSinceTemporaryIdentityAssignAckSeen == 1 && false)
                {
                    Console.WriteLine("==== MEOW text first ====");
                    // should be 
                    // OMM: DNM                   (   8) Layer(Lc ) Type(LcDataReq           )
                    // MCEI(0x00) B(0) Channel(Cs) Length(  3) Command(1) SAPI(0) LLN(1) NLF(False) Type(RR) P/F(False) Nr(0) N(1) M(0) L(0)
                    Debug.Assert(direction == MessageDirection.FromOmm);
                    Debug.Assert(dnm.DnmType == DnmType.LcDataReq);

                    byte[] length = [0x00, 31]; // full newData.length - 4 (the RFP/OMM protocol header size)
                    var mcei = dnm.MCEI;
                    byte subfield = 0x10;
                    var alsoLength = (byte) (length[1] - 0x05); // length minus the DECToE header (i.e. -5 bytes)
                    byte address = 0x13; // (1 bit NLF, 3 bit LLN: A1, 2 bit SAPI: COS, 1 bit C/R, 1 bit blank?)
                    byte control = 0x00; // (3 bit N(R), 3 bit N(S), 1 bit frame type)
                    var innerLength = (byte)(((length[1] - 0x5 - 0x03) << 2) + (0b0 << 1) + (0b1 << 0)); // (6 bit len, 1 bit last segment, 1 bit final octet)
                    var newData = new byte[]
                    {
                        // RFP/OMM protocol header
                        0x03, 0x01,
                        length[0], length[1],
                        // DECT-over-Ethernet frame
                        (byte)DnmLayer.Lc,
                        (byte)DnmType.LcDataReq,
                        mcei,
                        subfield,
                        alsoLength,
                        // DECT DLC frame
                        address,
                        control,
                        innerLength,
                        // DECT NWK frame
                        (byte) ((0b0110 << 4) + NwkProtocolDiscriminator.CC), (byte) NwkCCMessageType.Facility,
                        (byte) NwkVariableLengthElementType.Escape2Proprietary,
                        19, // length of all bytes after this one
                        0x81, // discriminator type: EMC
                        0x00, 0x02, // discriminator (i.e., EMC): 0x0002 -> Siemens
                            
                        // custom siemens stuff inside DECT NWK > Esc2Prop
                        // (https://osmocom.org/projects/dect/wiki/Siemens)
                        // (https://gitea.osmocom.org/dect/libdect/src/branch/master/example/fp-siemens-proprietary.c)
                        (byte)SiemensProprietaryContent.SiemensType.Display, // <<DISPLAY>>
                        11, // content len (every byte after this one)
                        0x01, // handset number
                        (byte)'C', (byte)'4', (byte)'3', (byte)'0', (byte)'@', (byte)'M', (byte)'i', (byte)'t', (byte)'e', (byte)'l',

                        // second element???
                        0x5b, // CissRequestSeq
                        0x01, // len = 1
                        0xe2, // seqnum, in capture starts at e2, continues with e3, e4 for next facilities
                    };

                    // we already forwarded that one and immediately after (so now) send the FACILITY message
                    await WriteAsync(MessageDirection.FromOmm, 0, rfp, newData, cancellationToken);
                }

                // TODO this is ugly and copy pasted from above to try more shit
                if (messagesSinceTemporaryIdentityAssignAckSeen == 1 && false)
                {
                    Console.WriteLine("==== MEOW 2 ====");
                    // should be 
                    // OMM: DNM                   (   8) Layer(Lc ) Type(LcDataReq           )
                    // MCEI(0x00) B(0) Channel(Cs) Length(  3) Command(1) SAPI(0) LLN(1) NLF(False) Type(RR) P/F(False) Nr(0) N(1) M(0) L(0)
                    Debug.Assert(direction == MessageDirection.FromOmm);
                    Debug.Assert(dnm.DnmType == DnmType.LcDataReq);

                    byte[] length = [0x00, 26]; // full newData.length - 4 (the RFP/OMM protocol header size)
                    var mcei = dnm.MCEI;
                    byte subfield = 0x10;
                    var alsoLength = (byte) (length[1] - 0x05); // length minus the DECToE header (i.e. -5 bytes)
                    byte address = 0x13; // (1 bit NLF, 3 bit LLN: A1, 2 bit SAPI: COS, 1 bit C/R, 1 bit blank?)
                    // since we are sending packets in quick succession and do not check anything coming back in terms of accs, we can just try increasing N(S)
                    byte control = (0b001 << 5) + (0b001 << 1) + (0b0 << 0); // (3 bit N(R), 3 bit N(S), 1 bit frame type)
                    var innerLength = (byte)(((length[1] - 0x5 - 0x03) << 2) + (0b0 << 1) + (0b1 << 0)); // (6 bit len, 1 bit last segment, 1 bit final octet)
                    var newData = new byte[]
                    {
                        // RFP/OMM protocol header
                        0x03, 0x01,
                        length[0], length[1],
                        // DECT-over-Ethernet frame
                        (byte)DnmLayer.Lc,
                        (byte)DnmType.LcDataReq,
                        mcei,
                        subfield,
                        alsoLength,
                        // DECT DLC frame
                        address,
                        control,
                        innerLength,
                        // DECT NWK frame
                        (byte) ((0b0110 << 4) + NwkProtocolDiscriminator.CC), (byte) NwkCCMessageType.Facility,
                        (byte) NwkVariableLengthElementType.Escape2Proprietary,
                        14, // length of all bytes after this one
                        0x81, // discriminator type: EMC
                        0x00, 0x02, // discriminator (i.e., EMC): 0x0002 -> Siemens
                            
                        // custom siemens stuff inside DECT NWK > Esc2Prop
                        // (https://osmocom.org/projects/dect/wiki/Siemens)
                        // (https://gitea.osmocom.org/dect/libdect/src/branch/master/example/fp-siemens-proprietary.c)
                        (byte)SiemensProprietaryContent.SiemensType.TimeDate, // <<TIME-DATE>>
                        9, // content len (every byte after this one)
                        0x03, // ???
                        0x03, 0x09, 0x26, // date (dd, MM, yy) (BCD)
                        0x00, 0x04, // ???
                        0x22, 0x36, 0x00 // time (hh, mm, ss (maybe also 12/24h time flag))
                    };

                    // we already forwarded that one and immediately after (so now) send the FACILITY message
                    await WriteAsync(MessageDirection.FromOmm, 0, rfp, newData, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Custom Stuff failed. :(");
            Console.WriteLine(ex);
        }
    }
}