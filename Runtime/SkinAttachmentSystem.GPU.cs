using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.DemoTeam.DigitalHuman
{
    using SkinAttachmentItem = SkinAttachmentItem3;

    public static partial class SkinAttachmentSystem
    {
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct SkinAttachmentPoseGPU
        {
            public float3 targetCoord;
            public int v0;
            public int v1;
            public int v2;
            public float area;
            public float targetDist;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct SkinAttachmentItemGPU
        {
            public float4 targetFrameDelta;
            public float3 targetOffset;
            public float targetFrameW;
            public int baseVertex;
            public int poseIndex;
            public int poseCount;
            public int pad0;
        };


        static class UniformsResolve
        {
            internal static int _AttachmentPosesBuffer = Shader.PropertyToID("_AttachmentPosesBuffer");
            internal static int _AttachmentItemsBuffer = Shader.PropertyToID("_AttachmentItemsBuffer");

            internal static int _TransformAttachmentOffsetBuffer =
                Shader.PropertyToID("_TransformAttachmentOffsetBuffer");

            internal static int _SkinPositionsBuffer = Shader.PropertyToID("_SkinPositionsBuffer");
            internal static int _SkinNormalsBuffer = Shader.PropertyToID("_SkinNormalsBuffer");
            internal static int _SkinTangentsBuffer = Shader.PropertyToID("_SkinTangentsBuffer");
            internal static int _SkinPositionStrideOffset = Shader.PropertyToID("_SkinPositionStrideOffset");
            internal static int _SkinNormalStrideOffset = Shader.PropertyToID("_SkinNormalStrideOffset");
            internal static int _SkinTangentStrideOffset = Shader.PropertyToID("_SkinTangentStrideOffset");
            internal static int _AttachmentPosNormTanBuffer = Shader.PropertyToID("_AttachmentPosNormTanBuffer");
            internal static int _AttachmentMovecsBuffer = Shader.PropertyToID("_AttachmentMovecsBuffer");

            internal static int _StrideOffsetPosNormTanAttachment =
                Shader.PropertyToID("_StrideOffsetPosNormTanAttachment");

            internal static int _StrideOffsetMovecsAttachment = Shader.PropertyToID("_StrideOffsetMovecsAttachment");
            internal static int _ResolveTransform = Shader.PropertyToID("_ResolveTransform");

            internal static int _PostSkinningToAttachmentTransform =
                Shader.PropertyToID("_PostSkinningToAttachmentTransform");

            internal static int _NumberOfAttachments = Shader.PropertyToID("_NumberOfAttachments");
            internal static int _AttachmentOffset = Shader.PropertyToID("_AttachmentOffset");
        }

        public struct SkinAttachmentTargetDescGPU
        {
            public GraphicsBuffer positionsBuffer;
            public GraphicsBuffer normalsBuffer;
            public GraphicsBuffer tangentsBuffer;
            public ValueTuple<int, int> positionsOffsetStride;
            public ValueTuple<int, int> normalsOffsetStride;
            public ValueTuple<int, int> tangentsOffsetStride;
            public Matrix4x4 postSkinningTransform;
            public bool releaseGPUBuffersAfterResolve;
        }

        public struct SkinAttachmentDescGPU
        {
            public GraphicsBuffer itemsBuffer;
            public GraphicsBuffer posesBuffer;
            public GraphicsBuffer positionsNormalsTangentsBuffer;
            public GraphicsBuffer movecsBuffer;
            public int itemsOffset;
            public int itemsCount;
            public ValueTuple<int, int, int, int> positionsNormalsTangentsOffsetStride;
            public ValueTuple<int, int> movecsOffsetStride;
            public Matrix4x4 targetToAttachment;
            public bool resolveNormalsAndTangents;
            public bool releaseOutputBuffersAfterResolve;
        }

        public static void ResolveSubjectsGPU(CommandBuffer cmd, ref SkinAttachmentTargetDescGPU targetDescGPU,
            SkinAttachmentDescGPU[] attachments)
        {
            if (s_resolveAttachmentsCS == null || attachments == null || attachments.Length == 0) return;

            GraphicsBuffer skinPositionsBuffer = targetDescGPU.positionsBuffer;
            GraphicsBuffer skinNormalsBuffer = targetDescGPU.normalsBuffer;
            GraphicsBuffer skinTangentsBuffer = targetDescGPU.tangentsBuffer;

            if (skinPositionsBuffer == null || skinNormalsBuffer == null || skinTangentsBuffer == null)
            {
                Debug.LogError(
                    "ResolveSubjectsGPU failed: the GPU buffer(s) for the target mesh was null, skipping resolve");
                return;
            }

            Vector2Int skinPositionStrideOffset = new Vector2Int(targetDescGPU.positionsOffsetStride.Item2,
                targetDescGPU.positionsOffsetStride.Item1);
            Vector2Int skinNormalStrideOffset =
                new Vector2Int(targetDescGPU.normalsOffsetStride.Item2, targetDescGPU.normalsOffsetStride.Item1);
            Vector2Int skinTangentStrideOffset =
                new Vector2Int(targetDescGPU.tangentsOffsetStride.Item2, targetDescGPU.tangentsOffsetStride.Item1);

            Matrix4x4 postSkinningToAttachment = targetDescGPU.postSkinningTransform;

            cmd.BeginSample("Resolve Attachments");
            //common uniforms
            cmd.SetComputeIntParams(s_resolveAttachmentsCS, UniformsResolve._SkinPositionStrideOffset,
                skinPositionStrideOffset.x, skinPositionStrideOffset.y);
            cmd.SetComputeIntParams(s_resolveAttachmentsCS, UniformsResolve._SkinNormalStrideOffset,
                skinNormalStrideOffset.x, skinNormalStrideOffset.y);
            cmd.SetComputeIntParams(s_resolveAttachmentsCS, UniformsResolve._SkinTangentStrideOffset,
                skinTangentStrideOffset.x, skinTangentStrideOffset.y);


            for (int i = 0; i < attachments.Length; i++)
            {
                ref SkinAttachmentDescGPU skinAttachment = ref attachments[i];

                if (skinAttachment.positionsNormalsTangentsBuffer == null)
                {
                    continue;
                }

                int resolveKernel = s_resolveAttachmentsPosKernel;
                if (skinAttachment.resolveNormalsAndTangents)
                {
                    resolveKernel = s_resolveAttachmentsPosNormalKernel;
                }

                if (skinAttachment.movecsBuffer != null)
                {
                    resolveKernel = s_resolveAttachmentsPosNormalMovecKernel;
                }


                cmd.SetComputeIntParams(s_resolveAttachmentsCS, UniformsResolve._StrideOffsetPosNormTanAttachment,
                    skinAttachment.positionsNormalsTangentsOffsetStride.Item4,
                    skinAttachment.positionsNormalsTangentsOffsetStride.Item1,
                    skinAttachment.positionsNormalsTangentsOffsetStride.Item2,
                    skinAttachment.positionsNormalsTangentsOffsetStride.Item3);
                cmd.SetComputeBufferParam(s_resolveAttachmentsCS, resolveKernel,
                    UniformsResolve._AttachmentPosNormTanBuffer, skinAttachment.positionsNormalsTangentsBuffer);


                //movecs TODO: currently assumes also normals and tangents, no real reason for that
                if (skinAttachment.movecsBuffer != null)
                {
                    GraphicsBuffer movecsVertexBuffer = skinAttachment.movecsBuffer;

                    cmd.SetComputeIntParams(s_resolveAttachmentsCS, UniformsResolve._StrideOffsetMovecsAttachment,
                        skinAttachment.movecsOffsetStride.Item2, skinAttachment.movecsOffsetStride.Item1);
                    cmd.SetComputeBufferParam(s_resolveAttachmentsCS, resolveKernel,
                        UniformsResolve._AttachmentMovecsBuffer, movecsVertexBuffer);
                }


                cmd.SetComputeBufferParam(s_resolveAttachmentsCS, resolveKernel,
                    UniformsResolve._AttachmentPosesBuffer,
                    skinAttachment.posesBuffer);
                cmd.SetComputeBufferParam(s_resolveAttachmentsCS, resolveKernel,
                    UniformsResolve._AttachmentItemsBuffer,
                    skinAttachment.itemsBuffer);

                cmd.SetComputeBufferParam(s_resolveAttachmentsCS, resolveKernel,
                    UniformsResolve._SkinPositionsBuffer,
                    skinPositionsBuffer);
                cmd.SetComputeBufferParam(s_resolveAttachmentsCS, resolveKernel,
                    UniformsResolve._SkinNormalsBuffer,
                    skinNormalsBuffer);
                cmd.SetComputeBufferParam(s_resolveAttachmentsCS, resolveKernel,
                    UniformsResolve._SkinTangentsBuffer,
                    skinTangentsBuffer);


                cmd.SetComputeMatrixParam(s_resolveAttachmentsCS, UniformsResolve._ResolveTransform,
                    skinAttachment.targetToAttachment);
                cmd.SetComputeMatrixParam(s_resolveAttachmentsCS, UniformsResolve._PostSkinningToAttachmentTransform,
                    postSkinningToAttachment);

                cmd.SetComputeIntParam(s_resolveAttachmentsCS, UniformsResolve._NumberOfAttachments,
                    skinAttachment.itemsCount);
                cmd.SetComputeIntParam(s_resolveAttachmentsCS, UniformsResolve._AttachmentOffset,
                    skinAttachment.itemsOffset);

                s_resolveAttachmentsCS.GetKernelThreadGroupSizes(resolveKernel, out uint groupX, out uint groupY,
                    out uint groupZ);
                int dispatchCount = (skinAttachment.itemsCount + (int)groupX - 1) / (int)groupX;

                cmd.DispatchCompute(s_resolveAttachmentsCS, resolveKernel, dispatchCount, 1, 1);
            }

            cmd.EndSample("Resolve Attachments");
        }

        #region UtilityGPU

        public static bool FillSkinAttachmentTargetDesc(SkinnedMeshRenderer smr, ref SkinAttachmentTargetDescGPU desc)
        {
            if (!smr || !smr.sharedMesh) return false;
            Mesh skinMesh = smr.sharedMesh;
            if (skinMesh == null) return false;

            int positionStream = skinMesh.GetVertexAttributeStream(VertexAttribute.Position);
            int normalStream = skinMesh.GetVertexAttributeStream(VertexAttribute.Normal);
            int tangentStream = skinMesh.GetVertexAttributeStream(VertexAttribute.Tangent);

            if ((smr.vertexBufferTarget & GraphicsBuffer.Target.Raw) == 0)
            {
                smr.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            }

            if (normalStream == -1)
            {
                Debug.LogError(
                    "SkinAttachment target (SkinnedMeshRenderer) does not have normals, the attachment resolve will not be correct!");
            }
            
            if (tangentStream == -1)
            {
                Debug.LogError(
                    "SkinAttachment target (SkinnedMeshRenderer) does not have tangents, the attachment resolve will not be correct!");
            }
            
            GraphicsBuffer skinPositionsBuffer = smr.GetVertexBuffer();
            GraphicsBuffer skinNormalsBuffer = smr.GetVertexBuffer();
            GraphicsBuffer skinTangentsBuffer = smr.GetVertexBuffer();

            if (skinPositionsBuffer == null || skinNormalsBuffer == null || skinTangentsBuffer == null)
            {
                //Don't issue an error if the skinnedmeshrenderer was not visible 
                if (smr == null || smr.isVisible || smr.updateWhenOffscreen)
                {
                    Debug.LogError(
                        "SkinAttachmentTarget unable to fetch vertex attribute buffers, unable to drive attachments");
                }
                
                skinPositionsBuffer?.Release();
                skinNormalsBuffer?.Release();
                skinTangentsBuffer?.Release();
                return false;
            }

            desc.positionsBuffer = skinPositionsBuffer;
            desc.positionsOffsetStride.Item1 = skinMesh.GetVertexAttributeOffset(VertexAttribute.Position);
            desc.positionsOffsetStride.Item2 = skinMesh.GetVertexBufferStride(positionStream);

            desc.normalsBuffer = skinNormalsBuffer;
            desc.normalsOffsetStride.Item1 = skinMesh.GetVertexAttributeOffset(VertexAttribute.Normal);
            desc.normalsOffsetStride.Item2 = skinMesh.GetVertexBufferStride(normalStream);

            desc.tangentsBuffer = skinTangentsBuffer;
            desc.tangentsOffsetStride.Item1 = skinMesh.GetVertexAttributeOffset(VertexAttribute.Tangent);
            desc.tangentsOffsetStride.Item2 = skinMesh.GetVertexBufferStride(tangentStream);

            //need to apply rootbone transform to skinned vertices when resolving since bakemesh has applied it when attachdata is calculated.
            Matrix4x4 postSkinningToAttachment = Matrix4x4.identity;
            
            if (smr.rootBone && smr.bones.Length > 0) //if there are no bones, assume that the root bone has been baked to the mesh
            {
                Matrix4x4 boneLocalToWorldNoScale =
                    Matrix4x4.TRS(smr.rootBone.position, smr.rootBone.rotation, Vector3.one);
                postSkinningToAttachment = smr.transform.parent.worldToLocalMatrix * boneLocalToWorldNoScale;
            }

            desc.postSkinningTransform = postSkinningToAttachment;
            desc.releaseGPUBuffersAfterResolve = true;
            return true;
        }

        public static bool FillSkinAttachmentTargetDesc(MeshRenderer mr, MeshFilter mf,
            ref SkinAttachmentTargetDescGPU desc, bool releaseOutputBuffersAfterResolve = true)
        {
            if (!mr || !mf || !mf.sharedMesh) return false;
            Mesh skinMesh = mf.sharedMesh;
            if (skinMesh == null) return false;

            int positionStream = skinMesh.GetVertexAttributeStream(VertexAttribute.Position);
            int normalStream = skinMesh.GetVertexAttributeStream(VertexAttribute.Normal);
            int tangentStream = skinMesh.GetVertexAttributeStream(VertexAttribute.Tangent);

            if ((mf.sharedMesh.vertexBufferTarget & GraphicsBuffer.Target.Raw) == 0)
            {
                mf.sharedMesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            }

            GraphicsBuffer skinPositionsBuffer = skinMesh.GetVertexBuffer(positionStream);
            GraphicsBuffer skinNormalsBuffer = skinMesh.GetVertexBuffer(normalStream);
            GraphicsBuffer skinTangentsBuffer = skinMesh.GetVertexBuffer(tangentStream);

            if (skinPositionsBuffer == null || skinNormalsBuffer == null || skinTangentsBuffer == null)
            {
                skinPositionsBuffer?.Release();
                skinNormalsBuffer?.Release();
                skinTangentsBuffer?.Release();
                
                Debug.LogError(
                    "SkinAttachmentTarget unable to fetch vertex attribute buffers, unable to drive attachments");
                return false;
            }

            desc.positionsBuffer = skinPositionsBuffer;
            desc.positionsOffsetStride.Item1 = skinMesh.GetVertexAttributeOffset(VertexAttribute.Position);
            desc.positionsOffsetStride.Item2 = skinMesh.GetVertexBufferStride(positionStream);

            desc.normalsBuffer = skinNormalsBuffer;
            desc.normalsOffsetStride.Item1 = skinMesh.GetVertexAttributeOffset(VertexAttribute.Normal);
            desc.normalsOffsetStride.Item2 = skinMesh.GetVertexBufferStride(normalStream);

            desc.tangentsBuffer = skinTangentsBuffer;
            desc.tangentsOffsetStride.Item1 = skinMesh.GetVertexAttributeOffset(VertexAttribute.Tangent);
            desc.tangentsOffsetStride.Item2 = skinMesh.GetVertexBufferStride(tangentStream);

            Matrix4x4 postSkinningToAttachment = Matrix4x4.identity;
            desc.postSkinningTransform = postSkinningToAttachment;
            desc.releaseGPUBuffersAfterResolve = true;
            return true;
        }


        public static bool FillSkinAttachmentDesc(Mesh attachmentMesh, Matrix4x4 targetToAttachments,
            GraphicsBuffer posesBuffer, GraphicsBuffer itemsBuffer,
            int itemsOffset, int itemsCount, bool releaseOutputBuffersAfterResolve, ref SkinAttachmentDescGPU desc)
        {
            if (!attachmentMesh || posesBuffer == null || itemsBuffer == null) return false;

            int posStream = attachmentMesh.GetVertexAttributeStream(VertexAttribute.Position);
            int normStream = attachmentMesh.GetVertexAttributeStream(VertexAttribute.Normal);
            int tanStream = attachmentMesh.GetVertexAttributeStream(VertexAttribute.Tangent);

            if (posStream == -1 || posStream != normStream || (posStream != tanStream && tanStream != -1))
            {
                Debug.LogError(
                    "Attachment is required to have positions and normals (and tangents if available) in the same stream. Skipping attachment");
                return false;
            }

            desc.posesBuffer = posesBuffer;
            desc.itemsBuffer = itemsBuffer;
            desc.itemsOffset = itemsOffset;
            desc.itemsCount = itemsCount;

            desc.positionsNormalsTangentsBuffer = attachmentMesh.GetVertexBuffer(posStream);
            desc.positionsNormalsTangentsOffsetStride.Item1 =
                attachmentMesh.GetVertexAttributeOffset(VertexAttribute.Position);
            desc.positionsNormalsTangentsOffsetStride.Item2 =
                attachmentMesh.GetVertexAttributeOffset(VertexAttribute.Normal);
            desc.positionsNormalsTangentsOffsetStride.Item3 =
                attachmentMesh.GetVertexAttributeOffset(VertexAttribute.Tangent);
            desc.positionsNormalsTangentsOffsetStride.Item4 = attachmentMesh.GetVertexBufferStride(posStream);

            if (attachmentMesh.HasVertexAttribute(VertexAttribute.TexCoord5))
            {
                int movecsStream = attachmentMesh.GetVertexAttributeStream(VertexAttribute.TexCoord5);

                desc.movecsBuffer = attachmentMesh.GetVertexBuffer(movecsStream);
                desc.movecsOffsetStride.Item1 = attachmentMesh.GetVertexAttributeOffset(VertexAttribute.TexCoord5);
                desc.movecsOffsetStride.Item2 = attachmentMesh.GetVertexBufferStride(movecsStream);
            }

            desc.targetToAttachment = targetToAttachments;
            desc.resolveNormalsAndTangents = true;
            desc.releaseOutputBuffersAfterResolve = releaseOutputBuffersAfterResolve;
            return true;
        }


        public static void FreeSkinAttachmentTargetDesc(in SkinAttachmentTargetDescGPU desc)
        {
            if (desc.releaseGPUBuffersAfterResolve)
            {
                desc.positionsBuffer?.Dispose();
                desc.normalsBuffer?.Dispose();
                desc.tangentsBuffer?.Dispose();
            }
        }

        public static void FreeSkinAttachmentDesc(in SkinAttachmentDescGPU desc)
        {
            if (desc.releaseOutputBuffersAfterResolve)
            {
                desc.positionsNormalsTangentsBuffer?.Dispose();
                desc.movecsBuffer?.Dispose();
            }
        }


        public static void UploadAttachmentPoseDataToGPU(in SkinAttachmentItem[] bakedAttachmentItems,
            in SkinAttachmentPose[] bakedAttachmentPoses,  int itemsCount, int posesCount,
            ref GraphicsBuffer bakedAttachmentItemsGPU, ref GraphicsBuffer bakedAttachmentPosesGPU, bool forceBufferSizeToMatch = true)
        {
            int itemStructSize = UnsafeUtility.SizeOf<SkinAttachmentSystem.SkinAttachmentItemGPU>();
            int poseStructSize = UnsafeUtility.SizeOf<SkinAttachmentSystem.SkinAttachmentPoseGPU>();

            if (bakedAttachmentPosesGPU == null || (forceBufferSizeToMatch ? bakedAttachmentPosesGPU.count != posesCount : bakedAttachmentPosesGPU.count < posesCount))
            {
                if (bakedAttachmentPosesGPU != null)
                {
                    bakedAttachmentPosesGPU.Release();
                }

                bakedAttachmentPosesGPU =
                    new GraphicsBuffer(GraphicsBuffer.Target.Structured, posesCount, poseStructSize);
            }

            if (bakedAttachmentItemsGPU == null || (forceBufferSizeToMatch ? bakedAttachmentItemsGPU.count != itemsCount : bakedAttachmentItemsGPU.count < itemsCount))
            {
                if (bakedAttachmentItemsGPU != null)
                {
                    bakedAttachmentItemsGPU.Release();
                }

                bakedAttachmentItemsGPU =
                    new GraphicsBuffer(GraphicsBuffer.Target.Structured, itemsCount, itemStructSize);
            }


            NativeArray<SkinAttachmentPoseGPU> posesBuffer =
                new NativeArray<SkinAttachmentPoseGPU>(posesCount, Allocator.Temp);
            for (int i = 0; i < posesCount; ++i)
            {
                SkinAttachmentPoseGPU poseGPU;
                poseGPU.targetCoord.x = bakedAttachmentPoses[i].targetCoord.u;
                poseGPU.targetCoord.y = bakedAttachmentPoses[i].targetCoord.v;
                poseGPU.targetCoord.z = bakedAttachmentPoses[i].targetCoord.w;
                poseGPU.v0 = bakedAttachmentPoses[i].v0;
                poseGPU.v1 = bakedAttachmentPoses[i].v1;
                poseGPU.v2 = bakedAttachmentPoses[i].v2;
                poseGPU.area = bakedAttachmentPoses[i].area;
                poseGPU.targetDist = bakedAttachmentPoses[i].targetDist;
                posesBuffer[i] = poseGPU;
            }

            bakedAttachmentPosesGPU.SetData(posesBuffer);
            posesBuffer.Dispose();

            NativeArray<SkinAttachmentItemGPU> itemsBuffer =
                new NativeArray<SkinAttachmentItemGPU>(itemsCount, Allocator.Temp);
            for (int i = 0; i < itemsCount; ++i)
            {
                SkinAttachmentItem item = bakedAttachmentItems[i];

                SkinAttachmentItemGPU itemGPU;
                itemGPU.targetFrameDelta = new float4(item.targetFrameDelta[0], item.targetFrameDelta[1],
                    item.targetFrameDelta[2], item.targetFrameDelta[3]);
                itemGPU.targetOffset = item.targetOffset;
                itemGPU.targetFrameW = item.targetFrameW;
                itemGPU.baseVertex = item.baseVertex;
                itemGPU.poseIndex = item.poseIndex;
                itemGPU.poseCount = item.poseCount;
                itemGPU.pad0 = 0;
                itemsBuffer[i] = itemGPU;
            }

            bakedAttachmentItemsGPU.SetData(itemsBuffer);
            itemsBuffer.Dispose();
        }

        #endregion UtilityGPU
    }
}