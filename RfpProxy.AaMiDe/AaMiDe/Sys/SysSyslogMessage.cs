using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;

namespace RfpProxy.AaMiDe.AaMiDe.Sys
{
    public sealed class SysSyslogMessage : AaMiDeMessage
    {
        public IPAddress Ip { get; }

        public ushort Port { get; }

        /// <summary>
        /// padding
        /// </summary>
        protected override ReadOnlyMemory<byte> Raw => base.Raw[18..];

        public override bool HasUnknown => false;

        public SysSyslogMessage(ReadOnlyMemory<byte> data):base(MsgType.SYS_SYSLOG, data)
        {
            var span = base.Raw.Span;
            Ip = new IPAddress(span[..16]);
            Port = BinaryPrimitives.ReadUInt16BigEndian(span[16..]);
        }

        public override void Log(TextWriter writer)
        {
            base.Log(writer);
            writer.Write($"Ip({Ip}) Port({Port})");
        }
    }
}