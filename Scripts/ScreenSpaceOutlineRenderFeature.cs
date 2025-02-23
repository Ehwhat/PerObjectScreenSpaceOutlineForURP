using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal.Internal;
using UnityEngine.Serialization;

namespace EhLib.Outline
{
    /// <summary>
    ///  A ScriptableRendererFeature that renders outlines in screen space.
    ///  This feature requires the UniversalRenderer to be used.
    ///
    ///  How to use:
    ///     1. Create a new OutlineDefinition asset (Assets > Create > Ehlib > Outline Definition).
    ///     2. Assign the OutlineDefinition to an Outline component.
    ///     3. Add the Outline component to a GameObject. (All renderers in the GetComponentInChildren will be rendered with the outline effect)
    ///     4. Add the ScreenSpaceOutlineRenderFeature to the renderer.
    /// </summary>
    ///
    /// TODO: See about adding a per-instance color to the outline effect.
    public class ScreenSpaceOutlineRenderFeature : ScriptableRendererFeature
    {
        private class OutlineParameters
        {
            public float Alpha;
        }
            
        private struct RendererData
        {
            public Renderer renderer;
            public int materialsCount;
            public OutlineParameters parameters;
        }
        
        [System.Serializable]
        public class ScreenSpaceOutlinesSettings
        {
            [Tooltip("The render pass event to inject the outline rendering into. This likely should be set to BeforeRenderingPostProcessing.")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        }

        public class SharedData : ContextItem
        {
            public TextureHandle depthCopy = TextureHandle.nullHandle;
            public override void Reset()
            {
                depthCopy = TextureHandle.nullHandle;
            }
        }

        class ScreenSpaceOutlinePass : ScriptableRenderPass
        {
            class RenderPassData
            {
                public List<RendererData> renderers = new List<RendererData>();
            }

            class CustomDepthBlitPassData
            {
                public TextureHandle source;
                public TextureHandle sourceDepth;
                public TextureHandle destination;
                public Material material;
                public int pass;
            }

            private static readonly int horizontalBlurId = Shader.PropertyToID("_HorizontalBlur");
            private static readonly int verticalBlurId = Shader.PropertyToID("_VerticalBlur");
            private static readonly int alphaId = Shader.PropertyToID("_Alpha");

            const string kRenderOutlineRenderersTag = "Render Outline Renderers";
            const string kHorizontalBlurTag = "Horizontal Blur";
            const string kVerticalBlurTag = "Vertical Blur";
            const string kInfillTag = "Infill Render";
            const string kOutlineTag = "Outline Render";
            const string kCompositeTag = "Composite";
            
            const int kVerticalBlurPass = 0;
            const int kHorizontalBlurPass = 1;
            const int kOutlineCompositePass = 2;
            const int kInfillCompositePass = 3;
            const int kDrawPass = 4;
            
            private ScreenSpaceOutlinesSettings settings;
            private Material outlineUtilityMaterial;
            private Dictionary<OutlineDefinition,List<RendererData>> outlineRendererData;
            public bool requestedDepthCopy;

            public void Setup(
                ScreenSpaceOutlinesSettings settings, 
                Material outlineUtilityMaterial, 
                Dictionary<OutlineDefinition, List<RendererData>> outlineRendererData,
                bool requestedDepthCopy = false)
            {
                this.settings = settings;
                this.outlineUtilityMaterial = outlineUtilityMaterial;
                this.outlineRendererData = outlineRendererData;
                this.requestedDepthCopy = requestedDepthCopy;
            }

