// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// Copyright (C) Song Mai Software.

namespace PDownloader.Models;

public partial class DownloadCategoryViewModel : ObservableObject
{
    public string Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _folderPath;

    [ObservableProperty]
    private string _extensionsText;

    [ObservableProperty]
    private bool _isEnabled;

    public DownloadCategoryViewModel(
        string id,
        string name,
        string folderPath,
        string extensionsText,
        bool isEnabled)
    {
        Id = id;
        _name = name;
        _folderPath = folderPath;
        _extensionsText = extensionsText;
        _isEnabled = isEnabled;
    }

    public static DownloadCategoryViewModel FromContract(DownloadCategoryDto category) => new(
        category.Id,
        category.Name,
        category.FolderPath,
        string.Join(", ", category.Extensions),
        category.IsEnabled);

    public DownloadCategoryDto ToContract() => new()
    {
        Id = Id,
        Name = Name,
        FolderPath = FolderPath,
        Extensions = ParseExtensions(ExtensionsText),
        IsEnabled = IsEnabled
    };

    public static List<string> ParseExtensions(string? value)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string token in (value ?? string.Empty).Split(
                     [',', ';', ' ', '\t', '\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string extension = token.ToLowerInvariant();
            if (!extension.StartsWith('.'))
            {
                extension = "." + extension;
            }

            if (extension.Length <= 32
                && extension.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                && !extension.Contains('*')
                && !extension.Contains('?')
                && seen.Add(extension))
            {
                result.Add(extension);
            }
        }

        return result;
    }
}
