// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Copyright (C) Song Mai Software.

namespace PDownloader.Core.Services.DownloadServices;

public class DownloadConfigService
{
    private const int MaxCategoryCount = 64;
    private const int MaxExtensionsPerCategory = 256;

    private readonly UserDataStore _userDataStore;
    private readonly object _sync = new();
    private DownloadSettingsDto _downloadConfigs = new();

    public event Action<DownloadSettingsDto>? Changed;

    public DownloadSettingsDto DownloadConfigs => GetSnapshot();

    public DownloadConfigService(UserDataStore userDataStore)
    {
        _userDataStore = userDataStore;
        TryLoadSettings(out DownloadSettingsDto configs);
        EnsureDefaults(configs);
        lock (_sync)
        {
            _downloadConfigs = configs;
        }
        EnsureCategoryDirectories(configs.DownloadCategories);
    }

    public static string GetDefaultDownloadFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads");

    public static string GetDefaultTempFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SM SOFT",
        "PDownloader",
        "Temp");

    private bool TryLoadSettings(out DownloadSettingsDto configs)
    {
        configs = new DownloadSettingsDto();
        try
        {
            _userDataStore.Reload();
            string? raw = _userDataStore.GetValue<string>(DownloadSettingsProtocol.StoreKey);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                DownloadSettingsDto? loaded = JsonSerializer.Deserialize<DownloadSettingsDto>(raw);
                if (loaded != null)
                {
                    configs = loaded;
                }
            }

            return true;
        }
        catch
        {
            // Keep in-memory defaults without overwriting unreadable user data.
            return false;
        }
    }

    private void EnsureDefaults(DownloadSettingsDto configs)
    {
        if (string.IsNullOrWhiteSpace(configs.DefaultDownloadFolder))
        {
            configs.DefaultDownloadFolder = GetDefaultDownloadFolder();
        }

        configs.DefaultDownloadFolder = NormalizeFolderOrFallback(
            configs.DefaultDownloadFolder,
            GetDefaultDownloadFolder());

        if (string.IsNullOrWhiteSpace(configs.DefaultTempFolder))
        {
            configs.DefaultTempFolder = GetDefaultTempFolder();
        }

        if (configs.DefaultThreadCount <= 0)
        {
            configs.DefaultThreadCount = 8;
        }

        configs.FileMergeMode = FileMergeModeParser
            .Parse(configs.FileMergeMode)
            .ToConfigValue();

        if (configs.DownloadCategories is not { Count: > 0 })
        {
            configs.DownloadCategories = DownloadCategoryDefaults.Create(
                configs.DefaultDownloadFolder);
        }

        configs.DownloadCategories = NormalizeCategories(
            configs.DownloadCategories,
            configs.DefaultDownloadFolder);
    }

    public void Reload()
    {
        DownloadSettingsDto snapshot;
        lock (_sync)
        {
            TryLoadSettings(out DownloadSettingsDto configs);
            EnsureDefaults(configs);
            _downloadConfigs = configs;
            snapshot = Clone(configs);
        }

        EnsureCategoryDirectories(snapshot.DownloadCategories);
        Changed?.Invoke(snapshot);
    }

    public FileMergeMode GetFileMergeMode() =>
        FileMergeModeParser.Parse(GetSnapshot().FileMergeMode);

    public DownloadCategorySelection CreateRunnerSelection(
        string? fileName,
        string? requestedPath,
        bool preserveRequestedPath)
    {
        DownloadSettingsDto configs = GetSnapshot();
        List<DownloadCategoryDto> categories = configs.DownloadCategories
            .Where(category => category.IsEnabled)
            .Select(Clone)
            .ToList();

        DownloadCategoryDto? selected = FindCategory(categories, fileName);
        string saveTo = preserveRequestedPath && !string.IsNullOrWhiteSpace(requestedPath)
            ? requestedPath
            : selected?.FolderPath ?? requestedPath ?? configs.DefaultDownloadFolder;

        EnsureCategoryDirectories(selected is null ? [] : [selected]);

        return new DownloadCategorySelection(
            categories,
            selected?.Id ?? string.Empty,
            saveTo);
    }

    public string PrepareOutputFolder(string? folder)
    {
        string candidate = string.IsNullOrWhiteSpace(folder)
            ? GetSnapshot().DefaultDownloadFolder
            : folder;
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"'))));
        if (File.Exists(fullPath))
        {
            throw new IOException("The download path points to a file instead of a folder.");
        }

        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public DownloadSettingsDto RememberCategoryPath(string categoryId, string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryId);
        string normalizedFolder = PrepareOutputFolder(folder);
        DownloadSettingsDto snapshot;

        lock (_sync)
        {
            // Merge with the latest persisted settings in case Main edited a
            // different category while this Runner was open.
            if (!TryLoadSettings(out DownloadSettingsDto configs))
            {
                throw new IOException("Download settings could not be read.");
            }
            EnsureDefaults(configs);
            DownloadCategoryDto? category = configs.DownloadCategories.FirstOrDefault(
                item => string.Equals(item.Id, categoryId, StringComparison.OrdinalIgnoreCase));
            if (category is null)
            {
                throw new InvalidOperationException("The selected download group no longer exists.");
            }

            category.FolderPath = normalizedFolder;
            Persist(configs);
            _downloadConfigs = configs;
            snapshot = Clone(configs);
        }

        Changed?.Invoke(snapshot);
        return snapshot;
    }

    public DownloadSettingsDto GetSnapshot()
    {
        lock (_sync)
        {
            return Clone(_downloadConfigs);
        }
    }

    private void Persist(DownloadSettingsDto configs)
    {
        string raw = JsonSerializer.Serialize(configs);
        _userDataStore.SetValue(DownloadSettingsProtocol.StoreKey, raw);
    }

    private static DownloadCategoryDto? FindCategory(
        IReadOnlyList<DownloadCategoryDto> categories,
        string? fileName)
    {
        string extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(extension))
        {
            DownloadCategoryDto? matched = categories.FirstOrDefault(category =>
                category.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
            if (matched is not null)
            {
                return matched;
            }
        }

        return categories.FirstOrDefault(category =>
                   string.Equals(category.Id, DownloadCategoryDefaults.OtherId,
                       StringComparison.OrdinalIgnoreCase))
               ?? categories.FirstOrDefault();
    }

    private static List<DownloadCategoryDto> NormalizeCategories(
        IEnumerable<DownloadCategoryDto>? source,
        string downloadRoot)
    {
        var result = new List<DownloadCategoryDto>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (DownloadCategoryDto item in source ?? [])
        {
            if (result.Count >= MaxCategoryCount)
            {
                break;
            }

            string id = item.Id?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
            {
                do
                {
                    id = "custom-" + Guid.NewGuid().ToString("N");
                }
                while (!ids.Add(id));
            }

            string name = item.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Group " + (result.Count + 1).ToString(CultureInfo.InvariantCulture);
            }

            result.Add(new DownloadCategoryDto
            {
                Id = id,
                Name = name,
                FolderPath = NormalizeFolderOrFallback(item.FolderPath, downloadRoot),
                Extensions = NormalizeExtensions(item.Extensions),
                IsEnabled = item.IsEnabled
            });
        }

        return result;
    }

    private static List<string> NormalizeExtensions(IEnumerable<string>? source)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in source ?? [])
        {
            string extension = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(extension))
            {
                continue;
            }

            if (!extension.StartsWith('.'))
            {
                extension = "." + extension;
            }

            if (extension.Length > 32
                || extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || extension.Contains('*')
                || extension.Contains('?'))
            {
                continue;
            }

            if (seen.Add(extension))
            {
                result.Add(extension);
                if (result.Count >= MaxExtensionsPerCategory)
                {
                    break;
                }
            }
        }

        return result;
    }

    private static void EnsureCategoryDirectories(IEnumerable<DownloadCategoryDto> categories)
    {
        foreach (DownloadCategoryDto category in categories)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(category.FolderPath)
                    && !File.Exists(category.FolderPath))
                {
                    Directory.CreateDirectory(category.FolderPath);
                }
            }
            catch (Exception ex)
            {
                // Keep Core available. Runner will report an unavailable folder
                // if the user selects a location that cannot be created.
                Debug.WriteLine($"[Settings] Could not create group folder '{category.FolderPath}': {ex.Message}");
            }
        }
    }

    private static string NormalizeFolderOrFallback(string? folder, string fallback)
    {
        try
        {
            string candidate = string.IsNullOrWhiteSpace(folder) ? fallback : folder;
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"'))));
        }
        catch
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(fallback));
        }
    }

    private static DownloadCategoryDto Clone(DownloadCategoryDto category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        FolderPath = category.FolderPath,
        Extensions = [.. category.Extensions],
        IsEnabled = category.IsEnabled
    };

    private static DownloadSettingsDto Clone(DownloadSettingsDto configs) => new()
    {
        DefaultDownloadFolder = configs.DefaultDownloadFolder,
        DownloadCategories = configs.DownloadCategories.Select(Clone).ToList(),
        DefaultTempFolder = configs.DefaultTempFolder,
        DefaultThreadCount = configs.DefaultThreadCount,
        FileMergeMode = configs.FileMergeMode,
        CloseProgressWindowWhenDownloadCompletes = configs.CloseProgressWindowWhenDownloadCompletes,
        CloseProgressWindowAfterOpeningFile = configs.CloseProgressWindowAfterOpeningFile,
        CloseProgressWindowAfterOpeningFolder = configs.CloseProgressWindowAfterOpeningFolder
    };
}

public sealed record DownloadCategorySelection(
    List<DownloadCategoryDto> Categories,
    string SelectedCategoryId,
    string SaveTo);
