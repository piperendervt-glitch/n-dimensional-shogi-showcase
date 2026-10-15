#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Executable = (Join-Path $PSScriptRoot '../builds/Showcase-Windows/NDShogiShowcase.exe'),
    [Parameter(Mandatory=$true)][string]$OutputRoot,
    [ValidateSet('Benchmark','Capture','Automation','Smoke','ShapeComparison','LightingAudit')][string]$RunKind = 'Benchmark',
    [ValidateSet('DirectedLive','VerifiedReplay')][string]$Mode = 'DirectedLive',
    [ValidateSet('ShowcaseRaster','GeometryReference')][string]$Profile = 'ShowcaseRaster',
    [ValidateSet('lattice-line/1','supercover-closed/1')][string]$Occlusion = 'lattice-line/1',
    [ValidateSet('A','C')][string]$Shape = 'C',
    [int]$Width = 1920,
    [int]$Height = 1080,
    [ValidateRange(1,20)][int]$Loops = 1,
    [ValidateRange(0.01,20)][double]$Speed = 1,
    [ValidateRange(0.25,10)][double]$SampleSeconds = 1,
    [ValidateRange(10,3600)][int]$TimeoutSeconds = 600,
    [switch]$CaptureVideo,
    [switch]$CapturePasses,
    [switch]$HelpersOff
)
$ErrorActionPreference = 'Stop'
if($Width -lt 320 -or $Height -lt 240){throw 'Resolution is invalid.'}
if($RunKind -eq 'Benchmark' -and ($CaptureVideo -or $CapturePasses)){throw 'Clean benchmark cannot enable capture.'}
if($RunKind -eq 'Automation' -and $Loops -lt 3){throw 'Playback automation requires -Loops 3 or more.'}
$taskExe = [System.IO.Path]::GetFullPath($Executable)
if(-not (Test-Path -LiteralPath $taskExe -PathType Leaf)){throw "Executable not found: $taskExe"}
$taskOutput = [System.IO.Path]::GetFullPath($OutputRoot)
$taskProcessName = [System.IO.Path]::GetFileNameWithoutExtension($taskExe)
if(Get-Process -Name $taskProcessName -ErrorAction SilentlyContinue){throw "An existing $taskProcessName process is running. No overlapping benchmark was started."}
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
if(Test-Path -LiteralPath (Join-Path $taskOutput 'launcher.json')){throw 'OutputRoot already contains a launcher record; choose a new output directory to preserve evidence.'}
$taskArgs = [System.Collections.Generic.List[string]]::new()
foreach($value in @('-screen-fullscreen','0','-screen-width',"$Width",'-screen-height',"$Height",'--profile',$Profile,'--mode',$Mode,'--occlusion',$Occlusion,'--shape',$Shape,'--loops',"$Loops",'--speed',$Speed.ToString([Globalization.CultureInfo]::InvariantCulture),'--evidence',$taskOutput,'-logFile',(Join-Path $taskOutput 'player.log'))){$taskArgs.Add([string]$value)}
if($RunKind -eq 'Benchmark'){$taskArgs.Add('--benchmark-no-captures')}
if($RunKind -eq 'ShapeComparison'){$taskArgs.Add('--shape-comparison')}
if($RunKind -eq 'LightingAudit'){$taskArgs.Add('--lighting-audit')}
if($CaptureVideo){$taskArgs.Add('--capture-video')}
if($CapturePasses){$taskArgs.Add('--capture-passes')}
if($HelpersOff){$taskArgs.Add('--helpers-off')}
if($RunKind -eq 'Automation'){
    foreach($value in @('--automation-playback','--automation-evidence',(Join-Path $taskOutput 'automation'),'--automation-exit')){$taskArgs.Add($value)}
}else{$taskArgs.Add('--exit-after-run')}
$taskGpuClass = 'Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory'
$taskGpuAvailable = $true
$taskGpuAvailabilityError = $null
try{Get-CimClass -Namespace root/cimv2 -ClassName $taskGpuClass | Out-Null}catch{$taskGpuAvailable=$false;$taskGpuAvailabilityError=$_.Exception.Message}
$taskDrivers = @(Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,DriverDate,PNPDeviceID)
$taskMeta = [ordered]@{
    schema='showcase-validation-launch/1';runKind=$RunKind;executable=$taskExe;
    executableSha256=(Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash.ToLowerInvariant();
    arguments=$taskArgs.ToArray();outputRoot=$taskOutput;width=$Width;height=$Height;mode=$Mode;profile=$Profile;occlusion=$Occlusion;
    loops=$Loops;speed=$Speed;sampleSeconds=$SampleSeconds;timeoutSeconds=$TimeoutSeconds;
    gpuCounterClass=$taskGpuClass;gpuCounterAvailable=$taskGpuAvailable;gpuCounterAvailabilityError=$taskGpuAvailabilityError;
    gpuCounterMeaning='Per-process WDDM DedicatedUsage/SharedUsage/TotalCommitted gauges, summed across unique adapter instances. Not a claim of exact instantaneous physical VRAM residency.';
    gpuAggregation='explicit Int64 sum of unique PSCustomObject adapter records /2';
    processMemoryMeaning='PrivateMemorySize64 and WorkingSet64 occupancy gauges, not allocated bytes/sec.';
    measurementOverhead='CIM and process sampling run during playback; each sample records query duration. These measurements are not zero-overhead.';
    windowStyle='Normal';visibilityReason='The target GUI application needs a visible active graphics swapchain for rendering, framebuffer captures and pass verification; it is not a background helper.';
    drivers=$taskDrivers;processId=$null;startedUtc=$null;endedUtc=$null;exitCode=$null;status='STARTING';samples=0;
    memoryFile='process-memory.jsonl';osKeyboardInjection=$false
}
$taskUtf8 = [Text.UTF8Encoding]::new($false)
function Write-TaskMetadata {
    [IO.File]::WriteAllText((Join-Path $taskOutput 'launcher.json'),($taskMeta | ConvertTo-Json -Depth 12),$taskUtf8)
}
Write-TaskMetadata
$taskStartInfo = [Diagnostics.ProcessStartInfo]::new()
$taskStartInfo.FileName=$taskExe
$taskStartInfo.WorkingDirectory=[IO.Path]::GetDirectoryName($taskExe)
$taskStartInfo.UseShellExecute=$false
$taskStartInfo.CreateNoWindow=$false
$taskStartInfo.WindowStyle=[Diagnostics.ProcessWindowStyle]::Normal
foreach($argument in $taskArgs){$taskStartInfo.ArgumentList.Add($argument)}
$taskClock=[Diagnostics.Stopwatch]::StartNew()
$taskProcess=[Diagnostics.Process]::Start($taskStartInfo)
$taskProcessId=$taskProcess.Id
$taskMeta.processId=$taskProcessId
$taskMeta.startedUtc=[DateTime]::UtcNow.ToString('O')
$taskMeta.status='RUNNING'
Write-TaskMetadata
$taskMemoryWriter=[IO.StreamWriter]::new((Join-Path $taskOutput 'process-memory.jsonl'),$false,$taskUtf8)
$taskTimedOut=$false
try{
    while(-not $taskProcess.HasExited){
        if($taskClock.Elapsed.TotalSeconds -gt $TimeoutSeconds){
            $taskTimedOut=$true
            # Only this invocation's child PID is terminated; never another running application.
            Stop-Process -Id $taskProcessId -ErrorAction SilentlyContinue
            break
        }
        $taskQueryClock=[Diagnostics.Stopwatch]::StartNew()
        $taskPrivate=$null;$taskWorking=$null;$taskCpuSeconds=$null;$taskProcessError=$null
        try{$taskProcess.Refresh();$taskPrivate=$taskProcess.PrivateMemorySize64;$taskWorking=$taskProcess.WorkingSet64;$taskCpuSeconds=$taskProcess.TotalProcessorTime.TotalSeconds}catch{$taskProcessError=$_.Exception.Message}
        $taskInstances=@();$taskGpuError=$null
        if($taskGpuAvailable){
            try{
                $taskInstances=@(Get-CimInstance -ClassName $taskGpuClass | Where-Object {$_.Name -match ('^pid_'+$taskProcessId+'_')} |
                    Sort-Object Name -Unique | ForEach-Object {[pscustomobject][ordered]@{name=$_.Name;dedicatedBytes=[long]$_.DedicatedUsage;sharedBytes=[long]$_.SharedUsage;committedBytes=[long]$_.TotalCommitted}})
            }catch{$taskGpuError=$_.Exception.Message}
        }
        $taskDedicated=$null;$taskShared=$null;$taskCommitted=$null
        if($taskInstances.Count -gt 0){
            $taskDedicated=[long]0;$taskShared=[long]0;$taskCommitted=[long]0
            foreach($taskInstance in $taskInstances){
                $taskDedicated += [long]$taskInstance.dedicatedBytes
                $taskShared += [long]$taskInstance.sharedBytes
                $taskCommitted += [long]$taskInstance.committedBytes
            }
        }
        $taskQueryClock.Stop()
        $taskSample=[ordered]@{recordedUtc=[DateTime]::UtcNow.ToString('O');wallSeconds=$taskClock.Elapsed.TotalSeconds;processId=$taskProcessId;
            privateBytes=$taskPrivate;workingSetBytes=$taskWorking;processCpuSeconds=$taskCpuSeconds;
            gpuDedicatedBytes=$taskDedicated;gpuSharedBytes=$taskShared;gpuCommittedBytes=$taskCommitted;
            gpuCounterStatus=$(if($taskInstances.Count -gt 0){'MEASURED'}elseif($taskGpuError){'QUERY_ERROR'}elseif($taskGpuAvailable){'NO_PID_INSTANCE'}else{'NOT_AVAILABLE'});
            gpuInstances=$taskInstances;queryMilliseconds=$taskQueryClock.Elapsed.TotalMilliseconds;gpuError=$taskGpuError;processError=$taskProcessError}
        $taskMemoryWriter.WriteLine(($taskSample | ConvertTo-Json -Depth 8 -Compress));$taskMemoryWriter.Flush()
        $taskMeta.samples++
        $taskNextSample=$taskMeta.samples*$SampleSeconds
        $taskSleepMs=[Math]::Min(10000,[Math]::Max(1,($taskNextSample-$taskClock.Elapsed.TotalSeconds)*1000))
        Start-Sleep -Milliseconds ([int]$taskSleepMs)
    }
    $taskProcess.WaitForExit()
    $taskMeta.exitCode=$taskProcess.ExitCode
    $taskMeta.status=$(if($taskTimedOut){'TIMEOUT_TERMINATED'}elseif($taskProcess.ExitCode -eq 0){'EXITED_ZERO'}else{'EXITED_NONZERO'})
}catch{
    $taskMeta.status='LAUNCHER_ERROR'
    $taskMeta['error']=$_.Exception.ToString()
    throw
}finally{
    if($taskMeta.status -eq 'LAUNCHER_ERROR' -and -not $taskProcess.HasExited){Stop-Process -Id $taskProcessId -ErrorAction SilentlyContinue}
    $taskMemoryWriter.Dispose();$taskMeta.endedUtc=[DateTime]::UtcNow.ToString('O');Write-TaskMetadata
    $taskProcess.Dispose()
}
$taskMeta | ConvertTo-Json -Depth 6
if($taskTimedOut -or $taskMeta.exitCode -ne 0){exit 1}
