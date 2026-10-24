// Retro PSX & CRT FX LITE - REEBOO Labs. Free version: pixelation, scanlines and CRT curvature.
// Full version (40 presets, palettes, dither, VHS, glitch, phosphor mask, Game Boy mode...): https://reebo0.itch.io/retro-psx-crt-fx-unity
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace REEBOO.RetroFx.Lite
{    /// <summary>Lite preset asset.</summary>
    [CreateAssetMenu(fileName = "RetroFxLitePreset", menuName = "REEBOO/Retro FX Lite Preset", order = 410)]
    public sealed class RetroFxLitePreset : ScriptableObject
    {
        public string displayName = "Lite Preset";
        [Range(0, 1080)] public int pixelResolution = 240;
        [Range(0f, 1f)] public float scanlines = 0.4f;
        [Range(0, 1080)] public int scanlineCount = 0;
        [Range(0f, 0.5f)] public float curvature = 0f;

        public void ApplyTo(RetroFxLiteVolume v)
        {
            if (v == null) return;
            v.intensity.Override(1f);
            v.pixelResolution.Override(pixelResolution);
            v.scanlines.Override(scanlines);
            v.scanlineCount.Override(scanlineCount);
            v.curvature.Override(curvature);
        }
    }
}
