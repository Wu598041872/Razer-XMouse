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
    private const int MaximumEventSearchLength = 128;
    private readonly MacroImportService importService;
    private readonly SafeExportService exportService;
    private readonly INestedMacroResolver resolver;
    private readonly IMacroValidator validator;
    private readonly MacroLimits limits;
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
    private string newDelayMillisecondsText = "10";
    private string newVirtualKeyText = "65";
    private bool newKeyIsExtended;
    private InputTransitionOption selectedKeyTransition;
    private InputTransitionOption selectedMouseTransition;
    private MouseButtonOption selectedMouseButton;
    private string eventSearchText = string.Empty;
    private int[] eventSearchMatches = [];
    private int currentEventSearchMatch = -1;

    public WorkspaceViewModel(
        MacroImportService importService,
        SafeExportService exportService,
        INestedMacroResolver resolver,
        IMacroValidator validator,
        MacroLimits? limits = null)
    {
        this.importService = importService ?? throw new ArgumentNullException(nameof(importService));
        this.exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
        this.limits = limits ?? new MacroLimits();
        if (this.limits.MaximumEventsPerMacro < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "事件数量上限必须至少为 1。");
        }
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
        InputTransitionOptions =
        [
            new InputTransitionOption("按下", InputTransition.Down),
            new InputTransitionOption("释放", InputTransition.Up),
        ];
        MouseButtonOptions =
        [
            new MouseButtonOption("左键", MouseButton.Left),
            new MouseButtonOption("右键", MouseButton.Right),
            new MouseButtonOption("中键", MouseButton.Middle),
            new MouseButtonOption("侧键 1 / MB4", MouseButton.XButton1),
            new MouseButtonOption("侧键 2 / MB5", MouseButton.XButton2),
            new MouseButtonOption("滚轮向上", MouseButton.WheelUp),
            new MouseButtonOption("滚轮向下", MouseButton.WheelDown),
            new MouseButtonOption("滚轮左倾", MouseButton.TiltLeft),
            new MouseButtonOption("滚轮右倾", MouseButton.TiltRight),
        ];
        selectedKeyTransition = InputTransitionOptions[0];
        selectedMouseTransition = InputTransitionOptions[0];
        selectedMouseButton = MouseButtonOptions[0];
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

    public IReadOnlyList<InputTransitionOption> InputTransitionOptions { get; }

    public IReadOnlyList<MouseButtonOption> MouseButtonOptions { get; }

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
                SynchronizeEventSearchPosition();
                NotifyEditingState();
            }
        }
    }

    public string EventSearchText
    {
        get => eventSearchText;
        set
        {
            var normalized = value ?? string.Empty;
            if (normalized.Length > MaximumEventSearchLength)
            {
                normalized = normalized[..MaximumEventSearchLength];
            }

            if (SetProperty(ref eventSearchText, normalized))
            {
                RebuildEventSearchResults();
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

    public string NewDelayMillisecondsText
    {
        get => newDelayMillisecondsText;
        set => SetProperty(ref newDelayMillisecondsText, value ?? string.Empty);
    }

    public string NewVirtualKeyText
    {
        get => newVirtualKeyText;
        set => SetProperty(ref newVirtualKeyText, value ?? string.Empty);
    }

    public bool NewKeyIsExtended
    {
        get => newKeyIsExtended;
        set => SetProperty(ref newKeyIsExtended, value);
    }

    public InputTransitionOption SelectedKeyTransition
    {
        get => selectedKeyTransition;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetProperty(ref selectedKeyTransition, value);
        }
    }

    public MouseButtonOption SelectedMouseButton
    {
        get => selectedMouseButton;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetProperty(ref selectedMouseButton, value);
        }
    }

    public InputTransitionOption SelectedMouseTransition
    {
        get => selectedMouseTransition;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetProperty(ref selectedMouseTransition, value);
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

    public bool CanInsertEvent =>
        !IsBusy && SelectedMacro is not null && SelectedMacro.Events.Count < limits.MaximumEventsPerMacro;

    public bool CanInsertDelay => CanInsertEvent;

    public bool CanDeleteEvent => !IsBusy && SelectedMacro is not null && SelectedEvent is not null;

    public bool CanCopyEvent =>
        CanDeleteEvent && SelectedMacro!.Events.Count < limits.MaximumEventsPerMacro;

    public bool CanMoveEventUp => CanDeleteEvent && SelectedEvent!.DisplayIndex > 0;

    public bool CanMoveEventDown =>
        CanDeleteEvent && SelectedEvent!.DisplayIndex < Events.Count - 1;

    public bool CanScaleDelays =>
        !IsBusy && SelectedMacro?.Events.Any(item => item is DelayMacroEvent or RandomDelayMacroEvent) == true;

    public bool CanUndo => !IsBusy && undoHistory.Count > 0;

    public bool CanRedo => !IsBusy && redoHistory.Count > 0;

    public bool CanFindPreviousEvent => !IsBusy && eventSearchMatches.Length > 0;

    public bool CanFindNextEvent => !IsBusy && eventSearchMatches.Length > 0;

    public string UndoAvailabilityText => undoHistory.Count == 0
        ? "没有可撤销的编辑"
        : $"撤销：{undoHistory[^1].Description}";

    public string RedoAvailabilityText => redoHistory.Count == 0
        ? "没有可重做的编辑"
        : $"重做：{redoHistory[^1].Description}";

    public string EventSearchResultText => string.IsNullOrWhiteSpace(EventSearchText)
        ? "未搜索"
        : eventSearchMatches.Length == 0
            ? "0 / 0"
            : currentEventSearchMatch < 0
                ? $"0 / {eventSearchMatches.Length}"
                : $"{currentEventSearchMatch + 1} / {eventSearchMatches.Length}";

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

    public static WorkspaceViewModel CreateDefault(MacroLimits? limits = null)
    {
        var registry = MacroFormatRegistry.CreateDefault();
        return new WorkspaceViewModel(
            new MacroImportService(registry),
            new SafeExportService(registry),
            new NestedMacroResolver(),
            new MacroValidator(),
            limits);
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
                var resolution = resolver.Resolve(document, Macros, limits);
                AppendDiagnostics(WithMacroContext(resolution.Diagnostics, document.Name));
                if (resolution.Document is null)
                {
                    StatusText = "嵌套宏展开失败";
                    return new ExportResult(false, null, resolution.Diagnostics);
                }

                document = resolution.Document;
            }

            var validation = validator.Validate(document, limits);
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
            SelectedEvent?.EventIndex,
            $"全部延时缩放为 {percentage.ToString(CultureInfo.InvariantCulture)}%");
    }

    public bool InsertDelayAfterSelection()
    {
        if (!EnsureCanInsertEvent("固定延时"))
        {
            return false;
        }

        if (!long.TryParse(
                NewDelayMillisecondsText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var milliseconds) || milliseconds < 0)
        {
            StatusText = "新延时必须是大于或等于 0 的整数毫秒";
            return false;
        }

        return InsertTimelineEvent(
            new DelayMacroEvent(0, milliseconds),
            $"插入 {milliseconds} ms 固定延时");
    }

    public bool InsertKeyboardEvent()
    {
        if (!EnsureCanInsertEvent("键盘事件"))
        {
            return false;
        }

        if (!int.TryParse(
                NewVirtualKeyText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var virtualKey) || virtualKey is < 0 or > 255)
        {
            StatusText = "虚拟键码必须是 0–255 范围内的十进制整数";
            return false;
        }

        var extendedText = NewKeyIsExtended ? "，扩展键" : string.Empty;
        return InsertTimelineEvent(
            new KeyMacroEvent(
                0,
                virtualKey,
                SelectedKeyTransition.Transition,
                IsExtended: NewKeyIsExtended),
            $"插入键盘{SelectedKeyTransition.DisplayName}事件（VK {virtualKey}{extendedText}）");
    }

    public bool InsertMouseEvent()
    {
        if (!EnsureCanInsertEvent("鼠标事件"))
        {
            return false;
        }

        return InsertTimelineEvent(
            new MouseMacroEvent(
                0,
                SelectedMouseButton.Button,
                SelectedMouseTransition.Transition),
            $"插入鼠标{SelectedMouseButton.DisplayName}{SelectedMouseTransition.DisplayName}事件");
    }

    private bool EnsureCanInsertEvent(string eventName)
    {
        if (IsBusy)
        {
            StatusText = $"操作进行中，暂时不能插入{eventName}";
            return false;
        }

        if (SelectedMacro is null)
        {
            StatusText = "请先选择要编辑的宏";
            return false;
        }

        if (SelectedMacro.Events.Count >= limits.MaximumEventsPerMacro)
        {
            StatusText = $"事件数量已达到上限 {limits.MaximumEventsPerMacro}，不能继续插入";
            return false;
        }

        return true;
    }

    private bool InsertTimelineEvent(MacroEvent macroEvent, string description)
    {
        var beforeSelection = SelectedEvent?.EventIndex;
        var result = TimelineEditOperations.InsertEventAfterSelection(
            SelectedMacro!.Events,
            beforeSelection,
            macroEvent);
        return ApplyMacroEdit(
            SelectedMacro,
            result.Events,
            beforeSelection,
            result.SelectedEventIndex,
            description);
    }

    public bool CopySelectedEvent()
    {
        if (!CanCopyEvent)
        {
            StatusText = IsBusy
                ? "操作进行中，暂时不能复制事件"
                : SelectedMacro is null || SelectedEvent is null
                    ? "请先选择要复制的事件"
                    : $"事件数量已达到上限 {limits.MaximumEventsPerMacro}，不能继续复制";
            return false;
        }

        var beforeSelection = SelectedEvent!.EventIndex;
        var result = TimelineEditOperations.CopySelectedAfter(
            SelectedMacro!.Events,
            beforeSelection);
        if (result is null)
        {
            StatusText = "选中的事件已不在当前宏中";
            return false;
        }

        return ApplyMacroEdit(
            SelectedMacro,
            result.Events,
            beforeSelection,
            result.SelectedEventIndex,
            $"复制{SelectedEvent.Type}事件");
    }

    public bool DeleteSelectedEvent()
    {
        if (!CanDeleteEvent)
        {
            StatusText = IsBusy ? "操作进行中，暂时不能删除事件" : "请先选择要删除的事件";
            return false;
        }

        var beforeSelection = SelectedEvent!.EventIndex;
        var selectedType = SelectedEvent.Type;
        var result = TimelineEditOperations.DeleteSelected(
            SelectedMacro!.Events,
            beforeSelection);
        if (result is null)
        {
            StatusText = "选中的事件已不在当前宏中";
            return false;
        }

        return ApplyMacroEdit(
            SelectedMacro,
            result.Events,
            beforeSelection,
            result.SelectedEventIndex,
            $"删除{selectedType}事件");
    }

    public bool MoveSelectedEventUp() => MoveSelectedEvent(-1);

    public bool MoveSelectedEventDown() => MoveSelectedEvent(1);

    public bool FindPreviousEvent() => FindEvent(-1);

    public bool FindNextEvent() => FindEvent(1);

    private bool FindEvent(int offset)
    {
        var canFind = offset < 0 ? CanFindPreviousEvent : CanFindNextEvent;
        if (!canFind)
        {
            StatusText = string.IsNullOrWhiteSpace(EventSearchText)
                ? "请输入时间线搜索关键词"
                : "当前宏没有匹配的时间线事件";
            return false;
        }

        var selectedMatch = SelectedEvent is null
            ? -1
            : Array.IndexOf(eventSearchMatches, SelectedEvent.DisplayIndex);
        var targetMatch = selectedMatch >= 0
            ? (selectedMatch + offset + eventSearchMatches.Length) % eventSearchMatches.Length
            : offset < 0
                ? eventSearchMatches.Length - 1
                : 0;

        currentEventSearchMatch = targetMatch;
        SelectedEvent = Events[eventSearchMatches[targetMatch]];
        OnPropertyChanged(nameof(EventSearchResultText));
        StatusText = $"已定位到时间线搜索结果 {targetMatch + 1} / {eventSearchMatches.Length}";
        return true;
    }

    private bool MoveSelectedEvent(int offset)
    {
        var canMove = offset < 0 ? CanMoveEventUp : CanMoveEventDown;
        if (!canMove)
        {
            StatusText = IsBusy
                ? "操作进行中，暂时不能移动事件"
                : SelectedEvent is null
                    ? "请先选择要移动的事件"
                    : offset < 0
                        ? "选中的事件已经位于最上方"
                        : "选中的事件已经位于最下方";
            return false;
        }

        var beforeSelection = SelectedEvent!.EventIndex;
        var selectedType = SelectedEvent.Type;
        var result = TimelineEditOperations.MoveSelected(
            SelectedMacro!.Events,
            beforeSelection,
            offset);
        if (result is null)
        {
            StatusText = "无法移动选中的事件";
            return false;
        }

        return ApplyMacroEdit(
            SelectedMacro,
            result.Events,
            beforeSelection,
            result.SelectedEventIndex,
            offset < 0 ? $"上移{selectedType}事件" : $"下移{selectedType}事件");
    }

    public bool Undo()
    {
        if (!CanUndo)
        {
            StatusText = IsBusy ? "操作进行中，暂时不能撤销" : "没有可撤销的编辑";
            return false;
        }

        var entry = undoHistory[^1];
        if (!ReplaceMacro(entry.Before, entry.BeforeSelectedEventIndex, entry.After))
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
        if (!ReplaceMacro(entry.After, entry.AfterSelectedEventIndex, entry.Before))
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
            var orderedEvents = SelectedMacro.Events
                .Select((macroEvent, index) => (MacroEvent: macroEvent, Index: index))
                .OrderBy(item => item.MacroEvent.Sequence)
                .ThenBy(item => item.Index)
                .ToArray();
            for (var displayIndex = 0; displayIndex < orderedEvents.Length; displayIndex++)
            {
                var item = orderedEvents[displayIndex];
                Events.Add(CreateEventRow(displayIndex, item.Index, item.MacroEvent));
            }
        }

        RebuildEventSearchResults();
        OnPropertyChanged(nameof(SelectedMacroSummary));
    }

    private void RebuildEventSearchResults()
    {
        var query = EventSearchText.Trim();
        eventSearchMatches = query.Length == 0
            ? []
            : Events
                .Where(item => EventMatchesSearch(item, query))
                .Select(item => item.DisplayIndex)
                .ToArray();
        SynchronizeEventSearchPosition();
        OnPropertyChanged(nameof(EventSearchResultText));
        OnPropertyChanged(nameof(CanFindPreviousEvent));
        OnPropertyChanged(nameof(CanFindNextEvent));
    }

    private void SynchronizeEventSearchPosition()
    {
        var newPosition = SelectedEvent is null
            ? -1
            : Array.IndexOf(eventSearchMatches, SelectedEvent.DisplayIndex);
        if (currentEventSearchMatch == newPosition)
        {
            return;
        }

        currentEventSearchMatch = newPosition;
        OnPropertyChanged(nameof(EventSearchResultText));
    }

    private static bool EventMatchesSearch(MacroEventRow item, string query)
    {
        Span<char> sequenceBuffer = stackalloc char[20];
        var sequenceMatches = item.Sequence.TryFormat(
                                  sequenceBuffer,
                                  out var sequenceLength,
                                  provider: CultureInfo.InvariantCulture) &&
                              MemoryExtensions.IndexOf(
                                  (ReadOnlySpan<char>)sequenceBuffer[..sequenceLength],
                                  query.AsSpan(),
                                  StringComparison.OrdinalIgnoreCase) >= 0;
        return sequenceMatches ||
               item.Type.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.Details.Contains(query, StringComparison.OrdinalIgnoreCase);
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
            var resolution = resolver.Resolve(document, Macros, limits);
            diagnostics.AddRange(WithMacroContext(resolution.Diagnostics, currentMacroName));
            if (resolution.Document is null || resolution.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
            {
                selectedMacroHasBlockingErrors = true;
                ReplaceEvaluationDiagnostics(currentMacro, diagnostics);
                return;
            }

            document = resolution.Document;
        }

        var validation = validator.Validate(document, limits);
        diagnostics.AddRange(WithMacroContext(validation.Diagnostics, currentMacroName)
            .Where(item => !IsRedundantUnknownEventDiagnostic(item)));
        selectedMacroHasBlockingErrors = validation.HasErrors;
        ReplaceEvaluationDiagnostics(currentMacro, diagnostics);
    }

    private bool IsRedundantUnknownEventDiagnostic(ConversionDiagnostic diagnostic)
    {
        if (!string.Equals(diagnostic.Code, "UNKNOWN_EVENT", StringComparison.Ordinal))
        {
            return false;
        }

        return Diagnostics.Any(existing =>
            existing.EventSequence == diagnostic.EventSequence &&
            string.Equals(existing.SourceContext, diagnostic.SourceContext, StringComparison.Ordinal) &&
            existing.Code is "RAZER_EVENT_UNKNOWN" or "RAZER_EVENT_INVALID" or
                "SYNAPSE4_EVENT_UNKNOWN" or "SYNAPSE4_EVENT_INVALID" or
                "XMBC_TOKEN_UNKNOWN" or "IMPORT_EVENT_LIMIT");
    }

    private static IEnumerable<ConversionDiagnostic> WithMacroContext(
        IEnumerable<ConversionDiagnostic> diagnostics,
        string macroName) =>
        diagnostics.Select(item => string.IsNullOrWhiteSpace(item.SourceContext)
            ? item with { SourceContext = macroName }
            : item);

    private static MacroEventRow CreateEventRow(
        int displayIndex,
        int eventIndex,
        MacroEvent macroEvent) => macroEvent switch
    {
        DelayMacroEvent delay => new(displayIndex, eventIndex, delay, "延时", $"{delay.Milliseconds} ms"),
        RandomDelayMacroEvent delay => new(displayIndex, eventIndex, delay, "随机延时", $"{delay.MinimumMilliseconds}–{delay.MaximumMilliseconds} ms"),
        KeyMacroEvent key => new(displayIndex, eventIndex, key, "键盘", $"{key.Transition} · VK {key.VirtualKey}{(key.IsExtended ? " · 扩展" : string.Empty)}"),
        MouseMacroEvent mouse => new(displayIndex, eventIndex, mouse, "鼠标", $"{mouse.Transition} · {mouse.Button}"),
        ScanCodeMacroEvent scan => new(displayIndex, eventIndex, scan, "扫描码", $"{scan.Transition} · {scan.ScanCode}{(scan.IsExtended ? " · 扩展" : string.Empty)}"),
        XmbcCommandMacroEvent command => new(displayIndex, eventIndex, command, "XMBC 命令", $"{command.Category} · {command.RawTag}"),
        MacroReferenceEvent reference => new(displayIndex, eventIndex, reference, "嵌套宏", reference.TargetName ?? reference.TargetGuid?.ToString() ?? $"索引 {reference.TargetIndex}"),
        UnknownMacroEvent unknown => new(displayIndex, eventIndex, unknown, "未知", $"{unknown.SourceType} · {unknown.RawPayload}"),
        _ => new(displayIndex, eventIndex, macroEvent, "其他", string.Empty),
    };

    private bool ApplyMacroEdit(
        MacroDocument before,
        MacroEvent[] events,
        int? beforeSelectedEventIndex,
        int? afterSelectedEventIndex,
        string description)
    {
        if (before.Events.SequenceEqual(events))
        {
            StatusText = "编辑没有产生可见变化";
            return false;
        }

        var after = before with { Events = Array.AsReadOnly(events) };
        var entry = new MacroEditHistoryEntry(
            before,
            after,
            beforeSelectedEventIndex,
            afterSelectedEventIndex,
            description);
        undoHistory.Add(entry);
        if (undoHistory.Count > MaximumEditHistoryEntries)
        {
            undoHistory.RemoveAt(0);
        }

        redoHistory.Clear();
        if (!ReplaceMacro(after, afterSelectedEventIndex, before))
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
        OnPropertyChanged(nameof(CanInsertEvent));
        OnPropertyChanged(nameof(CanInsertDelay));
        OnPropertyChanged(nameof(CanDeleteEvent));
        OnPropertyChanged(nameof(CanCopyEvent));
        OnPropertyChanged(nameof(CanMoveEventUp));
        OnPropertyChanged(nameof(CanMoveEventDown));
        OnPropertyChanged(nameof(CanScaleDelays));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanFindPreviousEvent));
        OnPropertyChanged(nameof(CanFindNextEvent));
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
        int? BeforeSelectedEventIndex,
        int? AfterSelectedEventIndex,
        string Description);
}
