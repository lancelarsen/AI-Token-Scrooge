param([string]$Repository = $env:GITHUB_REPOSITORY)
$ErrorActionPreference = 'Stop'
if (-not $env:GITHUB_SHA -or -not $env:GITHUB_ENV) { throw 'Version reservation must run in GitHub Actions.' }
$central = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTimeOffset]::UtcNow, 'Central Standard Time')
$prefix = 'v{0}.{1:D2}.{2}.' -f $central.Year, $central.Month, $central.Day
$tags = @(git ls-remote --tags origin "refs/tags/$prefix*")
if ($LASTEXITCODE -ne 0) { throw 'Could not read reserved build versions.' }
$numbers = @($tags | ForEach-Object {
    if ($_ -match ('refs/tags/' + [regex]::Escape($prefix) + '(\d+)$')) { [int]$Matches[1] }
})
$next = 1
if ($numbers.Count -gt 0) { $next = ($numbers | Measure-Object -Maximum).Maximum + 1 }
if ($next -gt 65534) { throw 'Daily build version exhausted.' }
$tag = $prefix + ('{0:D3}' -f [int]$next)
# Reserve before building. Keep this tag even if the build fails or its release is removed.
git push origin "$($env:GITHUB_SHA):refs/tags/$tag"
if ($LASTEXITCODE -ne 0) { throw 'Build version reservation failed. Do not reuse an existing tag.' }
"SCROOGE_VERSION=$($tag.Substring(1))" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8
"SCROOGE_TAG=$tag" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8
Write-Host "Reserved $tag"
