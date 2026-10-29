# Regenerate the app's duck cues from the user-selected auditions #02 and #08.
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

# The longer alarm uses the complete natural calling sequence from audition #08.
$alarmSource = Join-Path ([IO.Path]::GetTempPath()) 'scrooge-long-duck.mp3'
Invoke-WebRequest 'https://cdn.freesound.org/previews/185/185134_8043-hq.mp3' -OutFile $alarmSource
& ffmpeg -y -hide_banner -loglevel error -i $alarmSource -af 'afade=t=in:d=0.01,afade=t=out:st=3.78:d=0.05' -ar 48000 -ac 1 -c:a pcm_s16le (Join-Path $output 'duck-alert.wav')
if ($LASTEXITCODE -ne 0) { throw 'Failed to generate longer alarm' }
