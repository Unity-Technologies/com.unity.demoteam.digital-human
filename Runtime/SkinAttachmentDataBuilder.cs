﻿using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.DemoTeam.DigitalHuman
{
    using static Unity.Mathematics.math;
    using SkinAttachmentItem = Unity.DemoTeam.DigitalHuman.SkinAttachmentItem3;
    
    public struct MeshInfo
    {
        public MeshBuffers meshBuffers;
        public MeshAdjacency meshAdjacency;
        public KdTree3 meshVertexBSP;
        public bool valid;
    }
    public struct PoseBuildSettings
    {
        public bool onlyAllowPoseTrianglesContainingAttachedPoint;
    }

    public static class SkinAttachmentDataBuilder
    {
        public static float SqDistTriangle(in float3 v1, in float3 v2, in float3 v3, in float3 p)
        {
            // see: "distance to triangle" by Inigo Quilez
            // https://www.iquilezles.org/www/articles/triangledistance/triangledistance.htm

            float dot2(float3 v) => dot(v, v);

            // prepare data
            float3 v21 = v2 - v1;
            float3 p1 = p - v1;
            float3 v32 = v3 - v2;
            float3 p2 = p - v2;
            float3 v13 = v1 - v3;
            float3 p3 = p - v3;
            float3 nor = cross(v21, v13);

            // inside/outside test
            if (sign(dot(cross(v21, nor), p1)) +
                sign(dot(cross(v32, nor), p2)) +
                sign(dot(cross(v13, nor), p3)) < 2.0f)
            {
                // 3 edges
                return min(min(
                        dot2(v21 * clamp(dot(v21, p1) / dot2(v21), 0.0f, 1.0f) - p1),
                        dot2(v32 * clamp(dot(v32, p2) / dot2(v32), 0.0f, 1.0f) - p2)),
                    dot2(v13 * clamp(dot(v13, p3) / dot2(v13), 0.0f, 1.0f) - p3));
            }
            else
            {
                // 1 face
                return dot(nor, p1) * dot(nor, p1) / dot2(nor);
            }
        }

        public static unsafe int BuildPosesTriangle(SkinAttachmentPose* pose, in MeshInfo meshInfo,
            ref Vector3 target,
            int triangle, bool includeExterior)
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
                return 0; // no pose

            var targetDist = Vector3.Dot(triangleNormal, target - p0);
            var targetProjected = target - targetDist * triangleNormal;
            var targetCoord = new Barycentric(ref targetProjected, ref p0, ref p1, ref p2);


            if (includeExterior || targetCoord.Within())
            {
                pose->v0 = v0;
                pose->v1 = v1;
                pose->v2 = v2;
                pose->area = 1.0f / Mathf.Max(float.Epsilon, SqDistTriangle(p0, p1, p2, target));
                pose->targetDist = targetDist;
                pose->targetCoord = targetCoord;
                return 1;
            }
            else
            {
                return 0;
            }
        }

        public static unsafe int BuildPosesVertex(SkinAttachmentPose* pose, in MeshInfo meshInfo, ref Vector3 target,
            int vertex, bool tryToOnlyAllowInterior)
        {
            int poseCount = 0;

            if (tryToOnlyAllowInterior)
            {
                foreach (int triangle in meshInfo.meshAdjacency.vertexTriangles[vertex])
                {
                    poseCount += BuildPosesTriangle(pose + poseCount, meshInfo, ref target, triangle,
                        includeExterior: false);
                }
            }

            if (poseCount == 0)
            {
                foreach (int triangle in meshInfo.meshAdjacency.vertexTriangles[vertex])
                {
                    poseCount += BuildPosesTriangle(pose + poseCount, meshInfo, ref target, triangle,
                        includeExterior: true);
                }
            }

            return poseCount;
        }

        public static unsafe int CountPosesTriangle(in MeshInfo meshInfo, ref Vector3 target, int triangle,
            bool includeExterior)
        {
            SkinAttachmentPose dummyPose;
            return BuildPosesTriangle(&dummyPose, meshInfo, ref target, triangle, includeExterior);
        }

        public static unsafe int CountPosesVertex(in MeshInfo meshInfo, ref Vector3 target, int vertex,
            bool tryToOnlyAllowInterior)
        {
            int poseCount = 0;
            if (tryToOnlyAllowInterior)
            {
                foreach (int triangle in meshInfo.meshAdjacency.vertexTriangles[vertex])
                {
                    poseCount += CountPosesTriangle(meshInfo, ref target, triangle, includeExterior: false);
                }
            }

            if (poseCount == 0)
            {
                foreach (int triangle in meshInfo.meshAdjacency.vertexTriangles[vertex])
                {
                    poseCount += CountPosesTriangle(meshInfo, ref target, triangle, includeExterior: true);
                }
            }

            return poseCount;
        }

        public static unsafe void CountDataAttachToVertex(ref int poseCount, ref int itemCount, in MeshInfo meshInfo,
            in PoseBuildSettings settings, Vector3* targetPositions, Vector3* targetOffsets, Vector3* targetNormals,
            Vector4* targetTangents, int* targetVertices, int targetCount)
        {
            for (int i = 0; i != targetCount; i++)
            {
                poseCount += CountPosesVertex(meshInfo, ref targetPositions[i], targetVertices[i],
                    settings.onlyAllowPoseTrianglesContainingAttachedPoint);
                itemCount += 1;
            }
        }

        public static unsafe void CountDataAttachToClosestVertex(ref int poseCount, ref int itemCount,
            in MeshInfo meshInfo, in PoseBuildSettings settings, Vector3* targetPositions, Vector3* targetNormals,
            Vector4* targetTangents, int targetCount)
        {
            using (var targetOffsets = new UnsafeArrayVector3(targetCount))
            using (var targetVertices = new UnsafeArrayInt(targetCount))
            {
                for (int i = 0; i != targetCount; i++)
                {
                    targetVertices.val[i] = meshInfo.meshVertexBSP.FindNearest(ref targetPositions[i]);
                }

                CountDataAttachToVertex(ref poseCount, ref itemCount, meshInfo, settings, targetPositions,
                    targetOffsets.val, targetNormals, targetTangents, targetVertices.val, targetCount);
            }
        }

        public static unsafe void BuildDataAttachToVertex(SkinAttachmentPose* poses, SkinAttachmentItem* items,
            in MeshInfo meshInfo, Vector3* targetPositions, Vector3* targetOffsets,
            Vector3* targetNormals, Vector4* targetTangents, int* targetVertices, int targetCount,
            bool tryToOnlyAllowInterior, int* attachmentOffset,
            int* poseOffset)
        {
            int currentItemIndex = *attachmentOffset;
            int currentPoseIndex = *poseOffset;
            {
                for (int i = 0; i != targetCount; i++)
                {
                    var poseCount = BuildPosesVertex(poses + currentPoseIndex, meshInfo, ref targetPositions[i],
                        targetVertices[i], tryToOnlyAllowInterior);
                    if (poseCount == 0)
                    {
                        Debug.LogError("no valid poses for target vertex " + i + ", aborting");
                        currentItemIndex = *attachmentOffset;
                        currentPoseIndex = *poseOffset;
                        break;
                    }

                    ref readonly var baseNormal = ref meshInfo.meshBuffers.vertexNormals[targetVertices[i]];
                    ref readonly var baseTangent = ref meshInfo.meshBuffers.vertexTangents[targetVertices[i]];

                    var baseFrame = Quaternion.LookRotation(baseNormal, (Vector3)baseTangent * baseTangent.w);
                    var baseFrameInv = Quaternion.Inverse(baseFrame);

                    var targetTangent = targetTangents[i];
                    if (targetTangent.sqrMagnitude == 0)
                        targetTangent = new Vector4(0, 0, 1, 1);
                    var targetFrame = Quaternion.LookRotation(targetNormals[i],
                        (Vector3)targetTangent /* sign is omitted here, added on resolve */);

                    items[currentItemIndex].poseIndex = currentPoseIndex;
                    items[currentItemIndex].poseCount = poseCount;
                    items[currentItemIndex].baseVertex = targetVertices[i];
                    items[currentItemIndex].targetFrameW =
                        targetTangent.w; // used to preserve the sign of the resolved tangent
                    items[currentItemIndex].targetFrameDelta = baseFrameInv * targetFrame;
                    items[currentItemIndex].targetOffset = baseFrameInv * targetOffsets[i];

                    currentPoseIndex += poseCount;
                    currentItemIndex += 1;
                }
            }

            *attachmentOffset = currentItemIndex;
            *poseOffset = currentPoseIndex;
        }

        public static unsafe void BuildDataAttachToClosestVertex(SkinAttachmentPose* poses, SkinAttachmentItem* items,
            in MeshInfo meshInfo, in PoseBuildSettings settings, Vector3* targetPositions, Vector3* targetNormals,
            Vector4* targetTangents, int targetCount, int* attachmentOffset, int* poseOffset)
        {
            using (var targetOffsets = new UnsafeArrayVector3(targetCount))
            using (var targetVertices = new UnsafeArrayInt(targetCount))
            {
                for (int i = 0; i != targetCount; i++)
                {
                    targetOffsets.val[i] = Vector3.zero;
                    targetVertices.val[i] = meshInfo.meshVertexBSP.FindNearest(ref targetPositions[i]);
                }

                BuildDataAttachToVertex(poses, items, meshInfo, targetPositions,
                    targetOffsets.val, targetNormals, targetTangents, targetVertices.val, targetCount,
                    settings.onlyAllowPoseTrianglesContainingAttachedPoint, attachmentOffset, poseOffset);
            }
        }

        public static unsafe void BuildDataAttachMesh(SkinAttachmentPose* poses, SkinAttachmentItem* items,
            Matrix4x4 subjectToTarget, in MeshInfo meshInfo, in PoseBuildSettings settings,
            Vector3[] vertexPositions, Vector3[] vertexNormals, Vector4[] vertexTangents,
            bool dryRun, ref int dryRunPoseCount, ref int dryRunItemCount,
            int* attachmentOffset, int* poseOffset)
        {
            var subjectVertexCount = vertexPositions.Length;
            var subjectPositions = vertexPositions;
            var subjectNormals = vertexNormals;
            var subjectTangents = vertexTangents;

            using (var targetPositions = new UnsafeArrayVector3(subjectVertexCount))
            using (var targetNormals = new UnsafeArrayVector3(subjectVertexCount))
            using (var targetTangents = new UnsafeArrayVector4(subjectVertexCount))
            {
                for (int i = 0; i != subjectVertexCount; i++)
                {
                    targetPositions.val[i] = subjectToTarget.MultiplyPoint3x4(subjectPositions[i]);
                    targetNormals.val[i] = subjectToTarget.MultiplyVector(subjectNormals[i]);
                    Vector3 tan = subjectToTarget.MultiplyVector(subjectTangents[i]);
                    targetTangents.val[i] = new Vector4(tan.x, tan.y, tan.z, subjectTangents[i].w);
                }

                if (dryRun)
                    CountDataAttachToClosestVertex(ref dryRunPoseCount, ref dryRunItemCount, meshInfo, settings,
                        targetPositions.val, targetNormals.val, targetTangents.val, subjectVertexCount);
                else
                    BuildDataAttachToClosestVertex(poses, items, meshInfo, settings,
                        targetPositions.val, targetNormals.val, targetTangents.val, subjectVertexCount,
                        attachmentOffset, poseOffset);
            }
        }

        public static unsafe void BuildDataAttachMeshRoots(SkinAttachmentPose* poses, SkinAttachmentItem* items, 
            Matrix4x4 subjectToTarget, in MeshInfo meshInfo, in PoseBuildSettings settings, bool onlyAllowOneRoot,
            MeshIslands meshIslands, MeshAdjacency meshAdjacency, Vector3[] vertexPositions, Vector3[] vertexNormals, Vector4[] vertexTangents,
            bool dryRun, ref int dryRunPoseCount, ref int dryRunItemCount,
            int* attachmentOffset, int* poseOffset)
        {
            var subjectVertexCount = vertexPositions.Length;
            var subjectPositions = vertexPositions;
            var subjectNormals = vertexNormals;
            var subjectTangent = vertexTangents;

            using (var targetPositions = new UnsafeArrayVector3(subjectVertexCount))
            using (var targetNormals = new UnsafeArrayVector3(subjectVertexCount))
            using (var targetOffsets = new UnsafeArrayVector3(subjectVertexCount))
            using (var targetVertices = new UnsafeArrayInt(subjectVertexCount))
            using (var rootIdx = new UnsafeArrayInt(subjectVertexCount))
            using (var rootDir = new UnsafeArrayVector3(subjectVertexCount))
            using (var rootGen = new UnsafeArrayInt(subjectVertexCount))
            using (var visitor = new UnsafeBFS(subjectVertexCount))
            using (var targetTangents = new UnsafeArrayVector4(subjectVertexCount))
            {
                for (int i = 0; i != subjectVertexCount; i++)
                {
                    targetPositions.val[i] = subjectToTarget.MultiplyPoint3x4(subjectPositions[i]);
                    targetNormals.val[i] = subjectToTarget.MultiplyVector(subjectNormals[i]);
                    targetTangents.val[i] = subjectToTarget.MultiplyVector(subjectTangent[i]);
                    targetOffsets.val[i] = Vector3.zero;
                }

                visitor.Clear();

                // find island roots
                for (int island = 0; island != meshIslands.islandCount; island++)
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

                        if (meshInfo.meshVertexBSP.FindNearest(ref targetDist, ref targetNode,
                                ref targetPositions.val[i]))
                        {
                            // found a root if one or more neighbouring vertices are below
                            var bestDist = float.PositiveInfinity;
                            var bestNode = -1;

                            foreach (var j in meshAdjacency.vertexVertices[i])
                            {
                                var targetDelta = targetPositions.val[j] -
                                                  meshInfo.meshBuffers.vertexPositions[targetNode];
                                var targetNormalDist = Vector3.Dot(targetDelta,
                                    meshInfo.meshBuffers.vertexNormals[targetNode]);
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

                            if (bestNode != -1 && !(onlyAllowOneRoot && rootCount > 0))
                            {
                                visitor.Ignore(i);
                                rootIdx.val[i] = targetNode;
                                rootDir.val[i] =
                                    Vector3.Normalize(targetPositions.val[bestNode] - targetPositions.val[i]);
                                rootGen.val[i] = 0;
                                rootCount++;
                            }
                            else
                            {
                                rootIdx.val[i] = -1;
                                rootGen.val[i] = -1;

                                // see if node qualifies as second choice root
                                var targetDelta = targetPositions.val[i] -
                                                  meshInfo.meshBuffers.vertexPositions[targetNode];
                                var targetNormalDist = Mathf.Abs(Vector3.Dot(targetDelta,
                                    meshInfo.meshBuffers.vertexNormals[targetNode]));
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

                    int rootsAllowed = onlyAllowOneRoot ? 1 : 2;

                    if (rootCount < rootsAllowed && bestVert0 != -1)
                    {
                        visitor.Ignore(bestVert0);
                        rootIdx.val[bestVert0] = bestNode0;
                        rootDir.val[bestVert0] =
                            Vector3.Normalize(meshInfo.meshBuffers.vertexPositions[bestNode0] -
                                              targetPositions.val[bestVert0]);
                        rootGen.val[bestVert0] = 0;
                        rootCount++;

                        if (rootCount < rootsAllowed && bestVert1 != -1)
                        {
                            visitor.Ignore(bestVert1);
                            rootIdx.val[bestVert1] = bestNode1;
                            rootDir.val[bestVert1] = Vector3.Normalize(
                                meshInfo.meshBuffers.vertexPositions[bestNode1] -
                                targetPositions.val[bestVert1]);
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
                            var d = -Vector3.Dot(rootDir.val[j],
                                Vector3.Normalize(targetPositions.val[j] - targetPositions.val[i]));
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
                    CountDataAttachToVertex(ref dryRunPoseCount, ref dryRunItemCount, meshInfo, settings,
                        targetPositions.val, targetOffsets.val, targetNormals.val, targetTangents.val,
                        targetVertices.val, subjectVertexCount);
                else
                    BuildDataAttachToVertex(poses, items, meshInfo,
                        targetPositions.val, targetOffsets.val, targetNormals.val, targetTangents.val,
                        targetVertices.val, subjectVertexCount, settings.onlyAllowPoseTrianglesContainingAttachedPoint,
                        attachmentOffset, poseOffset);
            }
        }

        public static unsafe void BuildDataAttachTransform(SkinAttachmentPose* poses, SkinAttachmentItem* items,
            Matrix4x4 subjectToTarget, in MeshInfo meshInfo, in PoseBuildSettings settings,
            bool dryRun, ref int dryRunPoseCount, ref int dryRunItemCount,
            int* attachmentOffset, int* poseOffset)
        {
            var targetPosition = subjectToTarget.MultiplyPoint3x4(Vector3.zero);
            var targetNormal = subjectToTarget.MultiplyVector(Vector3.up);
            Vector4 targetTangent = subjectToTarget.MultiplyVector(Vector3.right);
            targetTangent.w = 1.0f;

            if (dryRun)
                CountDataAttachToClosestVertex(ref dryRunPoseCount, ref dryRunItemCount, meshInfo, settings,
                    &targetPosition, &targetNormal, &targetTangent, 1);
            else
                BuildDataAttachToClosestVertex(poses, items, meshInfo, settings,
                    &targetPosition, &targetNormal, &targetTangent, 1, attachmentOffset, poseOffset);
        }

    }
}