using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Experimental.Rendering;

//	basic procedure:
//
//	1. allocate temp
//		a. custom stencil
//		b. custom color (R8)
//		c. custom normal (ARGBHalf)
//
//	2. clear
//		a. write 0 -> custom stencil
//		b. write 1 -> custom color
//
//	3. copy depth
//		a. write camera depth -> custom depth
//
//	4. render decals
//		a. write 1 -> custom stencil
//		b. write 0 -> custom color
//
//	5. enable stencil
//
//	6. render fullscreen
//		a. write decoded normal -> custom normal
//
//	7. render fullscreen
//		a. write blurred decoded normal -> normal
//
//	8. free temp

public class NormalBufferBlurPass : CustomPass
{
	[HideInInspector] public RenderQueueType queue = RenderQueueType.AllOpaque;
	[HideInInspector] public LayerMask layerMask = 0;// default to 'None'

	static readonly int idInputDepth = Shader.PropertyToID("_InputDepth");
	static readonly int rtStencil = Shader.PropertyToID("_NormalBufferBlur_Stencil");
	static readonly int rtRegions = Shader.PropertyToID("_NormalBufferBlur_Regions");
	static readonly int rtDecoded = Shader.PropertyToID("_NormalBufferBlur_Decoded");

	const string NAME_SHADER = "Hidden/DigitalHuman/NormalBufferBlur";

	static readonly string[] NAME_PASS_REPLACE = new string[]
	{
		"Forward",// HDShaderPassNames.s_ForwardStr
		"ForwardOnly",// HDShaderPassNames.s_ForwardOnlyStr
		"SRPDefaultUnlit", // HDShaderPassNames.s_SRPDefaultUnlitStr
		"DBufferMesh_3RT",// HDShaderPassNames.s_MeshDecalsMStr
	};
	static ShaderTagId[] NAME_PASS_REPLACE_TAG = null;

	const int PASS_COPY_DEPTH = 0;
	const int PASS_MARK = 1;
	const int PASS_DECODE = 2;
	const int PASS_BLUR_AND_ENCODE = 3;

	Material passMaterial;

	static Material CreateMaterial(string shaderName)
	{
		var material = null as Material;
		{
			var shader = Shader.Find(shaderName);
			if (shader != null)
			{
				material = new Material(shader);
				material.hideFlags = HideFlags.HideAndDontSave;
			}

			//if (material != null)
			//	Debug.Log("created material for " + shaderName);
			//else
			//	Debug.LogError("FAILED to create material for " + shaderName);
		}
		return material;
	}

	static bool EnsureMaterial(ref Material material, string shaderName)
	{
		if (material != null && material.shader == null)
			material = null;

		if (material == null)
			material = CreateMaterial(shaderName);

		return (material != null);
	}

	protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
	{
		base.Setup(renderContext, cmd);

		EnsureMaterial(ref passMaterial, NAME_SHADER);

		if (NAME_PASS_REPLACE_TAG == null)
		{
			NAME_PASS_REPLACE_TAG = new ShaderTagId[NAME_PASS_REPLACE.Length];
			for (int i = 0; i != NAME_PASS_REPLACE_TAG.Length; i++)
			{
				NAME_PASS_REPLACE_TAG[i] = new ShaderTagId(NAME_PASS_REPLACE[i]);
			}
		}
	}

	protected override void Execute(ScriptableRenderContext renderContext, CommandBuffer cmd, HDCamera hdCamera, CullingResults cullingResults)
	{
		Profiler.BeginSample("NormalBufferBlur");
		ExecuteNormalBufferBlur(renderContext, cmd, hdCamera, cullingResults);
		Profiler.EndSample();
	}

	void ExecuteNormalBufferBlur(ScriptableRenderContext renderContext, CommandBuffer cmd, HDCamera hdCamera, CullingResults cullingResults)
	{
		if (!EnsureMaterial(ref passMaterial, NAME_SHADER))
			return;

		if (layerMask == 0)
			return;

		if (!hdCamera.frameSettings.IsEnabled(FrameSettingsField.Decals))
			return;

		int viewportW = hdCamera.actualWidth;
		int viewportH = hdCamera.actualHeight;

		//Debug.Log("custom blur pass, w = " + viewportW + ", h = " + viewportH);

		RTHandle cameraColor;
		RTHandle cameraDepth;
		GetCameraBuffers(out cameraColor, out cameraDepth);

		// allocate temporary buffers
		cmd.GetTemporaryRT(rtStencil, viewportW, viewportH, (int)DepthBits.Depth24, FilterMode.Point, RenderTextureFormat.Depth);
		cmd.GetTemporaryRT(rtRegions, viewportW, viewportH, (int)DepthBits.None, FilterMode.Point, RenderTextureFormat.R8, RenderTextureReadWrite.Linear, 1, false);
		cmd.GetTemporaryRT(rtDecoded, viewportW, viewportH, (int)DepthBits.None, FilterMode.Point, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear, 1, false);

		// copy depth from camera depth
		CoreUtils.SetRenderTarget(cmd,
			rtRegions, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
			rtStencil, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare,
			ClearFlag.All, Color.white
		);

		cmd.SetGlobalTexture(idInputDepth, cameraDepth);
		cmd.DrawProcedural(Matrix4x4.identity, passMaterial, PASS_COPY_DEPTH, MeshTopology.Triangles, 3, 1);

		// render decals to mark blur regions
		var renderListDesc = new RendererListDesc(NAME_PASS_REPLACE_TAG, cullingResults, hdCamera.camera)
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
			rtStencil, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare,
			ClearFlag.None
		);

		cmd.SetRandomWriteTarget(1, GetNormalBuffer());
		cmd.DrawProcedural(Matrix4x4.identity, passMaterial, PASS_DECODE, MeshTopology.Triangles, 3, 1);
		cmd.ClearRandomWriteTargets();

		// blur and re-encode normals in marked regions
		CoreUtils.SetRenderTarget(cmd,
			rtStencil, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare,
			ClearFlag.None
		);

		cmd.SetGlobalTexture(rtRegions, rtRegions);
		cmd.SetGlobalTexture(rtDecoded, rtDecoded);

		cmd.SetRandomWriteTarget(1, GetNormalBuffer());
		cmd.DrawProcedural(Matrix4x4.identity, passMaterial, PASS_BLUR_AND_ENCODE, MeshTopology.Triangles, 3, 1);
		cmd.ClearRandomWriteTargets();

		// free temporary buffers
		cmd.ReleaseTemporaryRT(rtStencil);
		cmd.ReleaseTemporaryRT(rtRegions);
		cmd.ReleaseTemporaryRT(rtDecoded);
	}

	protected override void Cleanup()
	{
		base.Cleanup();
	}
}
