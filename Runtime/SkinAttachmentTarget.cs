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

        [Header("Debug options")] 
        public bool showWireframe = false;
        public bool showUVSeams = false;
        public bool showResolved = false;
        public bool showMouseOver = false;
        
#if UNITY_2021_2_OR_NEWER
        public event Action afterGPUAttachmentWorkCommitted;
        
        [Header("Execution")]
        public bool forceCPUExecution = false;
#else
        private const bool forceCPUExecution = true;
#endif
        private bool UseCPUExecution => forceCPUExecution;

        private MeshInfo cachedMeshInfo;
        private int cachedMeshInfoFrame = -1;

        private JobHandle[] stagingJobs;
        private Vector3[][] stagingData;
        private GCHandle[] stagingPins;

        
        private bool subjectsNeedRefresh = false;
        private bool subjectsNeededRefresh = false;
        private List<SkinAttachment> subjectsCPU = new List<SkinAttachment>();
        private List<SkinAttachment> subjectsGPU = new List<SkinAttachment>();
        
#if UNITY_2021_2_OR_NEWER
        private ComputeShader resolveAttachmentsCS;
        private int resolveAttachmentsKernel = 0;
        private int resolveAttachmentsWithMovecsKernel = 0;
        private ComputeBuffer attachmentPosesBuffer;
        private ComputeBuffer attachmentItemsBuffer;
        
        //Sparse mesh 
        private GameObject sparseMeshGO;
        private Mesh sparseMeshOriginalSource;
        private Mesh sparseMeshDeformed; //deformed, referenced by SkinnedMeshRenderer/MeshFilter
        private Mesh sparseMeshSkinned; //deformed and skinned (if the mesh is skinned, otherwise null)
        private MeshBuffers sparseMeshBuffers;
        private Vector3[] sparseMeshUndeformedPositions;
        private Vector3[] sparseMeshUndeformedNormals;
        private Vector3[] sparseMeshDeformedPositions;
        private Vector3[] sparseMeshDeformedNormals;
        private SkinDeformationRenderer deformRenderer;
        
        static class UniformsResolve
        {
            internal static int _AttachmentPoses = Shader.PropertyToID("_AttachmentPoses");
            internal static int _AttachmentItems = Shader.PropertyToID("_AttachmentItems");

            internal static int _SkinPosNormalBuffer = Shader.PropertyToID("_SkinPosNormalBuffer");
            internal static int _AttachmentPosNormalBuffer = Shader.PropertyToID("_AttachmentPosNormalBuffer");
            internal static int _AttachmentMovecsBuffer = Shader.PropertyToID("_AttachmentMovecsBuffer");

            internal static int _StridePosNormOffsetSkin = Shader.PropertyToID("_StridePosNormOffsetSkin");
            internal static int _StridePosNormOffsetAttachment = Shader.PropertyToID("_StridePosNormOffsetAttachment");
            internal static int _StrideOffsetMovecs = Shader.PropertyToID("_StrideOffsetMovecs");
            

            internal static int _ResolveTransform = Shader.PropertyToID("_ResolveTransform");

            internal static int _PostSkinningToAttachmentTransform =
                Shader.PropertyToID("_PostSkinningToAttachmentTransform");

            internal static int _NumberOfAttachments = Shader.PropertyToID("_NumberOfAttachments");
            internal static int _AttachmentOffset = Shader.PropertyToID("_AttachmentOffset");
        }
