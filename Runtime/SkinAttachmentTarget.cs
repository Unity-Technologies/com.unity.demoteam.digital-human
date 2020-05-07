using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Burst;

namespace Unity.DemoTeam.DigitalHuman
{
	using static SkinAttachmentDataBuilder;

	[ExecuteAlways]
	public class SkinAttachmentTarget : MonoBehaviour
	{
		public struct MeshInfo
		{
			public MeshBuffers meshBuffers;
			public MeshAdjacency meshAdjacency;
			public KdTree3 meshVertexBSP;
			public bool valid;
		}

		[HideInInspector] public List<SkinAttachment> subjects = new List<SkinAttachment>();

		[NonSerialized] public Mesh meshBakedSmr;
		[NonSerialized] public Mesh meshBakedOrAsset;
		[NonSerialized] public MeshBuffers meshBuffers;
		[NonSerialized] public Mesh meshBuffersLastAsset;

		public SkinAttachmentData attachData;

		[Header("Debug options")]
		public bool showWireframe = false;
		public bool showUVSeams = false;
		public bool showResolved = false;
		public bool showMouseOver = false;

		private MeshInfo cachedMeshInfo;
		private int cachedMeshInfoFrame = -1;

		private JobHandle[] stagingJobs;
		private Vector3[][] stagingData;
		private GCHandle[] stagingPins;

		void OnEnable()
		{
			UpdateMeshBuffers();
		}

		void LateUpdate()
		{
			if (UpdateMeshBuffers())
			{
				ResolveSubjects();
			}
		}

		bool UpdateMeshBuffers()
		{
			meshBakedOrAsset = null;
			{
				var mf = GetComponent<MeshFilter>();
				if (mf != null)
				{
					meshBakedOrAsset = mf.sharedMesh;
				}

				var smr = GetComponent<SkinnedMeshRenderer>();
				if (smr != null)
				{
					if (meshBakedSmr == null)
					{
						meshBakedSmr = new Mesh();
						meshBakedSmr.name = "SkinAttachmentTarget(BakeMesh)";
						meshBakedSmr.hideFlags = HideFlags.HideAndDontSave & ~HideFlags.DontUnloadUnusedAsset;
						meshBakedSmr.MarkDynamic();
					}

					meshBakedOrAsset = meshBakedSmr;

					Profiler.BeginSample("smr.BakeMesh");
					{
						smr.BakeMesh(meshBakedSmr);
						{
							meshBakedSmr.bounds = smr.bounds;
						}
					}
					Profiler.EndSample();
				}
			}

			if (meshBakedOrAsset == null)
				return false;

			if (meshBuffers == null || meshBuffersLastAsset != meshBakedOrAsset)
			{
				meshBuffers = new MeshBuffers(meshBakedOrAsset);
			}
			else
			{
				meshBuffers.LoadPositionsFrom(meshBakedOrAsset);
				meshBuffers.LoadNormalsFrom(meshBakedOrAsset);
			}

			meshBuffersLastAsset = meshBakedOrAsset;
			return true;
		}

		void UpdateMeshInfo(ref MeshInfo info)
		{
			Profiler.BeginSample("upd-mesh-inf");
			if (meshBuffers == null)
			{
				info.valid = false;
			}
			else
			{
				info.meshBuffers = meshBuffers;

				const bool weldedAdjacency = false;//TODO enable for more reliable poses along uv seams

				if (info.meshAdjacency == null)
					info.meshAdjacency = new MeshAdjacency(meshBuffers, weldedAdjacency);
				else if (info.meshAdjacency.vertexCount != meshBuffers.vertexCount)
					info.meshAdjacency.LoadFrom(meshBuffers, weldedAdjacency);

				if (info.meshVertexBSP == null)
					info.meshVertexBSP = new KdTree3(meshBuffers.vertexPositions, meshBuffers.vertexCount);
				else
					info.meshVertexBSP.BuildFrom(meshBuffers.vertexPositions, meshBuffers.vertexCount);

				info.valid = true;
			}
			Profiler.EndSample();
		}

		public ref MeshInfo GetCachedMeshInfo(bool forceRefresh = false)
		{
			int frameIndex = Time.frameCount;
			if (frameIndex != cachedMeshInfoFrame || forceRefresh)
			{
				UpdateMeshInfo(ref cachedMeshInfo);

				if (cachedMeshInfo.valid)
					cachedMeshInfoFrame = frameIndex;
			}
			return ref cachedMeshInfo;
		}

		public void AddSubject(SkinAttachment subject)
		{
			if (subjects.Contains(subject) == false)
				subjects.Add(subject);
		}

