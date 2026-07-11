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

static MacroDocument CreateDocument(params MacroEvent[] events) =>
    new(Guid.NewGuid(), "Test macro", events);

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
