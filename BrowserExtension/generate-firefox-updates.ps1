param(
    [Parameter(Mandatory = $true)]
    [string]$XpiPath,

    [Parameter(Mandatory = $true)]
    [string]$ConfigPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

function Assert-HttpsUrl {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $uri = $null
    if (-not [System.Uri]::TryCreate($Value, [System.UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -ne [System.Uri]::UriSchemeHttps) {
        throw "$Name must be an absolute HTTPS URL: $Value"
    }
}

$xpiFullPath = (Resolve-Path -LiteralPath $XpiPath).Path
$configFullPath = (Resolve-Path -LiteralPath $ConfigPath).Path
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($outputFullPath)

$config = Get-Content -LiteralPath $configFullPath -Raw | ConvertFrom-Json
$extensionId = [string]$config.extension_id
$updateLink = [string]$config.update_link
$strictMinVersion = [string]$config.strict_min_version

if ([string]::IsNullOrWhiteSpace($extensionId)) {
    throw 'Firefox extension_id is missing from the Firefox build configuration.'
}

Assert-HttpsUrl -Value $updateLink -Name 'Firefox update_link'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$stream = [System.IO.File]::OpenRead($xpiFullPath)
try {
    $archive = [System.IO.Compression.ZipArchive]::new(
        $stream,
        [System.IO.Compression.ZipArchiveMode]::Read,
        $false
    )

    try {
        $manifestEntry = $archive.GetEntry('manifest.json')
        if ($null -eq $manifestEntry) {
            throw 'Invalid XPI: manifest.json was not found at the archive root.'
        }

        # A release XPI used for self-updates must be signed by Mozilla.
        $hasMozillaSignature =
            $null -ne $archive.GetEntry('META-INF/mozilla.rsa') -or
            $null -ne $archive.GetEntry('META-INF/cose.sig')

        if (-not $hasMozillaSignature) {
            throw 'Refusing to publish updates.json because the XPI does not contain a Mozilla signature.'
        }

        $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
        try {
            $manifest = $reader.ReadToEnd() | ConvertFrom-Json
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $stream.Dispose()
}

$version = [string]$manifest.version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'Invalid XPI: manifest.json does not contain a version.'
}

$actualExtensionId = [string]$manifest.browser_specific_settings.gecko.id
if (-not [string]::IsNullOrWhiteSpace($actualExtensionId) -and
    $actualExtensionId -ne $extensionId) {
    throw "XPI extension ID '$actualExtensionId' does not match configured ID '$extensionId'."
}

$sha256 = (Get-FileHash -LiteralPath $xpiFullPath -Algorithm SHA256).Hash.ToLowerInvariant()

$update = [ordered]@{
    version = $version
    update_link = $updateLink
    update_hash = "sha256:$sha256"
}

if (-not [string]::IsNullOrWhiteSpace($strictMinVersion)) {
    $update.applications = [ordered]@{
        gecko = [ordered]@{
            strict_min_version = $strictMinVersion
        }
    }
}

$addons = [ordered]@{}
$addons[$extensionId] = [ordered]@{
    updates = @($update)
}

$updateManifest = [ordered]@{
    addons = $addons
}

if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$json = $updateManifest | ConvertTo-Json -Depth 10
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($outputFullPath, $json + "`r`n", $utf8WithoutBom)

Write-Host "[OK] Firefox update manifest generated."
Write-Host "Version: $version"
Write-Host "Output : $outputFullPath"
