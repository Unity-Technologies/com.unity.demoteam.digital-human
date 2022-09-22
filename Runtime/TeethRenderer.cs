using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.DemoTeam.DigitalHuman
{
    [ExecuteAlways, RequireComponent(typeof(Renderer))]
    public class TeethRenderer : MonoBehaviour
    {
        private Renderer rnd;
        private Material rndMat;
        private MaterialPropertyBlock rndProps;

        private const uint vertexFixedBit6 = 1 << 5;
        private const uint vertexFixedMask = vertexFixedBit6;
        private const int vertexLimit = 32; // should match limit in LitTeeth.shader
        private Vector3[] vertexData = new Vector3[0];

        [Range(0.0f, 1.0f)] public float litPotentialMin = 0.0f;
        [Range(0.0f, 1.0f)] public float litPotentialMax = 1.0f;
        [Min(1.0f)] public float litPotentialFalloff = 4.0f;

#if UNITY_2021_2_OR_NEWER
        [Tooltip("This is only required if the asg markers are driven by attachment system that is configured to execute on GPU")]
        public SkinAttachmentTarget markerAttachmentTarget;
#endif

        public Attenuation mode;

        public enum Attenuation
        {
            None,
            Linear,
            SkyPolygon,
        }

        [EditableIf("mode", Attenuation.Linear)]
        public Transform linearBack;

        [EditableIf("mode", Attenuation.Linear)]
        public Transform linearFront;

        [EditableIf("mode", Attenuation.SkyPolygon)]
        public Transform skyPolygonContainer;

        [EditableIf("mode", Attenuation.SkyPolygon)]
        public Transform skyPolygonDebugSphere;

        public bool showDebugWireframe;

        private ComputeBuffer occlusionMarkersBuffer;
        private const int occlusionMarkersBufferStride = sizeof(float) * 3;
#if UNITY_2021_2_OR_NEWER
        private ComputeBuffer occlusionMarkerIndicesBuffer;
        private List<SkinAttachment> gatheredAttachments = new List<SkinAttachment>();
        private bool isHookedToSkinAttachmentTarget = false;
        private bool cpuOcclusionParametersValid = true;
#endif
        void Awake()
        {
            rnd = GetComponent<Renderer>();
#if UNITY_EDITOR
            rndMat = rnd.sharedMaterial;
#else
			rndMat = rnd.material;
#endif
            rndProps = new MaterialPropertyBlock();
        }

        private void OnDisable()
        {
            if (occlusionMarkersBuffer != null)
            {
                occlusionMarkersBuffer.Dispose();
                occlusionMarkersBuffer = null;
            }
#if UNITY_2021_2_OR_NEWER
            if (occlusionMarkerIndicesBuffer != null)
            {
                occlusionMarkerIndicesBuffer.Dispose();
                occlusionMarkerIndicesBuffer = null;
            }

            HookIntoSkinAttachmentTarget(false);
#endif
        }

        void PrepareKeyword(string keyword, bool enabled)
        {
#if UNITY_EDITOR
            if (rndMat != rnd.sharedMaterial)
                rndMat = rnd.sharedMaterial;
#endif

            if (rndMat.IsKeywordEnabled(keyword) != enabled)
            {
                if (enabled)
                    rndMat.EnableKeyword(keyword);
                else
                    rndMat.DisableKeyword(keyword);
            }
        }


        void PrepareVertexData(int vertexCount)
        {
            if (vertexData.Length != vertexCount)
            {
                vertexData = new Vector3[vertexCount];
            }
        }

        void PrepareGPUResources(bool needsIndirection)
        {
            if (occlusionMarkersBuffer != null && occlusionMarkersBuffer.count != vertexData.Length)
            {
                occlusionMarkersBuffer.Dispose();
                occlusionMarkersBuffer = null;
            }

            if (occlusionMarkersBuffer == null)
            {
                occlusionMarkersBuffer =
                    new ComputeBuffer(vertexData.Length, occlusionMarkersBufferStride, ComputeBufferType.Raw);
            }

#if UNITY_2021_2_OR_NEWER
            if (markerAttachmentTarget != null && markerAttachmentTarget.executeOnGPU)
            {
                if (occlusionMarkerIndicesBuffer != null &&
                    (!needsIndirection || (occlusionMarkerIndicesBuffer.count != vertexData.Length)))
                {
                    occlusionMarkerIndicesBuffer.Dispose();
                    occlusionMarkerIndicesBuffer = null;
                }

                if (occlusionMarkerIndicesBuffer == null && needsIndirection)
                {
                    occlusionMarkerIndicesBuffer =
                        new ComputeBuffer(vertexData.Length, sizeof(uint), ComputeBufferType.Default);
                }
            }

#endif
        }

#if UNITY_2021_2_OR_NEWER

        void ReadbackGPUDataForDebug()
        {
            if(gatheredAttachments.Count > 0 && markerAttachmentTarget != null && markerAttachmentTarget.executeOnGPU)
            {
                ComputeBuffer attachmentBuffer = markerAttachmentTarget.TransformAttachmentGPUPositionBuffer;

                if (attachmentBuffer == null) return;
                
                NativeArray<Vector3> readBackBuffer = new NativeArray<Vector3>(
                    attachmentBuffer.count,
                    Allocator.Persistent);

                var readbackRequest = AsyncGPUReadback.RequestIntoNativeArray(ref readBackBuffer, attachmentBuffer);
                readbackRequest.WaitForCompletion();

                for (int i = 0; i < gatheredAttachments.Count; ++i)
                {
                    Vector3 markerWSPos = readBackBuffer[gatheredAttachments[i].TransformAttachmentGPUBufferIndex];
                    gatheredAttachments[i].transform.position = markerWSPos;
                }
                readBackBuffer.Dispose();
            }
        }
        
        void HookIntoSkinAttachmentTarget(bool enabled)
        {
            if (markerAttachmentTarget != null)
            {
                markerAttachmentTarget.afterGPUAttachmentWorkCommitted -= UpdateAfterAttachmentResolve;
                if (enabled && markerAttachmentTarget.executeOnGPU)
                {
                    markerAttachmentTarget.afterGPUAttachmentWorkCommitted += UpdateAfterAttachmentResolve;
                }

                isHookedToSkinAttachmentTarget = markerAttachmentTarget.executeOnGPU && enabled;
            }
            
        }
        
        void GatherAttachmentMarkers(Attenuation attenuation)
        {
            gatheredAttachments.Clear();
            SkinAttachment attachmentOut;
            switch (attenuation)
            {
                case Attenuation.Linear:

                    if (linearFront.TryGetComponent(out attachmentOut))
                    {
                        gatheredAttachments.Add(attachmentOut);
                    }

                    if (linearBack.TryGetComponent(out attachmentOut))
                    {
                        gatheredAttachments.Add(attachmentOut);
                    }

                    break;

                case Attenuation.SkyPolygon:
                    for (int i = 0; i < Mathf.Min(vertexLimit, skyPolygonContainer.childCount); ++i)
                    {
                        if (skyPolygonContainer.GetChild(i).TryGetComponent(out attachmentOut))
                        {
                            gatheredAttachments.Add(attachmentOut);
                        }
                    }

                    break;
            }
        }
        
        void UpdateAfterAttachmentResolve()
        {
            if (markerAttachmentTarget != null && markerAttachmentTarget.executeOnGPU)
            {
                UpdateTeethRendererParameters();
            }
        }
#endif

        void LateUpdate()
        {
#if UNITY_2021_2_OR_NEWER
            
            if (markerAttachmentTarget == null || !markerAttachmentTarget.executeOnGPU)
            {
                UpdateTeethRendererParameters();
            }
            else
            {
                if (!isHookedToSkinAttachmentTarget)
                {
                    HookIntoSkinAttachmentTarget(true);
                }
            }
            
#else
            UpdateTeethRendererParameters();
#endif

        }

        void UpdateTeethRendererParameters()
        {
            var outputAttn = mode;
            var outputSize = 0;

            switch (outputAttn)
            {
                case Attenuation.None:
                    break;

                case Attenuation.Linear:
                    if (linearFront == null || linearBack == null)
                        outputAttn = Attenuation.None;
                    else
                        outputSize = 6;
                    break;

                case Attenuation.SkyPolygon:
                    if (skyPolygonContainer == null || skyPolygonContainer.childCount < 3)
                        outputAttn = Attenuation.None;
                    else
                        outputSize = Mathf.Min(vertexLimit, skyPolygonContainer.childCount);
                    break;
            }

            var outputFixedBit = outputSize > 0 ? (1u << (outputSize - 1)) : 0u;
            var outputFixedSize = (vertexFixedMask & outputFixedBit) != 0;

            PrepareKeyword("TEETH_ATTN_NONE", outputAttn == Attenuation.None);
            PrepareKeyword("TEETH_ATTN_LINEAR", outputAttn == Attenuation.Linear);
            PrepareKeyword("TEETH_ATTN_SKYPOLYGON", outputAttn == Attenuation.SkyPolygon);

            PrepareKeyword("TEETH_DATA_FIXED_6", outputFixedSize && outputSize == 6);
            PrepareKeyword("TEETH_DATA_VARIABLE_32", !outputFixedSize);

            if (outputFixedSize)
                PrepareVertexData(outputSize);
            else
                PrepareVertexData(vertexLimit);

            ComputeBuffer occlusionMarkers = null;
            ComputeBuffer attachmentIndicesBuffer = null;
            int occlusionMarkersStride = 0;
            bool useGPUAttachmentData = false;
            

            switch (outputAttn)
            {
                case Attenuation.Linear:
                    vertexData[0] = linearFront.position;
                    vertexData[1] = linearBack.position;
                    break;

                case Attenuation.SkyPolygon:
                    for (int i = 0; i != outputSize; i++)
                    {
                        vertexData[i] = skyPolygonContainer.GetChild(i).position;
                    }

                    break;
            }

#if UNITY_2021_2_OR_NEWER
            GatherAttachmentMarkers(outputAttn);
            useGPUAttachmentData = gatheredAttachments.Count > 0 && outputAttn != Attenuation.None &&
                                   markerAttachmentTarget != null && markerAttachmentTarget.executeOnGPU;
#endif
            
            PrepareGPUResources(useGPUAttachmentData);
            
#if UNITY_2021_2_OR_NEWER
            cpuOcclusionParametersValid = !useGPUAttachmentData; 
            if (useGPUAttachmentData)
            {
                NativeArray<int> attachmentIndices =
                    new NativeArray<int>(gatheredAttachments.Count, Allocator.Temp);
                for (int i = 0; i < gatheredAttachments.Count; ++i)
                {
                    var attachment = gatheredAttachments[i];
                    var attachmentTarget = attachment.target;
                    attachmentIndices[i] = attachment.TransformAttachmentGPUBufferIndex;
                    occlusionMarkers = attachmentTarget.TransformAttachmentGPUPositionBuffer;
                    occlusionMarkersStride = attachmentTarget.TransformAttachmentGPUPositionBufferStride;
                }

                occlusionMarkerIndicesBuffer.SetData(attachmentIndices);
                attachmentIndices.Dispose();
                attachmentIndicesBuffer = occlusionMarkerIndicesBuffer;
            }
            else
#endif
            {
                occlusionMarkersStride = occlusionMarkersBufferStride;
                occlusionMarkers = occlusionMarkersBuffer;

                occlusionMarkersBuffer.SetData(vertexData);
            }

            
            
            if (rndProps == null)
                rndProps = new MaterialPropertyBlock();

            rnd.GetPropertyBlock(rndProps);
            {
                rndProps.SetVector("_TeethParams",
                    new Vector4(litPotentialMin, litPotentialMax, litPotentialFalloff));
                rndProps.SetInt("_TeethVertexDataStride", occlusionMarkersStride);
                rndProps.SetBuffer("_TeethVertexData", occlusionMarkers);
                rndProps.SetBuffer("_TeethVertexDataIndices", attachmentIndicesBuffer);
                rndProps.SetInt("_TeethVertexCount", outputSize);
                rndProps.SetInt("_UseTeethVertexDataIndirection", useGPUAttachmentData ? 1 : 0);
            }
            rnd.SetPropertyBlock(rndProps);

            //DebugSpherical();
        }

        void OnDrawGizmos()
        {
            if (!showDebugWireframe)
                return;
#if UNITY_2021_2_OR_NEWER
            //readback from GPU to be able to draw the gizmos 
            if (!cpuOcclusionParametersValid)
            {
                ReadbackGPUDataForDebug();
            }
#endif
            Gizmos.color = Color.yellow;
            Gizmos.matrix = Matrix4x4.identity;

            switch (mode)
            {
                case Attenuation.Linear:
                    if (linearFront != null || linearBack != null)
                    {
                        int vertexCount = 2;
                        if (vertexCount > vertexData.Length)
                            break;

                        Gizmos.DrawLine(vertexData[0], vertexData[1]);
                    }

                    break;

                case Attenuation.SkyPolygon:
                    if (skyPolygonContainer != null)
                    {
                        int vertexCount = skyPolygonContainer.childCount;
                        if (vertexCount > vertexLimit)
                            vertexCount = vertexLimit;
                        if (vertexCount > vertexData.Length)
                            vertexCount = vertexData.Length;
                        if (vertexCount > 0)
                        {
                            for (int i = 1; i != vertexCount; i++)
                            {
                                Gizmos.DrawLine(vertexData[i - 1], vertexData[i]);
                            }

                            Gizmos.DrawLine(vertexData[vertexCount - 1], vertexData[0]);
                        }

                        if (skyPolygonDebugSphere != null)
                        {
                            var origin = skyPolygonDebugSphere.position;
                            var radius = skyPolygonDebugSphere.localScale.x * 0.5f;

                            for (int i = 0; i != vertexCount; i++)
                            {
                                Vector3 v0 = vertexData[i];
                                Vector3 v1 = vertexData[(i + 1) % vertexCount];
                                Vector3 A = radius * Vector3.Normalize(v0 - origin);
                                Vector3 B = radius * Vector3.Normalize(v1 - origin);

                                Gizmos_DrawArc(origin, A, B);
                            }
                        }
                    }

                    break;
            }
        }

        void Gizmos_DrawArc(Vector3 O, Vector3 A, Vector3 B, int segments = 100)
        {
            var arcRot = Quaternion.FromToRotation(A, B);
            var preRot = Quaternion.identity;
            var rcpSeg = 1.0f / segments;
            for (int i = 0; i != segments; i++)
            {
                var preDir = preRot * A;
                var curRot = preRot = Quaternion.Slerp(Quaternion.identity, arcRot, (i + 1) * rcpSeg);
                var curDir = curRot * A;
                Gizmos.DrawLine(O + preDir, O + curDir);
            }
        }
    }
}