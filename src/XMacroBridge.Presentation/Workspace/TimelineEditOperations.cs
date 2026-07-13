using XMacroBridge.Core.Models;

namespace XMacroBridge.Presentation.Workspace;

internal static class TimelineEditOperations
{
    public static TimelineEditResult InsertDelayAfterSelection(
        IReadOnlyList<MacroEvent> events,
        int? selectedEventIndex,
        long milliseconds) =>
        InsertEventAfterSelection(
            events,
            selectedEventIndex,
            new DelayMacroEvent(0, milliseconds));

    public static TimelineEditResult InsertEventAfterSelection(
        IReadOnlyList<MacroEvent> events,
        int? selectedEventIndex,
        MacroEvent newEvent)
    {
        ArgumentNullException.ThrowIfNull(newEvent);
        var orderedEvents = GetOrderedEvents(events);
        var selectedPosition = FindSelectedPosition(orderedEvents, selectedEventIndex);
        var insertionPosition = selectedPosition is null ? orderedEvents.Count : selectedPosition.Value + 1;
        var insertedEvent = newEvent with { Sequence = insertionPosition };
        orderedEvents.Insert(
            insertionPosition,
            new OrderedEvent(-1, insertedEvent));
        return Normalize(orderedEvents, insertionPosition);
    }

    public static TimelineEditResult? DeleteSelected(
        IReadOnlyList<MacroEvent> events,
        int selectedEventIndex)
    {
        var orderedEvents = GetOrderedEvents(events);
        var selectedPosition = FindSelectedPosition(orderedEvents, selectedEventIndex);
        if (selectedPosition is null)
        {
            return null;
        }

        orderedEvents.RemoveAt(selectedPosition.Value);
        int? nextSelection = orderedEvents.Count == 0
            ? null
            : Math.Min(selectedPosition.Value, orderedEvents.Count - 1);
        return Normalize(orderedEvents, nextSelection);
    }

    public static TimelineEditResult? CopySelectedAfter(
        IReadOnlyList<MacroEvent> events,
        int selectedEventIndex)
    {
        var orderedEvents = GetOrderedEvents(events);
        var selectedPosition = FindSelectedPosition(orderedEvents, selectedEventIndex);
        if (selectedPosition is null)
        {
            return null;
        }

        var insertionPosition = selectedPosition.Value + 1;
        var copy = orderedEvents[selectedPosition.Value].Event with { Sequence = insertionPosition };
        orderedEvents.Insert(insertionPosition, new OrderedEvent(-1, copy));
        return Normalize(orderedEvents, insertionPosition);
    }

    public static TimelineEditResult? MoveSelected(
        IReadOnlyList<MacroEvent> events,
        int selectedEventIndex,
        int offset)
    {
        if (offset is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "事件移动只接受 -1 或 1。");
        }

        var orderedEvents = GetOrderedEvents(events);
        var selectedPosition = FindSelectedPosition(orderedEvents, selectedEventIndex);
        if (selectedPosition is null)
        {
            return null;
        }

        var targetPosition = selectedPosition.Value + offset;
        if (targetPosition < 0 || targetPosition >= orderedEvents.Count)
        {
            return null;
        }

