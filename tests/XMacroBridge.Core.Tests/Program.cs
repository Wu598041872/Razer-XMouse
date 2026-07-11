using XMacroBridge.Application.Exporting;
using XMacroBridge.Application.Formats;
using XMacroBridge.Application.Importing;
using XMacroBridge.Core.Conversion;
using XMacroBridge.Core.Models;
using XMacroBridge.Formats.Razer;
using XMacroBridge.Formats.Xmbc;
using XMacroBridge.Presentation.Workspace;
using System.Text;

var tests = new (string Name, Action Run)[]
{
    ("Balanced keyboard macro is valid", BalancedKeyboardMacroIsValid),
    ("Release without press is rejected", ReleaseWithoutPressIsRejected),
    ("Unreleased mouse button is rejected", UnreleasedMouseButtonIsRejected),
    ("Negative delay is rejected", NegativeDelayIsRejected),
    ("Event limit is enforced", EventLimitIsEnforced),
    ("Razer XML fixture imports safely", RazerXmlFixtureImportsSafely),
    ("Razer XML rejects DTD", RazerXmlRejectsDtd),
    ("XMBC text fixture imports safely", XmbcTextFixtureImportsSafely),
    ("XMBC unknown token is preserved as error", XmbcUnknownTokenIsPreservedAsError),
    ("Nested macros flatten by GUID", NestedMacrosFlattenByGuid),
    ("Missing nested macro is rejected", MissingNestedMacroIsRejected),
    ("Nested macro cycle is rejected", NestedMacroCycleIsRejected),
    ("Nested macro can fall back to index", NestedMacroCanFallBackToIndex),
    ("Nested expansion event limit is enforced", NestedExpansionEventLimitIsEnforced),
    ("Synapse4 package imports and flattens", Synapse4PackageImportsAndFlattens),
    ("Synapse4 invalid payload is diagnosed", Synapse4InvalidPayloadIsDiagnosed),
    ("Synapse4 malformed event is preserved", Synapse4MalformedEventIsPreserved),
    ("XMBC settings extract action 28 macros", XmbcSettingsExtractAction28Macros),
    ("XMBC settings reject DTD", XmbcSettingsRejectDtd),
    ("XMBC text export round trips", XmbcTextExportRoundTrips),
    ("XMBC text export rejects invalid key", XmbcTextExportRejectsInvalidKey),
    ("Razer XML export round trips", RazerXmlExportRoundTrips),
    ("Razer XML export rejects unsupported mouse", RazerXmlExportRejectsUnsupportedMouse),
    ("XMBC modifiers apply to next key", XmbcModifiersApplyToNextKey),
    ("XMBC HOLDMS applies to next key", XmbcHoldMsAppliesToNextKey),
    ("XMBC PRESS and RELEASE span multiple keys", XmbcPressReleaseSpanMultipleKeys),
    ("XMBC extended key and mouse tags import", XmbcExtendedTagsImport),
    ("XMBC modifier states export round trip", XmbcModifierStatesExportRoundTrip),
    ("Razer XML export rejects extended key", RazerXmlExportRejectsExtendedKey),
    ("XMBC random delay round trips", XmbcRandomDelayRoundTrips),
    ("XMBC invalid random delay is rejected", XmbcInvalidRandomDelayIsRejected),
    ("XMBC scan codes round trip", XmbcScanCodesRoundTrip),
    ("XMBC commands are preserved and blocked for Razer", XmbcCommandsArePreservedAndBlockedForRazer),
    ("Application import service handles fixture directory", ApplicationImportServiceHandlesFixtureDirectory),
    ("Application import service diagnoses unknown XML", ApplicationImportServiceDiagnosesUnknownXml),
    ("Safe export writes atomically and protects source", SafeExportWritesAtomicallyAndProtectsSource),
    ("Failed safe export cleans temporary files", FailedSafeExportCleansTemporaryFiles),
    ("Safe export rejects Windows reserved names", SafeExportRejectsWindowsReservedNames),
    ("Workspace imports fixtures and refreshes event rows", WorkspaceImportsFixturesAndRefreshesEventRows),
    ("Workspace expands nested macros before export", WorkspaceExpandsNestedMacrosBeforeExport),
    ("Workspace exports both supported target formats", WorkspaceExportsBothSupportedTargetFormats),
    ("Workspace blocks invalid selection and reports missing selection", WorkspaceBlocksInvalidAndMissingSelection),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {test.Name}: {exception.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}

Console.WriteLine($"Executed {tests.Length} tests; {failures.Count} failed.");
return failures.Count == 0 ? 0 : 1;

static void BalancedKeyboardMacroIsValid()
{
    var document = CreateDocument(
        new KeyMacroEvent(0, 65, InputTransition.Down, "A"),
        new DelayMacroEvent(1, 50),
        new KeyMacroEvent(2, 65, InputTransition.Up, "A"));

    var result = new MacroValidator().Validate(document);
    Assert(!result.HasErrors, "Expected no validation errors.");
}

static void ReleaseWithoutPressIsRejected()
{
    var result = new MacroValidator().Validate(CreateDocument(
        new KeyMacroEvent(0, 65, InputTransition.Up, "A")));

    Assert(result.Diagnostics.Any(item => item.Code == "KEY_UP_WITHOUT_DOWN"), "Missing KEY_UP_WITHOUT_DOWN.");
}

static void UnreleasedMouseButtonIsRejected()
{
    var result = new MacroValidator().Validate(CreateDocument(
        new MouseMacroEvent(0, MouseButton.Left, InputTransition.Down)));

    Assert(result.Diagnostics.Any(item => item.Code == "MOUSE_NOT_RELEASED"), "Missing MOUSE_NOT_RELEASED.");
}

static void NegativeDelayIsRejected()
{
    var result = new MacroValidator().Validate(CreateDocument(new DelayMacroEvent(0, -1)));
    Assert(result.Diagnostics.Any(item => item.Code == "DELAY_NEGATIVE"), "Missing DELAY_NEGATIVE.");
}

static void EventLimitIsEnforced()
{
    var result = new MacroValidator().Validate(
        CreateDocument(new DelayMacroEvent(0, 1), new DelayMacroEvent(1, 1)),
        new MacroLimits(MaximumEventsPerMacro: 1));

    Assert(result.Diagnostics.Any(item => item.Code == "LIMIT_EVENT_COUNT"), "Missing LIMIT_EVENT_COUNT.");
}

static void RazerXmlFixtureImportsSafely()
{
    var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "basic-key-delay.xml");
    using var stream = File.OpenRead(fixturePath);
    var result = new RazerMacroXmlImporter().ImportAsync(stream, fixturePath).GetAwaiter().GetResult();

    Assert(result.Documents.Count == 1, "Expected one imported macro.");
    Assert(result.Documents[0].Events.Count == 6, "Expected six executable events.");
    Assert(result.Documents[0].Events.OfType<DelayMacroEvent>().Select(item => item.Milliseconds).SequenceEqual([50L, 10L]), "Delay conversion is incorrect.");
    Assert(!new MacroValidator().Validate(result.Documents[0]).HasErrors, "Imported fixture should validate.");
}

