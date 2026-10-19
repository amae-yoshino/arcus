#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('windows', 'collect:Explorer', 'collect:Files', 'collect:OneCommander', 'collect:Filedini', 'tree', 'dialogs', 'dialog')]
    [string]$Operation = 'windows',
    [long]$Window = 0,
    [uint32]$ProcessId = 0,
    [string]$OutputPath,
    [ValidateRange(1, 60)][int]$TimeoutSeconds = 15
)
$ErrorActionPreference = 'Stop'
$arcusRoot = Split-Path -Parent $PSScriptRoot
$arcusExecutable = Join-Path $arcusRoot 'src/FolderInspector/bin/Release/net10.0-windows/FolderInspector.exe'
if (-not (Test-Path -LiteralPath $arcusExecutable)) { throw 'Build the Release configuration first.' }
if (-not $OutputPath) {
    $arcusName = $Operation.Replace(':', '-')
    $OutputPath = Join-Path $arcusRoot "docs/evidence/$arcusName-latest.json"
}
$arcusOutput = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($arcusOutput)) | Out-Null
$arcusRequest = Join-Path ([IO.Path]::GetTempPath()) ("arcus-request-" + [guid]::NewGuid().ToString('N') + '.json')
$arcusProcess = $null
try {
    @{ Operation = $Operation; Window = $Window; ProcessId = $ProcessId } |
        ConvertTo-Json | Set-Content -LiteralPath $arcusRequest -Encoding utf8NoBOM
    # UseShellExecute=false + CreateNoWindow keep this read-only helper hidden.
    $arcusStart = [Diagnostics.ProcessStartInfo]::new($arcusExecutable)
    $arcusStart.UseShellExecute = $false
    $arcusStart.CreateNoWindow = $true
    foreach ($arcusArg in @('--worker', $arcusRequest, $arcusOutput)) { $arcusStart.ArgumentList.Add($arcusArg) }
    $arcusProcess = [Diagnostics.Process]::Start($arcusStart)
    if (-not $arcusProcess.WaitForExit($TimeoutSeconds * 1000)) {
        $arcusProcess.Kill($true)
        $arcusProcess.WaitForExit()
        throw "Probe timed out after $TimeoutSeconds seconds."
    }
    if ($arcusProcess.ExitCode -ne 0) { throw "Probe exited $($arcusProcess.ExitCode)." }
    if (-not (Test-Path -LiteralPath $arcusOutput)) { throw 'Probe returned no report.' }
    $arcusReport = Get-Content -LiteralPath $arcusOutput -Raw | ConvertFrom-Json
    $arcusReport | Select-Object Status, Detail
    Write-Output "Report: $arcusOutput"
} finally {
    if ($arcusProcess) { $arcusProcess.Dispose() }
    if (Test-Path -LiteralPath $arcusRequest) { Remove-Item -LiteralPath $arcusRequest }
}
