param(
    [string]$RevitInstallDir = 'C:\Program Files\Autodesk\Revit 2027',
    [string]$DotnetPath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist\release')
)
$ErrorActionPreference = 'Stop'
if (-not $DotnetPath) {
    $candidate = Join-Path $env:LOCALAPPDATA 'Codex\dotnet-sdk-10\dotnet.exe'
    if (Test-Path -LiteralPath $candidate) { $DotnetPath = $candidate }
    else { $DotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
}
if (-not (Test-Path -LiteralPath (Join-Path $RevitInstallDir 'RevitAPI.dll'))) {
    throw 'Revit 2027 não encontrado. Informe -RevitInstallDir.'
}
& $DotnetPath build (Join-Path $PSScriptRoot 'src\ChatGPTRevit.csproj') -c Release "-p:RevitInstallDir=$RevitInstallDir" -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw "Compilação falhou: $LASTEXITCODE" }
Write-Host "Compilado: $(Join-Path $OutputDirectory 'BIMCopiloto.dll')"