static void RazerXmlRejectsDtd()
{
    const string xml = "<!DOCTYPE Macro [<!ENTITY xxe SYSTEM 'file:///C:/Windows/win.ini'>]><Macro><Name>&xxe;</Name><MacroEvents /></Macro>";
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
    var result = new RazerMacroXmlImporter().ImportAsync(stream, "unsafe.xml").GetAwaiter().GetResult();

    Assert(result.Documents.Count == 0, "DTD input must not produce a document.");
    Assert(result.Diagnostics.Any(item => item.Code == "RAZER_XML_INVALID"), "DTD rejection diagnostic is missing.");
}

static void XmbcTextFixtureImportsSafely()
{
    var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "basic-key-delay.txt");
    using var stream = File.OpenRead(fixturePath);
    var result = new XmbcMacroTextImporter().ImportAsync(stream, fixturePath).GetAwaiter().GetResult();

    Assert(result.Documents.Count == 1, "Expected one imported XMBC macro.");
    Assert(result.Documents[0].Events.Count == 6, "Expected six XMBC events.");
    Assert(result.Documents[0].Events.OfType<DelayMacroEvent>().Select(item => item.Milliseconds).SequenceEqual([50L, 10L]), "XMBC delays are incorrect.");
    Assert(!new MacroValidator().Validate(result.Documents[0]).HasErrors, "Imported XMBC fixture should validate.");
}

static void XmbcUnknownTokenIsPreservedAsError()
{
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a{UNVERIFIED}b"));
    var result = new XmbcMacroTextImporter().ImportAsync(stream, "unknown.txt").GetAwaiter().GetResult();

    Assert(result.Documents[0].Events.OfType<UnknownMacroEvent>().Any(), "Unknown token must remain in the event model.");
    Assert(result.Diagnostics.Any(item => item.Code == "XMBC_TOKEN_UNKNOWN"), "Unknown token diagnostic is missing.");
    Assert(new MacroValidator().Validate(result.Documents[0]).HasErrors, "Unknown token must block validation.");
}

static void NestedMacrosFlattenByGuid()
{
    var child = CreateNamedDocument(
        "Child",
        new KeyMacroEvent(0, 65, InputTransition.Down),
        new KeyMacroEvent(1, 65, InputTransition.Up));
    var root = CreateNamedDocument(
        "Root",
        new DelayMacroEvent(0, 10),
        new MacroReferenceEvent(1, child.Id, null, child.Name),
        new DelayMacroEvent(2, 20));

    var result = new NestedMacroResolver().Resolve(root, [root, child]);
    var flattened = result.Document ?? throw new InvalidOperationException("Expected flattened macro.");
    Assert(flattened.Events.Count == 4, "Expected four flattened events.");
    Assert(!flattened.Events.OfType<MacroReferenceEvent>().Any(), "Flattened macro must not contain references.");
    Assert(flattened.Events.Select(item => item.Sequence).SequenceEqual([0L, 1L, 2L, 3L]), "Flattened events must be resequenced.");
}

static void MissingNestedMacroIsRejected()
{
    var root = CreateNamedDocument(
        "Root",
        new MacroReferenceEvent(0, Guid.NewGuid(), null, "Missing"));

    var result = new NestedMacroResolver().Resolve(root, [root]);
    Assert(result.Document is null, "Missing reference must not produce a flattened document.");
    Assert(result.Diagnostics.Any(item => item.Code == "REFERENCE_MISSING"), "Missing reference diagnostic is absent.");
}