#endif

        

        void OnEnable()
        {
            UpdateMeshBuffersFullMesh();
            subjectsNeedRefresh = true;

            
#if UNITY_2021_2_OR_NEWER
            deformRenderer = GetComponent<SkinDeformationRenderer>();
            var smr = GetComponent<SkinnedMeshRenderer>();
            if (smr != null)
            {
                smr.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            }

            RenderPipelineManager.beginFrameRendering -= AfterGpuSkinningCallback;
            RenderPipelineManager.beginFrameRendering += AfterGpuSkinningCallback;
#endif
        }

        private void OnDisable()
        {
#if UNITY_2021_2_OR_NEWER
            DestroyGPUResources();
            RenderPipelineManager.beginFrameRendering -= AfterGpuSkinningCallback;

            DestroySparseMeshResources();
#endif
        }

        void LateUpdate()
        {
            subjectsNeededRefresh = subjectsNeedRefresh;
            ResolveSubjects();
        }


        
        bool UpdateMeshBuffersFullMesh(bool forceMeshBakeCPU = false)
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
                meshBuffers.LoadPositionsFrom(meshBakedOrAsset);
                meshBuffers.LoadNormalsFrom(meshBakedOrAsset);
            }

            meshBuffersLastAsset = meshBakedOrAsset;
            return true;
        }

        void UpdateMeshInfo(ref MeshInfo info)
        {
            Profiler.BeginSample("upd-mesh-inf");
            UpdateMeshBuffersFullMesh(true);
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

            bool supportGPUResolve = !UseCPUExecution;
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
                        BuildDataAttachSubject(ref attachData, transform, meshInfo, subjects[i], dryRun: true,
                            ref dryRunPoseCount, ref dryRunItemCount);
                    }
                }

                dryRunPoseCount = Mathf.NextPowerOfTwo(dryRunPoseCount);
                dryRunItemCount = Mathf.NextPowerOfTwo(dryRunItemCount);

                ArrayUtils.ResizeCheckedIfLessThan(ref attachData.pose, dryRunPoseCount);
                ArrayUtils.ResizeCheckedIfLessThan(ref attachData.item, dryRunItemCount);

                // pass 2: build poses
                for (int i = 0, n = subjects.Count; i != n; i++)
                {
                    if (subjects[i].attachmentMode == SkinAttachment.AttachmentMode.BuildPoses)
                    {
                        BuildDataAttachSubject(ref attachData, transform, meshInfo, subjects[i], dryRun: false,
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
#if UNITY_2021_2_OR_NEWER
            if (supportGPUResolve)
            {
                ConvertSkinAttachmentDataForGPUAndCPUResolve();
            }
            DestroySparseMeshResources();
#endif
            attachData.builtForGPUResolve = supportGPUResolve;
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

        

        void ResolveSubjects()
        {
            if (attachData == null)
                return;

            if (attachData.driverVertexCount > meshBuffers.vertexCount)
                return; // prevent out of bounds if mesh shrunk since data was built

            if (attachData.builtForGPUResolve != !UseCPUExecution)
                return; //data has been built for incorrect execution mode
            
            int removed = subjects.RemoveAll(p => p == null);
            bool subjectsChanged = removed > 0 || subjectsNeedRefresh;

            if (subjectsChanged)
            {
                PrepareSubjectResolve();
            }

            if (UseCPUExecution)
            {
                if (UpdateMeshBuffersFullMesh(true))
                {
                    ResolveSubjectsCPU();
                }
            }
            
#if UNITY_2021_2_OR_NEWER
            else
            {
                if (UpdateMeshBuffersSparseMesh())
                {
                    ResolveSubjectsCPU();
                }
            }
#endif
            
        }

        void PrepareSubjectResolve()
        {
            subjectsCPU.Clear();
            subjectsGPU.Clear();

            for (int i = 0; i < subjects.Count; ++i)
            {
                if (subjects[i].UseComputeResolve() && !UseCPUExecution)
                {
                    subjectsGPU.Add(subjects[i]);
                }
                else
                {
                    subjectsCPU.Add(subjects[i]);
                }
            }
#if UNITY_2021_2_OR_NEWER
            //upload static data
            if (subjectsGPU.Count > 0)
            {
                subjectsNeedRefresh = !CreateGPUResources();
            }
            else
#endif
            {
                subjectsNeedRefresh = false;
            }
        }

        

        void ResolveSubjectsCPU()
        {
            Profiler.BeginSample("resolve-subj-all-cpu");
            int stagingPinsSourceDataCount = 3;
            int stagingPinsSourceDataOffset = subjectsCPU.Count * 2;

            ArrayUtils.ResizeChecked(ref stagingJobs, subjectsCPU.Count);
            ArrayUtils.ResizeChecked(ref stagingData, subjectsCPU.Count * 2);
            ArrayUtils.ResizeChecked(ref stagingPins, subjectsCPU.Count * 2 + stagingPinsSourceDataCount);

            GCHandle attachDataPosePin = GCHandle.Alloc(attachData.pose, GCHandleType.Pinned);
            GCHandle attachDataItemPin = GCHandle.Alloc(attachData.item, GCHandleType.Pinned);
#if UNITY_2021_2_OR_NEWER
            MeshBuffers mb = UseCPUExecution ? meshBuffers : sparseMeshBuffers;
#else
            MeshBuffers mb = meshBuffers;
#endif
            
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

            for (int i = 0, n = subjectsCPU.Count; i != n; i++)
            {
                var subject = subjectsCPU[i];
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

                var indexPos = i * 2 + 0;
                var indexNrm = i * 2 + 1;

                ArrayUtils.ResizeChecked(ref stagingData[indexPos], attachmentCount);
                ArrayUtils.ResizeChecked(ref stagingData[indexNrm], attachmentCount);
                stagingPins[indexPos] = GCHandle.Alloc(stagingData[indexPos], GCHandleType.Pinned);
                stagingPins[indexNrm] = GCHandle.Alloc(stagingData[indexNrm], GCHandleType.Pinned);

                unsafe
                {
                    Vector3* resolvedPositions = (Vector3*) stagingPins[indexPos].AddrOfPinnedObject().ToPointer();
                    Vector3* resolvedNormals = (Vector3*) stagingPins[indexNrm].AddrOfPinnedObject().ToPointer();

                    switch (subject.attachmentType)
                    {
                        case SkinAttachment.AttachmentType.Transform:
                        {
                            stagingJobs[i] = ScheduleResolve(attachmentIndex, attachmentCount,
                                ref targetToWorld, mb,
                                resolvedPositions, resolvedNormals);
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
                                resolvedPositions, resolvedNormals);
                        }
                            break;
                    }
                }
            }

            JobHandle.ScheduleBatchedJobs();

            while (true)
            {
                var jobsRunning = false;

                for (int i = 0, n = subjectsCPU.Count; i != n; i++)
                {
                    var subject = subjectsCPU[i];
                    if (subject.ChecksumCompare(attachData) == false)
                        continue;

                    var stillRunning = (stagingJobs[i].IsCompleted == false);
                    if (stillRunning)
                    {
                        jobsRunning = true;
                        continue;
                    }

                    var indexPos = i * 2 + 0;
                    var indexNrm = i * 2 + 1;

                    bool alreadyApplied = stagingPins[indexPos].IsAllocated == false;

                    if (alreadyApplied)
                        continue;

                    stagingPins[indexPos].Free();
                    stagingPins[indexNrm].Free();

                    Profiler.BeginSample("gather-subj");
                    switch (subject.attachmentType)
                    {
                        case SkinAttachment.AttachmentType.Transform:
                        {
                            subject.transform.position = stagingData[indexPos][0];
                        }
                            break;

                        case SkinAttachment.AttachmentType.Mesh:
                        case SkinAttachment.AttachmentType.MeshRoots:
                        {
                            if (subject.meshInstance == null)
                                break;

                            if (subject.meshInstance.vertexCount != stagingData[indexPos].Length)
                            {
                                Debug.LogError("mismatching vertex- and attachment count", subject);
                                break;
                            }
                            
                            subject.meshInstance.SilentlySetVertices(stagingData[indexPos]);
                            subject.meshInstance.SilentlySetNormals(stagingData[indexNrm]);
                            
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
        


        public unsafe JobHandle ScheduleResolve(int attachmentIndex, int attachmentCount,
            ref Matrix4x4 resolveTransform, MeshBuffers sourceMeshBuffers, Vector3* resolvedPositions, Vector3* resolvedNormals)
        {
            fixed (Vector3* meshPositions = sourceMeshBuffers.vertexPositions)
            fixed (Vector3* meshNormals = sourceMeshBuffers.vertexNormals)
            fixed (SkinAttachmentItem* attachItem = attachData.item)
            fixed (SkinAttachmentPose* attachPose = attachData.pose)
            {
                var job = new ResolveJob()
                {
                    meshPositions = meshPositions,
                    meshNormals = meshNormals,
                    attachItem = attachItem,
                    attachPose = attachPose,
                    resolveTransform = resolveTransform,
                    resolvedPositions = resolvedPositions,
                    resolvedNormals = resolvedNormals,
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
            public SkinAttachmentItem* attachItem;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public SkinAttachmentPose* attachPose;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* resolvedPositions;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* resolvedNormals;

            public Matrix4x4 resolveTransform;

            public int attachmentIndex;
            public int attachmentCount;

            //TODO this needs optimization
            public void Execute(int i)
            {
                var targetBlended = new Vector3(0.0f, 0.0f, 0.0f);
                var targetWeights = 0.0f;

                SkinAttachmentItem item = attachItem[attachmentIndex + i];

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

                var targetNormalRot = Quaternion.FromToRotation(item.baseNormal, meshNormals[item.baseVertex]);
                var targetNormal = targetNormalRot * item.targetNormal;
                var targetOffset = targetNormalRot * item.targetOffset;

                resolvedPositions[i] = resolveTransform.MultiplyPoint3x4(targetBlended / targetWeights + targetOffset);
                resolvedNormals[i] = resolveTransform.MultiplyVector(targetNormal);
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
            public float3 baseNormal;
            public int poseIndex;
            public float3 targetNormal;
            public int poseCount;
            public float3 targetOffset; //TODO split this into leaf type item that doesn't perform full resolve
            public int baseVertex;
        };

        void AfterGpuSkinningCallback(ScriptableRenderContext scriptableRenderContext, Camera[] cameras)
        {
            bool dataValid = !(attachData == null || !attachData.builtForGPUResolve || attachData.builtForGPUResolve != !UseCPUExecution || meshBuffers == null);

            if (!dataValid || attachData.driverVertexCount > meshBuffers.vertexCount)
                dataValid = false; // prevent out of bounds if mesh shrunk since data was built

            if (dataValid)
            {
                ResolveSubjectsGPU();
            }
                

            afterGPUAttachmentWorkCommitted?.Invoke();
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

            const int itemStructSize = 3 * 4 * sizeof(float); //4 * float4
            const int poseStructSize = 2 * 4 * sizeof(float); //2 * float4

            int itemsCount = attachData.gpuItemsCount > 0 ? attachData.gpuItemsCount : attachData.itemCount;
            int posesCount = attachData.gpuPosesCount > 0 ? attachData.gpuPosesCount : attachData.poseCount;

            int resolvedVerticesCount = 0;
            for (int i = 0; i < subjectsGPU.Count; ++i)
            {
                resolvedVerticesCount += subjectsGPU[i].attachmentCount;
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
                SkinAttachmentItemGPU itemGPU;
                itemGPU.baseNormal = attachData.item[i].baseNormal;
                itemGPU.poseIndex = attachData.item[i].poseIndex;
                itemGPU.targetNormal = attachData.item[i].targetNormal;
                itemGPU.poseCount = attachData.item[i].poseCount;
                itemGPU.targetOffset = attachData.item[i].targetOffset;
                itemGPU.baseVertex = attachData.item[i].baseVertex;
                itemsBuffer[i] = itemGPU;
            }

            attachmentItemsBuffer.SetData(itemsBuffer);
            itemsBuffer.Dispose();

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
        }

        void ResolveSubjectsGPU()
        {
            if (subjectsGPU.Count == 0 || resolveAttachmentsCS == null || attachmentPosesBuffer == null) return;
            var mf = GetComponent<MeshFilter>();
            var smr = GetComponent<SkinnedMeshRenderer>();
            if (smr == null && mf == null) return;

            Mesh skinMesh = mf != null ? mf.sharedMesh : smr.sharedMesh;
            int positionStream = skinMesh.GetVertexAttributeStream(VertexAttribute.Position);
            int normalStream = skinMesh.GetVertexAttributeStream(VertexAttribute.Normal);

            if (positionStream != normalStream)
            {
                Debug.LogError(
                    "SkinAttachmentTarget requires that the skin has it's positions and normals in the same vertex buffer/stream. Unable to drive attachments.");
                return;
            }

            var targetMeshWorldBounds = smr.bounds;
            var targetMeshWorldBoundsCenter = targetMeshWorldBounds.center;
            var targetMeshWorldBoundsExtent = targetMeshWorldBounds.extents;

            using GraphicsBuffer skinVertexBuffer =
                mf != null ? mf.sharedMesh.GetVertexBuffer(positionStream) : smr.GetVertexBuffer();

            if (skinVertexBuffer == null)
            {
                return;
            }

            int[] skinVertexBufferStrideAndOffsets =
            {
                skinMesh.GetVertexBufferStride(positionStream),
                skinMesh.GetVertexAttributeOffset(VertexAttribute.Position),
                skinMesh.GetVertexAttributeOffset(VertexAttribute.Normal)
            };

            Matrix4x4 targetToWorld;
            Matrix4x4
                postSkinningToAttachment =
                    Matrix4x4.identity; //need to apply rootbone transform to skinned vertices when resolving since bakemesh has applied it when attachdata is calculated
            {
                if (smr)
                {
                    targetToWorld = transform.parent.localToWorldMatrix * Matrix4x4.TRS(this.transform.localPosition, this.transform.localRotation, Vector3.one);

                    if (smr.rootBone)
                    {
                        Matrix4x4 boneLocalToWorldNoScale = Matrix4x4.TRS(smr.rootBone.position, smr.rootBone.rotation, Vector3.one);
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
            cmd.SetComputeIntParams(resolveAttachmentsCS, UniformsResolve._StridePosNormOffsetSkin,
                skinVertexBufferStrideAndOffsets);
            
            {
                cmd.SetComputeBufferParam(resolveAttachmentsCS, resolveAttachmentsKernel,
                    UniformsResolve._AttachmentPoses,
                    attachmentPosesBuffer);
                cmd.SetComputeBufferParam(resolveAttachmentsCS, resolveAttachmentsKernel,
                    UniformsResolve._AttachmentItems,
                    attachmentItemsBuffer);
                cmd.SetComputeBufferParam(resolveAttachmentsCS, resolveAttachmentsKernel,
                    UniformsResolve._SkinPosNormalBuffer,
                    skinVertexBuffer);
            }
            {
                cmd.SetComputeBufferParam(resolveAttachmentsCS, resolveAttachmentsWithMovecsKernel, UniformsResolve._AttachmentPoses,
                    attachmentPosesBuffer);
                cmd.SetComputeBufferParam(resolveAttachmentsCS, resolveAttachmentsWithMovecsKernel, UniformsResolve._AttachmentItems,
                    attachmentItemsBuffer);

                cmd.SetComputeBufferParam(resolveAttachmentsCS, resolveAttachmentsWithMovecsKernel,
                    UniformsResolve._SkinPosNormalBuffer,
                    skinVertexBuffer);
            }

            for (int i = 0; i < subjectsGPU.Count; i++)
            {
                SkinAttachment subject = subjectsGPU[i];
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

                if (posStream != normStream)
                {
                    Debug.LogError(
                        "Attachment is required to have positions and normals in the same stream. Skipping attachment " +
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
                    cmd.SetComputeBufferParam(resolveAttachmentsCS, resolveKernel, UniformsResolve._AttachmentMovecsBuffer, movecsVertexBuffer);
                    
                }

                using GraphicsBuffer attachmentVertexBuffer = subject.meshInstance.GetVertexBuffer(posStream);
                int[] attachmentVertexBufferStrideAndOffsets =
                {
                    subject.meshInstance.GetVertexBufferStride(posStream),
                    subject.meshInstance.GetVertexAttributeOffset(VertexAttribute.Position),
                    subject.meshInstance.GetVertexAttributeOffset(VertexAttribute.Normal)
                };

                cmd.SetComputeBufferParam(resolveAttachmentsCS, resolveKernel,
                    UniformsResolve._AttachmentPosNormalBuffer, attachmentVertexBuffer);
                cmd.SetComputeIntParams(resolveAttachmentsCS, UniformsResolve._StridePosNormOffsetAttachment,
                    attachmentVertexBufferStrideAndOffsets);

                cmd.SetComputeMatrixParam(resolveAttachmentsCS, UniformsResolve._ResolveTransform, targetToSubject);
                cmd.SetComputeMatrixParam(resolveAttachmentsCS, UniformsResolve._PostSkinningToAttachmentTransform,
                    postSkinningToAttachment);
                cmd.SetComputeIntParam(resolveAttachmentsCS, UniformsResolve._NumberOfAttachments,
                    subject.attachmentCount);
                cmd.SetComputeIntParam(resolveAttachmentsCS, UniformsResolve._AttachmentOffset,
                    subject.attachmentIndex);

                const int groupSize = 64;
                int dispatchCount = (subjectsGPU[i].attachmentCount + groupSize - 1) / groupSize;
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
            
            cmd.EndSample("Resolve SkinAttachments");
            
            Graphics.ExecuteCommandBuffer(cmd);
            
            CommandBufferPool.Release(cmd);
        }
        void CreateSparseMeshResources()
        {
            var smrOrig = GetComponent<SkinnedMeshRenderer>();

            Mesh newMesh = new Mesh();
            newMesh.name = "SkinAttachmentSparseMesh";
            newMesh.hideFlags = HideFlags.HideAndDontSave & ~HideFlags.DontUnloadUnusedAsset;
            newMesh.MarkDynamic();

            Mesh oldMesh = sparseMeshOriginalSource;
            int childrenCount = transform.childCount;
            for (int i = 0; i != childrenCount; ++i)
            {
                if (transform.GetChild(i).name == "sparseMeshGO")
                {
                    sparseMeshGO = transform.GetChild(i).gameObject;
                }
            }

            if (!sparseMeshGO)
            {
                sparseMeshGO = new GameObject("sparseMeshGO");
                sparseMeshGO.hideFlags = HideFlags.HideAndDontSave & ~HideFlags.DontUnloadUnusedAsset;
            }
            
            sparseMeshGO.transform.SetParent(transform, false);

            sparseMeshGO.transform.localPosition = Vector3.zero;
            sparseMeshGO.transform.localRotation = Quaternion.identity;
            sparseMeshGO.transform.localScale = Vector3.one;

            if (smrOrig)
            {
                SkinnedMeshRenderer smrNew = sparseMeshGO.GetComponent<SkinnedMeshRenderer>();
                if (!smrNew)
                {
                    smrNew = sparseMeshGO.AddComponent<SkinnedMeshRenderer>();
                }
                smrNew.bones = smrOrig.bones;
                smrNew.rootBone = smrOrig.rootBone;
                smrNew.skinnedMotionVectors = false;
                smrNew.updateWhenOffscreen = true;
                if (oldMesh == null)
                {
                    oldMesh = smrOrig.sharedMesh;
                }
                smrNew.sharedMesh = newMesh;
            }
            else
            {
                MeshFilter mf = GetComponent<MeshFilter>();
                if (mf)
                {
                    MeshFilter mfNew = sparseMeshGO.GetComponent<MeshFilter>();
                    if (!mfNew)
                    {
                        mfNew = sparseMeshGO.AddComponent<MeshFilter>();
                    }

                    if (oldMesh == null)
                    {
                        oldMesh = mf.mesh;
                    }
                    mfNew.mesh = newMesh;
                }
                else
                {
                    DestroyImmediate(sparseMeshGO);
                    return;
                }
            }

            if (!oldMesh) return;

            sparseMeshOriginalSource = oldMesh;

            sparseMeshGO.SetActive(false);

            int[] verticesToPreserve = attachData.verticesRequiredByCPUAttachments;

            int vCount = attachData.verticesRequiredByCPUAttachments.Length;

            Vector3[] positions = oldMesh.vertices;
            Vector3[] normals = oldMesh.normals;
            BoneWeight[] weights = oldMesh.boneWeights;

            sparseMeshUndeformedPositions = new Vector3[vCount];
            sparseMeshUndeformedNormals = new Vector3[vCount];
            sparseMeshDeformedPositions = new Vector3[vCount];
            sparseMeshDeformedNormals = new Vector3[vCount];

            BoneWeight[] newWeights = new BoneWeight[vCount];

            int[] newIndices = new int[vCount];
            
            for (int i = 0; i != vCount; ++i)
            {
                int oldIndex = verticesToPreserve[i];
                sparseMeshUndeformedPositions[i] = positions[oldIndex];
                sparseMeshUndeformedNormals[i] = normals[oldIndex];
                newWeights[i] = weights[oldIndex];
                newIndices[i] = i;
            }

            newMesh.SetVertices(sparseMeshUndeformedPositions);
            newMesh.SetNormals(sparseMeshUndeformedNormals);
            newMesh.boneWeights = newWeights;

            newMesh.SetIndices(newIndices, MeshTopology.Points, 0);
            newMesh.bindposes = oldMesh.bindposes;

            newMesh.Optimize();

            sparseMeshDeformed = newMesh;
            if (smrOrig)
            {
                sparseMeshSkinned = Instantiate(newMesh);
            }
        }

        void DestroySparseMeshResources()
        {
            
            DestroyImmediate(sparseMeshGO);
            DestroyImmediate(sparseMeshDeformed);
            if (sparseMeshSkinned)
            {
                DestroyImmediate(sparseMeshSkinned);
            }

            sparseMeshBuffers = null;
        }

        bool UpdateMeshBuffersSparseMesh()
        {
            if (attachData == null || attachData.verticesRequiredByCPUAttachments == null ||
                attachData.verticesRequiredByCPUAttachments.Length == 0) return false;

            if (sparseMeshDeformed == null)
            {
                CreateSparseMeshResources();
            }

            if (sparseMeshDeformed == null) return false;

            //apply deformation (if applicable)
            if (deformRenderer)
            {
                ApplyDeformationToSparseMesh();
                sparseMeshDeformed.SilentlySetVertices(sparseMeshDeformedPositions);
                sparseMeshDeformed.SilentlySetNormals(sparseMeshDeformedNormals);
            }

            Mesh targetMesh;
            var smrSparse = sparseMeshGO.GetComponent<SkinnedMeshRenderer>();
            if (smrSparse)
            {
                smrSparse.BakeMesh(sparseMeshSkinned);
                targetMesh = sparseMeshSkinned;
            }
            else
            {
                targetMesh = sparseMeshDeformed;
            }

            if (sparseMeshBuffers == null)
            {
                sparseMeshBuffers = new MeshBuffers(targetMesh);
            }
            else
            {
                sparseMeshBuffers.LoadPositionsFrom(targetMesh);
                sparseMeshBuffers.LoadNormalsFrom(targetMesh);
            }

            return true;
        }

        void ConvertSkinAttachmentDataForGPUAndCPUResolve()
        {
            if (attachData == null)
                return;

            var meshInfo = GetCachedMeshInfo(forceRefresh: true);
            if (meshInfo.valid == false)
                return;

            //Step 1: sort poses and items to cpu and gpu resolved 
            SkinAttachmentItem[] itemCopy = new SkinAttachmentItem[attachData.itemCount];
            SkinAttachmentPose[] posesCopy = new SkinAttachmentPose[attachData.poseCount];

            Array.Copy(attachData.pose, posesCopy, attachData.poseCount);
            Array.Copy(attachData.item, itemCopy, attachData.itemCount);

            int currentPosesIndex = 0;
            int currentItemsIndex = 0;

            //move all gpu resolved items and poses to the beginning of the lists
            for (int i = 0; i != subjects.Count; ++i)
            {
                if ((subjects[i].attachmentIndex + subjects[i].attachmentCount) > attachData.itemCount)
                    continue;

                if (subjects[i].attachmentMode != SkinAttachment.AttachmentMode.BuildPoses)
                    continue;

                if (subjects[i].meshInstance == null &&
                    subjects[i].attachmentType != SkinAttachment.AttachmentType.Transform)
                    continue;

                if (subjects[i].UseComputeResolve())
                {
                    Array.Copy(itemCopy, subjects[i].attachmentIndex, attachData.item, currentItemsIndex,
                        subjects[i].attachmentCount);

                    for (int k = 0; k != subjects[i].attachmentCount; ++k)
                    {
                        int itemIndex = currentItemsIndex + k;
                        int poseIndex = attachData.item[itemIndex].poseIndex;
                        int poseCount = attachData.item[itemIndex].poseCount;

                        Array.Copy(posesCopy, poseIndex, attachData.pose, currentPosesIndex, poseCount);
                        
                        attachData.item[itemIndex].poseIndex = currentPosesIndex;
                        currentPosesIndex += poseCount;
                    }

                    subjects[i].attachmentIndex = currentItemsIndex;
                    currentItemsIndex += subjects[i].attachmentCount;
                }
            }

            attachData.gpuItemsCount = currentItemsIndex;
            attachData.gpuPosesCount = currentPosesIndex;

            for (int i = 0; i != subjects.Count; ++i)
            {
                if ((subjects[i].attachmentIndex + subjects[i].attachmentCount) > attachData.itemCount)
                    continue;

                if (subjects[i].attachmentMode != SkinAttachment.AttachmentMode.BuildPoses)
                    continue;

                if (subjects[i].meshInstance == null &&
                    subjects[i].attachmentType != SkinAttachment.AttachmentType.Transform)
                    continue;

                if (!subjects[i].UseComputeResolve())
                {
                    Array.Copy(itemCopy, subjects[i].attachmentIndex, attachData.item, currentItemsIndex,
                        subjects[i].attachmentCount);

                    for (int k = 0; k != subjects[i].attachmentCount; ++k)
                    {
                        int itemIndex = currentItemsIndex + k;
                        int poseIndex = attachData.item[itemIndex].poseIndex;
                        int poseCount = attachData.item[itemIndex].poseCount;

                        Array.Copy(posesCopy, poseIndex, attachData.pose, currentPosesIndex, poseCount);

                        attachData.item[itemIndex].poseIndex = currentPosesIndex;
                        currentPosesIndex += poseCount;
                    }

                    subjects[i].attachmentIndex = currentItemsIndex;
                    currentItemsIndex += subjects[i].attachmentCount;
                }
            }

            Assert.AreEqual(currentItemsIndex, attachData.itemCount);
            Assert.AreEqual(currentPosesIndex, attachData.poseCount);

            for (int i = 0; i != subjects.Count; ++i)
            {
                if (subjects[i].attachmentMode == SkinAttachment.AttachmentMode.LinkPosesByReference)
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
            }

            //Step 2: Go through cpu entries and gather a list of required vertices to calculate poses. Also make the pose vertex indices to be relative to this subset of vertices

            Dictionary<int, int> fullMeshToSparseMeshMapping = new Dictionary<int, int>();

            //Discover vertices required by the poses
            for (int i = 0; i != subjects.Count; i++)
            {
                if (!subjects[i].UseComputeResolve())
                {
                    for (int attachmentIndex = subjects[i].attachmentIndex;
                        attachmentIndex < (subjects[i].attachmentIndex + subjects[i].attachmentCount);
                        ++attachmentIndex)
                    {
                        int poseIndexBase = attachData.item[attachmentIndex].poseIndex;
                        int poseCount = attachData.item[attachmentIndex].poseCount;

                        fullMeshToSparseMeshMapping[attachData.item[attachmentIndex].baseVertex] = 0;

                        for (int poseIndex = poseIndexBase; poseIndex < (poseIndexBase + poseCount); ++poseIndex)
                        {
                            fullMeshToSparseMeshMapping[attachData.pose[poseIndex].v0] = 0;
                            fullMeshToSparseMeshMapping[attachData.pose[poseIndex].v1] = 0;
                            fullMeshToSparseMeshMapping[attachData.pose[poseIndex].v2] = 0;
                        }
                    }
                }
            }

            //sort vertices 
            int[] relevantVertices = new int[fullMeshToSparseMeshMapping.Count];
            {
                int i = 0;
                foreach (var e in fullMeshToSparseMeshMapping)
                {
                    relevantVertices[i] = e.Key;
                    ++i;
                }
            }
            Array.Sort(relevantVertices);

            //fill mapping table
            for (int i = 0; i < relevantVertices.Length; ++i)
            {
                fullMeshToSparseMeshMapping[relevantVertices[i]] = i;
            }

            //assign new vertex indices
            for (int i = attachData.gpuPosesCount; i != attachData.poseCount; i++)
            {
                int sparseV0 = fullMeshToSparseMeshMapping[attachData.pose[i].v0];
                int sparseV1 = fullMeshToSparseMeshMapping[attachData.pose[i].v1];
                int sparseV2 = fullMeshToSparseMeshMapping[attachData.pose[i].v2];

                attachData.pose[i].v0 = sparseV0;
                attachData.pose[i].v1 = sparseV1;
                attachData.pose[i].v2 = sparseV2;
            }
            
            for (int i = attachData.gpuItemsCount; i != attachData.itemCount; i++)
            {
                int sparseV = fullMeshToSparseMeshMapping[attachData.item[i].baseVertex];
                attachData.item[i].baseVertex = sparseV;
            }

            attachData.verticesRequiredByCPUAttachments = relevantVertices;
        }
        

        unsafe void ApplyDeformationToSparseMesh()
        {
            fixed (Vector3* meshPositions = sparseMeshUndeformedPositions)
            fixed (Vector3* meshNormals = sparseMeshUndeformedNormals)
            fixed (Vector3* meshPositionsTarget = sparseMeshDeformedPositions)
            fixed (Vector3* meshNormalsTarget = sparseMeshDeformedNormals)
            fixed (Vector3* deltaPos = deformRenderer.CurrentDeformedPositionOffsets)
            fixed (Vector3* deltaNorm = deformRenderer.CurrentDeformedNormalOffsets)
            fixed(int* vertexRemappings = attachData.verticesRequiredByCPUAttachments)
            {
                var job = new ApplyDeformation()
                {
                    undeformedPosition = meshPositions,
                    undeformedNormals = meshNormals,
                    deformedPosition = meshPositionsTarget,
                    deformedNormals = meshNormalsTarget,
                    deltaPosition = deltaPos,
                    deltaNormal = deltaNorm,
                    vertexRemapping = vertexRemappings
                };
                job.Schedule(sparseMeshUndeformedPositions.Length, 128).Complete();
            }
        }
        
        [BurstCompile(FloatMode = FloatMode.Fast)]
        unsafe struct ApplyDeformation : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* undeformedPosition;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* undeformedNormals;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* deltaPosition;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* deltaNormal;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* deformedPosition;

            [NativeDisableUnsafePtrRestriction, NoAlias]
            public Vector3* deformedNormals;
            
            [NativeDisableUnsafePtrRestriction, NoAlias]
            public int* vertexRemapping;

            public void Execute(int i)
            {
                int deltaIndex = vertexRemapping[i];
                
                deformedPosition[i] = undeformedPosition[i] + deltaPosition[deltaIndex];
                deformedNormals[i] = undeformedNormals[i] + deltaNormal[deltaIndex];
            }
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
                    {
                        var resolveTransform = Matrix4x4.identity;
                        var resolveJob = ScheduleResolve(attachmentIndex, attachmentCount, ref resolveTransform, meshBuffers,
                            resolvedPositions.val, resolvedNormals.val);

                        JobHandle.ScheduleBatchedJobs();

                        resolveJob.Complete();

                        Gizmos.color = Color.yellow;
                        Vector3 size = 0.0002f * Vector3.one;

                        for (int i = 0; i != attachmentCount; i++)
                        {
                            Gizmos.DrawCube(resolvedPositions.val[i], size);
                        }
                    }
                }

                Profiler.EndSample();
            }
        }
#endif
    }
}