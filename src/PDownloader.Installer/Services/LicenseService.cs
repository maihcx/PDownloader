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

using System.Reflection;

namespace PDownloader.Installer.Services;

public sealed class LicenseService : ILicenseService
{
    private const string DefaultLicense = """
        MIT License

        Copyright (c) 2024 PDownloader

        Permission is hereby granted, free of charge, to any person obtaining a copy
        of this software and associated documentation files (the "Software"), to deal
        in the Software without restriction, including without limitation the rights
        to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
        copies of the Software, and to permit persons to whom the Software is
        furnished to do so, subject to the following conditions:

        The above copyright notice and this permission notice shall be included in all
        copies or substantial portions of the Software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
        IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
        FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
        AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
        LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
        OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
        SOFTWARE.
        """;

    public string Load(string languageCode)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        if (!string.IsNullOrEmpty(languageCode)
            && !languageCode.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            string? localizedLicense = ReadResource(
                assembly,
                $"PDownloader.Installer.LICENSE.{languageCode}");
            if (localizedLicense is not null)
            {
                return localizedLicense;
            }
        }

        string? englishLicense = ReadResource(
            assembly,
            "PDownloader.Installer.LICENSE");
        if (englishLicense is not null)
        {
            return englishLicense;
        }

        string installerDirectory = Path.GetDirectoryName(
            Environment.ProcessPath ?? AppContext.BaseDirectory) ?? string.Empty;

        string[] candidates =
        {
            Path.Combine(installerDirectory, $"LICENSE.{languageCode}"),
            Path.Combine(installerDirectory, "LICENSE"),
            Path.Combine(installerDirectory, "..", $"LICENSE.{languageCode}"),
            Path.Combine(installerDirectory, "..", "LICENSE"),
        };

        string? licensePath = candidates.FirstOrDefault(File.Exists);
        return licensePath is null
            ? DefaultLicense
            : File.ReadAllText(licensePath);
    }

    private static string? ReadResource(Assembly assembly, string resourceName)
    {
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
