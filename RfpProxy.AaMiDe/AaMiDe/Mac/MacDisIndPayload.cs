using System;
using System.IO;
using RfpProxy.AaMiDe.AaMiDe.Dnm;

namespace RfpProxy.AaMiDe.AaMiDe.Mac
{
    /// <summary>
    /// (cf. EN 300 175‑3, Medium Access Control, section 8.1.6, Connection release: MAC_DIS {req;ind})
    /// </summary>
    /// <remarks>
    /// Connection release is the last phase of a connection orientated MAC service.
    /// If an MBC receives a MAC_DIS-req primitive from its DLC the MBC initiates a bearer release on all TBCs and disconnects the TBCs.
    /// The MAC releases the MBC and reports this event to the LLME.
    /// (cf. EN 300 175‑3, Medium Access Control, section 10.4, C/O connection release)
    /// </remarks>
    public sealed class MacDisIndPayload : DnmPayload
    {
        public enum MacDisIndReason : byte
        {
            Unspecified = 0,
            Normal = 1,
            Abnormal = 2
        }

        public MacDisIndReason Reason { get; }

        public override ReadOnlyMemory<byte> Raw => base.Raw.IsEmpty ? base.Raw : base.Raw[1..];

        public MacDisIndPayload(ReadOnlyMemory<byte> data) : base(data)
        {
            if (data.Length > 1)
                throw new ArgumentException("only one byte allowed");
            if (data.Length == 1)
                Reason = (MacDisIndReason) data.Span[0];
        }

        public override void Log(TextWriter writer)
        {
            writer.Write($" Reason({Reason,-11:G})");
        }
    }
}