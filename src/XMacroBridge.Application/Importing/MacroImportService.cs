using XMacroBridge.Application.Formats;
using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;

namespace XMacroBridge.Application.Importing;

public sealed class MacroImportService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xml", ".synapse4", ".xmbcp", ".txt",
    };

    private readonly MacroFormatRegistry registry;

    public MacroImportService(MacroFormatRegistry registry)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<ImportBatchResult> ImportAsync(
        IEnumerable<string> inputPaths,
        MacroImportOptions? options = null,
        IProgress<(int Completed, int Total, string FileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        options ??= new MacroImportOptions();

        var diagnostics = new List<ConversionDiagnostic>();
        var files = ExpandInputPaths(inputPaths, options.MaximumFiles, diagnostics);
        var documents = new List<MacroDocument>();
        var processedFiles = new List<string>();
        long totalBytes = 0;

        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = files[index];
            progress?.Report((index, files.Count, Path.GetFileName(path)));
            try
            {
                var fileInfo = new FileInfo(path);
                totalBytes = checked(totalBytes + fileInfo.Length);
                if (totalBytes > options.MaximumBatchBytes)
                {
                    diagnostics.Add(new ConversionDiagnostic(
                        "IMPORT_BATCH_SIZE_LIMIT",
                        DiagnosticSeverity.Error,
                        $"导入批次超过 {options.MaximumBatchBytes} 字节上限。",
                        SourceContext: Path.GetFileName(path)));
                    break;
                }

                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var headerSize = (int)Math.Min(options.HeaderProbeBytes, stream.Length);
                var header = new byte[headerSize];
                var read = await stream.ReadAsync(header, cancellationToken).ConfigureAwait(false);
                stream.Position = 0;
                var importer = registry.FindImporter(header.AsSpan(0, read), path);
                if (importer is null)
                {
                    diagnostics.Add(new ConversionDiagnostic(
                        "IMPORT_FORMAT_UNSUPPORTED",
                        DiagnosticSeverity.Error,
                        $"无法识别文件格式：{Path.GetFileName(path)}。",
                        SourceContext: Path.GetFileName(path)));
                    processedFiles.Add(path);
                    continue;
                }

                var result = await importer.ImportAsync(stream, path, cancellationToken).ConfigureAwait(false);
                documents.AddRange(result.Documents);
                diagnostics.AddRange(result.Diagnostics);
                processedFiles.Add(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OverflowException)
            {
                diagnostics.Add(new ConversionDiagnostic(
                    "IMPORT_FILE_ERROR",
                    DiagnosticSeverity.Error,
                    $"无法读取 {Path.GetFileName(path)}：{exception.Message}",
                    SourceContext: Path.GetFileName(path)));
            }
        }

        progress?.Report((files.Count, files.Count, string.Empty));
        return new ImportBatchResult(documents, diagnostics, processedFiles);
    }

    private static IReadOnlyList<string> ExpandInputPaths(
        IEnumerable<string> inputPaths,
        int maximumFiles,
        ICollection<ConversionDiagnostic> diagnostics)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inputPath in inputPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var fullPath = Path.GetFullPath(inputPath);
            if (File.Exists(fullPath))
            {
                AddFile(fullPath, result, maximumFiles, diagnostics);
            }
            else if (Directory.Exists(fullPath))
            {
                EnumerateDirectorySafely(fullPath, result, maximumFiles, diagnostics);
            }
            else
            {
                diagnostics.Add(new ConversionDiagnostic(
                    "IMPORT_PATH_NOT_FOUND",
                    DiagnosticSeverity.Error,
                    $"输入路径不存在：{Path.GetFileName(fullPath)}。",
                    SourceContext: Path.GetFileName(fullPath)));
            }

            if (result.Count >= maximumFiles)
            {
                break;
            }
        }

        return result.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void EnumerateDirectorySafely(
        string root,
        ISet<string> files,
        int maximumFiles,
        ICollection<ConversionDiagnostic> diagnostics)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0 && files.Count < maximumFiles)
        {
            var directory = pending.Pop();
            try
            {
                foreach (var file in directory.EnumerateFiles().OrderBy(item => item.FullName, StringComparer.OrdinalIgnoreCase))
                {
                    AddFile(file.FullName, files, maximumFiles, diagnostics);
                    if (files.Count >= maximumFiles)
                    {
                        return;
                    }
                }

                foreach (var child in directory.EnumerateDirectories().OrderByDescending(item => item.FullName, StringComparer.OrdinalIgnoreCase))
                {
                    if ((child.Attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(child);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new ConversionDiagnostic(
                    "IMPORT_DIRECTORY_ERROR",
                    DiagnosticSeverity.Warning,
                    $"无法枚举目录 {directory.Name}：{exception.Message}",
                    SourceContext: directory.Name));
            }
        }
    }

    private static void AddFile(
        string path,
        ISet<string> files,
        int maximumFiles,
        ICollection<ConversionDiagnostic> diagnostics)
    {
        if (!SupportedExtensions.Contains(Path.GetExtension(path)))
        {
            return;
        }

        if (files.Count >= maximumFiles)
        {
            diagnostics.Add(new ConversionDiagnostic(
                "IMPORT_FILE_COUNT_LIMIT",
                DiagnosticSeverity.Error,
                $"导入文件数量超过上限 {maximumFiles}。"));
            return;
        }

        files.Add(Path.GetFullPath(path));
    }
}
