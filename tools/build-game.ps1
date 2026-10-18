param(
    [string]$UnityEditor = '',
    [switch]$NoZip
)
# Builds the N-dimensional shogi game (Cfg2 scene, NDShogi.exe) into release\NDShogi with its notices, and the zip to hand out.
# The previous release folder and zip are replaced only after Unity reports success,
# and the swap is rename-based so a running or locked old build is never half deleted.
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = Join-Path $repoRoot 'release'
$gameFolder = Join-Path $releaseRoot 'NDShogi'
$zipPath = Join-Path $releaseRoot 'NDShogi-Windows.zip'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$staging = Join-Path $releaseRoot ('.staging-' + $stamp)
$stagedGame = Join-Path $staging 'NDShogi'
$previous = Join-Path $releaseRoot ('.previous-' + $stamp)
$logFolder = Join-Path $releaseRoot 'logs'
$logPath = Join-Path $logFolder ('build-' + $stamp + '.log')
$recordFolder = Join-Path $logFolder ('build-' + $stamp)
# Cfg2Build embeds the hashes of these two documents into the player identity.
$request = Join-Path $repoRoot 'docs\history\PIVOT-RESTART-20261017\00_RESTART_REQUEST_JA.md'
$design = Join-Path $repoRoot 'docs\history\PIVOT-DESIGN-20261017\00_DESIGN_JA.md'

if (-not $UnityEditor) {
    # Use the editor version the project was saved with.
    $versionFile = Join-Path $repoRoot 'Unity\ProjectSettings\ProjectVersion.txt'
    $version = (Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value
    $UnityEditor = Join-Path $env:ProgramFiles ('Unity\Hub\Editor\' + $version + '\Editor\Unity.exe')
}
if (-not (Test-Path $UnityEditor)) { throw ('Unity Editor not found: ' + $UnityEditor) }
foreach ($required in @($request, $design)) {
    if (-not (Test-Path $required)) { throw ('Build input missing: ' + $required) }
}
New-Item -ItemType Directory -Force -Path $logFolder | Out-Null
$keepStaging = $false
try {
    $unityArguments = @('-batchmode', '-quit', '-projectPath', ('"' + (Join-Path $repoRoot 'Unity') + '"'),
        '-executeMethod', 'Cfg2Build.BuildWindows', '-logFile', ('"' + $logPath + '"'),
        '--cfg2-output', ('"' + (Join-Path $stagedGame 'NDShogi.exe') + '"'),
        '--cfg2-run-root', ('"' + $releaseRoot + '"'),
        '--pivot-request', ('"' + $request + '"'),
        '--pivot-design', ('"' + $design + '"'),
        '--pivot-request-id', 'PIVOT-RESTART-20261017-083955-JST')
    Write-Host 'Building NDShogi (Unity batchmode, several minutes) ...'
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
    $reportPath = Join-Path $stagedGame 'build-report.json'
    if ($exitCode -ne 0 -or -not (Test-Path $reportPath)) {
        throw ('Unity exited with ' + $exitCode + '. Inspect ' + $logPath)
    }
    $report = Get-Content -Raw -Encoding UTF8 $reportPath | ConvertFrom-Json
    if ($report.result -ne 'Succeeded' -or $report.errors -ne 0) { throw ('Build failed: ' + $report.result + '. Inspect ' + $logPath) }

    # Build records hold absolute paths of this PC; keep them in release\logs instead of the shipped folder.
    New-Item -ItemType Directory -Force -Path $recordFolder | Out-Null
    foreach ($record in @('build-report.json', 'embedded-identity.json', 'source-manifest.json')) {
        $path = Join-Path $stagedGame $record
        if (Test-Path $path) { Move-Item $path $recordFolder }
    }
    # Debug symbols are not needed to play.
    Get-ChildItem -Path $stagedGame -Directory -Filter '*_DoNotShip' | Remove-Item -Recurse -Force
    # The zip goes out on its own, so it carries the licence, the third-party notices and a short readme.
    Copy-Item (Join-Path $repoRoot 'LICENSE') (Join-Path $stagedGame 'LICENSE.txt')
    Copy-Item (Join-Path $PSScriptRoot 'release\README.txt') (Join-Path $stagedGame 'README.txt')
    Copy-Item (Join-Path $PSScriptRoot 'release\THIRD_PARTY_NOTICES.txt') (Join-Path $stagedGame 'THIRD_PARTY_NOTICES.txt')
    $notices = Join-Path $stagedGame 'Notices\PolyHaven'
    New-Item -ItemType Directory -Force -Path $notices | Out-Null
    Copy-Item (Join-Path $repoRoot 'assets\source\polyhaven\NOTICE.txt') $notices
    Copy-Item (Join-Path $repoRoot 'assets\source\polyhaven\provenance.json') $notices

    if (-not $NoZip) {
        $stagedZip = Join-Path $staging 'NDShogi-Windows.zip'
        Compress-Archive -Path $stagedGame -DestinationPath $stagedZip
    }
    # Rename first: if the old game is running, this fails cleanly and leaves it untouched.
    if (Test-Path $gameFolder) {
        try { Rename-Item $gameFolder (Split-Path $previous -Leaf) }
        catch {
            $keepStaging = $true
            throw ('The previous build is in use. Close NDShogi.exe; the new build is kept in ' + $stagedGame + '. ' + $_.Exception.Message)
        }
    }
    Move-Item $stagedGame $gameFolder
    if (-not $NoZip) { Move-Item -Force $stagedZip $zipPath }
    if (Test-Path $previous) { Remove-Item -Recurse -Force $previous }
    Write-Host ('Completed: ' + (Join-Path $gameFolder 'NDShogi.exe') + ' (buildGuid ' + $report.buildGuid + ')')
}
finally {
    if (-not $keepStaging -and (Test-Path $staging)) { Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue }
}
