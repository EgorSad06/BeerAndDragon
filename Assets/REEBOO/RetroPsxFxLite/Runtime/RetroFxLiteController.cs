// Retro PSX & CRT FX LITE - REEBOO Labs. Free version: pixelation, scanlines and CRT curvature.
// Full version (40 presets, palettes, dither, VHS, glitch, phosphor mask, Game Boy mode...): https://reebo0.itch.io/retro-psx-crt-fx-unity
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace REEBOO.RetroFx.Lite
{    /// <summary>Applies Lite presets to a Volume at runtime (works on a runtime copy of the profile).</summary>
    [AddComponentMenu("REEBOO/Retro FX Lite/Retro FX Lite Controller")]
    public sealed class RetroFxLiteController : MonoBehaviour
    {
        [SerializeField] private Volume volume;
        public RetroFxLitePreset[] presets = new RetroFxLitePreset[0];
        [SerializeField] private int startIndex;

        private RetroFxLiteVolume m_Fx;
        private int m_Index;
        private bool m_Enabled = true;

        public int Index => m_Index;
        public RetroFxLitePreset Current => presets.Length > 0 ? presets[m_Index] : null;

        public bool EffectsEnabled
        {
            get => m_Enabled;
            set { m_Enabled = value; Apply(m_Index); }
        }

        private void Awake() => Apply(startIndex);

        private bool Ensure()
        {
            if (m_Fx != null) return true;
            if (volume == null) volume = GetComponent<Volume>();
            if (volume == null) return false;
            VolumeProfile p = Application.isPlaying ? volume.profile : volume.sharedProfile;
            if (p == null) return false;
            if (!p.TryGet(out m_Fx)) m_Fx = p.Add<RetroFxLiteVolume>(true);
            return true;
        }

        public void Apply(int index)
        {
            if (presets.Length == 0 || !Ensure()) return;
            m_Index = ((index % presets.Length) + presets.Length) % presets.Length;
            presets[m_Index].ApplyTo(m_Fx);
            if (!m_Enabled) m_Fx.intensity.Override(0f);
        }

        public void Next() => Apply(m_Index + 1);
        public void Previous() => Apply(m_Index - 1);
    }
}
