using System;
using System.Threading.Tasks;
using RfpProxy.AaMiDe;
using RfpProxyLib;
using RfpProxyLib.Messages;

namespace RfpProxy.Gigaset;

public class MessageContext
{
    public required MessageDirection Direction { get; init; }
    public required uint MessageId { get; init; }
    public required RfpIdentifier Rfp { get; init; }
    public required Memory<byte> Data { get; init; }
    public required MacConnection Connection { get; init; }
    
}