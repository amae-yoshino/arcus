#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory, [ValidateRange(1, 4)][int]$Samples = 2)
$ErrorActionPreference = 'Stop'
$arcusRoot = Split-Path -Parent $PSScriptRoot
$arcusExe = Join-Path $arcusRoot 'src/DialogNavigator/bin/Release/net10.0-windows/DialogNavigator.exe'
$arcusOutput = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($arcusOutput) | Out-Null
$arcusRows = @()
foreach ($arcusKind in @('open-nav', 'save-nav')) {
    for ($arcusSample = 1; $arcusSample -le $Samples; $arcusSample++) {
        $arcusReport = Join-Path $arcusOutput "$arcusKind-$arcusSample.json"
        $arcusStart = [Diagnostics.ProcessStartInfo]::new($arcusExe)
        $arcusStart.UseShellExecute = $false
        $arcusStart.CreateNoWindow = $true
        $arcusStart.WorkingDirectory = $arcusRoot
        foreach ($arcusArg in @('--harness', $arcusKind, $arcusReport)) { $arcusStart.ArgumentList.Add($arcusArg) }
        $arcusProcess = [Diagnostics.Process]::Start($arcusStart)
        try {
            if (-not $arcusProcess.WaitForExit(25000)) { $arcusProcess.Kill($true); throw 'Navigation harness timed out' }
            $arcusTiming = Get-Content -LiteralPath ($arcusReport + '.timing.json') -Raw | ConvertFrom-Json
            if ($arcusTiming.Status -ne 'Success') {
                Get-Content -LiteralPath $arcusReport -Raw | Write-Output
                throw 'Navigation/filename/type verification failed'
            }
            $arcusRows += [pscustomobject]@{ Kind = $arcusKind; Sample = $arcusSample; WorkerRoundTripMs = [Math]::Round($arcusTiming.WorkerRoundTripMs, 1) }
        } finally { $arcusProcess.Dispose() }
    }
}
ConvertTo-Json -InputObject $arcusRows | Set-Content -LiteralPath (Join-Path $arcusOutput 'timing.json') -Encoding utf8NoBOM
$arcusRows | Format-Table
