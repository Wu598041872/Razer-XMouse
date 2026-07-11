using XMacroBridge.Core.Abstractions;
using XMacroBridge.Formats.Razer;
using XMacroBridge.Formats.Xmbc;

namespace XMacroBridge.Application.Formats;

public sealed class MacroFormatRegistry
{
    private readonly IReadOnlyList<IMacroImporter> importers;
    private readonly IReadOnlyDictionary<string, IMacroExporter> exporters;

    public MacroFormatRegistry(
        IEnumerable<IMacroImporter> importers,
        IEnumerable<IMacroExporter> exporters)
    {
        ArgumentNullException.ThrowIfNull(importers);
        ArgumentNullException.ThrowIfNull(exporters);
        this.importers = importers.ToArray();
        this.exporters = exporters.ToDictionary(item => item.FormatId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IMacroImporter> Importers => importers;

    public IMacroImporter? FindImporter(ReadOnlySpan<byte> header, string? fileName)
    {
        foreach (var importer in importers)
        {
            if (importer.CanImport(header, fileName))
            {
                return importer;
            }
        }

        return null;
    }

    public bool TryGetExporter(string formatId, out IMacroExporter? exporter) =>
        exporters.TryGetValue(formatId, out exporter);

    public static MacroFormatRegistry CreateDefault() =>
        new(
            [new Synapse4Importer(), new RazerMacroXmlImporter(), new XmbcSettingsImporter(), new XmbcMacroTextImporter()],
            [new RazerMacroXmlExporter(), new XmbcMacroTextExporter()]);
}
