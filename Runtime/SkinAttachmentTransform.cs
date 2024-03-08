using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.DemoTeam.DigitalHuman
{
    using SkinAttachmentItem = SkinAttachmentItem3;

    [ExecuteAlways]
    public class SkinAttachmentTransform : MonoBehaviour, SkinAttachmentComponentCommon.ISkinAttachmentComponent
    {
        public static int TransformAttachmentBufferStride => c_transformAttachmentBufferStride;
        public int CurrentOffsetIntoGPUPositionsBuffer => currentOffsetToTransformGroup;
        public GraphicsBuffer CurrentGPUPositionsBuffer => currentPositionsBufferGPU;

        public SkinAttachmentComponentCommon common = new SkinAttachmentComponentCommon();
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


        public bool readbackTransformFromGPU = false;

        public event Action onSkinAttachmentTransformResolved;

        private int currentOffsetToTransformGroup = 0;
        private GraphicsBuffer currentPositionsBufferGPU;
        private const int c_transformAttachmentBufferStride = 3 * sizeof(float);

        public void Attach(bool storePositionRotation = true)
        {
            common.Attach(this, storePositionRotation);
            UpdateAttachedState();
        }

        public void Detach(bool revertPositionRotation = true)
        {
            common.Detach(this, revertPositionRotation);
        }

        void OnEnable()
        {
            common.LoadBakedData();
            UpdateAttachedState();
        }

        void LateUpdate()
        {
            UpdateAttachedState();
            if (common.hasValidState && !common.explicitScheduling)
            {
                SkinAttachmentTransformGroupHandler.Inst.AddTransformAttachmentForResolve(this);
            }
        }
        
        public void QueueForResolve()
        {
            if (!common.explicitScheduling)
            {
                Debug.LogErrorFormat("Tried to call QueueForResolve for SkinAttachmentTransform {0} but explicit scheduling not enabled. Skipping", name);
                return;
            }
            UpdateAttachedState();
            if (common.hasValidState)
            {
                SkinAttachmentTransformGroupHandler.Inst.AddTransformAttachmentForResolve(this);
            }
        }


        public Renderer GetTargetRenderer()
        {
            return common.currentTarget;
        }
        
        public bool CanAttach()
        {
            return common.IsAttachmentTargetValid() && !IsAttached;
        }


        void UpdateAttachedState()
        {
            common.UpdateAttachedState(this);
        }

        static void BuildDataAttachSubject(ref SkinAttachmentPose[] posesArray, ref SkinAttachmentItem[] itemsArray,
            Matrix4x4 resolveMatrix, in MeshInfo targetBakeData, in PoseBuildSettings settings, ref int itemOffset, ref int poseOffset)
        {
            SkinAttachmentDataBuilder.BuildDataAttachTransform(ref posesArray, ref itemsArray, resolveMatrix, targetBakeData,
                settings, itemOffset, poseOffset, out var itemCount, out var poseCount);

            itemOffset += itemCount;
            poseOffset += poseCount;
        }

        bool BakeAttachmentPoses(SkinAttachmentComponentCommon.PoseBakeOutput bakeOutput)
        {
            return BakeAttachmentPoses(ref bakeOutput.items, ref bakeOutput.poses);
        }

        bool BakeAttachmentPoses(ref SkinAttachmentItem[] items, ref SkinAttachmentPose[] poses)
        {
            if (!SkinAttachmentSystem.Inst.GetAttachmentTargetMeshInfo(common.attachmentTarget,
                    out MeshInfo attachmentTargetBakeData, common.readbackTargetMeshWhenBaking, common.explicitBakeMesh))
                return false;

            Matrix4x4 subjectToTarget =
                common.attachmentTarget.transform.worldToLocalMatrix * transform.localToWorldMatrix;

            //for now deactive this path as it's not yet stable
            PoseBuildSettings poseBuildParams = new PoseBuildSettings
            {
                onlyAllowPoseTrianglesContainingAttachedPoint = false
            };
            
            int currentPoseOffset = 0;
            int currentItemOffset = 0;

            BuildDataAttachSubject(ref poses, ref items, subjectToTarget, attachmentTargetBakeData, poseBuildParams, ref currentItemOffset,
                ref currentPoseOffset);

            return true;
        }

        public bool ValidateBakedData()
        {
            bool validData = common.ValidateBakedData();

            
            return validData;
        }
        
        public void AfterSkinAttachmentGroupResolve(CommandBuffer cmd, Vector3[] positionsCPU,
            GraphicsBuffer positionsGPU,
            int indexInGroup)
        {
            currentOffsetToTransformGroup = indexInGroup;
            currentPositionsBufferGPU = positionsGPU;

            if (common.schedulingMode == SkinAttachmentComponentCommon.SchedulingMode.CPU)
            {
                transform.position = positionsCPU[indexInGroup];
            }

            
        }

        public void AfterAllAttachmentsInQueueResolved()
        {
            if (readbackTransformFromGPU && common.schedulingMode == SkinAttachmentComponentCommon.SchedulingMode.GPU)
            {
                if (currentPositionsBufferGPU != null)
                {
                    NativeArray<Vector3> readBackBuffer = new NativeArray<Vector3>(1, Allocator.Persistent);
	
                    var readbackRequest =
                        AsyncGPUReadback.RequestIntoNativeArray(ref readBackBuffer, currentPositionsBufferGPU, TransformAttachmentBufferStride, currentOffsetToTransformGroup * TransformAttachmentBufferStride);
                    readbackRequest.WaitForCompletion();

                    Vector3 pos = readBackBuffer[0];
                    transform.position = pos;
                    readBackBuffer.Dispose();

                }
            }
            
            onSkinAttachmentTransformResolved?.Invoke();
        }

        public bool GetSkinAttachmentPosesAndItem(out SkinAttachmentPose[] poses, out SkinAttachmentItem item)
        {
            if (common.bakedPoses == null || common.bakedItems == null)
            {
                poses = null;
                item = default;
                return false;
            }

            poses = common.bakedPoses;
            item = common.bakedItems[0];
            return true;
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
                var checksumProperty = serializedObject.FindProperty(nameof(common)).FindPropertyRelative(nameof(common.checkSum));
                PrefabUtility.RevertPropertyOverride(checksumProperty, UnityEditor.InteractionMode.AutomatedAction);
            }

            serializedObject.ApplyModifiedProperties();
#endif
        }
        
        #if UNITY_EDITOR
        public void OnDrawGizmosSelected()
        {
            common.DrawDebug(this);
            
            if (!IsAttached)
            {
                DrawDistanceToClosestPointSphere();
            }
        }

        public void DrawDistanceToClosestPointSphere()
        {
            // draw sphere with radius to closest vertex
            var closestDist = float.MaxValue;
            var closestNode = -1;

            Renderer target = common.attachmentTarget;
            if (target == null) return;

            if (!SkinAttachmentSystem.Inst.GetAttachmentTargetMeshInfo(target,
                    out MeshInfo attachmentTargetBakeData, common.readbackTargetMeshWhenBaking, common.explicitBakeMesh))
                return;

            var targetLocalPos = target.transform.InverseTransformPoint(this.transform.position);
            if (attachmentTargetBakeData.meshVertexBSP.FindNearest(ref closestDist, ref closestNode,
                    ref targetLocalPos))
            {
                Gizmos.matrix = target.transform.localToWorldMatrix;

                var r = targetLocalPos - attachmentTargetBakeData.meshBuffers.vertexPositions[closestNode];
                var d = Vector3.Dot(attachmentTargetBakeData.meshBuffers.vertexNormals[closestNode], r);
                var c = (d >= 0.0f) ? Color.cyan : Color.magenta;

                Gizmos.color = Color.Lerp(Color.clear, c, 0.25f);
                Gizmos.DrawSphere(targetLocalPos, Mathf.Sqrt(closestDist));

                Gizmos.color = Color.Lerp(Color.clear, c, 0.75f);
                Gizmos.DrawLine(targetLocalPos, attachmentTargetBakeData.meshBuffers.vertexPositions[closestNode]);

                foreach (var triangle in attachmentTargetBakeData.meshAdjacency.vertexTriangles[closestNode])
                {
                    int _0 = triangle * 3;
                    int v0 = attachmentTargetBakeData.meshBuffers.triangles[_0];
                    int v1 = attachmentTargetBakeData.meshBuffers.triangles[_0 + 1];
                    int v2 = attachmentTargetBakeData.meshBuffers.triangles[_0 + 2];

                    Gizmos.DrawLine(attachmentTargetBakeData.meshBuffers.vertexPositions[v0],
                        attachmentTargetBakeData.meshBuffers.vertexPositions[v1]);
                    Gizmos.DrawLine(attachmentTargetBakeData.meshBuffers.vertexPositions[v1],
                        attachmentTargetBakeData.meshBuffers.vertexPositions[v2]);
                    Gizmos.DrawLine(attachmentTargetBakeData.meshBuffers.vertexPositions[v2],
                        attachmentTargetBakeData.meshBuffers.vertexPositions[v0]);
                }
            }
        }
#endif
        
    }
}