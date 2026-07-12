param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "Spellkit\Spellkit.csproj"
$props = [xml](Get-Content -Raw -Encoding UTF8 (Join-Path $repoRoot "Directory.Build.props"))
$version = $props.SelectSingleNode("//SpellkitVersion").InnerText
$runRoot = Join-Path $repoRoot ("artifacts\package-smoke\" + (Get-Date -Format "yyyyMMdd-HHmmss"))
$packageDirectory = Join-Path $runRoot "packages"
$consumerDirectory = Join-Path $runRoot "consumer"
$consumerPackages = Join-Path $runRoot "nuget"
$dotnetHome = Join-Path $runRoot "dotnet-home"
$appData = Join-Path $runRoot "appdata"
$localAppData = Join-Path $runRoot "localappdata"
$package = Join-Path $packageDirectory "Spellkit.$version.nupkg"
$nugetConfig = Join-Path $consumerDirectory "NuGet.Config"

New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $consumerDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $dotnetHome | Out-Null
New-Item -ItemType Directory -Force -Path $appData | Out-Null
New-Item -ItemType Directory -Force -Path $localAppData | Out-Null

$env:DOTNET_CLI_HOME = $dotnetHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = "0"
$env:APPDATA = $appData
$env:LOCALAPPDATA = $localAppData

dotnet pack $project `
    -c $Configuration `
    --no-restore `
    -o $packageDirectory `
    -m:1 `
    -p:BuildInParallel=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try
{
    $entries = @($archive.Entries.FullName)
    foreach ($required in @(
        "lib/net10.0/Spellkit.dll",
        "analyzers/dotnet/cs/Spellkit.Generators.dll",
        "spellkit.png",
        "README.md"))
    {
        if ($required -notin $entries)
        {
            throw "Package is missing required entry '$required'."
        }
    }
}
finally
{
    $archive.Dispose()
}

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Spellkit" Version="$version" />
  </ItemGroup>
</Project>
"@ | Set-Content -Encoding UTF8 (Join-Path $consumerDirectory "Consumer.csproj")

@'
using Spellkit.Hosting;

var commands = new SampleCommands();
var host = new SpellkitHost().AddModule(commands);
using var instance = host.CreateInstance(commands);
var result = instance.Execute("import sample\nsample.Add(20, 22)");
return result.Success && result.GetValue<long>() == 42 ? 0 : 1;

[SpellkitModule("sample")]
public sealed class SampleCommands
{
    [SpellkitCommand]
    public long Add(long left, long right) => left + right;
}
'@ | Set-Content -Encoding UTF8 (Join-Path $consumerDirectory "Program.cs")

@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$packageDirectory" />
  </packageSources>
</configuration>
"@ | Set-Content -Encoding UTF8 $nugetConfig

dotnet restore (Join-Path $consumerDirectory "Consumer.csproj") `
    --configfile $nugetConfig `
    --packages $consumerPackages
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet run `
    --project (Join-Path $consumerDirectory "Consumer.csproj") `
    -c $Configuration `
    --no-restore `
    -p:RestorePackagesPath=$consumerPackages
exit $LASTEXITCODE
