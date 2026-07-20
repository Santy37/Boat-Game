# Rebuilds the island stage (with the shoreline fix, enemy variants, and
# chest variants), repairs NGO scene identities, re-captures the preview
# image, and runs the full EditMode test suite.
#
# IMPORTANT: The Unity editor must be CLOSED before running this script.
#
# Usage:  powershell -ExecutionPolicy Bypass -File .\rebuild-island.ps1

$ErrorActionPreference = "Stop"
$unity = "C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe"
$project = $PSScriptRoot

if (Get-Process Unity -ErrorAction SilentlyContinue) {
    Write-Error "Close the Unity editor first - the project is locked while it is open."
}

function Invoke-Unity([string[]]$UnityArgs) {
    # Unity is a GUI executable: launch it and WAIT for it to exit,
    # otherwise $LASTEXITCODE is checked before the build even starts.
    $process = Start-Process -FilePath $unity -ArgumentList $UnityArgs `
        -PassThru -Wait -NoNewWindow
    return $process.ExitCode
}

Write-Host "[1/3] Rebuilding island stage (shorelines, enemy variants, chests)..."
$code = Invoke-Unity @(
    "-batchmode", "-quit",
    "-projectPath", "`"$project`"",
    "-executeMethod", "IslandStageBuilder.BuildAllFromCommandLine",
    "-logFile", "`"$project\Logs\rebuild-island.log`""
)
if ($code -ne 0) {
    Write-Error "Island rebuild FAILED (exit $code) - see Logs\rebuild-island.log"
}

Write-Host "[2/3] Capturing island preview..."
$code = Invoke-Unity @(
    "-batchmode", "-quit",
    "-projectPath", "`"$project`"",
    "-executeMethod", "IslandStageBuilder.CapturePreviewFromCommandLine",
    "-logFile", "`"$project\Logs\rebuild-preview.log`""
)
if ($code -ne 0) {
    Write-Warning "Preview capture failed (exit $code) - see Logs\rebuild-preview.log"
}

Write-Host "[3/3] Running EditMode tests..."
$code = Invoke-Unity @(
    "-batchmode",
    "-projectPath", "`"$project`"",
    "-runTests", "-testPlatform", "EditMode",
    "-testResults", "`"$project\Logs\rebuild-test-results.xml`"",
    "-logFile", "`"$project\Logs\rebuild-tests.log`""
)

$results = [xml](Get-Content "$project\Logs\rebuild-test-results.xml")
$run = $results.'test-run'
Write-Host ""
Write-Host "Tests: $($run.result)  passed=$($run.passed) failed=$($run.failed) total=$($run.total)"
if ($run.failed -ne "0") {
    $results.SelectNodes("//test-case[@result='Failed']") |
        ForEach-Object { Write-Host "  FAILED: $($_.name)" }
    exit 1
}

Write-Host "All good. Check Logs\codex-island-preview.png for the new island look."