static void NestedMacroCycleIsRejected()
{
    var firstId = Guid.NewGuid();
    var secondId = Guid.NewGuid();
    var first = new MacroDocument(firstId, "First", [new MacroReferenceEvent(0, secondId, null, "Second")]);
    var second = new MacroDocument(secondId, "Second", [new MacroReferenceEvent(0, firstId, null, "First")]);

    var result = new NestedMacroResolver().Resolve(first, [first, second]);
    Assert(result.Document is null, "Cycle must not produce a flattened document.");
    Assert(result.Diagnostics.Any(item => item.Code == "REFERENCE_CYCLE"), "Cycle diagnostic is absent.");
}

static void NestedMacroCanFallBackToIndex()
{
    var child = CreateNamedDocument("Child", new DelayMacroEvent(0, 5));
    var root = CreateNamedDocument("Root", new MacroReferenceEvent(0, Guid.NewGuid(), 1, "Child"));

    var result = new NestedMacroResolver().Resolve(root, [root, child]);
    Assert(result.Document is not null, "Valid fallback index should resolve.");
    Assert(result.Diagnostics.Any(item => item.Code == "REFERENCE_GUID_FALLBACK_INDEX"), "Fallback warning is absent.");
}

static void NestedExpansionEventLimitIsEnforced()
{
    var child = CreateNamedDocument("Child", new DelayMacroEvent(0, 1), new DelayMacroEvent(1, 1));
    var root = CreateNamedDocument("Root", new MacroReferenceEvent(0, child.Id, null, "Child"));

    var result = new NestedMacroResolver().Resolve(root, [root, child], new MacroLimits(MaximumEventsPerMacro: 1));
    Assert(result.Document is null, "Over-limit expansion must not produce a document.");
    Assert(result.Diagnostics.Any(item => item.Code == "REFERENCE_EVENT_LIMIT"), "Expansion limit diagnostic is absent.");
}

static void Synapse4PackageImportsAndFlattens()
{
    var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "nested-macros.synapse4");
    using var stream = File.OpenRead(fixturePath);
    var imported = new Synapse4Importer().ImportAsync(stream, fixturePath).GetAwaiter().GetResult();
    Assert(imported.Documents.Count == 2, "Expected two macros from the package.");

    var root = imported.Documents.Single(item => item.Id == Guid.Parse("22222222-2222-2222-2222-222222222222"));
    var resolved = new NestedMacroResolver().Resolve(root, imported.Documents);
    var flattened = resolved.Document ?? throw new InvalidOperationException("Expected package macro to flatten.");
    Assert(flattened.Events.Count == 4, "Expected child events plus parent delay.");
    Assert(!flattened.Events.OfType<MacroReferenceEvent>().Any(), "Package flattening left a reference event.");
    Assert(!new MacroValidator().Validate(flattened).HasErrors, "Flattened package macro should validate.");
}

static void Synapse4InvalidPayloadIsDiagnosed()
{
    const string package = "{\"macros\":[{\"name\":\"Broken\",\"payload\":\"not-base64\",\"hash\":\"ignored\"}]}";
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(package));
    var imported = new Synapse4Importer().ImportAsync(stream, "broken.synapse4").GetAwaiter().GetResult();
    Assert(imported.Documents.Count == 0, "Invalid payload must not create a macro.");
    Assert(imported.Diagnostics.Any(item => item.Code == "SYNAPSE4_MACRO_INVALID"), "Invalid payload diagnostic is absent.");
}

static void Synapse4MalformedEventIsPreserved()
{
    const string inner = "{\"guid\":\"33333333-3333-3333-3333-333333333333\",\"macroEvents\":[123]}";
    var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(inner));
    var package = $"{{\"macros\":[{{\"name\":\"Malformed\",\"payload\":\"{payload}\",\"hash\":\"ignored\"}}]}}";
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(package));
    var imported = new Synapse4Importer().ImportAsync(stream, "malformed.synapse4").GetAwaiter().GetResult();

    Assert(imported.Documents.Count == 1, "Malformed event should not discard the containing macro.");
    Assert(imported.Documents[0].Events.OfType<UnknownMacroEvent>().Any(), "Malformed event must remain visible.");
    Assert(imported.Diagnostics.Any(item => item.Code == "SYNAPSE4_EVENT_INVALID"), "Malformed event diagnostic is absent.");
}

static void XmbcSettingsExtractAction28Macros()
{
    var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings-action28.xml");
    using var stream = File.OpenRead(fixturePath);
    var imported = new XmbcSettingsImporter().ImportAsync(stream, fixturePath).GetAwaiter().GetResult();

    Assert(imported.Documents.Count == 4, "Expected four action=28 mappings.");
    Assert(imported.Documents.Any(item => item.Name.Contains("默认配置 / 第 1 层：默认层 / Middle / 基础宏", StringComparison.Ordinal)), "Default layer name is incorrect.");
    Assert(imported.Documents.Any(item => item.Name.Contains("和弦 Middle+Left", StringComparison.Ordinal)), "Chord name is incorrect.");
    Assert(imported.Documents.Any(item => item.Name.Contains("第 2 层：第二层 / XLeft", StringComparison.Ordinal)), "Second layer name is incorrect.");
    var applicationMacro = imported.Documents.Single(item => item.Name.Contains("演示应用 (demo.exe)", StringComparison.Ordinal));
    Assert(applicationMacro.Metadata?["xmbc.application"] == "demo.exe", "Application metadata is missing.");
    Assert(applicationMacro.Metadata?["xmbc.keyaction"] == "1", "keyaction metadata is missing.");
    Assert(imported.Documents.All(item => !new MacroValidator().Validate(item).HasErrors), "Fixture macros should validate.");
}