		public void RemoveSubject(SkinAttachment subject)
		{
			if (subjects.Contains(subject))
				subjects.Remove(subject);
		}

		public bool CommitRequired()
		{
			if (attachData == null)
				return false;

			if (meshBuffers.vertexCount < attachData.driverVertexCount)
				return true;

			for (int i = 0, n = subjects.Count; i != n; i++)
			{
				if (subjects[i].ChecksumCompare(attachData) == false)
					return true;
			}

			return false;
		}

		public void CommitSubjectsIfRequired()
		{
			if (CommitRequired())
				CommitSubjects();
		}

		public void CommitSubjects()
		{
			if (attachData == null)
				return;

			var meshInfo = GetCachedMeshInfo(forceRefresh: true);
			if (meshInfo.valid == false)
				return;

			attachData.Clear();
			attachData.driverVertexCount = meshInfo.meshBuffers.vertexCount;
			{
				subjects.RemoveAll(p => (p == null));

				// pass 1: dry run
				int dryRunPoseCount = 0;
				int dryRunItemCount = 0;

				for (int i = 0, n = subjects.Count; i != n; i++)
				{
					if (subjects[i].attachmentMode == SkinAttachment.AttachmentMode.BuildPoses)
					{
						subjects[i].RevertVertexData();
						BuildDataAttachSubject(ref attachData, transform, meshInfo, subjects[i], dryRun: true, ref dryRunPoseCount, ref dryRunItemCount);
					}
				}

				dryRunPoseCount = Mathf.NextPowerOfTwo(dryRunPoseCount);
				dryRunItemCount = Mathf.NextPowerOfTwo(dryRunItemCount);

				ArrayUtils.ResizeCheckedIfLessThan(ref attachData.pose, dryRunPoseCount);
				ArrayUtils.ResizeCheckedIfLessThan(ref attachData.item, dryRunItemCount);

				// pass 2: build poses
				for (int i = 0, n = subjects.Count; i != n; i++)
				{
					if (subjects[i].attachmentMode == SkinAttachment.AttachmentMode.BuildPoses)
					{
						BuildDataAttachSubject(ref attachData, transform, meshInfo, subjects[i], dryRun: false, ref dryRunPoseCount, ref dryRunPoseCount);
					}
				}

				// pass 3: reference poses
				for (int i = 0, n = subjects.Count; i != n; i++)
				{
					switch (subjects[i].attachmentMode)
					{
						case SkinAttachment.AttachmentMode.LinkPosesByReference:
							{
								if (subjects[i].attachmentLink != null)
								{
									subjects[i].attachmentType = subjects[i].attachmentLink.attachmentType;
									subjects[i].attachmentIndex = subjects[i].attachmentLink.attachmentIndex;
									subjects[i].attachmentCount = subjects[i].attachmentLink.attachmentCount;
								}
								else
								{
									subjects[i].attachmentIndex = -1;
									subjects[i].attachmentCount = 0;
								}
							}
							break;

						case SkinAttachment.AttachmentMode.LinkPosesBySpecificIndex:
							{
								subjects[i].attachmentIndex = Mathf.Clamp(subjects[i].attachmentIndex, 0, attachData.itemCount - 1);
								subjects[i].attachmentCount = Mathf.Clamp(subjects[i].attachmentCount, 0, attachData.itemCount - subjects[i].attachmentIndex);
							}
							break;
					}
				}
			}
			attachData.subjectCount = subjects.Count;
			attachData.Persist();

			for (int i = 0, n = subjects.Count; i != n; i++)
			{
				subjects[i].checksum0 = attachData.checksum0;
				subjects[i].checksum1 = attachData.checksum1;
#if UNITY_EDITOR
				UnityEditor.EditorUtility.SetDirty(subjects[i]);
				UnityEditor.Undo.ClearUndo(subjects[i]);
#endif
			}
		}

