using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Unity.DemoTeam.DigitalHuman
{
    using static SkinAttachmentDataBuilder;
    using SkinAttachmentItem = Unity.DemoTeam.DigitalHuman.SkinAttachmentItem3;
    [ExecuteAlways]
    public class LegacySkinAttachmentTarget : MonoBehaviour
    {
        [HideInInspector] public List<LegacySkinAttachment> subjects = new List<LegacySkinAttachment>();

        [NonSerialized] public Mesh meshBakedSmr;
        [NonSerialized] public Mesh meshBakedOrAsset;
        [NonSerialized] public MeshBuffers meshBuffers;
        [NonSerialized] public Mesh meshBuffersLastAsset;

        public LegacySkinAttachmentData attachData;

        [Header("Debug options")] public bool showWireframe = false;
        public bool showUVSeams = false;
        public bool showResolved = false;
        public bool showMouseOver = false;

        public event Action afterGPUAttachmentWorkCommitted;
        public bool IsAfterGPUResolveFenceValid => afterGPUResolveFenceValid;
        public GraphicsFence AfterGPUResolveFence => afterGPUResolveFence;

        public bool ExecuteSkinAttachmentResolveAutomatically { get; set; } =
            true; //if set to false, external logic must drive the resolve tick

#if UNITY_2021_2_OR_NEWER
        [VisibleIfAttribute("executeOnGPU", true)]
        public bool readbackTransformPositions = false;

        [Header("Execution")] public bool executeOnGPU = false;

        public GraphicsBuffer TransformAttachmentGPUPositionBuffer => transformAttachmentPosBuffer;
        public int TransformAttachmentGPUPositionBufferStride => transformAttachmentBufferStride;


#else
        public readonly bool executeOnGPU = false;

#endif
        private bool UseCPUExecution => !executeOnGPU;

        private MeshInfo cachedMeshInfo;
        private int cachedMeshInfoFrame = -1;
        
        private Vector3[][] stagingDataVec3;
        private Vector4[][] stagingDataVec4;


        private bool subjectsNeedRefresh = false;

        private bool afterGPUResolveFenceValid;
        private GraphicsFence afterGPUResolveFence;
        private bool afterResolveFenceRequested = false;
        private bool transformGPUPositionsReadBack = false;

#if UNITY_2021_2_OR_NEWER
        
        private GraphicsBuffer attachmentPosesBuffer;
        private GraphicsBuffer attachmentItemsBuffer;
        private GraphicsBuffer transformAttachmentPosBuffer;
        private GraphicsBuffer transformAttachmentItemBuffer;
        private int transformAttachmentCount = 0;
        private bool gpuResourcesAllocated = false;
        const int transformAttachmentBufferStride = 3 * sizeof(float); //float3, position

#endif
        void OnEnable()
        {
            UpdateMeshBuffers();
            subjectsNeedRefresh = true;

            afterGPUResolveFenceValid = false;
#if UNITY_2021_2_OR_NEWER
            var smr = GetComponent<SkinnedMeshRenderer>();
            if (smr != null)
            {
                smr.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            }

            var mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                mf.sharedMesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            }

            RenderPipelineManager.beginFrameRendering -= AfterGpuSkinningCallback;
            RenderPipelineManager.beginFrameRendering += AfterGpuSkinningCallback;

            RenderPipelineManager.endFrameRendering -= AfterFrameDone;
            RenderPipelineManager.endFrameRendering += AfterFrameDone;
#endif
        }

        private void OnDisable()
        {
#if UNITY_2021_2_OR_NEWER
            DestroyGPUResources();
            RenderPipelineManager.beginFrameRendering -= AfterGpuSkinningCallback;
            RenderPipelineManager.endFrameRendering -= AfterFrameDone;
#endif
        }

        void LateUpdate()
        {
            if (ExecuteSkinAttachmentResolveAutomatically && UseCPUExecution)
            {
                ResolveSubjects();
            }
        }

        bool PrepareSubjectsResolve()
        {
            //if meshbuffers are null, retry creating them (could be that the mesh was not yet created on OnEnable)
            if (meshBuffers == null)
            {
                UpdateMeshBuffers();
            }

            if (attachData == null || meshBuffers == null)
                return false;

            if (attachData.driverVertexCount > meshBuffers.vertexCount)
                return false; // prevent out of bounds if mesh shrunk since data was built

#if UNITY_2021_2_OR_NEWER
            if (gpuResourcesAllocated != executeOnGPU)
            {
                subjectsNeedRefresh = true;
            }
#endif

            int removed = subjects.RemoveAll(p => p == null);
            bool subjectsChanged = removed > 0 || subjectsNeedRefresh;
            subjectsNeedRefresh = false;
            bool success = true;
#if UNITY_2021_2_OR_NEWER
            if (subjectsChanged)
            {
                if (executeOnGPU)
                {
                    success = CreateGPUResources();
                }
                else
                {
                    DestroyGPUResources();
                }
            }
#endif
            return success;
        }

        void ResolveSubjects()
        {
            if (!PrepareSubjectsResolve()) return;
            if (UseCPUExecution)
            {
                if (UpdateMeshBuffers(true))
                {
                    ResolveSubjectsCPU();
                }
            }
#if UNITY_2021_2_OR_NEWER
            else
            {
                ResolveSubjectsGPU();
				if (executeOnGPU && !transformGPUPositionsReadBack)
				
				//readback transform positions to CPU for debugging
				if (readbackTransformPositions && transformAttachmentPosBuffer != null)
				{
					NativeArray<Vector3> readBackBuffer = new NativeArray<Vector3>(
						transformAttachmentPosBuffer.count,
						Allocator.Persistent);
	
					var readbackRequest =
						AsyncGPUReadback.RequestIntoNativeArray(ref readBackBuffer, transformAttachmentPosBuffer);
					readbackRequest.WaitForCompletion();
	
					for (int i = 0; i < subjects.Count; ++i)
					{
						if (subjects[i].attachmentType != LegacySkinAttachment.AttachmentType.Transform) continue;
						int index = subjects[i].TransformAttachmentGPUBufferIndex;
						Vector3 pos = readBackBuffer[index];
						subjects[i].transform.position = pos;
					}
	
					readBackBuffer.Dispose();
					transformGPUPositionsReadBack = true;
				}
				
				
				
                afterGPUAttachmentWorkCommitted?.Invoke();
            }
#endif
        }


        void ResolveSubjectsCPU()
        {
            Profiler.BeginSample("resolve-subj-all-cpu");

            ArrayUtils.ResizeChecked(ref stagingDataVec3, subjects.Count * 2);
            ArrayUtils.ResizeChecked(ref stagingDataVec4, subjects.Count);
            MeshBuffers mb = meshBuffers;

            // NOTE: for skinned targets, targetToWorld specifically excludes scale, since source data (BakeMesh) is already scaled
            Matrix4x4 targetToWorld;
            {
                if (this.meshBakedSmr != null)
                    targetToWorld = Matrix4x4.TRS(this.transform.position, this.transform.rotation, Vector3.one);
                else
                    targetToWorld = this.transform.localToWorldMatrix;
            }

            var targetMeshWorldBounds = meshBakedOrAsset.bounds;
            var targetMeshWorldBoundsCenter = targetMeshWorldBounds.center;
            var targetMeshWorldBoundsExtent = targetMeshWorldBounds.extents;

            SkinAttachmentSystem.SkinAttachmentTargetDescCPU attachmentTargetDesc;
            attachmentTargetDesc.positions = mb.vertexPositions;
            attachmentTargetDesc.normals = mb.vertexNormals;
            attachmentTargetDesc.tangents = mb.vertexTangents;
            
            List<SkinAttachmentSystem.SkinAttachmentDescCPU> attachmentDescs =
                new List<SkinAttachmentSystem.SkinAttachmentDescCPU>();
            List<LegacySkinAttachment> resolvedAttachments = new List<LegacySkinAttachment>();
            int resolvedSubjectIndex = 0;
            for (int i = 0; i < subjects.Count; i++)
            {
                var subject = subjects[i];
                if (subject.ChecksumCompare(attachData) == false)
                    continue;

                int attachmentIndex = subject.attachmentIndex;
                int attachmentCount = subject.attachmentCount;
                if (attachmentIndex == -1)
                    continue;

                if (attachmentIndex + attachmentCount > attachData.itemCount)
                    continue; // prevent out of bounds if subject holds damaged index/count 

                if (!subject.gameObject.activeInHierarchy)
                    continue;

                Matrix4x4 resolveTransform = targetToWorld;
                if (subject.attachmentType != LegacySkinAttachment.AttachmentType.Transform)
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
                        resolveTransform =
                            (subject.skinningBoneBindPoseInverse *
                             subject.skinningBone.worldToLocalMatrix) * targetToWorld;
                    else
                        resolveTransform = subject.transform.worldToLocalMatrix * targetToWorld;
                }

                bool resolveTangents = subject.meshInstance &&
                                       subject.meshInstance.HasVertexAttribute(VertexAttribute.Tangent);
                var indexPosStaging = resolvedSubjectIndex * 2 + 0;
                var indexNrmStaging = resolvedSubjectIndex * 2 + 1;
                var indexTanStaging = resolvedSubjectIndex;
                ArrayUtils.ResizeChecked(ref stagingDataVec3[indexPosStaging], attachmentCount);
                ArrayUtils.ResizeChecked(ref stagingDataVec3[indexNrmStaging], attachmentCount);
                if (resolveTangents)
                {
                    ArrayUtils.ResizeChecked(ref stagingDataVec4[indexTanStaging], attachmentCount);
                }

                SkinAttachmentSystem.SkinAttachmentDescCPU attachmentDesc;
                attachmentDesc.skinAttachmentItems = attachData.ItemData;
                attachmentDesc.skinAttachmentPoses = attachData.pose;
                attachmentDesc.resolvedPositions = stagingDataVec3[indexPosStaging];
                attachmentDesc.resolvedNormals = stagingDataVec3[indexNrmStaging];
                attachmentDesc.resolvedTangents = resolveTangents ? stagingDataVec4[indexTanStaging] : null;
                attachmentDesc.resolveTransform = resolveTransform;
                attachmentDesc.itemsOffset = subject.attachmentIndex;
                attachmentDesc.itemsCount = subject.attachmentCount;

                attachmentDescs.Add(attachmentDesc);
                resolvedAttachments.Add(subject);
                ++resolvedSubjectIndex;
            }

            SkinAttachmentSystem.SkinAttachmentDescCPU[] attachmentDescsArray = attachmentDescs.ToArray();
            SkinAttachmentSystem.ResolveSubjectsCPU(ref attachmentTargetDesc, attachmentDescsArray);

            for (int i = 0; i < attachmentDescs.Count; ++i)
            {
                ref SkinAttachmentSystem.SkinAttachmentDescCPU attachmentDesc = ref attachmentDescsArray[i];
                LegacySkinAttachment subject = resolvedAttachments[i];
                
                bool resolveTangents = subject.meshInstance &&
                                       subject.meshInstance.HasVertexAttribute(VertexAttribute.Tangent);
                var indexPosStaging = i * 2 + 0;
                var indexNrmStaging = i * 2 + 1;
                var indexTanStaging = i;
                
                Profiler.BeginSample("gather-subj");
                switch (subject.attachmentType)
                {
                    case LegacySkinAttachment.AttachmentType.Transform:
                    {
                        subject.transform.position = stagingDataVec3[indexPosStaging][0];
                    } 
                        break;

                    case LegacySkinAttachment.AttachmentType.Mesh:
                    case LegacySkinAttachment.AttachmentType.MeshRoots:
                    {
                        if (subject.meshInstance == null)
                            break;

                        if (subject.meshInstance.vertexCount != stagingDataVec3[indexPosStaging].Length)
                        {
                            Debug.LogError("mismatching vertex- and attachment count", subject);
                            break;
                        }

                        subject.meshInstance.SilentlySetVertices(stagingDataVec3[indexPosStaging]);
                        subject.meshInstance.SilentlySetNormals(stagingDataVec3[indexNrmStaging]);

                        if (resolveTangents)
                        {
                            subject.meshInstance.SilentlySetTangents(stagingDataVec4[indexTanStaging]);
                        }

                        Profiler.BeginSample("conservative-bounds");
                        {
                            //Debug.Log("targetMeshWorldBoundsCenter = " + targetMeshWorldBoundsCenter.ToString("G4") + " (from meshBakedOrAsset = " + meshBakedOrAsset.ToString() + ")");
                            //Debug.Log("targetMeshWorldBoundsExtents = " + targetMeshWorldBoundsExtents.ToString("G4"));
                            var worldToSubject = subject.transform.worldToLocalMatrix;
                            var subjectBoundsCenter = worldToSubject.MultiplyPoint(targetMeshWorldBoundsCenter);
                            var subjectBoundsRadius =
                                worldToSubject.MultiplyVector(targetMeshWorldBoundsExtent).magnitude +
                                subject.meshAssetRadius;
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
            
            Profiler.EndSample();
        }

        bool UpdateMeshBuffers(bool forceMeshBakeCPU = false)
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
                        meshBakedSmr = Instantiate(smr.sharedMesh);
                        meshBakedSmr.name = "SkinAttachmentTarget(BakeMesh)";
                        meshBakedSmr.hideFlags = HideFlags.HideAndDontSave & ~HideFlags.DontUnloadUnusedAsset;
                        meshBakedSmr.MarkDynamic();
                    }

                    meshBakedOrAsset = meshBakedSmr;
                    if (forceMeshBakeCPU)
                    {
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
            }

            if (meshBakedOrAsset == null)
                return false;

            if (meshBuffers == null || meshBuffersLastAsset != meshBakedOrAsset)
            {
                meshBuffers = new MeshBuffers(meshBakedOrAsset);
            }
            else
            {
                meshBuffers.LoadFrom(meshBakedOrAsset);
            }

            meshBuffersLastAsset = meshBakedOrAsset;
            return true;
        }

        void UpdateMeshInfo(ref MeshInfo info)
        {
            Profiler.BeginSample("upd-mesh-inf");
            UpdateMeshBuffers(true);
            if (meshBuffers == null)
            {
                info.valid = false;
            }
            else
            {
                info.meshBuffers = meshBuffers;

                const bool weldedAdjacency = false; //TODO enable for more reliable poses along uv seams

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

        public void RequestGPUFenceAfterResolve()
        {
            afterResolveFenceRequested = true;
        }

        public void Resolve()
        {
            if (ExecuteSkinAttachmentResolveAutomatically)
            {
                Debug.LogError(
                    "Trying to call explicit Resolve but ExecuteSkinAttachmentResolveAutomatically is true. Ignoring...");
                return;
            }

            ResolveSubjects();
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

        public void AddSubject(LegacySkinAttachment subject)
        {
            if (subjects.Contains(subject) == false)
                subjects.Add(subject);

            subjectsNeedRefresh = true;
        }

        public void RemoveSubject(LegacySkinAttachment subject)
        {
            if (subjects.Contains(subject))
                subjects.Remove(subject);

            subjectsNeedRefresh = true;
        }

        public bool CommitRequired()
        {
            if (attachData == null || meshBuffers == null)
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
        
        
        public static void BuildDataAttachSubject(ref LegacySkinAttachmentData attachData, Transform target,
            in MeshInfo meshInfo, in PoseBuildSettings settings, LegacySkinAttachment subject, ref int itemOffset, ref int poseOffset)
        {
            Matrix4x4 subjectToTarget;
            {
                if (subject.skinningBone != null)
                    subjectToTarget = target.transform.worldToLocalMatrix *
                                      (subject.skinningBone.localToWorldMatrix * subject.skinningBoneBindPose);
                else
                    subjectToTarget = target.transform.worldToLocalMatrix * subject.transform.localToWorldMatrix;
            }

            ref SkinAttachmentPose[] pose = ref attachData.pose;
            ref SkinAttachmentItem[] items =  ref attachData.ItemDataRef;

            int itemCount = 0;
            int poseCount = 0;

            switch (subject.attachmentType)
            {
                case LegacySkinAttachment.AttachmentType.Transform:
                    BuildDataAttachTransform(ref pose, ref items, subjectToTarget, meshInfo, settings,
                        itemOffset, poseOffset, out itemCount, out poseCount);
                    break;
                case LegacySkinAttachment.AttachmentType.Mesh:
                    BuildDataAttachMesh(ref pose, ref items, subjectToTarget, meshInfo, settings,
                        subject.meshBuffers.vertexPositions, subject.meshBuffers.vertexNormals,
                        subject.meshBuffers.vertexTangents, itemOffset, poseOffset, out itemCount,out poseCount);
                    break;
                case LegacySkinAttachment.AttachmentType.MeshRoots:
                    BuildDataAttachMeshRoots(ref pose, ref items, subjectToTarget, meshInfo, settings,
                        subject.allowOnlyOneRoot, subject.meshIslands, subject.meshAdjacency,
                        subject.meshBuffers.vertexPositions, subject.meshBuffers.vertexNormals,
                        subject.meshBuffers.vertexTangents,
                        itemOffset, poseOffset, out itemCount,out poseCount);
                    break;
            }


            itemOffset += itemCount;
            poseOffset += poseCount;
        }

        
        public void CommitSubjects()
        {
            if (attachData == null)
                return;

            var meshInfo = GetCachedMeshInfo(true);
            if (meshInfo.valid == false)
                return;

            //for now deactive this path as it's not yet stable
            PoseBuildSettings poseBuildParams = new PoseBuildSettings
            {
                onlyAllowPoseTrianglesContainingAttachedPoint = false
            };

            attachData.Clear();
            attachData.driverVertexCount = meshInfo.meshBuffers.vertexCount;
            {
                subjects.RemoveAll(p => (p == null));

                // pass 1: dry run
                for (int i = 0, n = subjects.Count; i != n; i++)
                {
                    if (subjects[i].attachmentMode == LegacySkinAttachment.AttachmentMode.BuildPoses)
                    {
                        subjects[i].RevertVertexData();
                    }
                }
                
                int currentPoseOffset = 0;
                int currentItemOffset = 0;
                
                // pass 2: build poses
                for (int i = 0, n = subjects.Count; i != n; i++)
                {
                    if (subjects[i].attachmentMode == LegacySkinAttachment.AttachmentMode.BuildPoses)
                    {
                        int poseOffset = currentPoseOffset;
                        int itemOffset = currentItemOffset;
                        
                        
                        BuildDataAttachSubject(ref attachData, transform, meshInfo, poseBuildParams, subjects[i], ref currentItemOffset, ref currentPoseOffset);

                        int itemCount = currentItemOffset - itemOffset;
                        int poseCount = currentPoseOffset - poseOffset;

                        if (itemCount != 0)
                        {
                            subjects[i].attachmentIndex = itemOffset;
                            subjects[i].attachmentCount = itemCount;
                        }
                        else
                        {
                            subjects[i].attachmentIndex = -1;
                            subjects[i].attachmentCount = 0;
                        }
                    }
                }

                attachData.poseCount = currentPoseOffset;
                attachData.itemCount = currentItemOffset;
                
                // pass 3: reference poses
                for (int i = 0, n = subjects.Count; i != n; i++)
                {
                    switch (subjects[i].attachmentMode)
                    {
                        case LegacySkinAttachment.AttachmentMode.LinkPosesByReference:
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

                        case LegacySkinAttachment.AttachmentMode.LinkPosesBySpecificIndex:
                        {
                            subjects[i].attachmentIndex =
                                Mathf.Clamp(subjects[i].attachmentIndex, 0, attachData.itemCount - 1);
                            subjects[i].attachmentCount = Mathf.Clamp(subjects[i].attachmentCount, 0,
                                attachData.itemCount - subjects[i].attachmentIndex);
                        }
                            break;
                    }
                }
            }
            attachData.dataVersion = LegacySkinAttachmentData.DataVersion.Version_3;
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

            subjectsNeedRefresh = true;
        }

        #region GPUResolve

#if UNITY_2021_2_OR_NEWER
        
        void AfterFrameDone(ScriptableRenderContext scriptableRenderContext, Camera[] cameras)
        {
            afterResolveFenceRequested = false;
        }

        void AfterGpuSkinningCallback(ScriptableRenderContext scriptableRenderContext, Camera[] cameras)
        {
            if (!ExecuteSkinAttachmentResolveAutomatically)
                return;

            ResolveSubjects();
        }

        bool CreateGPUResources()
        {
            DestroyGPUResources();

            int itemStructSize = UnsafeUtility.SizeOf<SkinAttachmentSystem.SkinAttachmentItemGPU>();
            int poseStructSize = UnsafeUtility.SizeOf<SkinAttachmentSystem.SkinAttachmentPoseGPU>();

            int itemsCount = attachData.itemCount;
            int posesCount = attachData.poseCount;

            if (itemsCount == 0 || posesCount == 0) return false;

            attachmentPosesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, posesCount, poseStructSize);
            attachmentItemsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, itemsCount, itemStructSize);
    
            //upload stuff that doesn't change
            NativeArray<SkinAttachmentSystem.SkinAttachmentPoseGPU> posesBuffer =
                new NativeArray<SkinAttachmentSystem.SkinAttachmentPoseGPU>(posesCount, Allocator.Temp);
            for (int i = 0; i < posesCount; ++i)
            {
                SkinAttachmentSystem.SkinAttachmentPoseGPU poseGPU;
                poseGPU.targetCoord.x = attachData.pose[i].targetCoord.u;
                poseGPU.targetCoord.y = attachData.pose[i].targetCoord.v;
                poseGPU.targetCoord.z = attachData.pose[i].targetCoord.w;
                poseGPU.v0 = attachData.pose[i].v0;
                poseGPU.v1 = attachData.pose[i].v1;
                poseGPU.v2 = attachData.pose[i].v2;
                poseGPU.area = attachData.pose[i].area;
                poseGPU.targetDist = attachData.pose[i].targetDist;
                posesBuffer[i] = poseGPU;
            }

            attachmentPosesBuffer.SetData(posesBuffer);
            posesBuffer.Dispose();

            NativeArray<SkinAttachmentSystem.SkinAttachmentItemGPU> itemsBuffer =
                new NativeArray<SkinAttachmentSystem.SkinAttachmentItemGPU>(itemsCount, Allocator.Temp);
            for (int i = 0; i < itemsCount; ++i)
            {
                SkinAttachmentItem3 item = attachData.ItemData[i];
                
                SkinAttachmentSystem.SkinAttachmentItemGPU itemGPU;
                itemGPU.targetFrameDelta = new float4(item.targetFrameDelta[0], item.targetFrameDelta[1], item.targetFrameDelta[2], item.targetFrameDelta[3]);
                itemGPU.targetOffset = item.targetOffset;
                itemGPU.targetFrameW = item.targetFrameW;
                itemGPU.baseVertex = item.baseVertex;
                itemGPU.poseIndex = item.poseIndex;
                itemGPU.poseCount = item.poseCount;
                itemGPU.pad0 = 0;
                itemsBuffer[i] = itemGPU;
            }

            attachmentItemsBuffer.SetData(itemsBuffer);
            

            //buffers for resolving transform attachments on GPU
            transformAttachmentCount = 0;
            for (int i = 0; i < subjects.Count; ++i)
            {
                if (subjects[i].attachmentType == LegacySkinAttachment.AttachmentType.Transform)
                {
                    subjects[i].TransformAttachmentGPUBufferIndex = transformAttachmentCount;
                    ++transformAttachmentCount;
                }
            }

            //push transform attachment items to separate buffer so they can be resolved all at once
            if (transformAttachmentCount > 0)
            {
                {
                    NativeArray<SkinAttachmentSystem.SkinAttachmentItemGPU> transformItems =
                        new NativeArray<SkinAttachmentSystem.SkinAttachmentItemGPU>(transformAttachmentCount, Allocator.Temp);
                    int transformPoseOffsetIndex = 0;
                    for (int i = 0; i < subjects.Count; ++i)
                    {
                        if (subjects[i].attachmentType == LegacySkinAttachment.AttachmentType.Transform)
                        {
                            transformItems[transformPoseOffsetIndex++] = itemsBuffer[subjects[i].attachmentIndex];
                        }
                    }

                    transformAttachmentItemBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,transformAttachmentCount, UnsafeUtility.SizeOf<SkinAttachmentSystem.SkinAttachmentItemGPU>());
                    transformAttachmentItemBuffer.SetData(transformItems);
                    transformItems.Dispose();
                }

                transformAttachmentPosBuffer =
                    new GraphicsBuffer(GraphicsBuffer.Target.Raw,transformAttachmentCount, transformAttachmentBufferStride);
                transformAttachmentPosBuffer.name = "Transform Attachment Positions Buffer";
            }

            itemsBuffer.Dispose();
            
            gpuResourcesAllocated = true;

            return true;
        }

        void DestroyGPUResources()
        {
            if (attachmentPosesBuffer != null)
            {
                attachmentPosesBuffer.Dispose();
                attachmentItemsBuffer.Dispose();

                attachmentPosesBuffer = null;
            }

            if (transformAttachmentPosBuffer != null)
            {
                transformAttachmentPosBuffer.Dispose();
                transformAttachmentPosBuffer = null;
            }

            if (transformAttachmentItemBuffer != null)
            {
                transformAttachmentItemBuffer.Dispose();
                transformAttachmentItemBuffer = null;
            }

            gpuResourcesAllocated = false;
        }

        void ResolveSubjectsGPU()
        {
            if (subjects.Count == 0 || attachmentPosesBuffer == null) return;
            TryGetComponent<MeshFilter>(out var mf);
            TryGetComponent<SkinnedMeshRenderer>(out var smr);
            TryGetComponent<MeshRenderer>(out var mr);
            if (smr == null && (mf == null || mr == null)) return;

            SkinAttachmentSystem.SkinAttachmentTargetDescGPU targetDesc = default;
            
            if (smr != null)
            {
                if (!SkinAttachmentSystem.FillSkinAttachmentTargetDesc(smr, ref targetDesc))
                {
                    return;
                }
            }
            else
            {
                if (!SkinAttachmentSystem.FillSkinAttachmentTargetDesc(mr, mf, ref targetDesc))
                {
                    return;
                }
            }

            Matrix4x4 targetToWorld;
            if (smr)
            {
                targetToWorld = transform.parent.localToWorldMatrix * Matrix4x4.TRS(transform.localPosition,
                    transform.localRotation, Vector3.one);
            }
            else
            {
                targetToWorld = transform.localToWorldMatrix;
            }
            CommandBuffer cmd = CommandBufferPool.Get("Resolve SkinAttachments");
            
            List<SkinAttachmentSystem.SkinAttachmentDescGPU> attachmentDescs =
                new List<SkinAttachmentSystem.SkinAttachmentDescGPU>();
            
            for (int i = 0; i < subjects.Count; i++)
            {
                LegacySkinAttachment subject = subjects[i];
                if (subject.meshInstance == null) continue;
                
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
                        targetToSubject =
                            (subject.skinningBoneBindPoseInverse * subject.skinningBone.worldToLocalMatrix) *
                            targetToWorld;
                    else
                        targetToSubject = subject.transform.worldToLocalMatrix * targetToWorld;
                }
                if (subject.attachmentCount != 0)
                {
                    SkinAttachmentSystem.SkinAttachmentDescGPU attachmentDesc = default;
                    if (SkinAttachmentSystem.FillSkinAttachmentDesc(subject.meshInstance, targetToSubject,
                            attachmentPosesBuffer,
                            attachmentItemsBuffer, subject.attachmentIndex, subject.attachmentCount, true, ref attachmentDesc))
                    {
                        attachmentDescs.Add(attachmentDesc);
                    }
                }
                
            }

            //execute resolve
            if (attachmentDescs.Count > 0)
            {
                SkinAttachmentSystem.ResolveSubjectsGPU(cmd, ref targetDesc, attachmentDescs.ToArray());
            }
            
            //update mesh bounds
            var targetMeshWorldBounds = smr ? smr.bounds : mr.bounds;
            var targetMeshWorldBoundsCenter = targetMeshWorldBounds.center;
            var targetMeshWorldBoundsExtent = targetMeshWorldBounds.extents;
            for (int i = 0; i < subjects.Count; i++)
            {
                LegacySkinAttachment subject = subjects[i];
                if (subject.meshInstance == null) continue;
                
                Profiler.BeginSample("conservative-bounds");
                {
                    var worldToSubject = subject.transform.worldToLocalMatrix;
                    var subjectBoundsCenter = worldToSubject.MultiplyPoint(targetMeshWorldBoundsCenter);
                    var subjectBoundsRadius =
                        worldToSubject.MultiplyVector(targetMeshWorldBoundsExtent).magnitude +
                        subject.meshAssetRadius;
                    var subjectBounds = subject.meshInstance.bounds;
                    {
                        subjectBounds.center = subjectBoundsCenter;
                        subjectBounds.extents = subjectBoundsRadius * Vector3.one;
                    }
                    subject.meshInstance.bounds = subjectBounds;
                }
                Profiler.EndSample();
                
                subject.NotifyOfMeshModified(cmd);
            }

            //Resolve transform attachments
            if (transformAttachmentPosBuffer != null)
            {
                SkinAttachmentSystem.SkinAttachmentDescGPU transformAttachmentData = new SkinAttachmentSystem.SkinAttachmentDescGPU 
                {
                    itemsBuffer = transformAttachmentItemBuffer,
                    posesBuffer = attachmentPosesBuffer,
                    positionsNormalsTangentsBuffer = transformAttachmentPosBuffer,
                    movecsBuffer = null,
                    itemsOffset = 0,
                    itemsCount = transformAttachmentItemBuffer.count,
                    positionsNormalsTangentsOffsetStride = (0, -1, -1, transformAttachmentPosBuffer.stride),
                    movecsOffsetStride = (0,0),
                    targetToAttachment = targetToWorld,
                    resolveNormalsAndTangents = false
                };
                
                SkinAttachmentSystem.ResolveSubjectsGPU(cmd, ref targetDesc, new [] { transformAttachmentData });
            }

            bool needsGPUFence = afterResolveFenceRequested;

            if (needsGPUFence)
            {
                afterGPUResolveFence = cmd.CreateAsyncGraphicsFence();
                afterGPUResolveFenceValid = true;
            }
            else
            {
                afterGPUResolveFenceValid = false;
            }

            Graphics.ExecuteCommandBuffer(cmd);
            
            CommandBufferPool.Release(cmd);

            transformGPUPositionsReadBack = false;
        
            foreach (var desc in attachmentDescs)
            {
                SkinAttachmentSystem.FreeSkinAttachmentDesc(desc);
            }

            SkinAttachmentSystem.FreeSkinAttachmentTargetDesc(targetDesc);
            
        }

#endif

        #endregion GPUResolve

#if UNITY_EDITOR
        public void OnDrawGizmosSelected()
        {
            var activeGO = UnityEditor.Selection.activeGameObject;
            if (activeGO == null)
                return;
            if (activeGO != this.gameObject && activeGO.GetComponent<LegacySkinAttachment>() == null)
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
                        Gizmos.DrawRay(meshPositions[i], meshNormals[i] * 0.001f); // 1mm
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

                    SkinAttachmentSystem.SkinAttachmentTargetDescCPU attachmentTargetDesc;
                    attachmentTargetDesc.positions = meshBuffers.vertexPositions;
                    attachmentTargetDesc.normals = meshBuffers.vertexNormals;
                    attachmentTargetDesc.tangents = meshBuffers.vertexTangents;

                    Vector3[] resolvedPositions = new Vector3[attachmentCount];
                    Vector3[] resolvedNormals = new Vector3[attachmentCount];
                    Vector4[] resolvedTangents = new Vector4[attachmentCount];
                    
                    SkinAttachmentSystem.SkinAttachmentDescCPU attachmentDesc;
                    attachmentDesc.skinAttachmentItems = attachData.ItemData;
                    attachmentDesc.skinAttachmentPoses = attachData.pose;
                    attachmentDesc.resolvedPositions = resolvedPositions;
                    attachmentDesc.resolvedNormals = resolvedNormals;
                    attachmentDesc.resolvedTangents = resolvedTangents;
                    attachmentDesc.resolveTransform = Matrix4x4.identity;
                    attachmentDesc.itemsOffset = attachmentIndex;
                    attachmentDesc.itemsCount = attachmentCount;

                    {
                        SkinAttachmentSystem.ResolveSubjectsCPU(ref attachmentTargetDesc, new[] { attachmentDesc });

                        Vector3 size = 0.0002f * Vector3.one;

                        for (int i = 0; i != attachmentCount; i++)
                        {
                            Gizmos.color = Color.yellow;
                            Gizmos.DrawCube(resolvedPositions[i], size);
                            Gizmos.color = Color.green;
                            Gizmos.DrawRay(resolvedPositions[i], 0.1f * resolvedNormals[i]);
                            Gizmos.color = Color.red;
                            Gizmos.DrawRay(resolvedPositions[i], 0.1f * resolvedTangents[i] * resolvedTangents[i].w);
                        }
                    }
                }

                Profiler.EndSample();
            }
        }
#endif
    }
}