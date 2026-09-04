using System;
using System.Buffers.Binary;
using System.IO;
using RfpProxy.AaMiDe.AaMiDe.Dnm;
using RfpProxyLib;

namespace RfpProxy.AaMiDe.AaMiDe.Mac
{
    public sealed class MacHoInProgressIndPayload : DnmPayload
    {
        public uint PMID { get; }

        public override ReadOnlyMemory<byte> Raw => base.Raw[3..];
        
        public override bool HasUnknown => Raw.Length != 2 || (Raw.Span[0] != 0 && Raw.Span[0] != 2) || Raw.Span[1] != 1;

        public MacHoInProgressIndPayload(ReadOnlyMemory<byte> data):base(data)
        {
            var span = data.Span;
            PMID = (uint) (((span[0] & 0xf) << 16) | BinaryPrimitives.ReadUInt16BigEndian(span[1..]));
        }

        public override void Log(TextWriter writer)
        {
            writer.Write($" PMID({PMID:x5}) Reserved({Raw.ToHex()})");
        }
    }
}