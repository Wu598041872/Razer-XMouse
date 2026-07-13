using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XMacroBridge.Core.Text;

namespace XMacroBridge.Application.Library;

public sealed class MacroLibraryService
{
    private const string ManagementDirectoryName = ".xmacrobridge";
    private const string MetadataFileName = "metadata.json";
    private const string TrashDirectoryName = "trash";
    private const int MaximumItems = 10_000;
    private const int MaximumRecentItems = 20;
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    public async Task<MacroLibrarySnapshot> ScanAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        var root = NormalizeRoot(rootPath);
        if (!Directory.Exists(root))
        {
            return new MacroLibrarySnapshot([], [], [], []);
        }

        var warnings = new List<string>();
        var metadata = await LoadMetadataAsync(root, warnings, cancellationToken).ConfigureAwait(false);
        var items = new List<MacroLibraryItem>();
        var groups = new List<MacroLibraryGroup>();

        foreach (var file in EnumerateManagedFiles(root, warnings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (items.Count >= MaximumItems)
            {
                warnings.Add($"宏库项目超过 {MaximumItems} 个，后续项目未载入。");
                break;
            }

            items.Add(CreateItem(root, file, string.Empty, metadata));
        }

        foreach (var directory in EnumerateGroupDirectories(root, warnings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var countBefore = items.Count;
            foreach (var file in EnumerateManagedFiles(directory.FullName, warnings))
            {
                if (items.Count >= MaximumItems)
                {
                    warnings.Add($"宏库项目超过 {MaximumItems} 个，后续项目未载入。");
                    break;
                }

                items.Add(CreateItem(root, file, directory.Name, metadata));
            }

            if (directory.EnumerateDirectories().Any(child => (child.Attributes & FileAttributes.ReparsePoint) == 0))
            {
                warnings.Add($"分组“{directory.Name}”包含多级目录，深层内容未纳入宏库。");
            }

            groups.Add(new MacroLibraryGroup(directory.Name, items.Count - countBefore));
        }

        var trashItems = await ScanTrashAsync(root, metadata, warnings, cancellationToken).ConfigureAwait(false);
        return new MacroLibrarySnapshot(
            groups.OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            items.OrderBy(item => item.SortOrder).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            trashItems,
            warnings);
    }

    public Task<MacroLibraryOperationResult> CreateGroupAsync(
        string rootPath,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = EnsureRoot(rootPath);
        var normalizedName = ValidateName(groupName, "分组");
        var path = ResolveGroupPath(root, normalizedName);
        if (Directory.Exists(path))
        {
            return Task.FromResult(new MacroLibraryOperationResult(false, Message: "同名分组已经存在。"));
        }

        Directory.CreateDirectory(path);
        return Task.FromResult(new MacroLibraryOperationResult(true, normalizedName));
    }

    public async Task<MacroLibraryOperationResult> RenameGroupAsync(
        string rootPath,
        string oldName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = NormalizeRoot(rootPath);
        var oldPath = ResolveGroupPath(root, ValidateName(oldName, "分组"));
        var normalizedNewName = ValidateName(newName, "分组");
        var newPath = ResolveGroupPath(root, normalizedNewName);
        if (!Directory.Exists(oldPath))
        {
            return new MacroLibraryOperationResult(false, Message: "原分组不存在。");
        }

        if (Directory.Exists(newPath) || File.Exists(newPath))
        {
            return new MacroLibraryOperationResult(false, Message: "目标分组已经存在。");
        }

        Directory.Move(oldPath, newPath);
        var metadata = await LoadMetadataAsync(root, [], cancellationToken).ConfigureAwait(false);
        metadata.ReplacePrefix(ToRelative(root, oldPath), ToRelative(root, newPath));
        await SaveMetadataAsync(root, metadata, cancellationToken).ConfigureAwait(false);
        return new MacroLibraryOperationResult(true, normalizedNewName);
    }

    public Task<MacroLibraryOperationResult> DeleteEmptyGroupAsync(
        string rootPath,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = NormalizeRoot(rootPath);
        var path = ResolveGroupPath(root, ValidateName(groupName, "分组"));
        if (!Directory.Exists(path))
        {
            return Task.FromResult(new MacroLibraryOperationResult(false, Message: "分组不存在。"));
        }

        if (Directory.EnumerateFileSystemEntries(path).Any())
        {
            return Task.FromResult(new MacroLibraryOperationResult(false, Message: "分组仍包含文件或子目录，不能删除。"));
        }

        Directory.Delete(path);
        return Task.FromResult(new MacroLibraryOperationResult(true));
    }

    public bool GroupContainsOtherEntries(string rootPath, string groupName)
    {
        var root = NormalizeRoot(rootPath);
        var path = ResolveGroupPath(root, ValidateName(groupName, "分组"));
        if (!Directory.Exists(path))
        {
            return false;
        }

        return Directory.EnumerateDirectories(path).Any() ||
               Directory.EnumerateFiles(path).Any(file => !IsManagedExtension(Path.GetExtension(file)));
    }

    public async Task<MacroLibraryOperationResult> SaveTextAsync(
        string rootPath,
        string groupName,
        string name,
        string text,
        string? updateRelativePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new MacroLibraryOperationResult(false, Message: "XMouse 宏文本不能为空。");
        }

        var root = EnsureRoot(rootPath);
        var target = updateRelativePath is null
            ? GetUniqueItemPath(root, groupName, name, ".txt")
            : ResolveManagedItemPath(root, updateRelativePath, MacroLibraryItemKind.XMouseText);
        await WriteAtomicAsync(target, Encoding.UTF8.GetBytes(text), cancellationToken).ConfigureAwait(false);
        return new MacroLibraryOperationResult(true, ToRelative(root, target));
    }

