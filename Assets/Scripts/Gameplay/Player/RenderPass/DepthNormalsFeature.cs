using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal.Internal;
using System.Collections.Generic;

namespace Unity.FPSSample_2
{
    public class DepthNormalsFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class DepthSettings
        {
            public bool overrideDepth = false;
            public CompareFunction depthTest = CompareFunction.LessEqual;
            public bool writeDepth = true;
        }

        [System.Serializable]
        public class StencilSettings
        {
            public bool enabled = false;
            [Range(0, 255)] public int referenceValue = 0;
            public CompareFunction compareFunction = CompareFunction.Always;
            public StencilOp passOperation = StencilOp.Keep;
            public StencilOp failOperation = StencilOp.Keep;
            public StencilOp zFailOperation = StencilOp.Keep;
        }

        [System.Serializable]
        public class Settings
        {
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
            public RenderQueueType renderQueueType = RenderQueueType.Opaque;
            public LayerMask layerMask = -1;
            public bool setGlobalTextures = true;
            public bool enableRenderingLayers = false;

            [SerializeField]
            internal RenderingLayerUtils.MaskSize renderingLayersMaskSize = RenderingLayerUtils.MaskSize.Bits8;

            public DepthSettings depth = new DepthSettings();
            public StencilSettings stencil = new StencilSettings();
        }

        public Settings settings = new Settings();
        private DepthNormalsRenderPass m_RenderPass;

        private class DepthNormalsRenderPass : ScriptableRenderPass
        {
            private Settings m_Settings;
            private RenderStateBlock m_RenderStateBlock;
            private FilteringSettings m_FilteringSettings;
            private List<ShaderTagId> m_ShaderTagIds;
            private ProfilingSampler m_ProfilingSampler;

            public DepthNormalsRenderPass(Settings settings)
            {
                m_Settings = settings;
                m_ProfilingSampler = new ProfilingSampler("DepthNormals_Custom");

                RenderQueueRange renderQueueRange = settings.renderQueueType switch
                {
                    RenderQueueType.Opaque => RenderQueueRange.opaque,
                    RenderQueueType.Transparent => RenderQueueRange.transparent,
                    _ => RenderQueueRange.all
                };

                m_FilteringSettings = new FilteringSettings(renderQueueRange, settings.layerMask);

                m_ShaderTagIds = new List<ShaderTagId>
                {
                    new ShaderTagId("DepthNormals"),
                    new ShaderTagId("DepthNormalsOnly")
                };

                m_RenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);

                if (settings.depth.overrideDepth)
                {
                    m_RenderStateBlock.mask |= RenderStateMask.Depth;
                    m_RenderStateBlock.depthState = new DepthState(settings.depth.writeDepth, settings.depth.depthTest);
                }

                if (settings.stencil.enabled)
                {
                    StencilState stencilState = StencilState.defaultValue;
                    stencilState.enabled = true;
                    stencilState.SetCompareFunction(settings.stencil.compareFunction);
                    stencilState.SetPassOperation(settings.stencil.passOperation);
                    stencilState.SetFailOperation(settings.stencil.failOperation);
                    stencilState.SetZFailOperation(settings.stencil.zFailOperation);

                    m_RenderStateBlock.mask |= RenderStateMask.Stencil;
                    m_RenderStateBlock.stencilReference = settings.stencil.referenceValue;
                    m_RenderStateBlock.stencilState = stencilState;
                }
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                TextureHandle depthTexture = resourceData.cameraDepthTexture;
                TextureHandle normalsTexture = resourceData.cameraNormalsTexture;
                TextureHandle renderingLayersTexture = resourceData.renderingLayersTexture;

                if (!normalsTexture.IsValid())
                {
                    var descriptor = cameraData.cameraTargetDescriptor;
                    descriptor.msaaSamples = 1;
                    descriptor.depthBufferBits = 0;
                    descriptor.graphicsFormat = DepthNormalOnlyPass.GetGraphicsFormat();

                    normalsTexture = UniversalRenderer.CreateRenderGraphTexture(
                        renderGraph, descriptor, DepthNormalOnlyPass.k_CameraNormalsTextureName, false);
                }

