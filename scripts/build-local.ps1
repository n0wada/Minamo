param(
    [string]$Configuration = "Debug",
    [string]$Project = "Spellkit.sln"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnetHome = Join-Path $repoRoot ".dotnet-home"
$nugetPackages = Join-Path $repoRoot ".nuget\packages"
$nugetConfig = Join-Path $repoRoot "NuGet.Config"
$appData = Join-Path $repoRoot ".appdata"
$localAppData = Join-Path $repoRoot ".localappdata"
$userNuGetDir = Join-Path $appData "NuGet"
$userNuGetConfig = Join-Path $userNuGetDir "NuGet.Config"
$projectPath = Join-Path $repoRoot $Project

New-Item -ItemType Directory -Force -Path $dotnetHome | Out-Null
New-Item -ItemType Directory -Force -Path $nugetPackages | Out-Null
New-Item -ItemType Directory -Force -Path $userNuGetDir | Out-Null
New-Item -ItemType Directory -Force -Path $localAppData | Out-Null
Copy-Item -Force $nugetConfig $userNuGetConfig

$env:DOTNET_CLI_HOME = $dotnetHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = "0"
$env:NUGET_PACKAGES = $nugetPackages
$env:APPDATA = $appData
$env:LOCALAPPDATA = $localAppData

dotnet build $projectPath -c $Configuration --configfile $nugetConfig -m:1 -p:BuildInParallel=false

if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

if ($Configuration -eq "Release")
{
    $consoleProject = Join-Path $repoRoot "Spellkit.Console\Spellkit.Console.csproj"
    $releaseOutput = Join-Path $repoRoot "bin"

    dotnet restore $consoleProject `
        --configfile $nugetConfig `
        -m:1 `
        -p:BuildInParallel=false

    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    dotnet publish $consoleProject `
        -c Release `
        --self-contained false `
        --no-restore `
        -o $releaseOutput `
        -p:DebugType=None `
        -m:1 `
        -p:BuildInParallel=false

    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    foreach ($fileName in @(
        "Spellkit.deps.json",
        "Spellkit.pdb",
        "Spellkit.Generators.deps.json",
        "Spellkit.Generators.pdb",
        "spell.pdb"))
    {
        $file = Join-Path $releaseOutput $fileName
        if (Test-Path -LiteralPath $file)
        {
            Remove-Item -LiteralPath $file -Force
        }
    }
}
