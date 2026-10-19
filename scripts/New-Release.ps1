#Requires -Version 7.0
[CmdletBinding()]
param([string]$OutputRoot)
$ErrorActionPreference = 'Stop'
$arcusRoot = Split-Path -Parent $PSScriptRoot
$arcusProjectPath = Join-Path $arcusRoot 'src/arcus/arcus.csproj'
$arcusProject = [xml](Get-Content -LiteralPath $arcusProjectPath -Raw)
$arcusVersion = [string]$arcusProject.Project.PropertyGroup.Version
if (-not $OutputRoot) { $OutputRoot = Join-Path $arcusRoot 'artifacts/github-releases' }
$arcusOutput = [IO.Path]::GetFullPath($OutputRoot)
$arcusName = "arcus-$arcusVersion-win-x64"
$arcusPackage = Join-Path $arcusOutput $arcusName
$arcusPublish = Join-Path $arcusOutput "$arcusName-publish"
$arcusZip = Join-Path $arcusOutput "$arcusName.zip"
$arcusChecksums = Join-Path $arcusOutput "$arcusName.sha256"
foreach ($arcusTarget in @($arcusPackage,$arcusPublish,$arcusZip,$arcusChecksums)) {
    if (Test-Path -LiteralPath $arcusTarget) { throw "Output already exists: $arcusTarget. Nothing was overwritten." }
}
if (-not (Test-Path -LiteralPath (Join-Path $arcusRoot 'LICENSE'))) { throw 'LICENSE is required to package a release.' }
dotnet publish $arcusProjectPath -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:UseSharedCompilation=false -m:1 -nr:false -o $arcusPublish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
[IO.Directory]::CreateDirectory($arcusPackage) | Out-Null
Copy-Item -LiteralPath (Join-Path $arcusPublish 'arcus.exe') -Destination $arcusPackage
foreach ($arcusFile in @('README.txt','README.en.txt','更新履歴.txt')) { Copy-Item -LiteralPath (Join-Path $arcusRoot "docs/distribution/$arcusFile") -Destination $arcusPackage }
foreach ($arcusFile in @('LICENSE','THIRD_PARTY_NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $arcusRoot $arcusFile) -Destination $arcusPackage }
Compress-Archive -LiteralPath $arcusPackage -DestinationPath $arcusZip
$arcusHash = (Get-FileHash -LiteralPath $arcusZip -Algorithm SHA256).Hash.ToLowerInvariant()
($arcusHash + '  ' + [IO.Path]::GetFileName($arcusZip)) | Set-Content -LiteralPath $arcusChecksums -Encoding ascii
Write-Output "Release candidate: $arcusZip"
Write-Output "SHA-256: $arcusChecksums"
Write-Output 'Review the ZIP contents and SHA-256 before uploading. Mark beta releases as pre-releases on GitHub.'
