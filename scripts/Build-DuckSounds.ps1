# Regenerate the app's duck cues from the user-selected audition #02.
# Requires ffmpeg on PATH. Source and license are recorded in SOUND-CREDITS.txt.
$ErrorActionPreference = 'Stop'
$source = Join-Path ([IO.Path]::GetTempPath()) 'scrooge-selected-quack.mp3'
Invoke-WebRequest 'https://cdn.pixabay.com/audio/2022/06/08/audio_98351be2c8.mp3' -OutFile $source
$output = Join-Path $PSScriptRoot '../src/AIResetRadar.App/Resources/Sounds'
# Remove leading/trailing silence, retain the natural quack, and fade edges.
$base = 'atrim=start=0.36:end=0.80,asetpts=PTS-STARTPTS,aformat=channel_layouts=mono,afade=t=in:d=0.008,afade=t=out:st=0.41:d=0.03'
$cues = @{
    'alert' = "$base,apad=pad_dur=0.14,aloop=loop=2:size=27840"
    'snooze' = "$base,asetrate=43200,aresample=48000,volume=0.85"
    'dismiss' = "$base,asetrate=50856,aresample=48000,volume=0.70"
    'info' = "$base,asetrate=53900,aresample=48000,apad=pad_dur=0.12,aloop=loop=1:size=24569,volume=0.9"
}
foreach ($cue in $cues.GetEnumerator()) {
    & ffmpeg -y -hide_banner -loglevel error -i $source -af $cue.Value -ar 48000 -ac 1 -c:a pcm_s16le (Join-Path $output "duck-$($cue.Key).wav")
    if ($LASTEXITCODE -ne 0) { throw "Failed to generate $($cue.Key)" }
}
