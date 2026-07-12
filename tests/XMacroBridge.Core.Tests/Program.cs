using XMacroBridge.Application.Exporting;
using XMacroBridge.Application.Formats;
using XMacroBridge.Application.Importing;
using XMacroBridge.Core.Abstractions;
using XMacroBridge.Core.Conversion;
using XMacroBridge.Core.Diagnostics;
using XMacroBridge.Core.Models;
using XMacroBridge.Formats.Razer;
using XMacroBridge.Formats.Xmbc;
using XMacroBridge.Presentation.Workspace;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

var tests = new (string Name, Action Run)[]
{
    ("Balanced keyboard macro is valid", BalancedKeyboardMacroIsValid),
    ("Release without press is rejected", ReleaseWithoutPressIsRejected),
    ("Unreleased mouse button is rejected", UnreleasedMouseButtonIsRejected),
    ("Negative delay is rejected", NegativeDelayIsRejected),
    ("Event limit is enforced", EventLimitIsEnforced),
    ("Razer XML fixture imports safely", RazerXmlFixtureImportsSafely),
    ("Razer XML native zero-based mouse codes import", RazerXmlNativeZeroBasedMouseCodesImport),
    ("Razer XML rejects DTD", RazerXmlRejectsDtd),
    ("Razer XML malformed event preserves surrounding events", RazerXmlMalformedEventPreservesSurroundingEvents),
    ("Razer delay precision loss is diagnosed", RazerDelayPrecisionLossIsDiagnosed),
    ("Razer XML Type 6 millisecond delay imports", RazerXmlType6MillisecondDelayImports),
    ("Razer XML Type 6 loop expands for XMBC and Razer", RazerXmlType6LoopExpandsForBothExports),
    ("Razer XML nested loops expand safely", RazerXmlNestedLoopsExpandSafely),
    ("Razer XML malformed loops are rejected", RazerXmlMalformedLoopsAreRejected),
    ("Razer XML loop expansion respects limits", RazerXmlLoopExpansionRespectsLimits),
    ("XMBC text fixture imports safely", XmbcTextFixtureImportsSafely),
    ("XMBC unknown token is preserved as error", XmbcUnknownTokenIsPreservedAsError),
    ("Unknown-event validation does not expose raw payload", UnknownEventValidationDoesNotExposeRawPayload),
    ("Nested macros flatten by GUID", NestedMacrosFlattenByGuid),
    ("Missing nested macro is rejected", MissingNestedMacroIsRejected),
    ("Nested macro cycle is rejected", NestedMacroCycleIsRejected),
    ("Nested macro can fall back to index", NestedMacroCanFallBackToIndex),
    ("Nested expansion event limit is enforced", NestedExpansionEventLimitIsEnforced),
    ("Synapse4 package imports and flattens", Synapse4PackageImportsAndFlattens),
    ("Synapse4 invalid payload is diagnosed", Synapse4InvalidPayloadIsDiagnosed),
    ("Synapse4 invalid macro entry does not block later entries", Synapse4InvalidMacroEntryDoesNotBlockLaterEntries),
    ("Synapse4 malformed event is preserved", Synapse4MalformedEventIsPreserved),
    ("Synapse4 malformed known event preserves surrounding events", Synapse4MalformedKnownEventPreservesSurroundingEvents),
    ("Synapse4 Type 6 millisecond delay imports", Synapse4Type6MillisecondDelayImports),
    ("Synapse4 Type 6 loop expands", Synapse4Type6LoopExpands),
    ("UTF BOM encodings import consistently", UtfBomEncodingsImportConsistently),
    ("XML importers enforce supported encoding policy", XmlImportersEnforceSupportedEncodingPolicy),
    ("Format diagnostics sanitize source paths", FormatDiagnosticsSanitizeSourcePaths),
    ("Importers stop at the configured event limit", ImportersStopAtConfiguredEventLimit),
    ("Default event limit handles a 100k-event input", DefaultEventLimitHandlesLargeInput),
    ("XMBC settings extract action 28 macros", XmbcSettingsExtractAction28Macros),
    ("XMBC settings reject DTD", XmbcSettingsRejectDtd),
    ("XMBC text export round trips", XmbcTextExportRoundTrips),
    ("XMBC text export rejects invalid key", XmbcTextExportRejectsInvalidKey),
    ("Razer XML export round trips", RazerXmlExportRoundTrips),
    ("Razer XML export uses Synapse 4 input codes", RazerXmlExportUsesSynapse4InputCodes),
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
    ("XMBC extended fixture round trips semantically", XmbcExtendedFixtureRoundTripsSemantically),
    ("XMBC atomic mouse actions export all supported markers", XmbcAtomicMouseActionsExportAllSupportedMarkers),
    ("XMBC rejects non-adjacent atomic wheel pairs", XmbcRejectsNonAdjacentAtomicWheelPairs),
    ("XMBC rejects mismatched and isolated atomic mouse events", XmbcRejectsMalformedAtomicMouseEvents),
    ("Application import service handles fixture directory", ApplicationImportServiceHandlesFixtureDirectory),
    ("Application import service preserves BOM-encoded inputs", ApplicationImportServicePreservesBomEncodedInputs),
    ("Application import service diagnoses unknown XML", ApplicationImportServiceDiagnosesUnknownXml),
    ("Cancelled import releases input handle and supports retry", CancelledImportReleasesInputHandleAndSupportsRetry),
    ("Safe export writes atomically and protects source", SafeExportWritesAtomicallyAndProtectsSource),
    ("Failed safe export cleans temporary files", FailedSafeExportCleansTemporaryFiles),
    ("Cancelled workspace export preserves target and supports retry", CancelledWorkspaceExportPreservesTargetAndSupportsRetry),
    ("Locked export target is preserved and supports retry", LockedExportTargetIsPreservedAndSupportsRetry),
    ("Write failure cleans partial export and preserves target", WriteFailureCleansPartialExportAndPreservesTarget),
    ("Export rejects a file used as the target directory", ExportRejectsFileUsedAsTargetDirectory),
    ("Safe export rejects Windows reserved names", SafeExportRejectsWindowsReservedNames),
    ("Workspace imports fixtures and refreshes event rows", WorkspaceImportsFixturesAndRefreshesEventRows),
    ("Workspace suppresses duplicate unknown-event diagnostics", WorkspaceSuppressesDuplicateUnknownEventDiagnostics),
    ("Workspace expands nested macros before export", WorkspaceExpandsNestedMacrosBeforeExport),
    ("Workspace exports both supported target formats", WorkspaceExportsBothSupportedTargetFormats),
    ("Workspace blocks invalid selection and reports missing selection", WorkspaceBlocksInvalidAndMissingSelection),
    ("Workspace filters and groups diagnostics", WorkspaceFiltersAndGroupsDiagnostics),
    ("Workspace delay editing revalidates and supports history", WorkspaceDelayEditingRevalidatesAndSupportsHistory),
    ("Workspace delay scaling rounds fixed and random delays", WorkspaceDelayScalingRoundsFixedAndRandomDelays),
    ("Workspace delay scaling rejects overflow atomically", WorkspaceDelayScalingRejectsOverflowAtomically),
    ("Workspace edit history is bounded", WorkspaceEditHistoryIsBounded),
    ("Workspace editing isolates duplicate macro identifiers", WorkspaceEditingIsolatesDuplicateMacroIdentifiers),
    ("Workspace inserts and deletes timeline events", WorkspaceInsertsAndDeletesTimelineEvents),
    ("Workspace copied events revalidate safety", WorkspaceCopiedEventsRevalidateSafety),
    ("Workspace moved events normalize order and revalidate", WorkspaceMovedEventsNormalizeOrderAndRevalidate),
    ("Workspace structural editing respects event limit", WorkspaceStructuralEditingRespectsEventLimit),
    ("Workspace inserts parameterized keyboard events", WorkspaceInsertsParameterizedKeyboardEvents),
    ("Workspace rejects invalid virtual-key input", WorkspaceRejectsInvalidVirtualKeyInput),
    ("Workspace inserts parameterized mouse events", WorkspaceInsertsParameterizedMouseEvents),
    ("Workspace searches timeline events and cycles selection", WorkspaceSearchesTimelineEventsAndCyclesSelection),
    ("Workspace searches 100k events within resource budget", WorkspaceSearchesMaximumEventCountWithinResourceBudget),
    ("Workspace repeated search editing remains stable", WorkspaceRepeatedSearchEditingRemainsStable),
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
    Assert(result.Diagnostics.All(item => item.Code != "RAZER_DELAY_PRECISION_LOSS"), "Exact millisecond delays must not produce precision warnings.");
    Assert(!new MacroValidator().Validate(result.Documents[0]).HasErrors, "Imported fixture should validate.");
}

static void RazerXmlNativeZeroBasedMouseCodesImport()
{
    const string xml = """
        <Macro><Name>Native Mouse Codes</Name><MacroEvents>
          <MacroEvent><Type>2</Type><MouseEvent><MouseButton>0</MouseButton><State>0</State></MouseEvent></MacroEvent>
          <MacroEvent><Type>2</Type><MouseEvent><MouseButton>0</MouseButton><State>1</State></MouseEvent></MacroEvent>
          <MacroEvent><Type>6</Type><Number>2</Number><LoopEvent><State>0</State></LoopEvent></MacroEvent>
          <MacroEvent><Type>2</Type><MouseEvent><MouseButton>1</MouseButton><State>0</State></MouseEvent></MacroEvent>
          <MacroEvent><Type>2</Type><MouseEvent><MouseButton>1</MouseButton><State>1</State></MouseEvent></MacroEvent>
          <MacroEvent><Type>6</Type><Number>2</Number><LoopEvent><State>1</State></LoopEvent></MacroEvent>
        </MacroEvents><DelaySetting>0</DelaySetting><Version>4</Version><MouseMoveType>none</MouseMoveType></Macro>
        """;
    using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml));
    var result = new RazerMacroXmlImporter().ImportAsync(input, "native-zero-based.xml").GetAwaiter().GetResult();

    Assert(result.Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error), "Native zero-based mouse codes produced an import error.");
    Assert(
        EventSignatures(result.Documents.Single()).SequenceEqual([
            "mouse:Left:Down",
            "mouse:Left:Up",
            "mouse:Right:Down",
            "mouse:Right:Up",
            "mouse:Right:Down",
            "mouse:Right:Up",
        ]),
        "Native zero-based mouse codes or loop expansion changed mouse semantics.");

    using var output = new MemoryStream();
    var diagnostics = new XmbcMacroTextExporter().ExportAsync(result.Documents.Single(), output).GetAwaiter().GetResult();
    Assert(diagnostics.All(item => item.Severity != DiagnosticSeverity.Error), "Native mouse codes failed XMBC export.");
    var text = Encoding.UTF8.GetString(output.ToArray());
    Assert(text.Contains("{LMBD}{LMBU}", StringComparison.Ordinal), "Native left click did not export to XMBC.");
    Assert(text.Split("{RMBD}{RMBU}", StringSplitOptions.None).Length - 1 == 2, "Expanded native right-click loop did not export twice.");
}

static void RazerXmlRejectsDtd()
{
    const string xml = "<!DOCTYPE Macro [<!ENTITY xxe SYSTEM 'file:///C:/Windows/win.ini'>]><Macro><Name>&xxe;</Name><MacroEvents /></Macro>";
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
    var result = new RazerMacroXmlImporter().ImportAsync(stream, "unsafe.xml").GetAwaiter().GetResult();

    Assert(result.Documents.Count == 0, "DTD input must not produce a document.");
    Assert(result.Diagnostics.Any(item => item.Code == "RAZER_XML_INVALID"), "DTD rejection diagnostic is missing.");
}

static void RazerXmlMalformedEventPreservesSurroundingEvents()
{
    const string xml = """
        <Macro>
          <Name>Mixed</Name>
          <MacroEvents>
            <MacroEvent><Type>1</Type><KeyEvent><Makecode>65</Makecode><State>0</State></KeyEvent></MacroEvent>
            <MacroEvent><Type>0</Type></MacroEvent>
            <MacroEvent><Type>1</Type><KeyEvent><Makecode>65</Makecode><State>1</State></KeyEvent></MacroEvent>
          </MacroEvents>
        </Macro>
        """;
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
    var result = new RazerMacroXmlImporter().ImportAsync(stream, "mixed.xml").GetAwaiter().GetResult();

    Assert(result.Documents.Count == 1, "A malformed Razer event must not discard its containing macro.");
    Assert(result.Documents[0].Events.Count == 3, "Surrounding Razer events must be retained in order.");
    Assert(result.Documents[0].Events[0] is KeyMacroEvent { Transition: InputTransition.Down }, "Leading valid event was lost.");
    Assert(result.Documents[0].Events[1] is UnknownMacroEvent, "Malformed event must be represented as UnknownMacroEvent.");
    Assert(result.Documents[0].Events[2] is KeyMacroEvent { Transition: InputTransition.Up }, "Trailing valid event was lost.");
    var diagnostic = result.Diagnostics.SingleOrDefault(item => item.Code == "RAZER_EVENT_INVALID");
    Assert(diagnostic?.EventSequence == 1, "Malformed Razer event diagnostic is missing its sequence.");
    Assert(diagnostic?.SourceContext == "Mixed", "Malformed Razer event diagnostic is missing macro context.");
    Assert(diagnostic?.Message.Contains("第 2 个雷云事件", StringComparison.Ordinal) == true, "Malformed Razer event diagnostic is missing its source position.");
}

