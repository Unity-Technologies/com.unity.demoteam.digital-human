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
			var descIndex = attachData.itemCount;

			fixed (SkinAttachmentPose* pose = attachData.pose)
			fixed (SkinAttachmentItem* desc = attachData.item)
			{
				for (int i = 0; i != targetCount; i++)
				{
					var poseCount = BuildPosesVertex(pose + poseIndex, meshInfo, ref targetPositions[i], targetVertices[i]);
					if (poseCount == 0)
					{
						Debug.LogError("no valid poses for target vertex " + i + ", aborting");
						poseIndex = attachData.poseCount;
						descIndex = attachData.itemCount;
						break;
					}

					desc[descIndex].poseIndex = poseIndex;
					desc[descIndex].poseCount = poseCount;
					desc[descIndex].baseVertex = targetVertices[i];
					desc[descIndex].baseNormal = meshInfo.meshBuffers.vertexNormals[targetVertices[i]];
					desc[descIndex].targetNormal = targetNormals[i];
					desc[descIndex].targetOffset = targetOffsets[i];

					poseIndex += poseCount;
					descIndex += 1;
				}
			}

			*attachmentIndex = descIndex > attachData.itemCount ? attachData.itemCount : -1;
			*attachmentCount = descIndex - attachData.itemCount;

			attachData.poseCount = poseIndex;
			attachData.itemCount = descIndex;
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
	}
}
