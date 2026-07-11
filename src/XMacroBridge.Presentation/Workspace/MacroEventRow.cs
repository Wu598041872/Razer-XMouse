using XMacroBridge.Core.Models;

namespace XMacroBridge.Presentation.Workspace;

public sealed record MacroEventRow(
    int EventIndex,
    MacroEvent Event,
    string Type,
    string Details)
{
    public long Sequence => Event.Sequence;

    public bool IsFixedDelay => Event is DelayMacroEvent;
}
