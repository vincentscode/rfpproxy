using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;

namespace RfpProxy.AaMiDe.AaMiDe.Sys
{
    public sealed class SysRPingMessage : AaMiDeMessage
    {
        public IPAddress Ip { get; }

        public TimeSpan Rtt { get; }

        public override bool HasUnknown => false;

        /// <summary>
        /// padding
        /// </summary>
        protected override ReadOnlyMemory<byte> Raw => base.Raw[20..];

        public SysRPingMessage(ReadOnlyMemory<byte> data):base(MsgType.SYS_RPING, data)
        {
            var span = base.Raw.Span;
            Ip = new IPAddress(span[..16]);
            Rtt = TimeSpan.FromMilliseconds(BinaryPrimitives.ReadUInt32BigEndian(span[16..]));
        }

        public override void Log(TextWriter writer)
        {
            base.Log(writer);
            writer.Write($"IP({Ip}) Rtt({Rtt.TotalMilliseconds}ms)");
        }
    }
}