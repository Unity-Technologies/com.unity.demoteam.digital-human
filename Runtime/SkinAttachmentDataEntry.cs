using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Unity.DemoTeam.DigitalHuman
{
	using SkinAttachmentItem = SkinAttachmentItem3;
	
	[PreferBinarySerialization]
	public class SkinAttachmentDataEntry : ScriptableObject
	{
		public Hash128 hashKey;
		public SkinAttachmentPose[] poses;
		public SkinAttachmentItem[] items;
	}
}