static void XmbcSettingsRejectDtd()
{
    const string xml = "<!DOCTYPE root [<!ENTITY xxe SYSTEM 'file:///C:/Windows/win.ini'>]><root><version major='2'/><Default><Left action='28' keys='&xxe;'/></Default></root>";
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
    var imported = new XmbcSettingsImporter().ImportAsync(stream, "unsafe.xmbcp").GetAwaiter().GetResult();

    Assert(imported.Documents.Count == 0, "DTD input must not create XMBC macros.");
    Assert(imported.Diagnostics.Any(item => item.Code == "XMBC_SETTINGS_INVALID"), "DTD rejection diagnostic is absent.");
}

static void XmbcTextExportRoundTrips()
{
    var original = CreateDocument(
        new KeyMacroEvent(0, 65, InputTransition.Down),
        new DelayMacroEvent(1, 50),
        new KeyMacroEvent(2, 65, InputTransition.Up),
        new MouseMacroEvent(3, MouseButton.Left, InputTransition.Down),
        new DelayMacroEvent(4, 10),
        new MouseMacroEvent(5, MouseButton.Left, InputTransition.Up));
    using var output = new MemoryStream();
    var exportDiagnostics = new XmbcMacroTextExporter().ExportAsync(original, output).GetAwaiter().GetResult();
    Assert(!exportDiagnostics.Any(item => item.Severity == XMacroBridge.Core.Diagnostics.DiagnosticSeverity.Error), "XMBC export should succeed.");

    output.Position = 0;
    var imported = new XmbcMacroTextImporter().ImportAsync(output, "roundtrip.txt").GetAwaiter().GetResult();
    Assert(EventSignatures(imported.Documents.Single()).SequenceEqual(EventSignatures(original)), "XMBC round trip changed events.");
}

static void XmbcTextExportRejectsInvalidKey()
{
    var document = CreateDocument(
        new KeyMacroEvent(0, 300, InputTransition.Down),
        new KeyMacroEvent(1, 300, InputTransition.Up));
    using var output = new MemoryStream();
    var diagnostics = new XmbcMacroTextExporter().ExportAsync(document, output).GetAwaiter().GetResult();

    Assert(diagnostics.Any(item => item.Code == "KEY_CODE_OUT_OF_RANGE"), "Invalid key diagnostic is absent.");
    Assert(output.Length == 0, "Failed export must not write partial output.");
}

static void RazerXmlExportRoundTrips()
{
    var original = new MacroDocument(
        Guid.NewGuid(),
        "往返测试",
        [
            new KeyMacroEvent(0, 65, InputTransition.Down),
            new DelayMacroEvent(1, 50),
            new KeyMacroEvent(2, 65, InputTransition.Up),
            new MouseMacroEvent(3, MouseButton.Right, InputTransition.Down),
            new DelayMacroEvent(4, 10),
            new MouseMacroEvent(5, MouseButton.Right, InputTransition.Up),
        ]);
    using var output = new MemoryStream();
    var exportDiagnostics = new RazerMacroXmlExporter().ExportAsync(original, output).GetAwaiter().GetResult();
    Assert(!exportDiagnostics.Any(item => item.Severity == XMacroBridge.Core.Diagnostics.DiagnosticSeverity.Error), "Razer export should succeed.");

    output.Position = 0;
    var imported = new RazerMacroXmlImporter().ImportAsync(output, "roundtrip.xml").GetAwaiter().GetResult();
    Assert(imported.Documents.Single().Name == "往返测试", "Razer macro name changed.");
    Assert(EventSignatures(imported.Documents.Single()).SequenceEqual(EventSignatures(original)), "Razer round trip changed events.");
}

static void RazerXmlExportRejectsUnsupportedMouse()
{
    var document = CreateDocument(
        new MouseMacroEvent(0, MouseButton.Middle, InputTransition.Down),
        new MouseMacroEvent(1, MouseButton.Middle, InputTransition.Up));
    using var output = new MemoryStream();
    var diagnostics = new RazerMacroXmlExporter().ExportAsync(document, output).GetAwaiter().GetResult();

    Assert(diagnostics.Any(item => item.Code == "RAZER_EXPORT_MOUSE_UNSUPPORTED"), "Unsupported Razer mouse diagnostic is absent.");
    Assert(output.Length == 0, "Failed Razer export must not write partial output.");
}

