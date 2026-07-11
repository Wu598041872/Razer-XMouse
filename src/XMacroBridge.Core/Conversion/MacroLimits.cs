namespace XMacroBridge.Core.Conversion;

public sealed record MacroLimits(
    long MaximumFileBytes = 64L * 1024 * 1024,
    int MaximumEventsPerMacro = 100_000,
    int MaximumNestingDepth = 32,
    long MaximumDelayMilliseconds = 24L * 60 * 60 * 1000);
