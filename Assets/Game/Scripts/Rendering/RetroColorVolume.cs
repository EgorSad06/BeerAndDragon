using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Цвет как на PS1: мало уровней на канал (Colour Levels) и упорядоченный дизеринг Байера 4x4 (Dither) крупными
// «пикселями» виртуального экрана высотой pixelResolution строк. Плюс дымка (Fog): по глубине, от fogStart до fogEnd
// метров цвет уходит в fogColor (не дальше fogMax -- небо тоже в дымке), ступенями fogSteps, сглаженными дизерингом. Дополняет Retro FX Lite (REEBOO), где этого нет:
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

    [Header("Дымка")]
    [Tooltip("Плотность дымки вдали (0 -- нет дымки, 1 -- вдали только цвет дымки).")]
    public ClampedFloatParameter fogMax = new ClampedFloatParameter(0f, 0f, 1f);
    public ColorParameter fogColor = new ColorParameter(new Color(0.55f, 0.52f, 0.6f), false, false, true);
    public MinFloatParameter fogStart = new MinFloatParameter(8f, 0f);
    public MinFloatParameter fogEnd = new MinFloatParameter(60f, 0.1f);
    [Tooltip("Ступеней дымки (как полосы тумана на PS1). 0 -- плавно.")]
    public ClampedIntParameter fogSteps = new ClampedIntParameter(8, 0, 64);

    public bool IsActive() => colourLevels.value > 1 || fogMax.value > 0f;
}