static void RazerDelayPrecisionLossIsDiagnosed()
{
    const string xml = "<Macro><Name>Precision XML</Name><MacroEvents><MacroEvent><Type>0</Type><Number>0.0005</Number></MacroEvent></MacroEvents></Macro>";
    using var xmlStream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
    var xmlResult = new RazerMacroXmlImporter().ImportAsync(xmlStream, "precision.xml").GetAwaiter().GetResult();
    Assert(xmlResult.Documents.Single().Events.Single() is DelayMacroEvent { Milliseconds: 1 }, "Standalone Razer delay should round away from zero.");
    Assert(xmlResult.Diagnostics.Any(item => item.Code == "RAZER_DELAY_PRECISION_LOSS" && item.Severity == DiagnosticSeverity.Warning), "Standalone Razer precision warning is absent.");

    const string inner = "{\"guid\":\"44444444-4444-4444-4444-444444444444\",\"macroEvents\":[{\"Type\":0,\"Number\":\"0.0015\"}]}";
    var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(inner));
    var package = $"{{\"macros\":[{{\"name\":\"Precision Package\",\"payload\":\"{payload}\",\"hash\":\"ignored\"}}]}}";
    using var packageStream = new MemoryStream(Encoding.UTF8.GetBytes(package));
    var packageResult = new Synapse4Importer().ImportAsync(packageStream, "precision.synapse4").GetAwaiter().GetResult();
    Assert(packageResult.Documents.Single().Events.Single() is DelayMacroEvent { Milliseconds: 2 }, "Synapse delay should round away from zero.");
    Assert(packageResult.Diagnostics.Any(item => item.Code == "RAZER_DELAY_PRECISION_LOSS" && item.Severity == DiagnosticSeverity.Warning), "Synapse precision warning is absent.");

    const string negativeXml = "<Macro><Name>Negative</Name><MacroEvents><MacroEvent><Type>0</Type><Number>-0.0004</Number></MacroEvent></MacroEvents></Macro>";
    using var negativeStream = new MemoryStream(Encoding.UTF8.GetBytes(negativeXml));
    var negativeResult = new RazerMacroXmlImporter().ImportAsync(negativeStream, "negative.xml").GetAwaiter().GetResult();
    Assert(negativeResult.Documents.Single().Events.Single() is UnknownMacroEvent, "A negative sub-millisecond delay must not round into a valid zero delay.");
    Assert(negativeResult.Diagnostics.Any(item => item.Code == "RAZER_EVENT_INVALID"), "Negative Razer delay diagnostic is absent.");
}

static void RazerXmlType6MillisecondDelayImports()
{
    var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "type6-millisecond-delay.xml");
    using var stream = File.OpenRead(fixturePath);
    var result = new RazerMacroXmlImporter().ImportAsync(stream, fixturePath).GetAwaiter().GetResult();
    var document = result.Documents.Single();

    Assert(result.Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error), "Type 6 delay produced an import error.");
    Assert(
        document.Events.Count == 3 && document.Events[1] is DelayMacroEvent { Milliseconds: 265 },
        "Type 6 Delay was not imported as an integer millisecond delay.");
    Assert(!new MacroValidator().Validate(document).HasErrors, "Type 6 delay fixture failed state validation.");

    var viewModel = WorkspaceViewModel.CreateDefault();
    viewModel.ImportAsync([fixturePath]).GetAwaiter().GetResult();
    Assert(viewModel.CanExport, "Type 6 delay fixture remained blocked in the workspace.");

    using var xmbcOutput = new MemoryStream();
    var xmbcDiagnostics = new XmbcMacroTextExporter()
        .ExportAsync(document, xmbcOutput)
        .GetAwaiter()
        .GetResult();
    Assert(xmbcDiagnostics.All(item => item.Severity != DiagnosticSeverity.Error), "Type 6 delay failed XMBC export.");
    Assert(
        Encoding.UTF8.GetString(xmbcOutput.ToArray()).Contains("{WAITMS:265}", StringComparison.Ordinal),
        "Type 6 delay did not export as XMBC millisecond delay.");

    using var razerOutput = new MemoryStream();
    var razerDiagnostics = new RazerMacroXmlExporter()
        .ExportAsync(document, razerOutput)
        .GetAwaiter()
        .GetResult();
    Assert(razerDiagnostics.All(item => item.Severity != DiagnosticSeverity.Error), "Type 6 delay failed Razer XML export.");
    razerOutput.Position = 0;
    var roundTrip = new RazerMacroXmlImporter().ImportAsync(razerOutput, "type6-roundtrip.xml").GetAwaiter().GetResult();
    Assert(
        roundTrip.Documents.Single().Events[1] is DelayMacroEvent { Milliseconds: 265 },
        "Type 6 delay changed during Razer XML export round trip.");
}

static void RazerXmlType6LoopExpandsForBothExports()
{
    const string xml = """
        <Macro><Name>Loop Five</Name><MacroEvents>
          <MacroEvent><Type>6</Type><Number>5</Number><LoopEvent><State>0</State></LoopEvent></MacroEvent>
          <MacroEvent><Type>6</Type><Delay>10</Delay></MacroEvent>
          <MacroEvent><Type>6</Type><Number>5</Number><LoopEvent><State>1</State></LoopEvent></MacroEvent>
        </MacroEvents></Macro>
        """;
    using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml));
    var result = new RazerMacroXmlImporter().ImportAsync(input, "loop-five.xml").GetAwaiter().GetResult();
    var document = result.Documents.Single();

    Assert(result.Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error), "A valid Razer loop produced an import error.");
    Assert(document.Events.Count == 5, "A five-count loop did not expand to five body copies.");
    Assert(document.Events.All(item => item is DelayMacroEvent { Milliseconds: 10 }), "Expanded loop body changed semantics.");
    Assert(document.Events.Select(item => item.Sequence).SequenceEqual(Enumerable.Range(0, 5).Select(value => (long)value)), "Expanded loop sequences are not continuous.");
    Assert(document.Events.All(item => item.GetType().Name != "RazerLoopBoundaryEvent"), "Loop boundary leaked into the unified model.");

    using var xmbcOutput = new MemoryStream();
    var xmbcDiagnostics = new XmbcMacroTextExporter().ExportAsync(document, xmbcOutput).GetAwaiter().GetResult();
    var xmbcText = Encoding.UTF8.GetString(xmbcOutput.ToArray());
    Assert(xmbcDiagnostics.All(item => item.Severity != DiagnosticSeverity.Error), "Expanded loop failed XMBC export.");
    Assert(xmbcText.Split("{WAITMS:10}", StringSplitOptions.None).Length - 1 == 5, "XMBC output does not contain five loop-body copies.");

    using var razerOutput = new MemoryStream();
    var razerDiagnostics = new RazerMacroXmlExporter().ExportAsync(document, razerOutput).GetAwaiter().GetResult();
    Assert(razerDiagnostics.All(item => item.Severity != DiagnosticSeverity.Error), "Expanded loop failed Razer export.");
    razerOutput.Position = 0;
    var roundTrip = new RazerMacroXmlImporter().ImportAsync(razerOutput, "loop-roundtrip.xml").GetAwaiter().GetResult();
    Assert(EventSignatures(roundTrip.Documents.Single()).SequenceEqual(EventSignatures(document)), "Linear Razer round trip changed the expanded loop.");

    const string warningXml = """
        <Macro><Name>Loop Warning</Name><MacroEvents>
          <MacroEvent><Type>6</Type><Number>2</Number><LoopEvent><State>0</State></LoopEvent></MacroEvent>
          <MacroEvent><Type>0</Type><Number>0.0005</Number></MacroEvent>
          <MacroEvent><Type>6</Type><Number>2</Number><LoopEvent><State>1</State></LoopEvent></MacroEvent>
        </MacroEvents></Macro>
        """;
    using var warningInput = new MemoryStream(Encoding.UTF8.GetBytes(warningXml));
    var warningResult = new RazerMacroXmlImporter().ImportAsync(warningInput, "loop-warning.xml").GetAwaiter().GetResult();
    Assert(
        warningResult.Diagnostics.Where(item => item.Code == "RAZER_DELAY_PRECISION_LOSS").Select(item => item.EventSequence).SequenceEqual(new long?[] { 0, 1 }),
        "Diagnostics inside an expanded loop were not remapped to every output event.");
}

static void RazerXmlNestedLoopsExpandSafely()
{
    const string xml = """
        <Macro><Name>Nested Loop</Name><MacroEvents>
          <MacroEvent><Type>6</Type><Number>2</Number><LoopEvent><State>0</State></LoopEvent></MacroEvent>
          <MacroEvent><Type>6</Type><Delay>1</Delay></MacroEvent>
          <MacroEvent><Type>6</Type><Number>3</Number><LoopEvent><State>0</State></LoopEvent></MacroEvent>
          <MacroEvent><Type>6</Type><Delay>2</Delay></MacroEvent>
          <MacroEvent><Type>6</Type><Number>3</Number><LoopEvent><State>1</State></LoopEvent></MacroEvent>
          <MacroEvent><Type>6</Type><Number>2</Number><LoopEvent><State>1</State></LoopEvent></MacroEvent>
        </MacroEvents></Macro>
        """;
    using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml));
    var result = new RazerMacroXmlImporter().ImportAsync(input, "nested-loop.xml").GetAwaiter().GetResult();

    Assert(result.Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error), "Valid nested loops produced an error.");
    Assert(
        result.Documents.Single().Events.Cast<DelayMacroEvent>().Select(item => item.Milliseconds).SequenceEqual([1L, 2L, 2L, 2L, 1L, 2L, 2L, 2L]),
        "Nested loop expansion order or count is incorrect.");

    using var depthInput = new MemoryStream(Encoding.UTF8.GetBytes(xml));
    var depthResult = new RazerMacroXmlImporter(new MacroLimits(MaximumNestingDepth: 1))
        .ImportAsync(depthInput, "nested-loop.xml").GetAwaiter().GetResult();
    Assert(depthResult.Diagnostics.Any(item => item.Code == "RAZER_LOOP_DEPTH_LIMIT" && item.Severity == DiagnosticSeverity.Error), "Loop nesting depth limit was not enforced.");
    Assert(depthResult.Documents.Single().Events.OfType<UnknownMacroEvent>().Any(), "Rejected nested loop did not remain visibly blocked.");
}

static void RazerXmlMalformedLoopsAreRejected()
{
    var cases = new[]
    {
        ("missing-start", "<MacroEvent><Type>6</Type><Number>5</Number><LoopEvent><State>1</State></LoopEvent></MacroEvent>"),
        ("missing-end", "<MacroEvent><Type>6</Type><Number>5</Number><LoopEvent><State>0</State></LoopEvent></MacroEvent>"),
        ("mismatch", "<MacroEvent><Type>6</Type><Number>5</Number><LoopEvent><State>0</State></LoopEvent></MacroEvent><MacroEvent><Type>6</Type><Delay>1</Delay></MacroEvent><MacroEvent><Type>6</Type><Number>4</Number><LoopEvent><State>1</State></LoopEvent></MacroEvent>"),
        ("zero", "<MacroEvent><Type>6</Type><Number>0</Number><LoopEvent><State>0</State></LoopEvent></MacroEvent><MacroEvent><Type>6</Type><Number>0</Number><LoopEvent><State>1</State></LoopEvent></MacroEvent>"),
    };

    foreach (var (name, events) in cases)
    {
        var xml = $"<Macro><Name>{name}</Name><MacroEvents>{events}</MacroEvents></Macro>";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var result = new RazerMacroXmlImporter().ImportAsync(input, name + ".xml").GetAwaiter().GetResult();
        Assert(result.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error), $"Malformed loop case {name} was not rejected.");
        Assert(result.Documents.Single().Events.OfType<UnknownMacroEvent>().Any(), $"Malformed loop case {name} did not stay visibly blocked.");
    }
}

