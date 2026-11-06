$ErrorActionPreference = 'Stop'
$root = Join-Path ([System.IO.Path]::GetTempPath()) ('ScroogePackageTest-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    Set-Content (Join-Path $root 'github-app.json') '{"clientId":"public-client-id"}'
    & "$PSScriptRoot/Test-PublishContent.ps1" -PublishDirectory $root
    $cases = @(
        @{ Name = 'DevinProfiles'; Content = '' },
        @{ Name = 'DevinTrials'; Content = '' },
        @{ Name = '.credentials.json'; Content = '{}' },
        @{ Name = '.credentials.json.bak'; Content = '{}' },
        @{ Name = 'auth.json.tmp'; Content = '{}' },
        @{ Name = 'radar.db-wal'; Content = '{}' },
        @{ Name = 'unexpected.json'; Content = '{"refresh_token":"synthetic-sentinel-not-a-real-token"}' },
        @{ Name = 'unexpected.log'; Content = 'access_token=synthetic-sentinel-not-a-real-token' }
    )
    foreach ($case in $cases) {
        $path = Join-Path $root $case.Name
        Set-Content -LiteralPath $path -Value $case.Content
        $rejected = $false
        try { & "$PSScriptRoot/Test-PublishContent.ps1" -PublishDirectory $root }
        catch { $rejected = $true; if ($_.Exception.Message.Contains('synthetic-sentinel')) { throw 'Error leaked a credential.' } }
        if (-not $rejected) { throw "Fixture was not rejected: $($case.Name)" }
        Remove-Item -LiteralPath $path
    }
    Write-Output 'Publish-content checks passed: public client ID accepted; eight secret/state fixtures rejected.'
}
finally {
    $resolved = [System.IO.Path]::GetFullPath($root)
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path $resolved -Leaf) -notlike 'ScroogePackageTest-*') { throw 'Unsafe fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
