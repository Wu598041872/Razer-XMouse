using System.IO;

namespace XMacroBridge.Core.Diagnostics;

public static class DiagnosticContext
{
    public static string? FromSourceName(string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return null;
        }

        try
        {
            var fileName = Path.GetFileName(sourceName.Trim());
            return string.IsNullOrWhiteSpace(fileName) ? "输入文件" : fileName;
        }
        catch (ArgumentException)
        {
            return "输入文件";
        }
    }
}