static void XmbcModifiersApplyToNextKey()
{
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{CTRL}a{CTRL}s"));
    var imported = new XmbcMacroTextImporter().ImportAsync(stream, "modifiers.txt").GetAwaiter().GetResult();
    var document = imported.Documents.Single();

    Assert(document.Events.Count == 8, "Expected two Ctrl-modified key clicks.");
    Assert(EventSignatures(document).Take(4).SequenceEqual([
        "key:17:Down:False",
        "key:65:Down:False",
        "key:65:Up:False",
        "key:17:Up:False",
    ]), "Modifier event order is incorrect.");
    Assert(!new MacroValidator().Validate(document).HasErrors, "Modifier sequence should validate.");
}

static void XmbcHoldMsAppliesToNextKey()
{
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{HOLDMS:50}r"));
    var imported = new XmbcMacroTextImporter().ImportAsync(stream, "hold.txt").GetAwaiter().GetResult();
    Assert(EventSignatures(imported.Documents.Single()).SequenceEqual([
        "key:82:Down:False",
        "delay:50",
        "key:82:Up:False",
    ]), "HOLDMS sequence is incorrect.");
}

static void XmbcPressReleaseSpanMultipleKeys()
{
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{PRESS}abc{WAITMS:100}{RELEASE}cba"));
    var imported = new XmbcMacroTextImporter().ImportAsync(stream, "press-release.txt").GetAwaiter().GetResult();
    var document = imported.Documents.Single();
    Assert(document.Events.Count == 7, "PRESS/RELEASE should span three keys and one delay.");
    Assert(!new MacroValidator().Validate(document).HasErrors, "Balanced multi-key PRESS/RELEASE should validate.");
}

static void XmbcExtendedTagsImport()
{
    const string text = "{F24}{MMB}{MB4}{MWUP}{NUM9}{VOL+}{MEDIAPLAY}{BACK}";
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
    var imported = new XmbcMacroTextImporter().ImportAsync(stream, "extended.txt").GetAwaiter().GetResult();
    var document = imported.Documents.Single();

    Assert(imported.Diagnostics.Count == 0, "Confirmed extended tags should not produce diagnostics.");
    Assert(document.Events.OfType<KeyMacroEvent>().Any(item => item.VirtualKey == 0x87), "F24 was not mapped.");
    Assert(document.Events.OfType<MouseMacroEvent>().Any(item => item.Button == MouseButton.Middle), "MMB was not mapped.");
    Assert(document.Events.OfType<MouseMacroEvent>().Any(item => item.Button == MouseButton.XButton1), "MB4 was not mapped.");
    Assert(document.Events.OfType<MouseMacroEvent>().Any(item => item.Button == MouseButton.WheelUp), "MWUP was not mapped.");
    Assert(!new MacroValidator().Validate(document).HasErrors, "Extended tag sequence should validate.");
}

static void XmbcModifierStatesExportRoundTrip()
{
    var original = CreateDocument(
        new KeyMacroEvent(0, 0x11, InputTransition.Down, "CTRL"),
        new KeyMacroEvent(1, 65, InputTransition.Down, "A"),
        new KeyMacroEvent(2, 65, InputTransition.Up, "A"),
        new KeyMacroEvent(3, 0x11, InputTransition.Up, "CTRL"));
    using var output = new MemoryStream();
    var diagnostics = new XmbcMacroTextExporter().ExportAsync(original, output).GetAwaiter().GetResult();
    Assert(!diagnostics.Any(item => item.Severity == XMacroBridge.Core.Diagnostics.DiagnosticSeverity.Error), "Modifier export should succeed.");

    output.Position = 0;
    var imported = new XmbcMacroTextImporter().ImportAsync(output, "modifier-roundtrip.txt").GetAwaiter().GetResult();
    Assert(EventSignatures(imported.Documents.Single()).SequenceEqual(EventSignatures(original)), "Modifier state round trip changed events.");
}

static void RazerXmlExportRejectsExtendedKey()
{
    var document = CreateDocument(
        new KeyMacroEvent(0, 0x0D, InputTransition.Down, "NUMENTER", true),
        new KeyMacroEvent(1, 0x0D, InputTransition.Up, "NUMENTER", true));
    using var output = new MemoryStream();
    var diagnostics = new RazerMacroXmlExporter().ExportAsync(document, output).GetAwaiter().GetResult();

    Assert(diagnostics.Any(item => item.Code == "RAZER_EXPORT_EXTENDED_KEY_UNSUPPORTED"), "Extended key diagnostic is absent.");
    Assert(output.Length == 0, "Failed extended-key export must not write output.");
}

static void XmbcRandomDelayRoundTrips()
{
    using var input = new MemoryStream(Encoding.UTF8.GetBytes("{WAITMS:10-20}"));
    var imported = new XmbcMacroTextImporter().ImportAsync(input, "random.txt").GetAwaiter().GetResult();
    var document = imported.Documents.Single();
    var randomDelay = document.Events.OfType<RandomDelayMacroEvent>().Single();
    Assert(randomDelay.MinimumMilliseconds == 10 && randomDelay.MaximumMilliseconds == 20, "Random delay range changed.");
    Assert(!new MacroValidator().Validate(document).HasErrors, "Valid random delay should validate.");

    using var output = new MemoryStream();
    var diagnostics = new XmbcMacroTextExporter().ExportAsync(document, output).GetAwaiter().GetResult();
    Assert(!diagnostics.Any(item => item.Severity == XMacroBridge.Core.Diagnostics.DiagnosticSeverity.Error), "Random delay XMBC export should succeed.");
    Assert(Encoding.UTF8.GetString(output.ToArray()) == "{WAITMS:10-20}", "Random delay text changed.");
}

