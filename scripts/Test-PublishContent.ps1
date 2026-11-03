param([Parameter(Mandatory)][string]$PublishDirectory)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $PublishDirectory).Path
if ((Get-Item -LiteralPath $source -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
    throw 'The publish directory is redirected; packaging stopped.'
}
# Walk manually so a redirected directory is rejected before traversal.
$pending = [System.Collections.Generic.Stack[string]]::new()
$pending.Push($source)
while ($pending.Count -gt 0) {
    foreach ($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
        if ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
            throw 'A publish entry is redirected; packaging stopped.'
        }
        if ($entry.Name -match '^(Credentials|Profiles|ClaudeProfiles|\.git)$|^(auth\.json|\.credentials\.json|\.env)(\..*)?$|^radar\.db.*$|\.(pfx|pem|key)$') {
            throw 'The publish folder contains local state or credential material; packaging stopped.'
        }
        if ($entry.PSIsContainer) { $pending.Push($entry.FullName); continue }
        if ($entry.Extension -in @('.json','.txt','.log','.toml','.ini','.yaml','.yml','.xml','.config')) {
            # Stream text rather than loading unbounded files. Never print matching content.
            $reader = [System.IO.File]::OpenText($entry.FullName)
            try { while ($null -ne ($line = $reader.ReadLine())) {
                if ($line -match '(?i)["'']?(access_token|refresh_token|client_secret|OPENAI_API_KEY|ANTHROPIC_API_KEY|oauthToken)["'']?\s*[:=]\s*["'']?[^\s"'',}\]]+' -or
                    $line -match '(gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|sk-ant-[A-Za-z0-9_-]{20,}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----)') {
                    throw 'Potential credential content found in publish output; packaging stopped.'
                }
            } } finally { $reader.Dispose() }
        }
    }
}
