// Retro PSX & CRT FX LITE - REEBOO Labs. Free version: pixelation, scanlines and CRT curvature.
// Full version (40 presets, palettes, dither, VHS, glitch, phosphor mask, Game Boy mode...): https://reebo0.itch.io/retro-psx-crt-fx-unity
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace REEBOO.RetroFx.Lite
{    /// <summary>Add to your URP Renderer (or use Tools > REEBOO > Retro FX Lite > Setup Current Pipeline).</summary>
    [DisallowMultipleRendererFeature("Retro FX Lite (REEBOO)")]
    public sealed class RetroFxLiteFeature : ScriptableRendererFeature
    {
        public const string ScreenShaderName = "Hidden/REEBOO/RetroFxLite/Screen";
        [SerializeField] private Shader shader;
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPostProcessing;

        private Material m_Material;
        private LitePass m_Pass;

        public override void Create()
        {
            if (shader == null) shader = Shader.Find(ScreenShaderName);
            m_Pass = new LitePass { renderPassEvent = passEvent };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (shader == null) shader = Shader.Find(ScreenShaderName);
            if (shader == null) return;
            if (m_Material == null) m_Material = CoreUtils.CreateEngineMaterial(shader);
            m_Pass.renderPassEvent = passEvent;
            m_Pass.material = m_Material;
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing) => CoreUtils.Destroy(m_Material);

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (shader == null)
            {
                shader = Shader.Find(ScreenShaderName);
                if (shader != null) UnityEditor.EditorUtility.SetDirty(this);
            }
        }
#endif

        private sealed class LitePass : ScriptableRenderPass
        {
            public Material material;
            private static readonly MaterialPropertyBlock s_Props = new MaterialPropertyBlock();
            private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
            private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            private static readonly int SizeId = Shader.PropertyToID("_RFXL_Size");
            private static readonly int ParamsId = Shader.PropertyToID("_RFXL_Params");

            private sealed class PassData
            {
                public TextureHandle source;
                public Material material;
                public Vector4 size, values;
                public float intensity;
            }

            public LitePass()
            {
                profilingSampler = new ProfilingSampler("REEBOO Retro FX Lite");
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (material == null) return;
                RetroFxLiteVolume fx = VolumeManager.instance.stack?.GetComponent<RetroFxLiteVolume>();
                if (fx == null || !fx.active || !fx.IsActive()) return;
                UniversalCameraData cam = frameData.Get<UniversalCameraData>();
                if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection || !cam.resolveFinalTarget) return;
                UniversalResourceData res = frameData.Get<UniversalResourceData>();
                if (res.isActiveTargetBackBuffer) return;

                TextureHandle src = res.activeColorTexture;
                TextureDesc desc = renderGraph.GetTextureDesc(src);
                desc.name = "_RetroFxLiteColor";
                desc.clearBuffer = false;
                desc.msaaSamples = MSAASamples.None;
                TextureHandle dst = renderGraph.CreateTexture(desc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("REEBOO Retro FX Lite", out var data, profilingSampler))
                {
                    data.source = src;
                    data.material = material;
                    data.size = new Vector4(desc.width, desc.height, 1f / desc.width, 1f / desc.height);
                    float count = fx.scanlineCount.value > 0 ? fx.scanlineCount.value : (fx.pixelResolution.value > 0 ? fx.pixelResolution.value : 270);
                    data.values = new Vector4(fx.pixelResolution.value, fx.scanlines.value, count, fx.curvature.value);
                    data.intensity = fx.intensity.value;
                    builder.UseTexture(src, AccessFlags.Read);
                    builder.SetRenderAttachment(dst, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                    {
                        s_Props.Clear();
                        s_Props.SetTexture(BlitTextureId, d.source);
                        s_Props.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                        s_Props.SetVector(SizeId, new Vector4(d.size.x, d.size.y, d.size.z, d.intensity));
                        s_Props.SetVector(ParamsId, d.values);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1, s_Props);
                    });
                }
                res.cameraColor = dst;
            }
        }
    }
}
