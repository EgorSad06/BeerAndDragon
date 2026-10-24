// Retro PSX & CRT FX LITE - REEBOO Labs. Free version: pixelation, scanlines and CRT curvature.
// Full version (40 presets, palettes, dither, VHS, glitch, phosphor mask, Game Boy mode...): https://reebo0.itch.io/retro-psx-crt-fx-unity
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace REEBOO.RetroFx.Lite
{    /// <summary>Lite screen effect: pixelation + scanlines + CRT curvature. Add to a Volume profile.</summary>
    [Serializable, VolumeComponentMenu("REEBOO/Retro FX Lite")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class RetroFxLiteVolume : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Blend between the clean image (0) and the retro result (1).")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(1f, 0f, 1f);
        [Tooltip("Virtual vertical resolution (e.g. 240). 0 = off.")]
        public ClampedIntParameter pixelResolution = new ClampedIntParameter(0, 0, 1080);
        [Tooltip("Darkness of the gaps between scanlines.")]
        public ClampedFloatParameter scanlines = new ClampedFloatParameter(0f, 0f, 1f);
        [Tooltip("Number of scanlines. 0 = match Pixel Resolution (or 270).")]
        public ClampedIntParameter scanlineCount = new ClampedIntParameter(0, 0, 1080);
        [Tooltip("Barrel curvature of the CRT glass. 0 = flat.")]
        public ClampedFloatParameter curvature = new ClampedFloatParameter(0f, 0f, 0.5f);

        public bool IsActive() => intensity.value > 0f && (pixelResolution.value > 0 || scanlines.value > 0f || curvature.value > 0f);
    }
}
