[CmdletBinding()]
param(
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$repoRootFull = [IO.Path]::GetFullPath($repoRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$publishDirectory = [IO.Path]::GetFullPath((Join-Path $repoRootFull 'artifacts\publish\win-x64'))
if (-not $publishDirectory.StartsWith($repoRootFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe publish directory: $publishDirectory"
}
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetPath = if ($dotnetCommand) {
    $dotnetCommand.Source
} else {
    Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
}

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    throw '.NET 8 SDK not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0'
}

Push-Location $repoRoot
try {
    & $dotnetPath test '.\TypelessSwitch.sln' --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

    if (Test-Path -LiteralPath $publishDirectory) {
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }
    & $dotnetPath publish '.\src\TypelessSwitch.App\TypelessSwitch.App.csproj' `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --property:PublishProfile=win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    if (-not $SkipInstaller) {
        $isccCommand = Get-Command iscc.exe -ErrorAction SilentlyContinue
        $isccCandidates = @(
            $(if ($isccCommand) { $isccCommand.Source }),
            $(if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }),
            $(if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' })
        ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
        $isccPath = $isccCandidates | Select-Object -First 1
        if (-not $isccPath) {
            throw 'Inno Setup 6 not found. Install JRSoftware.InnoSetup with winget, or use -SkipInstaller.'
        }
        & $isccPath '.\installer\TypelessSwitch.iss'
        if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
    }
}
finally {
    Pop-Location
}
