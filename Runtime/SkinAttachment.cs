using System;
using UnityEngine;
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
		[Range(0, 6)]
		public int debugIndex = 0;
		public bool debugIndexEnabled = false;
		public bool debugBounds = false;

		[Header("Runtime options")]
		public bool forceRecalculateBounds;
		public bool forceRecalculateNormals;
		public bool forceRecalculateTangents;

		[NonSerialized] public float meshAssetRadius;
		[NonSerialized] public MeshBuffers meshBuffers;
		[NonSerialized] public MeshAdjacency meshAdjacency;
		[NonSerialized] public MeshIslands meshIslands;

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
		}

		protected override void OnMeshInstanceDeleted()
		{
			// do nothing
		}

		public Hash128 Checksum()
		{
			return new Hash128(checksum0, checksum1);
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

			if (target == null)
				return;

			var targetMeshInfo = target.GetCachedMeshInfo();
			if (targetMeshInfo.valid == false)
				return;

			//TODO get rid of duplicate code

			if (attached)
			{
				if (attachmentType != AttachmentType.Transform && debugBounds)
				{
					Gizmos.matrix = this.transform.localToWorldMatrix;
					Gizmos.DrawWireCube(meshInstance.bounds.center, meshInstance.bounds.extents * 2.0f);
				}
				return;
			}

			if (attachmentType == AttachmentType.Transform)
			{
				var targetPosition = target.transform.InverseTransformPoint(this.transform.position);

				var closestDist = float.MaxValue;
				var closestNode = -1;

				if (targetMeshInfo.meshVertexBSP.FindNearest(ref closestDist, ref closestNode, ref targetPosition))
				{
					Gizmos.matrix = target.transform.localToWorldMatrix;

					var r = targetPosition - target.meshBuffers.vertexPositions[closestNode];
					var d = Vector3.Dot(target.meshBuffers.vertexNormals[closestNode], r);
					var c = (d >= 0.0f) ? Color.cyan : Color.magenta;

					Gizmos.color = Color.Lerp(Color.clear, c, 0.75f);
					Gizmos.DrawSphere(targetPosition, Mathf.Sqrt(closestDist));

					Gizmos.color = Color.Lerp(Color.clear, c, 0.75f);
					Gizmos.DrawLine(targetPosition, target.meshBuffers.vertexPositions[closestNode]);

					target.meshBuffers.DrawTriangles(targetMeshInfo.meshAdjacency.vertexTriangles[closestNode]);
				}

				return;
			}

			Gizmos.matrix = this.transform.localToWorldMatrix;

			if (attachmentType == AttachmentType.Mesh)
			{
				var colorArray = new Color[] { Color.red, Color.green, Color.blue, Color.cyan, Color.magenta, Color.yellow, Color.white };
				var colorIndex = -1;

				var positions = meshBuffers.vertexPositions;

				for (int island = 0; island != meshIslands.islandCount; island++)
				{
					colorIndex++;
					colorIndex %= colorArray.Length;

					if (colorIndex != debugIndex && debugIndexEnabled)
						continue;

					Gizmos.color = Color.Lerp(Color.clear, colorArray[colorIndex], 0.3f);

					foreach (var i in meshIslands.islandVertices[island])
					{
						foreach (var j in meshAdjacency.vertexVertices[i])
						{
							Gizmos.DrawLine(positions[i], positions[j]);
						}
					}
				}

				return;
			}

			if (attachmentType == AttachmentType.MeshRoots)
			{
				var colorArray = new Color[] { Color.red, Color.green, Color.blue, Color.cyan, Color.magenta, Color.yellow, Color.white };
				var colorIndex = -1;

				var positions = meshBuffers.vertexPositions;

				var targetPositions = new Vector3[positions.Length];
				var subjectToTarget = target.transform.worldToLocalMatrix * this.transform.localToWorldMatrix;
				var targetToSubject = this.transform.worldToLocalMatrix * target.transform.localToWorldMatrix;

				for (int i = 0; i != positions.Length; i++)
				{
					targetPositions[i] = subjectToTarget.MultiplyPoint3x4(positions[i]);
				}

				for (int island = 0; island != meshIslands.islandCount; island++)
				{
					colorIndex++;
					colorIndex %= colorArray.Length;

					if (colorIndex != debugIndex && debugIndexEnabled)
						continue;

					Gizmos.color = colorArray[colorIndex];

					// draw the faces
					foreach (int i in meshIslands.islandVertices[island])
					{
						foreach (int j in meshAdjacency.vertexVertices[i])
						{
							Gizmos.DrawLine(positions[i], positions[j]);
						}
					}

					// draw root-lines
					unsafe
					{
						var rootIdx = new UnsafeArrayInt(positions.Length);
						var rootDir = new UnsafeArrayVector3(positions.Length);
						var rootGen = new UnsafeArrayInt(positions.Length);
						var visitor = new UnsafeBFS(positions.Length);

						visitor.Clear();

						// find island roots
						{
							int rootCount = 0;

							var bestDist0 = float.PositiveInfinity;
							var bestNode0 = -1;
							var bestVert0 = -1;

							var bestDist1 = float.PositiveInfinity;
							var bestNode1 = -1;
							var bestVert1 = -1;

							foreach (var i in meshIslands.islandVertices[island])
							{
								var targetDist = float.PositiveInfinity;
								var targetNode = -1;

								if (targetMeshInfo.meshVertexBSP.FindNearest(ref targetDist, ref targetNode, ref targetPositions[i]))
								{
									// found a root if one or more neighbouring vertices are below
									var bestDist = float.PositiveInfinity;
									var bestNode = -1;

									foreach (var j in meshAdjacency.vertexVertices[i])
									{
										var targetDelta = targetPositions[j] - target.meshBuffers.vertexPositions[targetNode];
										var targetNormalDist = Vector3.Dot(targetDelta, target.meshBuffers.vertexNormals[targetNode]);
										if (targetNormalDist < 0.0f)
										{
											var d = Vector3.SqrMagnitude(targetDelta);
											if (d < bestDist)
											{
												bestDist = d;
												bestNode = j;
											}
										}
									}

									if (bestNode != -1)
									{
										visitor.Ignore(i);
										rootIdx.val[i] = targetNode;
										rootDir.val[i] = Vector3.Normalize(targetPositions[bestNode] - targetPositions[i]);
										rootGen.val[i] = 0;
										rootCount++;
									}
									else
									{
										rootIdx.val[i] = -1;
										rootGen.val[i] = -1;

										// see if node qualifies as second choice root
										var targetDelta = targetPositions[i] - target.meshBuffers.vertexPositions[targetNode];
										var targetNormalDist = Mathf.Abs(Vector3.Dot(targetDelta, target.meshBuffers.vertexNormals[targetNode]));
										if (targetNormalDist < bestDist0)
										{
											bestDist1 = bestDist0;
											bestNode1 = bestNode0;
											bestVert1 = bestVert0;

											bestDist0 = targetNormalDist;
											bestNode0 = targetNode;
											bestVert0 = i;
										}
										else if (targetNormalDist < bestDist1)
										{
											bestDist1 = targetNormalDist;
											bestNode1 = targetNode;
											bestVert1 = i;
										}
									}
								}
							}

							if (rootCount < 2 && bestVert0 != -1)
							{
								visitor.Ignore(bestVert0);
								rootIdx.val[bestVert0] = bestNode0;
								rootDir.val[bestVert0] = Vector3.Normalize(target.meshBuffers.vertexPositions[bestNode0] - targetPositions[bestVert0]);
								rootGen.val[bestVert0] = 0;
								rootCount++;

								if (rootCount < 2 && bestVert1 != -1)
								{
									visitor.Ignore(bestVert1);
									rootIdx.val[bestVert1] = bestNode1;
									rootDir.val[bestVert1] = Vector3.Normalize(target.meshBuffers.vertexPositions[bestNode1] - targetPositions[bestVert1]);
									rootGen.val[bestVert1] = 0;
									rootCount++;
								}
							}
						}

						// find boundary
						foreach (var i in meshIslands.islandVertices[island])
						{
							if (rootIdx.val[i] != -1)
								continue;

							foreach (var j in meshAdjacency.vertexVertices[i])
							{
								if (rootIdx.val[j] != -1)
								{
									visitor.Insert(i);
									break;
								}
							}
						}

						// propagate roots
						while (visitor.MoveNext())
						{
							var i = visitor.position;

							var bestDist = float.PositiveInfinity;
							var bestNode = -1;

							foreach (var j in meshAdjacency.vertexVertices[i])
							{
								if (rootIdx.val[j] != -1)
								{
									var d = -Vector3.Dot(rootDir.val[j], Vector3.Normalize(targetPositions[j] - targetPositions[i]));
									if (d < bestDist)
									{
										bestDist = d;
										bestNode = j;
									}
								}
								else
								{
									visitor.Insert(j);
								}
							}

							rootIdx.val[i] = rootIdx.val[bestNode];
							rootDir.val[i] = Vector3.Normalize(targetPositions[bestNode] - targetPositions[i]);
							rootGen.val[i] = rootGen.val[bestNode] + 1;

							Gizmos.color = colorArray[rootGen.val[i] % colorArray.Length];
							Gizmos.DrawSphere(positions[bestNode], 0.0002f);
							Gizmos.DrawSphere(0.5f * (positions[i] + positions[bestNode]), 0.0001f);
						}

						// draw roots
						foreach (var i in meshIslands.islandVertices[island])
						{
							var root = rootIdx.val[i];
							if (root < 0)
							{
								Debug.Log("i " + i + " has rootIdx " + root);
								Gizmos.DrawLine(positions[i], positions[i] + Vector3.up);
							}
							Gizmos.DrawLine(positions[i], targetToSubject.MultiplyPoint3x4(target.meshBuffers.vertexPositions[root]));
						}

						// dispose
						visitor.Dispose();
						rootGen.Dispose();
						rootDir.Dispose();
						rootIdx.Dispose();
					}

					//-------------
				}

				return;
			}
		}
#endif
	}
}