		void ResolveSubjects()
		{
			if (attachData == null)
				return;

			if (attachData.driverVertexCount > meshBuffers.vertexCount)
				return;// prevent out of bounds if mesh shrunk since data was built

			Profiler.BeginSample("resolve-subj-all");

			subjects.RemoveAll(p => p == null);

			//Profiler.BeginSample("sort");
			//subjects.Sort((a, b) => { return b.attachmentCount.CompareTo(a.attachmentCount); });
			//Profiler.EndSample();

			int stagingPinsSourceDataCount = 3;
			int stagingPinsSourceDataOffset = subjects.Count * 2;

			ArrayUtils.ResizeChecked(ref stagingJobs, subjects.Count);
			ArrayUtils.ResizeChecked(ref stagingData, subjects.Count * 2);
			ArrayUtils.ResizeChecked(ref stagingPins, subjects.Count * 2 + stagingPinsSourceDataCount);

			GCHandle attachDataPosePin = GCHandle.Alloc(attachData.pose, GCHandleType.Pinned);
			GCHandle attachDataItemPin = GCHandle.Alloc(attachData.item, GCHandleType.Pinned);

			stagingPins[stagingPinsSourceDataOffset + 0] = GCHandle.Alloc(meshBuffers.vertexPositions, GCHandleType.Pinned);
			stagingPins[stagingPinsSourceDataOffset + 1] = GCHandle.Alloc(meshBuffers.vertexTangents, GCHandleType.Pinned);
			stagingPins[stagingPinsSourceDataOffset + 2] = GCHandle.Alloc(meshBuffers.vertexNormals, GCHandleType.Pinned);

			var targetToWorld = Matrix4x4.TRS(this.transform.position, this.transform.rotation, Vector3.one);
			// NOTE: targetToWorld specifically excludes scale, since source data (BakeMesh) is already scaled

			var targetMeshWorldBounds = meshBakedOrAsset.bounds;
			var targetMeshWorldBoundsCenter = targetMeshWorldBounds.center;
			var targetMeshWorldBoundsExtent = targetMeshWorldBounds.extents;

			for (int i = 0, n = subjects.Count; i != n; i++)
			{
				var subject = subjects[i];
				if (subject.ChecksumCompare(attachData) == false)
					continue;

				int attachmentIndex = subject.attachmentIndex;
				int attachmentCount = subject.attachmentCount;
				if (attachmentIndex == -1)
					continue;

				if (attachmentIndex + attachmentCount > attachData.itemCount)
					continue;// prevent out of bounds if subject holds damaged index/count 

				var indexPos = i * 2 + 0;
				var indexNrm = i * 2 + 1;

				ArrayUtils.ResizeChecked(ref stagingData[indexPos], attachmentCount);
				ArrayUtils.ResizeChecked(ref stagingData[indexNrm], attachmentCount);
				stagingPins[indexPos] = GCHandle.Alloc(stagingData[indexPos], GCHandleType.Pinned);
				stagingPins[indexNrm] = GCHandle.Alloc(stagingData[indexNrm], GCHandleType.Pinned);

				unsafe
				{
					var resolvedPositions = (Vector3*)stagingPins[indexPos].AddrOfPinnedObject().ToPointer();
					var resolvedNormals = (Vector3*)stagingPins[indexNrm].AddrOfPinnedObject().ToPointer();
					switch (subject.attachmentType)
					{
						case SkinAttachment.AttachmentType.Transform:
							{
								stagingJobs[i] = ScheduleResolve(attachmentIndex, attachmentCount, ref targetToWorld, resolvedPositions, resolvedNormals);
							}
							break;

						case SkinAttachment.AttachmentType.Mesh:
						case SkinAttachment.AttachmentType.MeshRoots:
							{
								Matrix4x4 targetToSubject;
								{
									// this used to always read:
									//   var targetToSubject = subject.transform.worldToLocalMatrix * targetToWorld;
									//
									// to support attachments that have skinning renderers, we sometimes have to transform
									// the vertices into a space that takes into account the subsequently applied skinning:
									//    var targetToSubject = (subject.skinningBone.localToWorldMatrix * subject.meshInstanceBoneBindPose).inverse * targetToWorld;
									//
									// we can reshuffle a bit to get rid of the per-resolve inverse:
									//    var targetToSubject = (subject.skinningBoneBindPoseInverse * subject.meshInstanceBone.worldToLocalMatrix) * targetToWorld;

									if (subject.skinningBone != null)
										targetToSubject = (subject.skinningBoneBindPoseInverse * subject.skinningBone.worldToLocalMatrix) * targetToWorld;
									else
										targetToSubject = subject.transform.worldToLocalMatrix * targetToWorld;
								}

								stagingJobs[i] = ScheduleResolve(attachmentIndex, attachmentCount, ref targetToSubject, resolvedPositions, resolvedNormals);
							}
							break;
					}
				}
			}

			JobHandle.ScheduleBatchedJobs();

			while (true)
			{
				var jobsRunning = false;

				for (int i = 0, n = subjects.Count; i != n; i++)
				{
					var subject = subjects[i];
					if (subject.ChecksumCompare(attachData) == false)
						continue;

					var stillRunning = (stagingJobs[i].IsCompleted == false);
					if (stillRunning)
					{
						jobsRunning = true;
						continue;
					}

					var indexPos = i * 2 + 0;
					var indexNrm = i * 2 + 1;

					var alreadyApplied = (stagingPins[indexPos].IsAllocated == false);
					if (alreadyApplied)
						continue;

					stagingPins[indexPos].Free();
					stagingPins[indexNrm].Free();

					Profiler.BeginSample("gather-subj");
					switch (subject.attachmentType)
					{
						case SkinAttachment.AttachmentType.Transform:
							{
								subject.transform.position = stagingData[indexPos][0];
							}
							break;

						case SkinAttachment.AttachmentType.Mesh:
						case SkinAttachment.AttachmentType.MeshRoots:
							{
								if (subject.meshInstance.vertexCount != stagingData[indexPos].Length)
								{
									Debug.LogError("mismatching vertex- and attachment count", subject);
									break;
								}

								subject.meshInstance.SilentlySetVertices(stagingData[indexPos]);
								subject.meshInstance.SilentlySetNormals(stagingData[indexNrm]);

								Profiler.BeginSample("conservative-bounds");
								{
									//Debug.Log("targetMeshWorldBoundsCenter = " + targetMeshWorldBoundsCenter.ToString("G4") + " (from meshBakedOrAsset = " + meshBakedOrAsset.ToString() + ")");
									//Debug.Log("targetMeshWorldBoundsExtents = " + targetMeshWorldBoundsExtents.ToString("G4"));
									var worldToSubject = subject.transform.worldToLocalMatrix;
									var subjectBoundsCenter = worldToSubject.MultiplyPoint(targetMeshWorldBoundsCenter);
									var subjectBoundsRadius = worldToSubject.MultiplyVector(targetMeshWorldBoundsExtent).magnitude + subject.meshAssetRadius;
									var subjectBounds = subject.meshInstance.bounds;
									{
										subjectBounds.center = subjectBoundsCenter;
										subjectBounds.extents = subjectBoundsRadius * Vector3.one;
									}
									subject.meshInstance.bounds = subjectBounds;
								}
								Profiler.EndSample();
							}
							break;
					}
					Profiler.EndSample();
				}

				if (jobsRunning == false)
					break;
			}

			for (int i = 0; i != stagingPinsSourceDataCount; i++)
			{
				stagingPins[stagingPinsSourceDataOffset + i].Free();
			}

			attachDataPosePin.Free();
			attachDataItemPin.Free();

			Profiler.EndSample();
		}

