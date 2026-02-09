#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

echo "==> Cleaning CycloneDX.MSBuildTask..."
rm -rf ~/.nuget/packages/cyclonedx.msbuildtask
rm -f "$REPO_ROOT"/src/CycloneDX.MSBuildTask/bin/Release/*.nupkg
rm -f "$REPO_ROOT"/src/CycloneDX.MSBuildTask/bin/Release/*.snupkg

echo "==> Building and packing CycloneDX.MSBuildTask..."
dotnet build "$REPO_ROOT/src/CycloneDX.MSBuildTask" -c Release /p:Version=0.0.0-local

echo "==> Restoring SampleAppUsingLocalNugetPackage..."
dotnet restore --force

echo "==> Building SampleAppUsingLocalNugetPackage..."
dotnet build -c Release --no-restore

echo "==> Done."
