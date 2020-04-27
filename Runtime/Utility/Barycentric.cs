using System;
using UnityEngine;

namespace Unity.DemoTeam.DigitalHuman
{
	[Serializable]
	public struct Barycentric
	{
		public float u;
		public float v;
		public float w;

		public Barycentric(ref Vector3 q, ref Vector3 a, ref Vector3 b, ref Vector3 c)
		{
			// compute (u, v, w) for point q in plane spanned by triangle (a, b, c)
			// https://gamedev.stackexchange.com/a/23745

			//Vector3 v0 = b - a;
			float x0 = b.x - a.x;
			float y0 = b.y - a.y;
			float z0 = b.z - a.z;
			//Vector3 v1 = c - a;
			float x1 = c.x - a.x;
			float y1 = c.y - a.y;
			float z1 = c.z - a.z;
			//Vector3 v2 = q - a;
			float x2 = q.x - a.x;
			float y2 = q.y - a.y;
			float z2 = q.z - a.z;

			float d00 = x0 * x0 + y0 * y0 + z0 * z0;//Vector3.Dot(v0, v0);
			float d01 = x0 * x1 + y0 * y1 + z0 * z1;//Vector3.Dot(v0, v1);
			float d11 = x1 * x1 + y1 * y1 + z1 * z1;//Vector3.Dot(v1, v1);
			float d20 = x2 * x0 + y2 * y0 + z2 * z0;//Vector3.Dot(v2, v0);
			float d21 = x2 * x1 + y2 * y1 + z2 * z1;//Vector3.Dot(v2, v1);

			float denom = d00 * d11 - d01 * d01;
			v = (d11 * d20 - d01 * d21) / denom;
			w = (d00 * d21 - d01 * d20) / denom;
			u = 1.0f - v - w;
		}

		public Vector3 Resolve(ref Vector3 a, ref Vector3 b, ref Vector3 c)
		{
			//return a * u + b * v + c * w;
			Vector3 q;
			q.x = a.x * u + b.x * v + c.x * w;
			q.y = a.y * u + b.y * v + c.y * w;
			q.z = a.z * u + b.z * v + c.z * w;
			return q;
		}

		public bool Within()
		{
			return (u >= 0.0f && u <= 1.0f) && (v >= 0.0f && v <= 1.0f) && (w >= 0.0f && w <= 1.0f);
		}
	}
}
