using XMacroBridge.Core.Conversion;
using XMacroBridge.Core.Models;
using XMacroBridge.Formats.Razer;
using XMacroBridge.Formats.Xmbc;
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
