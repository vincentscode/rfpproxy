using System;
using System.Collections.Generic;
using System.IO;
using RfpProxy.AaMiDe.AaMiDe.Dnm;
using RfpProxyLib;

namespace RfpProxy.AaMiDe.AaMiDe.Mac;

public sealed class MacConExtIndPayload : DnmPayload
{
    // TODO: very cursed and ugly, find a better way
    public static readonly Dictionary<string, uint> TPUI2PMID = new();
    
    /// <summary>
    /// Temporary Portable User Identity
    /// </summary>
    // TODO: this should probably be handled similar to NwkIePortableIdentity does it
    public ReadOnlyMemory<byte> TPUI { get; }
    
    // TODO: figure out what these bytes mean, probably somewhat similar to MacConIndPayload?
    public ReadOnlyMemory<byte> Unknown { get; }

    public override bool HasUnknown => true;

    public MacConExtIndPayload(ReadOnlyMemory<byte> data) : base(data)
    {
        TPUI = data[..3];
        Unknown = data[3..];
    }
        
    public override void Log(TextWriter writer)
    {
        writer.Write($" TPUI({TPUI.ToHex()}) Unknown({Unknown.ToHex()})");
    }
}