static void RazerXmlLoopExpansionRespectsLimits()
{
    const string xml = """
        <Macro><Name>Expansion Limit</Name><MacroEvents>
          <MacroEvent><Type>6</Type><Number>5</Number><LoopEvent><State>0</State></LoopEvent></MacroEvent>
          <MacroEvent><Type>6</Type><Delay>1</Delay></MacroEvent>
          <MacroEvent><Type>6</Type><Number>5</Number><LoopEvent><State>1</State></LoopEvent></MacroEvent>
        </MacroEvents></Macro>
        """;
    using var input = new MemoryStream(Encoding.UTF8.GetBytes(xml));
    var result = new RazerMacroXmlImporter(new MacroLimits(MaximumEventsPerMacro: 4))
        .ImportAsync(input, "expansion-limit.xml").GetAwaiter().GetResult();

    Assert(result.Diagnostics.Any(item => item.Code == "RAZER_LOOP_EXPANSION_LIMIT" && item.Severity == DiagnosticSeverity.Error), "Loop expansion event limit was not enforced.");
    Assert(result.Documents.Single().Events.Count <= 4, "Rejected loop expansion exceeded the configured event limit.");
    Assert(result.Documents.Single().Events.OfType<UnknownMacroEvent>().Any(), "Rejected loop expansion did not remain visibly blocked.");

    const string emptyLoopXml = """
        <Macro><Name>Empty Loop</Name><MacroEvents>
          <MacroEvent><Type>6</Type><Number>2147483647</Number><LoopEvent><State>0</State></LoopEvent></MacroEvent>
          <MacroEvent><Type>6</Type><Number>2147483647</Number><LoopEvent><State>1</State></LoopEvent></MacroEvent>
        </MacroEvents></Macro>
        """;
    using var emptyLoopInput = new MemoryStream(Encoding.UTF8.GetBytes(emptyLoopXml));
    var emptyLoopResult = new RazerMacroXmlImporter().ImportAsync(emptyLoopInput, "empty-loop.xml").GetAwaiter().GetResult();
    Assert(emptyLoopResult.Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error), "A valid empty loop produced an error.");
    Assert(emptyLoopResult.Documents.Single().Events.Count == 0, "An empty loop did not collapse to an empty linear sequence.");
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
    Assert(result.Diagnostics.Single(item => item.Code == "XMBC_TOKEN_UNKNOWN").SourceContext is null, "Unknown XMBC raw token must not become diagnostic source context.");
    Assert(new MacroValidator().Validate(result.Documents[0]).HasErrors, "Unknown token must block validation.");
}

static void UnknownEventValidationDoesNotExposeRawPayload()
{
    const string rawPayload = @"C:\Private\raw-macro-content";
    var document = CreateDocument(new UnknownMacroEvent(0, "broken", rawPayload));
    var diagnostic = new MacroValidator().Validate(document).Diagnostics.Single(item => item.Code == "UNKNOWN_EVENT");

    Assert(diagnostic.SourceContext is null, "Unknown-event raw payload must not be reused as diagnostic source context.");
    Assert(!diagnostic.Message.Contains(rawPayload, StringComparison.Ordinal), "Unknown-event diagnostic message leaked the raw payload.");
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

static void Synapse4InvalidMacroEntryDoesNotBlockLaterEntries()
{
    const string firstInner = "{\"guid\":\"77777777-7777-7777-7777-777777777777\",\"macroEvents\":[]}";
    const string lastInner = "{\"guid\":\"88888888-8888-8888-8888-888888888888\",\"macroEvents\":[]}";
    var firstPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(firstInner));
    var lastPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(lastInner));
    var package = $"{{\"macros\":[" +
                  $"{{\"name\":\"First\",\"payload\":\"{firstPayload}\"}}," +
                  "{\"name\":\"Broken\",\"payload\":\"not-base64\"}," +
                  $"{{\"name\":\"Last\",\"payload\":\"{lastPayload}\"}}]}}";
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(package));
    var imported = new Synapse4Importer().ImportAsync(stream, "mixed-entries.synapse4").GetAwaiter().GetResult();

    Assert(imported.Documents.Select(item => item.Name).SequenceEqual(["First", "Last"]), "Valid Synapse entries around a broken entry must be retained.");
    Assert(imported.Diagnostics.Count(item => item.Code == "SYNAPSE4_MACRO_INVALID") == 1, "Broken Synapse entry requires one isolated diagnostic.");
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

static void Synapse4MalformedKnownEventPreservesSurroundingEvents()
{
    const string inner = """
        {"guid":"55555555-5555-5555-5555-555555555555","macroEvents":[
          {"Type":1,"KeyEvent":{"Makecode":65,"State":0}},
          {"Type":0},
          {"Type":1,"KeyEvent":{"Makecode":"broken","State":1}},
          {"Type":2,"MouseEvent":{"MouseButton":99,"State":0}},
          {"Type":7,"guid":"broken","MPIndex":"broken"},
          {"Type":1,"KeyEvent":{"Makecode":65,"State":1}}
        ]}
        """;
    var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(inner));
    var package = $"{{\"macros\":[{{\"name\":\"Known Malformed\",\"payload\":\"{payload}\",\"hash\":\"ignored\"}}]}}";
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(package));
    var imported = new Synapse4Importer().ImportAsync(stream, "known-malformed.synapse4").GetAwaiter().GetResult();

    Assert(imported.Documents.Count == 1, "A malformed known Synapse event must not discard its containing macro.");
    Assert(imported.Documents[0].Events.Count == 6, "Surrounding Synapse events must be retained in order.");
    Assert(imported.Documents[0].Events[0] is KeyMacroEvent { Transition: InputTransition.Down }, "Leading Synapse event was lost.");
    Assert(imported.Documents[0].Events.Skip(1).Take(4).All(item => item is UnknownMacroEvent), "Every malformed known Synapse event must remain visible.");
    Assert(imported.Documents[0].Events[5] is KeyMacroEvent { Transition: InputTransition.Up }, "Trailing Synapse event was lost.");
    var diagnostics = imported.Diagnostics.Where(item => item.Code == "SYNAPSE4_EVENT_INVALID").OrderBy(item => item.EventSequence).ToArray();
    Assert(diagnostics.Length == 4, "Each malformed known Synapse event requires one diagnostic.");
    Assert(diagnostics.Select(item => item.EventSequence).SequenceEqual(new long?[] { 1, 2, 3, 4 }), "Malformed Synapse event sequences are incorrect.");
    Assert(diagnostics.All(item => item.SourceContext == "Known Malformed"), "Malformed known Synapse diagnostics are missing macro context.");
}

static void Synapse4Type6MillisecondDelayImports()
{
    var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "type6-millisecond-delay.synapse4");
    using var stream = File.OpenRead(fixturePath);
    var result = new Synapse4Importer().ImportAsync(stream, fixturePath).GetAwaiter().GetResult();

    Assert(result.Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error), "Synapse4 Type 6 delay produced an import error.");
    Assert(
        result.Documents.Single().Events.Single() is DelayMacroEvent { Milliseconds: 265 },
        "Synapse4 Type 6 Delay was not imported as an integer millisecond delay.");
}

static void Synapse4Type6LoopExpands()
{
    const string inner = """
        {"guid":"99999999-9999-9999-9999-999999999999","macroEvents":[
          {"Type":6,"Number":5,"LoopEvent":{"State":0}},
          {"Type":6,"Delay":7},
          {"Type":6,"Number":5,"LoopEvent":{"State":1}}
        ]}
        """;
    var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(inner));
    var package = $"{{\"macros\":[{{\"name\":\"Synapse Loop\",\"payload\":\"{payload}\",\"hash\":\"ignored\"}}]}}";
    using var input = new MemoryStream(Encoding.UTF8.GetBytes(package));
    var result = new Synapse4Importer().ImportAsync(input, "loop.synapse4").GetAwaiter().GetResult();

    Assert(result.Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error), "A valid Synapse4 loop produced an import error.");
    Assert(result.Documents.Single().Events.Count == 5, "Synapse4 loop did not expand to five body copies.");
    Assert(result.Documents.Single().Events.All(item => item is DelayMacroEvent { Milliseconds: 7 }), "Synapse4 loop body changed during expansion.");
    Assert(result.Documents.Single().Events.Select(item => item.Sequence).SequenceEqual(Enumerable.Range(0, 5).Select(value => (long)value)), "Synapse4 expanded sequences are not continuous.");
}

static void UtfBomEncodingsImportConsistently()
{
    var encodings = new Encoding[]
    {
        new UTF8Encoding(true, true),
        new UnicodeEncoding(false, true, true),
        new UnicodeEncoding(true, true, true),
    };
    const string razerXml = "<Macro><Name>Encoded Razer</Name><MacroEvents><MacroEvent><Type>0</Type><Number>0.001</Number></MacroEvent></MacroEvents></Macro>";
    const string xmbcSettings = "<root><version major='2'/><Default><Left action='28' keys='a'/></Default></root>";
    const string xmbcText = "a{WAITMS:1}";
    const string synapseInner = "{\"guid\":\"66666666-6666-6666-6666-666666666666\",\"macroEvents\":[]}";
    var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(synapseInner));
    var synapseOuter = $"{{\"macros\":[{{\"name\":\"Encoded Synapse\",\"payload\":\"{payload}\",\"hash\":\"ignored\"}}]}}";
    var registry = MacroFormatRegistry.CreateDefault();
    var lateMacrosHeader = Encoding.UTF8.GetBytes("{\"profiles\":\"" + new string('x', 5_000));
    Assert(new Synapse4Importer().CanImport(lateMacrosHeader.AsSpan(0, 4_096), "late-macros.synapse4"), "Synapse routing must not require macros to appear in the probe window.");

    foreach (var encoding in encodings)
    {
        var label = encoding.WebName;
        var razerBytes = EncodeWithPreamble(razerXml, encoding);
        Assert(registry.FindImporter(razerBytes, "encoded-razer.xml") is RazerMacroXmlImporter, $"Razer XML detection failed for {label}.");
        using var razerStream = new MemoryStream(razerBytes);
        var razerResult = new RazerMacroXmlImporter().ImportAsync(razerStream, "encoded-razer.xml").GetAwaiter().GetResult();
        Assert(razerResult.Documents.Single().Events.Single() is DelayMacroEvent { Milliseconds: 1 }, $"Razer XML import failed for {label}.");

        var settingsBytes = EncodeWithPreamble(xmbcSettings, encoding);
        Assert(registry.FindImporter(settingsBytes, "encoded-settings.xml") is XmbcSettingsImporter, $"XMBC settings detection failed for {label}.");
        using var settingsStream = new MemoryStream(settingsBytes);
        var settingsResult = new XmbcSettingsImporter().ImportAsync(settingsStream, "encoded-settings.xml").GetAwaiter().GetResult();
        Assert(settingsResult.Documents.Count == 1, $"XMBC settings import failed for {label}.");

        var textBytes = EncodeWithPreamble(xmbcText, encoding);
        using var textStream = new MemoryStream(textBytes);
        var textResult = new XmbcMacroTextImporter().ImportAsync(textStream, "encoded.txt").GetAwaiter().GetResult();
        Assert(textResult.Documents.Single().Events.Count == 3, $"XMBC text import failed for {label}.");

        var synapseBytes = EncodeWithPreamble(synapseOuter, encoding);
        Assert(registry.FindImporter(synapseBytes, "encoded.synapse4") is Synapse4Importer, $"Synapse detection failed for {label}.");
        using var synapseStream = new MemoryStream(synapseBytes);
        var synapseResult = new Synapse4Importer().ImportAsync(synapseStream, "encoded.synapse4").GetAwaiter().GetResult();
        Assert(synapseResult.Documents.Count == 1, $"Synapse import failed for {label}.");
    }

    var utf32 = new UTF32Encoding(false, true, true);
    using var invalidTextStream = new MemoryStream(EncodeWithPreamble(xmbcText, utf32));
    var invalidText = new XmbcMacroTextImporter().ImportAsync(invalidTextStream, "utf32.txt").GetAwaiter().GetResult();
    Assert(invalidText.Documents.Count == 0 && invalidText.Diagnostics.Any(item => item.Code == "XMBC_TEXT_INVALID"), "Unsupported UTF-32 text must fail visibly.");

    using var invalidSynapseStream = new MemoryStream(EncodeWithPreamble(synapseOuter, utf32));
    var invalidSynapse = new Synapse4Importer().ImportAsync(invalidSynapseStream, "utf32.synapse4").GetAwaiter().GetResult();
    Assert(invalidSynapse.Documents.Count == 0 && invalidSynapse.Diagnostics.Any(item => item.Code == "SYNAPSE4_INVALID"), "Unsupported UTF-32 Synapse input must fail visibly.");

    var utf8BomPayload = Convert.ToBase64String(EncodeWithPreamble(synapseInner, encodings[0]));
    var utf8BomPackage = $"{{\"macros\":[{{\"name\":\"UTF8 BOM Payload\",\"payload\":\"{utf8BomPayload}\"}}]}}";
    using var utf8BomPayloadStream = new MemoryStream(Encoding.UTF8.GetBytes(utf8BomPackage));
    var utf8BomPayloadResult = new Synapse4Importer().ImportAsync(utf8BomPayloadStream, "utf8-bom-payload.synapse4").GetAwaiter().GetResult();
    Assert(utf8BomPayloadResult.Documents.Count == 1, "UTF-8 BOM Synapse payload should be accepted.");

    var utf16Payload = Convert.ToBase64String(EncodeWithPreamble(synapseInner, encodings[1]));
    var utf16PayloadPackage = $"{{\"macros\":[{{\"name\":\"UTF16 Payload\",\"payload\":\"{utf16Payload}\"}}]}}";
    using var utf16PayloadStream = new MemoryStream(Encoding.UTF8.GetBytes(utf16PayloadPackage));
    var utf16PayloadResult = new Synapse4Importer().ImportAsync(utf16PayloadStream, "utf16-payload.synapse4").GetAwaiter().GetResult();
    Assert(utf16PayloadResult.Documents.Count == 0 && utf16PayloadResult.Diagnostics.Any(item => item.Code == "SYNAPSE4_MACRO_INVALID"), "UTF-16 Synapse payload must be rejected without affecting the package parser.");
}

