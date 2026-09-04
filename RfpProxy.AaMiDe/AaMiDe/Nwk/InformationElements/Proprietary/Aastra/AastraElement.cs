using System.IO;

namespace RfpProxy.AaMiDe.AaMiDe.Nwk.InformationElements.Proprietary.Aastra
{
    public abstract class AastraElement
    {
        public abstract bool HasUnknown { get; }

        public abstract void Log(TextWriter writer);
    }
}
