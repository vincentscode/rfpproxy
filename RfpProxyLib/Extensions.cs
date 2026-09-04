using System;
using System.Buffers;
using System.Text;

namespace RfpProxyLib
{
    public static class Extensions
    {
        public static string CString(this ReadOnlySpan<byte> data)
        {
            var eos = data.IndexOf((byte) 0);
            return eos < 0 ? string.Empty : Encoding.UTF8.GetString(data[..eos]);
        }

        public static bool IsEmpty(this Span<byte> data)
        {
            foreach (var b in data)
            {
                if (b != 0x0) return false;
            }

            return true;
        }

        public static bool IsEmpty(this ReadOnlySpan<byte> data)
        {
            foreach (var b in data)
            {
                if (b != 0x0) return false;
            }

            return true;
        }

        public static ReadOnlyMemory<byte> ToMemory(this ReadOnlySequence<byte> source)
        {
            if (source.IsSingleSegment)
            {
                return source.First;
            }
            return source.ToArray();
        }
    }
}