            // CompositePass is a helper method that blits the source texture to the destination texture using additive pass in the blur material.
            // Reason for not using a BlitPass is that we're also using the depth texture in the shader for a stencil test, which a BlitPass doesn't support.
            private void CompositePass(RenderGraph renderGraph, int pass, TextureHandle source, TextureHandle depth, TextureHandle destination)
            {
                using (var builder = renderGraph.AddRasterRenderPass<CustomDepthBlitPassData>(kCompositeTag, out var passData))
                {
                    passData.source = source;
                    passData.destination = destination;
                    passData.sourceDepth = depth;
                    
                    builder.UseTexture(source, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0);
                    builder.SetRenderAttachmentDepth(depth, AccessFlags.Read);
                    
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.UseAllGlobalTextures(true);
                    
                    builder.SetRenderFunc((CustomDepthBlitPassData data, RasterGraphContext context) =>
                    {
                        RasterCommandBuffer cmd = context.cmd;
                        Vector4 bias = new Vector4(1, 1, 0, 0);
                        Blitter.BlitTexture(cmd, data.source, bias, outlineUtilityMaterial, pass);
                    });
                }
            }
            
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (outlineRendererData.Count == 0)
                {
                    return;
                }
                
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                if (resourceData.isActiveTargetBackBuffer)
                {
                    // If the active target is the back buffer, we need to create an intermediate render target to render the outline.
                    return;
                }

                TextureHandle source = resourceData.activeColorTexture;

                RenderTextureDescriptor tempDesc = cameraData.cameraTargetDescriptor;
                tempDesc.depthBufferBits = 0;
                tempDesc.colorFormat = RenderTextureFormat.DefaultHDR;
                
                TextureHandle outlineTexture0 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, tempDesc, "Outline Texture 1", true);
                TextureHandle outlineTexture1 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, tempDesc, "Outline Texture 2", true);
                
                TextureHandle sceneDepth = TextureHandle.nullHandle;
                // If any outline effect uses the scene depth for occlusion, we need to copy the depth texture from the camera.
                bool useSceneDepthToOcclude = requestedDepthCopy && frameData.Contains<SharedData>();
                if (useSceneDepthToOcclude)
                {
                    SharedData sharedData = frameData.Get<SharedData>();
                    if (!sharedData.depthCopy.IsValid())
                    {
                        Debug.LogWarning("Depth copy texture is invalid, skipping outline effect.");
                        return;
                    }
                    sceneDepth = sharedData.depthCopy;
                    
                }
                // The temp depth texture is used for the outline effect if no outline effect uses the scene depth for occlusion.
                // as we still need a depth texture for the stencil test.
                var tempDepthDesc = renderGraph.GetTextureDesc(resourceData.activeDepthTexture);
                tempDepthDesc.depthBufferBits = DepthBits.Depth24;
                tempDepthDesc.name = "Outline Temp Depth Texture";
                tempDepthDesc.clearBuffer = false;
                TextureHandle tempDepth = renderGraph.CreateTexture(tempDepthDesc);

