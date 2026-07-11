using System.Collections.ObjectModel;
using System.Globalization;
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
    private const int MaximumEditHistoryEntries = 20;
    private readonly MacroImportService importService;
    private readonly SafeExportService exportService;
    private readonly INestedMacroResolver resolver;
    private readonly IMacroValidator validator;
    private readonly List<MacroEditHistoryEntry> undoHistory = [];
    private readonly List<MacroEditHistoryEntry> redoHistory = [];
    private readonly Dictionary<MacroDocument, List<ConversionDiagnostic>> evaluationDiagnostics =
        new(ReferenceEqualityComparer.Instance);
    private CancellationTokenSource? operationCancellation;
    private MacroDocument? selectedMacro;
    private MacroEventRow? selectedEvent;
    private string targetFormatId = "razer.macro.xml";
    private bool isBusy;
    private bool selectedMacroHasBlockingErrors;
    private double progressPercent;
    private string statusText = "拖入宏文件或选择文件夹开始转换";
    private DiagnosticSeverityOption selectedDiagnosticSeverity;
    private DiagnosticScopeOption selectedDiagnosticScope;
    private string delayMillisecondsText = string.Empty;
    private string delayScalePercentText = "100";

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
        DiagnosticSeverityOptions =
        [
            new DiagnosticSeverityOption("全部级别", null),
            new DiagnosticSeverityOption("仅错误", DiagnosticSeverity.Error),
            new DiagnosticSeverityOption("仅警告", DiagnosticSeverity.Warning),
            new DiagnosticSeverityOption("仅信息", DiagnosticSeverity.Info),
        ];
        selectedDiagnosticSeverity = DiagnosticSeverityOptions[0];
        selectedDiagnosticScope = new DiagnosticScopeOption("全部来源", null);
        DiagnosticScopes.Add(selectedDiagnosticScope);
        Diagnostics.CollectionChanged += (_, _) => RebuildDiagnosticView();
    }

    public ObservableCollection<MacroDocument> Macros { get; } = [];

    public ObservableCollection<MacroEventRow> Events { get; } = [];

    public ObservableCollection<ConversionDiagnostic> Diagnostics { get; } = [];

    public ObservableCollection<DiagnosticGroup> DiagnosticGroups { get; } = [];

    public ObservableCollection<DiagnosticScopeOption> DiagnosticScopes { get; } = [];

    public IReadOnlyList<ExportFormatOption> ExportFormats { get; }

    public IReadOnlyList<DiagnosticSeverityOption> DiagnosticSeverityOptions { get; }

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
                NotifyEditingState();
            }
        }
    }

    public MacroEventRow? SelectedEvent
    {
        get => selectedEvent;
        set
        {
            if (SetProperty(ref selectedEvent, value))
            {
                DelayMillisecondsText = value?.Event is DelayMacroEvent delay
                    ? delay.Milliseconds.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
                OnPropertyChanged(nameof(CanUpdateSelectedDelay));
            }
        }
    }

    public string DelayMillisecondsText
    {
        get => delayMillisecondsText;
        set => SetProperty(ref delayMillisecondsText, value ?? string.Empty);
    }

    public string DelayScalePercentText
    {
        get => delayScalePercentText;
        set => SetProperty(ref delayScalePercentText, value ?? string.Empty);
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

    public DiagnosticSeverityOption SelectedDiagnosticSeverity
    {
        get => selectedDiagnosticSeverity;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref selectedDiagnosticSeverity, value))
            {
                RebuildDiagnosticGroups();
            }
        }
    }

    public DiagnosticScopeOption SelectedDiagnosticScope
    {
        get => selectedDiagnosticScope;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref selectedDiagnosticScope, value))
            {
                RebuildDiagnosticGroups();
            }
        }
    }

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
                NotifyEditingState();
            }
        }
    }

    public bool CanCancel => IsBusy;

    public bool CanImport => !IsBusy;

    public bool CanExport => SelectedMacro is not null && !selectedMacroHasBlockingErrors && !IsBusy;

    public bool CanUpdateSelectedDelay =>
        !IsBusy && SelectedMacro is not null && SelectedEvent?.Event is DelayMacroEvent;

    public bool CanScaleDelays =>
        !IsBusy && SelectedMacro?.Events.Any(item => item is DelayMacroEvent or RandomDelayMacroEvent) == true;

    public bool CanUndo => !IsBusy && undoHistory.Count > 0;

    public bool CanRedo => !IsBusy && redoHistory.Count > 0;

    public string UndoAvailabilityText => undoHistory.Count == 0
        ? "没有可撤销的编辑"
        : $"撤销：{undoHistory[^1].Description}";

    public string RedoAvailabilityText => redoHistory.Count == 0
        ? "没有可重做的编辑"
        : $"重做：{redoHistory[^1].Description}";

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

    public string DiagnosticCountText => FilteredDiagnosticCount == Diagnostics.Count
        ? $"{Diagnostics.Count} 条诊断"
        : $"显示 {FilteredDiagnosticCount} / {Diagnostics.Count} 条";

    public int FilteredDiagnosticCount => DiagnosticGroups.Sum(item => item.Diagnostics.Count);

    public bool HasFilteredDiagnostics => FilteredDiagnosticCount > 0;

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
        ClearEditHistory();
        evaluationDiagnostics.Clear();
        SelectedMacro = null;
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
                AppendDiagnostics(WithMacroContext(resolution.Diagnostics, document.Name));
                if (resolution.Document is null)
                {
                    StatusText = "嵌套宏展开失败";
                    return new ExportResult(false, null, resolution.Diagnostics);
                }

                document = resolution.Document;
            }

            var validation = validator.Validate(document);
            AppendDiagnostics(WithMacroContext(validation.Diagnostics, document.Name));
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

    public bool UpdateSelectedDelay()
    {
        if (IsBusy)
        {
            StatusText = "操作进行中，暂时不能编辑时间线";
            return false;
        }

        if (SelectedMacro is null || SelectedEvent is null ||
            SelectedEvent.EventIndex < 0 || SelectedEvent.EventIndex >= SelectedMacro.Events.Count ||
            SelectedMacro.Events[SelectedEvent.EventIndex] is not DelayMacroEvent delay)
        {
            StatusText = "请先在时间线中选择一个固定延时事件";
            return false;
        }

        if (!long.TryParse(
                DelayMillisecondsText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var milliseconds) || milliseconds < 0)
        {
            StatusText = "延时必须是大于或等于 0 的整数毫秒";
            return false;
        }

        if (delay.Milliseconds == milliseconds)
        {
            StatusText = "延时数值没有变化";
            return false;
        }

        var events = SelectedMacro.Events.ToArray();
        events[SelectedEvent.EventIndex] = delay with { Milliseconds = milliseconds };
        return ApplyMacroEdit(
            SelectedMacro,
            events,
            SelectedEvent.EventIndex,
            $"事件 {delay.Sequence} 延时改为 {milliseconds} ms");
    }

    public bool ScaleAllDelays()
    {
        if (IsBusy)
        {
            StatusText = "操作进行中，暂时不能编辑时间线";
            return false;
        }

        if (SelectedMacro is null)
        {
            StatusText = "请先选择要编辑的宏";
            return false;
        }

        const NumberStyles styles = NumberStyles.AllowLeadingSign |
                                    NumberStyles.AllowDecimalPoint |
                                    NumberStyles.AllowLeadingWhite |
                                    NumberStyles.AllowTrailingWhite;
        if (!decimal.TryParse(
                DelayScalePercentText,
                styles,
                CultureInfo.InvariantCulture,
                out var percentage) || percentage < 0)
        {
            StatusText = "缩放比例必须是大于或等于 0 的百分比，例如 125 或 50.5";
            return false;
        }

        var events = SelectedMacro.Events.ToArray();
        var foundDelay = false;
        var changed = false;
        for (var index = 0; index < events.Length; index++)
        {
            switch (events[index])
            {
                case DelayMacroEvent delay:
                    foundDelay = true;
                    if (!TryScaleDelay(delay.Milliseconds, percentage, out var scaledDelay))
                    {
                        StatusText = "宏包含无效延时，或缩放结果超出 64 位毫秒范围";
                        return false;
                    }

                    if (scaledDelay != delay.Milliseconds)
                    {
                        events[index] = delay with { Milliseconds = scaledDelay };
                        changed = true;
                    }

                    break;
                case RandomDelayMacroEvent randomDelay:
                    foundDelay = true;
                    if (randomDelay.MinimumMilliseconds > randomDelay.MaximumMilliseconds ||
                        !TryScaleDelay(randomDelay.MinimumMilliseconds, percentage, out var scaledMinimum) ||
                        !TryScaleDelay(randomDelay.MaximumMilliseconds, percentage, out var scaledMaximum))
                    {
                        StatusText = "宏包含无效随机延时，或缩放结果超出 64 位毫秒范围";
                        return false;
                    }

                    if (scaledMinimum != randomDelay.MinimumMilliseconds ||
                        scaledMaximum != randomDelay.MaximumMilliseconds)
                    {
                        events[index] = randomDelay with
                        {
                            MinimumMilliseconds = scaledMinimum,
                            MaximumMilliseconds = scaledMaximum,
                        };
                        changed = true;
                    }

                    break;
            }
        }

        if (!foundDelay)
        {
            StatusText = "当前宏没有可缩放的延时事件";
            return false;
        }

        if (!changed)
        {
            StatusText = "缩放后延时数值没有变化";
            return false;
        }

        return ApplyMacroEdit(
            SelectedMacro,
            events,
            SelectedEvent?.EventIndex,
            $"全部延时缩放为 {percentage.ToString(CultureInfo.InvariantCulture)}%");
    }

    public bool Undo()
    {
        if (!CanUndo)
        {
            StatusText = IsBusy ? "操作进行中，暂时不能撤销" : "没有可撤销的编辑";
            return false;
        }

        var entry = undoHistory[^1];
        if (!ReplaceMacro(entry.Before, entry.SelectedEventIndex, entry.After))
        {
            ClearEditHistory();
            StatusText = "撤销目标已不在工作区，编辑历史已清空";
            return false;
        }

        undoHistory.RemoveAt(undoHistory.Count - 1);
        redoHistory.Add(entry);
        StatusText = $"已撤销：{entry.Description}";
        NotifyEditingState();
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
        {
            StatusText = IsBusy ? "操作进行中，暂时不能重做" : "没有可重做的编辑";
            return false;
        }

        var entry = redoHistory[^1];
        if (!ReplaceMacro(entry.After, entry.SelectedEventIndex, entry.Before))
        {
            ClearEditHistory();
            StatusText = "重做目标已不在工作区，编辑历史已清空";
            return false;
        }

        redoHistory.RemoveAt(redoHistory.Count - 1);
        undoHistory.Add(entry);
        StatusText = $"已重做：{entry.Description}";
        NotifyEditingState();
        return true;
    }

    private void RebuildEventRows()
    {
        SelectedEvent = null;
        Events.Clear();
        if (SelectedMacro is not null)
        {
            foreach (var item in SelectedMacro.Events
                         .Select((macroEvent, index) => (MacroEvent: macroEvent, Index: index))
                         .OrderBy(item => item.MacroEvent.Sequence)
                         .ThenBy(item => item.Index))
            {
                Events.Add(CreateEventRow(item.Index, item.MacroEvent));
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
        var currentMacro = document;
        var currentMacroName = document.Name;
        var diagnostics = new List<ConversionDiagnostic>();
        if (document.Events.OfType<MacroReferenceEvent>().Any())
        {
            var resolution = resolver.Resolve(document, Macros);
            diagnostics.AddRange(WithMacroContext(resolution.Diagnostics, currentMacroName));
            if (resolution.Document is null || resolution.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
            {
                selectedMacroHasBlockingErrors = true;
                ReplaceEvaluationDiagnostics(currentMacro, diagnostics);
                return;
            }

            document = resolution.Document;
        }

        var validation = validator.Validate(document);
        diagnostics.AddRange(WithMacroContext(validation.Diagnostics, currentMacroName));
        selectedMacroHasBlockingErrors = validation.HasErrors;
        ReplaceEvaluationDiagnostics(currentMacro, diagnostics);
    }

    private static IEnumerable<ConversionDiagnostic> WithMacroContext(
        IEnumerable<ConversionDiagnostic> diagnostics,
        string macroName) =>
        diagnostics.Select(item => string.IsNullOrWhiteSpace(item.SourceContext)
            ? item with { SourceContext = macroName }
            : item);

    private static MacroEventRow CreateEventRow(int eventIndex, MacroEvent macroEvent) => macroEvent switch
    {
        DelayMacroEvent delay => new(eventIndex, delay, "延时", $"{delay.Milliseconds} ms"),
        RandomDelayMacroEvent delay => new(eventIndex, delay, "随机延时", $"{delay.MinimumMilliseconds}–{delay.MaximumMilliseconds} ms"),
        KeyMacroEvent key => new(eventIndex, key, "键盘", $"{key.Transition} · VK {key.VirtualKey}{(key.IsExtended ? " · 扩展" : string.Empty)}"),
        MouseMacroEvent mouse => new(eventIndex, mouse, "鼠标", $"{mouse.Transition} · {mouse.Button}"),
        ScanCodeMacroEvent scan => new(eventIndex, scan, "扫描码", $"{scan.Transition} · {scan.ScanCode}{(scan.IsExtended ? " · 扩展" : string.Empty)}"),
        XmbcCommandMacroEvent command => new(eventIndex, command, "XMBC 命令", $"{command.Category} · {command.RawTag}"),
        MacroReferenceEvent reference => new(eventIndex, reference, "嵌套宏", reference.TargetName ?? reference.TargetGuid?.ToString() ?? $"索引 {reference.TargetIndex}"),
        UnknownMacroEvent unknown => new(eventIndex, unknown, "未知", $"{unknown.SourceType} · {unknown.RawPayload}"),
        _ => new(eventIndex, macroEvent, "其他", string.Empty),
    };

    private bool ApplyMacroEdit(
        MacroDocument before,
        MacroEvent[] events,
        int? selectedEventIndex,
        string description)
    {
        if (before.Events.SequenceEqual(events))
        {
            return false;
        }

        var after = before with { Events = Array.AsReadOnly(events) };
        var entry = new MacroEditHistoryEntry(before, after, selectedEventIndex, description);
        undoHistory.Add(entry);
        if (undoHistory.Count > MaximumEditHistoryEntries)
        {
            undoHistory.RemoveAt(0);
        }

        redoHistory.Clear();
        if (!ReplaceMacro(after, selectedEventIndex, before))
        {
            undoHistory.RemoveAt(undoHistory.Count - 1);
            StatusText = "编辑目标已不在工作区，未应用更改";
            NotifyEditingState();
            return false;
        }

        StatusText = $"已完成：{description}";
        NotifyEditingState();
        return true;
    }

    private bool ReplaceMacro(
        MacroDocument document,
        int? selectedEventIndex,
        MacroDocument expectedCurrent)
    {
        var macroIndex = -1;
        for (var index = 0; index < Macros.Count; index++)
        {
            if (ReferenceEquals(Macros[index], expectedCurrent))
            {
                macroIndex = index;
                break;
            }
        }

        if (macroIndex < 0)
        {
            var matchingIndexes = Enumerable.Range(0, Macros.Count)
                .Where(index => Macros[index].Id == expectedCurrent.Id)
                .Take(2)
                .ToArray();
            if (matchingIndexes.Length == 1)
            {
                macroIndex = matchingIndexes[0];
            }
        }

        if (macroIndex < 0)
        {
            return false;
        }

        RemoveEvaluationDiagnostics(expectedCurrent);
        Macros[macroIndex] = document;
        SelectedMacro = document;
        SelectedEvent = selectedEventIndex is null
            ? null
            : Events.FirstOrDefault(item => item.EventIndex == selectedEventIndex.Value);
        return true;
    }

    private static bool TryScaleDelay(long milliseconds, decimal percentage, out long result)
    {
        result = 0;
        if (milliseconds < 0)
        {
            return false;
        }

        try
        {
            var scaled = milliseconds * percentage / 100m;
            if (scaled > long.MaxValue)
            {
                return false;
            }

            result = decimal.ToInt64(decimal.Round(scaled, 0, MidpointRounding.AwayFromZero));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private void ReplaceEvaluationDiagnostics(
        MacroDocument macro,
        IEnumerable<ConversionDiagnostic> diagnostics)
    {
        RemoveEvaluationDiagnostics(macro);

        var ownedDiagnostics = new List<ConversionDiagnostic>();
        foreach (var diagnostic in diagnostics)
        {
            if (!Diagnostics.Contains(diagnostic))
            {
                Diagnostics.Add(diagnostic);
                ownedDiagnostics.Add(diagnostic);
            }
        }

        if (ownedDiagnostics.Count > 0)
        {
            evaluationDiagnostics[macro] = ownedDiagnostics;
        }
    }

    private void RemoveEvaluationDiagnostics(MacroDocument macro)
    {
        if (!evaluationDiagnostics.Remove(macro, out var previousDiagnostics))
        {
            return;
        }

        for (var diagnosticIndex = Diagnostics.Count - 1; diagnosticIndex >= 0; diagnosticIndex--)
        {
            if (previousDiagnostics.Any(previous => ReferenceEquals(previous, Diagnostics[diagnosticIndex])))
            {
                Diagnostics.RemoveAt(diagnosticIndex);
            }
        }
    }

    private void ClearEditHistory()
    {
        undoHistory.Clear();
        redoHistory.Clear();
        NotifyEditingState();
    }

    private void NotifyEditingState()
    {
        OnPropertyChanged(nameof(CanUpdateSelectedDelay));
        OnPropertyChanged(nameof(CanScaleDelays));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoAvailabilityText));
        OnPropertyChanged(nameof(RedoAvailabilityText));
    }

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

    private void RebuildDiagnosticView()
    {
        var previousSource = selectedDiagnosticScope.SourceContext;
        var contexts = Diagnostics
            .Select(item => item.SourceContext)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        DiagnosticScopes.Clear();
        DiagnosticScopes.Add(new DiagnosticScopeOption("全部来源", null));
        foreach (var context in contexts)
        {
            DiagnosticScopes.Add(new DiagnosticScopeOption(FormatScopeName(context!), context));
        }

        selectedDiagnosticScope = DiagnosticScopes.FirstOrDefault(item =>
            string.Equals(item.SourceContext, previousSource, StringComparison.Ordinal)) ?? DiagnosticScopes[0];
        OnPropertyChanged(nameof(SelectedDiagnosticScope));
        RebuildDiagnosticGroups();
    }

    private void RebuildDiagnosticGroups()
    {
        var filtered = Diagnostics.Where(item =>
            (selectedDiagnosticSeverity.Severity is null || item.Severity == selectedDiagnosticSeverity.Severity) &&
            (selectedDiagnosticScope.SourceContext is null ||
             string.Equals(item.SourceContext, selectedDiagnosticScope.SourceContext, StringComparison.Ordinal)));

        DiagnosticGroups.Clear();
        foreach (var group in filtered
                     .GroupBy(item => string.IsNullOrWhiteSpace(item.SourceContext) ? "工作区" : FormatScopeName(item.SourceContext))
                     .OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var ordered = group
                .OrderByDescending(item => item.Severity)
                .ThenBy(item => item.EventSequence)
                .ThenBy(item => item.Code, StringComparer.Ordinal)
                .ToArray();
            DiagnosticGroups.Add(new DiagnosticGroup(group.Key, ordered));
        }

        OnPropertyChanged(nameof(FilteredDiagnosticCount));
        OnPropertyChanged(nameof(HasFilteredDiagnostics));
        OnPropertyChanged(nameof(DiagnosticCountText));
    }

    private static string FormatScopeName(string sourceContext)
    {
        const int maximumLength = 60;
        return sourceContext.Length <= maximumLength
            ? sourceContext
            : sourceContext[..maximumLength] + "…";
    }

    private void NotifyCollectionSummaries()
    {
        OnPropertyChanged(nameof(MacroCountText));
        OnPropertyChanged(nameof(DiagnosticCountText));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(ExportAvailabilityText));
    }

    private sealed record MacroEditHistoryEntry(
        MacroDocument Before,
        MacroDocument After,
        int? SelectedEventIndex,
        string Description);
}
