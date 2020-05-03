using System;
using UnityEngine;

namespace Unity.DemoTeam.DigitalHuman
{
	[Serializable]
	public struct SkinDeformation// => SkinDeformation.Frame
	{
		public Texture2D albedo;

		[NonSerialized] public Vector3[] deltaPositions;
		[NonSerialized] public Vector3[] deltaNormals;

		[NonSerialized] public float[] fittedWeights;

		public void Allocate(int vertexCount, int fittedWeightsCount)
		{
			ArrayUtils.ResizeChecked(ref deltaPositions, vertexCount);
			ArrayUtils.ResizeChecked(ref deltaNormals, vertexCount);
			ArrayUtils.ResizeChecked(ref fittedWeights, fittedWeightsCount);

			for (int i = 0; i != vertexCount; i++)
			{
				deltaPositions[i] = Vector3.zero;
				deltaNormals[i] = Vector3.zero;
			}

			for (int i = 0; i != fittedWeightsCount; i++)
			{
				fittedWeights[i] = 0.0f;
			}
		}

		public void SetAlbedo(Texture2D albedo)
		{
			this.albedo = albedo;
		}

		public void SetDeltas(MeshBuffers buffersFrame0, MeshBuffers buffersFrameX)
		{
			int vertexCount = buffersFrame0.vertexCount;
			if (vertexCount < buffersFrameX.vertexCount)
			{
				Debug.LogWarning("target has more vertices (" + buffersFrameX.vertexCount + " vs. " + vertexCount + "), ignoring excess");
			}
			else if (vertexCount > buffersFrameX.vertexCount)
			{
				Debug.LogError("target has too few vertices (" + buffersFrameX.vertexCount + " vs. " + vertexCount + "), aborting");
				return;
			}

			ArrayUtils.ResizeChecked(ref deltaPositions, vertexCount);
			ArrayUtils.ResizeChecked(ref deltaNormals, vertexCount);

			for (int i = 0; i != vertexCount; i++)
			{
				deltaPositions[i] = buffersFrameX.vertexPositions[i] - buffersFrame0.vertexPositions[i];
				deltaNormals[i] = buffersFrameX.vertexNormals[i] - buffersFrame0.vertexNormals[i];
			}
		}
	}
}
