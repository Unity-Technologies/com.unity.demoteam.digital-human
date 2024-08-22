using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.DemoTeam.DigitalHuman
{
    using SkinAttachmentItem = SkinAttachmentItem3;
    using BakeData = SkinAttachmentComponentCommon.BakeData;
    [ExecuteAlways]
    public class SkinAttachmentMesh : MeshInstanceBehaviour, ISkinAttachment, SkinAttachmentComponentCommon.ISkinAttachmentComponent
    {
        public enum MeshAttachmentType
        {
            Mesh,
            MeshRoots,
        }

        public SkinAttachmentComponentCommon common = new SkinAttachmentComponentCommon();
        public MeshAttachmentType attachmentType = MeshAttachmentType.Mesh;
        public bool allowOnlyOneRoot = false;
        public bool generatePrecalculatedMotionVectors = false;
        
        public event Action onSkinAttachmentMeshResolved;
        
        public bool IsAttached => common.attached;
        public bool ScheduleExplicitly 
        {
            get => common.explicitScheduling;
            set => common.explicitScheduling = value;
        }

        public Renderer Target
        {
            get => common.attachmentTarget;
            set => common.attachmentTarget = value;
        }

        public Mesh ExplicitTargetBakeMesh
        {
            get => common.explicitBakeMesh;
            set => common.explicitBakeMesh = value;
        }

        public SkinAttachmentDataRegistry DataStorage
        {
            get => common.dataStorage;
            set => common.dataStorage = value;
        }

        public SkinAttachmentComponentCommon.SchedulingMode SchedulingMode
        {
            get => common.schedulingMode;
            set => common.schedulingMode = value;
        }


        private float meshAssetRadius;
        private Transform skinningBone;
        private Matrix4x4 skinningBoneBindPose;
        private Matrix4x4 skinningBoneBindPoseInverse;

        private Vector3[] stagingPositions;
        private Vector3[] stagingNormals;
        private Vector4[] stagingTangents;
        
        private GraphicsBuffer bakedAttachmentPosesGPU;
        private GraphicsBuffer bakedAttachmentItemsGPU;
        
        private bool GPUDataValid = false;

        public void Attach(bool storePositionRotation = true)
        {
            common.Attach(this, storePositionRotation);
            UpdateAttachedState();
        }

        public void Detach(bool revertPositionRotation = true)
        {
            RemoveMeshInstance();

            common.Detach(this, revertPositionRotation);
        }

        public void QueueForResolve()
        {
            if (!common.explicitScheduling)
            {
                Debug.LogErrorFormat("Tried to call QueueForResolve for SkinAttachmentMesh {0} but explicit scheduling not enabled. Skipping", name);
                return;
            }
            UpdateAttachedState();
            if (common.hasValidState)
            {
                SkinAttachmentSystem.Inst.QueueExplicitAttachmentResolve(this, common.schedulingMode == SkinAttachmentComponentCommon.SchedulingMode.GPU);
            }
        }

        void OnEnable()
        {
            EnsureMeshInstance();
            GPUDataValid = false;
            common.LoadBakedData();
            UpdateAttachedState();
        }

        private void OnDisable()
        {
            ReleaseGPUResources();
        }

        void LateUpdate()
        {
            UpdateAttachedState();
            if (common.hasValidState && !common.explicitScheduling)
            {
                SkinAttachmentSystem.Inst.QueueAttachmentResolve(this, common.schedulingMode == SkinAttachmentComponentCommon.SchedulingMode.GPU);
            }
        }


        void OnDestroy()
        {
            RemoveMeshInstance();
        }

        public Renderer GetTargetRenderer()
        {
            return common.currentTarget;
        }

        public void NotifyAttachmentResolved(CommandBuffer cmd)
        {
            //update mesh bounds
            {
                //update mesh bounds
                var targetMeshWorldBounds = common.currentTarget.bounds;
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
        }

        public void NotifyAllAttachmentsFromQueueResolved()
        {
            if (onSkinAttachmentMeshResolved != null)
                onSkinAttachmentMeshResolved();
        }

        public bool ValidateBakedData()
        {
            bool validData = common.ValidateBakedData();

            if (validData && meshInstance != null)
            {
                bool meshInstanceVertexCountMatches = common.bakedItems.Length == meshInstance.vertexCount;
                return meshInstanceVertexCountMatches;
            }

            return false;
        }

        public bool CanAttach()
        {
            return common.IsAttachmentTargetValid() && IsAttachmentMeshValid()  && !IsAttached;
        }

        public bool IsAttachmentMeshValid()
        {
            if (meshAsset != null)
            {
                if (meshAsset.isReadable)
                {
                    return true;
                }
                else
                {
                    Debug.LogError($"SkinAttachmentMesh {meshAsset.name} is not readable, attachment logic cannot run. Make the mesh readable from import settings");
                }
            }

            return false;
        }

        void UpdateAttachedState()
        {
            common.UpdateAttachedState(this);
            
            if (IsAttached)
            {
                common.hasValidState = common.hasValidState && IsAttachmentMeshValid();
                
                if (!common.hasValidState)
                {
                    return;
                }

                
                //if we need to generate motion vectors and the meshInstance doesn't have proper streams setup, reset the meshinstance (so it gets recreated)
                if (generatePrecalculatedMotionVectors && meshInstance &&
                    !meshInstance.HasVertexAttribute(VertexAttribute.TexCoord5))
                {
                    RemoveMeshInstance();
                }

                EnsureMeshInstance();
            }
            else
            {
                RemoveMeshInstance();
            }
        }
        
        static void BuildDataAttachSubject(ref SkinAttachmentPose[] posesArray, ref SkinAttachmentItem[] itemsArray,
            in BakeData attachmentBakeData, Matrix4x4 subjectToTarget,
            in MeshInfo targetBakeData, in PoseBuildSettings settings, MeshAttachmentType attachmentType,
            bool allowOnlyOneRoot,  ref int itemOffset, ref int poseOffset)
        {
            
            int poseCount = 0;
            int itemCount = 0;

            switch (attachmentType)
            {
                case MeshAttachmentType.Mesh:
                    SkinAttachmentDataBuilder.BuildDataAttachMesh(ref posesArray, ref itemsArray, subjectToTarget, targetBakeData,
                        settings,
                        attachmentBakeData.meshBuffers.vertexPositions,
                        attachmentBakeData.meshBuffers.vertexNormals,
                        attachmentBakeData.meshBuffers.vertexTangents, 
                        itemOffset,
                        poseOffset,
                        out itemCount,
                        out poseCount);
                    break;
                case MeshAttachmentType.MeshRoots:
                    SkinAttachmentDataBuilder.BuildDataAttachMeshRoots(ref posesArray, ref itemsArray, subjectToTarget,
                        targetBakeData, settings,
                        allowOnlyOneRoot, attachmentBakeData.meshIslands, attachmentBakeData.meshAdjacency,
                        attachmentBakeData.meshBuffers.vertexPositions,
                        attachmentBakeData.meshBuffers.vertexNormals,
                        attachmentBakeData.meshBuffers.vertexTangents,
                        itemOffset,
                        poseOffset,
                        out itemCount,
                        out poseCount);
                    break;
            }
            

            itemOffset += itemCount;
            poseOffset += poseCount;

        }

        bool BakeAttachmentPoses(SkinAttachmentComponentCommon.PoseBakeOutput bakeOutput)
        {
            return BakeAttachmentPoses(ref bakeOutput.items, ref bakeOutput.poses);
        }

        bool BakeAttachmentPoses(ref SkinAttachmentItem[] items, ref SkinAttachmentPose[] poses)
        {
            if (!GetBakeData(out BakeData attachmentBakeData))
                return false;
            if (!SkinAttachmentSystem.Inst.GetAttachmentTargetMeshInfo(common.attachmentTarget,
                    out MeshInfo attachmentTargetBakeData, common.readbackTargetMeshWhenBaking, common.explicitBakeMesh))
                return false;

            Matrix4x4 subjectToTarget;
            if (skinningBone != null)
            {
                subjectToTarget = common.attachmentTarget.transform.worldToLocalMatrix * skinningBone.localToWorldMatrix *
                                  skinningBoneBindPose;
            }
            else
            {
                subjectToTarget = common.attachmentTarget.transform.worldToLocalMatrix * transform.localToWorldMatrix;
            }

            //for now deactive this path as it's not yet stable
            PoseBuildSettings poseBuildParams = new PoseBuildSettings
            {
                onlyAllowPoseTrianglesContainingAttachedPoint = false
            };
            
            int currentPoseOffset = 0;
            int currentItemOffset = 0;

            BuildDataAttachSubject(ref poses, ref items, attachmentBakeData, subjectToTarget,
                attachmentTargetBakeData, poseBuildParams,
                attachmentType, allowOnlyOneRoot,
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

                if (skinningBoneIndex != -1 && skinningBoneIndex < smr.bones.Length)
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

            if (generatePrecalculatedMotionVectors)
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

            GPUDataValid = false;
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
                SkinAttachmentSystem.UploadAttachmentPoseDataToGPU(common.bakedItems, common.bakedPoses, common.bakedItems.Length, common.bakedPoses.Length, ref bakedAttachmentItemsGPU, ref bakedAttachmentPosesGPU);
                GPUDataValid = true;
            }

            itemsBuffer = bakedAttachmentItemsGPU;
            posesBuffer = bakedAttachmentPosesGPU;
            itemOffset = 0;
            itemCount = common.bakedItems.Length;

            return true;
        }

        private bool GetPoseData(out SkinAttachmentItem[] itemsBuffer, out SkinAttachmentPose[] posesBuffer,
            out int itemOffset,
            out int itemCount)
        {
            itemsBuffer = common.bakedItems;
            posesBuffer = common.bakedPoses;
            itemOffset = 0;
            itemCount = common.bakedItems?.Length ?? 0;

            if (itemCount == 0) return false;

            return true;
        }

        private Matrix4x4 GetTargetToAttachmentTransform()
        {
            Matrix4x4 targetToWorld;
            if (common.currentTarget is SkinnedMeshRenderer)
            {
                var tr = common.currentTarget.transform;
                targetToWorld = tr.parent.localToWorldMatrix * Matrix4x4.TRS(
                    tr.localPosition,
                    tr.localRotation, Vector3.one);
            }
            else
            {
                targetToWorld = common.currentTarget.transform.localToWorldMatrix;
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
                    posesBuffer, itemsBuffer, itemOffset, itemsCount, true, ref desc);
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

        public SkinAttachmentComponentCommon GetCommonComponent()
        {
            return common;
        }

        public bool BakeAttachmentData(SkinAttachmentComponentCommon.PoseBakeOutput output)
        {
            return BakeAttachmentPoses(output);
        }

        public void RevertPropertyOverrides()
        {
            #if UNITY_EDITOR
            var serializedObject = new UnityEditor.SerializedObject(this);
            {
                var checksumProperty = serializedObject.FindProperty(nameof(common.checkSum));
                PrefabUtility.RevertPropertyOverride(checksumProperty, UnityEditor.InteractionMode.AutomatedAction);
            }

            serializedObject.ApplyModifiedProperties();
            #endif
        }
        
#if UNITY_EDITOR
        public void OnDrawGizmosSelected()
        {
            common.DrawDebug(this);
        }
#endif
        
    }
}