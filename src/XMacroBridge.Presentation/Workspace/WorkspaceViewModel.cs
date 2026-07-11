using System.Collections.ObjectModel;
using XMacroBridge.Application.Exporting;
using XMacroBridge.Application.Formats;
using XMacroBridge.Application.Importing;
using XMacroBridge.Core.Abstractions;
using XMacroBridge.Core.Conversion;
using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;
using XMacroBridge.Presentation.Common;

namespace XMacroBridge.Presentation.Workspace;

public sealed class WorkspaceViewModel : ObservableObject
{
    private readonly MacroImportService importService;
    private readonly SafeExportService exportService;
    private readonly INestedMacroResolver resolver;
    private readonly IMacroValidator validator;
    private CancellationTokenSource? operationCancellation;
    private MacroDocument? selectedMacro;
    private string targetFormatId = "razer.macro.xml";
    private bool isBusy;
    private bool selectedMacroHasBlockingErrors;
    private double progressPercent;
    private string statusText = "拖入宏文件或选择文件夹开始转换";

    public WorkspaceViewModel(
        MacroImportService importService,
        SafeExportService exportService,
        INestedMacroResolver resolver,
        IMacroValidator validator)
    {
        this.importService = importService ?? throw new ArgumentNullException(nameof(importService));
        this.exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        ExportFormats =
        [
            new ExportFormatOption("razer.macro.xml", "雷云 4 宏 XML", ".xml"),
            new ExportFormatOption("xmbc.macro.text", "X-Mouse 宏文本", ".txt"),
        ];
    }

    public ObservableCollection<MacroDocument> Macros { get; } = [];

    public ObservableCollection<MacroEventRow> Events { get; } = [];

    public ObservableCollection<ConversionDiagnostic> Diagnostics { get; } = [];

    public IReadOnlyList<ExportFormatOption> ExportFormats { get; }

    public MacroDocument? SelectedMacro
    {
        get => selectedMacro;
        set
        {
            if (SetProperty(ref selectedMacro, value))
            {
                RebuildEventRows();
                EvaluateSelectedMacro();
                OnPropertyChanged(nameof(CanExport));
                OnPropertyChanged(nameof(SelectedMacroSummary));
                OnPropertyChanged(nameof(ExportAvailabilityText));
            }
        }
    }

    public string TargetFormatId
    {
        get => targetFormatId;
        set
        {
            if (SetProperty(ref targetFormatId, value))
            {
                OnPropertyChanged(nameof(SelectedExportFormat));
            }
        }
    }

