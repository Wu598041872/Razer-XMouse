using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using XMacroBridge.Core.Abstractions;
using XMacroBridge.Core.Conversion;
using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;

namespace XMacroBridge.Formats.Xmbc;

public sealed partial class XmbcSettingsImporter : IMacroImporter
{
    private static readonly MacroLimits DefaultLimits = new();

    public string FormatId => "xmbc.settings.xml";

    public bool CanImport(ReadOnlySpan<byte> header, string? fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (!string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".xmbcp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(header);
        return text.Contains("<root", StringComparison.OrdinalIgnoreCase)
            && text.Contains("<version", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<MacroImportResult> ImportAsync(
        Stream input,
        string? sourceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            await using var buffer = new MemoryStream();
            await CopyWithLimitAsync(input, buffer, DefaultLimits.MaximumFileBytes, cancellationToken).ConfigureAwait(false);
            buffer.Position = 0;

            var settings = new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = DefaultLimits.MaximumFileBytes,
                IgnoreComments = true,
                IgnoreWhitespace = true,
            };
            using var reader = XmlReader.Create(buffer, settings);
            var xml = await XDocument.LoadAsync(reader, LoadOptions.SetLineInfo, cancellationToken).ConfigureAwait(false);
            if (xml.Root is null || !string.Equals(xml.Root.Name.LocalName, "root", StringComparison.OrdinalIgnoreCase))
            {
                return Failure("XMBC_SETTINGS_ROOT", "XMBC 配置的根元素不是 <root>。", sourceName);
            }

            var documents = new List<MacroDocument>();
            var diagnostics = new List<ConversionDiagnostic>();
            foreach (var mapping in xml.Descendants().Where(IsSimulatedKeysMapping))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var document = await ParseMappingAsync(mapping, sourceName, diagnostics, cancellationToken).ConfigureAwait(false);
                documents.Add(document);
            }

            if (documents.Count == 0)
            {
                diagnostics.Add(new ConversionDiagnostic(
                    "XMBC_SETTINGS_NO_MACROS",
                    DiagnosticSeverity.Warning,
                    "配置中没有找到 action=28 的模拟按键宏。"));
            }

            return new MacroImportResult(documents, diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is XmlException or InvalidDataException)
        {
            return Failure("XMBC_SETTINGS_INVALID", $"XMBC 配置无效：{exception.Message}", sourceName);
        }
    }

    private static bool IsSimulatedKeysMapping(XElement element) =>
        string.Equals((string?)element.Attribute("action"), "28", StringComparison.Ordinal);

    private static async Task<MacroDocument> ParseMappingAsync(
        XElement mapping,
        string? sourceName,
        ICollection<ConversionDiagnostic> aggregateDiagnostics,
        CancellationToken cancellationToken)
    {
        var keys = (string?)mapping.Attribute("keys") ?? string.Empty;
        var name = BuildMacroName(mapping);
        await using var textStream = new MemoryStream(Encoding.UTF8.GetBytes(keys));
        var parsed = await new XmbcMacroTextImporter()
            .ImportAsync(textStream, name + ".txt", cancellationToken)
            .ConfigureAwait(false);
        var parsedDocument = parsed.Documents.Single();

        foreach (var diagnostic in parsed.Diagnostics)
        {
            aggregateDiagnostics.Add(diagnostic with { SourceContext = diagnostic.SourceContext ?? name });
        }

        if (string.Equals((string?)mapping.Attribute("active"), "false", StringComparison.OrdinalIgnoreCase))
        {
            aggregateDiagnostics.Add(new ConversionDiagnostic(
                "XMBC_MAPPING_INACTIVE",
                DiagnosticSeverity.Info,
                $"宏“{name}”在来源配置中处于未激活状态。",
                SourceContext: name));
        }

        var metadata = BuildMetadata(mapping);
        var idSource = string.Join("|", sourceName, BuildElementPath(mapping), keys);
        return parsedDocument with
        {
            Id = CreateDeterministicGuid(Encoding.UTF8.GetBytes(idSource)),
            Name = name,
            SourceFormat = "xmbc.settings.action28",
            SourcePath = sourceName,
            Metadata = metadata,
        };
    }

    private static string BuildMacroName(XElement mapping)
    {
        var application = mapping.Ancestors().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "Application", StringComparison.OrdinalIgnoreCase));
        var defaultProfile = mapping.Ancestors().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "Default", StringComparison.OrdinalIgnoreCase));

        var profileLabel = application is null
            ? defaultProfile is null ? "未知配置" : "默认配置"
            : BuildApplicationLabel(application);
        var layerIndex = GetLayerIndex(mapping);
        var layerLabel = GetLayerLabel(mapping, application, defaultProfile, layerIndex);
        var buttonLabel = BuildButtonLabel(mapping);
        var description = ((string?)mapping.Attribute("desc"))?.Trim();
        var result = $"{profileLabel} / {layerLabel} / {buttonLabel}";
        return string.IsNullOrWhiteSpace(description) ? result : $"{result} / {description}";
    }

    private static string BuildApplicationLabel(XElement application)
    {
        var executable = ((string?)application.Attribute("Name"))?.Trim();
        var description = ((string?)application.Attribute("Description"))?.Trim();
        if (!string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(executable))
        {
            return $"{description} ({executable})";
        }

        return !string.IsNullOrWhiteSpace(description)
            ? description
            : !string.IsNullOrWhiteSpace(executable) ? executable : "未命名应用";
    }

    private static int GetLayerIndex(XElement mapping)
    {
        foreach (var ancestor in mapping.Ancestors())
        {
            var match = LayerElementRegex().Match(ancestor.Name.LocalName);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var zeroBasedAdditionalLayer))
            {
                return zeroBasedAdditionalLayer + 1;
            }
        }

        return 1;
    }

    private static string GetLayerLabel(
        XElement mapping,
        XElement? application,
        XElement? defaultProfile,
        int layerIndex)
    {
        string? customName = null;
        if (application is not null)
        {
            var layerNames = application.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "LayerNames", StringComparison.OrdinalIgnoreCase));
            customName = ((string?)layerNames?.Attribute($"layer{layerIndex}"))?.Trim();
        }

        var layerContainer = mapping.Ancestors().FirstOrDefault(element => LayerElementRegex().IsMatch(element.Name.LocalName));
        var profileContainer = layerContainer ?? application ?? defaultProfile;
        var layerElement = profileContainer?.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "Layer", StringComparison.OrdinalIgnoreCase));
        customName ??= ((string?)layerElement?.Attribute("name"))?.Trim();

        return string.IsNullOrWhiteSpace(customName) ? $"第 {layerIndex} 层" : $"第 {layerIndex} 层：{customName}";
    }

    private static string BuildButtonLabel(XElement mapping)
    {
        if (mapping.Parent is { } parent &&
            string.Equals(parent.Name.LocalName, "chords", StringComparison.OrdinalIgnoreCase) &&
            parent.Parent is { } chordSource)
        {
            return $"和弦 {chordSource.Name.LocalName}+{mapping.Name.LocalName}";
        }

        return mapping.Name.LocalName;
    }

    private static IReadOnlyDictionary<string, string> BuildMetadata(XElement mapping)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["xmbc.button"] = mapping.Name.LocalName,
            ["xmbc.layer"] = GetLayerIndex(mapping).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        foreach (var name in new[] { "keyaction", "keyrepeat", "active", "blockmouse", "randomisedelay", "desc" })
        {
            if (mapping.Attribute(name) is { } attribute)
            {
                metadata["xmbc." + name] = attribute.Value;
            }
        }

        var application = mapping.Ancestors().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "Application", StringComparison.OrdinalIgnoreCase));
        if (application?.Attribute("Name") is { } applicationName)
        {
            metadata["xmbc.application"] = applicationName.Value;
        }

        if (mapping.Parent is { } parent && string.Equals(parent.Name.LocalName, "chords", StringComparison.OrdinalIgnoreCase))
        {
            metadata["xmbc.chordSource"] = parent.Parent?.Name.LocalName ?? string.Empty;
        }

        return metadata;
    }

    private static string BuildElementPath(XElement element) =>
        string.Join("/", element.AncestorsAndSelf().Reverse().Select(item => item.Name.LocalName));

    private static async Task CopyWithLimitAsync(
        Stream input,
        Stream output,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            total += read;
            if (total > maximumBytes)
            {
                throw new InvalidDataException($"输入配置超过 {maximumBytes} 字节上限。 ");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static Guid CreateDeterministicGuid(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return new Guid(hash[..16]);
    }

    private static MacroImportResult Failure(string code, string message, string? sourceName) =>
        new([], [new ConversionDiagnostic(code, DiagnosticSeverity.Error, message, SourceContext: sourceName)]);

    [GeneratedRegex("^Layer([1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LayerElementRegex();
}
