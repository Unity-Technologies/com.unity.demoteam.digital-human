using System;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.DemoTeam.Attributes;

namespace Unity.DemoTeam.DigitalHuman
{
	[ExecuteAlways]
	public class SkinAttachment : MeshInstanceBehaviour
	{
		public enum AttachmentMode
		{
			BuildPoses,
			LinkPosesByReference,
			LinkPosesBySpecificIndex,
		}

		public enum AttachmentType
		{
			Transform,
			Mesh,
			MeshRoots,
		}

		[HideInInspector] public bool attached;
		[HideInInspector] public Vector3 attachedLocalPosition;
		[HideInInspector] public Quaternion attachedLocalRotation;

		[HideInInspector]
		public SkinAttachmentTarget targetActive;

		[EditableIf("attached", false)]
		public SkinAttachmentTarget target;

		[EditableIf("attached", false)]
		public AttachmentType attachmentType = AttachmentType.Transform;

		[EditableIf("attached", false)]
		public AttachmentMode attachmentMode = AttachmentMode.BuildPoses;

		[EditableIf("attached", false)]
		public SkinAttachment attachmentLink = null;

		[EditableIf("attachmentMode", AttachmentMode.LinkPosesBySpecificIndex)]
		public int attachmentIndex = -1;

		[EditableIf("attachmentMode", AttachmentMode.LinkPosesBySpecificIndex)]
		public int attachmentCount = 0;

		[HideInInspector]
		public ulong checksum0 = 0;
		[HideInInspector]
		public ulong checksum1 = 0;

		[Header("Debug options")]
		public bool showBounds = false;
		public bool showIslands = false;
		public bool showRootLines = false;
		private const int debugColorsSize = 7;
		private static Color[] debugColors = new Color[debugColorsSize] { Color.red, Color.green, Color.blue, Color.cyan, Color.magenta, Color.yellow, Color.white };
		private static SkinAttachmentData debugData;

		[Header("Runtime options")]
		public bool forceRecalculateBounds;
		public bool forceRecalculateNormals;
		public bool forceRecalculateTangents;

		[NonSerialized] public float meshAssetRadius;
		[NonSerialized] public MeshBuffers meshBuffers;
		[NonSerialized] public MeshAdjacency meshAdjacency;
		[NonSerialized] public MeshIslands meshIslands;

		[NonSerialized] public Transform skinningBone;
		[NonSerialized] public Matrix4x4 skinningBoneBindPose;
		[NonSerialized] public Matrix4x4 skinningBoneBindPoseInverse;

		public Matrix4x4 GetWorldToLocalSkinning()
		{
			if (skinningBone != null)
				return (skinningBoneBindPoseInverse * skinningBone.worldToLocalMatrix);
			else
				return (this.transform.worldToLocalMatrix);
		}

		public Matrix4x4 GetLocalSkinningToWorld()
		{
			if (skinningBone != null)
				return (skinningBone.localToWorldMatrix * skinningBoneBindPose);
			else
				return (this.transform.localToWorldMatrix);
		}

		void DiscoverSkinningBone()
		{
			skinningBone = null;
			skinningBoneBindPose = Matrix4x4.identity;
			skinningBoneBindPoseInverse = Matrix4x4.identity;

			// search for skinning bone
			var smr = GetComponent<SkinnedMeshRenderer>();
			if (smr != null)
			{
				int skinningBoneIndex = -1;

				unsafe
				{
					var boneWeights = meshAsset.GetAllBoneWeights();
					var boneWeightPtr = (BoneWeight1*)boneWeights.GetUnsafeReadOnlyPtr();

					for (int i = 0; i != boneWeights.Length; i++)
					{
						if (boneWeightPtr[i].weight > 0.0f)
						{
							if (skinningBoneIndex == -1)
								skinningBoneIndex = boneWeightPtr[i].boneIndex;

							if (skinningBoneIndex != boneWeightPtr[i].boneIndex)
							{
								skinningBoneIndex = -1;
								break;
							}
						}
					}
				}

				if (skinningBoneIndex != -1)
				{
					skinningBone = smr.bones[skinningBoneIndex];
					skinningBoneBindPose = meshInstance.bindposes[skinningBoneIndex];
					skinningBoneBindPoseInverse = skinningBoneBindPose.inverse;
					//Debug.Log("discovered skinning bone for " + this.name + " : " + skinningBone.name);
				}
			}
		}

