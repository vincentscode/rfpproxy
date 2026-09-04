using System;
using System.IO;

namespace RfpProxy.AaMiDe.AaMiDe.Rfpc
{
    public sealed class ByteRfpcValue : DnmRfpcValue
    {
        public byte Value { get; }

        public override ReadOnlyMemory<byte> Raw => base.Raw[1..];

        public ByteRfpcValue(RfpcKey type, ReadOnlyMemory<byte> data):base(type, data)
        {
            Value = data.Span[0];
        }

        public override void Log(TextWriter writer)
        {
            base.Log(writer);
            writer.Write($" {Value}");
        }
    }
}