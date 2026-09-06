using System.Diagnostics;
using RfpProxy.AaMiDe.AaMiDe.Mac;
using RfpProxyLib;

namespace RfpProxy.AaMiDe
{
    public class MacConnection(RfpConnectionTracker tracker, byte mcei)
    {
        /// <summary>
        /// MAC Connection Endpoint Identification
        /// </summary>
        public byte MCEI { get; } = mcei;

        /// <summary>
        /// Portable part MAC IDentity
        /// </summary>
        public uint PMID { get; private set; }
        
        public NwkReassembler Reassembler { get; private set; }

        public bool IsConnected { get; private set; } = false;

        public void Open(MacConIndPayload macConInd)
        {
            PMID = macConInd.PMID;
            Reassembler = new NwkReassembler();
            if (macConInd.Ho)
            {
                var previous = tracker.Find(PMID);
                if (previous != null)
                    Reassembler.CopyFrom(previous.Reassembler);
            }
            IsConnected = true;
        }
        
        public void Open(MacConExtIndPayload macConExtInd)
        {
            PMID = MacConExtIndPayload.TPUI2PMID[macConExtInd.TPUI.ToHex()];
            Reassembler = new NwkReassembler();
            IsConnected = true;
        }

        public void Close()
        {
            if (IsConnected)
                Reassembler.Clear();
            tracker.Close(this);
        }

        public override string ToString()
        {
            return $"Connection: MCEI={MCEI}, PMID={PMID}, IsConnected={IsConnected}";
        }
    }
}