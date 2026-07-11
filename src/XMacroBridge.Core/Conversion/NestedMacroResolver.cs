using XMacroBridge.Core.Abstractions;
using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;

namespace XMacroBridge.Core.Conversion;

public sealed class NestedMacroResolver : INestedMacroResolver
{
    public MacroResolutionResult Resolve(
        MacroDocument root,
        IReadOnlyList<MacroDocument> availableDocuments,
        MacroLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(availableDocuments);
        limits ??= new MacroLimits();

        var diagnostics = new List<ConversionDiagnostic>();
        var documents = EnsureRootIsAvailable(root, availableDocuments);
        var byGuid = BuildGuidIndex(documents, diagnostics);
        if (diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
        {
            return new MacroResolutionResult(null, diagnostics);
        }

        var output = new List<MacroEvent>();
        var path = new List<MacroDocument>();
        if (!Expand(root, documents, byGuid, limits, output, path, diagnostics, 0))
        {
            return new MacroResolutionResult(null, diagnostics);
        }

        var resequenced = output.Select((macroEvent, index) => WithSequence(macroEvent, index)).ToArray();
        return new MacroResolutionResult(root with { Events = resequenced }, diagnostics);
    }

    private static IReadOnlyList<MacroDocument> EnsureRootIsAvailable(
        MacroDocument root,
        IReadOnlyList<MacroDocument> availableDocuments)
    {
        if (availableDocuments.Any(item => item.Id == root.Id))
        {
            return availableDocuments;
        }

        return [root, .. availableDocuments];
    }

    private static IReadOnlyDictionary<Guid, MacroDocument> BuildGuidIndex(
        IReadOnlyList<MacroDocument> documents,
        ICollection<ConversionDiagnostic> diagnostics)
    {
        var index = new Dictionary<Guid, MacroDocument>();
        foreach (var document in documents)
        {
            if (!index.TryAdd(document.Id, document))
            {
                diagnostics.Add(new ConversionDiagnostic(
                    "REFERENCE_DUPLICATE_GUID",
                    DiagnosticSeverity.Error,
                    $"多个宏使用相同 GUID {document.Id}，无法安全解析引用。",
                    SourceContext: document.Name));
            }
        }

        return index;
    }

    private static bool Expand(
        MacroDocument document,
        IReadOnlyList<MacroDocument> documents,
        IReadOnlyDictionary<Guid, MacroDocument> byGuid,
        MacroLimits limits,
        ICollection<MacroEvent> output,
        IList<MacroDocument> path,
        ICollection<ConversionDiagnostic> diagnostics,
        int depth)
    {
        if (depth > limits.MaximumNestingDepth)
        {
            diagnostics.Add(new ConversionDiagnostic(
                "REFERENCE_DEPTH_LIMIT",
                DiagnosticSeverity.Error,
                $"嵌套深度超过上限 {limits.MaximumNestingDepth}。",
                SourceContext: FormatPath(path, document)));
            return false;
        }

        var cycleStart = FindDocumentIndex(path, document.Id);
        if (cycleStart >= 0)
        {
            var cycle = path.Skip(cycleStart).Append(document).Select(item => item.Name);
            diagnostics.Add(new ConversionDiagnostic(
                "REFERENCE_CYCLE",
                DiagnosticSeverity.Error,
                $"检测到循环宏引用：{string.Join(" → ", cycle)}。"));
            return false;
        }

        path.Add(document);
        try
        {
            foreach (var macroEvent in document.Events.OrderBy(item => item.Sequence))
            {
                if (macroEvent is not MacroReferenceEvent reference)
                {
                    output.Add(macroEvent);
                    if (output.Count > limits.MaximumEventsPerMacro)
                    {
                        diagnostics.Add(new ConversionDiagnostic(
                            "REFERENCE_EVENT_LIMIT",
                            DiagnosticSeverity.Error,
                            $"嵌套展开后的事件数超过上限 {limits.MaximumEventsPerMacro}。",
                            macroEvent.Sequence,
                            FormatPath(path)));
                        return false;
                    }

                    continue;
                }

                var target = ResolveTarget(reference, documents, byGuid, diagnostics);
                if (target is null)
                {
                    return false;
                }

                if (!Expand(target, documents, byGuid, limits, output, path, diagnostics, depth + 1))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            path.RemoveAt(path.Count - 1);
        }
    }

    private static MacroDocument? ResolveTarget(
        MacroReferenceEvent reference,
        IReadOnlyList<MacroDocument> documents,
        IReadOnlyDictionary<Guid, MacroDocument> byGuid,
        ICollection<ConversionDiagnostic> diagnostics)
    {
        if (reference.TargetGuid is { } guid && byGuid.TryGetValue(guid, out var guidTarget))
        {
            return guidTarget;
        }

        if (reference.TargetIndex is { } index && index >= 0 && index < documents.Count)
        {
            if (reference.TargetGuid is not null)
            {
                diagnostics.Add(new ConversionDiagnostic(
                    "REFERENCE_GUID_FALLBACK_INDEX",
                    DiagnosticSeverity.Warning,
                    $"未找到 GUID {reference.TargetGuid}，已回退使用索引 {index}。",
                    reference.Sequence));
            }

            return documents[index];
        }

        diagnostics.Add(new ConversionDiagnostic(
            "REFERENCE_MISSING",
            DiagnosticSeverity.Error,
            $"找不到嵌套宏：{reference.TargetName ?? reference.TargetGuid?.ToString() ?? reference.TargetIndex?.ToString() ?? "未知引用"}。",
            reference.Sequence));
        return null;
    }

    private static int FindDocumentIndex(IList<MacroDocument> path, Guid id)
    {
        for (var index = 0; index < path.Count; index++)
        {
            if (path[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    private static string FormatPath(IEnumerable<MacroDocument> path, MacroDocument? tail = null)
    {
        var names = tail is null ? path.Select(item => item.Name) : path.Append(tail).Select(item => item.Name);
        return string.Join(" → ", names);
    }

    private static MacroEvent WithSequence(MacroEvent macroEvent, long sequence) => macroEvent switch
    {
        DelayMacroEvent delay => delay with { Sequence = sequence },
        KeyMacroEvent key => key with { Sequence = sequence },
        MouseMacroEvent mouse => mouse with { Sequence = sequence },
        MacroReferenceEvent reference => reference with { Sequence = sequence },
        UnknownMacroEvent unknown => unknown with { Sequence = sequence },
        _ => throw new InvalidOperationException($"无法重新编号事件类型 {macroEvent.GetType().Name}。"),
    };
}
