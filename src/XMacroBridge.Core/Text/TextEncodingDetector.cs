using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace XMacroBridge.Core.Text;

public static class TextEncodingDetector
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UnicodeEncoding StrictUtf16LittleEndian = new(false, false, true);
    private static readonly UnicodeEncoding StrictUtf16BigEndian = new(true, false, true);
    private static readonly byte[] Utf8Preamble = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf16LittleEndianPreamble = [0xFF, 0xFE];
    private static readonly byte[] Utf16BigEndianPreamble = [0xFE, 0xFF];
    private static readonly byte[] Utf32LittleEndianPreamble = [0xFF, 0xFE, 0x00, 0x00];
    private static readonly byte[] Utf32BigEndianPreamble = [0x00, 0x00, 0xFE, 0xFF];
    private static readonly Regex XmlEncodingPattern = new(
        @"\bencoding\s*=\s*(?:'(?<single>[^']+)'|""(?<double>[^""]+)"")",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        var detected = Detect(bytes);
        return GetStrictEncoding(detected.Kind).GetString(bytes[detected.PreambleLength..]);
    }

    public static string DecodeUtf8(ReadOnlySpan<byte> bytes)
    {
        var detected = Detect(bytes);
        if (detected.Kind != EncodingKind.Utf8)
        {
            throw new InvalidDataException("该内容只允许 UTF-8 编码。 ");
        }

        return StrictUtf8.GetString(bytes[detected.PreambleLength..]);
    }

    public static void ValidateXmlEncoding(ReadOnlySpan<byte> bytes)
    {
        var detected = Detect(bytes);
        var content = bytes[detected.PreambleLength..];
        _ = GetStrictEncoding(detected.Kind).GetCharCount(content);

        var prefixLength = Math.Min(content.Length, 8_192);
        if (detected.Kind is EncodingKind.Utf16LittleEndian or EncodingKind.Utf16BigEndian && prefixLength % 2 != 0)
        {
            prefixLength--;
        }

        var prefix = GetLenientEncoding(detected.Kind).GetString(content[..prefixLength]).TrimStart();
        if (!prefix.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var declarationEnd = prefix.IndexOf("?>", StringComparison.Ordinal);
        if (declarationEnd < 0)
        {
            throw new InvalidDataException("XML 声明过长或不完整。 ");
        }

        var match = XmlEncodingPattern.Match(prefix[..declarationEnd]);
        if (!match.Success)
        {
            return;
        }

        var declared = (match.Groups["single"].Success ? match.Groups["single"].Value : match.Groups["double"].Value)
            .Trim()
            .ToLowerInvariant();
        var isAllowed = detected.Kind switch
        {
            EncodingKind.Utf8 => declared is "utf-8" or "utf8",
            EncodingKind.Utf16LittleEndian => declared is "utf-16" or "utf-16le",
            EncodingKind.Utf16BigEndian => declared is "utf-16" or "utf-16be",
            _ => false,
        };
        if (!isAllowed)
        {
            throw new InvalidDataException($"XML 声明的编码 {declared} 不在允许范围内或与 BOM 不一致。 ");
        }
    }

    public static bool TryDecodePrefix(ReadOnlySpan<byte> bytes, out string text)
    {
        try
        {
            var detected = Detect(bytes);
            var content = bytes[detected.PreambleLength..];
            if (detected.Kind is EncodingKind.Utf16LittleEndian or EncodingKind.Utf16BigEndian && content.Length % 2 != 0)
            {
                content = content[..^1];
            }

            text = GetLenientEncoding(detected.Kind).GetString(content);
            return true;
        }
        catch (InvalidDataException)
        {
            text = string.Empty;
            return false;
        }
    }

    private static DetectedEncoding Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(Utf32BigEndianPreamble) || bytes.StartsWith(Utf32LittleEndianPreamble))
        {
            throw new InvalidDataException("不支持 UTF-32 编码。 ");
        }

        if (bytes.StartsWith(Utf8Preamble))
        {
            return new DetectedEncoding(EncodingKind.Utf8, 3);
        }

        if (bytes.StartsWith(Utf16LittleEndianPreamble))
        {
            return new DetectedEncoding(EncodingKind.Utf16LittleEndian, 2);
        }

        if (bytes.StartsWith(Utf16BigEndianPreamble))
        {
            return new DetectedEncoding(EncodingKind.Utf16BigEndian, 2);
        }

        return new DetectedEncoding(EncodingKind.Utf8, 0);
    }

    private static Encoding GetStrictEncoding(EncodingKind kind) => kind switch
    {
        EncodingKind.Utf8 => StrictUtf8,
        EncodingKind.Utf16LittleEndian => StrictUtf16LittleEndian,
        EncodingKind.Utf16BigEndian => StrictUtf16BigEndian,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static Encoding GetLenientEncoding(EncodingKind kind) => kind switch
    {
        EncodingKind.Utf8 => Encoding.UTF8,
        EncodingKind.Utf16LittleEndian => Encoding.Unicode,
        EncodingKind.Utf16BigEndian => Encoding.BigEndianUnicode,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private enum EncodingKind
    {
        Utf8,
        Utf16LittleEndian,
        Utf16BigEndian,
    }

    private readonly record struct DetectedEncoding(EncodingKind Kind, int PreambleLength);
}
