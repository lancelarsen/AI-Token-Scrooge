param(
    [string]$Repository = $env:GITHUB_REPOSITORY,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Repository)) { throw 'Repository is required.' }

$json = gh api "repos/$Repository/releases?per_page=100" --paginate --slurp
if ($LASTEXITCODE -ne 0) { throw 'Could not list releases.' }
$releases = @((($json -join "`n") | ConvertFrom-Json) | ForEach-Object { $_ } | Where-Object {
    $_.prerelease -and -not $_.draft -and $_.tag_name -match '^(preview-\d+-\d+|v\d{4}\.\d{1,2}\.\d{1,2}\.\d{3,5})$'
} | Sort-Object { [DateTimeOffset]$_.published_at } -Descending)

if ($releases.Count -eq 0) { Write-Host 'No Windows previews found.'; return }
$keep = $releases[0]
if (@($keep.assets | Where-Object { $_.name -like '*.zip' -and $_.state -eq 'uploaded' -and $_.size -gt 0 }).Count -eq 0) {
    throw 'Newest preview has no uploaded ZIP. Keeping all existing releases.'
}
Write-Host "Keeping $($keep.tag_name)"
foreach ($release in ($releases | Select-Object -Skip 1)) {
    if ($DryRun) { Write-Host "Would remove $($release.tag_name)"; continue }
    # Calendar-version tags reserve sequence numbers permanently, independently of release retention.
    if ($release.tag_name -like 'preview-*') { gh release delete $release.tag_name --repo $Repository --yes --cleanup-tag }
    else { gh release delete $release.tag_name --repo $Repository --yes }
    if ($LASTEXITCODE -ne 0) { throw "Could not delete $($release.tag_name)." }
    Write-Host "Removed $($release.tag_name)"
}
