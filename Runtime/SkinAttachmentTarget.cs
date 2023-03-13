using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
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

namespace Unity.DemoTeam.DigitalHuman
{
    using static SkinAttachmentDataBuilder;

    [ExecuteAlways]
    public class SkinAttachmentTarget : MonoBehaviour
    {
        public struct MeshInfo
        {
            public MeshBuffers meshBuffers;
            public MeshAdjacency meshAdjacency;
            public KdTree3 meshVertexBSP;
            public bool valid;
        }

        
        [HideInInspector] public List<SkinAttachment> subjects = new List<SkinAttachment>();

        [NonSerialized] public Mesh meshBakedSmr;
        [NonSerialized] public Mesh meshBakedOrAsset;
        [NonSerialized] public MeshBuffers meshBuffers;
        [NonSerialized] public Mesh meshBuffersLastAsset;

        public SkinAttachmentData attachData;

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

        public ComputeBuffer TransformAttachmentGPUPositionBuffer => transformAttachmentPosBuffer;
        public int TransformAttachmentGPUPositionBufferStride => transformAttachmentBufferStride;


#else
        public readonly bool executeOnGPU = false;

#endif
        private bool UseCPUExecution => !executeOnGPU;

        private MeshInfo cachedMeshInfo;
        private int cachedMeshInfoFrame = -1;

        private JobHandle[] stagingJobs;
        private Vector3[][] stagingDataVec3;
        private Vector4[][] stagingDataVec4;
        private GCHandle[] stagingPins;


        private bool subjectsNeedRefresh = false;

        private bool afterGPUResolveFenceValid;
        private GraphicsFence afterGPUResolveFence;
        private bool afterResolveFenceRequested = false;
        private bool transformGPUPositionsReadBack = false;

#if UNITY_2021_2_OR_NEWER
        private ComputeShader resolveAttachmentsCS;
        private int resolveAttachmentsKernel = 0;
        private int resolveAttachmentsWithMovecsKernel = 0;
        private int resolveTransformAttachmentsKernel = 0;
        private ComputeBuffer attachmentPosesBuffer;
        private ComputeBuffer attachmentItemsBuffer;
        private ComputeBuffer transformAttachmentPosBuffer;
        private ComputeBuffer transformAttachmentOffsetBuffer;
        private int transformAttachmentCount = 0;
        private bool gpuResourcesAllocated = false;
        const int transformAttachmentBufferStride = 3 * sizeof(float); //float3, position

        static class UniformsResolve
        {
            internal static int _AttachmentPosesBuffer = Shader.PropertyToID("_AttachmentPosesBuffer");
            internal static int _AttachmentItemsBuffer = Shader.PropertyToID("_AttachmentItemsBuffer");
            internal static int _TransformAttachmentOffsetBuffer = Shader.PropertyToID("_TransformAttachmentOffsetBuffer");

            internal static int _SkinPositionsBuffer = Shader.PropertyToID("_SkinPositionsBuffer");
            internal static int _SkinNormalsBuffer = Shader.PropertyToID("_SkinNormalsBuffer");
            internal static int _SkinTangentsBuffer = Shader.PropertyToID("_SkinTangentsBuffer");
            
            internal static int _SkinPositionStrideOffset = Shader.PropertyToID("_SkinPositionStrideOffset");
            internal static int _SkinNormalStrideOffset = Shader.PropertyToID("_SkinNormalStrideOffset");
            internal static int _SkinTangentStrideOffset = Shader.PropertyToID("_SkinTangentStrideOffset");

            internal static int _AttachmentPosNormalTangentBuffer = Shader.PropertyToID("_AttachmentPosNormalTangentBuffer");

            internal static int _AttachmentMovecsBuffer = Shader.PropertyToID("_AttachmentMovecsBuffer");

           

            internal static int _StridePosNormTanOffsetAttachment = Shader.PropertyToID("_StridePosNormTanOffsetAttachment");

            internal static int _StrideOffsetMovecs = Shader.PropertyToID("_StrideOffsetMovecs");

            internal static int _ResolveTransform = Shader.PropertyToID("_ResolveTransform");

