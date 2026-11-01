using RfpProxy.AaMiDe.AaMiDe.Dnm;
using RfpProxy.AaMiDe.AaMiDe.Nwk;

namespace RfpProxy.Gigaset.Helpers;

public static class DnmMessageExtensions
{
    public static bool IsNonEmptyNwkMessage(this DnmMessage dnm)
    {
        return dnm.Payload is LcDataPayload { Payload: not NwkEmptyPayload };
    }
}