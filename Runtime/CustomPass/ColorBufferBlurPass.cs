using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
#if UNITY_2021_2_OR_NEWER
using UnityEngine.Rendering.RendererUtils;
#else
using UnityEngine.Experimental.Rendering;
#endif
#if UNITY_2020_2_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif
using System.Reflection;

namespace Unity.DemoTeam.DigitalHuman
{
    public class ColorBufferBlurPass : CustomPass
    {
        [System.Serializable]
        public class BlurParameters
        {
            [Range(0, 8)] public float blurStr;
            [Range(0.0f, 1.0f)] public float blurWidth;
            [Range(0.0f, 1.0f)] public float blurShift;
        }

        public RenderQueueType queue = RenderQueueType.AllOpaque;
        public LayerMask layerMask = 0; // default to 'None'

        public BlurParameters leftBlur = new BlurParameters{blurStr = 2.0f, blurWidth = 0.3f, blurShift = 0.0f};
        public BlurParameters rightBlur = new BlurParameters{blurStr = 2.0f, blurWidth = 0.3f, blurShift = 0.0f};
        public BlurParameters topBlur = new BlurParameters{blurStr = 2.0f, blurWidth = 0.3f, blurShift = 0.0f};
        public BlurParameters bottomBlur = new BlurParameters{blurStr = 2.0f, blurWidth = 0.3f, blurShift = 0.0f};

        public Texture2D blurStrengthMask;
        public float blurMaskMultiplier = 1.0f;
        public bool useBlurMask = false;

        [Range(0.0f, 0.01f)] public float depthOffset = 0.0001f;

        const string SHADER_NAME = "Hidden/DigitalHuman/ColorBufferBlurPass";

        private static readonly string[] SHADER_TAG_NAMES =
        {
            "Forward", // HDShaderPassNames.s_ForwardStr
            "ForwardOnly", // HDShaderPassNames.s_ForwardOnlyStr
            "SRPDefaultUnlit", // HDShaderPassNames.s_SRPDefaultUnlitStr
            "DBufferMesh_3RT", // HDShaderPassNames.s_MeshDecalsMStr
            "DBufferMesh", // HDShaderPassNames.s_DBufferMeshStr
        };

        private ShaderTagId[] SHADER_TAG_IDS;

        Material passMaterial;

        public static class Uniforms
        {
            public static readonly int _BlurParams = Shader.PropertyToID("_BlurParams");
            public static readonly int _DepthOffset = Shader.PropertyToID("_DepthOffset");
            public static readonly int _BlurMask = Shader.PropertyToID("_BlurMask");
        }

        static bool EnsureMaterial(ref Material material, string shaderName)
        {
            if (material != null && material.shader == null)
                material = null;

            if (material == null)
                material = CoreUtils.CreateEngineMaterial(SHADER_NAME);

            return (material != null);
        }
        
        protected override void AggregateCullingParameters(ref ScriptableCullingParameters cullingParameters,
            HDCamera hdCamera)
        {
            if (layerMask == 0)
                return;
            cullingParameters.cullingMask |= (uint)layerMask.value;
        }

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            SHADER_TAG_IDS = new ShaderTagId[SHADER_TAG_NAMES.Length];
            for (int i = 0; i < SHADER_TAG_IDS.Length; ++i)
            {
                SHADER_TAG_IDS[i] = new ShaderTagId(SHADER_TAG_NAMES[i]);
            }


            base.Setup(renderContext, cmd);
            base.name = "ColorBufferBlurPass";

            EnsureMaterial(ref passMaterial, SHADER_NAME);
        }

#if UNITY_2020_2_OR_NEWER
		protected override void Execute(CustomPassContext ctx)
		{
			ExecuteColorBufferBlur(
				ctx.renderContext,
				ctx.cmd,
				ctx.hdCamera,
				ctx.cameraColorBuffer,
				ctx.cameraDepthBuffer,
				ctx.cameraNormalBuffer,
				ctx.cullingResults
			);
		}
#else
        protected override void Execute(ScriptableRenderContext renderContext, CommandBuffer cmd, HDCamera hdCamera, CullingResults cullingResults)
        {
            RTHandle cameraColor;
            RTHandle cameraDepth;
            GetCameraBuffers(out cameraColor, out cameraDepth);

            ExecuteColorBufferBlur(
                renderContext,
                cmd,
                hdCamera,
                cameraColor,
                cameraDepth,
                GetNormalBuffer(),
                cullingResults
            );

        }
#endif
        
        void ExecuteColorBufferBlur(ScriptableRenderContext renderContext, CommandBuffer cmd, HDCamera hdCamera, RTHandle cameraColor, RTHandle cameraDepth, RTHandle cameraNormal, CullingResults cullingResults)
        {
            if (!EnsureMaterial(ref passMaterial, SHADER_NAME))
                return;

            if (layerMask == 0)
                return;

            if (!hdCamera.frameSettings.IsEnabled(FrameSettingsField.Decals))
                return;
            
            CoreUtils.SetRenderTarget(cmd, cameraColor, cameraDepth, ClearFlag.None);
            CoreUtils.SetViewport(cmd, cameraColor);
            
            if(useBlurMask)
                passMaterial.EnableKeyword("USE_BLUR_MASK_TEXTURE");
            else
                passMaterial.DisableKeyword("USE_BLUR_MASK_TEXTURE");
            
            RendererListDesc renderListDesc =
                new RendererListDesc(SHADER_TAG_IDS, cullingResults, hdCamera.camera)
                {
                    rendererConfiguration = PerObjectData.None,
                    renderQueueRange = GetRenderQueueRange(queue),
                    sortingCriteria = SortingCriteria.None,
                    layerMask = layerMask,
                    overrideMaterial = passMaterial,
                    overrideMaterialPassIndex = 0,
                    stateBlock = null,
                    excludeObjectMotionVectors = false,
                };

            Vector4[] blurParams =
            {
                new Vector4(leftBlur.blurStr, leftBlur.blurWidth, leftBlur.blurShift, blurMaskMultiplier),
                new Vector4(rightBlur.blurStr, rightBlur.blurWidth, rightBlur.blurShift, 0.0f),
                new Vector4(bottomBlur.blurStr, bottomBlur.blurWidth, bottomBlur.blurShift, 0.0f),
                new Vector4(topBlur.blurStr, topBlur.blurWidth, topBlur.blurShift, 0.0f)
            };


            passMaterial.SetVectorArray(Uniforms._BlurParams, blurParams);
            passMaterial.SetFloat(Uniforms._DepthOffset, depthOffset);
            passMaterial.SetTexture(Uniforms._BlurMask, blurStrengthMask ? blurStrengthMask : Texture2D.grayTexture);

#if UNITY_2021_2_OR_NEWER
			CoreUtils.DrawRendererList(renderContext, cmd, renderContext.CreateRendererList(renderListDesc));
#elif UNITY_2020_2_OR_NEWER
			CoreUtils.DrawRendererList(renderContext, cmd, RendererList.Create(renderListDesc));
#else
            HDUtils.DrawRendererList(renderContext, cmd, RendererList.Create(renderListDesc));
#endif
        }
    }
}