using System;

namespace RfpProxy.AaMiDe.AaMiDe.Dnm
{
    public sealed class EmptyLcPayload : LcPayload
    {
        public override byte DataLength => 0;

        public EmptyLcPayload(ReadOnlyMemory<byte> data) : base(data)
        {
        }
    }
}