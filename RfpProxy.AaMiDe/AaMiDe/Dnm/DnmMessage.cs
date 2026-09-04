using System;
using System.IO;
using System.Linq;
using RfpProxy.AaMiDe.AaMiDe.Mac;
using RfpProxy.AaMiDe.AaMiDe.Mt;
using RfpProxy.AaMiDe.AaMiDe.Rfpc;

namespace RfpProxy.AaMiDe.AaMiDe.Dnm
{
    public enum DnmType : byte
    {
        /// <summary>
        /// MAC Connect Ind
        /// </summary>
        MacConInd = 0x01,
        /// <summary>
        /// MAC Disconnect Req
        /// </summary>
        MacDisReq = 0x02,
        /// <summary>
        /// MAC Disconnect Ind
        /// </summary>
        MacDisInd = 0x03,
        /// <summary>
        /// Lc Data Transfer Req
        /// </summary>
        LcDataReq = 0x05,
        /// <summary>
        /// Lc Data Transfer Ind
        /// </summary>
        LcDataInd = 0x06,
        /// <summary>
        /// Lc Data Transmit Ready Ind
        /// </summary>
        LcDtrInd = 0x07,
        MacPageReq = 0x08,
        MacEncKeyReq = 0x09,
        MacEncEksInd = 0x0a,
        HoInProgressInd = 0x0b,
        HoInProgressRes = 0x0c,
        HoFailedInd = 0x0d,
        HoFailedReq = 0x0e,
        DlcRfpErrorInd = 0x14,
        MacConExtInd = 0x15,
        HoInProgressExtInd = 0x16,
        MacModReq = 0x17,
        MacModCnf = 0x18,
        MacModInd = 0x19,
        MacModRej = 0x1a,
        MacRecordAudio = 0x1b,
        MacInfoInd = 0x1c,
        MacGetDefCkeyInd = 0x1d,
        MacGetDefCkeyRes = 0x1e,
        MacClearDefCkeyReq = 0x1f,
        MacGetCurrCkeyIdReq = 0x20,
        MacGetCurrCkeyIdCnf = 0x21,
    }

    public enum DnmLayer : byte
    {
        /// <summary>
        /// RFP Control Stuff probably?
        /// </summary>
        Rfpc = 0x78,
        /// <summary>
        /// Lc entity: the lower Lc entity buffers and fragments complete LAPC frames (LAPC protocol data units) to/from the MAC layer.
        /// </summary>
        Lc = 0x79,
        /// <summary>
        /// MAC
        /// </summary>
        Mac = 0x7a,
        Unknown1 = 0x7B,
        Mt = 0x7C,
        Sync = 0x7d,
    }

    /// <summary>
    /// DNM messages = Dect-over-Ethernet messages (cf. Wireshark dissector)
    /// </summary>
    public sealed class DnmMessage : AaMiDeMessage
    {
        public DnmLayer Layer { get; }

        public DnmType DnmType { get; }

        /// <summary>
        /// MAC Connection Endpoint Identification 
        /// </summary>
        public byte MCEI { get; }

        public DnmPayload Payload { get; }

        protected override ReadOnlyMemory<byte> Raw => _raw;

        public override bool HasUnknown => Payload.HasUnknown;

        private byte[] _raw;

        public DnmMessage(ReadOnlyMemory<byte> data, RfpConnectionTracker reassembler) : base(MsgType.DNM, data)
        {
            var span = base.Raw.Span;
            Layer = (DnmLayer) span[0];
            DnmType = (DnmType) span[1];
            MCEI = span[2];
            Payload = DnmPayload.Create(Layer, DnmType, base.Raw[3..], reassembler.Get(MCEI));
        }

        public DnmMessage(DnmLayer layer, DnmType type, byte mcei, byte subfield, DnmPayload payload) : base(MsgType.DNM)
        {
            Layer = layer;
            DnmType = type;
            MCEI = mcei;
            Payload = payload;
        }

        public static AaMiDeMessage CreateDnm(ReadOnlyMemory<byte> data, RfpConnectionTracker reassembler)
        {
            var layer = (DnmLayer) data.Span[4];
            switch (layer)
            {
                case DnmLayer.Rfpc:
                    return new DnmRfpcMessage(data);
                case DnmLayer.Mt:
                    return new DnmMtMessage(data);
                case DnmLayer.Lc:
                case DnmLayer.Mac:
                    var type = (DnmType) data.Span[5];
                    switch (type)
                    {
                        case DnmType.MacPageReq:
                            return new MacPageReqMessage(data);
                        case DnmType.MacClearDefCkeyReq:
                            return new MacClearDefCkeyReqPayload(data);
                        default:
                            return new DnmMessage(data, reassembler);
                    }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public override void Log(TextWriter writer)
        {
            Log(writer, false);
        }

        public void Log(TextWriter writer, bool colored)
        {
            base.Log(writer);
            writer.Write($"Layer(");
            if (colored)
            {
                switch (Layer)
                {
                    case DnmLayer.Lc:
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        break;
                    case DnmLayer.Mac:
                        Console.ForegroundColor = ConsoleColor.Red;
                        break;
                    default:
                        Console.ResetColor();
                        break;
                }
            }
            writer.Write($"{Layer,-3:G}");
            if (colored)
            {
                Console.ResetColor();
            }
            writer.Write($") Type(");
            if (colored)
            {
                Console.ForegroundColor = ConsoleColor.DarkMagenta;
            }
            writer.Write($"{DnmType,-20:G}");
            if (colored)
            {
                Console.ResetColor();
            }
            writer.Write($") MCEI(0x{MCEI:x2})");
            Payload.Log(writer);
        }

        public override Span<byte> Serialize(Span<byte> data)
        {
            base.Serialize(data);
            // TODO: add own data
            
            return data;
        }
    }
}