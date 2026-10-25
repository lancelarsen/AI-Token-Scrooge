param(
    [string]$Executable = (Join-Path $PSScriptRoot '../artifacts/publish/win-x64/AIResetRadar.App.exe'),
    [int]$SampleSeconds = 15,
    [double]$MaximumCpuSeconds = 1.5
)
$ErrorActionPreference = 'Stop'
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$testData = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts/idle-test'))
$start = [System.Diagnostics.ProcessStartInfo]::new($executablePath)
$start.UseShellExecute = $false
foreach ($argument in @('--demo', '--compact', '--data-dir', $testData)) { $start.ArgumentList.Add($argument) }
$process = [System.Diagnostics.Process]::Start($start)
try {
    Start-Sleep -Seconds 6
    $process.Refresh()
    if ($process.HasExited) { throw 'The test instance exited. Close any existing Radar instance before this isolated test.' }
    $cpuBefore = $process.TotalProcessorTime.TotalSeconds
    $memoryBefore = $process.WorkingSet64
    Start-Sleep -Seconds $SampleSeconds
    $process.Refresh()
    if ($process.HasExited) { throw 'Radar exited during the idle sample.' }
    $cpu = $process.TotalProcessorTime.TotalSeconds - $cpuBefore
    $result = [PSCustomObject]@{
        Executable = $executablePath
        SHA256 = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash
        ManagedAssemblySHA256 = (Get-FileHash -LiteralPath ([System.IO.Path]::ChangeExtension($executablePath, '.dll')) -Algorithm SHA256).Hash
        SampleSeconds = $SampleSeconds
        CpuSeconds = $cpu
        StartingWorkingSetMB = [math]::Round($memoryBefore / 1MB, 1)
        EndingWorkingSetMB = [math]::Round($process.WorkingSet64 / 1MB, 1)
        Passed = ($cpu -le $MaximumCpuSeconds)
    }
    $result | ConvertTo-Json | Tee-Object -FilePath (Join-Path $testData 'idle-result.json')
    if (-not $result.Passed) { throw "Idle CPU budget exceeded: $cpu seconds during a $SampleSeconds-second sample." }
}
finally {
    $process.Refresh()
    if (-not $process.HasExited) { $process.Kill() }
    $process.Dispose()
}