                foreach (var (outlineDefinition, renderers) in outlineRendererData)
                {
                    TextureHandle depth = outlineDefinition.useSceneDepthForOcclusion ? sceneDepth : tempDepth;
                    
                    float horizontalBlur = outlineDefinition.outlineWidth / tempDesc.width;
                    float verticalBlur = outlineDefinition.outlineWidth / tempDesc.height;
                    
                    // Draw outline renderers, using a list of renderers from the RenderPassData context.
                    using (var builder =
                           renderGraph.AddRasterRenderPass<RenderPassData>(kRenderOutlineRenderersTag,
                               out var passData))
                    {
                        passData.renderers = renderers;

                        builder.SetRenderAttachment(outlineTexture0, 0);
                        builder.SetRenderAttachmentDepth(depth, AccessFlags.ReadWrite);
                        builder.AllowGlobalStateModification(true);

                        builder.SetRenderFunc((RenderPassData data, RasterGraphContext context) =>
                        {
                            RasterCommandBuffer cmd = context.cmd;
                            cmd.ClearRenderTarget(RTClearFlags.ColorStencil, Color.clear, 1.0f, 0);
                            
                            // Setting the blur parameters for the blur effect for future passes.
                            // Reasoning behind this is a bit iffy, but we can't send the blur parameters to the blur material directly.
                            // and there's no way to fit them into the BlitPass below.
                            // TODO: Look into a better way to handle this.
                            cmd.SetGlobalFloat(horizontalBlurId, horizontalBlur);
                            cmd.SetGlobalFloat(verticalBlurId, verticalBlur);
                            
                            foreach (var rendererData in data.renderers)
                            {
                                cmd.SetGlobalFloat(alphaId, rendererData.parameters.Alpha);

                                for (int i = 0; i < rendererData.materialsCount; i++)
                                {
                                    cmd.DrawRenderer(rendererData.renderer, outlineUtilityMaterial, i, kDrawPass);
                                }
                            }
                        });
                    }

                    // The Blur passes actually do a little more than just blurring the outline texture.
                    // The Alpha channel is filled with the Outline Alpha, which you can then use in the outline material to control the alpha of the outline.
                    RenderGraphUtils.BlitMaterialParameters blitParameters = new(outlineTexture0, outlineTexture1,
                        outlineUtilityMaterial, kHorizontalBlurPass);
                    renderGraph.AddBlitPass(blitParameters, kHorizontalBlurTag);

                    RenderGraphUtils.BlitMaterialParameters blurParameters = new(outlineTexture1, outlineTexture0,
                        outlineUtilityMaterial, kVerticalBlurPass);
                    renderGraph.AddBlitPass(blurParameters, kVerticalBlurTag);

                    Material outlineRenderMaterial = outlineDefinition.outlineMaterial;
                    if (outlineRenderMaterial)
                    {
                        // Render outline
                        RenderGraphUtils.BlitMaterialParameters outlineRenderParameters =
                            new(outlineTexture0, outlineTexture1, outlineRenderMaterial, -1);
                        renderGraph.AddBlitPass(outlineRenderParameters, kOutlineTag);
                        // Composite Stencil pass (outline only)
                        CompositePass(renderGraph, kOutlineCompositePass, outlineTexture1, depth, source);
                    }

                    Material infillRenderMaterial = outlineDefinition.infillMaterial;
                    if (infillRenderMaterial)
                    {
                        // Render infill
                        RenderGraphUtils.BlitMaterialParameters infillRenderParameters =
                            new(outlineTexture0, outlineTexture1, infillRenderMaterial, -1);
                        renderGraph.AddBlitPass(infillRenderParameters, kInfillTag);
                        // Composite Stencil pass (infill only)
                        CompositePass(renderGraph, kInfillCompositePass, outlineTexture1, depth, source);
                    }
                }