            internal static int _PostSkinningToAttachmentTransform = Shader.PropertyToID("_PostSkinningToAttachmentTransform");
            internal static int _NumberOfAttachments = Shader.PropertyToID("_NumberOfAttachments");
            internal static int _AttachmentOffset = Shader.PropertyToID("_AttachmentOffset");
        }
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
						if (subjects[i].attachmentType != SkinAttachment.AttachmentType.Transform) continue;
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
            int stagingPinsSourceDataCount = 3;
            int stagingPinsSourceDataOffset = subjects.Count * 3;

            ArrayUtils.ResizeChecked(ref stagingJobs, subjects.Count);
            ArrayUtils.ResizeChecked(ref stagingDataVec3, subjects.Count * 2);
            ArrayUtils.ResizeChecked(ref stagingDataVec4, subjects.Count);
            ArrayUtils.ResizeChecked(ref stagingPins, subjects.Count * 3 + stagingPinsSourceDataCount);

            GCHandle attachDataPosePin = GCHandle.Alloc(attachData.pose, GCHandleType.Pinned);
            GCHandle attachDataItemPin = GCHandle.Alloc(attachData.ItemData, GCHandleType.Pinned);

            MeshBuffers mb = meshBuffers;

            stagingPins[stagingPinsSourceDataOffset + 0] =
                GCHandle.Alloc(mb.vertexPositions, GCHandleType.Pinned);
            stagingPins[stagingPinsSourceDataOffset + 1] =
                GCHandle.Alloc(mb.vertexTangents, GCHandleType.Pinned);
            stagingPins[stagingPinsSourceDataOffset + 2] =
                GCHandle.Alloc(mb.vertexNormals, GCHandleType.Pinned);

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

            for (int i = 0, n = subjects.Count; i != n; i++)
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

                bool resolveTangents = subject.meshInstance && subject.meshInstance.HasVertexAttribute(VertexAttribute.Tangent);
                
                var indexPosStaging = i * 2 + 0;
                var indexNrmStaging = i * 2 + 1;
                var indexTanStaging = i;
                
                var indexPosPins = i * 3 + 0;
                var indexNrmPins = i * 3 + 1;
                var indexTanPins = i * 3 + 2;
                

                ArrayUtils.ResizeChecked(ref stagingDataVec3[indexPosStaging], attachmentCount);
                ArrayUtils.ResizeChecked(ref stagingDataVec3[indexNrmStaging], attachmentCount);
                stagingPins[indexPosPins] = GCHandle.Alloc(stagingDataVec3[indexPosStaging], GCHandleType.Pinned);
                stagingPins[indexNrmPins] = GCHandle.Alloc(stagingDataVec3[indexNrmStaging], GCHandleType.Pinned);
                if (resolveTangents)
                {
                    ArrayUtils.ResizeChecked(ref stagingDataVec4[indexTanStaging], attachmentCount);
                    stagingPins[indexTanPins] = GCHandle.Alloc(stagingDataVec4[indexTanStaging], GCHandleType.Pinned);
                }

