using System.Text.Json;

namespace XMacroBridge.Application.Library;

public sealed class MacroLibrarySettingsService
{
    private const string TestRootEnvironmentVariable = "XMACROBRIDGE_TEST_LIBRARY_ROOT";
    private const string TestModeEnvironmentVariable = "XMACROBRIDGE_TEST_MODE";
    private readonly string settingsPath;

    public MacroLibrarySettingsService(string? settingsPath = null)
    {
        this.settingsPath = settingsPath ?? (IsTestMode
            ? Path.Combine(Path.GetTempPath(), "XMacroBridge-tests", Environment.ProcessId.ToString(), "settings.json")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XMacroBridge",
                "settings.json"));
    }

    public static string DefaultLibraryRootPath
    {
        get
        {
            var testRoot = Environment.GetEnvironmentVariable(TestRootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(testRoot))
            {
                return Path.GetFullPath(testRoot);
            }

            if (IsTestMode)
            {
                return Path.Combine(Path.GetTempPath(), "XMacroBridge-tests", Environment.ProcessId.ToString(), "library");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "XMacroBridge",
                "宏库");
        }
    }

    private static bool IsTestMode =>
        string.Equals(Environment.GetEnvironmentVariable(TestModeEnvironmentVariable), "1", StringComparison.Ordinal);

    public async Task<(MacroLibraryAppSettings Settings, string? Warning)> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(settingsPath))
        {
            return (new MacroLibraryAppSettings(DefaultLibraryRootPath), null);
        }

        try
        {
            await using var stream = new FileStream(
                settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var settings = await JsonSerializer.DeserializeAsync<MacroLibraryAppSettings>(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (settings is null || string.IsNullOrWhiteSpace(settings.LibraryRootPath))
            {
                throw new InvalidDataException("宏库设置缺少保存路径。");
            }

            return (settings with { LibraryRootPath = Path.GetFullPath(settings.LibraryRootPath) }, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            return (
                new MacroLibraryAppSettings(DefaultLibraryRootPath),
                $"宏库设置无法读取，已恢复默认路径：{exception.Message}");
        }
    }

    public async Task SaveAsync(MacroLibraryAppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var fullPath = Path.GetFullPath(settings.LibraryRootPath);
        var directory = Path.GetDirectoryName(settingsPath)
            ?? throw new InvalidOperationException("设置文件路径无效。");
        Directory.CreateDirectory(directory);

        var temporaryPath = settingsPath + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new MacroLibraryAppSettings(fullPath),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, settingsPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
