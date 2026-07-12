using XMacroBridge.Core.Conversion;
using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;

namespace XMacroBridge.Formats.Razer;

internal static class RazerLoopExpander
{
    public static IReadOnlyList<MacroEvent> Expand(
        IReadOnlyList<MacroEvent> source,
        ICollection<ConversionDiagnostic> diagnostics,
        string macroName,
        MacroLimits limits,
        string diagnosticPrefix)
    {
        if (!source.Any(item => item is RazerLoopBoundaryEvent))
        {
            return Normalize(source);
        }

        var output = new List<MacroEvent>();
        var stack = new Stack<LoopFrame>();
        foreach (var macroEvent in source)
        {
            if (macroEvent is not RazerLoopBoundaryEvent boundary)
            {
                var target = stack.Count == 0 ? output : stack.Peek().Body;
                if (target.Count >= limits.MaximumEventsPerMacro)
                {
                    return Fail(
                        source,
                        diagnostics,
                        macroName,
                        limits,
                        diagnosticPrefix + "_LOOP_EXPANSION_LIMIT",
                        $"循环展开后的事件数超过上限 {limits.MaximumEventsPerMacro}。",
                        macroEvent.Sequence);
                }

                target.Add(macroEvent);
                continue;
            }

            if (boundary.IsStart)
            {
                if (stack.Count >= limits.MaximumNestingDepth)
                {
                    return Fail(
                        source,
                        diagnostics,
                        macroName,
                        limits,
                        diagnosticPrefix + "_LOOP_DEPTH_LIMIT",
                        $"循环嵌套深度超过上限 {limits.MaximumNestingDepth}。",
                        boundary.Sequence);
                }

                stack.Push(new LoopFrame(boundary.Count, boundary.Sequence));
                continue;
            }

            if (stack.Count == 0)
            {
                return Fail(
                    source,
                    diagnostics,
                    macroName,
                    limits,
                    diagnosticPrefix + "_LOOP_INVALID",
                    $"循环结束标记（次数 {boundary.Count}）缺少对应的开始标记。",
                    boundary.Sequence);
            }

            var frame = stack.Pop();
            if (frame.Count != boundary.Count)
            {
                return Fail(
                    source,
                    diagnostics,
                    macroName,
                    limits,
                    diagnosticPrefix + "_LOOP_INVALID",
                    $"循环开始次数 {frame.Count} 与结束次数 {boundary.Count} 不一致。",
                    boundary.Sequence);
            }

            var parent = stack.Count == 0 ? output : stack.Peek().Body;
            var expandedCount = (long)frame.Body.Count * frame.Count;
            if (expandedCount > limits.MaximumEventsPerMacro - parent.Count)
            {
                return Fail(
                    source,
                    diagnostics,
                    macroName,
                    limits,
                    diagnosticPrefix + "_LOOP_EXPANSION_LIMIT",
                    $"循环展开后的事件数超过上限 {limits.MaximumEventsPerMacro}。",
                    boundary.Sequence);
            }

            if (frame.Body.Count == 0)
            {
                continue;
            }

            for (var iteration = 0; iteration < frame.Count; iteration++)
            {
                parent.AddRange(frame.Body);
            }
        }

        if (stack.Count > 0)
        {
            var frame = stack.Peek();
            return Fail(
                source,
                diagnostics,
                macroName,
                limits,
                diagnosticPrefix + "_LOOP_INVALID",
                $"循环开始标记（次数 {frame.Count}）缺少对应的结束标记。",
                frame.StartSequence);
        }

        return NormalizeAndRemapDiagnostics(output, diagnostics, macroName);
    }

    private static IReadOnlyList<MacroEvent> Fail(
        IReadOnlyList<MacroEvent> source,
        ICollection<ConversionDiagnostic> diagnostics,
        string macroName,
        MacroLimits limits,
        string code,
        string message,
        long eventSequence)
    {
        diagnostics.Add(new ConversionDiagnostic(
            code,
            DiagnosticSeverity.Error,
            $"宏“{macroName}”中的循环无效：{message}",
            eventSequence,
            macroName));

        var blocked = source
            .Take(limits.MaximumEventsPerMacro)
            .Select(item => item is RazerLoopBoundaryEvent boundary
                ? new UnknownMacroEvent(
                    boundary.Sequence,
                    boundary.IsStart ? "razer.loop-start" : "razer.loop-end",
                    $"Count={boundary.Count}")
                : item)
            .ToArray();
        return Normalize(blocked);
    }

    private static IReadOnlyList<MacroEvent> Normalize(IEnumerable<MacroEvent> source) =>
        source.Select((item, index) => item with { Sequence = index }).ToArray();

    private static IReadOnlyList<MacroEvent> NormalizeAndRemapDiagnostics(
        IReadOnlyList<MacroEvent> source,
        ICollection<ConversionDiagnostic> diagnostics,
        string macroName)
    {
        var sequenceMap = source
            .Select((item, index) => (item.Sequence, ExpandedSequence: (long)index))
            .GroupBy(item => item.Sequence)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.ExpandedSequence).ToArray());
        var eventDiagnostics = diagnostics
            .Where(item =>
                item.EventSequence is long sequence &&
                item.SourceContext == macroName &&
                sequenceMap.ContainsKey(sequence))
            .ToArray();

        foreach (var diagnostic in eventDiagnostics)
        {
            diagnostics.Remove(diagnostic);
            foreach (var expandedSequence in sequenceMap[diagnostic.EventSequence!.Value])
            {
                diagnostics.Add(diagnostic with { EventSequence = expandedSequence });
            }
        }

        return Normalize(source);
    }

    private sealed record LoopFrame(int Count, long StartSequence)
    {
        public List<MacroEvent> Body { get; } = [];
    }
}
