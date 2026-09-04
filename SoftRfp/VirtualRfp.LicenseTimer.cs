using System;
using RfpProxy.AaMiDe.AaMiDe.Sys;

namespace RfpProxy.Virtual;

partial class VirtualRfp
{
    private TimeSpan _licenseGracePeriod = TimeSpan.FromMinutes(ushort.MaxValue);

    private void OnLicenseTimer(SysLicenseTimerMessage message)
    {
        if (message.GracePeriod.TotalMinutes > int.MaxValue)
        {
            //query
            var licenseTimer = new SysLicenseTimerMessage(_licenseGracePeriod, message.Md5);
            SendMessage(licenseTimer);
        }
        else
        {
            _licenseGracePeriod = message.GracePeriod;
        }
    }
}