# Downloads the BASS libraries from un4seen.com into native\<platform>\.
# Usage (PowerShell):  .\scripts\fetch-bass.ps1 [-Platform win|osx|linux|all]   (default: win)
# BASS is free for non-commercial use; commercial use needs a license from un4seen.
param([ValidateSet('win', 'osx', 'linux', 'all')][string]$Platform = 'win')
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$libs = 'bass', 'bassmix', 'bassenc', 'bassenc_mp3', 'bassenc_ogg', 'bassflac'

function Get-Zip($name, $dest) {
    foreach ($base in 'https://www.un4seen.com/files/', 'https://www.un4seen.com/files/z/2/') {
        try { Invoke-WebRequest -Uri "$base$name.zip" -OutFile $dest -UseBasicParsing; return } catch { }
    }
    throw "Could not download $name.zip"
}

function Fetch($plat) {
    switch ($plat) {
        'win'   { $suffix = '';       $dest = 'win-x64';   $pattern = 'x64\*.dll' }
        'osx'   { $suffix = '-osx';   $dest = 'osx';       $pattern = '*.dylib' }
        'linux' { $suffix = '-linux'; $dest = 'linux-x64'; $pattern = 'libs\x86_64\*.so' }
    }
    $list = $libs
    if ($plat -eq 'linux') { $list += 'bass_aac' }   # AAC/M4A: Windows and macOS use the OS decoder
    $out = Join-Path $root "native\$dest"
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid())
    New-Item -ItemType Directory -Path $tmp | Out-Null
    try {
        foreach ($lib in $list) {
            Write-Host "↓ ${lib}24$suffix.zip"
            $zip = Join-Path $tmp "$lib.zip"
            Get-Zip "${lib}24$suffix" $zip
            Expand-Archive -Path $zip -DestinationPath (Join-Path $tmp $lib) -Force
            Copy-Item (Join-Path (Join-Path $tmp $lib) $pattern) $out -Force
        }
    } finally { Remove-Item -Recurse -Force $tmp }
    Write-Host "→ $out"
}

if ($Platform -eq 'all') { 'linux', 'osx', 'win' | ForEach-Object { Fetch $_ } } else { Fetch $Platform }