    public async Task<MacroLibraryOperationResult> ImportFilesAsync(
        string rootPath,
        string groupName,
        IEnumerable<string> sourcePaths,
        MacroLibraryItemKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        var root = EnsureRoot(rootPath);
        string? lastRelativePath = null;
        foreach (var sourcePath in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.GetFullPath(sourcePath);
            if (!File.Exists(source))
            {
                return new MacroLibraryOperationResult(false, Message: $"找不到文件：{Path.GetFileName(source)}");
            }

            var expectedExtension = kind == MacroLibraryItemKind.XMouseText ? ".txt" : ".xml";
            if (!string.Equals(Path.GetExtension(source), expectedExtension, StringComparison.OrdinalIgnoreCase))
            {
                return new MacroLibraryOperationResult(false, Message: $"仅支持 {expectedExtension} 文件。");
            }

            var target = GetUniqueItemPath(root, groupName, Path.GetFileNameWithoutExtension(source), expectedExtension);
            await CopyAtomicAsync(source, target, cancellationToken).ConfigureAwait(false);
            lastRelativePath = ToRelative(root, target);
        }

        return new MacroLibraryOperationResult(true, lastRelativePath);
    }

    public async Task<MacroLibraryOperationResult> RenameItemAsync(
        string rootPath,
        string relativePath,
        string newName,
        CancellationToken cancellationToken = default)
    {
        var root = NormalizeRoot(rootPath);
        var source = ResolveManagedItemPath(root, relativePath);
        var stem = ValidateName(newName, "宏名称");
        var target = Path.Combine(Path.GetDirectoryName(source)!, stem + Path.GetExtension(source));
        EnsureInsideRoot(root, target);
        if (File.Exists(target))
        {
            return new MacroLibraryOperationResult(false, Message: "同名宏已经存在。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        File.Move(source, target);
        await UpdateMetadataPathAsync(root, ToRelative(root, source), ToRelative(root, target), cancellationToken).ConfigureAwait(false);
        return new MacroLibraryOperationResult(true, ToRelative(root, target));
    }

    public async Task<MacroLibraryOperationResult> MoveItemAsync(
        string rootPath,
        string relativePath,
        string groupName,
        CancellationToken cancellationToken = default)
    {
        var root = NormalizeRoot(rootPath);
        var source = ResolveManagedItemPath(root, relativePath);
        var directory = ResolveGroupDirectory(root, groupName, create: true);
        var target = Path.Combine(directory, Path.GetFileName(source));
        EnsureInsideRoot(root, target);
        if (File.Exists(target))
        {
            return new MacroLibraryOperationResult(false, Message: "目标分组中已有同名宏。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        File.Move(source, target);
        await UpdateMetadataPathAsync(root, ToRelative(root, source), ToRelative(root, target), cancellationToken).ConfigureAwait(false);
        return new MacroLibraryOperationResult(true, ToRelative(root, target));
    }

    public async Task<MacroLibraryOperationResult> MoveToTrashAsync(
        string rootPath,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var root = NormalizeRoot(rootPath);
        var source = ResolveManagedItemPath(root, relativePath);
        var id = Guid.NewGuid().ToString("N");
        var entryDirectory = Path.Combine(EnsureManagementDirectory(root), TrashDirectoryName, id);
        Directory.CreateDirectory(entryDirectory);
        var payload = Path.Combine(entryDirectory, "payload" + Path.GetExtension(source));
        var entry = new TrashEntry(ToRelative(root, source), DateTimeOffset.UtcNow);
        try
        {
            await WriteJsonAtomicAsync(Path.Combine(entryDirectory, "entry.json"), entry, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(source, payload);
        }
        catch
        {
            if (Directory.Exists(entryDirectory))
            {
                Directory.Delete(entryDirectory, true);
            }

            throw;
        }

        var metadata = await LoadMetadataAsync(root, [], cancellationToken).ConfigureAwait(false);
        metadata.Remove(ToRelative(root, source));
        await SaveMetadataAsync(root, metadata, cancellationToken).ConfigureAwait(false);
        return new MacroLibraryOperationResult(true, ToRelative(root, payload));
    }

    public async Task<MacroLibraryOperationResult> CopyItemAsync(
        string rootPath,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var root = NormalizeRoot(rootPath);
        var source = ResolveManagedItemPath(root, relativePath);
        var relativeDirectory = Path.GetDirectoryName(NormalizeRelative(relativePath)) ?? string.Empty;
        var groupName = relativeDirectory.Contains('/') ? string.Empty : relativeDirectory;
        var kind = GetKind(Path.GetExtension(source));
        var destination = GetUniqueItemPath(root, groupName, Path.GetFileNameWithoutExtension(source) + " - 副本", Path.GetExtension(source));
        await CopyAtomicAsync(source, destination, cancellationToken).ConfigureAwait(false);
        return new MacroLibraryOperationResult(true, ToRelative(root, destination), $"已复制“{Path.GetFileNameWithoutExtension(source)}”");
    }

    public async Task<MacroLibraryOperationResult> DeleteItemPermanentlyAsync(
        string rootPath,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var root = NormalizeRoot(rootPath);
        var source = ResolveManagedItemPath(root, relativePath);
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(source);
        var metadata = await LoadMetadataAsync(root, [], cancellationToken).ConfigureAwait(false);
        metadata.Remove(ToRelative(root, source));
        await SaveMetadataAsync(root, metadata, cancellationToken).ConfigureAwait(false);
        return new MacroLibraryOperationResult(true);
    }

    public async Task<MacroLibraryOperationResult> RestoreAsync(
        string rootPath,
        string trashRelativePath,
        CancellationToken cancellationToken = default)
    {
        var root = NormalizeRoot(rootPath);
        var payload = ResolveTrashPayload(root, trashRelativePath);
        var entryDirectory = Path.GetDirectoryName(payload)!;
        var entry = await ReadTrashEntryAsync(entryDirectory, cancellationToken).ConfigureAwait(false);
        var desired = ResolveRelativePath(root, entry.OriginalRelativePath);
        var destinationDirectory = Path.GetDirectoryName(desired)!;
        Directory.CreateDirectory(destinationDirectory);
        var destination = File.Exists(desired)
            ? GetUniquePath(destinationDirectory, Path.GetFileNameWithoutExtension(desired), Path.GetExtension(desired))
            : desired;
        File.Move(payload, destination);
        Directory.Delete(entryDirectory, true);
        return new MacroLibraryOperationResult(true, ToRelative(root, destination));
    }

    public Task<MacroLibraryOperationResult> DeletePermanentlyAsync(
        string rootPath,
        string trashRelativePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = NormalizeRoot(rootPath);
        var payload = ResolveTrashPayload(root, trashRelativePath);
        Directory.Delete(Path.GetDirectoryName(payload)!, true);
        return Task.FromResult(new MacroLibraryOperationResult(true));
    }

    public Task EmptyTrashAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = NormalizeRoot(rootPath);
        var trash = Path.Combine(root, ManagementDirectoryName, TrashDirectoryName);
        if (!Directory.Exists(trash))
        {
            return Task.CompletedTask;
        }

        foreach (var directory in Directory.EnumerateDirectories(trash))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Delete(directory, true);
        }

        return Task.CompletedTask;
    }

    public async Task SetFavoriteAsync(
        string rootPath,
        string relativePath,
        bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        var root = NormalizeRoot(rootPath);
        _ = ResolveManagedItemPath(root, relativePath);
        var metadata = await LoadMetadataAsync(root, [], cancellationToken).ConfigureAwait(false);
        var normalized = NormalizeRelative(relativePath);
        if (isFavorite)
        {
            metadata.Favorites.Add(normalized);
        }
        else
        {
            metadata.Favorites.Remove(normalized);
        }

        await SaveMetadataAsync(root, metadata, cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkRecentAsync(
        string rootPath,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var root = NormalizeRoot(rootPath);
        _ = ResolveManagedItemPath(root, relativePath);
        var metadata = await LoadMetadataAsync(root, [], cancellationToken).ConfigureAwait(false);
        var normalized = NormalizeRelative(relativePath);
        metadata.Recent.RemoveAll(item => PathComparer.Equals(item.RelativePath, normalized));
        metadata.Recent.Insert(0, new RecentEntry(normalized, DateTimeOffset.UtcNow));
        if (metadata.Recent.Count > MaximumRecentItems)
        {
            metadata.Recent.RemoveRange(MaximumRecentItems, metadata.Recent.Count - MaximumRecentItems);
        }

        await SaveMetadataAsync(root, metadata, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetOrderAsync(
        string rootPath,
        IEnumerable<string> relativePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);
        var root = NormalizeRoot(rootPath);
        var normalizedPaths = relativePaths
            .Select(NormalizeRelative)
            .Distinct(PathComparer)
            .ToArray();
        foreach (var relativePath in normalizedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = ResolveManagedItemPath(root, relativePath);
        }

        var metadata = await LoadMetadataAsync(root, [], cancellationToken).ConfigureAwait(false);
        metadata.SetOrder(normalizedPaths);
        await SaveMetadataAsync(root, metadata, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ReadTextAsync(
        string rootPath,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var root = NormalizeRoot(rootPath);
        var path = ResolveManagedItemPath(root, relativePath, MacroLibraryItemKind.XMouseText);
        return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ReadContentAsync(
        string rootPath,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var root = NormalizeRoot(rootPath);
        var path = ResolveManagedItemPath(root, relativePath);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return TextEncodingDetector.Decode(bytes);
    }

    public string GetFullPath(string rootPath, string relativePath)
    {
        var root = NormalizeRoot(rootPath);
        return ResolveManagedItemPath(root, relativePath);
    }

    public string PrepareSavePath(
        string rootPath,
        string groupName,
        string name,
        MacroLibraryItemKind kind,
        string? updateRelativePath = null)
    {
        var root = EnsureRoot(rootPath);
        if (updateRelativePath is not null)
        {
            return ResolveManagedItemPath(root, updateRelativePath, kind);
        }

        return GetUniqueItemPath(root, groupName, name, kind == MacroLibraryItemKind.XMouseText ? ".txt" : ".xml");
    }

    public async Task<MacroLibraryOperationResult> MigrateAsync(
        string sourceRootPath,
        string destinationRootPath,
        CancellationToken cancellationToken = default)
    {
        var source = NormalizeRoot(sourceRootPath);
        var destination = NormalizeRoot(destinationRootPath);
        if (!Directory.Exists(source))
        {
            return new MacroLibraryOperationResult(false, Message: "当前宏库目录不存在。");
        }

        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        {
            return new MacroLibraryOperationResult(false, Message: "迁移目标必须为空目录。" );
        }

        var destinationExisted = Directory.Exists(destination);
        Directory.CreateDirectory(destination);
        try
        {
            foreach (var sourceFile in EnumerateMigrationFiles(source))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(source, sourceFile);
                var target = ResolveRelativePath(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await CopyAtomicAsync(sourceFile, target, cancellationToken).ConfigureAwait(false);
                if (!await HashesEqualAsync(sourceFile, target, cancellationToken).ConfigureAwait(false))
                {
                    throw new IOException($"迁移校验失败：{Path.GetFileName(sourceFile)}");
                }
            }

            return new MacroLibraryOperationResult(true, destination);
        }
        catch
        {
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, true);
            }

            if (destinationExisted)
            {
                Directory.CreateDirectory(destination);
            }

            throw;
        }
    }

    private static IEnumerable<FileInfo> EnumerateManagedFiles(string directoryPath, ICollection<string> warnings)
    {
        try
        {
            return new DirectoryInfo(directoryPath)
                .EnumerateFiles()
                .Where(file => IsManagedExtension(file.Extension))
                .OrderBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"无法读取目录“{Path.GetFileName(directoryPath)}”：{exception.Message}");
            return [];
        }
    }

    private static IEnumerable<DirectoryInfo> EnumerateGroupDirectories(string root, ICollection<string> warnings)
    {
        try
        {
            return new DirectoryInfo(root)
                .EnumerateDirectories()
                .Where(directory =>
                    !string.Equals(directory.Name, ManagementDirectoryName, StringComparison.OrdinalIgnoreCase) &&
                    (directory.Attributes & FileAttributes.ReparsePoint) == 0)
                .OrderBy(directory => directory.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"无法读取宏库分组：{exception.Message}");
            return [];
        }
    }

    private static MacroLibraryItem CreateItem(string root, FileInfo file, string groupName, LibraryMetadata metadata)
    {
        var relative = ToRelative(root, file.FullName);
        var recent = metadata.Recent.FirstOrDefault(item => PathComparer.Equals(item.RelativePath, relative));
        return new MacroLibraryItem(
            relative,
            Path.GetFileNameWithoutExtension(file.Name),
            groupName,
            GetKind(file.Extension),
            file.Length,
            file.LastWriteTimeUtc,
            metadata.Favorites.Contains(relative),
            recent?.UsedAt,
            SortOrder: metadata.GetSortOrder(relative));
    }

    private static async Task<IReadOnlyList<MacroLibraryItem>> ScanTrashAsync(
        string root,
        LibraryMetadata metadata,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        var trashRoot = Path.Combine(root, ManagementDirectoryName, TrashDirectoryName);
        if (!Directory.Exists(trashRoot))
        {
            return [];
        }

        var result = new List<MacroLibraryItem>();
        foreach (var directory in Directory.EnumerateDirectories(trashRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var entry = await ReadTrashEntryAsync(directory, cancellationToken).ConfigureAwait(false);
                var payload = Directory.EnumerateFiles(directory, "payload.*").Single();
                var info = new FileInfo(payload);
                var originalName = Path.GetFileNameWithoutExtension(entry.OriginalRelativePath);
                var originalGroup = Path.GetDirectoryName(entry.OriginalRelativePath)?.Replace('\\', '/') ?? string.Empty;
                result.Add(new MacroLibraryItem(
                    ToRelative(root, payload),
                    originalName,
                    originalGroup,
                    GetKind(info.Extension),
                    info.Length,
                    entry.DeletedAt,
                    false,
                    IsTrashed: true,
                    OriginalRelativePath: NormalizeRelative(entry.OriginalRelativePath)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                warnings.Add($"回收站条目“{Path.GetFileName(directory)}”无法读取：{exception.Message}");
            }
        }

        return result.OrderByDescending(item => item.LastWriteTime).ToArray();
    }

    private static async Task<LibraryMetadata> LoadMetadataAsync(
        string root,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, ManagementDirectoryName, MetadataFileName);
        if (!File.Exists(path))
        {
            return new LibraryMetadata();
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            var stored = await JsonSerializer.DeserializeAsync<StoredMetadata>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new LibraryMetadata(stored);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            warnings.Add($"宏库元数据无法读取，收藏和最近使用已临时重置：{exception.Message}");
            return new LibraryMetadata();
        }
    }

    private static Task SaveMetadataAsync(string root, LibraryMetadata metadata, CancellationToken cancellationToken)
    {
        var path = Path.Combine(EnsureManagementDirectory(root), MetadataFileName);
        return WriteJsonAtomicAsync(path, metadata.ToStored(), cancellationToken);
    }

    private static string EnsureManagementDirectory(string root)
    {
        var path = Path.Combine(root, ManagementDirectoryName);
        Directory.CreateDirectory(path);
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A visible management directory is preferable to losing metadata on restricted volumes.
        }

        return path;
    }

    private static async Task UpdateMetadataPathAsync(
        string root,
        string oldRelativePath,
        string newRelativePath,
        CancellationToken cancellationToken)
    {
        var metadata = await LoadMetadataAsync(root, [], cancellationToken).ConfigureAwait(false);
        metadata.Replace(oldRelativePath, newRelativePath);
        await SaveMetadataAsync(root, metadata, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(stream, value, cancellationToken: cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task WriteAtomicAsync(string target, byte[] content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, target, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task CopyAtomicAsync(string source, string target, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, target);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task<bool> HashesEqualAsync(string first, string second, CancellationToken cancellationToken)
    {
        await using var firstStream = File.OpenRead(first);
        await using var secondStream = File.OpenRead(second);
        var firstHash = await SHA256.HashDataAsync(firstStream, cancellationToken).ConfigureAwait(false);
        var secondHash = await SHA256.HashDataAsync(secondStream, cancellationToken).ConfigureAwait(false);
        return firstHash.AsSpan().SequenceEqual(secondHash);
    }

    private static IEnumerable<string> EnumerateMigrationFiles(string root)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var file in directory.EnumerateFiles())
            {
                yield return file.FullName;
            }

            foreach (var child in directory.EnumerateDirectories())
            {
                if ((child.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static string GetUniqueItemPath(string root, string groupName, string name, string extension)
    {
        var directory = ResolveGroupDirectory(root, groupName, create: true);
        return GetUniquePath(directory, ValidateName(name, "宏名称"), extension);
    }

    private static string GetUniquePath(string directory, string stem, string extension)
    {
        var candidate = Path.Combine(directory, stem + extension);
        for (var suffix = 2; File.Exists(candidate); suffix++)
        {
            candidate = Path.Combine(directory, $"{stem} ({suffix}){extension}");
        }

        return candidate;
    }

    private static string ResolveGroupDirectory(string root, string groupName, bool create)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return root;
        }

        var path = ResolveGroupPath(root, ValidateName(groupName, "分组"));
        if (create)
        {
            Directory.CreateDirectory(path);
        }

        return path;
    }

    private static string ResolveGroupPath(string root, string groupName)
    {
        var path = Path.Combine(root, groupName);
        EnsureInsideRoot(root, path);
        return path;
    }

    private static string ResolveManagedItemPath(
        string root,
        string relativePath,
        MacroLibraryItemKind? expectedKind = null)
    {
        var path = ResolveRelativePath(root, relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("宏库条目不存在。", Path.GetFileName(path));
        }

        if (!IsManagedExtension(Path.GetExtension(path)))
        {
            throw new InvalidOperationException("该文件不是受支持的宏库条目。");
        }

        if (expectedKind is not null && GetKind(Path.GetExtension(path)) != expectedKind)
        {
            throw new InvalidOperationException("宏库条目格式与操作不匹配。");
        }

        var relative = NormalizeRelative(Path.GetRelativePath(root, path));
        if (relative.StartsWith(ManagementDirectoryName + "/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("管理目录中的文件不能作为普通宏操作。");
        }

        return path;
    }

    private static string ResolveTrashPayload(string root, string relativePath)
    {
        var path = ResolveRelativePath(root, relativePath);
        var requiredPrefix = NormalizeRelative(Path.Combine(ManagementDirectoryName, TrashDirectoryName)) + "/";
        if (!NormalizeRelative(Path.GetRelativePath(root, path)).StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path))
        {
            throw new InvalidOperationException("无效的回收站条目。");
        }

        return path;
    }

    private static string ResolveRelativePath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("必须提供宏库内的相对路径。", nameof(relativePath));
        }

        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        EnsureInsideRoot(root, path);
        return path;
    }

    private static async Task<TrashEntry> ReadTrashEntryAsync(string directory, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(Path.Combine(directory, "entry.json"));
        return await JsonSerializer.DeserializeAsync<TrashEntry>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("回收站元数据为空。");
    }

    private static string EnsureRoot(string rootPath)
    {
        var root = NormalizeRoot(rootPath);
        Directory.CreateDirectory(root);
        return root;
    }

    private static string NormalizeRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("宏库路径不能为空。", nameof(rootPath));
        }

        return Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void EnsureInsideRoot(string root, string path)
    {
        var rootPrefix = root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("路径超出宏库根目录。");
        }
    }

    private static string ValidateName(string value, string label)
    {
        var name = value?.Trim().TrimEnd('.', ' ') ?? string.Empty;
        if (name.Length == 0 || name.Length > 128 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            string.Equals(name, ManagementDirectoryName, StringComparison.OrdinalIgnoreCase) || IsReservedName(name))
        {
            throw new ArgumentException($"{label}无效或包含 Windows 不允许的名称。", nameof(value));
        }

        return name;
    }

    private static bool IsReservedName(string value)
    {
        var stem = Path.GetFileNameWithoutExtension(value).TrimEnd(' ', '.').ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CLOCK$" ||
               (stem.StartsWith("COM", StringComparison.Ordinal) && stem.Length == 4 && stem[3] is >= '1' and <= '9') ||
               (stem.StartsWith("LPT", StringComparison.Ordinal) && stem.Length == 4 && stem[3] is >= '1' and <= '9');
    }

    private static bool IsManagedExtension(string extension) =>
        string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase);

    private static MacroLibraryItemKind GetKind(string extension) =>
        string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase)
            ? MacroLibraryItemKind.XMouseText
            : MacroLibraryItemKind.RazerXml;

    private static string ToRelative(string root, string path) =>
        NormalizeRelative(Path.GetRelativePath(root, path));

    private static string NormalizeRelative(string path) => path.Replace('\\', '/');

    private sealed record TrashEntry(string OriginalRelativePath, DateTimeOffset DeletedAt);

    private sealed record RecentEntry(string RelativePath, DateTimeOffset UsedAt);

    private sealed record StoredMetadata(string[]? Favorites = null, RecentEntry[]? Recent = null, string[]? Order = null);

    private sealed class LibraryMetadata
    {
        public LibraryMetadata(StoredMetadata? stored = null)
        {
            Favorites = new HashSet<string>(stored?.Favorites?.Select(NormalizeRelative) ?? [], PathComparer);
            Recent = stored?.Recent?
                .Select(item => item with { RelativePath = NormalizeRelative(item.RelativePath) })
                .ToList() ?? [];
            Order = stored?.Order?
                .Select(NormalizeRelative)
                .Distinct(PathComparer)
                .ToList() ?? [];
        }

        public HashSet<string> Favorites { get; }

        public List<RecentEntry> Recent { get; }

        public List<string> Order { get; }

        public int GetSortOrder(string relativePath)
        {
            var normalized = NormalizeRelative(relativePath);
            for (var index = 0; index < Order.Count; index++)
            {
                if (PathComparer.Equals(Order[index], normalized))
                {
                    return index;
                }
            }

            return int.MaxValue;
        }

        public void SetOrder(IEnumerable<string> relativePaths)
        {
            Order.Clear();
            Order.AddRange(relativePaths.Select(NormalizeRelative).Distinct(PathComparer));
        }

        public void Remove(string relativePath)
        {
            var normalized = NormalizeRelative(relativePath);
            Favorites.Remove(normalized);
            Recent.RemoveAll(item => PathComparer.Equals(item.RelativePath, normalized));
            Order.RemoveAll(item => PathComparer.Equals(item, normalized));
        }

        public void Replace(string oldRelativePath, string newRelativePath)
        {
            var oldPath = NormalizeRelative(oldRelativePath);
            var newPath = NormalizeRelative(newRelativePath);
            if (Favorites.Remove(oldPath))
            {
                Favorites.Add(newPath);
            }

            for (var index = 0; index < Recent.Count; index++)
            {
                if (PathComparer.Equals(Recent[index].RelativePath, oldPath))
                {
                    Recent[index] = Recent[index] with { RelativePath = newPath };
                }
            }

            for (var index = 0; index < Order.Count; index++)
            {
                if (PathComparer.Equals(Order[index], oldPath))
                {
                    Order[index] = newPath;
                }
            }
        }

        public void ReplacePrefix(string oldPrefix, string newPrefix)
        {
            var normalizedOld = NormalizeRelative(oldPrefix).TrimEnd('/') + "/";
            var normalizedNew = NormalizeRelative(newPrefix).TrimEnd('/') + "/";
            foreach (var path in Favorites.Where(path => path.StartsWith(normalizedOld, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                Favorites.Remove(path);
                Favorites.Add(normalizedNew + path[normalizedOld.Length..]);
            }

            for (var index = 0; index < Recent.Count; index++)
            {
                var path = Recent[index].RelativePath;
                if (path.StartsWith(normalizedOld, StringComparison.OrdinalIgnoreCase))
                {
                    Recent[index] = Recent[index] with { RelativePath = normalizedNew + path[normalizedOld.Length..] };
                }
            }

            for (var index = 0; index < Order.Count; index++)
            {
                var path = Order[index];
                if (path.StartsWith(normalizedOld, StringComparison.OrdinalIgnoreCase))
                {
                    Order[index] = normalizedNew + path[normalizedOld.Length..];
                }
            }
        }

        public StoredMetadata ToStored() => new(
            Favorites.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            Recent.Take(MaximumRecentItems).ToArray(),
            Order.ToArray());
    }
}