static void XmbcInvalidRandomDelayIsRejected()
{
    var document = CreateDocument(new RandomDelayMacroEvent(0, 20, 10));
    var result = new MacroValidator().Validate(document);
    Assert(result.Diagnostics.Any(item => item.Code == "RANDOM_DELAY_RANGE_INVALID"), "Invalid random delay diagnostic is absent.");
}

static void XmbcScanCodesRoundTrip()
{
    const string text = "{PRESS}{SC:30}{RELEASE}{SC:30}{PRESS}{SCE:28}{RELEASE}{SCE:28}";
    using var input = new MemoryStream(Encoding.UTF8.GetBytes(text));
    var imported = new XmbcMacroTextImporter().ImportAsync(input, "scan.txt").GetAwaiter().GetResult();
    var document = imported.Documents.Single();
    Assert(document.Events.OfType<ScanCodeMacroEvent>().Count() == 4, "Expected four scan-code transitions.");
    Assert(document.Events.OfType<ScanCodeMacroEvent>().Any(item => item.IsExtended), "Extended scan code was not preserved.");
    Assert(!new MacroValidator().Validate(document).HasErrors, "Scan-code sequence should validate.");

    using var output = new MemoryStream();
    var diagnostics = new XmbcMacroTextExporter().ExportAsync(document, output).GetAwaiter().GetResult();
    Assert(!diagnostics.Any(item => item.Severity == XMacroBridge.Core.Diagnostics.DiagnosticSeverity.Error), "Scan-code XMBC export should succeed.");
    output.Position = 0;
    var roundTrip = new XmbcMacroTextImporter().ImportAsync(output, "scan-roundtrip.txt").GetAwaiter().GetResult();
    Assert(EventSignatures(roundTrip.Documents.Single()).SequenceEqual(EventSignatures(document)), "Scan-code round trip changed events.");
}

static void XmbcCommandsArePreservedAndBlockedForRazer()
{
    const string text = "{MADD:10,-5}{RUN:C:\\Tool.exe}{LAYER:next}{OD}";
    using var input = new MemoryStream(Encoding.UTF8.GetBytes(text));
    var imported = new XmbcMacroTextImporter().ImportAsync(input, "commands.txt").GetAwaiter().GetResult();
    var document = imported.Documents.Single();
    Assert(document.Events.OfType<XmbcCommandMacroEvent>().Count() == 4, "Expected four preserved XMBC commands.");
    Assert(!new MacroValidator().Validate(document).HasErrors, "Preserved XMBC commands are valid source events.");

    using var xmbcOutput = new MemoryStream();
    var xmbcDiagnostics = new XmbcMacroTextExporter().ExportAsync(document, xmbcOutput).GetAwaiter().GetResult();
    Assert(!xmbcDiagnostics.Any(item => item.Severity == XMacroBridge.Core.Diagnostics.DiagnosticSeverity.Error), "XMBC command re-export should succeed.");
    Assert(Encoding.UTF8.GetString(xmbcOutput.ToArray()) == text, "XMBC commands were not preserved exactly.");

    using var razerOutput = new MemoryStream();
    var razerDiagnostics = new RazerMacroXmlExporter().ExportAsync(document, razerOutput).GetAwaiter().GetResult();
    Assert(razerDiagnostics.Any(item => item.Code == "RAZER_EXPORT_XMBC_COMMAND_UNSUPPORTED"), "Razer command incompatibility is absent.");
    Assert(razerOutput.Length == 0, "Incompatible command export must not write Razer XML.");
}

static void ApplicationImportServiceHandlesFixtureDirectory()
{
    var tempDirectory = CreateTemporaryDirectory();
    try
    {
        foreach (var name in new[] { "basic-key-delay.xml", "nested-macros.synapse4", "basic-key-delay.txt", "settings-action28.xml" })
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), Path.Combine(tempDirectory, name));
        }

        var service = new MacroImportService(MacroFormatRegistry.CreateDefault());
        var result = service.ImportAsync([tempDirectory]).GetAwaiter().GetResult();
        Assert(result.ProcessedFiles.Count == 4, "Expected four processed fixture files.");
        Assert(result.Documents.Count == 8, "Expected eight macros from all fixture formats.");
        Assert(!result.Diagnostics.Any(item => item.Severity == XMacroBridge.Core.Diagnostics.DiagnosticSeverity.Error), "Fixture directory import should not contain errors.");
    }
    finally
    {
        Directory.Delete(tempDirectory, true);
    }
}

static void ApplicationImportServiceDiagnosesUnknownXml()
{
    var tempDirectory = CreateTemporaryDirectory();
    try
    {
        var filePath = Path.Combine(tempDirectory, "unknown.xml");
        File.WriteAllText(filePath, "<unknown />", Encoding.UTF8);
        var service = new MacroImportService(MacroFormatRegistry.CreateDefault());
        var result = service.ImportAsync([filePath]).GetAwaiter().GetResult();
        Assert(result.Diagnostics.Any(item => item.Code == "IMPORT_FORMAT_UNSUPPORTED"), "Unknown format diagnostic is absent.");
    }
    finally
    {
        Directory.Delete(tempDirectory, true);
    }
}

