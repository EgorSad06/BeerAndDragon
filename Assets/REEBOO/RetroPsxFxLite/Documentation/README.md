# Retro PSX & CRT FX LITE for Unity 6 (URP) - free version

By REEBOO Labs. Free Lite version of **Retro PSX & CRT FX**:
https://reebo0.itch.io/retro-psx-crt-fx-unity

## What is inside

- **PSX Lite shaders** (URP): `REEBOO/Retro FX Lite/PSX Lit` (per-vertex main light + ambient) and
  `REEBOO/Retro FX Lite/PSX Unlit`. Vertex snapping, affine texture mapping and ordered dither on 15-bit colour.
- **Retro FX Lite screen effect**: a URP renderer feature (Render Graph, Unity 6) driven by a Volume override
  `REEBOO/Retro FX Lite` with pixelation, scanlines and CRT curvature.
- **3 presets**: PS1 240p, Arcade Tube, Chunky TV.
- **Demo scene**: `Demo/RetroFxLite_Demo.unity` (Q/E or arrow keys = presets, Space = effect on/off).

## Quick start

1. Import the package into a project that uses the Universal Render Pipeline.
2. Menu **Tools > REEBOO > Retro FX Lite > Setup Current Pipeline**. This adds the Retro FX Lite renderer
   feature to your URP renderer(s) and a global Volume to the open scene (Undo works).
3. Select the new "Retro FX Lite" object and change Pixel Resolution, Scanlines and Curvature on the Volume.
4. Put `REEBOO/Retro FX Lite/PSX Lit` on your materials for vertex wobble and warped textures.

Tested with Unity 6000.3.26f1, URP 17.3.0 (Forward and Forward+ renderers), Linear colour space, Windows (D3D11 editor).
URP only (the Built-in render pipeline is not supported).

## Lite vs full version

| Feature | Lite (free) | Full |
| --- | --- | --- |
| Presets | 3 | 40 (console, CRT, VHS, handheld, palettes, stylised) |
| Pixelation | yes | yes |
| Scanlines | yes | yes, brightness-dependent beam width |
| CRT curvature | yes | yes + adjustable rounded corners, vignette, flicker |
| Phosphor mask (aperture grille / shadow / slot) + glow | - | yes |
| Palette modes (posterize, Game Boy ramp, CGA nearest, monochrome) + ordered dither | - | yes |
| VHS (jitter, tracking band, head switch, chroma bleed/shift, tape noise) | - | yes |
| Chromatic aberration, film grain, glitch, interlace, LCD grid | - | yes |
| Colour: saturation, contrast, brightness, tint | - | yes |
| Before/after split view | - | yes |
| PSX shaders: vertex snap, affine, dither | yes | yes |
| PSX shaders: additional lights, shadows, emission, fog, draw distance, colour depth, point-filter look, cutout/transparent/additive | - | yes |
| Scene-wide PSX Settings (shader globals) | - | yes |
| Material converter (URP Lit/Unlit/Simple Lit -> PSX) | - | yes |
| Runtime controller: blend between presets, glitch/noise/chromatic pulses | - | yes |
| Demo with uGUI preset picker, sliders, fly camera | - | yes |
| HTML manual with full API | - | yes |

Lite and full version can be installed in the same project (different folders, namespaces and shader names).

## License

See `LICENSE.txt`: royalty-free in your games and apps; do not resell or redistribute the assets as-is.

Full version: https://reebo0.itch.io/retro-psx-crt-fx-unity
Lite page: https://reebo0.itch.io/retro-psx-crt-fx-unity-lite
