using XMacroBridge.Core.Models;

namespace XMacroBridge.Formats.Razer;

internal sealed record RazerLoopBoundaryEvent(
    long Sequence,
    bool IsStart,
    int Count) : MacroEvent(Sequence);