static void XmlImportersEnforceSupportedEncodingPolicy()
{
    const string validUtf8 = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Macro><Name>UTF8</Name><MacroEvents /></Macro>";
    using var validUtf8Stream = new MemoryStream(Encoding.UTF8.GetBytes(validUtf8));
    Assert(new RazerMacroXmlImporter().ImportAsync(validUtf8Stream, "valid-utf8.xml").GetAwaiter().GetResult().Documents.Count == 1, "Valid UTF-8 XML declaration was rejected.");

    const string validUtf16 = "<?xml version=\"1.0\" encoding=\"UTF-16\"?><Macro><Name>UTF16</Name><MacroEvents /></Macro>";
    using var validUtf16Stream = new MemoryStream(EncodeWithPreamble(validUtf16, new UnicodeEncoding(false, true, true)));
    Assert(new RazerMacroXmlImporter().ImportAsync(validUtf16Stream, "valid-utf16.xml").GetAwaiter().GetResult().Documents.Count == 1, "Valid UTF-16 XML declaration was rejected.");

    const string mismatched = "<?xml version='1.0' encoding='UTF-16'?><Macro><Name>Mismatch</Name><MacroEvents /></Macro>";
    using var mismatchedStream = new MemoryStream(Encoding.UTF8.GetBytes(mismatched));
    var mismatchedResult = new RazerMacroXmlImporter().ImportAsync(mismatchedStream, "mismatched.xml").GetAwaiter().GetResult();
    Assert(mismatchedResult.Documents.Count == 0 && mismatchedResult.Diagnostics.Any(item => item.Code == "RAZER_XML_INVALID"), "Mismatched XML declaration and bytes must be rejected.");

    const string legacyRazer = "<?xml version='1.0' encoding='windows-1252'?><Macro><Name>Legacy</Name><MacroEvents /></Macro>";
    using var legacyRazerStream = new MemoryStream(Encoding.Latin1.GetBytes(legacyRazer));
    var legacyRazerResult = new RazerMacroXmlImporter().ImportAsync(legacyRazerStream, "legacy.xml").GetAwaiter().GetResult();
    Assert(legacyRazerResult.Documents.Count == 0 && legacyRazerResult.Diagnostics.Any(item => item.Code == "RAZER_XML_INVALID"), "Razer XML must reject undeclared legacy code-page support.");

    var longDeclaration = "<?xml version='1.0' " + new string(' ', 9_000) + "encoding='windows-1252'?><Macro><Name>Long</Name><MacroEvents /></Macro>";
    using var longDeclarationStream = new MemoryStream(Encoding.Latin1.GetBytes(longDeclaration));
    var longDeclarationResult = new RazerMacroXmlImporter().ImportAsync(longDeclarationStream, "long-declaration.xml").GetAwaiter().GetResult();
    Assert(longDeclarationResult.Documents.Count == 0 && longDeclarationResult.Diagnostics.Any(item => item.Code == "RAZER_XML_INVALID"), "An oversized XML declaration must not bypass encoding validation.");

    const string legacySettings = "<?xml version='1.0' encoding='windows-1252'?><root><version major='2'/></root>";
    using var legacySettingsStream = new MemoryStream(Encoding.Latin1.GetBytes(legacySettings));
    var legacySettingsResult = new XmbcSettingsImporter().ImportAsync(legacySettingsStream, "legacy-settings.xml").GetAwaiter().GetResult();
    Assert(legacySettingsResult.Documents.Count == 0 && legacySettingsResult.Diagnostics.Any(item => item.Code == "XMBC_SETTINGS_INVALID"), "XMBC settings must reject undeclared legacy code-page support.");

    var utf32 = new UTF32Encoding(false, true, true);
    using var utf32RazerStream = new MemoryStream(EncodeWithPreamble("<Macro><Name>UTF32</Name><MacroEvents /></Macro>", utf32));
    var utf32RazerResult = new RazerMacroXmlImporter().ImportAsync(utf32RazerStream, "utf32.xml").GetAwaiter().GetResult();
    Assert(utf32RazerResult.Documents.Count == 0 && utf32RazerResult.Diagnostics.Any(item => item.Code == "RAZER_XML_INVALID"), "Razer XML must reject UTF-32.");
}

static void FormatDiagnosticsSanitizeSourcePaths()
{
    const string privateDirectory = @"C:\Private\Macros";

    using var razerStream = new MemoryStream(Encoding.UTF8.GetBytes("<not-macro />"));
    var razer = new RazerMacroXmlImporter().ImportAsync(razerStream, privateDirectory + @"\secret.xml").GetAwaiter().GetResult();
    Assert(razer.Diagnostics.All(item => item.SourceContext is null || item.SourceContext == "secret.xml"), "Razer diagnostic leaked an absolute source path.");

    const string brokenPackage = "{\"macros\":[{\"name\":\"Broken\",\"payload\":\"not-base64\"}]}";
    using var synapseStream = new MemoryStream(Encoding.UTF8.GetBytes(brokenPackage));
    var synapse = new Synapse4Importer().ImportAsync(synapseStream, privateDirectory + @"\secret.synapse4").GetAwaiter().GetResult();
    Assert(synapse.Diagnostics.All(item => item.SourceContext is null || item.SourceContext == "secret.synapse4"), "Synapse diagnostic leaked an absolute source path.");

    using var settingsStream = new MemoryStream(Encoding.UTF8.GetBytes("<not-settings />"));
    var settings = new XmbcSettingsImporter().ImportAsync(settingsStream, privateDirectory + @"\secret-settings.xml").GetAwaiter().GetResult();
    Assert(settings.Diagnostics.All(item => item.SourceContext is null || item.SourceContext == "secret-settings.xml"), "XMBC settings diagnostic leaked an absolute source path.");

    using var textStream = new MemoryStream([0xFF]);
    var text = new XmbcMacroTextImporter().ImportAsync(textStream, privateDirectory + @"\secret.txt").GetAwaiter().GetResult();
    Assert(text.Diagnostics.All(item => item.SourceContext is null || item.SourceContext == "secret.txt"), "XMBC text diagnostic leaked an absolute source path.");
}

static void ImportersStopAtConfiguredEventLimit()
{
    var limits = new MacroLimits(MaximumEventsPerMacro: 3);
    const string razerXml = "<Macro><Name>Limited XML</Name><MacroEvents>" +
                            "<MacroEvent><Type>0</Type><Number>0.001</Number></MacroEvent>" +
                            "<MacroEvent><Type>0</Type><Number>0.001</Number></MacroEvent>" +
                            "<MacroEvent><Type>0</Type><Number>0.001</Number></MacroEvent>" +
                            "<MacroEvent><Type>0</Type><Number>0.001</Number></MacroEvent>" +
                            "</MacroEvents></Macro>";
    using var razerStream = new MemoryStream(Encoding.UTF8.GetBytes(razerXml));
    var razer = new RazerMacroXmlImporter(limits).ImportAsync(razerStream, "limited.xml").GetAwaiter().GetResult();
    Assert(razer.Documents.Single().Events.Count == 3 && razer.Diagnostics.Any(item => item.Code == "IMPORT_EVENT_LIMIT"), "Razer XML did not stop at the configured event limit.");
    Assert(razer.Documents.Single().Events[^1] is UnknownMacroEvent && new MacroValidator().Validate(razer.Documents.Single()).HasErrors, "Truncated Razer XML must remain blocked from export.");

    const string inner = "{\"guid\":\"aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb\",\"macroEvents\":[" +
                         "{\"Type\":0,\"Number\":0.001},{\"Type\":0,\"Number\":0.001}," +
                         "{\"Type\":0,\"Number\":0.001},{\"Type\":0,\"Number\":0.001}]}";
    var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(inner));
    var package = $"{{\"macros\":[{{\"name\":\"Limited Synapse\",\"payload\":\"{payload}\"}}]}}";
    using var synapseStream = new MemoryStream(Encoding.UTF8.GetBytes(package));
    var synapse = new Synapse4Importer(limits).ImportAsync(synapseStream, "limited.synapse4").GetAwaiter().GetResult();
    Assert(synapse.Documents.Single().Events.Count == 3 && synapse.Diagnostics.Any(item => item.Code == "IMPORT_EVENT_LIMIT"), "Synapse did not stop at the configured event limit.");
    Assert(synapse.Documents.Single().Events[^1] is UnknownMacroEvent && new MacroValidator().Validate(synapse.Documents.Single()).HasErrors, "Truncated Synapse macro must remain blocked from export.");

    using var textStream = new MemoryStream(Encoding.UTF8.GetBytes("ab"));
    var text = new XmbcMacroTextImporter(limits).ImportAsync(textStream, "limited.txt").GetAwaiter().GetResult();
    Assert(text.Documents.Single().Events.Count == 3 && text.Diagnostics.Any(item => item.Code == "IMPORT_EVENT_LIMIT"), "XMBC text did not stop at the configured event limit.");
    Assert(text.Documents.Single().Events[^1] is UnknownMacroEvent && new MacroValidator().Validate(text.Documents.Single()).HasErrors, "Truncated XMBC text must remain blocked from export.");

    const string settingsXml = "<root><version major='2'/><Default><Left action='28' keys='ab'/></Default></root>";
    using var settingsStream = new MemoryStream(Encoding.UTF8.GetBytes(settingsXml));
    var settings = new XmbcSettingsImporter(limits).ImportAsync(settingsStream, "limited-settings.xml").GetAwaiter().GetResult();
    Assert(settings.Documents.Single().Events.Count == 3 && settings.Diagnostics.Any(item => item.Code == "IMPORT_EVENT_LIMIT"), "XMBC settings did not propagate the configured event limit.");
    Assert(settings.Documents.Single().Events[^1] is UnknownMacroEvent && new MacroValidator().Validate(settings.Documents.Single()).HasErrors, "Truncated XMBC settings macro must remain blocked from export.");
}

static void DefaultEventLimitHandlesLargeInput()
{
    var macroText = new string('a', 50_001);
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(macroText));
    var result = new XmbcMacroTextImporter().ImportAsync(stream, "large.txt").GetAwaiter().GetResult();
    var document = result.Documents.Single();

    Assert(document.Events.Count == 100_000, "Default importer event limit did not cap the returned model at 100,000 events.");
    Assert(document.Events[^1] is UnknownMacroEvent { SourceType: "import.event-limit" }, "Large-input truncation sentinel is absent.");
    Assert(result.Diagnostics.Count(item => item.Code == "IMPORT_EVENT_LIMIT") == 1, "Large input requires one event-limit diagnostic.");
    Assert(new MacroValidator().Validate(document).HasErrors, "A truncated large input must remain blocked from export.");
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

static void RazerXmlExportUsesSynapse4InputCodes()
{
    var document = CreateDocument(
        new KeyMacroEvent(0, 69, InputTransition.Down),
        new KeyMacroEvent(1, 69, InputTransition.Up),
        new KeyMacroEvent(2, 87, InputTransition.Down),
        new KeyMacroEvent(3, 87, InputTransition.Up),
        new MouseMacroEvent(4, MouseButton.Left, InputTransition.Down),
        new MouseMacroEvent(5, MouseButton.Left, InputTransition.Up),
        new MouseMacroEvent(6, MouseButton.Right, InputTransition.Down),
        new MouseMacroEvent(7, MouseButton.Right, InputTransition.Up));
    using var output = new MemoryStream();
    var diagnostics = new RazerMacroXmlExporter().ExportAsync(document, output).GetAwaiter().GetResult();

    Assert(diagnostics.All(item => item.Severity != DiagnosticSeverity.Error), "Synapse 4 code export should succeed.");
    var xml = Encoding.UTF8.GetString(output.ToArray());
    Assert(xml.Contains("<Makecode>18</Makecode>", StringComparison.Ordinal), "E must use scan code 18 instead of virtual-key code 69.");
    Assert(xml.Contains("<Makecode>17</Makecode>", StringComparison.Ordinal), "W must use scan code 17 instead of virtual-key code 87.");
    Assert(xml.Contains("<MouseButton>1</MouseButton>", StringComparison.Ordinal), "Left mouse must use Synapse 4 button code 1.");
    Assert(xml.Contains("<MouseButton>2</MouseButton>", StringComparison.Ordinal), "Right mouse must use Synapse 4 button code 2.");
    Assert(xml.Contains("<mmtSetting>0</mmtSetting>", StringComparison.Ordinal), "Synapse 4 action bar metadata is missing.");
    Assert(xml.Contains("<DelaySetting>0</DelaySetting>", StringComparison.Ordinal), "Synapse 4 delay metadata is missing.");
    Assert(xml.Contains("<Version>4</Version>", StringComparison.Ordinal), "Synapse 4 version metadata is missing.");
    Assert(xml.Contains("<MouseMoveType>none</MouseMoveType>", StringComparison.Ordinal), "Synapse 4 mouse movement metadata is missing.");
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

static void XmbcExtendedFixtureRoundTripsSemantically()
{
    var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "extended-tags.txt");
    using var input = File.OpenRead(fixturePath);
    var imported = new XmbcMacroTextImporter().ImportAsync(input, fixturePath).GetAwaiter().GetResult();
    var document = imported.Documents.Single();

    Assert(!imported.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error), "Extended XMBC fixture should import without errors.");
    Assert(document.Events.OfType<RandomDelayMacroEvent>().Any(), "Random delay is absent from the extended fixture.");
    Assert(document.Events.OfType<ScanCodeMacroEvent>().Count() == 2, "Scan-code press/release pair is absent.");
    Assert(document.Events.OfType<XmbcCommandMacroEvent>().Count() == 2, "XMBC commands were not preserved.");
    Assert(document.Events.OfType<KeyMacroEvent>().Any(item => item.VirtualKey == 0x87), "F24 is absent.");
    Assert(document.Events.OfType<MouseMacroEvent>().Any(item => item.Button == MouseButton.XButton1), "MB4 is absent.");
    Assert(document.Events.OfType<MouseMacroEvent>().Any(item => item.Button == MouseButton.WheelUp), "MWUP is absent.");
    Assert(!new MacroValidator().Validate(document).HasErrors, "Extended fixture should pass model validation.");

    using var output = new MemoryStream();
    var exportDiagnostics = new XmbcMacroTextExporter().ExportAsync(document, output).GetAwaiter().GetResult();
    var exportErrors = exportDiagnostics.Where(item => item.Severity == DiagnosticSeverity.Error).ToArray();
    Assert(exportErrors.Length == 0, $"Extended XMBC fixture should export: {string.Join(", ", exportErrors.Select(item => item.Code))}");

    output.Position = 0;
    var roundTrip = new XmbcMacroTextImporter().ImportAsync(output, "extended-roundtrip.txt").GetAwaiter().GetResult();
    Assert(EventSignatures(roundTrip.Documents.Single()).SequenceEqual(EventSignatures(document)), "Extended XMBC semantic round trip changed events.");
}

