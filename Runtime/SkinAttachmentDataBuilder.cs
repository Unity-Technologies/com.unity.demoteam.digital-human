using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
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

    public unsafe struct MeshInfoUnsafe
    {
        //meshbuffers
        [NativeDisableUnsafePtrRestriction, NoAlias]
        public Vector3* vertexPositions;
        [NativeDisableUnsafePtrRestriction, NoAlias]
        public Vector4* vertexTangents;
        [NativeDisableUnsafePtrRestriction, NoAlias]
        public Vector3* vertexNormals;
        [NativeDisableUnsafePtrRestriction, NoAlias]
        public int* triangles;
        public LinkedIndexListArrayUnsafeView vertexTriangles;
    }

    public struct PoseBuildSettings
    {
        public bool onlyAllowPoseTrianglesContainingAttachedPoint;
    }

    public static class SkinAttachmentDataBuilder
    {
        public static readonly SkinAttachmentItem3 c_DummyItem = new SkinAttachmentItem3()
        {
            poseIndex = 0,
            poseCount = 1,
            baseVertex = 0,
            targetFrameW = 1,
            targetFrameDelta = Quaternion.identity,
            targetOffset = Vector3.zero
        };

        
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

        public static unsafe int BuildPosesTriangle(SkinAttachmentPose* pose, in MeshInfoUnsafe meshInfo,
            ref Vector3 target,
            int triangle, bool includeExterior)
        {
            var meshPositions = meshInfo.vertexPositions;
            var meshTriangles = meshInfo.triangles;

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

        public static unsafe int BuildPosesVertex(SkinAttachmentPose* pose, in MeshInfoUnsafe meshInfo,
            ref Vector3 target,
            int vertex, bool tryToOnlyAllowInterior)
        {
            int poseCount = 0;

            if (tryToOnlyAllowInterior)
            {
                foreach (var triangle in meshInfo.vertexTriangles[vertex])
                {
                    poseCount += BuildPosesTriangle(pose + poseCount, meshInfo, ref target, triangle,
                        includeExterior: false);
                }
            }

            if (poseCount == 0)
            {
                foreach (var triangle in meshInfo.vertexTriangles[vertex])
                {
                    poseCount += BuildPosesTriangle(pose + poseCount, meshInfo, ref target, triangle,
                        includeExterior: true);
                }
            }

            return poseCount;
        }

        public static unsafe int CountPosesTriangle(in MeshInfoUnsafe meshInfo, ref Vector3 target, int triangle,
            bool includeExterior)
        {
            SkinAttachmentPose dummyPose;
            return BuildPosesTriangle(&dummyPose, meshInfo, ref target, triangle, includeExterior);
        }

        public static unsafe int CountPosesVertex(in MeshInfoUnsafe meshInfo, ref Vector3 target, int vertex,
            bool tryToOnlyAllowInterior)
        {
            if (vertex == -1)
            {
                return 0;
            }
            
            int poseCount = 0;
            if (tryToOnlyAllowInterior)
            {
                foreach (var triangle in meshInfo.vertexTriangles[vertex])
                {
                    poseCount += CountPosesTriangle(meshInfo, ref target, triangle, includeExterior: false);
                }
            }

            if (poseCount == 0)
            {
                foreach (var triangle in meshInfo.vertexTriangles[vertex])
                {
                    poseCount += CountPosesTriangle(meshInfo, ref target, triangle, includeExterior: true);
                }
            }

            return poseCount;
        }


        public static unsafe void BuildDataAttachToVertex(SkinAttachmentPose* poses, SkinAttachmentItem* items,
            in MeshInfoUnsafe meshInfo, Vector3 targetPosition, Vector3 targetOffset,
            Vector3 targetNormal, Vector4 targetTangent, int targetVertex,
            bool tryToOnlyAllowInterior, int* attachmentOffset,
            int* poseOffset)
        {
            int currentItemIndex = *attachmentOffset;
            int currentPoseIndex = *poseOffset;

            if (targetVertex == -1)
            {
                items[currentItemIndex] = c_DummyItem;
                *attachmentOffset = currentItemIndex + 1;
            }

            var poseCount = BuildPosesVertex(poses + currentPoseIndex, meshInfo, ref targetPosition,
                targetVertex, tryToOnlyAllowInterior);
            if (poseCount == 0)
            {
                items[currentItemIndex] = c_DummyItem;
                *attachmentOffset = currentItemIndex + 1;
                return;
            }

            var baseNormal = meshInfo.vertexNormals[targetVertex];
            var baseTangent = meshInfo.vertexTangents[targetVertex];

            bool invalidTargetBasis = false;
            bool invalidSubjectBasis = false;
            if (baseNormal.sqrMagnitude < 0.00001f)
            {
                Debug.LogError($"Attachment target normal is zero! Vertex index {targetVertex}");
                invalidTargetBasis = true;
            }
            
            if (baseTangent.sqrMagnitude < 0.00001f)
            {
                Debug.LogError($"Attachment target tangent is zero! Vertex index {targetVertex}");
                invalidTargetBasis = true;
            }
            
            if (targetNormal.sqrMagnitude < 0.00001f)
            {
                Debug.LogError($"Attachment Mesh normal is zero!");
                invalidSubjectBasis = true;
            }
            
            if (targetTangent.sqrMagnitude < 0.00001f)
            {
                Debug.LogError($"Attachment Mesh tangent is zero!");
                invalidSubjectBasis = true;
            }
            
            var baseFrame = invalidTargetBasis ? Quaternion.identity : Quaternion.LookRotation(baseNormal, (Vector3) baseTangent * baseTangent.w);
            var baseFrameInv = Quaternion.Inverse(baseFrame);

            if (targetTangent.sqrMagnitude == 0)
                targetTangent = new Vector4(0, 0, 1, 1);
            var targetFrame = invalidSubjectBasis ? Quaternion.identity : Quaternion.LookRotation(targetNormal,
                (Vector3) targetTangent /* sign is omitted here, added on resolve */);

            items[currentItemIndex].poseIndex = currentPoseIndex;
            items[currentItemIndex].poseCount = poseCount;
            items[currentItemIndex].baseVertex = targetVertex;
            items[currentItemIndex].targetFrameW =
                targetTangent.w; // used to preserve the sign of the resolved tangent
            items[currentItemIndex].targetFrameDelta = baseFrameInv * targetFrame;
            items[currentItemIndex].targetOffset = baseFrameInv * targetOffset;

            currentPoseIndex += poseCount;
            currentItemIndex += 1;


            *attachmentOffset = currentItemIndex;
            *poseOffset = currentPoseIndex;

            return;
        }

        public static unsafe void CalculatePoses(ref SkinAttachmentPose[] poses, ref SkinAttachmentItem[] items, 
            in MeshInfo meshInfo, in PoseBuildSettings settings, int subjectVertexCount,
            Vector3* targetPositions, Vector3* targetNormals, Vector4* targetTangents, Vector3* targetOffsets, int* closestVertexIndices,
            int itemsArrayOffset, int posesArrayOffset, out int itemCount, out int poseCount)
        {
            //bake attachment data
            using (var poseOffsetPerItem = new UnsafeArrayInt(subjectVertexCount))
                fixed (Vector3* attachmentTargetPositions = meshInfo.meshBuffers.vertexPositions)
            fixed (Vector3* attachmentTargetNormals = meshInfo.meshBuffers.vertexNormals)
            fixed (Vector4* attachmentTargetTangents = meshInfo.meshBuffers.vertexTangents)
            fixed (int* attachmentTargetTriangles = meshInfo.meshBuffers.triangles)
            fixed (LinkedIndexItem* vertexTrianglesItems = meshInfo.meshAdjacency.vertexTriangles.items)
            fixed (LinkedIndexList* vertexTrianglesLists = meshInfo.meshAdjacency.vertexTriangles.lists)
                
            {
                MeshInfoUnsafe meshInfoUnsafe = new MeshInfoUnsafe()
                {
                    vertexPositions = attachmentTargetPositions,
                    vertexNormals = attachmentTargetNormals,
                    vertexTangents = attachmentTargetTangents,
                    triangles = attachmentTargetTriangles,
                    vertexTriangles = new LinkedIndexListArrayUnsafeView(vertexTrianglesLists, vertexTrianglesItems)
                };
                
                //count number of poses per item (and also item count, since not all source vertices are guaranteed to be valid)
                var countPosesPerItem = new CountPosesPerItemJob()
                {
                    targetPositions = targetPositions,
                    closestVertexIndices = closestVertexIndices,
                    poseCountPerItem = poseOffsetPerItem.val,
                    meshInfo = meshInfoUnsafe,
                    onlyAllowPoseTrianglesContainingAttachedPoint =
                        settings.onlyAllowPoseTrianglesContainingAttachedPoint
                };
                
                countPosesPerItem.Schedule(subjectVertexCount, 64).Complete();

                //TODO: parallelize/reduction prefixSum
                {
                    int poseSum = 0;

                    for (int i = 0; i < subjectVertexCount; ++i)
                    {
                        int poseCountPerItem = poseOffsetPerItem.val[i];
                        poseOffsetPerItem.val[i] = poseSum;
                        poseSum += poseCountPerItem;
                    }

                    poseCount = poseSum;
                }

                itemCount = subjectVertexCount;

                ArrayUtils.ResizeCheckedIfLessThan(ref poses, posesArrayOffset + poseCount);
                ArrayUtils.ResizeCheckedIfLessThan(ref items, itemsArrayOffset + itemCount);
                fixed (SkinAttachmentPose* posesPtr = poses)
                fixed (SkinAttachmentItem* itemsPtr = items)
                {
                    var calculatePosesJob = new CalculatePosesPerItemJob()
                    {
                        targetPositions = targetPositions,
                        targetNormals = targetNormals,
                        targetTangents = targetTangents,
                        targetOffsets = targetOffsets,
                        closestVertexIndices = closestVertexIndices,
                        offsetToPosesPerItem = poseOffsetPerItem.val,
                        poses = posesPtr,
                        items = itemsPtr,
                        meshInfo = meshInfoUnsafe,
                        onlyAllowPoseTrianglesContainingAttachedPoint =
                            settings.onlyAllowPoseTrianglesContainingAttachedPoint,
                        initialItemOffset = itemsArrayOffset,
                        initialPosesOffset = posesArrayOffset
                    };

                    calculatePosesJob.Schedule(subjectVertexCount, 64).Complete();
                }
            }
        }

        public static unsafe void BuildDataAttachMesh(ref SkinAttachmentPose[] poses,ref SkinAttachmentItem[] items,
            Matrix4x4 subjectToTarget, in MeshInfo meshInfo, in PoseBuildSettings settings,
            Vector3[] vertexPositions, Vector3[] vertexNormals, Vector4[] vertexTangents,
            int itemsArrayOffset, int posesArrayOffset, out int itemCount, out int poseCount)
        {
            var subjectVertexCount = vertexPositions.Length;

            using (var targetPositions = new UnsafeArrayVector3(subjectVertexCount))
            using (var targetNormals = new UnsafeArrayVector3(subjectVertexCount))
            using (var targetTangents = new UnsafeArrayVector4(subjectVertexCount))
            using (var targetOffsets = new UnsafeArrayVector3(subjectVertexCount))
            using (var closestVertexIndices = new UnsafeArrayInt(subjectVertexCount))
            {
                //calculate attachment target relative positions and find closest vertex per attached mesh vertex
                fixed (Vector3* subjectPositions = vertexPositions)
                fixed (Vector3* subjectNormals = vertexNormals)
                fixed (Vector4* subjectTangents = vertexTangents)
                {
                    var initializeTargetDataJob = new InitializeTargetDataJob()
                    {
                        subjectPositions = subjectPositions,
                        subjectNormals = subjectNormals,
                        subjectTangents = subjectTangents,
                        targetPositions = targetPositions.val,
                        targetNormals = targetNormals.val,
                        targetTangents = targetTangents.val,
                        targetOffsets = targetOffsets.val,
                        subjectToTarget = subjectToTarget
                    };
                    var jobToWait = initializeTargetDataJob.Schedule(subjectVertexCount, 64);

                    using (var validEntriesMask = new UnsafeArrayInt(meshInfo.meshBuffers.vertexCount))
                    {
                        fixed (Vector3* attachmentTargetNormals = meshInfo.meshBuffers.vertexNormals)
                        fixed (Vector4* attachmentTargetTangents = meshInfo.meshBuffers.vertexTangents)
                        {
                            var h = new GenerateValidEntriesMask()
                            {
                                normals = attachmentTargetNormals,
                                tangents = attachmentTargetTangents,
                                validEntryMask = validEntriesMask.val
                            }.Schedule(meshInfo.meshBuffers.vertexCount, 64);
                            jobToWait = JobHandle.CombineDependencies(jobToWait, h);
                        }
                        meshInfo.meshVertexBSP.FindNearestForPointsJob(targetPositions.val, closestVertexIndices.val,
                            subjectVertexCount, jobToWait, validEntriesMask.val);
                    }
                }

                CalculatePoses(ref poses, ref items, meshInfo, settings, subjectVertexCount, targetPositions.val,
                    targetNormals.val, targetTangents.val, targetOffsets.val, closestVertexIndices.val, itemsArrayOffset, posesArrayOffset, out itemCount, out poseCount);

            }
        }

        public static unsafe void BuildDataAttachMeshRoots(ref SkinAttachmentPose[] poses, ref SkinAttachmentItem[] items,
            Matrix4x4 subjectToTarget, in MeshInfo meshInfo, in PoseBuildSettings settings, bool onlyAllowOneRoot,
            MeshIslands meshIslands, MeshAdjacency meshAdjacency, Vector3[] vertexPositions, Vector3[] vertexNormals,
            Vector4[] vertexTangents, int itemsArrayOffset, int posesArrayOffset, out int itemCount, out int poseCount)
        {
            var subjectVertexCount = vertexPositions.Length;


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
                //calculate attachment target relative positions
                fixed (Vector3* subjectPositions = vertexPositions)
                fixed (Vector3* subjectNormals = vertexNormals)
                fixed (Vector4* subjectTangents = vertexTangents)
                {
                    var initializeTargetDataJob = new InitializeTargetDataJob()
                    {
                        subjectPositions = subjectPositions,
                        subjectNormals = subjectNormals,
                        subjectTangents = subjectTangents,
                        targetPositions = targetPositions.val,
                        targetNormals = targetNormals.val,
                        targetTangents = targetTangents.val,
                        targetOffsets = targetOffsets.val,
                        subjectToTarget = subjectToTarget
                    };

                    initializeTargetDataJob.Schedule(subjectVertexCount, 64).Complete();
                }
                
                visitor.Clear();

                // find island roots (TODO: parallelize this) 
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

                CalculatePoses(ref poses, ref items, meshInfo, settings, subjectVertexCount, targetPositions.val,
                    targetNormals.val, targetTangents.val, targetOffsets.val, targetVertices.val, itemsArrayOffset, posesArrayOffset, out itemCount, out poseCount);
            }
        }

        public static unsafe void BuildDataAttachTransform(ref SkinAttachmentPose[] poses, ref SkinAttachmentItem[] items,
            Matrix4x4 subjectToTarget, in MeshInfo meshInfo, in PoseBuildSettings settings,
            int itemsArrayOffset, int posesArrayOffset, out int itemCount, out int poseCount)
        {
            var targetPosition = subjectToTarget.MultiplyPoint3x4(Vector3.zero);
            var targetNormal = subjectToTarget.MultiplyVector(Vector3.up);
            Vector4 targetTangent = subjectToTarget.MultiplyVector(Vector3.right);
            targetTangent.w = 1.0f;

            fixed (Vector3* attachmentTargetPositions = meshInfo.meshBuffers.vertexPositions)
            fixed (Vector3* attachmentTargetNormals = meshInfo.meshBuffers.vertexNormals)
            fixed (Vector4* attachmentTargetTangents = meshInfo.meshBuffers.vertexTangents)
            fixed (int* attachmentTargetTriangles = meshInfo.meshBuffers.triangles)
            fixed (LinkedIndexItem* vertexTrianglesItems = meshInfo.meshAdjacency.vertexTriangles.items)
            fixed (LinkedIndexList* vertexTrianglesLists = meshInfo.meshAdjacency.vertexTriangles.lists)
            {
                int closestVertex = meshInfo.meshVertexBSP.FindNearest(ref targetPosition);

                MeshInfoUnsafe meshInfoUnsafe = new MeshInfoUnsafe()
                {
                    vertexPositions = attachmentTargetPositions,
                    vertexNormals = attachmentTargetNormals,
                    vertexTangents = attachmentTargetTangents,
                    triangles = attachmentTargetTriangles,
                    vertexTriangles = new LinkedIndexListArrayUnsafeView(vertexTrianglesLists, vertexTrianglesItems)
                };

                poseCount = CountPosesVertex(meshInfoUnsafe, ref targetPosition, closestVertex, settings.onlyAllowPoseTrianglesContainingAttachedPoint);
                itemCount = 1;
                
                ArrayUtils.ResizeCheckedIfLessThan(ref poses, posesArrayOffset + poseCount);
                ArrayUtils.ResizeCheckedIfLessThan(ref items, itemsArrayOffset + itemCount);
                fixed (SkinAttachmentPose* posesPtr = poses)
                fixed (SkinAttachmentItem* itemsPtr = items)
                {
                    BuildDataAttachToVertex(posesPtr, itemsPtr, meshInfoUnsafe, targetPosition, Vector3.zero,
                        targetNormal,
                        targetTangent, closestVertex, settings.onlyAllowPoseTrianglesContainingAttachedPoint,
                        &itemsArrayOffset, &posesArrayOffset);
                }
            }
        }

        //Burst jobs
        [BurstCompile(FloatMode = FloatMode.Fast)]
        unsafe struct GenerateValidEntriesMask : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* normals;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector4* tangents;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public int* validEntryMask;

            public void Execute(int i)
            {
                int val = (normals[i].sqrMagnitude > 0 && tangents[i].sqrMagnitude > 0) ? 1 : 0;
                validEntryMask[i] = val;
            }
        }

        
        [BurstCompile(FloatMode = FloatMode.Fast)]
        unsafe struct InitializeTargetDataJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* subjectPositions;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* subjectNormals;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector4* subjectTangents;


            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* targetPositions;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* targetNormals;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector4* targetTangents;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* targetOffsets;

            public Matrix4x4 subjectToTarget;


            public void Execute(int i)
            {
                targetPositions[i] = subjectToTarget.MultiplyPoint3x4(subjectPositions[i]);
                targetNormals[i] = subjectToTarget.MultiplyVector(subjectNormals[i]);
                Vector3 tan = subjectToTarget.MultiplyVector(subjectTangents[i]);
                targetTangents[i] = new Vector4(tan.x, tan.y, tan.z, subjectTangents[i].w);
                targetOffsets[i] = Vector3.zero;
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast)]
        unsafe struct CountPosesPerItemJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* targetPositions;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public int* closestVertexIndices;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public int* poseCountPerItem;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public int* itemCount;

            public MeshInfoUnsafe meshInfo;

            public bool onlyAllowPoseTrianglesContainingAttachedPoint;

            public void Execute(int i)
            {

                int poseCount = 0;
                poseCount = CountPosesVertex(meshInfo, ref targetPositions[i], closestVertexIndices[i], onlyAllowPoseTrianglesContainingAttachedPoint);
                poseCountPerItem[i] = poseCount;

            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast)]
        unsafe struct CalculatePosesPerItemJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* targetPositions;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* targetNormals;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector4* targetTangents;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* targetOffsets;


            [NativeDisableUnsafePtrRestriction, NoAlias]
            public int* closestVertexIndices;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public int* offsetToPosesPerItem;
            
            [NativeDisableUnsafePtrRestriction, NoAlias]
            public SkinAttachmentPose* poses;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public SkinAttachmentItem* items;

            public MeshInfoUnsafe meshInfo;

            public bool onlyAllowPoseTrianglesContainingAttachedPoint;

            public int initialItemOffset;
            public int initialPosesOffset;

            public void Execute(int i)
            {

                int itemOffset = initialItemOffset + i;
                int posesOffset = initialPosesOffset + offsetToPosesPerItem[i];
                BuildDataAttachToVertex(poses, items, meshInfo, targetPositions[i], targetOffsets[i],
                    targetNormals[i],
                    targetTangents[i], closestVertexIndices[i],
                    onlyAllowPoseTrianglesContainingAttachedPoint, &itemOffset, &posesOffset);
                
            }
        }

    }
}