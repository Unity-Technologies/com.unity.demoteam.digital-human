using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Unity.DemoTeam.DigitalHuman
{
	[Serializable]
	[TrackColor(0.8f, 0.4f, 0.4f)]
	[TrackBindingType(typeof(SkinDeformationRenderer))]
	[TrackClipType(typeof(SkinDeformationPlayableAsset))]
	public class SkinDeformationTimeline : TrackAsset
	{
		public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
		{
			foreach (TimelineClip timelineClip in m_Clips)
			{
				var clipAsset = timelineClip.asset as SkinDeformationPlayableAsset;
				if (clipAsset != null)
				{
					timelineClip.displayName = clipAsset.clip.name;
				}
			}

			return ScriptPlayable<SkinDeformationPlayableMixer>.Create(graph, inputCount);
		}
	}

	[Serializable]
	public class SkinDeformationPlayableMixer : PlayableBehaviour
	{
		public override void ProcessFrame(Playable playable, FrameData info, object playerData)
		{
			var target = playerData as SkinDeformationRenderer;
			if (target == null)
				return;

			var clipCount = playable.GetInputCount();
			if (clipCount <= 0)
				return;

			var inputIndexA = -1;
			var inputIndexB = -1;

			var inputWeightA = 0.0f;
			var inputWeightB = 0.0f;

			for (int i = 0; i < clipCount; i++)
			{
				var clipWeight = playable.GetInputWeight(i);
				if (clipWeight > 0.0f)
				{
					if (inputIndexA == -1)
					{
						inputIndexA = i;
						inputWeightA = clipWeight;
						continue;
					}
					if (inputIndexB == -1)
					{
						inputIndexB = i;
						inputWeightB = clipWeight;
						break;
					}
				}
			}

			if (inputIndexB != -1)
			{
				//Debug.Log("dual clip: A=" + inputWeightA + ", B=" + inputWeightB);

				var inputA = playable.GetInput(inputIndexA);
				var inputB = playable.GetInput(inputIndexB);

				var assetA = ((ScriptPlayable<SkinDeformationPlayable>)inputA).GetBehaviour().clip;
				var assetB = ((ScriptPlayable<SkinDeformationPlayable>)inputB).GetBehaviour().clip;

				target.SetBlendInput(0, assetA, (float)(inputA.GetTime() / assetA.Duration), inputWeightA);
				target.SetBlendInput(1, assetB, (float)(inputB.GetTime() / assetB.Duration), inputWeightB);
			}
			else
			{
				//Debug.Log("single clip: A=" + inputWeightA);

				if (inputIndexA == -1)
					inputIndexA = 0;

				var inputA = playable.GetInput(inputIndexA);
				var assetA = ((ScriptPlayable<SkinDeformationPlayable>)inputA).GetBehaviour().clip;

				target.SetBlendInput(0, assetA, (float)(inputA.GetTime() / assetA.Duration), inputWeightA);
				target.SetBlendInput(1, null, 0.0f, 0.0f);
			}
		}
	}
}