static void XmbcRejectsNonAdjacentAtomicWheelPairs()
{
    var document = CreateDocument(
        new MouseMacroEvent(0, MouseButton.WheelUp, InputTransition.Down),
        new DelayMacroEvent(1, 100),
        new MouseMacroEvent(2, MouseButton.WheelUp, InputTransition.Up));
    using var output = new MemoryStream();
    var diagnostics = new XmbcMacroTextExporter().ExportAsync(document, output).GetAwaiter().GetResult();

    Assert(diagnostics.Any(item => item.Code == "XMBC_EXPORT_ATOMIC_MOUSE_PAIR_REQUIRED"), "Non-adjacent wheel pair should be blocked.");
    Assert(output.Length == 0, "Blocked atomic wheel export must not write partial text.");
}

static void XmbcAtomicMouseActionsExportAllSupportedMarkers()
{
    var document = CreateDocument(
        new MouseMacroEvent(0, MouseButton.WheelUp, InputTransition.Down),
        new MouseMacroEvent(1, MouseButton.WheelUp, InputTransition.Up),
        new MouseMacroEvent(2, MouseButton.WheelDown, InputTransition.Down),
        new MouseMacroEvent(3, MouseButton.WheelDown, InputTransition.Up),
        new MouseMacroEvent(4, MouseButton.TiltLeft, InputTransition.Down),
        new MouseMacroEvent(5, MouseButton.TiltLeft, InputTransition.Up),
        new MouseMacroEvent(6, MouseButton.TiltRight, InputTransition.Down),
        new MouseMacroEvent(7, MouseButton.TiltRight, InputTransition.Up));
    using var output = new MemoryStream();
    var diagnostics = new XmbcMacroTextExporter().ExportAsync(document, output).GetAwaiter().GetResult();

    Assert(diagnostics.All(item => item.Severity != DiagnosticSeverity.Error), "Supported atomic mouse actions should export without errors.");
    Assert(Encoding.UTF8.GetString(output.ToArray()) == "{MWUP}{MWDN}{TILTL}{TILTR}", "Atomic mouse markers were not exported in order.");
}

static void XmbcRejectsMalformedAtomicMouseEvents()
{
    var document = CreateDocument(
        new MouseMacroEvent(0, MouseButton.WheelUp, InputTransition.Down),
        new MouseMacroEvent(1, MouseButton.WheelDown, InputTransition.Up),
        new MouseMacroEvent(2, MouseButton.TiltLeft, InputTransition.Up));
    using var output = new MemoryStream();
    var diagnostics = new XmbcMacroTextExporter().ExportAsync(document, output).GetAwaiter().GetResult();

    Assert(
        diagnostics.Count(item => item.Code == "XMBC_EXPORT_ATOMIC_MOUSE_PAIR_REQUIRED") >= 2,
        "Mismatched and isolated atomic mouse events should both be diagnosed.");
    Assert(output.Length == 0, "Malformed atomic mouse events must not produce partial output.");
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

static void ApplicationImportServicePreservesBomEncodedInputs()
{
    var tempDirectory = CreateTemporaryDirectory();
    try
    {
        var encoding = new UnicodeEncoding(true, true, true);
        const string razerXml = "<Macro><Name>Service Razer</Name><MacroEvents><MacroEvent><Type>0</Type><Number>0.001</Number></MacroEvent></MacroEvents></Macro>";
        const string settingsXml = "<root><version major='2'/><Default><Left action='28' keys='a'/></Default></root>";
        const string macroText = "a{WAITMS:1}";
        const string inner = "{\"guid\":\"99999999-9999-9999-9999-999999999999\",\"macroEvents\":[]}";
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(inner));
        var synapse = $"{{\"macros\":[{{\"name\":\"Service Synapse\",\"payload\":\"{payload}\"}}]}}";
        var files = new Dictionary<string, string>
        {
            ["razer.xml"] = razerXml,
            ["settings.xml"] = settingsXml,
            ["macro.txt"] = macroText,
            ["package.synapse4"] = synapse,
        };
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, content) in files)
        {
            var path = Path.Combine(tempDirectory, name);
            File.WriteAllBytes(path, EncodeWithPreamble(content, encoding));
            hashes[path] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }

        var result = new MacroImportService(MacroFormatRegistry.CreateDefault()).ImportAsync([tempDirectory]).GetAwaiter().GetResult();
        Assert(result.ProcessedFiles.Count == 4, "BOM-encoded service import did not process every file.");
        Assert(result.Documents.Count == 4, "BOM-encoded service import returned the wrong document count.");
        Assert(!result.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error), "BOM-encoded service import produced an error.");
        foreach (var (path, hash) in hashes)
        {
            Assert(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == hash, $"Import modified BOM-encoded input {Path.GetFileName(path)}.");
        }
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

static void CancelledImportReleasesInputHandleAndSupportsRetry()
{
    var tempDirectory = CreateTemporaryDirectory();
    try
    {
        var filePath = Path.Combine(tempDirectory, "cancel-import.txt");
        File.WriteAllText(filePath, "controlled import input", Encoding.UTF8);
        var originalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(filePath)));
        var importer = new CancellableThenSuccessfulImporter();
        var registry = new MacroFormatRegistry([importer], Array.Empty<IMacroExporter>());
        var service = new MacroImportService(registry);
        using var cancellation = new CancellationTokenSource();

        var importTask = service.ImportAsync([filePath], cancellationToken: cancellation.Token);
        Assert(importer.Started.Task.Wait(TimeSpan.FromSeconds(5)), "Controlled importer did not reach its cancellation point.");
        cancellation.Cancel();
        try
        {
            importTask.GetAwaiter().GetResult();
            throw new InvalidOperationException("Cancelled import completed without cancellation.");
        }
        catch (OperationCanceledException)
        {
        }

        using (File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
        }

        Assert(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(filePath))) == originalHash,
            "Cancelled import modified the input file.");
        var retry = service.ImportAsync([filePath]).GetAwaiter().GetResult();
        Assert(retry.Documents.Count == 1 && retry.Documents[0].Name == "取消后重试", "Import service was not reusable after cancellation.");
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

