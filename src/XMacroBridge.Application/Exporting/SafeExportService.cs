using XMacroBridge.Application.Formats;
using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;

namespace XMacroBridge.Application.Exporting;

public sealed class SafeExportService
{
    private readonly MacroFormatRegistry registry;

    public SafeExportService(MacroFormatRegistry registry)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<ExportResult> ExportAsync(
        MacroDocument document,
        string formatId,
        string targetPath,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(formatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var diagnostics = new List<ConversionDiagnostic>();
        if (!registry.TryGetExporter(formatId, out var exporter) || exporter is null)
        {
            diagnostics.Add(new ConversionDiagnostic(
                "EXPORT_FORMAT_UNSUPPORTED",
                DiagnosticSeverity.Error,
                $"没有可用的导出格式：{formatId}。"));
            return new ExportResult(false, null, diagnostics);
        }

        string fullTargetPath;
        try
        {
            fullTargetPath = Path.GetFullPath(targetPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            diagnostics.Add(new ConversionDiagnostic(
                "EXPORT_PATH_INVALID",
                DiagnosticSeverity.Error,
                $"导出路径无效：{exception.Message}"));
            return new ExportResult(false, null, diagnostics);
        }

        if (!string.IsNullOrWhiteSpace(document.SourcePath) &&
            PathsEqual(document.SourcePath, fullTargetPath))
        {
            diagnostics.Add(new ConversionDiagnostic(
                "EXPORT_SOURCE_OVERWRITE_BLOCKED",
                DiagnosticSeverity.Error,
                "禁止将输出直接写回原始输入文件。",
                SourceContext: Path.GetFileName(fullTargetPath)));
            return new ExportResult(false, null, diagnostics);
        }

        if (File.Exists(fullTargetPath) && !overwrite)
        {
            diagnostics.Add(new ConversionDiagnostic(
                "EXPORT_TARGET_EXISTS",
                DiagnosticSeverity.Error,
                "目标文件已存在，且未授权覆盖。",
                SourceContext: Path.GetFileName(fullTargetPath)));
            return new ExportResult(false, null, diagnostics);
        }

        var directory = Path.GetDirectoryName(fullTargetPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            diagnostics.Add(new ConversionDiagnostic(
                "EXPORT_DIRECTORY_INVALID",
                DiagnosticSeverity.Error,
                "无法确定导出目录。"));
            return new ExportResult(false, null, diagnostics);
        }

        var fileName = Path.GetFileName(fullTargetPath);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            diagnostics.Add(new ConversionDiagnostic(
                "EXPORT_FILE_NAME_INVALID",
                DiagnosticSeverity.Error,
                "导出文件名为空或包含 Windows 非法字符。"));
            return new ExportResult(false, null, diagnostics);
        }

        if (!string.Equals(fileName, fileName.TrimEnd(' ', '.'), StringComparison.Ordinal) || IsReservedFileName(fileName))
        {
            diagnostics.Add(new ConversionDiagnostic(
                "EXPORT_FILE_NAME_RESERVED",
                DiagnosticSeverity.Error,
                "导出文件名是 Windows 保留名称，或以点/空格结尾。"));
            return new ExportResult(false, null, diagnostics);
        }

        var temporaryPath = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
        var backupPath = temporaryPath + ".bak";
        try
        {
            Directory.CreateDirectory(directory);
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var exportDiagnostics = await exporter.ExportAsync(document, stream, cancellationToken).ConfigureAwait(false);
                diagnostics.AddRange(exportDiagnostics);
                if (diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
                {
                    return new ExportResult(false, null, diagnostics);
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(fullTargetPath))
            {
                File.Replace(temporaryPath, fullTargetPath, backupPath, true);
                File.Delete(backupPath);
            }
            else
            {
                File.Move(temporaryPath, fullTargetPath);
            }

            return new ExportResult(true, fullTargetPath, diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new ConversionDiagnostic(
                "EXPORT_FILE_ERROR",
                DiagnosticSeverity.Error,
                $"导出文件失败：{exception.Message}",
                SourceContext: fileName));
            return new ExportResult(false, null, diagnostics);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
            DeleteIfExists(backupPath);
        }
    }

    private static bool PathsEqual(string first, string second)
    {
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsReservedFileName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName).TrimEnd(' ', '.').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CLOCK$")
        {
            return true;
        }

        return stem.Length == 4
            && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
            && stem[3] is >= '1' and <= '9';
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best-effort. The original error/result must remain authoritative.
        }
    }
}
