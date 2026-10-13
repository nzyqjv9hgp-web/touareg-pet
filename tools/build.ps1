param([string]$Version = "1.2.0", [string]$Output = "")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (!$Output) { $Output = Join-Path $root "dist" }
dotnet run --project (Join-Path $root "Checks/Checks.csproj") -c Release
if ($LASTEXITCODE -ne 0) { throw "Checks failed" }
dotnet publish (Join-Path $root "Windows/TouaregPet.csproj") -c Release -r win-x64 --self-contained true "-p:Version=$Version" -o $Output
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Ready: $Output/TouaregPet.exe"