		protected override void OnMeshInstanceCreated()
		{
			meshAssetRadius = meshAsset.bounds.extents.magnitude;// conservative

			if (meshBuffers == null)
				meshBuffers = new MeshBuffers(meshInstance);
			else
				meshBuffers.LoadFrom(meshInstance);

			if (meshAdjacency == null)
				meshAdjacency = new MeshAdjacency(meshBuffers);
			else
				meshAdjacency.LoadFrom(meshBuffers);

			if (meshIslands == null)
				meshIslands = new MeshIslands(meshAdjacency);
			else
				meshIslands.LoadFrom(meshAdjacency);

			DiscoverSkinningBone();
		}

		protected override void OnMeshInstanceDeleted()
		{
			// do nothing
		}

		public Hash128 Checksum()
		{
			return new Hash128(checksum0, checksum1);
		}

		public bool ChecksumCompare(in SkinAttachmentData data)
		{
			return (checksum0 == data.checksum0) && (checksum1 == data.checksum1);
		}

		public void RevertVertexData()
		{
			if (meshAsset != null)
			{
				if (meshBuffers == null)
					meshBuffers = new MeshBuffers(meshAsset);
				else
					meshBuffers.LoadFrom(meshAsset);
			}
		}

		public void Attach(bool storePositionRotation = true)
		{
			EnsureMeshInstance();

			if (targetActive != null)
				targetActive.RemoveSubject(this);

			targetActive = target;
			targetActive.AddSubject(this);

			if (storePositionRotation)
			{
				attachedLocalPosition = transform.localPosition;
				attachedLocalRotation = transform.localRotation;
			}

			attached = true;
		}

		public void Detach(bool revertPositionRotation = true)
		{
			RemoveMeshInstance();

			if (targetActive != null)
				targetActive.RemoveSubject(this);

			if (revertPositionRotation)
			{
				transform.localPosition = attachedLocalPosition;
				transform.localRotation = attachedLocalRotation;
			}

			attached = false;
		}

		void ValidateAttachedState()
		{
			if (attached)
			{
				if (targetActive != null && targetActive == target)
				{
					EnsureMeshInstance();
				}
				else
				{
					Detach();
				}
			}
			else
			{
				RemoveMeshInstance();
			}
		}

		void OnEnable()
		{
			ValidateAttachedState();
		}

		void Update()
		{
			ValidateAttachedState();
		}

		void LateUpdate()
		{
			var forceRecalculateAny = forceRecalculateBounds || forceRecalculateNormals || forceRecalculateTangents;
			if (forceRecalculateAny && meshInstance != null)
			{
				if (forceRecalculateTangents)
					meshInstance.SilentlyRecalculateTangents();
				if (forceRecalculateNormals)
					meshInstance.SilentlyRecalculateNormals();
				if (forceRecalculateBounds)
					meshInstance.SilentlyRecalculateBounds();
			}
		}

		void OnDestroy()
		{
			RemoveMeshInstance();
		}

#if UNITY_EDITOR
		void OnDrawGizmosSelected()
		{
			if (isActiveAndEnabled == false)
				return;

			if (attached)
				DrawGizmosAttached();
			else
				DrawGizmosDetached();
		}

		void DrawGizmosAttached()
		{
			if (attachmentType != AttachmentType.Transform)
			{
				if (meshInstance == null)
					return;

				Gizmos.matrix = this.transform.localToWorldMatrix;

				if (showBounds)
				{
					Gizmos.color = Color.white;
					Gizmos.DrawWireCube(meshInstance.bounds.center, meshInstance.bounds.extents * 2.0f);
				}
			}
		}

