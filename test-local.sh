#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TASK_DLL="$SCRIPT_DIR/src/CycloneDX.MSBuildTask/bin/Release/net8.0/CycloneDX.MSBuildTask.dll"

usage() {
    echo "Usage: $0 <path-to-project-or-solution> [extra dotnet build args...]"
    echo ""
    echo "Builds the CycloneDX MSBuild task locally, then builds the target project"
    echo "using the local task DLL so you can inspect the generated SBOM."
    echo ""
    echo "Examples:"
    echo "  $0 ../my-app/MyApp.csproj"
    echo "  $0 ../my-app/MyApp.sln -c Release"
    echo "  $0 ../my-app/src/MyApp/MyApp.csproj -r linux-x64"
    exit 1
}

if [[ $# -lt 1 ]]; then
    usage
fi

TARGET="$1"
shift

echo "==> Building CycloneDX.MSBuildTask (Release)..."
dotnet build "$SCRIPT_DIR/src/CycloneDX.MSBuildTask" -c Release -v q

echo "==> Building target project with local task..."
echo "    Project: $TARGET"
echo "    Task DLL: $TASK_DLL"
dotnet build "$TARGET" -p:CycloneDxMSBuildTaskAssembly="$TASK_DLL" "$@"

echo ""
echo "==> Done. Look for bom.json / bom.xml in the project output directory."
