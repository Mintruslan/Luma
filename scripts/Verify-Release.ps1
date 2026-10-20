param(
    [string]$BinaryRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\Luma'),
    [string]$ArtifactRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\release-validation')
)
$ErrorActionPreference = 'Stop'
$ArtifactRoot = [System.IO.Path]::GetFullPath($ArtifactRoot)
$app = Join-Path $ArtifactRoot 'app'
New-Item -ItemType Directory -Force -Path $app | Out-Null
Get-ChildItem -LiteralPath $BinaryRoot -File | Copy-Item -Destination $app -Force
$results = @()
foreach ($suite in @('stability', 'audit-fix', 'ux', 'ui', 'visual', 'restore', 'feature', 'self')) {
    $folder = Join-Path $ArtifactRoot $suite
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $arguments = @('--' + $suite + '-test')
    if ($suite -ne 'self') { $arguments += ('"' + $folder + '"') }
    $process = Start-Process -FilePath (Join-Path $app 'Luma.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(240000)) { $process.Kill(); throw "$suite exceeded 240 seconds" }
    if ($suite -eq 'self') { Copy-Item -Path (Join-Path $app 'self-test\*') -Destination $folder -Recurse -Force }
    $lines = if (Test-Path -LiteralPath (Join-Path $folder 'results.txt')) { @(Get-Content -LiteralPath (Join-Path $folder 'results.txt')) } else { @('FAIL: no results file') }
    $passed = @($lines | Where-Object { $_ -like 'PASS:*' }).Count
    $complete = $process.ExitCode -eq 0 -and $lines[-1] -match 'All .*passed'
    $results += [ordered]@{ Suite = $suite; ExitCode = $process.ExitCode; Pass = $passed; Complete = $complete }
    $results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'suite.json') -Encoding UTF8
    Write-Output "$suite exit=$($process.ExitCode) PASS=$passed complete=$complete"
    if (-not $complete) { $lines | Select-Object -Last 8 | Write-Output }
}
Get-FileHash -LiteralPath (Join-Path $app 'Luma.exe') | Select-Object Hash | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactRoot 'tested-binary.json') -Encoding UTF8
if (@($results | Where-Object { -not $_.Complete }).Count -gt 0) { throw 'Release verification failed; inspect suite.json.' }
