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

namespace PDownloader.Runner.Models;

public sealed class DownloadCategoryItem
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string FolderPath { get; init; } = string.Empty;
    public IReadOnlyList<string> Extensions { get; init; } = [];
    public string ExtensionsSummary => Extensions.Count == 0
        ? LanguageBase.GetLangValue("download_group_all_other_types")
        : string.Join(", ", Extensions);

    public static DownloadCategoryItem FromContract(DownloadCategoryDto category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        FolderPath = category.FolderPath,
        Extensions = [.. category.Extensions]
    };
}