                // Apply the final result to the active target.
                resourceData.cameraColor = source;
            }
        }
        
        private class CopyDepthPassCustom : ScriptableRenderPass
        {
            private CopyDepthPass m_CopyDepthPass;
            
            public CopyDepthPassCustom(ScreenSpaceOutlinesSettings settings, Shader copyDepthShader)
            {
                m_CopyDepthPass = new CopyDepthPass(this.renderPassEvent, copyDepthShader, true, true, false, " Copy Depth custom");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                TextureHandle source = resourceData.activeDepthTexture;
                
                try
                {
                    if (!source.IsValid())
                    {
                        Debug.LogWarning("Depth texture is invalid, skipping copy depth pass.");
                        return;
                    }

                    SharedData sharedData = frameData.GetOrCreate<SharedData>();
                    renderGraph.GetTextureDesc(source);

                    TextureDesc depthTextureDesc = source.GetDescriptor(renderGraph);
                    depthTextureDesc.name = "Outline Depth Copy Texture";
                    depthTextureDesc.clearBuffer = true;
                    depthTextureDesc.colorFormat = GraphicsFormat.None;
                    depthTextureDesc.useMipMap = false;
                    depthTextureDesc.filterMode = FilterMode.Point;
                    depthTextureDesc.depthBufferBits = DepthBits.Depth24;
                    

                    sharedData.depthCopy = renderGraph.CreateTexture(depthTextureDesc);
                    m_CopyDepthPass.Render(
                        renderGraph, 
                        frameData, 
                        sharedData.depthCopy,
                        source, 
                        false,
                        "Outline Depth Copy");
                }
                catch (System.Exception e)
                {
                    Debug.LogError("Error copying depth texture for outline effect\nThis is likely due to the layer event not supporting it. Error: " + e.Message);
                }
            }
        }
        
        public ScreenSpaceOutlinesSettings settings;
        
        private ScreenSpaceOutlinePass outlinePass;
        private CopyDepthPassCustom copyDepthPassCustom;
        
        private Shader copyDepthShader;
        private CopyDepthPass copyDepthPass;
        private bool runThisFrame;
        private Dictionary<OutlineDefinition,List<RendererData>> outlineRendererData = 
            new Dictionary<OutlineDefinition,List<RendererData>>();
        private bool requestedDepthCopy;
        
        public override void Create()
        {
            Material blurMaterial = CoreUtils.CreateEngineMaterial("CustomEffects/Blur");
            if ( GraphicsSettings.TryGetRenderPipelineSettings<UniversalRendererResources>( out var resources ) )
            {
                copyDepthShader = resources.copyDepthPS;
            }
            
            // So how this works with the outline effect is that we need to copy the depth texture from the camera,
            // but only in the case that any outline effect uses the scene depth for occlusion.
            // Check AddRenderPasses for more info.
            copyDepthPassCustom = new CopyDepthPassCustom(settings, copyDepthShader);
            copyDepthPassCustom.renderPassEvent = settings.renderPassEvent;
            
            outlinePass = new ScreenSpaceOutlinePass();
            outlinePass.Setup(settings, blurMaterial, outlineRendererData);
            outlinePass.renderPassEvent = settings.renderPassEvent;
        }

        private void Reset()
        {
            settings = new ScreenSpaceOutlinesSettings();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // So here we're constructing the list of renderers that need to be rendered with the outline effect.
            // I don't like that we do this per camera, but it works for now as there's no begin/end frame event.
            // TODO: See about precomputing this in the Outline component as a static list.
            SetupOutlines();
            
            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType != CameraType.Game && cameraType != CameraType.SceneView)
            {
                return;
            }

            // If a OutlineDefinition uses the scene depth for occlusion, we need to copy the depth texture from the camera.
            // This is done in a separate pass as actually copying the depth texture a hassle, with needing to account for
            // MSAA, etc.
            if (requestedDepthCopy)
            {
                copyDepthPassCustom.renderPassEvent = settings.renderPassEvent;
                renderer.EnqueuePass(copyDepthPassCustom);
            }
            
            outlinePass.requestedDepthCopy = requestedDepthCopy;
            renderer.EnqueuePass(outlinePass);
        }
        
        private void SetupOutlines()
        {
            outlineRendererData.Clear();
            requestedDepthCopy = false;
            foreach (Outline outline in Outline.Outlines)
            {
                if (!outline || !outline.enabled || !outline.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!outline.outlineDefinition)
                {
                    Debug.LogWarning($"{outline.gameObject.name}'s Outline has no outline definition, skipping outline effect.");
                    continue;
                }
                
                if (outline.outlineDefinition.outlineMaterial == null && outline.outlineDefinition.infillMaterial == null)
                {
                    Debug.LogError($"Outline definition {outline.outlineDefinition.name} has no outline or infill material, skipping outline effect.");
                    continue;
                }

                if (outline.outlineDefinition.useSceneDepthForOcclusion)
                {
                    requestedDepthCopy = true;
                }

                var outlineRenderers = outline.Renderers;
                OutlineParameters parameters = new OutlineParameters()
                {
                    Alpha = outline.outlineAlpha
                };
                
                foreach (Renderer renderer in outlineRenderers)
                {
                    if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    int materialsCount = renderer.sharedMaterials.Length;
                    
                    var rendererData = new RendererData()
                    {
                        renderer = renderer,
                        materialsCount = materialsCount,
                        parameters = parameters
                    };
                    
                    if (!outlineRendererData.TryGetValue(outline.outlineDefinition, out var rendererList))
                    {
                        rendererList = new List<RendererData>();
                        outlineRendererData.Add(outline.outlineDefinition, rendererList);
                    }
                    rendererList.Add(rendererData);
                }
            }
        }

    }
}