		public unsafe JobHandle ScheduleResolve(int attachmentIndex, int attachmentCount, ref Matrix4x4 resolveTransform, Vector3* resolvedPositions, Vector3* resolvedNormals)
		{
			fixed (Vector3* meshPositions = meshBuffers.vertexPositions)
			fixed (Vector3* meshNormals = meshBuffers.vertexNormals)
			fixed (SkinAttachmentItem* attachItem = attachData.item)
			fixed (SkinAttachmentPose* attachPose = attachData.pose)
			{
				var job = new ResolveJob()
				{
					meshPositions = meshPositions,
					meshNormals = meshNormals,
					attachItem = attachItem,
					attachPose = attachPose,
					resolveTransform = resolveTransform,
					resolvedPositions = resolvedPositions,
					resolvedNormals = resolvedNormals,
					attachmentIndex = attachmentIndex,
					attachmentCount = attachmentCount,
				};
				return job.Schedule(attachmentCount, 64);
			}
		}

		[BurstCompile(FloatMode = FloatMode.Fast)]
		unsafe struct ResolveJob : IJobParallelFor
		{
			[NativeDisableUnsafePtrRestriction, NoAlias] public Vector3* meshPositions;
			[NativeDisableUnsafePtrRestriction, NoAlias] public Vector3* meshNormals;
			[NativeDisableUnsafePtrRestriction, NoAlias] public SkinAttachmentItem* attachItem;
			[NativeDisableUnsafePtrRestriction, NoAlias] public SkinAttachmentPose* attachPose;
			[NativeDisableUnsafePtrRestriction, NoAlias] public Vector3* resolvedPositions;
			[NativeDisableUnsafePtrRestriction, NoAlias] public Vector3* resolvedNormals;

			public Matrix4x4 resolveTransform;

			public int attachmentIndex;
			public int attachmentCount;

