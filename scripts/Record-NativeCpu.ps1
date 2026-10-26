param([ValidateRange(10, 120)][int]$Seconds = 60)
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Windows CPU recording requires administrator approval.'
}
$folder = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts/native-qa'))
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$output = Join-Path $folder ('native-cpu-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.etl')
$started = $false
try {
    # Do not cancel an existing recording. A failed start leaves it untouched.
    & wpr.exe -start CPU -filemode
    if ($LASTEXITCODE -ne 0) { throw 'CPU recording could not start; any existing recording was left untouched.' }
    $started = $true
    Start-Sleep -Seconds $Seconds
    & wpr.exe -stop $output
    if ($LASTEXITCODE -ne 0) { throw 'CPU recording could not be saved.' }
    $started = $false
    [pscustomobject]@{ Trace = $output; Seconds = $Seconds; Profile = 'CPU'; Scope = 'System-wide CPU activity' } |
        ConvertTo-Json | Set-Content -LiteralPath ($output + '.json')
}
finally {
    if ($started) { & wpr.exe -cancel }
}
