param([switch]$Package, [string]$PublishDirectory = 'artifacts/publish/win-x64')
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet test AIResetRadar.slnx --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    dotnet publish src/AIResetRadar.App/AIResetRadar.App.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o $PublishDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed. Exit any running copy from the publish folder before rebuilding.' }
    if ($Package) {
        & (Join-Path $PSScriptRoot 'Package-Windows.ps1') -PublishDirectory $PublishDirectory
    }
}
finally { Pop-Location }
