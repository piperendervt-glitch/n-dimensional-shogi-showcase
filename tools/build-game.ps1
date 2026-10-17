param(
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.3.11f1\Editor\Unity.exe',
    [switch]$NoZip
)
# Builds the 4D shogi game (Cfg2 scene, NDShogiPivot.exe) into release\NDShogiPivot.
# The previous release folder is replaced only after Unity reports success.
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = Join-Path $repoRoot 'release'
$gameFolder = Join-Path $releaseRoot 'NDShogiPivot'
$zipPath = Join-Path $releaseRoot 'NDShogiPivot-Windows.zip'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$staging = Join-Path $releaseRoot ('.staging-' + $stamp)
$stagedGame = Join-Path $staging 'NDShogiPivot'
$logFolder = Join-Path $releaseRoot 'logs'
$logPath = Join-Path $logFolder ('build-' + $stamp + '.log')
# Cfg2Build embeds the hashes of these two documents into the player identity.
$request = Join-Path $repoRoot 'docs\history\PIVOT-RESTART-20261017\00_RESTART_REQUEST_JA.md'
$design = Join-Path $repoRoot 'docs\history\PIVOT-DESIGN-20261017\00_DESIGN_JA.md'

if (-not (Test-Path $UnityEditor)) { throw ('Unity Editor not found: ' + $UnityEditor) }
New-Item -ItemType Directory -Force -Path $logFolder | Out-Null
$unityArguments = @('-batchmode', '-quit', '-projectPath', ('"' + (Join-Path $repoRoot 'Unity') + '"'),
    '-executeMethod', 'Cfg2Build.BuildWindows', '-logFile', ('"' + $logPath + '"'),
    '--cfg2-output', ('"' + (Join-Path $stagedGame 'NDShogiPivot.exe') + '"'),
    '--cfg2-run-root', ('"' + $releaseRoot + '"'),
    '--pivot-request', ('"' + $request + '"'),
    '--pivot-design', ('"' + $design + '"'),
    '--pivot-request-id', 'PIVOT-RESTART-20261017-083955-JST')
Write-Host 'Building NDShogiPivot (Unity batchmode, several minutes) ...'
$process = Start-Process -FilePath $UnityEditor -ArgumentList $unityArguments -PassThru -WindowStyle Hidden
Write-Host ('Unity PID ' + $process.Id + '; log: ' + $logPath)
$process.WaitForExit()
$reportPath = Join-Path $stagedGame 'build-report.json'
if ($process.ExitCode -ne 0 -or -not (Test-Path $reportPath)) {
    throw ('Unity exited with ' + $process.ExitCode + '. Inspect ' + $logPath)
}
$report = Get-Content -Raw -Encoding UTF8 $reportPath | ConvertFrom-Json
if ($report.result -ne 'Succeeded' -or $report.errors -ne 0) { throw ('Build failed: ' + $report.result + '. Inspect ' + $logPath) }

# Debug symbols are not needed to play.
Get-ChildItem -Path $stagedGame -Directory -Filter '*_DoNotShip' | Remove-Item -Recurse -Force
if (Test-Path $gameFolder) { Remove-Item -Recurse -Force $gameFolder }
Move-Item $stagedGame $gameFolder
Remove-Item -Recurse -Force $staging
if (-not $NoZip) {
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Compress-Archive -Path $gameFolder -DestinationPath $zipPath
}
Write-Host ('Completed: ' + (Join-Path $gameFolder 'NDShogiPivot.exe') + ' (buildGuid ' + $report.buildGuid + ')')
