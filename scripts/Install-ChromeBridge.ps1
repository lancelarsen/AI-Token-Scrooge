param([Parameter(Mandatory)][string]$HostDirectory)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $HostDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $source 'AIResetRadar.ChromeHost.exe'))) { throw 'Publish the Chrome host before installing.' }
$manifestSource = Join-Path $PSScriptRoot '../extensions/scrooge-api-credits/manifest.json'
$extension = Get-Content -LiteralPath $manifestSource -Raw | ConvertFrom-Json
$hash = [System.Security.Cryptography.SHA256]::HashData([Convert]::FromBase64String($extension.key))
$extensionId = -join ([Convert]::ToHexString($hash[0..15]).ToLower().ToCharArray() | ForEach-Object { [char](97 + [Convert]::ToInt32($_.ToString(), 16)) })
$destination = Join-Path $env:LOCALAPPDATA 'Programs/AI Token Scrooge/ChromeBridge'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $destination -Recurse -Force
$manifest = Join-Path $destination 'com.aitokenscrooge.billing.json'
@{name='com.aitokenscrooge.billing';description='AI Token Scrooge API Credit Bridge';path=(Join-Path $destination 'AIResetRadar.ChromeHost.exe');type='stdio';allowed_origins=@("chrome-extension://$extensionId/")} |
    ConvertTo-Json | Set-Content -LiteralPath $manifest -Encoding utf8NoBOM
$registry = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.aitokenscrooge.billing'
New-Item -Path $registry -Force | Out-Null
Set-Item -Path $registry -Value $manifest
Write-Output 'Installed the current-user Chrome bridge. Load the unpacked Scrooge API Credits extension in Chrome next.'
