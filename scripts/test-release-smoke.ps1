param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$console = Join-Path $repoRoot "bin\spk.dll"
$station = Join-Path $repoRoot "bin\examples\StationConsole\$Configuration\Spellkit.Examples.StationConsole.dll"
$workflow = Join-Path $repoRoot "bin\projects\OrderWorkflow\$Configuration\Spellkit.Examples.OrderWorkflow.dll"
$smokeDirectory = Join-Path $repoRoot "artifacts\release-smoke"
$languageExamples = Join-Path $repoRoot "Examples\Language"
$source = Join-Path $smokeDirectory "smoke test.kit"

& (Join-Path $PSScriptRoot "build-local.ps1") -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot "generate-recipes.ps1") -Check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

New-Item -ItemType Directory -Force -Path $smokeDirectory | Out-Null
"print(40 + 2)" | Set-Content -Encoding UTF8 $source

$help = & dotnet $console --help 2>&1
if ($LASTEXITCODE -ne 0 -or ($help -join "`n") -notmatch "Usage: spk")
{
    throw "Spellkit console help smoke test failed."
}

$version = & dotnet $console --version 2>&1
if ($LASTEXITCODE -ne 0 -or ($version -join "`n") -notmatch '^spk ')
{
    throw "Spellkit console version smoke test failed."
}

$execution = & dotnet $console $source -nologo 2>&1
if ($LASTEXITCODE -ne 0 -or ($execution -join "`n") -notmatch '42')
{
    throw "Spellkit source execution smoke test failed."
}

foreach ($example in Get-ChildItem $languageExamples -Filter "*.kit" -File | Sort-Object Name)
{
    & dotnet $console $example.FullName -nologo | Out-Null
    if ($LASTEXITCODE -ne 0)
    {
        throw "Spellkit language recipe '$($example.Name)' failed."
    }
}

$stationOutput = & dotnet $station 2>&1
if ($LASTEXITCODE -ne 0)
{
    throw "Station Console smoke test failed."
}

$stationText = $stationOutput -join "`n"
foreach ($expected in @(
    "Startup automation: OK",
    "Emergency signal: OK",
    "reactor=85%",
    "locked=[engineering]"))
{
    if ($stationText.IndexOf($expected, [System.StringComparison]::Ordinal) -lt 0)
    {
        throw "Station Console output is missing '$expected'."
    }
}

$workflowOutput = & dotnet $workflow 2>&1
if ($LASTEXITCODE -ne 0)
{
    throw "Order Workflow smoke test failed."
}

$workflowText = $workflowOutput -join "`n"
foreach ($expected in @(
    "Load workflow: OK",
    "Shipment requested: OK",
    "ORD-1001: shipped via courier (express)",
    "ORD-1002: not accepted"))
{
    if ($workflowText.IndexOf($expected, [System.StringComparison]::Ordinal) -lt 0)
    {
        throw "Order Workflow output is missing '$expected'."
    }
}