    public ExportFormatOption SelectedExportFormat =>
        ExportFormats.First(item => string.Equals(item.FormatId, TargetFormatId, StringComparison.OrdinalIgnoreCase));

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(CanImport));
                OnPropertyChanged(nameof(CanExport));
                OnPropertyChanged(nameof(ExportAvailabilityText));
            }
        }
    }

    public bool CanCancel => IsBusy;

    public bool CanImport => !IsBusy;

    public bool CanExport => SelectedMacro is not null && !selectedMacroHasBlockingErrors && !IsBusy;

    public double ProgressPercent
    {
        get => progressPercent;
        private set => SetProperty(ref progressPercent, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public string MacroCountText => $"{Macros.Count} 个宏";

    public string DiagnosticCountText => $"{Diagnostics.Count} 条诊断";

    public string SelectedMacroSummary => SelectedMacro is null
        ? "尚未选择宏"
        : $"{SelectedMacro.Events.Count} 个事件 · {SelectedMacro.SourceFormat ?? "未知来源"}";

    public string ExportAvailabilityText => IsBusy
        ? "操作进行中"
        : SelectedMacro is null
            ? "请先选择一个宏"
            : selectedMacroHasBlockingErrors
                ? "该宏包含阻断错误，请查看兼容性报告"
                : "已通过基础安全检查，可以导出";

    public static WorkspaceViewModel CreateDefault()
    {
        var registry = MacroFormatRegistry.CreateDefault();
        return new WorkspaceViewModel(
            new MacroImportService(registry),
            new SafeExportService(registry),
            new NestedMacroResolver(),
            new MacroValidator());
    }

    public async Task ImportAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (IsBusy)
        {
            StatusText = "请先取消或等待当前操作完成";
            return;
        }

        using var cancellation = new CancellationTokenSource();
        operationCancellation = cancellation;
        IsBusy = true;
        ProgressPercent = 0;
        StatusText = "正在读取宏文件…";
        Macros.Clear();
        Events.Clear();
        Diagnostics.Clear();
        NotifyCollectionSummaries();

        var progress = new Progress<(int Completed, int Total, string FileName)>(item =>
        {
            ProgressPercent = item.Total == 0 ? 0 : item.Completed * 100d / item.Total;
            if (!string.IsNullOrWhiteSpace(item.FileName))
            {
                StatusText = $"正在读取 {item.FileName}";
            }
        });

        try
        {
            var result = await importService
                .ImportAsync(paths, progress: progress, cancellationToken: cancellation.Token)
                .ConfigureAwait(true);
            foreach (var document in result.Documents)
            {
                Macros.Add(document);
            }

            foreach (var diagnostic in result.Diagnostics)
            {
                Diagnostics.Add(diagnostic);
            }

            SelectedMacro = Macros.FirstOrDefault();
            ProgressPercent = 100;
            StatusText = Macros.Count == 0 ? "没有找到可转换的宏" : $"已导入 {Macros.Count} 个宏";
        }
        catch (OperationCanceledException)
        {
            StatusText = "导入已取消";
        }
        finally
        {
            IsBusy = false;
            if (ReferenceEquals(operationCancellation, cancellation))
            {
                operationCancellation = null;
            }

            NotifyCollectionSummaries();
        }
    }

    public async Task<ExportResult> ExportAsync(string targetPath, bool overwrite = false)
    {
        if (IsBusy)
        {
            var busyDiagnostic = new ConversionDiagnostic(
                "WORKSPACE_BUSY",
                DiagnosticSeverity.Warning,
                "请先取消或等待当前操作完成。");
            AppendDiagnostics([busyDiagnostic]);
            return new ExportResult(false, null, [busyDiagnostic]);
        }

        if (SelectedMacro is null)
        {
            var diagnostic = new ConversionDiagnostic("WORKSPACE_NO_SELECTION", DiagnosticSeverity.Error, "请先选择要导出的宏。");
            Diagnostics.Add(diagnostic);
            NotifyCollectionSummaries();
            return new ExportResult(false, null, [diagnostic]);
        }

        using var cancellation = new CancellationTokenSource();
        operationCancellation = cancellation;
        IsBusy = true;
        ProgressPercent = 0;
        StatusText = "正在验证并导出…";
        try
        {
            var document = SelectedMacro;
            if (document.Events.OfType<MacroReferenceEvent>().Any())
            {
                var resolution = resolver.Resolve(document, Macros);
                AppendDiagnostics(resolution.Diagnostics);
                if (resolution.Document is null)
                {
                    StatusText = "嵌套宏展开失败";
                    return new ExportResult(false, null, resolution.Diagnostics);
                }

                document = resolution.Document;
            }

            var validation = validator.Validate(document);
            AppendDiagnostics(validation.Diagnostics);
            if (validation.HasErrors)
            {
                StatusText = "宏包含阻断错误，无法导出";
                return new ExportResult(false, null, validation.Diagnostics);
            }

            var result = await exportService
                .ExportAsync(document, TargetFormatId, targetPath, overwrite, cancellation.Token)
                .ConfigureAwait(true);
            AppendDiagnostics(result.Diagnostics);
            ProgressPercent = result.Succeeded ? 100 : 0;
            StatusText = result.Succeeded ? $"已导出到 {Path.GetFileName(result.OutputPath)}" : "导出失败";
            return result;
        }
        catch (OperationCanceledException)
        {
            StatusText = "导出已取消";
            var cancellationDiagnostic = new ConversionDiagnostic(
                "WORKSPACE_EXPORT_CANCELLED",
                DiagnosticSeverity.Info,
                "导出操作已取消，未写入目标文件。");
            AppendDiagnostics([cancellationDiagnostic]);
            return new ExportResult(false, null, [cancellationDiagnostic]);
        }
        finally
        {
            IsBusy = false;
            if (ReferenceEquals(operationCancellation, cancellation))
            {
                operationCancellation = null;
            }

            NotifyCollectionSummaries();
        }
    }

    public void Cancel() => operationCancellation?.Cancel();

    private void RebuildEventRows()
    {
        Events.Clear();
        if (SelectedMacro is not null)
        {
            foreach (var macroEvent in SelectedMacro.Events.OrderBy(item => item.Sequence))
            {
                Events.Add(CreateEventRow(macroEvent));
            }
        }

        OnPropertyChanged(nameof(SelectedMacroSummary));
    }

    private void EvaluateSelectedMacro()
    {
        selectedMacroHasBlockingErrors = false;
        if (SelectedMacro is null)
        {
            return;
        }

        var document = SelectedMacro;
        if (document.Events.OfType<MacroReferenceEvent>().Any())
        {
            var resolution = resolver.Resolve(document, Macros);
            AppendDiagnostics(WithMacroContext(resolution.Diagnostics, document.Name));
            if (resolution.Document is null || resolution.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
            {
                selectedMacroHasBlockingErrors = true;
                return;
            }

            document = resolution.Document;
        }

        var validation = validator.Validate(document);
        AppendDiagnostics(WithMacroContext(validation.Diagnostics, document.Name));
        selectedMacroHasBlockingErrors = validation.HasErrors;
    }

    private static IEnumerable<ConversionDiagnostic> WithMacroContext(
        IEnumerable<ConversionDiagnostic> diagnostics,
        string macroName) =>
        diagnostics.Select(item => string.IsNullOrWhiteSpace(item.SourceContext)
            ? item with { SourceContext = macroName }
            : item);

    private static MacroEventRow CreateEventRow(MacroEvent macroEvent) => macroEvent switch
    {
        DelayMacroEvent delay => new(delay.Sequence, "延时", $"{delay.Milliseconds} ms"),
        RandomDelayMacroEvent delay => new(delay.Sequence, "随机延时", $"{delay.MinimumMilliseconds}–{delay.MaximumMilliseconds} ms"),
        KeyMacroEvent key => new(key.Sequence, "键盘", $"{key.Transition} · VK {key.VirtualKey}{(key.IsExtended ? " · 扩展" : string.Empty)}"),
        MouseMacroEvent mouse => new(mouse.Sequence, "鼠标", $"{mouse.Transition} · {mouse.Button}"),
        ScanCodeMacroEvent scan => new(scan.Sequence, "扫描码", $"{scan.Transition} · {scan.ScanCode}{(scan.IsExtended ? " · 扩展" : string.Empty)}"),
        XmbcCommandMacroEvent command => new(command.Sequence, "XMBC 命令", $"{command.Category} · {command.RawTag}"),
        MacroReferenceEvent reference => new(reference.Sequence, "嵌套宏", reference.TargetName ?? reference.TargetGuid?.ToString() ?? $"索引 {reference.TargetIndex}"),
        UnknownMacroEvent unknown => new(unknown.Sequence, "未知", $"{unknown.SourceType} · {unknown.RawPayload}"),
        _ => new(macroEvent.Sequence, macroEvent.GetType().Name, string.Empty),
    };

    private void AppendDiagnostics(IEnumerable<ConversionDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (!Diagnostics.Contains(diagnostic))
            {
                Diagnostics.Add(diagnostic);
            }
        }

        NotifyCollectionSummaries();
    }

    private void NotifyCollectionSummaries()
    {
        OnPropertyChanged(nameof(MacroCountText));
        OnPropertyChanged(nameof(DiagnosticCountText));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(ExportAvailabilityText));
    }
}