static void CancelledWorkspaceExportPreservesTargetAndSupportsRetry()
{
    var tempDirectory = CreateTemporaryDirectory();
    try
    {
        var targetPath = Path.Combine(tempDirectory, "cancel-export.xml");
        File.WriteAllText(targetPath, "original target", Encoding.UTF8);
        var exporter = new CancellableThenSuccessfulExporter();
        var registry = new MacroFormatRegistry(Array.Empty<IMacroImporter>(), [exporter]);
        var viewModel = new WorkspaceViewModel(
            new MacroImportService(registry),
            new SafeExportService(registry),
            new NestedMacroResolver(),
            new MacroValidator());
        var document = CreateDocument(
            new KeyMacroEvent(0, 65, InputTransition.Down),
            new KeyMacroEvent(1, 65, InputTransition.Up));
        viewModel.Macros.Add(document);
        viewModel.SelectedMacro = document;

        var exportTask = viewModel.ExportAsync(targetPath, overwrite: true);
        Assert(exporter.Started.Task.Wait(TimeSpan.FromSeconds(5)), "Controlled exporter did not reach its cancellation point.");
        Assert(viewModel.IsBusy && viewModel.CanCancel, "Workspace did not expose its active export state.");
        viewModel.Cancel();
        var cancelled = exportTask.GetAwaiter().GetResult();

        Assert(!cancelled.Succeeded, "Cancelled export unexpectedly succeeded.");
        Assert(cancelled.Diagnostics.Any(item => item.Code == "WORKSPACE_EXPORT_CANCELLED"), "Workspace cancellation diagnostic is absent.");
        Assert(!viewModel.IsBusy && !viewModel.CanCancel && viewModel.CanExport, "Workspace did not recover after export cancellation.");
        Assert(File.ReadAllText(targetPath, Encoding.UTF8) == "original target", "Cancelled export changed the existing target.");
        Assert(Directory.EnumerateFiles(tempDirectory).Count() == 1, "Cancelled export left a temporary or backup file.");
        using (File.Open(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
        }

        var retry = viewModel.ExportAsync(targetPath, overwrite: true).GetAwaiter().GetResult();
        Assert(retry.Succeeded, "Workspace export was not reusable after cancellation.");
        Assert(File.ReadAllText(targetPath, Encoding.UTF8) == "complete target", "Retry export did not atomically replace the target.");
        Assert(Directory.EnumerateFiles(tempDirectory).Count() == 1, "Retry export left a temporary or backup file.");
    }
    finally
    {
        Directory.Delete(tempDirectory, true);
    }
}

static void LockedExportTargetIsPreservedAndSupportsRetry()
{
    var tempDirectory = CreateTemporaryDirectory();
    try
    {
        var targetPath = Path.Combine(tempDirectory, "locked.xml");
        File.WriteAllText(targetPath, "locked original", Encoding.UTF8);
        var document = CreateDocument(
            new KeyMacroEvent(0, 65, InputTransition.Down),
            new KeyMacroEvent(1, 65, InputTransition.Up));
        var service = new SafeExportService(MacroFormatRegistry.CreateDefault());

        ExportResult blocked;
        using (File.Open(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            blocked = service.ExportAsync(document, "razer.macro.xml", targetPath, overwrite: true).GetAwaiter().GetResult();
        }

        Assert(!blocked.Succeeded, "Export to an exclusively locked target unexpectedly succeeded.");
        Assert(blocked.Diagnostics.Any(item => item.Code == "EXPORT_FILE_ERROR"), "Locked-target file diagnostic is absent.");
        Assert(File.ReadAllText(targetPath, Encoding.UTF8) == "locked original", "Locked-target failure changed the original target.");
        Assert(Directory.EnumerateFiles(tempDirectory).Count() == 1, "Locked-target failure left a temporary or backup file.");

        var retry = service.ExportAsync(document, "razer.macro.xml", targetPath, overwrite: true).GetAwaiter().GetResult();
        Assert(retry.Succeeded, "Export did not recover after the target lock was released.");
        Assert(File.ReadAllText(targetPath, Encoding.UTF8) != "locked original", "Retry did not replace the target.");
        Assert(Directory.EnumerateFiles(tempDirectory).Count() == 1, "Locked-target retry left a temporary or backup file.");
    }
    finally
    {
        Directory.Delete(tempDirectory, true);
    }
}

static void WriteFailureCleansPartialExportAndPreservesTarget()
{
    var tempDirectory = CreateTemporaryDirectory();
    try
    {
        var targetPath = Path.Combine(tempDirectory, "write-failure.xml");
        File.WriteAllText(targetPath, "write failure original", Encoding.UTF8);
        var registry = new MacroFormatRegistry(Array.Empty<IMacroImporter>(), [new PartialWriteFailureExporter()]);
        var service = new SafeExportService(registry);
        var document = CreateDocument(new DelayMacroEvent(0, 1));

        var result = service.ExportAsync(document, "razer.macro.xml", targetPath, overwrite: true).GetAwaiter().GetResult();

        Assert(!result.Succeeded, "Controlled write failure unexpectedly succeeded.");
        Assert(result.Diagnostics.Any(item => item.Code == "EXPORT_FILE_ERROR"), "Controlled write failure diagnostic is absent.");
        Assert(File.ReadAllText(targetPath, Encoding.UTF8) == "write failure original", "Write failure changed the original target.");
        Assert(Directory.EnumerateFiles(tempDirectory).Count() == 1, "Write failure left a partial temporary or backup file.");
        using (File.Open(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
        }
    }
    finally
    {
        Directory.Delete(tempDirectory, true);
    }
}

static void ExportRejectsFileUsedAsTargetDirectory()
{
    var tempDirectory = CreateTemporaryDirectory();
    try
    {
        var fileAsDirectory = Path.Combine(tempDirectory, "not-a-directory");
        File.WriteAllText(fileAsDirectory, "blocking file", Encoding.UTF8);
        var targetPath = Path.Combine(fileAsDirectory, "output.xml");
        var service = new SafeExportService(MacroFormatRegistry.CreateDefault());
        var document = CreateDocument(new DelayMacroEvent(0, 1));

        var result = service.ExportAsync(document, "razer.macro.xml", targetPath).GetAwaiter().GetResult();

        Assert(!result.Succeeded, "Export unexpectedly treated a file as a target directory.");
        Assert(result.Diagnostics.Any(item => item.Code == "EXPORT_FILE_ERROR"), "Invalid target-directory diagnostic is absent.");
        Assert(File.ReadAllText(fileAsDirectory, Encoding.UTF8) == "blocking file", "Invalid target-directory export changed the blocking file.");
        Assert(Directory.EnumerateFileSystemEntries(tempDirectory).Count() == 1, "Invalid target-directory export created an artifact.");
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

static void WorkspaceSuppressesDuplicateUnknownEventDiagnostics()
{
    const string xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Macro>
          <Name>重复诊断</Name>
          <MacroEvents>
            <MacroEvent><Type>999</Type><Payload>unknown</Payload></MacroEvent>
          </MacroEvents>
        </Macro>
        """;
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
    var imported = new RazerMacroXmlImporter().ImportAsync(stream, "duplicate.xml").GetAwaiter().GetResult();
    var viewModel = WorkspaceViewModel.CreateDefault();
    foreach (var document in imported.Documents)
    {
        viewModel.Macros.Add(document);
    }
    foreach (var diagnostic in imported.Diagnostics)
    {
        viewModel.Diagnostics.Add(diagnostic);
    }

    viewModel.SelectedMacro = viewModel.Macros.Single();

    Assert(viewModel.Diagnostics.Any(item => item.Code == "RAZER_EVENT_UNKNOWN"), "Root unknown-event diagnostic is absent.");
    Assert(!viewModel.Diagnostics.Any(item => item.Code == "UNKNOWN_EVENT"), "Workspace added a duplicate generic unknown-event diagnostic.");
    Assert(viewModel.Diagnostics.Count(item => item.Severity == DiagnosticSeverity.Error) == 1, "Unknown event should produce one actionable error.");
}

static void WorkspaceSearchesTimelineEventsAndCyclesSelection()
{
    var document = CreateNamedDocument(
        "搜索基线",
        new KeyMacroEvent(0, 65, InputTransition.Down),
        new DelayMacroEvent(1, 25),
        new KeyMacroEvent(2, 65, InputTransition.Up),
        new MouseMacroEvent(3, MouseButton.Right, InputTransition.Down));
    var viewModel = WorkspaceViewModel.CreateDefault();
    viewModel.Macros.Add(document);
    viewModel.SelectedMacro = document;

    Assert(viewModel.EventSearchResultText == "未搜索", "Empty search should expose the idle summary.");
    viewModel.EventSearchText = "键盘";
    Assert(viewModel.EventSearchResultText == "0 / 2", "Search result count is incorrect before navigation.");
    Assert(viewModel.CanFindPreviousEvent && viewModel.CanFindNextEvent, "Matching search should enable both navigation directions.");
    Assert(!viewModel.CanUndo, "Searching must not enter the edit history.");

    Assert(viewModel.FindNextEvent(), "First next-result navigation should succeed.");
    Assert(viewModel.SelectedEvent is { DisplayIndex: 0, Type: "键盘" }, "Next-result navigation did not select the first match.");
    Assert(viewModel.EventSearchResultText == "1 / 2", "Current search position is incorrect after first navigation.");
    Assert(viewModel.FindNextEvent(), "Second next-result navigation should succeed.");
    Assert(viewModel.SelectedEvent is { DisplayIndex: 2, Type: "键盘" }, "Next-result navigation did not select the second match.");
    Assert(viewModel.FindNextEvent() && viewModel.SelectedEvent?.DisplayIndex == 0, "Next-result navigation did not wrap to the first match.");
    Assert(viewModel.FindPreviousEvent() && viewModel.SelectedEvent?.DisplayIndex == 2, "Previous-result navigation did not wrap to the final match.");

    viewModel.EventSearchText = "vk 65";
    Assert(viewModel.EventSearchResultText == "2 / 2", "Search should be case-insensitive and preserve a matching selection.");
    viewModel.EventSearchText = "1";
    Assert(viewModel.EventSearchResultText == "0 / 1", "Sequence search should match the visible sequence field.");
    viewModel.EventSearchText = "不存在";
    Assert(viewModel.EventSearchResultText == "0 / 0", "No-result summary is incorrect.");
    Assert(!viewModel.CanFindNextEvent && !viewModel.FindNextEvent(), "No-result search must reject navigation.");

    viewModel.EventSearchText = new string('x', 200);
    Assert(viewModel.EventSearchText.Length == 128, "Programmatic search text must respect the 128-character limit.");

    viewModel.EventSearchText = "延时";
    Assert(viewModel.FindNextEvent() && viewModel.SelectedEvent?.Event is DelayMacroEvent, "Delay search did not select its only match.");
    Assert(viewModel.DeleteSelectedEvent(), "Deleting the selected search result should succeed.");
    Assert(viewModel.EventSearchResultText == "0 / 0", "Search results were not rebuilt after timeline editing.");
    Assert(viewModel.Undo(), "Undo after deleting a search result should succeed.");
    Assert(viewModel.EventSearchResultText == "1 / 1", "Search results were not rebuilt after undo.");
}

static void WorkspaceSearchesMaximumEventCountWithinResourceBudget()
{
    var events = new MacroEvent[100_000];
    for (var index = 0; index < events.Length; index++)
    {
        events[index] = new DelayMacroEvent(index, index);
    }

    var document = CreateNamedDocument("十万事件搜索", events);
    var viewModel = WorkspaceViewModel.CreateDefault();
    viewModel.Macros.Add(document);
    viewModel.SelectedMacro = document;
    Assert(viewModel.Events.Count == 100_000, "Maximum-size timeline did not expose all events.");

    viewModel.EventSearchText = "warmup-not-found";
    viewModel.EventSearchText = string.Empty;
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var stopwatch = Stopwatch.StartNew();
    viewModel.EventSearchText = "99999";
    stopwatch.Stop();
    var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

    Assert(viewModel.EventSearchResultText == "0 / 1", "Maximum-size search result count is incorrect.");
    Assert(viewModel.FindNextEvent(), "Maximum-size search did not allow result navigation.");
    Assert(viewModel.SelectedEvent?.DisplayIndex == 99_999, "Maximum-size search selected the wrong event.");
    Assert(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Maximum-size search took {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
    Assert(allocatedBytes < 1_000_000, $"Maximum-size search allocated {allocatedBytes:N0} bytes.");
}

static void WorkspaceRepeatedSearchEditingRemainsStable()
{
    const int eventCount = 5_000;
    const int iterations = 100;
    var events = new MacroEvent[eventCount];
    for (var index = 0; index < events.Length; index++)
    {
        events[index] = new DelayMacroEvent(index, index);
    }

    var original = CreateNamedDocument("重复搜索编辑", events);
    var viewModel = WorkspaceViewModel.CreateDefault();
    viewModel.Macros.Add(original);
    viewModel.SelectedMacro = original;
    viewModel.EventSearchText = (eventCount - 1).ToString();
    Assert(viewModel.FindNextEvent(), "Initial stress-test search did not find the final event.");
    Assert(viewModel.SelectedEvent?.DisplayIndex == eventCount - 1, "Initial stress-test selection is incorrect.");

    var retainedBefore = GC.GetTotalMemory(forceFullCollection: true);
    var stopwatch = Stopwatch.StartNew();
    for (var iteration = 0; iteration < iterations; iteration++)
    {
        Assert(viewModel.DeleteSelectedEvent(), $"Stress-test delete failed at iteration {iteration}.");
        Assert(viewModel.Events.Count == eventCount - 1, "Stress-test delete produced the wrong event count.");
        Assert(viewModel.EventSearchResultText == "0 / 0", "Deleted search result remained in the search index.");

        Assert(viewModel.Undo(), $"Stress-test undo failed at iteration {iteration}.");
        Assert(viewModel.Events.Count == eventCount, "Stress-test undo did not restore the event count.");
        Assert(viewModel.SelectedEvent?.DisplayIndex == eventCount - 1, "Stress-test undo did not restore selection.");
        Assert(viewModel.EventSearchResultText == "1 / 1", "Stress-test undo did not rebuild the search index.");

        Assert(viewModel.Redo(), $"Stress-test redo failed at iteration {iteration}.");
        Assert(viewModel.Events.Count == eventCount - 1, "Stress-test redo produced the wrong event count.");
        Assert(viewModel.EventSearchResultText == "0 / 0", "Stress-test redo retained a stale search result.");

        Assert(viewModel.Undo(), $"Stress-test final undo failed at iteration {iteration}.");
        Assert(viewModel.Events.Count == eventCount, "Stress-test final undo did not restore the event count.");
        Assert(viewModel.SelectedEvent?.DisplayIndex == eventCount - 1, "Stress-test final undo did not restore selection.");
        Assert(viewModel.EventSearchResultText == "1 / 1", "Stress-test final undo did not restore the search result.");
    }

    stopwatch.Stop();
    var retainedAfter = GC.GetTotalMemory(forceFullCollection: true);
    var retainedGrowth = Math.Max(0, retainedAfter - retainedBefore);
    Assert(ReferenceEquals(viewModel.SelectedMacro, original), "Repeated undo did not restore the original immutable macro snapshot.");
    Assert(viewModel.Diagnostics.Count == 0, "Repeated valid edits accumulated unexpected diagnostics.");
    Assert(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Repeated search editing took {stopwatch.Elapsed.TotalSeconds:F1} seconds.");
    Assert(retainedGrowth < 64L * 1024 * 1024, $"Repeated search editing retained {retainedGrowth:N0} bytes after full GC.");
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

static void WorkspaceFiltersAndGroupsDiagnostics()
{
    var viewModel = WorkspaceViewModel.CreateDefault();
    viewModel.Diagnostics.Add(new ConversionDiagnostic(
        "FIRST_ERROR",
        DiagnosticSeverity.Error,
        "First error.",
        SourceContext: "宏 A"));
    viewModel.Diagnostics.Add(new ConversionDiagnostic(
        "FIRST_WARNING",
        DiagnosticSeverity.Warning,
        "First warning.",
        SourceContext: "宏 A"));
    viewModel.Diagnostics.Add(new ConversionDiagnostic(
        "SECOND_ERROR",
        DiagnosticSeverity.Error,
        "Second error.",
        SourceContext: "文件 B.xml"));

    Assert(viewModel.DiagnosticGroups.Count == 2, "Diagnostics should be grouped by source context.");
    Assert(viewModel.FilteredDiagnosticCount == 3, "All diagnostics should be visible initially.");

    viewModel.SelectedDiagnosticSeverity = viewModel.DiagnosticSeverityOptions.Single(item => item.Severity == DiagnosticSeverity.Error);
    Assert(viewModel.FilteredDiagnosticCount == 2, "Severity filter did not retain only errors.");
    Assert(viewModel.DiagnosticGroups.All(group => group.Diagnostics.All(item => item.Severity == DiagnosticSeverity.Error)), "Filtered groups contain a non-error diagnostic.");

    viewModel.SelectedDiagnosticScope = viewModel.DiagnosticScopes.Single(item => item.SourceContext == "宏 A");
    Assert(viewModel.FilteredDiagnosticCount == 1, "Source filter did not isolate the selected macro.");
    Assert(viewModel.DiagnosticCountText == "显示 1 / 3 条", "Filtered diagnostic summary is incorrect.");

    viewModel.Diagnostics.Clear();
    Assert(!viewModel.HasFilteredDiagnostics, "Empty diagnostics should expose an empty state.");
    Assert(viewModel.SelectedDiagnosticScope.SourceContext is null, "Clearing diagnostics should reset the source filter.");
}

static void WorkspaceDelayEditingRevalidatesAndSupportsHistory()
{
    var original = CreateNamedDocument(
        "延时修复",
        new KeyMacroEvent(0, 65, InputTransition.Down),
        new DelayMacroEvent(1, -1),
        new KeyMacroEvent(2, 65, InputTransition.Up));
    var viewModel = WorkspaceViewModel.CreateDefault();
    viewModel.Macros.Add(original);
    viewModel.SelectedMacro = original;

    Assert(!viewModel.CanExport, "Negative delay should block export before editing.");
    Assert(viewModel.Diagnostics.Any(item => item.Code == "DELAY_NEGATIVE"), "Initial delay diagnostic is missing.");

    viewModel.SelectedEvent = viewModel.Events.Single(item => item.IsFixedDelay);
    viewModel.DelayMillisecondsText = "25";
    Assert(viewModel.UpdateSelectedDelay(), "Selected delay edit should succeed.");
    Assert(
        original.Events.OfType<DelayMacroEvent>().Single().Milliseconds == -1,
        "Editing must not mutate the imported document instance.");
    Assert(
        viewModel.SelectedMacro!.Events.OfType<DelayMacroEvent>().Single().Milliseconds == 25,
        "Edited delay was not written to the replacement document.");
    Assert(viewModel.CanExport, "Fixing the negative delay should immediately re-enable export.");
    Assert(!viewModel.Diagnostics.Any(item => item.Code == "DELAY_NEGATIVE"), "Resolved delay diagnostic remained stale.");
    Assert(viewModel.CanUndo && !viewModel.CanRedo, "Successful edit did not update history state.");

    Assert(viewModel.Undo(), "Undo should restore the invalid delay snapshot.");
    Assert(
        viewModel.SelectedMacro!.Events.OfType<DelayMacroEvent>().Single().Milliseconds == -1,
        "Undo did not restore the previous delay.");
    Assert(!viewModel.CanExport, "Undo should immediately restore blocking validation.");
    Assert(viewModel.Diagnostics.Any(item => item.Code == "DELAY_NEGATIVE"), "Undo did not restore its validation diagnostic.");
    Assert(viewModel.CanRedo, "Undo did not enable redo.");

    Assert(viewModel.Redo(), "Redo should restore the repaired delay snapshot.");
    Assert(
        viewModel.SelectedMacro!.Events.OfType<DelayMacroEvent>().Single().Milliseconds == 25,
        "Redo did not restore the edited delay.");
    Assert(viewModel.CanExport, "Redo should immediately restore valid export state.");
    Assert(!viewModel.Diagnostics.Any(item => item.Code == "DELAY_NEGATIVE"), "Redo left a stale delay diagnostic.");
}

static void WorkspaceDelayScalingRoundsFixedAndRandomDelays()
{
    var original = CreateNamedDocument(
        "延时缩放",
        new KeyMacroEvent(0, 65, InputTransition.Down),
        new DelayMacroEvent(1, 3),
        new RandomDelayMacroEvent(2, 5, 7),
        new KeyMacroEvent(3, 65, InputTransition.Up));
    var viewModel = WorkspaceViewModel.CreateDefault();
    viewModel.Macros.Add(original);
    viewModel.SelectedMacro = original;
    viewModel.SelectedEvent = viewModel.Events.Single(item => item.IsFixedDelay);
    viewModel.DelayScalePercentText = "50";

    Assert(viewModel.ScaleAllDelays(), "Delay scaling should succeed.");
    var scaled = viewModel.SelectedMacro ?? throw new InvalidOperationException("Scaled macro selection is missing.");
    Assert(scaled.Events.OfType<DelayMacroEvent>().Single().Milliseconds == 2, "Fixed delay must round midpoint away from zero.");
    var random = scaled.Events.OfType<RandomDelayMacroEvent>().Single();
    Assert(random.MinimumMilliseconds == 3 && random.MaximumMilliseconds == 4, "Random delay bounds were not scaled consistently.");
    Assert(viewModel.SelectedEvent?.EventIndex == 1, "Scaling should preserve the selected event position.");

    Assert(viewModel.Undo(), "Scaled delays should be undoable.");
    Assert(viewModel.SelectedMacro!.Events.OfType<DelayMacroEvent>().Single().Milliseconds == 3, "Undo did not restore fixed delay.");
    var restoredRandom = viewModel.SelectedMacro.Events.OfType<RandomDelayMacroEvent>().Single();
    Assert(restoredRandom.MinimumMilliseconds == 5 && restoredRandom.MaximumMilliseconds == 7, "Undo did not restore random delay bounds.");

    viewModel.DelayMillisecondsText = "9";
    Assert(viewModel.UpdateSelectedDelay(), "A divergent edit after undo should succeed.");
    Assert(!viewModel.CanRedo, "A divergent edit must clear the redo history.");
}

static void WorkspaceDelayScalingRejectsOverflowAtomically()
{
    var original = CreateNamedDocument("溢出缩放", new DelayMacroEvent(0, long.MaxValue));
    var viewModel = WorkspaceViewModel.CreateDefault();
    viewModel.Macros.Add(original);
    viewModel.SelectedMacro = original;
    viewModel.DelayScalePercentText = "200";

    Assert(!viewModel.ScaleAllDelays(), "Overflowing delay scale must be rejected.");
    Assert(
        ReferenceEquals(viewModel.SelectedMacro, original) &&
        viewModel.SelectedMacro.Events.OfType<DelayMacroEvent>().Single().Milliseconds == long.MaxValue,
        "Rejected scaling must leave the macro snapshot unchanged.");
    Assert(!viewModel.CanUndo && !viewModel.CanRedo, "Rejected scaling must not create history entries.");

    var invalidRandom = CreateNamedDocument("无效随机延时", new RandomDelayMacroEvent(0, 10, 5));
    var invalidRandomViewModel = WorkspaceViewModel.CreateDefault();
    invalidRandomViewModel.Macros.Add(invalidRandom);
    invalidRandomViewModel.SelectedMacro = invalidRandom;
    invalidRandomViewModel.DelayScalePercentText = "50";
    Assert(!invalidRandomViewModel.ScaleAllDelays(), "An inverted random delay range must be rejected.");
    Assert(ReferenceEquals(invalidRandomViewModel.SelectedMacro, invalidRandom), "Rejected random-delay scaling changed the macro snapshot.");
    Assert(!invalidRandomViewModel.CanUndo, "Rejected random-delay scaling created an undo entry.");
}

static void WorkspaceEditHistoryIsBounded()
{
    var original = CreateNamedDocument("历史上限", new DelayMacroEvent(0, 0));
    var viewModel = WorkspaceViewModel.CreateDefault();
    viewModel.Macros.Add(original);
    viewModel.SelectedMacro = original;
    viewModel.SelectedEvent = viewModel.Events.Single();

    for (var milliseconds = 1; milliseconds <= 25; milliseconds++)
    {
        viewModel.DelayMillisecondsText = milliseconds.ToString();
        Assert(viewModel.UpdateSelectedDelay(), $"Edit {milliseconds} should succeed.");
    }

    var undoCount = 0;
    while (viewModel.Undo())
    {
        undoCount++;
    }

    Assert(undoCount == 20, $"History should retain exactly 20 edits, actual {undoCount}.");
    Assert(
        viewModel.SelectedMacro!.Events.OfType<DelayMacroEvent>().Single().Milliseconds == 5,
        "Bounded history restored entries older than the configured limit.");
}

static void WorkspaceEditingIsolatesDuplicateMacroIdentifiers()
{
    var duplicateId = Guid.NewGuid();
    var first = new MacroDocument(duplicateId, "重复一", [new DelayMacroEvent(0, -1)]);
    var second = new MacroDocument(duplicateId, "重复二", [new DelayMacroEvent(0, 2)]);
    var viewModel = WorkspaceViewModel.CreateDefault();
    viewModel.Macros.Add(first);
    viewModel.Macros.Add(second);
    viewModel.SelectedMacro = first;
    Assert(viewModel.Diagnostics.Any(item => item.Code == "DELAY_NEGATIVE"), "First duplicate-ID macro was not evaluated.");
    viewModel.SelectedMacro = second;
    viewModel.SelectedEvent = viewModel.Events.Single();
    viewModel.DelayMillisecondsText = "9";

    Assert(viewModel.UpdateSelectedDelay(), "Editing the second duplicate-ID macro should succeed.");
    Assert(ReferenceEquals(viewModel.Macros[0], first), "Editing replaced the wrong duplicate-ID macro.");
    Assert(viewModel.Macros[0].Events.OfType<DelayMacroEvent>().Single().Milliseconds == -1, "The first duplicate-ID macro was modified.");
    Assert(viewModel.Macros[1].Events.OfType<DelayMacroEvent>().Single().Milliseconds == 9, "The selected duplicate-ID macro was not updated.");
    Assert(viewModel.Diagnostics.Any(item => item.Code == "DELAY_NEGATIVE"), "Editing a duplicate-ID macro removed another macro's diagnostic.");
    Assert(viewModel.Undo(), "Undo should target the edited duplicate-ID macro.");
    Assert(ReferenceEquals(viewModel.Macros[0], first), "Undo replaced the wrong duplicate-ID macro.");
    Assert(ReferenceEquals(viewModel.Macros[1], second), "Undo did not restore the selected duplicate-ID snapshot.");
    Assert(viewModel.Redo(), "Redo should target the edited duplicate-ID macro.");
    Assert(viewModel.Macros[1].Events.OfType<DelayMacroEvent>().Single().Milliseconds == 9, "Redo did not restore the selected duplicate-ID edit.");
}

static void WorkspaceInsertsAndDeletesTimelineEvents()
{
    var original = CreateNamedDocument(
        "结构编辑",
        new KeyMacroEvent(10, 65, InputTransition.Down),
        new KeyMacroEvent(20, 65, InputTransition.Up));
    var viewModel = WorkspaceViewModel.CreateDefault();
    viewModel.Macros.Add(original);
    viewModel.SelectedMacro = original;
    viewModel.SelectedEvent = viewModel.Events[0];
    viewModel.NewDelayMillisecondsText = "15";

    Assert(viewModel.InsertDelayAfterSelection(), "Delay insertion should succeed.");
    Assert(original.Events.Count == 2 && original.Events[0].Sequence == 10, "Insertion mutated the imported document.");
    Assert(viewModel.SelectedMacro!.Events.Count == 3, "Inserted delay did not increase the event count.");
    Assert(
        viewModel.SelectedMacro.Events.Select(item => item.Sequence).SequenceEqual([0L, 1L, 2L]),
        "Structural editing did not normalize event sequences.");
    Assert(viewModel.SelectedMacro.Events[1] is DelayMacroEvent { Milliseconds: 15 }, "Delay was not inserted after the selected event.");
    Assert(viewModel.SelectedEvent is { DisplayIndex: 1, Event: DelayMacroEvent }, "Inserted delay was not selected.");
    Assert(viewModel.CanExport, "A balanced macro with an inserted delay should remain exportable.");

    Assert(viewModel.Undo(), "Inserted delay should be undoable.");
    Assert(viewModel.SelectedMacro!.Events.Count == 2, "Undo did not remove the inserted delay.");
    Assert(viewModel.SelectedEvent is { DisplayIndex: 0, Event: KeyMacroEvent }, "Undo did not restore the pre-insert selection.");
    Assert(viewModel.Redo(), "Inserted delay should be redoable.");
    Assert(viewModel.SelectedEvent?.Event is DelayMacroEvent, "Redo did not restore the inserted-event selection.");

    Assert(viewModel.DeleteSelectedEvent(), "Deleting the inserted delay should succeed.");
    Assert(viewModel.SelectedMacro!.Events.Count == 2, "Delete did not remove the selected event.");
    Assert(viewModel.SelectedEvent is { DisplayIndex: 1, Event: KeyMacroEvent }, "Delete did not select the next event.");
    Assert(viewModel.Undo(), "Deleted event should be undoable.");
    Assert(viewModel.SelectedEvent?.Event is DelayMacroEvent, "Undo did not restore the deleted-event selection.");

    var empty = CreateNamedDocument("空宏插入");
    var emptyViewModel = WorkspaceViewModel.CreateDefault();
    emptyViewModel.Macros.Add(empty);
    emptyViewModel.SelectedMacro = empty;
    emptyViewModel.NewDelayMillisecondsText = "8";
    Assert(emptyViewModel.InsertDelayAfterSelection(), "Insertion without a selection should append to an empty macro.");
    Assert(emptyViewModel.SelectedMacro!.Events.Single() is DelayMacroEvent { Sequence: 0, Milliseconds: 8 }, "Appended delay is incorrect.");
    Assert(emptyViewModel.SelectedEvent is { DisplayIndex: 0 }, "Appended delay was not selected.");
    Assert(emptyViewModel.DeleteSelectedEvent(), "Deleting the only event should succeed.");
    Assert(emptyViewModel.SelectedMacro.Events.Count == 0 && emptyViewModel.SelectedEvent is null, "Deleting the only event should leave an empty selection.");
}

static void WorkspaceCopiedEventsRevalidateSafety()
{
    var original = CreateNamedDocument(
        "复制验证",
        new KeyMacroEvent(0, 65, InputTransition.Down),
        new DelayMacroEvent(1, 10),
        new KeyMacroEvent(2, 65, InputTransition.Up));
    var viewModel = WorkspaceViewModel.CreateDefault();
    viewModel.Macros.Add(original);
    viewModel.SelectedMacro = original;
    viewModel.SelectedEvent = viewModel.Events[0];

    Assert(viewModel.CopySelectedEvent(), "Copying a selected event should succeed.");
    Assert(viewModel.SelectedMacro!.Events.Count == 4, "Copy did not add an event.");
    Assert(viewModel.SelectedEvent is { DisplayIndex: 1, Event: KeyMacroEvent }, "The copied event was not selected.");
    Assert(!viewModel.CanExport, "Duplicating a key-down event should immediately block export.");
    Assert(viewModel.Diagnostics.Any(item => item.Code == "KEY_DUPLICATE_DOWN"), "Copy did not produce the expected safety diagnostic.");

    Assert(viewModel.Undo(), "Copied event should be undoable.");
    Assert(viewModel.CanExport, "Undo should immediately restore a valid macro.");
    Assert(!viewModel.Diagnostics.Any(item => item.Code == "KEY_DUPLICATE_DOWN"), "Undo left a stale duplicate-key diagnostic.");
    Assert(viewModel.Redo(), "Copied event should be redoable.");
    Assert(!viewModel.CanExport, "Redo should restore the copied-event safety error.");
    Assert(viewModel.DeleteSelectedEvent(), "Deleting the copied event should succeed.");
    Assert(viewModel.CanExport, "Deleting the copied key-down event should restore validity.");
}

static void WorkspaceMovedEventsNormalizeOrderAndRevalidate()
{
    var original = CreateNamedDocument(
        "移动验证",
        new DelayMacroEvent(5, 10),
        new KeyMacroEvent(10, 65, InputTransition.Down),
        new KeyMacroEvent(20, 65, InputTransition.Up));
    var viewModel = WorkspaceViewModel.CreateDefault();
    viewModel.Macros.Add(original);
    viewModel.SelectedMacro = original;
    viewModel.SelectedEvent = viewModel.Events[2];

    Assert(viewModel.CanMoveEventUp && !viewModel.CanMoveEventDown, "Boundary move state is incorrect.");
    Assert(viewModel.MoveSelectedEventUp(), "Moving the key-up event upward should succeed.");
    Assert(
        viewModel.SelectedMacro!.Events.Select(item => item.Sequence).SequenceEqual([0L, 1L, 2L]),
        "Move did not normalize event sequences.");
    Assert(viewModel.SelectedEvent is { DisplayIndex: 1, Event: KeyMacroEvent { Transition: InputTransition.Up } }, "Moved event selection was not preserved.");
    Assert(!viewModel.CanExport, "Moving key-up before key-down should block export.");
    Assert(viewModel.Diagnostics.Any(item => item.Code == "KEY_UP_WITHOUT_DOWN"), "Move did not produce a key-order diagnostic.");

    Assert(viewModel.Undo(), "Moved event should be undoable.");
    Assert(viewModel.CanExport, "Undo should restore the valid key order.");
    Assert(viewModel.SelectedEvent is { DisplayIndex: 2, Event: KeyMacroEvent { Transition: InputTransition.Up } }, "Undo did not restore the pre-move selection.");
    Assert(!viewModel.MoveSelectedEventDown(), "Moving the last event downward must be rejected.");
    Assert(!viewModel.CanUndo, "Rejected boundary movement must not create history.");

    var identical = CreateNamedDocument(
        "相同事件移动",
        new DelayMacroEvent(0, 5),
        new DelayMacroEvent(1, 5));
    var identicalViewModel = WorkspaceViewModel.CreateDefault();
    identicalViewModel.Macros.Add(identical);
    identicalViewModel.SelectedMacro = identical;
    identicalViewModel.SelectedEvent = identicalViewModel.Events[1];
    Assert(!identicalViewModel.MoveSelectedEventUp(), "Swapping equal adjacent events should be treated as a no-op.");
    Assert(!identicalViewModel.CanUndo, "A semantic no-op must not create history.");
    Assert(identicalViewModel.StatusText == "编辑没有产生可见变化", "A semantic no-op should provide a visible status message.");
}

static void WorkspaceStructuralEditingRespectsEventLimit()
{
    var limits = new MacroLimits(MaximumEventsPerMacro: 2);
    var original = CreateNamedDocument(
        "编辑上限",
        new DelayMacroEvent(0, 1),
        new DelayMacroEvent(1, 2));
    var viewModel = WorkspaceViewModel.CreateDefault(limits);
    viewModel.Macros.Add(original);
    viewModel.SelectedMacro = original;
    viewModel.SelectedEvent = viewModel.Events[0];

    Assert(!viewModel.CanInsertDelay && !viewModel.CanCopyEvent, "Insert and copy should disable at the event limit.");
    Assert(!viewModel.InsertDelayAfterSelection(), "Insert must be rejected at the event limit.");
    Assert(!viewModel.CopySelectedEvent(), "Copy must be rejected at the event limit.");
    Assert(ReferenceEquals(viewModel.SelectedMacro, original), "Rejected limit operations changed the macro snapshot.");
    Assert(!viewModel.CanUndo, "Rejected limit operations created history entries.");

    Assert(viewModel.DeleteSelectedEvent(), "Delete should remain available at the event limit.");
    Assert(viewModel.CanInsertDelay && viewModel.CanCopyEvent, "Removing an event should reopen insert and copy capacity.");
    viewModel.NewDelayMillisecondsText = "3";
    Assert(viewModel.InsertDelayAfterSelection(), "Insert should succeed after capacity is available.");
    Assert(viewModel.SelectedMacro!.Events.Count == 2, "Insert after delete did not restore the configured event count.");
}

static void WorkspaceInsertsParameterizedKeyboardEvents()
{
    var original = CreateNamedDocument("键盘插入");
    var viewModel = WorkspaceViewModel.CreateDefault();
    viewModel.Macros.Add(original);
    viewModel.SelectedMacro = original;
    Assert(
        viewModel.InputTransitionOptions.Select(item => item.Transition).SequenceEqual([InputTransition.Down, InputTransition.Up]),
        "Keyboard transition options are incomplete or out of order.");

    viewModel.NewVirtualKeyText = "65";
    viewModel.NewKeyIsExtended = true;
    viewModel.SelectedKeyTransition = viewModel.InputTransitionOptions.Single(item => item.Transition == InputTransition.Down);
    Assert(viewModel.InsertKeyboardEvent(), "Parameterized key-down insertion should succeed.");
    Assert(original.Events.Count == 0, "Keyboard insertion mutated the imported document.");
    Assert(
        viewModel.SelectedMacro!.Events.Single() is KeyMacroEvent
        {
            Sequence: 0,
            VirtualKey: 65,
            Transition: InputTransition.Down,
            IsExtended: true,
        },
        "Inserted key-down parameters are incorrect.");
    Assert(!viewModel.CanExport, "An unpaired inserted key-down event should block export.");
    Assert(viewModel.Diagnostics.Any(item => item.Code == "KEY_NOT_RELEASED"), "Inserted key-down event did not produce a balance diagnostic.");

    viewModel.SelectedKeyTransition = viewModel.InputTransitionOptions.Single(item => item.Transition == InputTransition.Up);
    Assert(viewModel.InsertKeyboardEvent(), "Parameterized key-up insertion should succeed.");
    Assert(viewModel.SelectedMacro.Events.Count == 2, "Key-up insertion did not add a second event.");
    Assert(viewModel.SelectedMacro.Events[1] is KeyMacroEvent { Transition: InputTransition.Up, IsExtended: true }, "Inserted key-up parameters are incorrect.");
    Assert(viewModel.CanExport, "Balanced inserted keyboard events should restore export safety.");
    Assert(!viewModel.Diagnostics.Any(item => item.Code == "KEY_NOT_RELEASED"), "Balanced keyboard insertion left a stale diagnostic.");

    Assert(viewModel.Undo(), "Inserted key-up event should be undoable.");
    Assert(!viewModel.CanExport, "Undo should restore the unpaired key-down state.");
    Assert(viewModel.Redo(), "Inserted key-up event should be redoable.");
    Assert(viewModel.CanExport, "Redo should restore the balanced keyboard state.");
}

static void WorkspaceRejectsInvalidVirtualKeyInput()
{
    var original = CreateNamedDocument("非法键码");
    var viewModel = WorkspaceViewModel.CreateDefault();
    viewModel.Macros.Add(original);
    viewModel.SelectedMacro = original;

    foreach (var invalidValue in new[] { "-1", "256", "not-a-key" })
    {
        viewModel.NewVirtualKeyText = invalidValue;
        Assert(!viewModel.InsertKeyboardEvent(), $"Virtual-key input {invalidValue} must be rejected.");
        Assert(ReferenceEquals(viewModel.SelectedMacro, original), "Rejected virtual-key input changed the macro snapshot.");
        Assert(!viewModel.CanUndo, "Rejected virtual-key input created an undo entry.");
    }

    Assert(viewModel.StatusText.Contains("0–255", StringComparison.Ordinal), "Invalid virtual-key input did not produce a useful status message.");
}

static void WorkspaceInsertsParameterizedMouseEvents()
{
    var original = CreateNamedDocument("鼠标插入");
    var viewModel = WorkspaceViewModel.CreateDefault();
    viewModel.Macros.Add(original);
    viewModel.SelectedMacro = original;
    Assert(
        viewModel.MouseButtonOptions.Select(item => item.Button).SequenceEqual(Enum.GetValues<MouseButton>()),
        "Mouse button options do not cover the unified model.");

    viewModel.SelectedMouseButton = viewModel.MouseButtonOptions.Single(item => item.Button == MouseButton.Middle);
    viewModel.SelectedMouseTransition = viewModel.InputTransitionOptions.Single(item => item.Transition == InputTransition.Down);
    Assert(viewModel.InsertMouseEvent(), "Parameterized mouse-down insertion should succeed.");
    Assert(
        viewModel.SelectedMacro!.Events.Single() is MouseMacroEvent
        {
            Button: MouseButton.Middle,
            Transition: InputTransition.Down,
        },
        "Inserted mouse-down parameters are incorrect.");
    Assert(!viewModel.CanExport, "An unpaired mouse-down event should block export.");
    Assert(viewModel.Diagnostics.Any(item => item.Code == "MOUSE_NOT_RELEASED"), "Inserted mouse-down event did not produce a balance diagnostic.");

    viewModel.SelectedMouseTransition = viewModel.InputTransitionOptions.Single(item => item.Transition == InputTransition.Up);
    Assert(viewModel.InsertMouseEvent(), "Parameterized mouse-up insertion should succeed.");
    Assert(viewModel.CanExport, "Balanced inserted mouse events should restore export safety.");
    Assert(!viewModel.Diagnostics.Any(item => item.Code == "MOUSE_NOT_RELEASED"), "Balanced mouse insertion left a stale diagnostic.");

    viewModel.SelectedMouseButton = viewModel.MouseButtonOptions.Single(item => item.Button == MouseButton.WheelUp);
    viewModel.SelectedMouseTransition = viewModel.InputTransitionOptions.Single(item => item.Transition == InputTransition.Down);
    Assert(viewModel.InsertMouseEvent(), "Wheel-up down insertion should succeed.");
    viewModel.SelectedMouseTransition = viewModel.InputTransitionOptions.Single(item => item.Transition == InputTransition.Up);
    Assert(viewModel.InsertMouseEvent(), "Wheel-up release insertion should succeed.");
    using var output = new MemoryStream();
    var diagnostics = new XmbcMacroTextExporter()
        .ExportAsync(viewModel.SelectedMacro, output)
        .GetAwaiter()
        .GetResult();
    Assert(diagnostics.All(item => item.Severity != DiagnosticSeverity.Error), "Inserted mouse events failed XMBC export.");
    Assert(Encoding.UTF8.GetString(output.ToArray()).Contains("{MWUP}", StringComparison.Ordinal), "Inserted wheel pair did not export as an XMBC atomic action.");
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

static byte[] EncodeWithPreamble(string text, Encoding encoding)
{
    var preamble = encoding.GetPreamble();
    var content = encoding.GetBytes(text);
    var result = new byte[preamble.Length + content.Length];
    preamble.CopyTo(result, 0);
    content.CopyTo(result, preamble.Length);
    return result;
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

sealed class CancellableThenSuccessfulImporter : IMacroImporter
{
    private int invocationCount;

    public string FormatId => "test.cancellable-import";

    public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool CanImport(ReadOnlySpan<byte> header, string? fileName) => true;

    public async Task<MacroImportResult> ImportAsync(
        Stream input,
        string? sourceName,
        CancellationToken cancellationToken = default)
    {
        var invocation = Interlocked.Increment(ref invocationCount);
        var buffer = new byte[1];
        await input.ReadAsync(buffer, cancellationToken);
        if (invocation == 1)
        {
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return new MacroImportResult(
            [new MacroDocument(Guid.NewGuid(), "取消后重试", [new DelayMacroEvent(0, 1)])],
            []);
    }
}

sealed class CancellableThenSuccessfulExporter : IMacroExporter
{
    private int invocationCount;

    public string FormatId => "razer.macro.xml";

    public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<IReadOnlyList<ConversionDiagnostic>> ExportAsync(
        MacroDocument document,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        var invocation = Interlocked.Increment(ref invocationCount);
        var content = Encoding.UTF8.GetBytes(invocation == 1 ? "partial target" : "complete target");
        await output.WriteAsync(content, cancellationToken);
        if (invocation == 1)
        {
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return Array.Empty<ConversionDiagnostic>();
    }
}

sealed class PartialWriteFailureExporter : IMacroExporter
{
    public string FormatId => "razer.macro.xml";

    public async Task<IReadOnlyList<ConversionDiagnostic>> ExportAsync(
        MacroDocument document,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        await output.WriteAsync(Encoding.UTF8.GetBytes("partial output"), cancellationToken);
        throw new IOException("Controlled export write failure.");
    }
}
