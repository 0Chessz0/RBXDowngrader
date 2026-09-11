param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$artifacts = Join-Path $repo 'artifacts'
$work = Join-Path $artifacts 'package-work'
$appPublish = Join-Path $work 'app'
$uninstallerPublish = Join-Path $work 'uninstaller'
$payloadDirectory = Join-Path $work 'payload'
$payloadZip = Join-Path $repo 'src\RBXDowngrader.Installer\Payload.zip'

& (Join-Path $repo 'tools\New-AppIcon.ps1')

if (Test-Path $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Force $appPublish, $uninstallerPublish, $payloadDirectory, $artifacts | Out-Null

$payloadPublishProperties = @(
    '-c', 'Release', '-r', $Runtime,
    '--self-contained', 'true',
    '-p:PublishSingleFile=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false'
)

dotnet publish (Join-Path $repo 'src\RBXDowngrader.App\RBXDowngrader.App.csproj') @payloadPublishProperties -o $appPublish
if ($LASTEXITCODE -ne 0) { throw 'The RBXDowngrader app publish failed.' }
dotnet publish (Join-Path $repo 'src\RBXDowngrader.Uninstaller\RBXDowngrader.Uninstaller.csproj') @payloadPublishProperties -o $uninstallerPublish
if ($LASTEXITCODE -ne 0) { throw 'The uninstaller publish failed.' }

Copy-Item -Path (Join-Path $appPublish '*') -Destination $payloadDirectory -Recurse -Force
Copy-Item -Path (Join-Path $uninstallerPublish '*') -Destination $payloadDirectory -Recurse -Force

if (Test-Path $payloadZip) { Remove-Item -LiteralPath $payloadZip -Force }
Compress-Archive -Path (Join-Path $payloadDirectory '*') -DestinationPath $payloadZip -CompressionLevel Optimal

$installerPublishProperties = @(
    '-c', 'Release', '-r', $Runtime,
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false'
)
dotnet publish (Join-Path $repo 'src\RBXDowngrader.Installer\RBXDowngrader.Installer.csproj') @installerPublishProperties -o (Join-Path $work 'installer')
if ($LASTEXITCODE -ne 0) { throw 'The installer publish failed.' }
Copy-Item -LiteralPath (Join-Path $work 'installer\RBXDowngraderSetup.exe') -Destination (Join-Path $artifacts 'RBXDowngraderSetup.exe') -Force

Remove-Item -LiteralPath $payloadZip -Force
Remove-Item -LiteralPath $work -Recurse -Force
Write-Host "Created $artifacts\RBXDowngraderSetup.exe"
