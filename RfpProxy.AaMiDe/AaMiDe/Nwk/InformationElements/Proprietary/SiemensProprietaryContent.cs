using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using RfpProxyLib;

namespace RfpProxy.AaMiDe.AaMiDe.Nwk.InformationElements.Proprietary;

public class SiemensProperietaryCissRequestSeqElement(byte sequenceNumber)
    : SiemensProprietaryContent.SiemensElement(SiemensProprietaryContent.SiemensType.CissRequestSeq, ReadOnlyMemory<byte>.Empty)
{
    public SiemensProperietaryCissRequestSeqElement(ReadOnlyMemory<byte> data) : this(data.Span[0])
    {
    }

    public byte SequenceNumber { get; } = sequenceNumber;

    public override byte[] Serialize()
    {
        byte[] body =
        [
            SequenceNumber,
        ];
        return
        [
            (byte)SiemensProprietaryContent.SiemensType.CissRequestSeq,
            (byte)body.Length,
            ..body
        ];
    }
}
public class SiemensProperietaryDisplayElement(byte handsetNumber, byte[] displayString)
    : SiemensProprietaryContent.SiemensElement(SiemensProprietaryContent.SiemensType.Display, ReadOnlyMemory<byte>.Empty)
{
    public byte HandsetNumber { get; } = handsetNumber;
    public byte[] DisplayString { get; } = displayString;

    public SiemensProperietaryDisplayElement(byte handsetNumber, string displayString) : this(handsetNumber, [.. displayString.Select(c => (byte)c)])
    {
    }

    public override byte[] Serialize()
    {
        byte[] body =
        [
            HandsetNumber,
            ..DisplayString
        ];
        return
        [
            (byte)SiemensProprietaryContent.SiemensType.Display,
            (byte)body.Length,
            ..body
        ];
    }
}

public class SiemensProperietaryTimeDateElement(byte unknown1, byte dayBCD, byte monthBCD, byte yearBCD, byte unknown2, byte unknown3, byte hourBCD, byte minuteBCD, byte secondBCD)
    : SiemensProprietaryContent.SiemensElement(SiemensProprietaryContent.SiemensType.TimeDate,
        ReadOnlyMemory<byte>.Empty)
{
    public byte Unknown1 { get; } = unknown1;
    public byte DayBCD { get; } = dayBCD;
    public byte MonthBCD { get; } = monthBCD;
    public byte YearBCD { get; } = yearBCD;
    public byte Unknown2 { get; } = unknown2;
    public byte Unknown3 { get; } = unknown3;
    public byte HourBCD { get; } = hourBCD;
    public byte MinuteBCD { get; } = minuteBCD;
    public byte SecondBCD { get; } = secondBCD;

    public SiemensProperietaryTimeDateElement(DateTime dateTime, byte unk1, byte unk2, byte unk3)
        : this(unk1, ToBCD(dateTime.Day), ToBCD(dateTime.Month), ToBCD(dateTime.Year - 2000), unk2, unk3, ToBCD(dateTime.Hour), ToBCD(dateTime.Minute), ToBCD(dateTime.Second))
    {
    }

    public override byte[] Serialize()
    {
        byte[] body =
        [
            Unknown1,
            DayBCD, MonthBCD, YearBCD,
            Unknown2, Unknown3,
            HourBCD, MinuteBCD, SecondBCD
        ];
        return
        [
            (byte)SiemensProprietaryContent.SiemensType.TimeDate,
            (byte)body.Length,
            ..body
        ];
    }

    private static byte ToBCD(int i)
    {
        Debug.Assert(i <= 99);
        Debug.Assert(i >= 0);
        return byte.Parse($"{i:00}", NumberStyles.HexNumber);
    }
}

/// <summary>
/// https://osmocom.org/projects/dect/wiki/Siemens
/// https://gitea.osmocom.org/dect/libdect/src/branch/master/example/fp-siemens-proprietary.c
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
        CissRequestSeq= 0x5b,
    }

    public class SiemensElement(SiemensType type, ReadOnlyMemory<byte> data)
    {
        public SiemensType Type { get; } = type;

        public ReadOnlyMemory<byte> Raw { get; } = data;

        public virtual byte[] Serialize()
        {
            return
            [
                (byte)Type,
                (byte)Raw.Length,
                ..Raw.ToArray()
            ];
        }
    }

    public List<SiemensElement> Elements { get; }

    public override bool HasUnknown => false;

    public SiemensProprietaryContent(ReadOnlyMemory<byte> data)
    {
        Elements = [];
        while (data.Length > 0)
        {
            var length = data.Span[1];
            Elements.Add(new SiemensElement((SiemensType) data.Span[0], data.Slice(2, length)));
            data = data[2..][length..];
        }
    }
    
    public SiemensProprietaryContent(params SiemensElement[] elements)
    {
        Elements = [.. elements];
    }

    public override void Log(TextWriter writer)
    {
        foreach (var element in Elements)
        {
            writer.WriteLine();
            writer.Write("\t\t\t");
            writer.Write(Enum.IsDefined(typeof(SiemensType), element.Type) ? element.Type.ToString("G") : element.Type.ToString("x"));
            writer.Write($"({element.Raw.ToHex()})");
        }
    }
}