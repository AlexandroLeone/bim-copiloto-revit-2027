param(
    [ValidateSet('2027')][string]$RevitVersion = '2027',
    [string]$ArtifactDirectory = (Join-Path $PSScriptRoot 'dist\release')
)
$ErrorActionPreference = 'Stop'
$addinId = '2D3A5F8A-4A3C-4A78-9B5D-6D6E7E3D1B10'
$source = Join-Path $ArtifactDirectory 'BIMCopiloto.dll'
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw 'BIMCopiloto.dll não encontrado. Execute build.ps1 ou use o pacote compilado.'
}
$identity = [Reflection.AssemblyName]::GetAssemblyName($source)
if ($identity.Name -ne 'BIMCopiloto' -or $identity.Version.Major -ne 2) {
    throw 'A DLL não corresponde ao BIM Copiloto v2.'
}
$targetRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$manifest = Join-Path $targetRoot 'ChatGPTRevit.addin'
New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
# Never overwrite another add-in, and do not create duplicate registrations.
foreach ($existing in Get-ChildItem -LiteralPath $targetRoot -Filter '*.addin' -File) {
    try { [xml]$xml = [IO.File]::ReadAllText($existing.FullName) } catch { continue }
    $matching = @($xml.RevitAddIns.AddIn | Where-Object { $_.AddInId -eq $addinId })
    if ($matching.Count -gt 0 -and $existing.FullName -ne $manifest) {
        throw "Já existe outro manifesto deste plugin: $($existing.FullName)"
    }
}
if (Test-Path -LiteralPath $manifest) {
    [xml]$previous = [IO.File]::ReadAllText($manifest)
    $registrations = @($previous.RevitAddIns.AddIn)
    if ($registrations.Count -ne 1 -or $registrations[0].AddInId -ne $addinId) {
        throw 'O manifesto existente pertence a outro plugin; instalação interrompida.'
    }
}
$hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$versionFolder = '2.1.0-' + $hash.Substring(0,12)
$targetDirectory = Join-Path $targetRoot ("BIMCopiloto\" + $versionFolder)
New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
foreach ($name in @('BIMCopiloto.dll','BIMCopiloto.deps.json','BIMCopiloto.pdb')) {
    $from = Join-Path $ArtifactDirectory $name
    $to = Join-Path $targetDirectory $name
    if (-not (Test-Path -LiteralPath $from)) { continue }
    if (Test-Path -LiteralPath $to) {
        if ((Get-FileHash -LiteralPath $from).Hash -eq (Get-FileHash -LiteralPath $to).Hash) { continue }
        throw "Destino já existe com conteúdo diferente: $to"
    }
    Copy-Item -LiteralPath $from -Destination $to
}
$targetDll = Join-Path $targetDirectory 'BIMCopiloto.dll'
if ((Get-FileHash -LiteralPath $targetDll).Hash -ne $hash) { throw 'Falha na verificação da DLL copiada.' }
$backup = $null
if (Test-Path -LiteralPath $manifest) {
    $backupDirectory = Join-Path $targetRoot 'BIMCopiloto\backups'
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    $backup = Join-Path $backupDirectory ('ChatGPTRevit.' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.' + [guid]::NewGuid().ToString('N') + '.addin.bak')
    Copy-Item -LiteralPath $manifest -Destination $backup
}
[xml]$newManifest = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'RevitChatGPT.addin.template'))
$newManifest.SelectSingleNode('/RevitAddIns/AddIn/Assembly').InnerText = [string]$targetDll
$settings = New-Object System.Xml.XmlWriterSettings
$settings.Indent = $true
$settings.Encoding = New-Object System.Text.UTF8Encoding($false)
$tempManifest = Join-Path $targetRoot ('ChatGPTRevit.' + [guid]::NewGuid().ToString('N') + '.tmp')
$writer = [Xml.XmlWriter]::Create($tempManifest, $settings)
try { $newManifest.Save($writer) } finally { $writer.Dispose() }
if (Test-Path -LiteralPath $manifest) { [IO.File]::Replace([string]$tempManifest, [string]$manifest, [string]$backup) }
else { [IO.File]::Move($tempManifest, $manifest) }
Write-Host "Instalado: $targetDll"
Write-Host "SHA256: $hash"
if ($backup) { Write-Host "Manifesto anterior preservado: $backup" }
Write-Host 'Salve seu projeto e reinicie o Revit quando desejar. Aba ChatGPT > BIM Copiloto.'
Write-Host 'Nenhum projeto foi fechado ou salvo pelo instalador. A DLL anterior foi preservada.'
