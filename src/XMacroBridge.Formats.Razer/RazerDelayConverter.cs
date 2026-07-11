using System.Globalization;

namespace XMacroBridge.Formats.Razer;

internal static class RazerDelayConverter
{
    public static RazerDelayConversion Convert(string value)
    {
        var seconds = decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (seconds < 0)
        {
            throw new FormatException("延时不能为负数。 ");
        }

        var exactMilliseconds = seconds * 1000m;
        var roundedMilliseconds = decimal.Round(exactMilliseconds, 0, MidpointRounding.AwayFromZero);
        return new RazerDelayConversion(
            checked((long)roundedMilliseconds),
            exactMilliseconds != roundedMilliseconds);
    }
}

internal readonly record struct RazerDelayConversion(long Milliseconds, bool PrecisionLost);