static void SafeExportWritesAtomicallyAndProtectsSource()
{
    var tempDirectory = CreateTemporaryDirectory();
    try
    {
        var targetPath = Path.Combine(tempDirectory, "export.xml");
        var document = CreateDocument(
            new KeyMacroEvent(0, 65, InputTransition.Down),
            new KeyMacroEvent(1, 65, InputTransition.Up));
        var service = new SafeExportService(MacroFormatRegistry.CreateDefault());
        var result = service.ExportAsync(document, "razer.macro.xml", targetPath).GetAwaiter().GetResult();
        Assert(result.Succeeded && File.Exists(targetPath), "Safe export did not create the target.");
        Assert(!Directory.EnumerateFiles(tempDirectory, "*.tmp").Any(), "Successful export left a temporary file.");

        var sourceDocument = document with { SourcePath = targetPath };
        var blocked = service.ExportAsync(sourceDocument, "razer.macro.xml", targetPath, overwrite: true).GetAwaiter().GetResult();
        Assert(!blocked.Succeeded, "Source overwrite must be blocked.");
        Assert(blocked.Diagnostics.Any(item => item.Code == "EXPORT_SOURCE_OVERWRITE_BLOCKED"), "Source protection diagnostic is absent.");
    }
    finally
    {
        Directory.Delete(tempDirectory, true);
    }
}

static void FailedSafeExportCleansTemporaryFiles()
{
    var tempDirectory = CreateTemporaryDirectory();
    try
    {
        var targetPath = Path.Combine(tempDirectory, "failed.xml");
        var document = CreateDocument(new XmbcCommandMacroEvent(0, "{LAYER:next}", "layer"));
        var service = new SafeExportService(MacroFormatRegistry.CreateDefault());
        var result = service.ExportAsync(document, "razer.macro.xml", targetPath).GetAwaiter().GetResult();
        Assert(!result.Succeeded && !File.Exists(targetPath), "Failed export created a target file.");
        Assert(!Directory.EnumerateFiles(tempDirectory).Any(), "Failed export left temporary or backup files.");
    }
    finally
    {
        Directory.Delete(tempDirectory, true);
    }
}

static void SafeExportRejectsWindowsReservedNames()
{
    var tempDirectory = CreateTemporaryDirectory();
    try
    {
        var document = CreateDocument(
            new KeyMacroEvent(0, 65, InputTransition.Down),
            new KeyMacroEvent(1, 65, InputTransition.Up));
        var service = new SafeExportService(MacroFormatRegistry.CreateDefault());
        var result = service.ExportAsync(document, "razer.macro.xml", Path.Combine(tempDirectory, "CON.xml")).GetAwaiter().GetResult();
        Assert(!result.Succeeded, "Windows reserved file name must be rejected.");
        Assert(result.Diagnostics.Any(item => item.Code == "EXPORT_FILE_NAME_RESERVED"), "Reserved name diagnostic is absent.");
    }
    finally
    {
        Directory.Delete(tempDirectory, true);
    }
}

static void WorkspaceImportsFixturesAndRefreshesEventRows()
{
    var tempDirectory = CreateFixtureDirectory();
    try
    {
        var viewModel = WorkspaceViewModel.CreateDefault();
        viewModel.ImportAsync([tempDirectory]).GetAwaiter().GetResult();

        Assert(viewModel.Macros.Count == 8, "Workspace should expose all eight imported macros.");
        Assert(viewModel.SelectedMacro is not null, "Workspace should select the first imported macro.");
        var selected = viewModel.SelectedMacro ?? throw new InvalidOperationException("Workspace selection is missing.");
        Assert(viewModel.Events.Count == selected.Events.Count, "Selected macro event rows are out of sync.");
        Assert(viewModel.MacroCountText == "8 个宏", "Macro count summary is incorrect.");

        var alternate = viewModel.Macros.First(item => !ReferenceEquals(item, viewModel.SelectedMacro));
        viewModel.SelectedMacro = alternate;
        Assert(viewModel.Events.Count == alternate.Events.Count, "Changing selection did not rebuild event rows.");
    }
    finally
    {
        Directory.Delete(tempDirectory, true);
    }
}

static void WorkspaceExpandsNestedMacrosBeforeExport()
{
    var tempDirectory = CreateTemporaryDirectory();
    try
    {
        var child = CreateNamedDocument(
            "子宏",
            new KeyMacroEvent(0, 65, InputTransition.Down),
            new DelayMacroEvent(1, 20),
            new KeyMacroEvent(2, 65, InputTransition.Up));
        var root = CreateNamedDocument(
            "父宏",
            new MacroReferenceEvent(0, child.Id, null, child.Name));
        var viewModel = WorkspaceViewModel.CreateDefault();
        viewModel.Macros.Add(root);
        viewModel.Macros.Add(child);
        viewModel.SelectedMacro = root;
        viewModel.TargetFormatId = "xmbc.macro.text";

        var targetPath = Path.Combine(tempDirectory, "flattened.txt");
        var result = viewModel.ExportAsync(targetPath).GetAwaiter().GetResult();
        Assert(result.Succeeded, "Nested workspace export should succeed after expansion.");

        using var stream = File.OpenRead(targetPath);
        var imported = new XmbcMacroTextImporter().ImportAsync(stream, targetPath).GetAwaiter().GetResult();
        Assert(imported.Documents.Single().Events.All(item => item is not MacroReferenceEvent), "Export still contains a macro reference.");
        Assert(EventSignatures(imported.Documents.Single()).SequenceEqual(EventSignatures(child)), "Expanded events changed during export.");
    }
    finally
    {
        Directory.Delete(tempDirectory, true);
    }
}

