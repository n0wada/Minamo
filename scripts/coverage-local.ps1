param(
    [string]$Configuration = "Debug"
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
$settings = Join-Path $repoRoot "coverage.runsettings"
$runDirectory = Join-Path $repoRoot ("artifacts\coverage\" + (Get-Date -Format "yyyyMMdd-HHmmss"))

New-Item -ItemType Directory -Force -Path $dotnetHome | Out-Null
New-Item -ItemType Directory -Force -Path $nugetPackages | Out-Null
New-Item -ItemType Directory -Force -Path $userNuGetDir | Out-Null
New-Item -ItemType Directory -Force -Path $localAppData | Out-Null
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
Copy-Item -Force $nugetConfig $userNuGetConfig

$env:DOTNET_CLI_HOME = $dotnetHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = "0"
$env:NUGET_PACKAGES = $nugetPackages
$env:APPDATA = $appData
$env:LOCALAPPDATA = $localAppData

dotnet test $testProject `
    -c $Configuration `
    --collect "XPlat Code Coverage" `
    --settings $settings `
    --results-directory $runDirectory `
    -m:1 `
    -p:BuildInParallel=false `
    -p:RestoreConfigFile=$nugetConfig

if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

$coverageFile = Get-ChildItem $runDirectory -Recurse -Filter "coverage.cobertura.xml" |
    Select-Object -First 1
if ($null -eq $coverageFile)
{
    throw "Coverlet did not produce coverage.cobertura.xml."
}

[xml]$coverage = Get-Content -Raw -Encoding UTF8 $coverageFile.FullName
$classes = @($coverage.coverage.packages.package.classes.class)
$focusAreas = [ordered]@{
    "Parser" = '(^|[\\/])Parser([\\/]|$)'
    "Compiler" = '(^|[\\/])Compiler([\\/]|$)'
    "Runtime/Interop" = '(^|[\\/])Runtime([\\/]|$)'
    "Hosting" = '(^|[\\/])Hosting([\\/]|$)'
    "Linker/FileLookup" = '(^|[\\/])Linker([\\/]|$)'
}

function Get-Rate([int]$covered, [int]$total)
{
    if ($total -eq 0) { return "n/a" }
    return ("{0:N1}%" -f (100.0 * $covered / $total))
}

$rows = foreach ($entry in $focusAreas.GetEnumerator())
{
    $areaClasses = @($classes | Where-Object { $_.filename -match $entry.Value })
    $lines = @($areaClasses | ForEach-Object { $_.lines.line })
    $coveredLines = @($lines | Where-Object { [int]$_.hits -gt 0 }).Count
    $branchCovered = 0
    $branchTotal = 0

    foreach ($line in $lines | Where-Object { $_.branch -eq "true" })
    {
        if ($line.'condition-coverage' -match '\((\d+)/(\d+)\)')
        {
            $branchCovered += [int]$Matches[1]
            $branchTotal += [int]$Matches[2]
        }
    }

    [pscustomobject]@{
        Area = $entry.Key
        Lines = Get-Rate $coveredLines $lines.Count
        Branches = Get-Rate $branchCovered $branchTotal
        CoveredLines = "$coveredLines/$($lines.Count)"
        CoveredBranches = "$branchCovered/$branchTotal"
    }
}

$summary = @(
    "# Coverage summary"
    ""
    "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')"
    ""
    "| Area | Line coverage | Branch coverage | Lines | Branches |"
    "| --- | ---: | ---: | ---: | ---: |"
)
$summary += $rows | ForEach-Object {
    "| $($_.Area) | $($_.Lines) | $($_.Branches) | $($_.CoveredLines) | $($_.CoveredBranches) |"
}
$summary += @(
    ""
    "Cobertura: $($coverageFile.FullName)"
)

$summaryPath = Join-Path $runDirectory "coverage-summary.md"
$summary | Set-Content -Encoding UTF8 $summaryPath
$summary | ForEach-Object { Write-Host $_ }
