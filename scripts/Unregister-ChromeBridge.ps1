$ErrorActionPreference = 'Stop'
$registry = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.aitokenscrooge.billing'
if (Test-Path -LiteralPath $registry) { Remove-Item -LiteralPath $registry }
Write-Output 'Unregistered the Chrome bridge. Remove Scrooge API Credits from Chrome to stop browser monitoring. Browser sign-ins are unchanged.'
