param(
    [string]$Configuration = "Debug",
    [string]$TestPath = "Spellkit.UnitTests\Tests",
    [string]$Region,
    [ValidateRange(0.001, 86400)]
    [double]$TimeoutSeconds = 10,
    [ValidateSet("All", "Language", "Pipeline", "Hosting", "Generator", "Security")]
    [string]$Suite = "All"
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
$testProject = Join-Path $repoRoot "Spellkit.UnitTests\Spellkit.UnitTests.csproj"
$resolvedTestPath = Join-Path $repoRoot $TestPath
$languageTestOutput = Join-Path $repoRoot "bin\tests\language\$Configuration"
$languageTestEntry = Join-Path $languageTestOutput "spk-language-tests.dll"
$testReport = Join-Path $repoRoot "Docs\TestResult.md"
$runtimeWorkingDirectory = Join-Path $repoRoot "bin"

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
$env:DOTNET_ROLL_FORWARD = "Major"

$filter = switch ($Suite)
{
    "Pipeline" { "Suite=Pipeline" }
    "Hosting" { "Suite=Hosting" }
    "Generator" { "Suite=Generator" }
    "Language" { "Suite=Language" }
    "Security" { "Category=Security" }
    default { $null }
}
if ($Suite -eq "Language" -and $Region)
{
    # A region belongs to the standalone corpus runner. Avoid executing every
    # xUnit-wrapped corpus file before reaching the requested reproduction.
    $filter = "Suite=Language&FullyQualifiedName!~LanguageCorpusTests"
}

$testArguments = @(
    "test",
    $testProject,
    "-c", $Configuration,
    "--no-restore",
    "-m:1",
    "-p:BuildInParallel=false",
    "-p:RestoreConfigFile=$nugetConfig"
)
if ($filter)
{
    $testArguments += @("--filter", $filter)
}

& dotnet @testArguments
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

if ($Suite -in @("All", "Language"))
{
    dotnet build $testProject -c $Configuration --configfile $nugetConfig `
        -m:1 -p:BuildInParallel=false -p:LanguageRunner=true

    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    Push-Location $runtimeWorkingDirectory
    $testExitCode = 0
    try
    {
        $languageArguments = @(
            $languageTestEntry,
            $resolvedTestPath,
            "--output", $testReport,
            "--timeout-seconds",
            $TimeoutSeconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)
        )
        if ($Region)
        {
            $languageArguments += @("--region", $Region)
        }

        & dotnet @languageArguments
        $testExitCode = $LASTEXITCODE
    }
    finally
    {
        Pop-Location
    }

    if ($testExitCode -ne 0)
    {
        exit $testExitCode
    }
}
