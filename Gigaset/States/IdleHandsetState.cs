using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RfpProxy.AaMiDe.AaMiDe.Dnm;
using RfpProxy.AaMiDe.AaMiDe.Mac;
using RfpProxy.AaMiDe.AaMiDe.Nwk;
using RfpProxy.AaMiDe.AaMiDe.Nwk.InformationElements;
using RfpProxy.Gigaset.Helpers;

namespace RfpProxy.Gigaset.States;

public sealed class IdleHandsetState(HandsetStateMachine parentStateMachine) : HandsetState
{
    public override string ToString() => $"{nameof(IdleHandsetState)}()";

    private uint? _mceiToDropMacDisReqFor = null;
    
    public override async Task ProcessMessage(DnmMessage dnm, MessageContext ctx, CancellationToken cancellationToken)
    {
        // for now also intercept MAC_DIS_REQ on the same MCEI as AaMiDeUnsupportedCallClass
        if (dnm.DnmType == DnmType.MacDisReq && dnm.MCEI == _mceiToDropMacDisReqFor)
        {
            Console.WriteLine("IGNORING MAC DIS REQ");
            _mceiToDropMacDisReqFor = null;
            await parentStateMachine.GigasetClient.WriteAsync(ctx.Direction, ctx.MessageId, ctx.Rfp, new ReadOnlyMemory<byte>(), cancellationToken);
        }
        
        // intercept AaMiDeUnsupportedCallClass
        if (dnm.Payload is LcDataPayload { Payload: NwkCCPayload { Type: NwkCCMessageType.ReleaseCom } } lcDataPayload)
        {
            _mceiToDropMacDisReqFor = dnm.MCEI;
            
            Console.WriteLine("OVERWRITEOVERWRITEOVERWRITEOVERWRITEOVERWRITEOVERWRITE");
            {
                // steal the options from the original
                var options = new SiemensMessageGenerator.HeaderOptions
                {
                    NR = lcDataPayload.Nr,
                    NS = lcDataPayload.Ns,
                };
                
                byte[] siemensEMC = [0x00, 0x02];
                List<byte> escape2ProprietaryIeBody =
                [
                    (byte) NwkIeEscape2Proprietary.DiscriminatorType.EMC,
                    ..siemensEMC, // discriminator
                ];

                // escape2proprietary elements copied from wireshark
                // TODO: actually understand them
                escape2ProprietaryIeBody.AddRange([0x1, 0x2, 0x1, 0x7]); // TLV
                
                List<byte> escape2ProprietaryIe =
                [
                    (byte) NwkVariableLengthElementType.Escape2Proprietary,
                    (byte)escape2ProprietaryIeBody.Count,
                    ..escape2ProprietaryIeBody
                ];

                List<byte> codecListIe =
                [
                    (byte)NwkVariableLengthElementType.CodecList,
                    0x7, // length
                    0x90, 0x3, 0x0, 0x1, 0x2, 0x0, 0x84, // copied from wireshark
                ];
            
                List<byte> nwkFrame =
                [
                    (byte) ((0b0110 << 4) + NwkProtocolDiscriminator.CC),
                    (byte) NwkCCMessageType.Connect,

                    // IEs
                    ..escape2ProprietaryIe,
                    ..codecListIe
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
                    dnm.MCEI,
                    0x10, // subfield
                    (byte) (nwkFrame.Count+3),
                    // DECT DLC frame
                    0x13, // address = (1 bit NLF, 3 bit LLN: A1, 2 bit SAPI: COS, 1 bit C/R, 1 bit blank?),
                    (byte)((options.NR << 5) + (options.NS << 1) + 0b0), // control = (3 bit N(R), 3 bit N(S), 1 bit frame type)
                    (byte)(((byte)nwkFrame.Count << 2) + ((options.LastSegment ? 0b1 : 0b0) << 1) + ((options.FinalOctet ? 0b1 : 0b0) << 0)),
                    // DECT NWK frame
                    ..nwkFrame
                ];

                await parentStateMachine.GigasetClient.WriteAsync(ctx.Direction, ctx.MessageId, ctx.Rfp, newData.ToArray(), cancellationToken);
            }

            _ = Task.Run(async () =>
            {

            
            await Task.Delay(250, cancellationToken);
            
            Console.WriteLine("INJECTING11111111111111111111111");
            {
                // steal the options from the original
                var options = new SiemensMessageGenerator.HeaderOptions
                {
                    NR = 0x0,
                    NS = 0x1,
                    FinalOctet = true,
                };
                
                byte[] siemensEMC = [0x00, 0x02];
                List<byte> escape2ProprietaryIeBody =
                [
                    (byte) NwkIeEscape2Proprietary.DiscriminatorType.EMC,
                    ..siemensEMC, // discriminator
                ];

                // escape2proprietary elements copied from wireshark
                // TODO: actually understand them
                escape2ProprietaryIeBody.AddRange([0x32, 0x1, 0xe]); // TLV
                
                List<byte> escape2ProprietaryIe =
                [
                    (byte) NwkVariableLengthElementType.Escape2Proprietary,
                    (byte)escape2ProprietaryIeBody.Count,
                    ..escape2ProprietaryIeBody
                ];

                List<byte> nwkFrame =
                [
                    (byte) ((0b0110 << 4) + NwkProtocolDiscriminator.CC),
                    (byte) NwkCCMessageType.Info,

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
                    dnm.MCEI,
                    0x10, // subfield
                    (byte) (nwkFrame.Count+3),
                    // DECT DLC frame
                    0x13, // address = (1 bit NLF, 3 bit LLN: A1, 2 bit SAPI: COS, 1 bit C/R, 1 bit blank?),
                    (byte)((options.NR << 5) + (options.NS << 1) + 0b0), // control = (3 bit N(R), 3 bit N(S), 1 bit frame type)
                    (byte)(((byte)nwkFrame.Count << 2) + ((options.LastSegment ? 0b1 : 0b0) << 1) + ((options.FinalOctet ? 0b1 : 0b0) << 0)),
                    // DECT NWK frame
                    ..nwkFrame
                ];

                await parentStateMachine.GigasetClient.WriteAsync(ctx.Direction, 0x0, ctx.Rfp, newData.ToArray(), cancellationToken);
            }
            
            await Task.Delay(250, cancellationToken);

            Console.WriteLine("INJECTING22222222222222222222222222222222");
            {
                // steal the options from the original
                var options = new SiemensMessageGenerator.HeaderOptions
                {
                    NR = 0x0,
                    NS = 0x2,
                    FinalOctet = true,
                };
                
                List<byte> multiDisplayIeBody =
                [
                    0x2, 0xa, 0x1a, 0xa, 0x1a, 0xa, 0x1a, 0xa, 0x1a, 0xa, 0x1a, 0xa, 0x1a, 0x2, 0xa
                ];
                
                List<byte> multiDisplayIe =
                [
                    (byte) NwkVariableLengthElementType.MultiDisplay,
                    (byte)multiDisplayIeBody.Count,
                    ..multiDisplayIeBody
                ];

                List<byte> nwkFrame =
                [
                    (byte) ((0b0110 << 4) + NwkProtocolDiscriminator.CC),
                    (byte) NwkCCMessageType.Info,

                    // IEs
                    ..multiDisplayIe,
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
                    dnm.MCEI,
                    0x10, // subfield
                    (byte) (nwkFrame.Count+3),
                    // DECT DLC frame
                    0x13, // address = (1 bit NLF, 3 bit LLN: A1, 2 bit SAPI: COS, 1 bit C/R, 1 bit blank?),
                    (byte)((options.NR << 5) + (options.NS << 1) + 0b0), // control = (3 bit N(R), 3 bit N(S), 1 bit frame type)
                    (byte)(((byte)nwkFrame.Count << 2) + ((options.LastSegment ? 0b1 : 0b0) << 1) + ((options.FinalOctet ? 0b1 : 0b0) << 0)),
                    // DECT NWK frame
                    ..nwkFrame
                ];

                await parentStateMachine.GigasetClient.WriteAsync(ctx.Direction, 0x0, ctx.Rfp, newData.ToArray(), cancellationToken);
            }
            
            await Task.Delay(250, cancellationToken);
            
            Console.WriteLine("INJECTING3333333333333333333333333333333333333333333333333333333");
            {
                // steal the options from the original
                var options = new SiemensMessageGenerator.HeaderOptions
                {
                    NR = 0x0,
                    NS = 0x3,
                    FinalOctet = true,
                };
                
                List<byte> multiDisplayIeBody =
                [
                    0x2, 0x1a, 0x9c, 0x2, 0x0, 0x2, 0x91, 0x2, 0x2, 0x6, 0x49, 0x6e, 0x74, 0x65, 0x72, 0x6e, 0xa, 0x9c, 0xc, 0x1, 0x1, 0x2, 0x0, 0x3, 0x0, 0x4, 0x0, 0x5, 0x0, 0x6, 0x0, 0x2, 0xa, 0x1a, 0xa, 0x1a, 0xa, 0x1a, 0xa, 0x1a, 0xa, 0x1a, 0xa, 0x1a, 0x2, 0xa
                ];
                
                List<byte> multiDisplayIe =
                [
                    (byte) NwkVariableLengthElementType.MultiDisplay,
                    (byte)multiDisplayIeBody.Count,
                    ..multiDisplayIeBody
                ];

                List<byte> nwkFrame =
                [
                    (byte) ((0b0110 << 4) + NwkProtocolDiscriminator.CC),
                    (byte) NwkCCMessageType.Info,

                    // IEs
                    ..multiDisplayIe,
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
                    dnm.MCEI,
                    0x10, // subfield
                    (byte) (nwkFrame.Count+3),
                    // DECT DLC frame
                    0x13, // address = (1 bit NLF, 3 bit LLN: A1, 2 bit SAPI: COS, 1 bit C/R, 1 bit blank?),
                    (byte)((options.NR << 5) + (options.NS << 1) + 0b0), // control = (3 bit N(R), 3 bit N(S), 1 bit frame type)
                    (byte)(((byte)nwkFrame.Count << 2) + ((options.LastSegment ? 0b1 : 0b0) << 1) + ((options.FinalOctet ? 0b1 : 0b0) << 0)),
                    // DECT NWK frame
                    ..nwkFrame
                ];

                await parentStateMachine.GigasetClient.WriteAsync(ctx.Direction, 0x0, ctx.Rfp, newData.ToArray(), cancellationToken);
            }
            });
            
            return;
        }
        
        // otherwise just forward everything as is, for now
        Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss.fff} [IdleHandsetState] Forwarding message");
        await parentStateMachine.GigasetClient.WriteAsync(ctx.Direction, ctx.MessageId, ctx.Rfp, ctx.Data, cancellationToken);
    }

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}