			//TODO this needs optimization
			public void Execute(int i)
			{
				var targetBlended = new Vector3(0.0f, 0.0f, 0.0f);
				var targetWeights = 0.0f;

				SkinAttachmentItem item = attachItem[attachmentIndex + i];

				var poseIndex0 = item.poseIndex;
				var poseIndexN = item.poseIndex + item.poseCount;

				for (int poseIndex = poseIndex0; poseIndex != poseIndexN; poseIndex++)
				{
					SkinAttachmentPose pose = attachPose[poseIndex];

					var p0 = meshPositions[pose.v0];
					var p1 = meshPositions[pose.v1];
					var p2 = meshPositions[pose.v2];

					var v0v1 = p1 - p0;
					var v0v2 = p2 - p0;

					var triangleNormal = Vector3.Cross(v0v1, v0v2);
					var triangleArea = Vector3.Magnitude(triangleNormal);

					triangleNormal /= triangleArea;
					triangleArea *= 0.5f;

					var targetProjected = pose.targetCoord.Resolve(ref p0, ref p1, ref p2);
					var target = targetProjected + triangleNormal * pose.targetDist;

					targetBlended += triangleArea * target;
					targetWeights += triangleArea;
				}

				var targetNormalRot = Quaternion.FromToRotation(item.baseNormal, meshNormals[item.baseVertex]);
				var targetNormal = targetNormalRot * item.targetNormal;
				var targetOffset = targetNormalRot * item.targetOffset;

				resolvedPositions[i] = resolveTransform.MultiplyPoint3x4(targetBlended / targetWeights + targetOffset);
				resolvedNormals[i] = resolveTransform.MultiplyVector(targetNormal);
			}
		}

#if UNITY_EDITOR
		public void OnDrawGizmosSelected()
		{
			var activeGO = UnityEditor.Selection.activeGameObject;
			if (activeGO == null)
				return;
			if (activeGO != this.gameObject && activeGO.GetComponent<SkinAttachment>() == null)
				return;

			Gizmos.matrix = this.transform.localToWorldMatrix;

			if (showWireframe)
			{
				Profiler.BeginSample("show-wire");
				{
					var meshVertexCount = meshBuffers.vertexCount;
					var meshPositions = meshBuffers.vertexPositions;
					var meshNormals = meshBuffers.vertexNormals;

					Gizmos.color = Color.Lerp(Color.clear, Color.green, 0.25f);
					Gizmos.DrawWireMesh(meshBakedOrAsset, 0);

					Gizmos.color = Color.red;
					for (int i = 0; i != meshVertexCount; i++)
					{
						Gizmos.DrawRay(meshPositions[i], meshNormals[i] * 0.001f);// 1mm
					}
				}
				Profiler.EndSample();
			}

			if (showUVSeams)
			{
				Profiler.BeginSample("show-seams");
				{
					Gizmos.color = Color.cyan;
					var weldedAdjacency = new MeshAdjacency(meshBuffers, true);
					for (int i = 0; i != weldedAdjacency.vertexCount; i++)
					{
						if (weldedAdjacency.vertexWelded.GetCount(i) > 0)
						{
							bool seam = false;
							foreach (var j in weldedAdjacency.vertexVertices[i])
							{
								if (weldedAdjacency.vertexWelded.GetCount(j) > 0)
								{
									seam = true;
									if (i < j)
									{
										Gizmos.DrawLine(meshBuffers.vertexPositions[i], meshBuffers.vertexPositions[j]);
									}
								}
							}
							if (!seam)
							{
								Gizmos.color = Color.magenta;
								Gizmos.DrawRay(meshBuffers.vertexPositions[i], meshBuffers.vertexNormals[i] * 0.003f);
								Gizmos.color = Color.cyan;
							}
						}
					}
				}
				Profiler.EndSample();
			}

			if (showResolved)
			{
				Profiler.BeginSample("show-resolve");
				unsafe
				{
					var attachmentIndex = 0;
					var attachmentCount = attachData.itemCount;

					using (var resolvedPositions = new UnsafeArrayVector3(attachmentCount))
					using (var resolvedNormals = new UnsafeArrayVector3(attachmentCount))
					{
						var resolveTransform = Matrix4x4.identity;
						var resolveJob = ScheduleResolve(attachmentIndex, attachmentCount, ref resolveTransform, resolvedPositions.val, resolvedNormals.val);

						JobHandle.ScheduleBatchedJobs();

						resolveJob.Complete();

						Gizmos.color = Color.yellow;
						Vector3 size = 0.0002f * Vector3.one;

						for (int i = 0; i != attachmentCount; i++)
						{
							Gizmos.DrawCube(resolvedPositions.val[i], size);
						}
					}
				}
				Profiler.EndSample();
			}
		}
#endif
	}
}