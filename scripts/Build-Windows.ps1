param([switch]$Package, [string]$PublishDirectory = 'artifacts/publish/win-x64', [string]$BuildVersion = $env:SCROOGE_VERSION)
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    if (-not $BuildVersion) {
        $central = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTimeOffset]::UtcNow, 'Central Standard Time')
        $BuildVersion = '{0}.{1:D2}.{2}.000' -f $central.Year, $central.Month, $central.Day
    }
    if ($BuildVersion -notmatch '^\d{4}\.\d{1,2}\.\d{1,2}\.\d{3,5}$') { throw 'Invalid build version.' }
    dotnet test AIResetRadar.slnx --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    dotnet publish src/AIResetRadar.App/AIResetRadar.App.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true "-p:ScroogeVersion=$BuildVersion" -o $PublishDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed. Exit any running copy from the publish folder before rebuilding.' }
    dotnet publish tools/AIResetRadar.Launcher/AIResetRadar.Launcher.csproj -c Release -r win-x64 --self-contained true -o (Join-Path $PublishDirectory 'Launcher') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed.' }
    $commit = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not identify build commit.' }
    @{ Version = "v$BuildVersion"; Commit = $commit; UpdateProtocol = 1 } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PublishDirectory 'build-info.json')
    if ($Package) {
        & (Join-Path $PSScriptRoot 'Package-Windows.ps1') -PublishDirectory $PublishDirectory
    }
}
finally { Pop-Location }
