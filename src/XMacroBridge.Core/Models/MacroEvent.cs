namespace XMacroBridge.Core.Models;

public abstract record MacroEvent(long Sequence);

public sealed record DelayMacroEvent(long Sequence, long Milliseconds) : MacroEvent(Sequence);

public sealed record KeyMacroEvent(
    long Sequence,
    int VirtualKey,
    InputTransition Transition,
    string? DisplayName = null) : MacroEvent(Sequence);

public sealed record MouseMacroEvent(
    long Sequence,
    MouseButton Button,
    InputTransition Transition) : MacroEvent(Sequence);

public sealed record MacroReferenceEvent(
    long Sequence,
    Guid? TargetGuid,
    int? TargetIndex,
    string? TargetName = null) : MacroEvent(Sequence);

public sealed record UnknownMacroEvent(
    long Sequence,
    string SourceType,
    string? RawPayload = null) : MacroEvent(Sequence);
