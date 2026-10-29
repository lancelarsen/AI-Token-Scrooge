using System.Text.Json;
using AIResetRadar.Core;
using Xunit;

namespace AIResetRadar.Core.Tests;

public class SoundSettingsTests
{
    [Fact]
    public void ExistingSettingsKeepPreviousVolumeAndSelectedVolumeSurvivesReload()
    {
        var older = JsonSerializer.Deserialize<RadarSettings>("{\"Sound\":true}")!;
        Assert.Equal(65d, older.SoundVolume);
        var muted = older with { SoundVolume = 0 };
        var restored = JsonSerializer.Deserialize<RadarSettings>(JsonSerializer.Serialize(muted))!;
        Assert.Equal(0d, restored.SoundVolume);
        Assert.True(restored.Sound);
    }
}
