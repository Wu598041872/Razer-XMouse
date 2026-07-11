namespace XMacroBridge.Application.Importing;

public sealed record MacroImportOptions(
    long MaximumBatchBytes = 512L * 1024 * 1024,
    int MaximumFiles = 1000,
    int HeaderProbeBytes = 4096);
