<#
  Công cụ phát hành bản cập nhật Con Ngoan (chạy bằng Windows PowerShell có sẵn, không cần build).

  KHÓA BÍ MẬT (.pem) TUYỆT ĐỐI KHÔNG đưa vào mã nguồn / GitHub, không gửi qua chat hay email. Sao lưu ra USB cất riêng.
  Mất khóa = các máy đã cài sẽ không nhận được bản cập nhật nào nữa.

  Ký một bản phát hành (tạo version.json + version.json.sig để đưa lên website):
    powershell -ExecutionPolicy Bypass -File scripts\cmc-release.ps1 sign -Key <khoa.pem> -Installer <Setup.exe> `
        -Version 1.6.1 -Url https://caominhcomputer.com/con-ngoan/ConNgoan-Setup-1.6.1.exe -Notes "Có gì mới" -Out <thư mục>
  Kiểm tra lại trước khi đăng:
    powershell -ExecutionPolicy Bypass -File scripts\cmc-release.ps1 verify -Out <thư mục> [-Installer <Setup.exe>]
  In khóa công khai của một khóa bí mật (để dán vào UpdateKeys.Trusted):
    powershell -ExecutionPolicy Bypass -File scripts\cmc-release.ps1 pubkey -Key <khoa.pem>
  Tạo cặp khóa mới (chỉ khi đổi khóa):
    powershell -ExecutionPolicy Bypass -File scripts\cmc-release.ps1 keygen -Key <khoa-moi.pem>
#>
param(
    [Parameter(Mandatory, Position = 0)][ValidateSet('sign', 'verify', 'pubkey', 'keygen')][string]$Command,
    [string]$Key,
    [string]$Installer,
    [string]$Version,
    [string]$Url,
    [string]$Notes,
    [string]$Out
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Core
[Console]::OutputEncoding = [Text.Encoding]::UTF8

# Tiền tố SubjectPublicKeyInfo cho khóa ECDSA P-256 (id-ecPublicKey + prime256v1), sau đó là 0x04 || X || Y.
$SpkiPrefix = [byte[]](0x30,0x59,0x30,0x13,0x06,0x07,0x2a,0x86,0x48,0xce,0x3d,0x02,0x01,0x06,0x08,0x2a,0x86,0x48,0xce,0x3d,0x03,0x01,0x07,0x03,0x42,0x00)

function Read-PrivateKey([string]$path) {
    $pem = (Get-Content $path -Raw) -replace '-----[^-]+-----', '' -replace '\s', ''
    $cng = [Security.Cryptography.CngKey]::Import([Convert]::FromBase64String($pem), [Security.Cryptography.CngKeyBlobFormat]::Pkcs8PrivateBlob)
    New-Object Security.Cryptography.ECDsaCng($cng)
}

function Get-Spki([Security.Cryptography.ECDsaCng]$ecdsa) {
    # ECCPUBLIC_BLOB: magic(4) cbKey(4) X(32) Y(32)
    $blob = $ecdsa.Key.Export([Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob)
    [Convert]::ToBase64String($SpkiPrefix + [byte[]](0x04) + $blob[8..71])
}

function Import-Spki([string]$b64) {
    $spki = [Convert]::FromBase64String($b64)
    if ($spki.Length -ne 91) { throw 'Khóa công khai không phải ECDSA P-256.' }
    $blob = [BitConverter]::GetBytes([int]0x31534345) + [BitConverter]::GetBytes([int]32) + $spki[27..90]
    New-Object Security.Cryptography.ECDsaCng([Security.Cryptography.CngKey]::Import($blob, [Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob))
}

function Get-TrustedKeys {
    $src = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\CaoMinhComputer.Core\Services\UpdateService.cs'
    [regex]::Matches((Get-Content $src -Raw -Encoding UTF8), '"(MFkw[A-Za-z0-9+/=]{80,})"') | ForEach-Object { $_.Groups[1].Value }
}

function Get-Sha256Hex([string]$path) { (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() }

switch ($Command) {
    'keygen' {
        if (-not $Key) { throw 'Thiếu -Key' }
        if (Test-Path $Key) { throw "$Key đã tồn tại – không ghi đè khóa cũ." }
        $p = New-Object Security.Cryptography.CngKeyCreationParameters
        $p.ExportPolicy = [Security.Cryptography.CngExportPolicies]::AllowPlaintextExport
        $cng = [Security.Cryptography.CngKey]::Create([Security.Cryptography.CngAlgorithm]::ECDsaP256, $null, $p)
        $der = $cng.Export([Security.Cryptography.CngKeyBlobFormat]::Pkcs8PrivateBlob)
        $b64 = [Convert]::ToBase64String($der) -replace '(.{64})', "`$1`n"
        New-Item -ItemType Directory -Force (Split-Path -Parent ([IO.Path]::GetFullPath($Key))) | Out-Null
        [IO.File]::WriteAllText($Key, "-----BEGIN PRIVATE KEY-----`n$($b64.Trim())`n-----END PRIVATE KEY-----`n")
        icacls $Key /inheritance:r /grant:r "$($env:USERNAME):F" | Out-Null
        Write-Host "Đã tạo khóa bí mật: $Key (chỉ tài khoản $env:USERNAME đọc được). HÃY SAO LƯU RA USB."
        Write-Host "Khóa công khai (dán vào UpdateKeys.Trusted):"
        Get-Spki (New-Object Security.Cryptography.ECDsaCng($cng))
    }
    'pubkey' {
        Get-Spki (Read-PrivateKey $Key)
    }
    'sign' {
        foreach ($n in 'Key', 'Installer', 'Version', 'Url', 'Out') { if (-not (Get-Variable $n -ValueOnly)) { throw "Thiếu -$n" } }
        [void][Version]::Parse($Version)
        if (-not $Url.StartsWith('https://')) { Write-Warning 'Nên dùng link https://' }
        $ecdsa = Read-PrivateKey $Key
        $pub = Get-Spki $ecdsa
        if ((Get-TrustedKeys) -notcontains $pub) {
            Write-Warning 'Khóa này KHÔNG có trong UpdateKeys.Trusted của mã nguồn hiện tại – máy khách sẽ từ chối bản cập nhật.'
        }
        $file = Get-Item $Installer
        $manifest = [ordered]@{
            product   = 'ConNgoan'
            version   = $Version
            url       = $Url
            sha256    = Get-Sha256Hex $file.FullName
            size      = [long]$file.Length
            published = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
            notes     = $(if ($Notes) { $Notes } else { $null })
        }
        $json = $manifest | ConvertTo-Json
        $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes($json)
        New-Item -ItemType Directory -Force $Out | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $Out 'version.json'), $bytes)
        $sig = $ecdsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256)   # định dạng IEEE P1363 (r||s)
        [IO.File]::WriteAllText((Join-Path $Out 'version.json.sig'), [Convert]::ToBase64String($sig))
        Write-Host ("Đã ký phiên bản {0} ({1:N1} MB, SHA-256 {2})." -f $Version, ($file.Length / 1MB), $manifest.sha256)
        Write-Host "Đưa lên website: version.json + version.json.sig (cùng thư mục) và bộ cài tại $Url"
    }
    'verify' {
        if (-not $Out) { throw 'Thiếu -Out (thư mục chứa version.json)' }
        $bytes = [IO.File]::ReadAllBytes((Join-Path $Out 'version.json'))
        $sig = [Convert]::FromBase64String((Get-Content (Join-Path $Out 'version.json.sig') -Raw).Trim())
        $ok = $false
        foreach ($k in Get-TrustedKeys) {
            if ((Import-Spki $k).VerifyData($bytes, $sig, [Security.Cryptography.HashAlgorithmName]::SHA256)) { $ok = $true }
        }
        if (-not $ok) { Write-Host 'KHÔNG HỢP LỆ: chữ ký không khớp khóa công khai trong app.' -ForegroundColor Red; exit 1 }
        $m = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
        Write-Host "Chữ ký hợp lệ: $($m.product) $($m.version), $($m.size) byte, $($m.url)" -ForegroundColor Green
        if ($Installer) {
            if ((Get-Item $Installer).Length -ne $m.size -or (Get-Sha256Hex $Installer) -ne $m.sha256.ToLowerInvariant()) {
                Write-Host 'BỘ CÀI KHÔNG KHỚP SHA-256/kích thước.' -ForegroundColor Red; exit 1
            }
            Write-Host 'Bộ cài khớp SHA-256.' -ForegroundColor Green
        }
    }
}