static void WorkspaceExportsBothSupportedTargetFormats()
{
    var tempDirectory = CreateTemporaryDirectory();
    try
    {
        var document = CreateNamedDocument(
            "双格式",
            new KeyMacroEvent(0, 65, InputTransition.Down),
            new DelayMacroEvent(1, 10),
            new KeyMacroEvent(2, 65, InputTransition.Up));
        var viewModel = WorkspaceViewModel.CreateDefault();
        viewModel.Macros.Add(document);
        viewModel.SelectedMacro = document;

        var razerPath = Path.Combine(tempDirectory, "macro.xml");
        var razerResult = viewModel.ExportAsync(razerPath).GetAwaiter().GetResult();
        Assert(razerResult.Succeeded && File.Exists(razerPath), "Workspace Razer export failed.");

        viewModel.TargetFormatId = "xmbc.macro.text";
        var xmbcPath = Path.Combine(tempDirectory, "macro.txt");
        var xmbcResult = viewModel.ExportAsync(xmbcPath).GetAwaiter().GetResult();
        Assert(xmbcResult.Succeeded && File.Exists(xmbcPath), "Workspace XMBC export failed.");
    }
    finally
    {
        Directory.Delete(tempDirectory, true);
    }
}

static void WorkspaceBlocksInvalidAndMissingSelection()
{
    var tempDirectory = CreateTemporaryDirectory();
    try
    {
        var viewModel = WorkspaceViewModel.CreateDefault();
        var noSelection = viewModel.ExportAsync(Path.Combine(tempDirectory, "none.xml")).GetAwaiter().GetResult();
        Assert(!noSelection.Succeeded, "Export without a selection must fail.");
        Assert(noSelection.Diagnostics.Any(item => item.Code == "WORKSPACE_NO_SELECTION"), "Missing-selection diagnostic is absent.");

        var invalid = CreateDocument(new KeyMacroEvent(0, 65, InputTransition.Down));
        viewModel.Macros.Add(invalid);
        viewModel.SelectedMacro = invalid;
        Assert(!viewModel.CanExport, "Invalid macro should disable export.");
        Assert(viewModel.Diagnostics.Any(item => item.Code == "KEY_NOT_RELEASED"), "Disabled export should expose its blocking diagnostic.");

        var blocked = viewModel.ExportAsync(Path.Combine(tempDirectory, "invalid.xml")).GetAwaiter().GetResult();
        Assert(!blocked.Succeeded, "Invalid macro export must fail.");
        Assert(blocked.Diagnostics.Any(item => item.Code == "KEY_NOT_RELEASED"), "Validation diagnostic is absent.");
        Assert(!File.Exists(Path.Combine(tempDirectory, "invalid.xml")), "Blocked export created a file.");
    }
    finally
    {
        Directory.Delete(tempDirectory, true);
    }
}

static string CreateFixtureDirectory()
{
    var directory = CreateTemporaryDirectory();
    foreach (var name in new[] { "basic-key-delay.xml", "nested-macros.synapse4", "basic-key-delay.txt", "settings-action28.xml" })
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), Path.Combine(directory, name));
    }

    return directory;
}

static string CreateTemporaryDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), "XMacroBridge.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static IEnumerable<string> EventSignatures(MacroDocument document) =>
    document.Events.OrderBy(item => item.Sequence).Select(item => item switch
    {
        DelayMacroEvent delay => $"delay:{delay.Milliseconds}",
        RandomDelayMacroEvent randomDelay => $"random-delay:{randomDelay.MinimumMilliseconds}:{randomDelay.MaximumMilliseconds}",
        KeyMacroEvent key => $"key:{key.VirtualKey}:{key.Transition}:{key.IsExtended}",
        MouseMacroEvent mouse => $"mouse:{mouse.Button}:{mouse.Transition}",
        ScanCodeMacroEvent scanCode => $"scan:{scanCode.ScanCode}:{scanCode.Transition}:{scanCode.IsExtended}",
        XmbcCommandMacroEvent command => $"xmbc-command:{command.Category}:{command.RawTag}",
        MacroReferenceEvent reference => $"reference:{reference.TargetGuid}:{reference.TargetIndex}",
        UnknownMacroEvent unknown => $"unknown:{unknown.SourceType}:{unknown.RawPayload}",
        _ => item.GetType().Name,
    });

static MacroDocument CreateDocument(params MacroEvent[] events) =>
    new(Guid.NewGuid(), "Test macro", events);

static MacroDocument CreateNamedDocument(string name, params MacroEvent[] events) =>
    new(Guid.NewGuid(), name, events);

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