                unsafe
                {
                    Vector3* resolvedPositions = (Vector3*) stagingPins[indexPosPins].AddrOfPinnedObject().ToPointer();
                    Vector3* resolvedNormals = (Vector3*) stagingPins[indexNrmPins].AddrOfPinnedObject().ToPointer();
                    Vector4* resolvedTangents = resolveTangents ? (Vector4*) stagingPins[indexTanPins].AddrOfPinnedObject().ToPointer(): null; 
                    
                    switch (subject.attachmentType)
                    {
                        case SkinAttachment.AttachmentType.Transform:
                        {
                            stagingJobs[i] = ScheduleResolve(attachmentIndex, attachmentCount,
                                ref targetToWorld, mb,
                                resolvedPositions, resolvedNormals, resolvedTangents);
                        }
                            break;

                        case SkinAttachment.AttachmentType.Mesh:
                        case SkinAttachment.AttachmentType.MeshRoots:
                        {
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
                                        (subject.skinningBoneBindPoseInverse *
                                         subject.skinningBone.worldToLocalMatrix) * targetToWorld;
                                else
                                    targetToSubject = subject.transform.worldToLocalMatrix * targetToWorld;
                            }

                            stagingJobs[i] = ScheduleResolve(attachmentIndex, attachmentCount,
                                ref targetToSubject, mb,
                                resolvedPositions, resolvedNormals, resolvedTangents);
                        }
                            break;
                    }
                }
            }

            JobHandle.ScheduleBatchedJobs();

            while (true)
            {
                var jobsRunning = false;

                for (int i = 0, n = subjects.Count; i != n; i++)
                {
                    var subject = subjects[i];
                    if (subject.ChecksumCompare(attachData) == false)
                        continue;

                    var stillRunning = (stagingJobs[i].IsCompleted == false);
                    if (stillRunning)
                    {
                        jobsRunning = true;
                        continue;
                    }

                    var indexPosStaging = i * 2 + 0;
                    var indexNrmStaging = i * 2 + 1;
                    var indexTanStaging = i;
                
                    var indexPosPins = i * 3 + 0;
                    var indexNrmPins = i * 3 + 1;
                    var indexTanPins = i * 3 + 2;

                    bool alreadyApplied = stagingPins[indexPosPins].IsAllocated == false;

                    if (alreadyApplied)
                        continue;

                    bool resolvedTangents = subject.meshInstance && subject.meshInstance.HasVertexAttribute(VertexAttribute.Tangent);
                    
                    stagingPins[indexPosPins].Free();
                    stagingPins[indexNrmPins].Free();
                    if (resolvedTangents)
                    {
                        stagingPins[indexTanPins].Free();
                    }

                    Profiler.BeginSample("gather-subj");
                    switch (subject.attachmentType)
                    {
                        case SkinAttachment.AttachmentType.Transform:
                        {
                            subject.transform.position = stagingDataVec3[indexPosStaging][0];
                        }
                            break;

                        case SkinAttachment.AttachmentType.Mesh:
                        case SkinAttachment.AttachmentType.MeshRoots:
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

                            if (resolvedTangents)
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

                if (jobsRunning == false)
                    break;
            }

            for (int i = 0; i != stagingPinsSourceDataCount; i++)
            {
                stagingPins[stagingPinsSourceDataOffset + i].Free();
            }

            attachDataPosePin.Free();
            attachDataItemPin.Free();

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

        public void AddSubject(SkinAttachment subject)
        {
            if (subjects.Contains(subject) == false)
                subjects.Add(subject);

            subjectsNeedRefresh = true;
        }

        public void RemoveSubject(SkinAttachment subject)
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
                int dryRunPoseCount = 0;
                int dryRunItemCount = 0;

                for (int i = 0, n = subjects.Count; i != n; i++)
                {
                    if (subjects[i].attachmentMode == SkinAttachment.AttachmentMode.BuildPoses)
                    {
                        subjects[i].RevertVertexData();
                        BuildDataAttachSubject(ref attachData, transform, meshInfo, poseBuildParams, subjects[i], dryRun: true,
                            ref dryRunPoseCount, ref dryRunItemCount);
                    }
                }

                dryRunPoseCount = Mathf.NextPowerOfTwo(dryRunPoseCount);
                dryRunItemCount = Mathf.NextPowerOfTwo(dryRunItemCount);

                ArrayUtils.ResizeCheckedIfLessThan(ref attachData.pose, dryRunPoseCount);
                ArrayUtils.ResizeCheckedIfLessThan(ref attachData.ItemDataRef, dryRunItemCount);

                // pass 2: build poses
                for (int i = 0, n = subjects.Count; i != n; i++)
                {
                    if (subjects[i].attachmentMode == SkinAttachment.AttachmentMode.BuildPoses)
                    {
                        BuildDataAttachSubject(ref attachData, transform, meshInfo, poseBuildParams, subjects[i], dryRun: false,
                            ref dryRunPoseCount, ref dryRunPoseCount);
                    }
                }

                // pass 3: reference poses
                for (int i = 0, n = subjects.Count; i != n; i++)
                {
                    switch (subjects[i].attachmentMode)
                    {
                        case SkinAttachment.AttachmentMode.LinkPosesByReference:
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

                        case SkinAttachment.AttachmentMode.LinkPosesBySpecificIndex:
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
            attachData.dataVersion = SkinAttachmentData.DataVersion.Version_2;
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

        public static float3x3 ConstructMatrix(float3 normal, float3 tangent, float tangentW)
        {
            float3 bitangent = math.cross(normal, tangent) * math.sign(tangentW);
            return math.transpose(new float3x3(tangent, bitangent, normal));
        }

         public unsafe JobHandle ScheduleResolve(int attachmentIndex, int attachmentCount,
            ref Matrix4x4 resolveTransform, MeshBuffers sourceMeshBuffers, Vector3* resolvedPositions,
            Vector3* resolvedNormals, Vector4* resolvedTangents)
        {
            fixed (Vector3* meshPositions = sourceMeshBuffers.vertexPositions)
            fixed (Vector3* meshNormals = sourceMeshBuffers.vertexNormals)
            fixed (Vector4* meshTangents = sourceMeshBuffers.vertexTangents)
            fixed (SkinAttachmentItem3* attachItem = attachData.ItemData)
            fixed (SkinAttachmentPose* attachPose = attachData.pose)
            {
                var job = new ResolveJob()
                {
                    meshPositions = meshPositions,
                    meshNormals = meshNormals,
                    meshTangents = meshTangents,
                    attachItem = attachItem,
                    attachPose = attachPose,
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

        #region GPUResolve

#if UNITY_2021_2_OR_NEWER

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct SkinAttachmentPoseGPU
        {
            public float3 targetCoord;
            public int v0;
            public int v1;
            public int v2;
            public float area;
            public float targetDist;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct SkinAttachmentItemGPU
        {
            public float4 targetFrameDelta;
            public float3 targetOffset;
            public float targetFrameW;
            public int baseVertex;
            public int poseIndex;
            public int poseCount;
            public int pad0;
        };

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

            if (resolveAttachmentsCS == null)
            {
                resolveAttachmentsCS = Resources.Load<ComputeShader>("SkinAttachmentCS");
            }

            if (resolveAttachmentsCS == null)
            {
                return false;
            }

            resolveAttachmentsKernel = resolveAttachmentsCS.FindKernel("ResolveAttachment");
            resolveAttachmentsWithMovecsKernel = resolveAttachmentsCS.FindKernel("ResolveAttachmentWithMovecs");
            resolveTransformAttachmentsKernel = resolveAttachmentsCS.FindKernel("ResolveTransformAttachments");

            const int itemStructSize = 3 * 4 * sizeof(float); //3 * float4
            const int poseStructSize = 2 * 4 * sizeof(float); //2 * float4

            int itemsCount = attachData.itemCount;
            int posesCount = attachData.poseCount;

            if (itemsCount == 0 || posesCount == 0) return false;

            int resolvedVerticesCount = 0;
            for (int i = 0; i < subjects.Count; ++i)
            {
                resolvedVerticesCount += subjects[i].attachmentCount;
            }

            attachmentPosesBuffer = new ComputeBuffer(posesCount, poseStructSize);
            attachmentItemsBuffer = new ComputeBuffer(itemsCount, itemStructSize);

            //upload stuff that doesn't change
            NativeArray<SkinAttachmentPoseGPU> posesBuffer =
                new NativeArray<SkinAttachmentPoseGPU>(posesCount, Allocator.Temp);
            for (int i = 0; i < posesCount; ++i)
            {
                SkinAttachmentPoseGPU poseGPU;
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

            NativeArray<SkinAttachmentItemGPU> itemsBuffer =
                new NativeArray<SkinAttachmentItemGPU>(itemsCount, Allocator.Temp);
            for (int i = 0; i < itemsCount; ++i)
            {
                SkinAttachmentItem3 item = attachData.ItemData[i];
                
                SkinAttachmentItemGPU itemGPU;
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
            itemsBuffer.Dispose();

            //buffers for resolving transform attachments on GPU
            transformAttachmentCount = 0;
            for (int i = 0; i < subjects.Count; ++i)
            {
                if (subjects[i].attachmentType == SkinAttachment.AttachmentType.Transform)
                {
                    subjects[i].TransformAttachmentGPUBufferIndex = transformAttachmentCount;
                    ++transformAttachmentCount;
                }
            }

            //push transform attachments pose offset to a buffer to allow resolving them in one go
            if (transformAttachmentCount > 0)
            {
                {
                    NativeArray<uint> transformPoseOffsetCount =
                        new NativeArray<uint>(transformAttachmentCount, Allocator.Temp);
                    int transformPoseOffsetIndex = 0;
                    for (int i = 0; i < subjects.Count; ++i)
                    {
                        if (subjects[i].attachmentType == SkinAttachment.AttachmentType.Transform)
                        {
                            transformPoseOffsetCount[transformPoseOffsetIndex++] = (uint) subjects[i].attachmentIndex;
                        }
                    }

                    transformAttachmentOffsetBuffer = new ComputeBuffer(transformAttachmentCount, sizeof(uint),
                        ComputeBufferType.Structured);
                    transformAttachmentOffsetBuffer.SetData(transformPoseOffsetCount);
                    transformPoseOffsetCount.Dispose();
                }

                transformAttachmentPosBuffer =
                    new ComputeBuffer(transformAttachmentCount, transformAttachmentBufferStride, ComputeBufferType.Raw);
                transformAttachmentPosBuffer.name = "Transform Attachment Positions Buffer";
            }

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

            if (transformAttachmentOffsetBuffer != null)
            {
                transformAttachmentOffsetBuffer.Dispose();
                transformAttachmentOffsetBuffer = null;
            }

            gpuResourcesAllocated = false;
        }

        void ResolveSubjectsGPU()
        {
            if (subjects.Count == 0 || resolveAttachmentsCS == null || attachmentPosesBuffer == null) return;
            TryGetComponent<MeshFilter>(out var mf);
            TryGetComponent<SkinnedMeshRenderer>(out var smr);
            TryGetComponent<MeshRenderer>(out var mr);
            if (smr == null && (mf == null || mr == null)) return;

            Mesh skinMesh = mf != null ? mf.sharedMesh : smr.sharedMesh;
            if (skinMesh == null) return;
            int positionStream = skinMesh.GetVertexAttributeStream(VertexAttribute.Position);
            int normalStream = skinMesh.GetVertexAttributeStream(VertexAttribute.Normal);
            int tangentStream = skinMesh.GetVertexAttributeStream(VertexAttribute.Tangent);

            if (smr && (smr.vertexBufferTarget & GraphicsBuffer.Target.Raw) == 0)
            {
                smr.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            }

            if (mf && (mf.sharedMesh.vertexBufferTarget & GraphicsBuffer.Target.Raw) == 0)
            {
                mf.sharedMesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            }
            

            var targetMeshWorldBounds = smr ? smr.bounds : mr.bounds;
            var targetMeshWorldBoundsCenter = targetMeshWorldBounds.center;
            var targetMeshWorldBoundsExtent = targetMeshWorldBounds.extents;

            using GraphicsBuffer skinPositionsBuffer =
                mf != null ? mf.sharedMesh.GetVertexBuffer(positionStream) : smr.GetVertexBuffer();
            using GraphicsBuffer skinNormalsBuffer =
                mf != null ? mf.sharedMesh.GetVertexBuffer(normalStream) : smr.GetVertexBuffer();
            using GraphicsBuffer skinTangentsBuffer =
                mf != null ? mf.sharedMesh.GetVertexBuffer(tangentStream) : smr.GetVertexBuffer();

            if (skinPositionsBuffer == null || skinNormalsBuffer == null || skinTangentsBuffer == null)
            {
                Debug.LogError("SkinAttachmentTarget unable to fetch vertex attribute buffers, unable to drive attachments");
                return;
            }
            
            int[] skinPositionStrideOffset =
            {
                skinMesh.GetVertexBufferStride(positionStream),
                skinMesh.GetVertexAttributeOffset(VertexAttribute.Position),
            };
            int[] skinNormalStrideOffset =
            {
                skinMesh.GetVertexBufferStride(normalStream),
                skinMesh.GetVertexAttributeOffset(VertexAttribute.Normal),
            };
            int[] skinTangentStrideOffset =
            {
                skinMesh.GetVertexBufferStride(tangentStream),
                skinMesh.GetVertexAttributeOffset(VertexAttribute.Tangent),
            };

            Matrix4x4 targetToWorld;
            Matrix4x4
                postSkinningToAttachment =
                    Matrix4x4.identity; //need to apply rootbone transform to skinned vertices when resolving since bakemesh has applied it when attachdata is calculated
            {
                if (smr)
                {
                    targetToWorld = transform.parent.localToWorldMatrix * Matrix4x4.TRS(this.transform.localPosition,
                        this.transform.localRotation, Vector3.one);

                    if (smr.rootBone)
                    {
                        Matrix4x4 boneLocalToWorldNoScale =
                            Matrix4x4.TRS(smr.rootBone.position, smr.rootBone.rotation, Vector3.one);
                        postSkinningToAttachment = transform.parent.worldToLocalMatrix * boneLocalToWorldNoScale;
                    }
                }
                else
                {
                    postSkinningToAttachment = Matrix4x4.identity;
                    targetToWorld = this.transform.localToWorldMatrix;
                }
            }

            CommandBuffer cmd = CommandBufferPool.Get("Resolve SkinAttachments");

            cmd.BeginSample("Resolve SkinAttachments");
            //common uniforms
            cmd.SetComputeIntParams(resolveAttachmentsCS, UniformsResolve._SkinPositionStrideOffset,
                skinPositionStrideOffset);
            cmd.SetComputeIntParams(resolveAttachmentsCS, UniformsResolve._SkinNormalStrideOffset,
                skinNormalStrideOffset);
            cmd.SetComputeIntParams(resolveAttachmentsCS, UniformsResolve._SkinTangentStrideOffset,
                skinTangentStrideOffset);

            int[] kernels =
                {resolveAttachmentsKernel, resolveAttachmentsWithMovecsKernel, resolveTransformAttachmentsKernel};
            foreach (int kernel in kernels)
            {
                cmd.SetComputeBufferParam(resolveAttachmentsCS, kernel,
                    UniformsResolve._AttachmentPosesBuffer,
                    attachmentPosesBuffer);
                cmd.SetComputeBufferParam(resolveAttachmentsCS, kernel,
                    UniformsResolve._AttachmentItemsBuffer,
                    attachmentItemsBuffer);
                
                cmd.SetComputeBufferParam(resolveAttachmentsCS, kernel,
                    UniformsResolve._SkinPositionsBuffer,
                    skinPositionsBuffer);
                cmd.SetComputeBufferParam(resolveAttachmentsCS, kernel,
                    UniformsResolve._SkinNormalsBuffer,
                    skinNormalsBuffer);
                cmd.SetComputeBufferParam(resolveAttachmentsCS, kernel,
                    UniformsResolve._SkinTangentsBuffer,
                    skinTangentsBuffer);
            }


            //first resolve mesh attachments
            for (int i = 0; i < subjects.Count; i++)
            {
                SkinAttachment subject = subjects[i];
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


                int posStream = subject.meshInstance.GetVertexAttributeStream(VertexAttribute.Position);
                int normStream = subject.meshInstance.GetVertexAttributeStream(VertexAttribute.Normal);
                int tanStream = subject.meshInstance.GetVertexAttributeStream(VertexAttribute.Tangent);

                if (posStream != normStream || (posStream != tanStream && tanStream != -1))
                {
                    Debug.LogError(
                        "Attachment is required to have positions and normals (and tangents if available) in the same stream. Skipping attachment " +
                        subject.name);
                    continue;
                }

                int resolveKernel = resolveAttachmentsKernel;

                //movecs
                if (subject.GeneratePrecalculatedMotionVectors)
                {
                    resolveKernel = resolveAttachmentsWithMovecsKernel;
                    int movecsStream = subject.meshInstance.GetVertexAttributeStream(VertexAttribute.TexCoord5);

                    using GraphicsBuffer movecsVertexBuffer = subject.meshInstance.GetVertexBuffer(movecsStream);
                    int[] strideOffset =
                    {
                        subject.meshInstance.GetVertexBufferStride(movecsStream),
                        subject.meshInstance.GetVertexAttributeOffset(VertexAttribute.TexCoord5)
                    };

                    cmd.SetComputeIntParams(resolveAttachmentsCS, UniformsResolve._StrideOffsetMovecs,
                        strideOffset);
                    cmd.SetComputeBufferParam(resolveAttachmentsCS, resolveKernel,
                        UniformsResolve._AttachmentMovecsBuffer, movecsVertexBuffer);
                }

                using GraphicsBuffer attachmentVertexBuffer = subject.meshInstance.GetVertexBuffer(posStream);
                int[] attachmentVertexBufferStrideAndOffsets =
                {
                    subject.meshInstance.GetVertexBufferStride(posStream),
                    subject.meshInstance.GetVertexAttributeOffset(VertexAttribute.Position),
                    subject.meshInstance.GetVertexAttributeOffset(VertexAttribute.Normal),
                    subject.meshInstance.GetVertexAttributeOffset(VertexAttribute.Tangent)
                };

                cmd.SetComputeBufferParam(resolveAttachmentsCS, resolveKernel,
                    UniformsResolve._AttachmentPosNormalTangentBuffer, attachmentVertexBuffer);
                cmd.SetComputeIntParams(resolveAttachmentsCS, UniformsResolve._StridePosNormTanOffsetAttachment,
                    attachmentVertexBufferStrideAndOffsets);

                cmd.SetComputeMatrixParam(resolveAttachmentsCS, UniformsResolve._ResolveTransform, targetToSubject);
                cmd.SetComputeMatrixParam(resolveAttachmentsCS, UniformsResolve._PostSkinningToAttachmentTransform,
                    postSkinningToAttachment);
                cmd.SetComputeIntParam(resolveAttachmentsCS, UniformsResolve._NumberOfAttachments,
                    subject.attachmentCount);
                cmd.SetComputeIntParam(resolveAttachmentsCS, UniformsResolve._AttachmentOffset,
                    subject.attachmentIndex);


                resolveAttachmentsCS.GetKernelThreadGroupSizes(resolveKernel, out uint groupX, out uint groupY,
                    out uint groupZ);
                int dispatchCount = (subjects[i].attachmentCount + (int) groupX - 1) / (int) groupX;
                cmd.DispatchCompute(resolveAttachmentsCS, resolveKernel, dispatchCount, 1, 1);

                subject.NotifyOfMeshModified(cmd);

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
            }

            //Resolve transform attachments
            if (transformAttachmentPosBuffer != null)
            {
                int resolveKernel = resolveTransformAttachmentsKernel;

                int[] posBufferStrideOffset =
                {
                    transformAttachmentPosBuffer.stride,
                    0, 0, 0
                };

                cmd.SetComputeBufferParam(resolveAttachmentsCS, resolveKernel,
                    UniformsResolve._AttachmentPosNormalTangentBuffer, transformAttachmentPosBuffer);
                cmd.SetComputeIntParams(resolveAttachmentsCS, UniformsResolve._StridePosNormTanOffsetAttachment,
                    posBufferStrideOffset);

                cmd.SetComputeMatrixParam(resolveAttachmentsCS, UniformsResolve._ResolveTransform, targetToWorld);
                cmd.SetComputeMatrixParam(resolveAttachmentsCS, UniformsResolve._PostSkinningToAttachmentTransform,
                    postSkinningToAttachment);
                cmd.SetComputeIntParam(resolveAttachmentsCS, UniformsResolve._NumberOfAttachments,
                    transformAttachmentCount);
                cmd.SetComputeBufferParam(resolveAttachmentsCS, resolveKernel,
                    UniformsResolve._TransformAttachmentOffsetBuffer,
                    transformAttachmentOffsetBuffer);


                resolveAttachmentsCS.GetKernelThreadGroupSizes(resolveKernel, out uint groupX, out uint groupY,
                    out uint groupZ);
                int dispatchCount = (transformAttachmentCount + (int) groupX - 1) / (int) groupX;
                cmd.DispatchCompute(resolveAttachmentsCS, resolveKernel, dispatchCount, 1, 1);
            }

            cmd.EndSample("Resolve SkinAttachments");

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
        }

#endif

        #endregion GPUResolve

#if UNITY_EDITOR
        public void OnDrawGizmosSelected()
        {
            var activeGO = UnityEditor.Selection.activeGameObject;
            if (activeGO == null)
                return;
            if (activeGO != this.gameObject && activeGO.GetComponent<SkinAttachment>() == null)
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

                    using (var resolvedPositions = new UnsafeArrayVector3(attachmentCount))
                    using (var resolvedNormals = new UnsafeArrayVector3(attachmentCount))
                    using (var resolvedTangents = new UnsafeArrayVector4(attachmentCount))
                    {
                        var resolveTransform = Matrix4x4.identity;
                        var resolveJob = ScheduleResolve(attachmentIndex, attachmentCount, ref resolveTransform,
                            meshBuffers,
                            resolvedPositions.val, resolvedNormals.val, resolvedTangents.val);

                        JobHandle.ScheduleBatchedJobs();

                        resolveJob.Complete();

                        Vector3 size = 0.0002f * Vector3.one;

                        for (int i = 0; i != attachmentCount; i++)
                        {
                            Gizmos.color = Color.yellow;
                            Gizmos.DrawCube(resolvedPositions.val[i], size);
                            Gizmos.color = Color.green;
                            Gizmos.DrawRay(resolvedPositions.val[i], 0.1f * resolvedNormals.val[i]);
                            Gizmos.color = Color.red;
                            Gizmos.DrawRay(resolvedPositions.val[i], 0.1f * resolvedTangents.val[i] * resolvedTangents.val[i].w);
                        }
                    }
                }

                Profiler.EndSample();
            }
        }
#endif
    }
}