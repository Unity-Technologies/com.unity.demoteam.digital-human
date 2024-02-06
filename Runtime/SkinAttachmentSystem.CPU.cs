using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

using SkinAttachmentItem = Unity.DemoTeam.DigitalHuman.SkinAttachmentItem3;

namespace Unity.DemoTeam.DigitalHuman
{
    public static partial class SkinAttachmentSystem
    {
        public struct SkinAttachmentTargetDescCPU
        {
            public Vector3[] positions;
            public Vector3[] normals;
            public Vector4[] tangents;
        }
        
        public struct SkinAttachmentDescCPU
        {
            public int itemsOffset;
            public int itemsCount;
            public SkinAttachmentItem[] skinAttachmentItems;
            public SkinAttachmentPose[] skinAttachmentPoses;
            public Vector3[] resolvedPositions;
            public Vector3[] resolvedNormals;
            public Vector4[] resolvedTangents;
            public Matrix4x4 resolveTransform;
        }

        private static JobHandle[] s_stagingJobs;
        private static GCHandle[] s_stagingPins;
        
        public static void ResolveSubjectsCPU(ref SkinAttachmentTargetDescCPU targetDescCPU,
            SkinAttachmentDescCPU[] attachments)
        {
            int stagingPinsSourceDataCount = 3;
            int stagingPinsPerAttachment = 5;
            int stagingPinsSourceDataOffset = attachments.Length * stagingPinsPerAttachment;
            ArrayUtils.ResizeChecked(ref s_stagingJobs, attachments.Length);
            ArrayUtils.ResizeChecked(ref s_stagingPins, stagingPinsSourceDataOffset + stagingPinsSourceDataCount);

            s_stagingPins[stagingPinsSourceDataOffset + 0] = GCHandle.Alloc(targetDescCPU.positions, GCHandleType.Pinned);
            s_stagingPins[stagingPinsSourceDataOffset + 1] = GCHandle.Alloc(targetDescCPU.normals, GCHandleType.Pinned);
            s_stagingPins[stagingPinsSourceDataOffset + 2] = GCHandle.Alloc(targetDescCPU.tangents, GCHandleType.Pinned);
            unsafe
            {
                Vector3* targetPositions =
                    (Vector3*)s_stagingPins[stagingPinsSourceDataOffset + 0].AddrOfPinnedObject().ToPointer();
                Vector3* targetNormals =
                    (Vector3*)s_stagingPins[stagingPinsSourceDataOffset + 1].AddrOfPinnedObject().ToPointer();
                Vector4* targetTangents =
                    (Vector4*)s_stagingPins[stagingPinsSourceDataOffset + 2].AddrOfPinnedObject().ToPointer();

                for (int i = 0; i < attachments.Length; ++i)
                {
                    ref SkinAttachmentDescCPU attachment = ref attachments[i];

                    int attachmentItemsIndex = i * stagingPinsPerAttachment + 0;
                    int attachmentPosesIndex = i * stagingPinsPerAttachment + 1;
                    int resolvedPositionsIndex = i * stagingPinsPerAttachment + 2;
                    int resolvedNormalsIndex = i * stagingPinsPerAttachment + 3;
                    int resolvedTangentsIndex = i * stagingPinsPerAttachment + 4;

                    bool resolveNormals = attachment.resolvedNormals != null;
                    bool resolveTangents = attachment.resolvedTangents != null;
                    
                    s_stagingPins[attachmentItemsIndex] =
                        GCHandle.Alloc(attachment.skinAttachmentItems, GCHandleType.Pinned);
                    s_stagingPins[attachmentPosesIndex] =
                        GCHandle.Alloc(attachment.skinAttachmentPoses, GCHandleType.Pinned);
                    s_stagingPins[resolvedPositionsIndex] =
                        GCHandle.Alloc(attachment.resolvedPositions, GCHandleType.Pinned);
                    s_stagingPins[resolvedNormalsIndex] = resolveNormals
                        ? GCHandle.Alloc(attachment.resolvedNormals, GCHandleType.Pinned)
                        : default;
                    s_stagingPins[resolvedTangentsIndex] = resolveTangents
                        ? GCHandle.Alloc(attachment.resolvedTangents, GCHandleType.Pinned)
                        : default;

                    SkinAttachmentItem* attachmentItemsPtr = (SkinAttachmentItem*) s_stagingPins[attachmentItemsIndex].AddrOfPinnedObject().ToPointer();
                    SkinAttachmentPose* attachmentPosesPtr = (SkinAttachmentPose*) s_stagingPins[attachmentPosesIndex].AddrOfPinnedObject().ToPointer();
                    Vector3* resolvedPositions = (Vector3*) s_stagingPins[resolvedPositionsIndex].AddrOfPinnedObject().ToPointer();
                    Vector3* resolvedNormals = resolveNormals ? (Vector3*) s_stagingPins[resolvedNormalsIndex].AddrOfPinnedObject().ToPointer() : null;
                    Vector4* resolvedTangents = resolveTangents ? (Vector4*) s_stagingPins[resolvedTangentsIndex].AddrOfPinnedObject().ToPointer() : null; 
                    
                    s_stagingJobs[i] = ScheduleResolve(attachment.itemsOffset, attachment.itemsCount, attachmentItemsPtr, attachmentPosesPtr,
                        ref attachment.resolveTransform, targetPositions, targetNormals, targetTangents,
                        resolvedPositions, resolvedNormals, resolvedTangents);

                }
            }

            JobHandle.ScheduleBatchedJobs();
            
            
            while (true)
            {
                var jobsRunning = false;

                for (int i = 0; i < attachments.Length; i++)
                {

                    var stillRunning = (s_stagingJobs[i].IsCompleted == false);
                    if (stillRunning)
                    {
                        jobsRunning = true;
                        continue;
                    }

                    int attachmentItemsIndex = i * stagingPinsPerAttachment + 0;
                    int attachmentPosesIndex = i * stagingPinsPerAttachment + 1;
                    int resolvedPositionsIndex = i * stagingPinsPerAttachment + 2;
                    int resolvedNormalsIndex = i * stagingPinsPerAttachment + 3;
                    int resolvedTangentsIndex = i * stagingPinsPerAttachment + 4;

                    bool alreadyApplied = s_stagingPins[resolvedPositionsIndex].IsAllocated == false;

                    if (alreadyApplied)
                        continue;

                    s_stagingPins[resolvedPositionsIndex].Free();
                    s_stagingPins[attachmentItemsIndex].Free();
                    s_stagingPins[attachmentPosesIndex].Free();
                    
                    if (s_stagingPins[resolvedNormalsIndex].IsAllocated)
                    {
                        s_stagingPins[resolvedNormalsIndex].Free();
                    }
                    if (s_stagingPins[resolvedTangentsIndex].IsAllocated)
                    {
                        s_stagingPins[resolvedTangentsIndex].Free();
                    }
                }

                if (jobsRunning == false)
                    break;
            }

            for (int i = 0; i != stagingPinsSourceDataCount; i++)
            {
                s_stagingPins[stagingPinsSourceDataOffset + i].Free();
            }
            
        }
        
