param(
    [string]$ISCCPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$localRoot = Join-Path $repoRoot '.local'
$projectPath = Join-Path $repoRoot 'src\CodexMascot.App\CodexMascot.App.csproj'
$scriptPath = Join-Path $repoRoot 'installer\AgentMascot.iss'
$project = [xml](Get-Content -LiteralPath $projectPath -Raw)
$version = [string]$project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw 'The app version is missing from the project file.' }

if ([string]::IsNullOrWhiteSpace($ISCCPath)) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $ISCCPath = $command.Source }
}
if ([string]::IsNullOrWhiteSpace($ISCCPath)) {
    foreach ($candidate in @(
        (Join-Path $localRoot 'tools\InnoSetup\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 7\ISCC.exe',
        'C:\Program Files\Inno Setup 7\ISCC.exe'
    )) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { $ISCCPath = $candidate; break }
    }
}
if ([string]::IsNullOrWhiteSpace($ISCCPath) -or !(Test-Path -LiteralPath $ISCCPath -PathType Leaf)) {
    throw 'Inno Setup compiler (ISCC.exe) is not installed. Install Inno Setup 6 or 7, then run this script again.'
}

$toolsDotnet = Join-Path (Join-Path (Split-Path $repoRoot -Parent) '.local') '.dotnet\dotnet.exe'
$dotnetPath = $toolsDotnet
if (!(Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if (!$dotnetCommand) { throw '.NET SDK was not found.' }
    $dotnetPath = $dotnetCommand.Source
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$buildRoot = Join-Path $localRoot ('installer-build\' + $stamp)
$payload = Join-Path $buildRoot 'payload'
$output = Join-Path $localRoot ('distribution\AgentMascot-Installer-' + $version + '-' + $stamp)
New-Item -ItemType Directory -Path $payload -Force | Out-Null
New-Item -ItemType Directory -Path $output -Force | Out-Null

try {
    & $dotnetPath publish $projectPath --configuration Release --runtime win-x64 --self-contained true --output $payload '-p:TargetPlatformDisplayName=Windows'
    if ($LASTEXITCODE -ne 0) { throw "Self-contained publish failed with exit code $LASTEXITCODE." }
    foreach ($document in @('README.md', 'VALIDATION.md', 'THIRD-PARTY-NOTICES.txt', 'LICENSE')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot $document) -Destination $payload
    }

    $arguments = @(
        "-dSourceDir=$payload"
        "-dOutputDir=$output"
        "-dAppVersion=$version"
        "-dSetupIconPath=$(Join-Path $repoRoot 'src\CodexMascot.App\Branding\App.ico')"
        $scriptPath
    )
    & $ISCCPath @arguments
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed with exit code $LASTEXITCODE." }

    $installer = Join-Path $output ("AgentMascot-Setup-$version-win-x64.exe")
    if (!(Test-Path -LiteralPath $installer -PathType Leaf)) { throw 'The installer compiler did not create the expected setup file.' }
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath ($installer + '.sha256.txt') -Value "$hash  $(Split-Path $installer -Leaf)" -Encoding ascii
    Write-Output "Installer: $installer"
    Write-Output "SHA256: $hash"
}
finally {
    $resolvedBuildRoot = [IO.Path]::GetFullPath($buildRoot)
    if ($resolvedBuildRoot.StartsWith([IO.Path]::GetFullPath((Join-Path $localRoot 'installer-build')) + '\', [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedBuildRoot)) {
        Remove-Item -LiteralPath $resolvedBuildRoot -Recurse -Force
    }
}
