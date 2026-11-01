using System;
using System.Collections.Generic;
using System.Diagnostics;
using RfpProxy.AaMiDe.AaMiDe.Dnm;
using RfpProxy.AaMiDe.AaMiDe.Nwk;
using RfpProxy.AaMiDe.AaMiDe.Nwk.InformationElements;
using RfpProxy.AaMiDe.AaMiDe.Nwk.InformationElements.Proprietary;

namespace RfpProxy.Gigaset.Helpers;

public static class SiemensMessageGenerator
{
    /// <summary>
    /// Generate a Siemens FACILITY message with a DISPLAY IE and a CISS-REQUEST-SEQ IE.
    /// </summary>
    /// <returns>The message as bytes, ready to be sent.</returns>
    public static ReadOnlyMemory<byte> GenerateFacilityDisplayMessage(string displayString, byte mcei, byte sequenceNumber, byte handsetNumber = 0x01)
    {
        var cissRequestSeqTlv = new SiemensProprietaryCissRequestSeqElement(sequenceNumber);
        var displayTlv = new SiemensProprietaryDisplayElement(handsetNumber, displayString);

        var options = new HeaderOptions { NR = 0b000, NS = 0b000 };
        return GenerateFacilityMessage([displayTlv, cissRequestSeqTlv], mcei, options).ToArray();
    }

    /// <summary>
    /// Generate a Siemens FACILITY message with a TIME-DATE IE and a CISS-REQUEST-SEQ IE.
    /// </summary>
    /// <returns>The message as bytes, ready to be sent.</returns>
    public static ReadOnlyMemory<byte> GenerateFacilityDateTimeMessage(DateTime dateTime, byte mcei, byte sequenceNumber)
    {
        var timeDateTlv = new SiemensProprietaryTimeDateElement(dateTime, 0x03, 0x00, 0x04);
        var cissRequestSeqTlv = new SiemensProprietaryCissRequestSeqElement(sequenceNumber);

        var options = new HeaderOptions { NR = 0b000, NS = 0b001 };
        return GenerateFacilityMessage([timeDateTlv, cissRequestSeqTlv], mcei, options).ToArray();
    }

    private static List<byte> GenerateFacilityMessage(List<SiemensProprietaryContent.SiemensElement> elements, byte mcei, HeaderOptions options)
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

        return newData;
    }
    
    public sealed record HeaderOptions
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
}