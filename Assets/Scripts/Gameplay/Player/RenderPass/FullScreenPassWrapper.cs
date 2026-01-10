using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Unity.FPSSample_2
{
    public class FullScreenPassWrapper
    {
        private FullScreenPassRendererFeature.FullScreenRenderPass _fullScreenRenderPass;

        public FullScreenPassWrapper(string passName, Material material, int passIndex, bool fetchActiveColor,
            bool bindDepthStencilAttachment)
        {
            _fullScreenRenderPass = new FullScreenPassRendererFeature.FullScreenRenderPass(passName);
            _fullScreenRenderPass.SetupMembers(material, passIndex, fetchActiveColor, bindDepthStencilAttachment);
        }

        public void EnqueuePass(Camera camera)
        {
            camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(_fullScreenRenderPass);
        }
    }
}
