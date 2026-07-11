using XMacroBridge.Core.Models;

namespace XMacroBridge.Presentation.Workspace;

internal static class TimelineEditOperations
{
    public static TimelineEditResult InsertDelayAfterSelection(
        IReadOnlyList<MacroEvent> events,
        int? selectedEventIndex,
        long milliseconds)
    {
        var orderedEvents = GetOrderedEvents(events);
        var selectedPosition = FindSelectedPosition(orderedEvents, selectedEventIndex);
        var insertionPosition = selectedPosition is null ? orderedEvents.Count : selectedPosition.Value + 1;
        orderedEvents.Insert(
            insertionPosition,
            new OrderedEvent(-1, new DelayMacroEvent(insertionPosition, milliseconds)));
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

    private sealed record OrderedEvent(int OriginalIndex, MacroEvent Event);
}

internal sealed record TimelineEditResult(
    MacroEvent[] Events,
    int? SelectedEventIndex);
