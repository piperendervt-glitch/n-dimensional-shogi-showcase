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
if (-not (Test-Path $UnityEditor)) { throw ('Unity Editor not found: ' + $UnityEditor) }
# Start-Process and WaitForExit can report a null ExitCode; Process.Start keeps the handle.
$startInfo = New-Object Diagnostics.ProcessStartInfo
$startInfo.FileName = $UnityEditor
$startInfo.Arguments = $unityArguments -join ' '
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$process = [Diagnostics.Process]::Start($startInfo)
Write-Host ('Unity PID ' + $process.Id + '; log: ' + $logPath)
$process.WaitForExit()
$exitCode = $process.ExitCode
if ($exitCode -ne 0) { throw ('Unity exited with ' + $exitCode + '. Inspect ' + $logPath) }
Write-Host ('Completed: ' + $method)
