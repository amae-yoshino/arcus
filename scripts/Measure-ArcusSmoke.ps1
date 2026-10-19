#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExecutablePath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1, 5)][int]$Samples = 3
)
$ErrorActionPreference = 'Stop'
$arcusExe = [IO.Path]::GetFullPath($ExecutablePath)
$arcusOutput = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($arcusOutput) | Out-Null
$arcusResults = @()
for ($arcusSample = 1; $arcusSample -le $Samples; $arcusSample++) {
    $arcusSampleDirectory = Join-Path $arcusOutput "sample-$arcusSample"
    $arcusStart = [Diagnostics.ProcessStartInfo]::new($arcusExe)
    $arcusStart.UseShellExecute = $false
    $arcusStart.CreateNoWindow = $true
    $arcusStart.ArgumentList.Add('--smoke')
    $arcusStart.ArgumentList.Add($arcusSampleDirectory)
    $arcusWatch = [Diagnostics.Stopwatch]::StartNew()
    $arcusProcess = [Diagnostics.Process]::Start($arcusStart)
    try {
        if (-not $arcusProcess.WaitForExit(25000)) { $arcusProcess.Kill($true); throw 'Smoke deadline exceeded.' }
        $arcusWatch.Stop()
        $arcusReport = Get-Content -LiteralPath (Join-Path $arcusSampleDirectory 'desktop-smoke.json') -Raw | ConvertFrom-Json
        if ($arcusProcess.ExitCode -ne 0 -or $arcusReport.Status -ne 'Success') { throw 'Smoke verification failed.' }
        $arcusResults += [pscustomobject]@{ Sample = $arcusSample; ElapsedMs = [Math]::Round($arcusWatch.Elapsed.TotalMilliseconds); FolderCount = $arcusReport.FolderCount }
    } finally { $arcusProcess.Dispose() }
}
$arcusSorted = @($arcusResults.ElapsedMs | Sort-Object)
$arcusSummary = [ordered]@{
    Measurement = 'Process launch through own-dialog bar rendering, folder snapshot, PNG capture, and exit; includes 500ms smoke sampling cadence. Not tray-only or cold-boot latency.'
    Executable = $arcusExe
    Samples = $arcusResults
    MedianMs = $arcusSorted[[int][Math]::Floor($arcusSorted.Count / 2)]
}
$arcusSummary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $arcusOutput 'timing.json') -Encoding utf8NoBOM
$arcusResults | Format-Table
Write-Output "Median: $($arcusSummary.MedianMs) ms"
