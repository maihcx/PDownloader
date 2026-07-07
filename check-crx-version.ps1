# Kiem tra version thuc te ma GitHub dang tra ve cho file .crx
# Sua URL ben duoi cho dung repo cua ban (lay tu update.xml, thuoc tinh codebase)

$url = "https://raw.githubusercontent.com/maihcx/PDownloader/main/BrowserExtension/PDownloader.crx"

$tmpCrx = "$env:TEMP\check_pdownloader.crx"
$tmpZip = "$env:TEMP\check_pdownloader.zip"
$tmpDir = "$env:TEMP\check_pdownloader_extract"

Write-Host "Dang tai crx tu: $url"
Invoke-WebRequest -Uri $url -OutFile $tmpCrx -Headers @{ "Cache-Control" = "no-cache" }

$bytes = [System.IO.File]::ReadAllBytes($tmpCrx)

# Tim vi tri chu ky ZIP "PK\x03\x04" de bo qua phan header CRX3
$sig = [byte[]](0x50,0x4B,0x03,0x04)
$offset = -1
for ($i = 0; $i -lt $bytes.Length - 4; $i++) {
    if ($bytes[$i] -eq $sig[0] -and $bytes[$i+1] -eq $sig[1] -and $bytes[$i+2] -eq $sig[2] -and $bytes[$i+3] -eq $sig[3]) {
        $offset = $i
        break
    }
}

if ($offset -lt 0) {
    Write-Host "KHONG PHAI FILE CRX HOP LE (hoac da bi hong)." -ForegroundColor Red
    exit 1
}

Write-Host "Kich thuoc file tai duoc: $($bytes.Length) bytes"
Write-Host "Zip payload bat dau tai offset: $offset"

$zipBytes = $bytes[$offset..($bytes.Length - 1)]
[System.IO.File]::WriteAllBytes($tmpZip, $zipBytes)

if (Test-Path $tmpDir) { Remove-Item $tmpDir -Recurse -Force }
Expand-Archive -Path $tmpZip -DestinationPath $tmpDir -Force

$manifest = Get-Content "$tmpDir\manifest.json" -Raw
if ($manifest -match '"version"\s*:\s*"([^"]+)"') {
    Write-Host ""
    Write-Host "==> VERSION THUC TE DANG DUOC GITHUB TRA VE: $($Matches[1])" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "So sanh voi version ban da bump trong manifest.json local (0.1.1)."
    Write-Host "Neu khac nhau -> file .crx tren GitHub CHUA duoc cap nhat dung -> push lai."
} else {
    Write-Host "Khong doc duoc version tu manifest.json." -ForegroundColor Red
}
