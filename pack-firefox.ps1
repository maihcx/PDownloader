param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDir,

    [Parameter(Mandatory = $true)]
    [string]$OutputXpi
)

$ErrorActionPreference = 'Stop'

$sourceRoot = (Resolve-Path -LiteralPath $SourceDir).Path.TrimEnd([char[]]'\/')
$outputPath = [System.IO.Path]::GetFullPath($OutputXpi)
$outputDir = [System.IO.Path]::GetDirectoryName($outputPath)

if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
    throw "Firefox build directory does not exist: $sourceRoot"
}

if (-not (Test-Path -LiteralPath $outputDir -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

if (Test-Path -LiteralPath $outputPath) {
    Remove-Item -LiteralPath $outputPath -Force
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$fileStream = [System.IO.File]::Open(
    $outputPath,
    [System.IO.FileMode]::CreateNew,
    [System.IO.FileAccess]::ReadWrite,
    [System.IO.FileShare]::None
)

try {
    $archive = [System.IO.Compression.ZipArchive]::new(
        $fileStream,
        [System.IO.Compression.ZipArchiveMode]::Create,
        $false
    )

    try {
        Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | ForEach-Object {
            $relativePath = $_.FullName.Substring($sourceRoot.Length).TrimStart([char[]]'\/')

            # Firefox/Gecko expects ZIP resource paths to use '/' regardless of
            # the host OS. Do not let Windows path separators leak into the XPI.
            $entryName = $relativePath.Replace('\', '/')

            $entry = $archive.CreateEntry(
                $entryName,
                [System.IO.Compression.CompressionLevel]::Optimal
            )

            $input = [System.IO.File]::OpenRead($_.FullName)
            try {
                $output = $entry.Open()
                try {
                    $input.CopyTo($output)
                }
                finally {
                    $output.Dispose()
                }
            }
            finally {
                $input.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $fileStream.Dispose()
}

# Validate exact resource names after packaging. This catches the failure where
# Firefox can read manifest.json but cannot resolve _locales/... inside the XPI.
$requiredEntries = @(
    'manifest.json',
    '_locales/en/messages.json',
    '_locales/vi/messages.json'
)

$readStream = [System.IO.File]::OpenRead($outputPath)
try {
    $archive = [System.IO.Compression.ZipArchive]::new(
        $readStream,
        [System.IO.Compression.ZipArchiveMode]::Read,
        $false
    )

    try {
        $entryNames = @{}
        foreach ($entry in $archive.Entries) {
            $entryNames[$entry.FullName] = $true
        }

        foreach ($required in $requiredEntries) {
            if (-not $entryNames.ContainsKey($required)) {
                throw "Invalid XPI: missing exact entry '$required'."
            }
        }

        $backslashEntry = $archive.Entries | Where-Object { $_.FullName.Contains('\') } | Select-Object -First 1
        if ($null -ne $backslashEntry) {
            throw "Invalid XPI: entry contains a Windows path separator: '$($backslashEntry.FullName)'."
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $readStream.Dispose()
}

Write-Host "Firefox XPI validated: $outputPath"
