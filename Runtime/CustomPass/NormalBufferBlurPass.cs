using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Experimental.Rendering;
using System.Reflection;

namespace Unity.DemoTeam.DigitalHuman
{
	//	basic procedure:
	//
	//	1. alloc temp
	//		b. custom color (R8)
	//		c. custom normal (ARGBHalf)
	//
	//	2. clear
	//		b. write 1 -> custom color
	//
	//	3. mark decals
	//		a. write 0 -> custom color
	//		b. write 1 -> custom stencil
	//
	//	4. enable stencil
	//
	//	5. render fullscreen
	//		a. write decoded normal -> custom normal
	//
	//	6. render fullscreen
	//		a. write blurred decoded normal -> normal
	//
	//	7. free temp

	public class NormalBufferBlurPass : CustomPass
	{
		[HideInInspector] public RenderQueueType queue = RenderQueueType.AllOpaque;
		[HideInInspector] public LayerMask layerMask = 0;// default to 'None'

		static readonly int rtRegions = Shader.PropertyToID("_NormalBufferBlur_Regions");
		static readonly int rtDecoded = Shader.PropertyToID("_NormalBufferBlur_Decoded");

		const string NAME_SHADER = "Hidden/DigitalHuman/NormalBufferBlurPass";

		static readonly string[] NAME_PASS_REPLACE = new string[]
		{
			"Forward",// HDShaderPassNames.s_ForwardStr
			"ForwardOnly",// HDShaderPassNames.s_ForwardOnlyStr
			"SRPDefaultUnlit", // HDShaderPassNames.s_SRPDefaultUnlitStr
			"DBufferMesh_3RT",// HDShaderPassNames.s_MeshDecalsMStr
		};
		static ShaderTagId[] NAME_PASS_REPLACE_TAG = null;

		const int PASS_MARK = 0;
		const int PASS_DECODE = 1;
		const int PASS_BLUR_AND_ENCODE = 2;
		const int PASS_BLUR_AND_ENCODE_AND_DECAL = 3;

		Material passMaterial;

		const int DBUFFER_NORMALS = 1;
		const int DBUFFER_MASK = 2;

		private RTHandle[] dbufferRTs;
		private RenderTargetIdentifier[] dbufferRTIDs;
		private RenderTargetIdentifier[] dbufferNormalMaskRTIDs;

		private void FindDbufferRTs(HDRenderPipeline hdPipeline)
		{
			dbufferRTIDs = null;

			var fieldInfo_m_DbufferManager = typeof(HDRenderPipeline).GetField("m_DbufferManager", BindingFlags.NonPublic | BindingFlags.Instance);
			if (fieldInfo_m_DbufferManager != null)
			{
				//Debug.Log("FindDbufferRTs : " + fieldInfo_m_DbufferManager);
				var m_DbufferManager = fieldInfo_m_DbufferManager.GetValue(hdPipeline);
				if (m_DbufferManager != null)
				{
					var fieldInfo_m_RTs = m_DbufferManager.GetType().GetField("m_RTs", BindingFlags.NonPublic | BindingFlags.Instance);
					if (fieldInfo_m_RTs != null)
					{
						//Debug.Log("FindDbufferRTs : " + fieldInfo_m_RTs);
						dbufferRTs = fieldInfo_m_RTs.GetValue(m_DbufferManager) as RTHandle[];
					}

					var fieldInfo_m_RTIDs = m_DbufferManager.GetType().GetField("m_RTIDs", BindingFlags.NonPublic | BindingFlags.Instance);
					if (fieldInfo_m_RTIDs != null)
					{
						//Debug.Log("FindDbufferRTIDs : " + fieldInfo_m_RTIDs);
						dbufferRTIDs = fieldInfo_m_RTIDs.GetValue(m_DbufferManager) as RenderTargetIdentifier[];
					}
				}
			}
		}

		private RenderTargetIdentifier[] GetDbufferNormalMaskRTIDs()
		{
			if (dbufferRTs != null)
			{
				if (dbufferRTs[DBUFFER_NORMALS] == null || dbufferRTs[DBUFFER_MASK] == null)
				{
					return null;
				}
			}

			if (dbufferRTIDs != null)
			{
				if (dbufferNormalMaskRTIDs == null)
					dbufferNormalMaskRTIDs = new RenderTargetIdentifier[2];

				dbufferNormalMaskRTIDs[0] = dbufferRTIDs[DBUFFER_NORMALS];
				dbufferNormalMaskRTIDs[1] = dbufferRTIDs[DBUFFER_MASK];
			}

			return dbufferNormalMaskRTIDs;
		}

		static bool EnsureMaterial(ref Material material, string shaderName)
		{
			if (material != null && material.shader == null)
				material = null;

			if (material == null)
				material = CoreUtils.CreateEngineMaterial(shaderName);

			return (material != null);
		}

		protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
		{
			base.Setup(renderContext, cmd);
			base.name = "NormalBufferBlurPass";

			FindDbufferRTs(RenderPipelineManager.currentPipeline as HDRenderPipeline);

			EnsureMaterial(ref passMaterial, NAME_SHADER);
			if (passMaterial != null)
			{
				passMaterial.SetInt("_StencilBit", (int)UserStencilUsage.UserBit0);
			}

			if (NAME_PASS_REPLACE_TAG == null)
			{
				NAME_PASS_REPLACE_TAG = new ShaderTagId[NAME_PASS_REPLACE.Length];
				for (int i = 0; i != NAME_PASS_REPLACE_TAG.Length; i++)
				{
					NAME_PASS_REPLACE_TAG[i] = new ShaderTagId(NAME_PASS_REPLACE[i]);
				}
			}
		}

#if UNITY_2020_2_OR_NEWER
		protected override void Execute(CustomPassContext ctx)
		{
			Profiler.BeginSample("NormalBufferBlurPass");
			ExecuteNormalBufferBlur(
				ctx.renderContext,
				ctx.cmd,
				ctx.hdCamera,
				ctx.cameraColorBuffer,
				ctx.cameraDepthBuffer,
				ctx.cameraNormalBuffer,
				ctx.cullingResults
			);
			Profiler.EndSample();
		}
#else
		protected override void Execute(ScriptableRenderContext renderContext, CommandBuffer cmd, HDCamera hdCamera, CullingResults cullingResults)
		{
			RTHandle cameraColor;
			RTHandle cameraDepth;
			GetCameraBuffers(out cameraColor, out cameraDepth);

			Profiler.BeginSample("NormalBufferBlurPass");
			ExecuteNormalBufferBlur(
				renderContext,
				cmd,
				hdCamera,
				cameraColor,
				cameraDepth,
				GetNormalBuffer(),
				cullingResults
			);
			Profiler.EndSample();
		}
#endif

		void ExecuteNormalBufferBlur(ScriptableRenderContext renderContext, CommandBuffer cmd, HDCamera hdCamera, RTHandle cameraColor, RTHandle cameraDepth, RTHandle cameraNormal, CullingResults cullingResults)
		{
			if (!EnsureMaterial(ref passMaterial, NAME_SHADER))
				return;

			if (layerMask == 0)
				return;

			if (!hdCamera.frameSettings.IsEnabled(FrameSettingsField.Decals))
				return;

			int bufferW = cameraColor.rt.width;
			int bufferH = cameraColor.rt.height;

			// allocate temporary buffers
			cmd.GetTemporaryRT(rtRegions, bufferW, bufferH, (int)DepthBits.None, FilterMode.Point, RenderTextureFormat.R8, RenderTextureReadWrite.Linear, 1, false);
			cmd.GetTemporaryRT(rtDecoded, bufferW, bufferH, (int)DepthBits.None, FilterMode.Point, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear, 1, false);

			// render decals to mark blur regions
			CoreUtils.SetRenderTarget(cmd,
				rtRegions, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
				cameraDepth, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
				ClearFlag.Color, Color.white
			);
			CoreUtils.SetViewport(cmd, cameraDepth);

			RendererListDesc renderListDesc = new RendererListDesc(NAME_PASS_REPLACE_TAG, cullingResults, hdCamera.camera)
			{
				rendererConfiguration = PerObjectData.None,
				renderQueueRange = GetRenderQueueRange(queue),
				sortingCriteria = SortingCriteria.None,
				layerMask = layerMask,
				overrideMaterial = passMaterial,
				overrideMaterialPassIndex = PASS_MARK,
				stateBlock = null,
				excludeObjectMotionVectors = false,
			};

			HDUtils.DrawRendererList(renderContext, cmd, RendererList.Create(renderListDesc));

			// decode normal buffer in marked regions
			CoreUtils.SetRenderTarget(cmd,
				rtDecoded, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
				cameraDepth, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare,
				ClearFlag.None
			);
			CoreUtils.SetViewport(cmd, cameraDepth);

			cmd.SetRandomWriteTarget(2, cameraNormal);
			cmd.DrawProcedural(Matrix4x4.identity, passMaterial, PASS_DECODE, MeshTopology.Triangles, 3, 1);
			cmd.ClearRandomWriteTargets();

			// blur and re-encode normals in marked regions
			cmd.SetGlobalTexture(rtRegions, rtRegions);
			cmd.SetGlobalTexture(rtDecoded, rtDecoded);

			if (GetDbufferNormalMaskRTIDs() != null)
			{
				CoreUtils.SetRenderTarget(cmd,
					GetDbufferNormalMaskRTIDs(),
					cameraDepth,
					ClearFlag.None);
				CoreUtils.SetViewport(cmd, cameraDepth);

				cmd.SetRandomWriteTarget(2, cameraNormal);
				cmd.DrawProcedural(Matrix4x4.identity, passMaterial, PASS_BLUR_AND_ENCODE_AND_DECAL, MeshTopology.Triangles, 3, 1);
				cmd.ClearRandomWriteTargets();
			}
			else
			{
				CoreUtils.SetRenderTarget(cmd,
					cameraDepth, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
					ClearFlag.None
				);
				CoreUtils.SetViewport(cmd, cameraDepth);

				cmd.SetRandomWriteTarget(2, cameraNormal);
				cmd.DrawProcedural(Matrix4x4.identity, passMaterial, PASS_BLUR_AND_ENCODE, MeshTopology.Triangles, 3, 1);
				cmd.ClearRandomWriteTargets();
			}

			// free temporary buffers
			cmd.ReleaseTemporaryRT(rtRegions);
			cmd.ReleaseTemporaryRT(rtDecoded);
		}

		protected override void Cleanup()
		{
			base.Cleanup();
		}
	}
}