                if (!depthTexture.IsValid())
                {
                    var descriptor = cameraData.cameraTargetDescriptor;
                    descriptor.msaaSamples = 1;
                    descriptor.depthBufferBits = 32;
                    descriptor.colorFormat = RenderTextureFormat.Depth;
                    descriptor.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;

                    depthTexture = UniversalRenderer.CreateRenderGraphTexture(
                        renderGraph, descriptor, "_CameraDepthTexture", false);
                }

                if (m_Settings.enableRenderingLayers && !renderingLayersTexture.IsValid())
                {
                    var descriptor = cameraData.cameraTargetDescriptor;
                    descriptor.msaaSamples = 1;
                    descriptor.depthBufferBits = 0;
                    descriptor.graphicsFormat = RenderingLayerUtils.GetFormat(m_Settings.renderingLayersMaskSize);

                    renderingLayersTexture = UniversalRenderer.CreateRenderGraphTexture(
                        renderGraph, descriptor, "_CameraRenderingLayersTexture", false);
                }

                using (var builder =
                       renderGraph.AddRasterRenderPass<PassData>("DepthNormals", out var passData, m_ProfilingSampler))
                {
                    builder.SetRenderAttachment(normalsTexture, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.Write);

                    passData.enableRenderingLayers = m_Settings.enableRenderingLayers;
                    passData.maskSize = m_Settings.renderingLayersMaskSize;

                    if (m_Settings.enableRenderingLayers && renderingLayersTexture.IsValid())
                    {
                        builder.SetRenderAttachment(renderingLayersTexture, 1, AccessFlags.Write);
                    }

                    var sortFlags = cameraData.defaultOpaqueSortFlags;
                    var drawSettings = RenderingUtils.CreateDrawingSettings(m_ShaderTagIds, renderingData, cameraData,
                        lightData, sortFlags);
                    drawSettings.perObjectData = PerObjectData.None;

                    if (m_Settings.stencil.enabled || m_Settings.depth.overrideDepth)
                    {
                        RenderingUtils.CreateRendererListWithRenderStateBlock(
                            renderGraph,
                            ref renderingData.cullResults,
                            drawSettings,
                            m_FilteringSettings,
                            m_RenderStateBlock,
                            ref passData.rendererListHdl);
                    }
                    else
                    {
                        var rendererListParams = new RendererListParams(renderingData.cullResults, drawSettings,
                            m_FilteringSettings);
                        passData.rendererListHdl = renderGraph.CreateRendererList(rendererListParams);
                    }

                    builder.UseRendererList(passData.rendererListHdl);

                    if (m_Settings.setGlobalTextures)
                    {
                        builder.SetGlobalTextureAfterPass(normalsTexture,
                            Shader.PropertyToID(DepthNormalOnlyPass.k_CameraNormalsTextureName));

                        if (m_Settings.enableRenderingLayers && renderingLayersTexture.IsValid())
                            builder.SetGlobalTextureAfterPass(renderingLayersTexture,
                                Shader.PropertyToID("_CameraRenderingLayersTexture"));
                    }

                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    {
                        RenderingLayerUtils.SetupProperties(context.cmd, data.maskSize);

                        if (data.enableRenderingLayers)
                            context.cmd.SetKeyword(ShaderGlobalKeywords.WriteRenderingLayers, true);

                        context.cmd.DrawRendererList(data.rendererListHdl);

                        if (data.enableRenderingLayers)
                            context.cmd.SetKeyword(ShaderGlobalKeywords.WriteRenderingLayers, false);
                    });
                }
            }

            private class PassData
            {
                internal RendererListHandle rendererListHdl;
                internal bool enableRenderingLayers;
                internal RenderingLayerUtils.MaskSize maskSize;
            }
        }

        public override void Create()
        {
            m_RenderPass = new DepthNormalsRenderPass(settings);
            m_RenderPass.renderPassEvent = settings.renderPassEvent;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (m_RenderPass == null)
                return;

            renderer.EnqueuePass(m_RenderPass);
        }

        protected override void Dispose(bool disposing)
        {
            m_RenderPass = null;
        }
    }
}