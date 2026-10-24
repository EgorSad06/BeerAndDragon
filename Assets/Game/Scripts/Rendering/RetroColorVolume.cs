using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Цвет как на PS1: мало уровней на канал (Colour Levels) и упорядоченный дизеринг Байера 4x4 (Dither) крупными
// «пикселями» виртуального экрана высотой pixelResolution строк. Дополняет Retro FX Lite (REEBOO), где этого нет:
// там -- пикселизация, здесь -- цвет. Рисует RetroColorFeature (стоит в PC_Renderer и Mobile_Renderer после Retro FX Lite),
// значения -- в Volume Profile (Assets/Game/Settings/DefaultVolumeProfile -- для всех сцен).
[System.Serializable, VolumeComponentMenu("BeerAndDragon/Retro Colour")]
[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
public sealed class RetroColorVolume : VolumeComponent, IPostProcessComponent
{
    [Tooltip("Уровней на канал (32 -- 15-битный цвет PS1). 0 -- выключено.")]
    public ClampedIntParameter colourLevels = new ClampedIntParameter(0, 0, 256);
    [Tooltip("Сила дизеринга между уровнями.")]
    public ClampedFloatParameter dither = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Высота виртуального экрана в строках -- размер клетки дизеринга (как Pixel Resolution у Retro FX Lite).")]
    public ClampedIntParameter pixelResolution = new ClampedIntParameter(240, 16, 1080);

    public bool IsActive() => colourLevels.value > 1;
}
