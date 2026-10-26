param([Parameter(Mandatory)][string]$PublishDirectory)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $PublishDirectory).Path
foreach ($name in @('AIResetRadar.App.exe','AIResetRadar.App.dll','AIResetRadar.App.runtimeconfig.json','github-app.json','coreclr.dll','Microsoft.UI.Xaml.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) { throw "Missing self-contained publish file: $name" }
}
$entries = @(Get-ChildItem -LiteralPath $source -Recurse -Force)
$files = @($entries | Where-Object { -not $_.PSIsContainer })
foreach ($file in $entries) {
    $relative = [System.IO.Path]::GetRelativePath($source, $file.FullName)
    if ($relative -match '(^|[\\/])(Credentials|Profiles|\.git)([\\/]|$)|(^|[\\/])(auth\.json|radar\.db.*|\.env.*)$|\.(pfx|pem|key)$') { throw 'The publish folder contains local state or credential material; packaging stopped.' }
    if ($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw 'A publish file is a reparse point; packaging stopped.' }
}
$releaseDirectory = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts/releases'))
New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null
$destination = Join-Path $releaseDirectory 'AITokenScrooge-0.1.0-win-x64-preview.zip'
if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory($source, $destination, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$archive = [System.IO.Compression.ZipFile]::OpenRead($destination)
try {
    foreach ($name in @('AIResetRadar.App.exe','AIResetRadar.App.dll','github-app.json','coreclr.dll','Microsoft.UI.Xaml.dll')) {
        if ($archive.Entries.FullName -notcontains $name) { throw "The archive is missing $name" }
    }
    if (@($archive.Entries | Where-Object { $_.Name.Length -gt 0 }).Count -ne $files.Count) { throw 'Archive and publish file counts differ.' }
    $count = $archive.Entries.Count
}
finally { $archive.Dispose() }
$manifest = [ordered]@{
    Package = $destination
    SHA256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    ManagedAssemblySHA256 = (Get-FileHash -LiteralPath (Join-Path $source 'AIResetRadar.App.dll') -Algorithm SHA256).Hash
    Files = $count
    Runtime = (Get-Content -LiteralPath (Join-Path $source 'AIResetRadar.App.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks
    Signed = $false
    Acceptance = 'Preview only; live account, final native UI, lifecycle, and clean-user acceptance remain open.'
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseDirectory 'AITokenScrooge-0.1.0-win-x64-preview.manifest.json')
$manifest | ConvertTo-Json -Depth 5
