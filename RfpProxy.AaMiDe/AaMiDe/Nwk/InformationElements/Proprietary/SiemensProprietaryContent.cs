using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RfpProxyLib;

namespace RfpProxy.AaMiDe.AaMiDe.Nwk.InformationElements.Proprietary
{

    // TODO: this is a stub
    public class SiemensProperietaryCissRequestSeqContent : SiemensProprietaryContent.SiemensElement
    {
        private int v;

        public SiemensProperietaryCissRequestSeqContent(int v) : base(SiemensProprietaryContent.SiemensType.CissRequestSeq, ReadOnlyMemory<byte>.Empty)
        {
            this.v = v;
        }
    }
    // TODO: this is a stub
    public class SiemensProperietaryTimeDateContent : SiemensProprietaryContent.SiemensElement
    {
        private int v1;
        private int v2;
        private int v3;
        private int v4;
        private int v5;
        private int v6;
        private int v7;
        private int v8;
        private int v9;

        public SiemensProperietaryTimeDateContent(int v1, int v2, int v3, int v4, int v5, int v6, int v7, int v8, int v9) : base(SiemensProprietaryContent.SiemensType.TimeDate, ReadOnlyMemory<byte>.Empty)
        {
            this.v1 = v1;
            this.v2 = v2;
            this.v3 = v3;
            this.v4 = v4;
            this.v5 = v5;
            this.v6 = v6;
            this.v7 = v7;
            this.v8 = v8;
            this.v9 = v9;
        }
    }

    /// <summary>
    /// http://dect.osmocom.org/wiki/Siemens
    /// </summary>
    public class SiemensProprietaryContent : NwkIeProprietaryContent
    {
        public enum SiemensType : byte
        {
            CallerId = 0x28,
            TimeDate = 0x3b,
            Display = 0x54,
            Unnamed = 0x58,
            CissAcknowledgementSeq = 0x59,
            CissRequestSeq= 0x5b ,
        }

        public class SiemensElement
        {
            public SiemensType Type { get; }

            public ReadOnlyMemory<byte> Raw { get; }

            public SiemensElement(SiemensType type, ReadOnlyMemory<byte> data)
            {
                Type = type;
                Raw = data;
            }
        }

        public List<SiemensElement> Elements { get; }

        public override bool HasUnknown => false;//true;

        public SiemensProprietaryContent(ReadOnlyMemory<byte> data)
        {
            Elements = new List<SiemensElement>();
            while (data.Length > 0)
            {
                var length = data.Span[1];
                Elements.Add(new SiemensElement((SiemensType) data.Span[0], data.Slice(2,length)));
                data = data[2..][length..];
            }
        }
        public SiemensProprietaryContent(params SiemensElement[] elements)
        {
            Elements = elements.ToList();
        }

        public override void Log(TextWriter writer)
        {
            foreach (var element in Elements)
            {
                writer.WriteLine();
                writer.Write("\t\t\t");
                if (Enum.IsDefined(typeof(SiemensType), element.Type))
                    writer.Write(element.Type.ToString("G"));
                else
                    writer.Write(element.Type.ToString("x"));
                writer.Write($"({element.Raw.ToHex()})");
            }
        }
    }
}