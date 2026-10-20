param([switch]$SkipDownload, [switch]$Test)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$toolsFolder = Join-Path $PSScriptRoot 'tools'
$distributionFolder = Join-Path $PSScriptRoot 'dist\Luma'
New-Item -ItemType Directory -Force -Path $toolsFolder,$distributionFolder | Out-Null
$archive = Join-Path $toolsFolder 'ffmpeg-release-essentials.zip'
if (-not $SkipDownload) {
    & curl.exe -fL --retry 3 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' -o $archive
    if ($LASTEXITCODE -ne 0) { throw 'Could not download FFmpeg.' }
    & curl.exe -fL --retry 3 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip.sha256' -o "$archive.sha256"
    if ($LASTEXITCODE -ne 0) { throw 'Could not download FFmpeg checksum.' }
}
if (-not (Test-Path -LiteralPath $archive)) { throw 'FFmpeg archive is missing in tools.' }
if (Test-Path -LiteralPath "$archive.sha256") {
    $expected = ([System.IO.File]::ReadAllText("$archive.sha256").Trim() -split '\s+')[0]
    $actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    if ($actual -ne $expected) { throw 'FFmpeg archive checksum mismatch.' }
}
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $toolsFolder 'ffmpeg') -Force
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'scripts\New-Icon.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Could not generate the icon.' }
& dotnet publish src\Luma.csproj -c Release -r win-x64 --self-contained true -o $distributionFolder
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
$ffmpegRoot = Get-ChildItem -LiteralPath (Join-Path $toolsFolder 'ffmpeg') -Directory | Select-Object -First 1
Copy-Item -LiteralPath (Join-Path $ffmpegRoot.FullName 'bin\ffmpeg.exe') -Destination $distributionFolder
Copy-Item -LiteralPath (Join-Path $ffmpegRoot.FullName 'bin\ffprobe.exe') -Destination $distributionFolder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\Assets\luma.ico') -Destination $distributionFolder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md'),(Join-Path $PSScriptRoot 'LICENSE'),(Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $distributionFolder
Copy-Item -LiteralPath (Join-Path $env:USERPROFILE '.nuget\packages\naudio\2.2.1\license.txt') -Destination (Join-Path $distributionFolder 'NAUDIO-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $ffmpegRoot.FullName 'LICENSE') -Destination (Join-Path $distributionFolder 'FFMPEG-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $ffmpegRoot.FullName 'README.txt') -Destination (Join-Path $distributionFolder 'FFMPEG-README.txt')
$manifest = [ordered]@{
    App = 'Luma 2.5.0'
    Target = 'Windows x64'
    FFmpegArchive = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
    FFmpegArchiveSHA256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    FFmpegBinarySHA256 = (Get-FileHash -LiteralPath (Join-Path $distributionFolder 'ffmpeg.exe') -Algorithm SHA256).Hash
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $distributionFolder 'build-info.json') -Encoding UTF8
if ($Test) {
    & (Join-Path $PSScriptRoot 'scripts\Verify-Release.ps1') -BinaryRoot $distributionFolder
}
# Keep test captures outside the portable archive.
$packageFiles = Get-ChildItem -LiteralPath $distributionFolder -File | Where-Object { $_.Extension -ne '.pdb' }
Compress-Archive -LiteralPath $packageFiles.FullName -DestinationPath (Join-Path $PSScriptRoot 'dist\Luma-Windows-x64.zip') -Force
Write-Output "Ready: $distributionFolder\Luma.exe"
