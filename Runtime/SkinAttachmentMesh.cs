using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.DemoTeam.DigitalHuman
{
    using SkinAttachmentItem = SkinAttachmentItem3;

    [ExecuteAlways]
    public class SkinAttachmentMesh : MeshInstanceBehaviour, ISkinAttachmentMesh
    {
        public enum MeshAttachmentType
        {
            Mesh,
            MeshRoots,
        }

        public enum SchedulingMode
        {
            CPU,
            GPU,
            External
        }

        public Renderer attachmentTarget;
        public SkinAttachmentDataStorage dataStorage;
        public SchedulingMode schedulingMode;
        public MeshAttachmentType attachmentType = MeshAttachmentType.Mesh;
        public bool allowOnlyOneRoot = false;
        public bool GeneratePrecalculatedMotionVectors = false;

        public event Action<CommandBuffer> onSkinAttachmentMeshModified;

        public bool IsAttached => attached;
        
        private struct BakeData
        {
            public MeshBuffers meshBuffers;
            public MeshAdjacency meshAdjacency;
            public MeshIslands meshIslands;
        }

        [SerializeField][HideInInspector] private bool attached = false;
        [SerializeField][HideInInspector] private Vector3 attachedLocalPosition;
        [SerializeField][HideInInspector] private Quaternion attachedLocalRotation;
        [SerializeField][HideInInspector] private Hash128 checkSum;
        [SerializeField][HideInInspector] private SkinAttachmentDataStorage currentStorage;

        private float meshAssetRadius;
        private Transform skinningBone;
        private Matrix4x4 skinningBoneBindPose;
        private Matrix4x4 skinningBoneBindPoseInverse;

        private Renderer currentTarget;

        private Vector3[] stagingPositions;
        private Vector3[] stagingNormals;
        private Vector4[] stagingTangents;
        
        private GraphicsBuffer bakedAttachmentPosesGPU;
        private GraphicsBuffer bakedAttachmentItemsGPU;
        private SkinAttachmentPose[] bakedPoses;
        private SkinAttachmentItem[] bakedItems;

        private bool GPUDataValid = false;
        private bool hasValidState = false;

        public void Attach(bool storePositionRotation = true)
        {
            if (storePositionRotation)
            {
                attachedLocalPosition = transform.localPosition;
                attachedLocalRotation = transform.localRotation;
            }

            attached = true;

            UpdateAttachedState();
        }

        public void Detach(bool revertPositionRotation = true)
        {
            RemoveMeshInstance();

            if (revertPositionRotation)
            {
                transform.localPosition = attachedLocalPosition;
                transform.localRotation = attachedLocalRotation;
            }

            attached = false;
            currentTarget = null;
        }

        void OnEnable()
        {
            LoadBakedData();
            UpdateAttachedState();
        }

        private void OnDisable()
        {
            ReleaseGPUResources();
        }

        void LateUpdate()
        {
            UpdateAttachedState();
            if (hasValidState && schedulingMode != SchedulingMode.External)
            {
                SkinAttachmentSystem.Inst.QueueAttachmentResolve(this, schedulingMode == SchedulingMode.GPU);
            }
        }


        void OnDestroy()
        {
            RemoveMeshInstance();
        }

        public Hash128 Checksum()
        {
            return checkSum;
        }

        public Renderer GetTargetRenderer()
        {
            return currentTarget;
        }

        public void NotifyAttachmentUpdated(CommandBuffer cmd)
        {
            //update mesh bounds
            {
                //update mesh bounds
                var targetMeshWorldBounds = currentTarget.bounds;
                var targetMeshWorldBoundsCenter = targetMeshWorldBounds.center;
                var targetMeshWorldBoundsExtent = targetMeshWorldBounds.extents;

                var worldToSubject = transform.worldToLocalMatrix;
                var subjectBoundsCenter = worldToSubject.MultiplyPoint(targetMeshWorldBoundsCenter);
                var subjectBoundsRadius =
                    worldToSubject.MultiplyVector(targetMeshWorldBoundsExtent).magnitude +
                    meshAssetRadius;
                var subjectBounds = meshInstance.bounds;
                {
                    subjectBounds.center = subjectBoundsCenter;
                    subjectBounds.extents = subjectBoundsRadius * Vector3.one;
                }
                meshInstance.bounds = subjectBounds;
            }
            
            //if cpu resolved, replicate data to mesh
            if(cmd == null)
            {
                bool resolveTangents = meshInstance.HasVertexAttribute(VertexAttribute.Tangent);
                meshInstance.SilentlySetVertices(stagingPositions);
                meshInstance.SilentlySetNormals(stagingNormals);

                if (resolveTangents)
                {
                    meshInstance.SilentlySetTangents(stagingTangents);
                }
            }


            if (onSkinAttachmentMeshModified != null)
                onSkinAttachmentMeshModified(cmd);
        }

        public bool ValidateBakedData()
        {

            bool dataExists = currentStorage != null && bakedPoses != null && bakedItems != null;
            if (dataExists && meshInstance != null)
            {
                bool meshInstanceVertexCountMatches = bakedItems.Length == meshInstance.vertexCount;
                return meshInstanceVertexCountMatches;
            }

            return false;
        }

        public void EnsureBakedData()
        {
            bool newDataBaked = false;
            SkinAttachmentItem[] tempItems = default;
            SkinAttachmentPose[] tempPoses = default;
            
            if (currentTarget == null)
            {
                bool bakeSuccessfull = BakeAttachmentPoses(ref tempItems, ref tempPoses);
                if (bakeSuccessfull)
                {
                    currentTarget = attachmentTarget;
                    newDataBaked = true;
                }
            } 
            else if (!ValidateBakedData())
            {
                bool bakeSuccessfull = BakeAttachmentPoses(ref tempItems, ref tempPoses);
                if (bakeSuccessfull)
                {
                    newDataBaked = true;
                }
            }

            if (newDataBaked)
            {
                StoreBakedData(tempItems, tempPoses);
            }
        }

        public bool CanAttach()
        {
            return IsAttachmentTargetValid() && IsAttachmentMeshValid() && dataStorage != null && !IsAttached;
        }

        public bool IsAttachmentTargetValid()
        {
            return SkinAttachmentSystem.IsValidAttachmentTarget(attachmentTarget);
        }

        public bool IsAttachmentMeshValid()
        {
            return meshAsset != null && meshAsset.isReadable;
        }

        void UpdateAttachedState()
        {
            hasValidState = false;
            if (attachmentTarget == null) return;

            //make sure target is a supported renderer (meshrenderer or skinnedMeshRenderer)
            if (!(attachmentTarget is SkinnedMeshRenderer || attachmentTarget is MeshRenderer))
            {
                attachmentTarget = null;
                currentTarget = null;
                Detach();
                return;
            }

            //target changed, detach
            if (currentTarget != attachmentTarget && currentTarget != null)
            {
                Detach();
                return;
            }

            if (attached)
            {
                if (!IsAttachmentMeshValid() || !IsAttachmentTargetValid())
                {
                    hasValidState = false;
                    return;
                }
                
                //if we need to generate motion vectors and the meshInstance doesn't have proper streams setup, reset the meshinstance (so it gets recreated)
                if (GeneratePrecalculatedMotionVectors && meshInstance &&
                    !meshInstance.HasVertexAttribute(VertexAttribute.TexCoord5))
                {
                    RemoveMeshInstance();
                }

                ValidateDataStorage();
                EnsureBakedData();

                hasValidState = currentTarget != null && ValidateBakedData();
                
                EnsureMeshInstance();
            }
            else
            {
                RemoveMeshInstance();
            }
        }

        public void ValidateDataStorage()
        {
            if (currentStorage != null && currentStorage != dataStorage)
            {
                if (checkSum.isValid)
                {
                    currentStorage.RemoveAttachmentData(checkSum);
                    checkSum = default;
                }
                currentStorage = null;
            }
            
            currentStorage = dataStorage;
        }

        void StoreBakedData(SkinAttachmentItem[] items, SkinAttachmentPose[] poses)
        {
            if (currentStorage != null)
            {
                if (checkSum.isValid)
                {
                    checkSum = currentStorage.UpdateAttachmentData(poses, items, checkSum);
                }
                else
                {
                    checkSum = currentStorage.StoreAttachmentData(poses, items);
                }

                LoadBakedData();
            }
            
        }

        void LoadBakedData()
        {
            if (checkSum.isValid && currentStorage != null)
            {
                currentStorage.LoadAttachmentData(checkSum, out bakedPoses, out bakedItems);
            }
        }
        

        static void BuildDataAttachSubject(in SkinAttachmentPose[] posesArray, in SkinAttachmentItem[] itemsArray,
            in BakeData attachmentBakeData, Matrix4x4 subjectToTarget,
            in MeshInfo targetBakeData, in PoseBuildSettings settings, MeshAttachmentType attachmentType,
            bool allowOnlyOneRoot, bool dryRun,
            ref int dryRunPoseCount, ref int dryRunItemCount, ref int itemOffset, ref int poseOffset)
        {
            unsafe
            {
                fixed (int* attachmentIndex = &itemOffset)
                fixed (int* poseIndex = &poseOffset)
                fixed (SkinAttachmentPose* pose = posesArray)
                fixed (SkinAttachmentItem* items = itemsArray)
                {
                    switch (attachmentType)
                    {
                        case MeshAttachmentType.Mesh:
                            SkinAttachmentDataBuilder.BuildDataAttachMesh(pose, items, subjectToTarget, targetBakeData,
                                settings,
                                attachmentBakeData.meshBuffers.vertexPositions,
                                attachmentBakeData.meshBuffers.vertexNormals,
                                attachmentBakeData.meshBuffers.vertexTangents,
                                dryRun, ref dryRunPoseCount, ref dryRunItemCount, attachmentIndex,
                                poseIndex);
                            break;
                        case MeshAttachmentType.MeshRoots:
                            SkinAttachmentDataBuilder.BuildDataAttachMeshRoots(pose, items, subjectToTarget,
                                targetBakeData, settings,
                                allowOnlyOneRoot, attachmentBakeData.meshIslands, attachmentBakeData.meshAdjacency,
                                attachmentBakeData.meshBuffers.vertexPositions,
                                attachmentBakeData.meshBuffers.vertexNormals,
                                attachmentBakeData.meshBuffers.vertexTangents,
                                dryRun, ref dryRunPoseCount, ref dryRunItemCount, attachmentIndex,
                                poseIndex);
                            break;
                    }
                }
            }
        }

        bool BakeAttachmentPoses(ref SkinAttachmentItem[] items, ref SkinAttachmentPose[] poses)
        {
            if (!GetBakeData(out BakeData attachmentBakeData))
                return false;
            if (!SkinAttachmentSystem.Inst.GetAttachmentTargetMeshInfo(attachmentTarget,
                    out MeshInfo attachmentTargetBakeData))
                return false;

            Matrix4x4 subjectToTarget;
            if (skinningBone != null)
            {
                subjectToTarget = attachmentTarget.transform.worldToLocalMatrix * skinningBone.localToWorldMatrix *
                                  skinningBoneBindPose;
            }
            else
            {
                subjectToTarget = attachmentTarget.transform.worldToLocalMatrix * transform.localToWorldMatrix;
            }

            //for now deactive this path as it's not yet stable
            PoseBuildSettings poseBuildParams = new PoseBuildSettings
            {
                onlyAllowPoseTrianglesContainingAttachedPoint = false
            };

            // pass 1: dry run
            int dryRunPoseCount = 0;
            int dryRunItemCount = 0;

            int poseOffsetDummy = 0;
            int itemOffsetDummy = 0;

            BuildDataAttachSubject(poses, items, attachmentBakeData, subjectToTarget,
                attachmentTargetBakeData, poseBuildParams,
                attachmentType, allowOnlyOneRoot, true, ref dryRunPoseCount, ref dryRunItemCount, ref itemOffsetDummy,
                ref poseOffsetDummy);

            //int dryRunPoseCountNextPowerOfTwo = Mathf.NextPowerOfTwo(dryRunPoseCount);
            //int dryRunItemCountNextPowerOfTwo = Mathf.NextPowerOfTwo(dryRunItemCount);

            ArrayUtils.ResizeCheckedIfLessThan(ref poses, dryRunPoseCount);
            ArrayUtils.ResizeCheckedIfLessThan(ref items, dryRunItemCount);

            int currentPoseOffset = 0;
            int currentItemOffset = 0;

            BuildDataAttachSubject(poses, items, attachmentBakeData, subjectToTarget,
                attachmentTargetBakeData, poseBuildParams,
                attachmentType, allowOnlyOneRoot, false, ref dryRunPoseCount, ref dryRunItemCount,
                ref currentItemOffset, ref currentPoseOffset);


            GPUDataValid = false;
            return true;
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
            meshAssetRadius = meshAsset.bounds.extents.magnitude; // conservative
            DiscoverSkinningBone();

            //if we need to generate precalculated movecs, we need float3 texcoord5 
            List<Tuple<VertexAttribute, int>> neededAttributes = new List<Tuple<VertexAttribute, int>>();
            neededAttributes.Add(Tuple.Create(VertexAttribute.Position, 0));
            neededAttributes.Add(Tuple.Create(VertexAttribute.Normal, 0));

            if (meshInstance.HasVertexAttribute(VertexAttribute.Tangent))
            {
                neededAttributes.Add(Tuple.Create(VertexAttribute.Tangent, 0));
            }

            if (GeneratePrecalculatedMotionVectors)
            {
                VertexAttributeDescriptor[] attributes = meshInstance.GetVertexAttributes();
                bool containsPrecalcMovecs = false;
                foreach (var attr in attributes)
                {
                    if (attr.attribute == VertexAttribute.TexCoord5)
                    {
                        containsPrecalcMovecs = true;
                        break;
                    }
                }

                if (!containsPrecalcMovecs)
                {
                    VertexAttributeDescriptor[] newAttributes = new VertexAttributeDescriptor[attributes.Length + 1];
                    attributes.CopyTo(newAttributes, 0);
                    newAttributes[newAttributes.Length - 1] = new VertexAttributeDescriptor(VertexAttribute.TexCoord5,
                        VertexAttributeFormat.Float32, 3, 1);
                    meshInstance.SetVertexBufferParams(meshInstance.vertexCount, newAttributes);
                }

                neededAttributes.Add(Tuple.Create(VertexAttribute.TexCoord5, 1));
                meshInstance.ChangeVertexStreamLayout(neededAttributes.ToArray(), 2);
            }
            else
            {
                meshInstance.ChangeVertexStreamLayout(neededAttributes.ToArray(), 1);
            }


            meshInstance.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
        }

        private bool GetBakeData(out BakeData data)
        {
            data = new BakeData();

            Mesh m = meshAsset != null ? meshAsset : meshInstance;

            if (m != null)
            {
                if (data.meshBuffers == null)
                    data.meshBuffers = new MeshBuffers(m);
                else
                    data.meshBuffers.LoadFrom(m);


                //only need island and adjacency info with meshroots
                if (attachmentType == MeshAttachmentType.MeshRoots)
                {
                    if (data.meshAdjacency == null)
                        data.meshAdjacency = new MeshAdjacency(data.meshBuffers);
                    else
                        data.meshAdjacency.LoadFrom(data.meshBuffers);

                    if (data.meshIslands == null)
                        data.meshIslands = new MeshIslands(data.meshAdjacency);
                    else
                        data.meshIslands.LoadFrom(data.meshAdjacency);
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        private void ReleaseGPUResources()
        {
            if (bakedAttachmentPosesGPU != null)
            {
                bakedAttachmentPosesGPU.Release();
                bakedAttachmentPosesGPU = null;
            }
            
            if (bakedAttachmentItemsGPU != null)
            {
                bakedAttachmentItemsGPU.Release();
                bakedAttachmentItemsGPU = null;
            }
            
        }
        
        private bool GetPoseData(out GraphicsBuffer itemsBuffer, out GraphicsBuffer posesBuffer, out int itemOffset,
            out int itemCount)
        {
            if (!ValidateBakedData())
            {
                itemsBuffer = null;
                posesBuffer = null;
                itemOffset = 0;
                itemCount = 0;
                return false;
            }

            if (!GPUDataValid)
            {
                SkinAttachmentSystem.UploadAttachmentPoseDataToGPU(bakedItems, bakedPoses, 0, bakedItems.Length, 0, bakedPoses.Length, ref bakedAttachmentItemsGPU, ref bakedAttachmentPosesGPU);
                GPUDataValid = true;
            }

            itemsBuffer = bakedAttachmentItemsGPU;
            posesBuffer = bakedAttachmentPosesGPU;
            itemOffset = 0;
            itemCount = bakedItems.Length;

            return true;
        }

        private bool GetPoseData(out SkinAttachmentItem[] itemsBuffer, out SkinAttachmentPose[] posesBuffer,
            out int itemOffset,
            out int itemCount)
        {
            itemsBuffer = bakedItems;
            posesBuffer = bakedPoses;
            itemOffset = 0;
            itemCount = bakedItems?.Length ?? 0;

            if (itemCount == 0) return false;

            return true;
        }

        private Matrix4x4 GetTargetToAttachmentTransform()
        {
            Matrix4x4 targetToWorld;
            if (currentTarget is SkinnedMeshRenderer)
            {
                targetToWorld = currentTarget.transform.parent.localToWorldMatrix * Matrix4x4.TRS(
                    currentTarget.transform.localPosition,
                    currentTarget.transform.localRotation, Vector3.one);
            }
            else
            {
                targetToWorld = currentTarget.transform.localToWorldMatrix;
            }

            Matrix4x4 targetToSubject;
            if (skinningBone != null)
            {
                targetToSubject = (skinningBoneBindPoseInverse * skinningBone.worldToLocalMatrix) * targetToWorld;
            }
            else
            {
                targetToSubject = transform.worldToLocalMatrix * targetToWorld;
            }

            return targetToSubject;
        }

        public bool FillSkinAttachmentDesc(ref SkinAttachmentSystem.SkinAttachmentDescGPU desc)
        {
            if (GetPoseData(out GraphicsBuffer itemsBuffer, out GraphicsBuffer posesBuffer, out int itemOffset,
                    out int itemsCount))
            {
                //calculate resolve matrix
                return SkinAttachmentSystem.FillSkinAttachmentDesc(meshInstance, GetTargetToAttachmentTransform(),
                    posesBuffer, itemsBuffer, itemOffset, itemsCount, ref desc);
            }

            return false;
        }

        public bool FillSkinAttachmentDesc(ref SkinAttachmentSystem.SkinAttachmentDescCPU desc)
        {
            if (!meshInstance) return false;

            if (GetPoseData(out SkinAttachmentItem[] itemsBuffer, out SkinAttachmentPose[] posesBuffer,
                    out int itemOffset,
                    out int itemsCount))
            {
                if (itemsCount != meshInstance.vertexCount) return false;

                int vertexCount = meshInstance.vertexCount;
                bool resolveTangents = meshInstance.HasVertexAttribute(VertexAttribute.Tangent);

                ArrayUtils.ResizeChecked(ref stagingPositions, vertexCount);
                ArrayUtils.ResizeChecked(ref stagingNormals, vertexCount);
                if (resolveTangents)
                {
                    ArrayUtils.ResizeChecked(ref stagingTangents, vertexCount);
                }


                desc.skinAttachmentItems = itemsBuffer;
                desc.skinAttachmentPoses = posesBuffer;
                desc.resolvedPositions = stagingPositions;
                desc.resolvedNormals = stagingNormals;
                desc.resolvedTangents = resolveTangents ? stagingTangents : null;
                desc.resolveTransform = GetTargetToAttachmentTransform();
                desc.itemsOffset = itemOffset;
                desc.itemsCount = itemsCount;
                return true;
            }

            return false;
        }
    }
}