        (orderedEvents[selectedPosition.Value], orderedEvents[targetPosition]) =
            (orderedEvents[targetPosition], orderedEvents[selectedPosition.Value]);
        return Normalize(orderedEvents, targetPosition);
    }

    public static TimelineMultiEditResult? CopySelectedAfter(
        IReadOnlyList<MacroEvent> events,
        IReadOnlyCollection<int> selectedEventIndices)
    {
        var orderedEvents = GetOrderedEvents(events);
        var selectedPositions = FindSelectedPositions(orderedEvents, selectedEventIndices);
        if (selectedPositions.Length == 0)
        {
            return null;
        }

        var insertionPosition = selectedPositions[^1] + 1;
        var copies = selectedPositions
            .Select(position => new OrderedEvent(-1, orderedEvents[position].Event))
            .ToArray();
        orderedEvents.InsertRange(insertionPosition, copies);
        return NormalizeMulti(
            orderedEvents,
            Enumerable.Range(insertionPosition, copies.Length).ToArray());
    }

    public static TimelineMultiEditResult? DeleteSelected(
        IReadOnlyList<MacroEvent> events,
        IReadOnlyCollection<int> selectedEventIndices)
    {
        var orderedEvents = GetOrderedEvents(events);
        var selectedPositions = FindSelectedPositions(orderedEvents, selectedEventIndices);
        if (selectedPositions.Length == 0)
        {
            return null;
        }

        var nextPosition = selectedPositions[0];
        for (var index = selectedPositions.Length - 1; index >= 0; index--)
        {
            orderedEvents.RemoveAt(selectedPositions[index]);
        }

        var nextSelection = orderedEvents.Count == 0
            ? Array.Empty<int>()
            : new[] { Math.Min(nextPosition, orderedEvents.Count - 1) };
        return NormalizeMulti(orderedEvents, nextSelection);
    }

    public static TimelineMultiEditResult? MoveSelectedBlock(
        IReadOnlyList<MacroEvent> events,
        IReadOnlyCollection<int> selectedEventIndices,
        int offset)
    {
        if (offset is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "事件组移动只接受 -1 或 1。");
        }

        var orderedEvents = GetOrderedEvents(events);
        var selectedPositions = FindSelectedPositions(orderedEvents, selectedEventIndices);
        if (selectedPositions.Length == 0)
        {
            return null;
        }

        var selectedSet = selectedEventIndices.ToHashSet();
        if (offset < 0)
        {
            var preceding = Enumerable.Range(0, selectedPositions[0])
                .Reverse()
                .FirstOrDefault(position => !selectedSet.Contains(orderedEvents[position].OriginalIndex), -1);
            return preceding < 0
                ? null
                : MoveSelectionTo(orderedEvents, selectedSet, orderedEvents[preceding].OriginalIndex, insertAfter: false);
        }

        var following = Enumerable.Range(selectedPositions[^1] + 1, orderedEvents.Count - selectedPositions[^1] - 1)
            .FirstOrDefault(position => !selectedSet.Contains(orderedEvents[position].OriginalIndex), -1);
        return following < 0
            ? null
            : MoveSelectionTo(orderedEvents, selectedSet, orderedEvents[following].OriginalIndex, insertAfter: true);
    }

    public static TimelineMultiEditResult? MoveSelectedTo(
        IReadOnlyList<MacroEvent> events,
        IReadOnlyCollection<int> selectedEventIndices,
        int targetEventIndex,
        bool insertAfter)
    {
        var orderedEvents = GetOrderedEvents(events);
        var selectedSet = selectedEventIndices.ToHashSet();
        return selectedSet.Count == 0 || selectedSet.Contains(targetEventIndex)
            ? null
            : MoveSelectionTo(orderedEvents, selectedSet, targetEventIndex, insertAfter);
    }

    private static List<OrderedEvent> GetOrderedEvents(IReadOnlyList<MacroEvent> events) =>
        events
            .Select((macroEvent, index) => new OrderedEvent(index, macroEvent))
            .OrderBy(item => item.Event.Sequence)
            .ThenBy(item => item.OriginalIndex)
            .ToList();

    private static int? FindSelectedPosition(
        IReadOnlyList<OrderedEvent> orderedEvents,
        int? selectedEventIndex)
    {
        if (selectedEventIndex is null)
        {
            return null;
        }

        for (var position = 0; position < orderedEvents.Count; position++)
        {
            if (orderedEvents[position].OriginalIndex == selectedEventIndex.Value)
            {
                return position;
            }
        }

        return null;
    }

    private static int[] FindSelectedPositions(
        IReadOnlyList<OrderedEvent> orderedEvents,
        IReadOnlyCollection<int> selectedEventIndices)
    {
        var selectedSet = selectedEventIndices.ToHashSet();
        return Enumerable.Range(0, orderedEvents.Count)
            .Where(position => selectedSet.Contains(orderedEvents[position].OriginalIndex))
            .ToArray();
    }

    private static TimelineMultiEditResult? MoveSelectionTo(
        List<OrderedEvent> orderedEvents,
        HashSet<int> selectedSet,
        int targetEventIndex,
        bool insertAfter)
    {
        var moving = orderedEvents.Where(item => selectedSet.Contains(item.OriginalIndex)).ToArray();
        if (moving.Length == 0)
        {
            return null;
        }

        orderedEvents.RemoveAll(item => selectedSet.Contains(item.OriginalIndex));
        var targetPosition = orderedEvents.FindIndex(item => item.OriginalIndex == targetEventIndex);
        if (targetPosition < 0)
        {
            return null;
        }

        var insertionPosition = targetPosition + (insertAfter ? 1 : 0);
        orderedEvents.InsertRange(insertionPosition, moving);
        return NormalizeMulti(
            orderedEvents,
            Enumerable.Range(insertionPosition, moving.Length).ToArray());
    }

    private static TimelineEditResult Normalize(
        IReadOnlyList<OrderedEvent> orderedEvents,
        int? selectedPosition)
    {
        var normalized = orderedEvents
            .Select((item, index) => item.Event.Sequence == index
                ? item.Event
                : item.Event with { Sequence = index })
            .ToArray();
        return new TimelineEditResult(normalized, selectedPosition);
    }

    private static TimelineMultiEditResult NormalizeMulti(
        IReadOnlyList<OrderedEvent> orderedEvents,
        int[] selectedPositions)
    {
        var normalized = orderedEvents
            .Select((item, index) => item.Event.Sequence == index
                ? item.Event
                : item.Event with { Sequence = index })
            .ToArray();
        return new TimelineMultiEditResult(normalized, selectedPositions);
    }

    private sealed record OrderedEvent(int OriginalIndex, MacroEvent Event);
}

internal sealed record TimelineEditResult(
    MacroEvent[] Events,
    int? SelectedEventIndex);

internal sealed record TimelineMultiEditResult(
    MacroEvent[] Events,
    int[] SelectedEventIndices);
