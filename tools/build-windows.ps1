param(
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.3.11f1\Editor\Unity.exe',
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repoRoot 'Unity'
$logFolder = Join-Path $repoRoot 'artifacts\logs'
New-Item -ItemType Directory -Force -Path $logFolder | Out-Null
$method = if ($PrepareOnly) { 'ShowcaseBuild.PrepareProject' } else { 'ShowcaseBuild.BuildWindows' }
$logPath = Join-Path $logFolder $(if ($PrepareOnly) { 'unity-prepare.log' } else { 'unity-build.log' })
$unityArguments = @('-batchmode', '-quit', '-projectPath', ('"' + $projectPath + '"'), '-executeMethod', $method, '-logFile', ('"' + $logPath + '"'))
$process = Start-Process -FilePath $UnityEditor -ArgumentList $unityArguments -PassThru -WindowStyle Hidden
Write-Host ('Unity PID ' + $process.Id + '; log: ' + $logPath)
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw ('Unity exited with ' + $process.ExitCode + '. Inspect ' + $logPath) }
Write-Host ('Completed: ' + $method)
