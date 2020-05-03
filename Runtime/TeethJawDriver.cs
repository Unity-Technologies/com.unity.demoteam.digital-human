using UnityEngine;

namespace Unity.DemoTeam.DigitalHuman
{
	[ExecuteAlways]
	public class TeethJawDriver : MonoBehaviour
	{
		public Transform head;
		public Transform upperJaw;
		public Transform jaw;
		public Transform chin;

		public Vector3 neutralUpperJawPos;
		public Quaternion neutralUpperJawRot;

		public Vector3 neutralJawPos;
		public Quaternion neutralJawRot;

		public Vector3 neutralChinPos;
		public Vector3 neutralChinDir;

		[Range(0.0f, 2.0f)]
		public float jawForward = 1.0f;

		[ContextMenu("Initialize Neutral Position")]
		void Initialize()
		{
			if (!head || !upperJaw | !jaw || !chin)
				return;

			neutralUpperJawPos = head.InverseTransformPoint(upperJaw.position);
			neutralUpperJawRot = Quaternion.Inverse(head.rotation) * upperJaw.rotation;

			neutralJawPos = head.InverseTransformPoint(jaw.position);
			neutralJawRot = Quaternion.Inverse(head.rotation) * jaw.rotation;

			neutralChinPos = head.InverseTransformPoint(chin.position);
			neutralChinDir = Vector3.Normalize(neutralChinPos - neutralJawPos);
		}

		void LateUpdate()
		{
			if (!head || !upperJaw || !jaw || !chin)
				return;

			upperJaw.position = head.TransformPoint(neutralUpperJawPos);
			upperJaw.rotation = head.rotation * neutralUpperJawRot;

			var currentChinPos = head.InverseTransformPoint(chin.position);
			var currentChinVec = currentChinPos - neutralJawPos;
			var neutralChinVec = neutralChinPos - neutralJawPos;

			var currentChinDir = Vector3.Normalize(currentChinVec);
			var currentChinRot = Quaternion.FromToRotation(neutralChinDir, currentChinDir);

			var jawForwardDir = head.InverseTransformDirection(chin.forward);
			var jawForwardVec = jawForwardDir * Vector3.Dot(jawForwardDir, currentChinVec - currentChinRot * neutralChinVec);

			var currentJawPos = neutralJawPos + jawForwardVec * jawForward;
			var currentJawRot = currentChinRot * neutralJawRot;

			jaw.position = head.TransformPoint(currentJawPos);
			jaw.rotation = head.rotation * currentJawRot;
		}

		void OnDisable()
		{
			if (!head || !upperJaw || !jaw || !chin)
				return;

			upperJaw.position = head.TransformPoint(neutralUpperJawPos);
			upperJaw.rotation = head.rotation * neutralUpperJawRot;

			jaw.position = head.TransformPoint(neutralJawPos);
			jaw.rotation = head.rotation * neutralJawRot;
		}
	}
}