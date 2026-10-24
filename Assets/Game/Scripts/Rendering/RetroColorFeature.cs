using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Проход URP для RetroColorVolume: дымка по глубине, квантование цвета + дизеринг по всему кадру
// (шейдер Hidden/BeerAndDragon/RetroColor). Глубину просит у URP сам (ConfigureInput).
// Порядок: после Retro FX Lite (тот же renderPassEvent, фича стоит ниже в списке рендерера).
public sealed class RetroColorFeature : ScriptableRendererFeature
{
    public const string ShaderName = "Hidden/BeerAndDragon/RetroColor";
    [SerializeField] private Shader shader;
    public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPostProcessing;

    private Material material;
    private ColorPass pass;

    public override void Create()
    {
        if (shader == null) shader = Shader.Find(ShaderName);
        pass = new ColorPass { renderPassEvent = passEvent };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (shader == null) shader = Shader.Find(ShaderName);
        if (shader == null) return;
        if (material == null) material = CoreUtils.CreateEngineMaterial(shader);
        pass.renderPassEvent = passEvent;
        pass.material = material;
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);

    private sealed class ColorPass : ScriptableRenderPass
    {
        public Material material;
        private static readonly MaterialPropertyBlock Props = new MaterialPropertyBlock();
        private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
        private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        private static readonly int ParamsId = Shader.PropertyToID("_RetroColorParams");
        private static readonly int FogId = Shader.PropertyToID("_RetroFogParams");
        private static readonly int FogColorId = Shader.PropertyToID("_RetroFogColor");
        private static readonly int DepthId = Shader.PropertyToID("_CameraDepthTexture");

        private sealed class PassData
        {
            public TextureHandle source, depth;
            public Material material;
            public Vector4 values, fog;
            public Color fogColor;
        }

        public ColorPass()
        {
            profilingSampler = new ProfilingSampler("Retro Colour");
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null) return;
            RetroColorVolume fx = VolumeManager.instance.stack?.GetComponent<RetroColorVolume>();
            if (fx == null || !fx.active || !fx.IsActive()) return;
            UniversalCameraData cam = frameData.Get<UniversalCameraData>();
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection || !cam.resolveFinalTarget) return;
            UniversalResourceData res = frameData.Get<UniversalResourceData>();
            if (res.isActiveTargetBackBuffer) return;

            TextureHandle src = res.activeColorTexture;
            TextureDesc desc = renderGraph.GetTextureDesc(src);
            desc.name = "_RetroColor";
            desc.clearBuffer = false;
            desc.msaaSamples = MSAASamples.None;
            TextureHandle dst = renderGraph.CreateTexture(desc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Retro Colour", out var data, profilingSampler))
            {
                data.source = src;
                data.material = material;
                // уровни, дизеринг, размер клетки дизеринга в пикселях экрана
                data.values = new Vector4(fx.colourLevels.value, fx.dither.value,
                                          Mathf.Max(1f, Mathf.Round(desc.height / (float)fx.pixelResolution.value)),
                                          fx.pixelResolution.value);
                data.fog = new Vector4(fx.fogStart.value, Mathf.Max(fx.fogEnd.value, fx.fogStart.value + 0.1f),
                                       fx.fogMax.value, fx.fogSteps.value);
                data.fogColor = fx.fogColor.value;
                data.depth = res.cameraDepthTexture;
                builder.UseTexture(src, AccessFlags.Read);
                if (data.depth.IsValid()) builder.UseTexture(data.depth, AccessFlags.Read);
                else data.fog.z = 0f;
                builder.SetRenderAttachment(dst, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                {
                    Props.Clear();
                    Props.SetTexture(BlitTextureId, d.source);
                    Props.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                    Props.SetVector(ParamsId, d.values);
                    Props.SetVector(FogId, d.fog);
                    Props.SetColor(FogColorId, d.fogColor);
                    if (d.depth.IsValid()) Props.SetTexture(DepthId, d.depth);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1, Props);
                });
            }
            res.cameraColor = dst;
        }
    }
}
