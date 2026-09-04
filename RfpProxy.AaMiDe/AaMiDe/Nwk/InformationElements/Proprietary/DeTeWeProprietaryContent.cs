using System;
using System.Collections.Generic;
using System.IO;
using RfpProxy.AaMiDe.AaMiDe.Nwk.InformationElements.Proprietary.DeTeWe;

namespace RfpProxy.AaMiDe.AaMiDe.Nwk.InformationElements.Proprietary
{
    public class DeTeWeProprietaryContent:NwkIeProprietaryContent
    {
        public List<DeTeWeElement> Elements { get; }

        public override bool HasUnknown => false;//Elements.Any(x=>x.HasUnknown);

        public DeTeWeProprietaryContent(ReadOnlyMemory<byte> data)
        {
            Elements = new List<DeTeWeElement>();
            while (data.Length > 0)
            {
                var length = data.Span[1];
                Elements.Add(DeTeWeElement.Create((DeTeWeType) data.Span[0], data.Slice(2,length)));
                data = data[2..][length..];
            }
        }

        public override void Log(TextWriter writer)
        {
            foreach (var element in Elements)
            {
                element.Log(writer);
            }
        }
    }
}