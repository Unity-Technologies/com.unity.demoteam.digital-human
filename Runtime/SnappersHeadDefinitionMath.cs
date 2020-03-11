using System;
using UnityEngine;

namespace Unity.DemoTeam.DigitalHuman
{
	public static class SnappersHeadDefinitionMath
	{
		public static float clamp(float value, float min, float max)
		{
			return Mathf.Clamp(value, min, max);
		}

		public static float min(float a, float b)
		{
			return Mathf.Min(a, b);
		}

		public static float max(float a, float b)
		{
			return Mathf.Max(a, b);
		}

		public static float hermite(float p0, float p1, float r0, float r1, float t)
		{
			var t2 = t * t;
			var t3 = t2 * t;
			var _3t2 = 3.0f * t2;
			var _2t3 = 2.0f * t3;
			return (p0 * (_2t3 - _3t2 + 1.0f) + p1 * (-_2t3 + _3t2) + r0 * (t3 - 2.0f * t2 + t) + r1 * (t3 - t2));
		}

		public static float linstep(float start, float end, float parameter)
		{
			return Mathf.InverseLerp(start, end, parameter);
		}
	}
}
