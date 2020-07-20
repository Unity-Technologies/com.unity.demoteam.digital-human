using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.DemoTeam.DigitalHuman
{
	using MeshInfo = SkinAttachmentTarget.MeshInfo;

	public static class SkinAttachmentDataBuilder
	{
		public static unsafe int BuildPosesTriangle(SkinAttachmentPose* pose, in MeshInfo meshInfo, ref Vector3 target, int triangle)
		{
			var meshPositions = meshInfo.meshBuffers.vertexPositions;
			var meshTriangles = meshInfo.meshBuffers.triangles;

			int _0 = triangle * 3;
			var v0 = meshTriangles[_0];
			var v1 = meshTriangles[_0 + 1];
			var v2 = meshTriangles[_0 + 2];

			var p0 = meshPositions[v0];
			var p1 = meshPositions[v1];
			var p2 = meshPositions[v2];

			var v0v1 = p1 - p0;
			var v0v2 = p2 - p0;

			var triangleNormal = Vector3.Cross(v0v1, v0v2);
			var triangleArea = Vector3.Magnitude(triangleNormal);

			triangleNormal /= triangleArea;
			triangleArea *= 0.5f;

			if (triangleArea < float.Epsilon)
				return 0;// no pose

			var targetDist = Vector3.Dot(triangleNormal, target - p0);
			var targetProjected = target - targetDist * triangleNormal;
			var targetCoord = new Barycentric(ref targetProjected, ref p0, ref p1, ref p2);

			pose->v0 = v0;
			pose->v1 = v1;
			pose->v2 = v2;
			pose->area = triangleArea;
			pose->targetDist = targetDist;
			pose->targetCoord = targetCoord;
			return 1;
		}

		public static unsafe int BuildPosesVertex(SkinAttachmentPose* pose, in MeshInfo meshInfo, ref Vector3 target, int vertex)
		{
			int poseCount = 0;
			foreach (int triangle in meshInfo.meshAdjacency.vertexTriangles[vertex])
			{
				poseCount += BuildPosesTriangle(pose + poseCount, meshInfo, ref target, triangle);
			}
			return poseCount;
		}

		// note: unused -- remove?
		public static unsafe void BuildDataAttachToTriangle(SkinAttachmentData attachData, int* attachmentIndex, int* attachmentCount, in MeshInfo meshInfo, Vector3* targetPositions, int* targetTriangles, int targetCount)
		{
			var poseIndex = attachData.poseCount;
			var itemIndex = attachData.itemCount;

			fixed (SkinAttachmentPose* pose = attachData.pose)
			fixed (SkinAttachmentItem* item = attachData.item)
			{
				for (int i = 0; i != targetCount; i++)
				{
					var poseCount = BuildPosesTriangle(pose + poseIndex, meshInfo, ref targetPositions[i], targetTriangles[i]);
					if (poseCount == 0)
					{
						Debug.LogError("no valid poses for target triangle " + i + ", aborting");
						poseIndex = attachData.poseCount;
						itemIndex = attachData.itemCount;
						break;
					}

					item[itemIndex].poseIndex = poseIndex;
					item[itemIndex].poseCount = poseCount;
					item[itemIndex].baseVertex = meshInfo.meshBuffers.triangles[3 * targetTriangles[i]];
					item[itemIndex].baseNormal = meshInfo.meshBuffers.vertexNormals[item[itemIndex].baseVertex];
					item[itemIndex].targetNormal = item[itemIndex].baseNormal;
					item[itemIndex].targetOffset = Vector3.zero;

					poseIndex += poseCount;
					itemIndex += 1;
				}
			}

			*attachmentIndex = itemIndex > attachData.itemCount ? attachData.itemCount : -1;
			*attachmentCount = itemIndex - attachData.itemCount;

			attachData.poseCount = poseIndex;
			attachData.itemCount = itemIndex;
		}

		public static unsafe void BuildDataAttachToVertex(SkinAttachmentData attachData, int* attachmentIndex, int* attachmentCount, in MeshInfo meshInfo, Vector3* targetPositions, Vector3* targetOffsets, Vector3* targetNormals, int* targetVertices, int targetCount)
		{
			var poseIndex = attachData.poseCount;
			var itemIndex = attachData.itemCount;

			fixed (SkinAttachmentPose* pose = attachData.pose)
			fixed (SkinAttachmentItem* item = attachData.item)
			{
				for (int i = 0; i != targetCount; i++)
				{
					var poseCount = BuildPosesVertex(pose + poseIndex, meshInfo, ref targetPositions[i], targetVertices[i]);
					if (poseCount == 0)
					{
						Debug.LogError("no valid poses for target vertex " + i + ", aborting");
						poseIndex = attachData.poseCount;
						itemIndex = attachData.itemCount;
						break;
					}

					item[itemIndex].poseIndex = poseIndex;
					item[itemIndex].poseCount = poseCount;
					item[itemIndex].baseVertex = targetVertices[i];
					item[itemIndex].baseNormal = meshInfo.meshBuffers.vertexNormals[targetVertices[i]];
					item[itemIndex].targetNormal = targetNormals[i];
					item[itemIndex].targetOffset = targetOffsets[i];

					poseIndex += poseCount;
					itemIndex += 1;
				}
			}

			*attachmentIndex = itemIndex > attachData.itemCount ? attachData.itemCount : -1;
			*attachmentCount = itemIndex - attachData.itemCount;

			attachData.poseCount = poseIndex;
			attachData.itemCount = itemIndex;
		}

		public static unsafe void BuildDataAttachToClosestVertex(SkinAttachmentData attachData, int* attachmentIndex, int* attachmentCount, in MeshInfo meshInfo, Vector3* targetPositions, Vector3* targetNormals, int targetCount)
		{
			using (var targetOffsets = new UnsafeArrayVector3(targetCount))
			using (var targetVertices = new UnsafeArrayInt(targetCount))
			{
				for (int i = 0; i != targetCount; i++)
				{
					targetOffsets.val[i] = Vector3.zero;
					targetVertices.val[i] = meshInfo.meshVertexBSP.FindNearest(ref targetPositions[i]);
				}
				BuildDataAttachToVertex(attachData, attachmentIndex, attachmentCount, meshInfo, targetPositions, targetOffsets.val, targetNormals, targetVertices.val, targetCount);
			}
		}

		public static unsafe int CountPosesTriangle(in MeshInfo meshInfo, ref Vector3 target, int triangle)
		{
			SkinAttachmentPose dummyPose;
			return BuildPosesTriangle(&dummyPose, meshInfo, ref target, triangle);
		}

		public static unsafe int CountPosesVertex(in MeshInfo meshInfo, ref Vector3 target, int vertex)
		{
			int poseCount = 0;
			foreach (int triangle in meshInfo.meshAdjacency.vertexTriangles[vertex])
			{
				poseCount += CountPosesTriangle(meshInfo, ref target, triangle);
			}
			return poseCount;
		}

		public static unsafe void CountDataAttachToTriangle(ref int poseCount, ref int itemCount, in MeshInfo meshInfo, Vector3* targetPositions, int* targetTriangles, int targetCount)
		{
			for (int i = 0; i != targetCount; i++)
			{
				poseCount += CountPosesTriangle(meshInfo, ref targetPositions[i], targetTriangles[i]);
				itemCount += 1;
			}
		}

		public static unsafe void CountDataAttachToVertex(ref int poseCount, ref int itemCount, in MeshInfo meshInfo, Vector3* targetPositions, Vector3* targetOffsets, Vector3* targetNormals, int* targetVertices, int targetCount)
		{
			for (int i = 0; i != targetCount; i++)
			{
				poseCount += CountPosesVertex(meshInfo, ref targetPositions[i], targetVertices[i]);
				itemCount += 1;
			}
		}

		public static unsafe void CountDataAttachToClosestVertex(ref int poseCount, ref int itemCount, in MeshInfo meshInfo, Vector3* targetPositions, Vector3* targetNormals, int targetCount)
		{
			using (var targetOffsets = new UnsafeArrayVector3(targetCount))
			using (var targetVertices = new UnsafeArrayInt(targetCount))
			{
				for (int i = 0; i != targetCount; i++)
				{
					targetVertices.val[i] = meshInfo.meshVertexBSP.FindNearest(ref targetPositions[i]);
				}
				CountDataAttachToVertex(ref poseCount, ref itemCount, meshInfo, targetPositions, targetOffsets.val, targetNormals, targetVertices.val, targetCount);
			}
		}

		public static unsafe void BuildDataAttachSubject(ref SkinAttachmentData attachData, int* attachmentIndex, int* attachmentCount, Transform target, in MeshInfo meshInfo, SkinAttachment subject, bool dryRun, ref int dryRunPoseCount, ref int dryRunItemCount)
		{
			Matrix4x4 subjectToTarget;
			{
				if (subject.skinningBone != null)
					subjectToTarget = target.transform.worldToLocalMatrix * (subject.skinningBone.localToWorldMatrix * subject.skinningBoneBindPose);
				else
					subjectToTarget = target.transform.worldToLocalMatrix * subject.transform.localToWorldMatrix;
			}

			switch (subject.attachmentType)
			{
				case SkinAttachment.AttachmentType.Transform:
					{
						var targetPosition = subjectToTarget.MultiplyPoint3x4(Vector3.zero);
						var targetNormal = subjectToTarget.MultiplyVector(Vector3.up);

						if (dryRun)
							CountDataAttachToClosestVertex(ref dryRunPoseCount, ref dryRunItemCount, meshInfo, &targetPosition, &targetNormal, 1);
						else
							BuildDataAttachToClosestVertex(attachData, attachmentIndex, attachmentCount, meshInfo, &targetPosition, &targetNormal, 1);
					}
					break;

				case SkinAttachment.AttachmentType.Mesh:
					{
						if (subject.meshInstance == null)
							break;

						var subjectVertexCount = subject.meshBuffers.vertexCount;
						var subjectPositions = subject.meshBuffers.vertexPositions;
						var subjectNormals = subject.meshBuffers.vertexNormals;

						using (var targetPositions = new UnsafeArrayVector3(subjectVertexCount))
						using (var targetNormals = new UnsafeArrayVector3(subjectVertexCount))
						{
							for (int i = 0; i != subjectVertexCount; i++)
							{
								targetPositions.val[i] = subjectToTarget.MultiplyPoint3x4(subjectPositions[i]);
								targetNormals.val[i] = subjectToTarget.MultiplyVector(subjectNormals[i]);
							}

							if (dryRun)
								CountDataAttachToClosestVertex(ref dryRunPoseCount, ref dryRunItemCount, meshInfo, targetPositions.val, targetNormals.val, subjectVertexCount);
							else
								BuildDataAttachToClosestVertex(attachData, attachmentIndex, attachmentCount, meshInfo, targetPositions.val, targetNormals.val, subjectVertexCount);
						}
					}
					break;

				case SkinAttachment.AttachmentType.MeshRoots:
					{
						if (subject.meshInstance == null)
							break;

						var subjectVertexCount = subject.meshBuffers.vertexCount;
						var subjectPositions = subject.meshBuffers.vertexPositions;
						var subjectNormals = subject.meshBuffers.vertexPositions;

						using (var targetPositions = new UnsafeArrayVector3(subjectVertexCount))
						using (var targetNormals = new UnsafeArrayVector3(subjectVertexCount))
						using (var targetOffsets = new UnsafeArrayVector3(subjectVertexCount))
						using (var targetVertices = new UnsafeArrayInt(subjectVertexCount))
						using (var rootIdx = new UnsafeArrayInt(subjectVertexCount))
						using (var rootDir = new UnsafeArrayVector3(subjectVertexCount))
						using (var rootGen = new UnsafeArrayInt(subjectVertexCount))
						using (var visitor = new UnsafeBFS(subjectVertexCount))
						{
							for (int i = 0; i != subjectVertexCount; i++)
							{
								targetPositions.val[i] = subjectToTarget.MultiplyPoint3x4(subjectPositions[i]);
								targetNormals.val[i] = subjectToTarget.MultiplyVector(subjectNormals[i]);
								targetOffsets.val[i] = Vector3.zero;
							}

							visitor.Clear();

							// find island roots
							for (int island = 0; island != subject.meshIslands.islandCount; island++)
							{
								int rootCount = 0;

								var bestDist0 = float.PositiveInfinity;
								var bestNode0 = -1;
								var bestVert0 = -1;

								var bestDist1 = float.PositiveInfinity;
								var bestNode1 = -1;
								var bestVert1 = -1;

								foreach (var i in subject.meshIslands.islandVertices[island])
								{
									var targetDist = float.PositiveInfinity;
									var targetNode = -1;

									if (meshInfo.meshVertexBSP.FindNearest(ref targetDist, ref targetNode, ref targetPositions.val[i]))
									{
										// found a root if one or more neighbouring vertices are below
										var bestDist = float.PositiveInfinity;
										var bestNode = -1;

										foreach (var j in subject.meshAdjacency.vertexVertices[i])
										{
											var targetDelta = targetPositions.val[j] - meshInfo.meshBuffers.vertexPositions[targetNode];
											var targetNormalDist = Vector3.Dot(targetDelta, meshInfo.meshBuffers.vertexNormals[targetNode]);
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
											rootDir.val[i] = Vector3.Normalize(targetPositions.val[bestNode] - targetPositions.val[i]);
											rootGen.val[i] = 0;
											rootCount++;
										}
										else
										{
											rootIdx.val[i] = -1;
											rootGen.val[i] = -1;

											// see if node qualifies as second choice root
											var targetDelta = targetPositions.val[i] - meshInfo.meshBuffers.vertexPositions[targetNode];
											var targetNormalDist = Mathf.Abs(Vector3.Dot(targetDelta, meshInfo.meshBuffers.vertexNormals[targetNode]));
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
									rootDir.val[bestVert0] = Vector3.Normalize(meshInfo.meshBuffers.vertexPositions[bestNode0] - targetPositions.val[bestVert0]);
									rootGen.val[bestVert0] = 0;
									rootCount++;

									if (rootCount < 2 && bestVert1 != -1)
									{
										visitor.Ignore(bestVert1);
										rootIdx.val[bestVert1] = bestNode1;
										rootDir.val[bestVert1] = Vector3.Normalize(meshInfo.meshBuffers.vertexPositions[bestNode1] - targetPositions.val[bestVert1]);
										rootGen.val[bestVert1] = 0;
										rootCount++;
									}
								}
							}

							// find boundaries
							for (int i = 0; i != subjectVertexCount; i++)
							{
								if (rootIdx.val[i] != -1)
									continue;

								foreach (var j in subject.meshAdjacency.vertexVertices[i])
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

								foreach (var j in subject.meshAdjacency.vertexVertices[i])
								{
									if (rootIdx.val[j] != -1)
									{
										var d = -Vector3.Dot(rootDir.val[j], Vector3.Normalize(targetPositions.val[j] - targetPositions.val[i]));
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
								rootDir.val[i] = Vector3.Normalize(targetPositions.val[bestNode] - targetPositions.val[i]);
								rootGen.val[i] = rootGen.val[bestNode] + 1;

								targetOffsets.val[i] = targetPositions.val[i] - targetPositions.val[bestNode];
								targetPositions.val[i] = targetPositions.val[bestNode];
							}

							// copy to target vertices
							for (int i = 0; i != subjectVertexCount; i++)
							{
								targetVertices.val[i] = rootIdx.val[i];
							}

							if (dryRun)
								CountDataAttachToVertex(ref dryRunPoseCount, ref dryRunItemCount, meshInfo, targetPositions.val, targetOffsets.val, targetNormals.val, targetVertices.val, subjectVertexCount);
							else
								BuildDataAttachToVertex(attachData, attachmentIndex, attachmentCount, meshInfo, targetPositions.val, targetOffsets.val, targetNormals.val, targetVertices.val, subjectVertexCount);
						}
					}
					break;
			}
		}

		public static void BuildDataAttachSubject(ref SkinAttachmentData attachData, Transform target, in MeshInfo meshInfo, SkinAttachment subject, bool dryRun, ref int dryRunPoseCount, ref int dryRunItemCount)
		{
			unsafe
			{
				fixed (int* attachmentIndex = &subject.attachmentIndex)
				fixed (int* attachmentCount = &subject.attachmentCount)
				{
					BuildDataAttachSubject(ref attachData, attachmentIndex, attachmentCount, target, meshInfo, subject, dryRun, ref dryRunPoseCount, ref dryRunItemCount);
				}
			}
		}

		public static void BuildDataAttachSubjectReadOnly(ref SkinAttachmentData attachData, Transform target, in MeshInfo meshInfo, SkinAttachment subject, bool dryRun, ref int dryRunPoseCount, ref int dryRunItemCount)
		{
			unsafe
			{
				int attachmentIndex = 0;
				int attachmentCount = 0;
				{
					BuildDataAttachSubject(ref attachData, &attachmentIndex, &attachmentCount, target, meshInfo, subject, dryRun, ref dryRunPoseCount, ref dryRunItemCount);
				}
			}
		}
	}
}