		void DrawGizmosDetached()
		{
			if (target == null)
				return;

			var targetMeshInfo = target.GetCachedMeshInfo();
			if (targetMeshInfo.valid == false)
				return;

			if (attachmentType == AttachmentType.Transform)
			{
				// draw sphere with radius to closest vertex
				var closestDist = float.MaxValue;
				var closestNode = -1;

				var targetLocalPos = target.transform.InverseTransformPoint(this.transform.position);
				if (targetMeshInfo.meshVertexBSP.FindNearest(ref closestDist, ref closestNode, ref targetLocalPos))
				{
					Gizmos.matrix = target.transform.localToWorldMatrix;

					var r = targetLocalPos - target.meshBuffers.vertexPositions[closestNode];
					var d = Vector3.Dot(target.meshBuffers.vertexNormals[closestNode], r);
					var c = (d >= 0.0f) ? Color.cyan : Color.magenta;

					Gizmos.color = Color.Lerp(Color.clear, c, 0.25f);
					Gizmos.DrawSphere(targetLocalPos, Mathf.Sqrt(closestDist));

					Gizmos.color = Color.Lerp(Color.clear, c, 0.75f);
					Gizmos.DrawLine(targetLocalPos, target.meshBuffers.vertexPositions[closestNode]);

					foreach (var triangle in targetMeshInfo.meshAdjacency.vertexTriangles[closestNode])
					{
						int _0 = triangle * 3;
						int v0 = target.meshBuffers.triangles[_0];
						int v1 = target.meshBuffers.triangles[_0 + 1];
						int v2 = target.meshBuffers.triangles[_0 + 2];

						Gizmos.DrawLine(target.meshBuffers.vertexPositions[v0], target.meshBuffers.vertexPositions[v1]);
						Gizmos.DrawLine(target.meshBuffers.vertexPositions[v1], target.meshBuffers.vertexPositions[v2]);
						Gizmos.DrawLine(target.meshBuffers.vertexPositions[v2], target.meshBuffers.vertexPositions[v0]);
					}
				}
			}
			else
			{
				EnsureMeshInstance();

				if (meshInstance == null)
					return;

				var subjectPositions = meshBuffers.vertexPositions;

				if (skinningBone != null)
					Gizmos.matrix = skinningBone.localToWorldMatrix * skinningBoneBindPose;
				else
					Gizmos.matrix = this.transform.localToWorldMatrix;

				if (showIslands)
				{
					for (int island = 0; island != meshIslands.islandCount; island++)
					{
						Gizmos.color = Color.Lerp(Color.clear, debugColors[island % debugColors.Length], 0.5f);
						foreach (var i in meshIslands.islandVertices[island])
						{
							foreach (var j in meshAdjacency.vertexVertices[i])
							{
								Gizmos.DrawLine(subjectPositions[i], subjectPositions[j]);
							}
						}
					}
				}

				if (showRootLines)
				{
					if (debugData == null)
					{
						debugData = SkinAttachmentData.CreateInstance<SkinAttachmentData>();
						debugData.hideFlags = HideFlags.HideAndDontSave;
					}
					else
					{
						debugData.Clear();
					}

					int dryRunPoseCount = -1;
					int dryRunItemCount = -1;

					SkinAttachmentDataBuilder.BuildDataAttachSubjectReadOnly(ref debugData, target.transform, target.GetCachedMeshInfo(), this, dryRun: true, ref dryRunPoseCount, ref dryRunItemCount);
					{
						ArrayUtils.ResizeCheckedIfLessThan(ref debugData.pose, dryRunPoseCount);
						ArrayUtils.ResizeCheckedIfLessThan(ref debugData.item, dryRunItemCount);
					}

					SkinAttachmentDataBuilder.BuildDataAttachSubjectReadOnly(ref debugData, target.transform, target.GetCachedMeshInfo(), this, dryRun: false, ref dryRunPoseCount, ref dryRunItemCount);

					Matrix4x4 targetToWorld;
					{
						// NOTE: for skinned targets, targetToWorld specifically excludes scale, since source data (BakeMesh) is already scaled
						if (target.meshBakedSmr != null)
							targetToWorld = Matrix4x4.TRS(target.transform.position, target.transform.rotation, Vector3.one);
						else
							targetToWorld = target.transform.localToWorldMatrix;
					}

					Matrix4x4 targetToSubject;
					{
						if (skinningBone != null)
							targetToSubject = (skinningBoneBindPoseInverse * skinningBone.worldToLocalMatrix) * targetToWorld;
						else
							targetToSubject = this.transform.worldToLocalMatrix * targetToWorld;
					}

					for (int island = 0; island != meshIslands.islandCount; island++)
					{
						Gizmos.color = Color.Lerp(Color.clear, debugColors[island % debugColors.Length], 0.5f);
						foreach (var i in meshIslands.islandVertices[island])
						{
							Vector3 rootOffset = targetToSubject.MultiplyVector(-debugData.item[i].targetOffset);
							Gizmos.DrawRay(subjectPositions[i], rootOffset);
						}
					}
				}
			}
		}
#endif
	}
}
