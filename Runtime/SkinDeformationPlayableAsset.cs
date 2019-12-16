using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Unity.DemoTeam.DigitalHuman
{
	[Serializable]
	public class SkinDeformationPlayableAsset : PlayableAsset, ITimelineClipAsset
	{
		public SkinDeformationClip clip;

		public override Playable CreatePlayable(PlayableGraph graph, GameObject go)
		{
			var playable = ScriptPlayable<SkinDeformationPlayable>.Create(graph);
			var playableBehaviour = playable.GetBehaviour();
			{
				playableBehaviour.clip = clip;
			}
			return playable;
		}

		public ClipCaps clipCaps
		{
			get
			{
				return ClipCaps.Blending | ClipCaps.ClipIn | ClipCaps.Extrapolation | ClipCaps.SpeedMultiplier;
			}
		}

		public override double duration
		{
			get
			{
				if (clip != null)
					return clip.Duration;
				else
					return 3.0;
			}
		}
	}
}