        public static float3x3 ConstructMatrix(float3 normal, float3 tangent, float tangentW)
        {
            float3 bitangent = math.cross(normal, tangent) * math.sign(tangentW);
            return math.transpose(new float3x3(tangent, bitangent, normal));
        }

         public static unsafe JobHandle ScheduleResolve(int attachmentIndex, int attachmentCount, SkinAttachmentItem* attachmentItems, SkinAttachmentPose* attachmentPoses,
             ref Matrix4x4 resolveTransform, Vector3* targetPositions, Vector3* targetNormals, Vector4* targetTangents, Vector3* resolvedPositions, Vector3* resolvedNormals, Vector4* resolvedTangents)
        {
            var job = new ResolveJob()
            {
                meshPositions = targetPositions,
                meshNormals = targetNormals,
                meshTangents = targetTangents,
                attachItem = attachmentItems,
                attachPose = attachmentPoses,
                resolveTransform = resolveTransform,
                resolvedPositions = resolvedPositions,
                resolvedNormals = resolvedNormals,
                resolvedTangents = resolvedTangents,
                writeTangents = resolvedTangents != null,
                attachmentIndex = attachmentIndex,
                attachmentCount = attachmentCount,
            };
            return job.Schedule(attachmentCount, 64);
        }

        [BurstCompile(FloatMode = FloatMode.Fast)]
        unsafe struct ResolveJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* meshPositions;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* meshNormals;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector4* meshTangents;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public SkinAttachmentItem3* attachItem;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public SkinAttachmentPose* attachPose;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* resolvedPositions;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* resolvedNormals;
            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector4* resolvedTangents;

            public Matrix4x4 resolveTransform;

            public int attachmentIndex;
            public int attachmentCount;

            public bool writeTangents;

            //TODO this needs optimization
            public void Execute(int i)
            {
                var targetBlended = new Vector3(0.0f, 0.0f, 0.0f);
                var targetWeights = 0.0f;

                SkinAttachmentItem3 item = attachItem[attachmentIndex + i];

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

                ref readonly var baseNormal = ref meshNormals[item.baseVertex];
                ref readonly var baseTangent = ref meshTangents[item.baseVertex];
                
                var baseFrame = Quaternion.LookRotation(baseNormal, (Vector3)baseTangent * baseTangent.w);

                var targetFrame = baseFrame * item.targetFrameDelta;
                var targetOffset = baseFrame * item.targetOffset;
                var targetNormal = targetFrame * Vector3.forward;
                var targetTangent = targetFrame * Vector3.up;

                resolvedPositions[i] = resolveTransform.MultiplyPoint3x4(targetBlended / targetWeights + targetOffset);
                resolvedNormals[i] = resolveTransform.MultiplyVector(targetNormal).normalized;

                if (writeTangents)
                {
                    targetTangent = resolveTransform.MultiplyVector(targetTangent).normalized;
                    resolvedTangents[i] = new Vector4(targetTangent.x, targetTangent.y, targetTangent.z, item.targetFrameW);
                }
            }
        }

    }
}