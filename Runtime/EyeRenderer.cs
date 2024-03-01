using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Serialization;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace Unity.DemoTeam.DigitalHuman
{
    [ExecuteAlways, RequireComponent(typeof(Renderer))]
    public class EyeRenderer : MonoBehaviour
    {
        private Renderer rnd;
        private MaterialPropertyBlock rndProps;

        // http://hyperphysics.phy-astr.gsu.edu/hbase/vision/eyescal.html
        const float IOR_HUMAN_AQUEOUS_HUMOR = 1.336f;
        const float IOR_HUMAN_CORNEA = 1.376f;
        const float IOR_HUMAN_LENS = 1.406f;

        // https://journals.lww.com/optvissci/Abstract/1995/10000/Refractive_Index_and_Osmolality_of_Human_Tears_.4.aspx
        const float IOR_HUMAN_TEARS = 1.33698f;

        [Header("Geometry")] public float geometryRadius = 0.014265f;
        public Vector3 geometryOrigin = Vector3.zero;
        public Vector3 geometryAngle = Vector3.zero;

        [Header("Sclera")] [FormerlySerializedAs("eyeLitIORSclera"), Range(1.0f, 2.0f)]
        public float scleraIOR = IOR_HUMAN_TEARS;

        [Range(0.0f, 360.0f)] public float scleraTextureRoll = 0.0f;

        [Header("Cornea")] public bool corneaCrossSectionEditMode = false;

        [FormerlySerializedAs("eyeCorneaRadiusStart")]
        public float corneaCrossSection = 0.01325f;

        [FormerlySerializedAs("eyeIrisPlaneOffset")]
        public float corneaCrossSectionIrisOffset = 0.002f;

        [FormerlySerializedAs("eyeCorneaLimbusDarkeningOffset")]
        public float corneaCrossSectionFadeOffset = 0.00075f;

        [FormerlySerializedAs("eyeLitIORCornea"), Range(1.0f, 2.0f)]
        public float corneaIOR = IOR_HUMAN_CORNEA;

        [FormerlySerializedAs("eyeCorneaIndexOfRefraction"), Range(1.0f, 2.0f)]
        public float corneaIORIrisRay = 1.3f;

        [FormerlySerializedAs("eyeCorneaSmoothness"), Range(0.0f, 1.0f)]
        public float corneaSmoothness = 0.917f;

        [FormerlySerializedAs("eyeCorneaSSSScale"), Range(0.0f, 1.0f)]
        public float corneaSSS = 0.0f;

        [Range(0.000001f, 2)] public float limbalRingPower = 1.0f;

        [Header("Iris")] [FormerlySerializedAs("eyeIrisBentLighting")]
        public bool irisRefractedLighting = true;

        [Tooltip(
            "Enables constant step (being the specified iris offset) along the refracted ray below the cornea cross section, rather than stepping all the way to the plane defined by the iris offset. Effectively curves the iris towards the refracted ray.")]
        public bool irisRefractedOffset = true;

        [Header("Pupil")] [FormerlySerializedAs("eyePupilOffset")]
        public Vector2 pupilUVOffset = new Vector2(0.002f, 0.016f);

        [FormerlySerializedAs("eyePupilDiameter")]
        public float pupilUVDiameter = 0.095f;

        [FormerlySerializedAs("eyePupilFalloff")]
        public float pupilUVFalloff = 0.015f;

        [FormerlySerializedAs("eyePupilScale"), Range(0.001f, 2.2f)]
        public float pupilScale = 1.0f;

        public float pupilScaleUVMin = 0.5f;
        public float pupilScaleUVMax = 2.2f;


        [Header("Occlusion")] [FormerlySerializedAs("eyeAsgPower"), Range(1e-1f, 128.0f)]
        public float asgPower = 10.0f;

        [FormerlySerializedAs("eyeAsgThreshold"), Range(1e-7f, 1e-1f)]
        public float asgThreshold = 1e-7f;

        [FormerlySerializedAs("eyeAsgModulateAlbedo"), Range(0.0f, 1.0f)]
        public float asgModulateAlbedo = 0.65f;

        public bool useExtraASGLayerOnIris = false;

        [VisibleIf("useExtraASGLayerOnIris", true), Range(1e-1f, 128.0f)]
        public float asgPowerIris = 10.0f;

        [VisibleIf("useExtraASGLayerOnIris", true), Range(0.0f, 1.0f)]
        public float asgModulateAlbedoIris = 0.0f;
#if UNITY_2021_2_OR_NEWER
        [Tooltip("This is only required if the asg markers are driven by attachment system that is configured to execute on GPU")]
        public LegacySkinAttachmentTarget markerAttachmentTarget;
#endif

        [FormerlySerializedAs("eyePolygonContainer")]
        public Transform asgMarkerPolygon;


        public bool useSeparateIrisTextures = false;

        [VisibleIf("useSeparateIrisTextures", true)]
        public float irisUVExtraScale = 1.0f;

        public enum ConeMapping
        {
            ObjectSpaceMean,
            ClosingAxisSpaceSplit,
        }

        public struct ConeData
        {
            public Vector3 osMarkerL;
            public Vector3 osMarkerR;
            public Vector3 osMarkerT;
            public Vector3 osMarkerB;
            public Vector3 closingPlaneOrigin;
            public Vector3 closingPlanePosTop;
            public Vector3 closingPlanePosBottom;
            public Vector3 openingPosLeft;
            public Vector3 openingPosRight;
        }

        [Space] public ConeMapping coneMapping = ConeMapping.ClosingAxisSpaceSplit;

        [VisibleIf("coneMapping", ConeMapping.ClosingAxisSpaceSplit)]
        public float coneOriginOffset = 1.0f;

        [VisibleIf("coneMapping", ConeMapping.ClosingAxisSpaceSplit)]
        public Vector2 coneScale = Vector2.one;

        [VisibleIf("coneMapping", ConeMapping.ClosingAxisSpaceSplit)]
        public Vector3 coneBias = Vector3.zero;

        public bool coneDebug = false;
        private ConeData coneDebugData;

        private EyeOcclusionParameters eyeOcclusionParameters;

        public delegate void EyeRendererAboutToSetMaterialCallback(MaterialPropertyBlock block);
        public event EyeRendererAboutToSetMaterialCallback EyeMaterialUpdateEvent;

#if UNITY_2021_2_OR_NEWER
        private List<LegacySkinAttachment> markerAttachments = new List<LegacySkinAttachment>();
        
        private static ComputeShader s_computeASGParamsShader = null;
        private static int s_computeASGParamsKernel;
        private bool isHookedToSkinAttachmentTarget = false;
        private List<SkinAttachmentTransform> markerAttachments2= new List<SkinAttachmentTransform>();
        private int numberOfAttachment2callbacks = -1;
#endif
        
        private Vector3[] markerPositions = new Vector3[4];
        private ComputeBuffer asgParametersBuffer = null;
        private bool cpuOcclusionParametersValid = true;

        void Awake()
        {
            rnd = GetComponent<Renderer>();
            rndProps = new MaterialPropertyBlock();
        }
 
        private void OnEnable()
        {
#if UNITY_2021_2_OR_NEWER
            if (s_computeASGParamsShader == null)
            {
                s_computeASGParamsShader = Resources.Load<ComputeShader>("EyeRendererCS");
                s_computeASGParamsKernel = s_computeASGParamsShader.FindKernel("CalculateEyeOcclusionParameters");
            }
           
#endif
            int structSize;
            unsafe
            {
                structSize = sizeof(EyeOcclusionParameters);
            }

            asgParametersBuffer = new ComputeBuffer(1, structSize, ComputeBufferType.Structured);
            asgParametersBuffer.name = "Asg Parameters Buffer";
        }

        private void OnDisable()
        {
#if UNITY_2021_2_OR_NEWER
            HookIntoSkinAttachments(false);
#endif
            asgParametersBuffer.Dispose();
            asgParametersBuffer = null;
        }

        // https://mynameismjp.wordpress.com/2016/10/09/sg-series-part-2-spherical-gaussians-101/
        float AsgSharpnessFromThreshold(float epsilon, float amplitude, float power, float cosTheta)
        {
            // amplitude * e^(pow(sharpness * (cosTheta - 1), power)) = epsilon
            // e^(pow(sharpness * (cosTheta - 1), power)) = epsilon / amplitude
            // pow(sharpness * (cosTheta - 1), power) = log(epsilon / amplitude)
            // pow(sharpness * (cosTheta - 1), power) = log(epsilon) - log(amplitude)
            // sharpness * (cosTheta - 1) = pow(log(epsilon) - log(amplitude), 1.0 / power)
            // sharpness = pow(log(epsilon) - log(amplitude), 1.0 / power) / (cosTheta - 1)
            return 0.5f * Mathf.Pow(-Mathf.Log(epsilon) - Mathf.Log(amplitude), 1.0f / power) / -(cosTheta - 1.0f);
        }

        void AsgParameterUpdateCPU()
        {
            Vector3 osMarkerL = (1.1f * geometryRadius) * Vector3.Normalize(Vector3.forward + Vector3.left);
            Vector3 osMarkerR = (1.1f * geometryRadius) * Vector3.Normalize(Vector3.forward + Vector3.right);
            Vector3 osMarkerT = (1.1f * geometryRadius) * Vector3.Normalize(Vector3.forward + 0.35f * Vector3.up);
            Vector3 osMarkerB = (1.1f * geometryRadius) * Vector3.Normalize(Vector3.forward + 0.35f * Vector3.down);

            if (asgMarkerPolygon != null && asgMarkerPolygon.childCount == 4)
            {
                //       1
                //   .-´   `-.
                // 0           2
                //   `-.   .-´
                //       3

                osMarkerL = this.transform.InverseTransformPoint(markerPositions[0]);
                osMarkerR = this.transform.InverseTransformPoint(markerPositions[2]);
                osMarkerT = this.transform.InverseTransformPoint(markerPositions[1]);
                osMarkerB = this.transform.InverseTransformPoint(markerPositions[3]);
            }

            coneDebugData.osMarkerL = osMarkerL;
            coneDebugData.osMarkerR = osMarkerR;
            coneDebugData.osMarkerT = osMarkerT;
            coneDebugData.osMarkerB = osMarkerB;

            float cosThetaTangent = 0.0f;
            float cosThetaBitangent = 0.0f;

            switch (coneMapping)
            {
                case ConeMapping.ObjectSpaceMean:
                {
                    eyeOcclusionParameters.asgOriginOS = geometryOrigin;
                    eyeOcclusionParameters.asgMeanOS = Vector3.Normalize(
                        Vector3.Normalize(osMarkerT) +
                        Vector3.Normalize(osMarkerR) +
                        Vector3.Normalize(osMarkerB) +
                        Vector3.Normalize(osMarkerL)
                    );
                    eyeOcclusionParameters.asgBitangentOS = Vector3.Cross(eyeOcclusionParameters.asgMeanOS,
                        Vector3.Normalize(osMarkerR - osMarkerL));
                    eyeOcclusionParameters.asgTangentOS = Vector3.Cross(eyeOcclusionParameters.asgBitangentOS,
                        eyeOcclusionParameters.asgMeanOS);

                    float cosThetaMeanToLeft =
                        Vector3.Dot(Vector3.Normalize(osMarkerL), eyeOcclusionParameters.asgMeanOS);
                    float cosThetaMeanToRight =
                        Vector3.Dot(Vector3.Normalize(osMarkerR), eyeOcclusionParameters.asgMeanOS);
                    float cosThetaMeanToTop =
                        Vector3.Dot(Vector3.Normalize(osMarkerT), eyeOcclusionParameters.asgMeanOS);
                    float cosThetaMeanToBottom =
                        Vector3.Dot(Vector3.Normalize(osMarkerB), eyeOcclusionParameters.asgMeanOS);

                    cosThetaTangent = (cosThetaMeanToLeft + cosThetaMeanToRight) * 0.5f;
                    cosThetaBitangent = (cosThetaMeanToTop + cosThetaMeanToBottom) * 0.5f;
                }
                    break;

                case ConeMapping.ClosingAxisSpaceSplit:
                {
                    var asgPolygonRot = Quaternion.Euler(coneBias.y, coneBias.x, coneBias.z);
                    osMarkerL = asgPolygonRot * osMarkerL;
                    osMarkerR = asgPolygonRot * osMarkerR;
                    osMarkerT = asgPolygonRot * osMarkerT;
                    osMarkerB = asgPolygonRot * osMarkerB;

                    var closingPlaneNormal = Vector3.Normalize(osMarkerR - osMarkerL);
                    var closingPlaneOrigin = Vector3.ProjectOnPlane(osMarkerL, closingPlaneNormal);

                    var closingPlanePosTop = Vector3.ProjectOnPlane(osMarkerT, closingPlaneNormal) - closingPlaneOrigin;
                    var closingPlanePosBottom =
                        Vector3.ProjectOnPlane(osMarkerB, closingPlaneNormal) - closingPlaneOrigin;
                    var closingPlaneDirTop = Vector3.Normalize(closingPlanePosTop);
                    var closingPlaneDirBottom = Vector3.Normalize(closingPlanePosBottom);

                    var closingPlaneForward = Vector3.Normalize(closingPlaneDirTop + closingPlaneDirBottom);
                    {
                        closingPlaneOrigin -= closingPlaneForward * (0.01f * coneOriginOffset);
                        //TODO pick an origin that sends the resulting forward vector through the original origin in the closing plane

                        closingPlanePosTop = Vector3.ProjectOnPlane(osMarkerT, closingPlaneNormal) - closingPlaneOrigin;
                        closingPlanePosBottom =
                            Vector3.ProjectOnPlane(osMarkerB, closingPlaneNormal) - closingPlaneOrigin;
                        closingPlaneDirTop = Vector3.Normalize(closingPlanePosTop);
                        closingPlaneDirBottom = Vector3.Normalize(closingPlanePosBottom);

                        closingPlaneForward = Vector3.Normalize(closingPlaneDirTop + closingPlaneDirBottom);
                    }

                    var openingPosLeft = (osMarkerL - closingPlaneOrigin);
                    var openingPosRight = (osMarkerR - closingPlaneOrigin);
                    var openingDirLeft = Vector3.Normalize(openingPosLeft);
                    var openingDirRight = Vector3.Normalize(openingPosRight);

                    var closingPlaneAltitude = coneScale.y * 0.5f * Mathf.Deg2Rad *
                                               Vector3.Angle(closingPlaneDirTop, closingPlaneDirBottom);
                    var closingPlaneAzimuth = coneScale.x * 0.5f * Mathf.Deg2Rad *
                                              Vector3.Angle(openingDirLeft, openingDirRight);

                    coneDebugData.closingPlaneOrigin = closingPlaneOrigin;
                    coneDebugData.closingPlanePosTop = closingPlanePosTop;
                    coneDebugData.closingPlanePosBottom = closingPlanePosBottom;
                    coneDebugData.openingPosLeft = openingPosLeft;
                    coneDebugData.openingPosRight = openingPosRight;

                    eyeOcclusionParameters.asgOriginOS = closingPlaneOrigin;
                    eyeOcclusionParameters.asgMeanOS = closingPlaneForward;
                    eyeOcclusionParameters.asgTangentOS = closingPlaneNormal;
                    eyeOcclusionParameters.asgBitangentOS = Vector3.Normalize(
                        Vector3.Cross(closingPlaneForward, closingPlaneNormal));

                    cosThetaTangent = Mathf.Cos(closingPlaneAzimuth);
                    cosThetaBitangent = Mathf.Cos(closingPlaneAltitude);
                }
                    break;
            } // switch (coneMapping)

            eyeOcclusionParameters.asgSharpness = new Vector2(
                AsgSharpnessFromThreshold(asgThreshold, 1.0f, asgPower, cosThetaTangent),
                AsgSharpnessFromThreshold(asgThreshold, 1.0f, asgPower, cosThetaBitangent)
                );

            eyeOcclusionParameters.asgThresholdScaleBias = new Vector2(1.0f / (1.0f - asgThreshold), -asgThreshold / (1.0f - asgThreshold));

        }
#if UNITY_2021_2_OR_NEWER

        void HookIntoSkinAttachments(bool val)
        {
            if (markerAttachmentTarget != null)
            {
                markerAttachmentTarget.afterGPUAttachmentWorkCommitted -= UpdateAfterAttachmentResolve;
                if (val && markerAttachmentTarget.executeOnGPU)
                {
                    markerAttachmentTarget.afterGPUAttachmentWorkCommitted += UpdateAfterAttachmentResolve;
                }

                isHookedToSkinAttachmentTarget = markerAttachmentTarget.executeOnGPU && val;
            }
            else
            {
                if (markerAttachments2.Count == 4)
                {
                    if (val)
                    {
                        numberOfAttachment2callbacks = 0;
                    }
                    foreach (var att in markerAttachments2)
                    {
                        att.onSkinAttachmentTransformResolved -= UpdateAfterAttachmentResolve2;
                        if (val)
                        {
                            att.onSkinAttachmentTransformResolved += UpdateAfterAttachmentResolve2;
                        }
                    }
                }
            }
            
        }
        
        void ASGParameterUpdateGPU()
        {
            CommandBuffer cmd = CommandBufferPool.Get("Calculate Eye Occlusion Parameters");

            GraphicsBuffer attachmentBuffer = null;
            int[] attachmentOffsets = new int[4];
            if (markerAttachmentTarget != null)
            {
                var attachmentTarget = markerAttachmentTarget;
                attachmentBuffer = attachmentTarget.TransformAttachmentGPUPositionBuffer;
            
                int attachmentBufferStride = attachmentTarget.TransformAttachmentGPUPositionBufferStride;

                attachmentOffsets[0] = markerAttachments[0].TransformAttachmentGPUBufferIndex * attachmentBufferStride;
                attachmentOffsets[1] = markerAttachments[1].TransformAttachmentGPUBufferIndex * attachmentBufferStride;
                attachmentOffsets[2] = markerAttachments[2].TransformAttachmentGPUBufferIndex * attachmentBufferStride;
                attachmentOffsets[3] = markerAttachments[3].TransformAttachmentGPUBufferIndex * attachmentBufferStride;
            }
            else
            {
                int attachmentBufferStride = SkinAttachmentTransform.TransformAttachmentBufferStride;

                attachmentBuffer = markerAttachments2[0].CurrentGPUPositionsBuffer;
                
                attachmentOffsets[0] = markerAttachments2[0].CurrentOffsetIntoGPUPositionsBuffer * attachmentBufferStride;
                attachmentOffsets[1] = markerAttachments2[1].CurrentOffsetIntoGPUPositionsBuffer * attachmentBufferStride;
                attachmentOffsets[2] = markerAttachments2[2].CurrentOffsetIntoGPUPositionsBuffer * attachmentBufferStride;
                attachmentOffsets[3] = markerAttachments2[3].CurrentOffsetIntoGPUPositionsBuffer * attachmentBufferStride;
            }
            

            if (attachmentBuffer == null) return;
            
            cmd.SetComputeBufferParam(s_computeASGParamsShader, s_computeASGParamsKernel, "_ASGMarkerPositionsBuffer", attachmentBuffer);
            cmd.SetComputeIntParams(s_computeASGParamsShader, "_ASGMarkerBufferOffsets", attachmentOffsets);
            cmd.SetComputeBufferParam(s_computeASGParamsShader, s_computeASGParamsKernel, "_EyeOcclusionParametersOutput", asgParametersBuffer);
            
            cmd.SetComputeMatrixParam(s_computeASGParamsShader, "_WorldToLocalMat", transform.worldToLocalMatrix);
            cmd.SetComputeIntParam(s_computeASGParamsShader, "_ConeMappingType",
                coneMapping == ConeMapping.ObjectSpaceMean ? 0 : 1);
            
            var asgPolygonRot = Quaternion.Euler(coneBias.y, coneBias.x, coneBias.z);
            cmd.SetComputeVectorParam(s_computeASGParamsShader, "_ASGConeRotation", new Vector4(asgPolygonRot.x, asgPolygonRot.y, asgPolygonRot.z, asgPolygonRot.w));
            cmd.SetComputeVectorParam(s_computeASGParamsShader, "_GeometryOrigin", geometryOrigin);
            cmd.SetComputeFloatParam(s_computeASGParamsShader, "_ASGConeOffset", coneOriginOffset);
            cmd.SetComputeVectorParam(s_computeASGParamsShader, "_ASGConeScale", coneScale);

            cmd.SetComputeFloatParam(s_computeASGParamsShader, "_ASGThreshold", asgThreshold);
            cmd.SetComputeFloatParam(s_computeASGParamsShader, "_ASGPower", asgPower);

            cmd.DispatchCompute(s_computeASGParamsShader, s_computeASGParamsKernel, 1, 1, 1);
            
            Graphics.ExecuteCommandBuffer(cmd);
            
            CommandBufferPool.Release(cmd);
        }
        
        void ReadbackDebugDataFromGPUForDebug()
        {
            if(asgMarkerPolygon != null && markerAttachments.Count == 4 && asgMarkerPolygon.childCount == 4)
            {
                GraphicsBuffer attachmentBuffer = markerAttachmentTarget.TransformAttachmentGPUPositionBuffer;
                
                if (attachmentBuffer == null) return;
                
                NativeArray<Vector3> readBackBuffer = new NativeArray<Vector3>(
                    attachmentBuffer.count,
                    Allocator.Persistent);

                var readbackRequest = AsyncGPUReadback.RequestIntoNativeArray(ref readBackBuffer, attachmentBuffer);
                readbackRequest.WaitForCompletion();

                for (int i = 0; i < 4; ++i)
                {
                    int index = markerAttachments[i].TransformAttachmentGPUBufferIndex;
                    Vector3 markerWSPos = readBackBuffer[index];
                    asgMarkerPolygon.GetChild(i).position = markerWSPos;

                }

                AsgParameterUpdateCPU();
                
                readBackBuffer.Dispose();


            }
            
        }
        
        void UpdateAfterAttachmentResolve()
        {
            if (s_computeASGParamsShader != null && markerAttachmentTarget != null && markerAttachmentTarget.executeOnGPU)
            {
                UpdateEyeRenderer();
            }
        }
        
        void UpdateAfterAttachmentResolve2()
        {
            if (++numberOfAttachment2callbacks == 4)
            {
                UpdateEyeRenderer();
            }
        }
        
        
#endif

        void GatherSkinAttachments()
        {
#if UNITY_2021_2_OR_NEWER
            markerAttachments.Clear();
            markerAttachments2.Clear();

            if (markerAttachmentTarget != null)
            {
                for (int i = 0; i < 4; ++i)
                {
                    var child = asgMarkerPolygon.GetChild(i);
                    markerPositions[i] = child.position;
                
                    LegacySkinAttachment attachment;
                    if (markerAttachmentTarget != null)
                    {
                        if (child.TryGetComponent(out attachment))
                        {
                            if (attachment.target == markerAttachmentTarget)
                            {
                                markerAttachments.Add(attachment);
                            }
                        }
                    }
                }
            }
            else
            {
                Renderer attachmentTarget = null;
                for (int i = 0; i < 4; ++i)
                {
                    var child = asgMarkerPolygon.GetChild(i);
                    markerPositions[i] = child.position;
                
                    SkinAttachmentTransform attachment;

                    if (child.TryGetComponent(out attachment))
                    {
                        if (attachment.common.schedulingMode == SkinAttachmentComponentCommon.SchedulingMode.GPU && (attachmentTarget == null || attachmentTarget == attachment.GetTargetRenderer()))
                        {
                            markerAttachments2.Add(attachment);
                            attachmentTarget = attachment.GetTargetRenderer();
                        }
                    }
                }
            }
            
#endif
        }
        
        void SetupASGParameters()
        {
#if UNITY_2021_2_OR_NEWER
            bool calculateOnGPU = s_computeASGParamsShader != null && 
                                  ((markerAttachmentTarget != null && markerAttachmentTarget.executeOnGPU && markerAttachments.Count == 4)
                                   || (markerAttachments2.Count == 4));
            cpuOcclusionParametersValid = !calculateOnGPU;
            if (calculateOnGPU)
            {
                ASGParameterUpdateGPU();
            }
#else
            for (int i = 0; i < 4; ++i)
            {
                var child = asgMarkerPolygon.GetChild(i);
                markerPositions[i] = child.position;
            }
            const bool calculateOnGPU = false;
#endif

            if(!calculateOnGPU)
            {
                AsgParameterUpdateCPU();
                NativeArray<EyeOcclusionParameters> nativeArray =
                    new NativeArray<EyeOcclusionParameters>(1, Allocator.Temp);
                nativeArray[0] = eyeOcclusionParameters;
                asgParametersBuffer.SetData(nativeArray);
                nativeArray.Dispose();
            }
        }

        void LateUpdate()
        {
            GatherSkinAttachments();
            
#if UNITY_2021_2_OR_NEWER
            
            bool calculateOnGPU = s_computeASGParamsShader != null && 
                                  ((markerAttachmentTarget != null && markerAttachmentTarget.executeOnGPU && markerAttachments.Count == 4)
                                   || (markerAttachments2.Count == 4));
            
            if (!calculateOnGPU)
            {
                UpdateEyeRenderer();
            }
            else
            {
                isHookedToSkinAttachmentTarget = false;
                HookIntoSkinAttachments(true);
                
            }
#else
            UpdateEyeRenderer();
#endif

        }

        void UpdateEyeRenderer()
        {
            SetupASGParameters();

            if (rndProps == null)
                rndProps = new MaterialPropertyBlock();

            rnd.GetPropertyBlock(rndProps);
            {
                var geometryLookRotation = Quaternion.Euler(geometryAngle);

                rndProps.SetFloat("_EyeGeometryRadius", geometryRadius);
                rndProps.SetVector("_EyeGeometryOrigin", geometryOrigin);
                rndProps.SetVector("_EyeGeometryForward", Vector3.Normalize(geometryLookRotation * Vector3.forward));
                rndProps.SetVector("_EyeGeometryRight", Vector3.Normalize(geometryLookRotation * Vector3.right));
                rndProps.SetVector("_EyeGeometryUp", Vector3.Normalize(geometryLookRotation * Vector3.up));

                if (corneaCrossSectionEditMode && Application.isEditor)
                {
                    rndProps.SetFloat("_EyeCorneaCrossSection", 1e+7f);
                    rndProps.SetFloat("_EyeCorneaCrossSectionIrisOffset", 0.0f);
                    rndProps.SetFloat("_EyeCorneaCrossSectionFadeOffset", 0.0f);
                    rndProps.SetFloat("_EyeLimbalRingPower", limbalRingPower);
                }
                else
                {
                    rndProps.SetFloat("_EyeCorneaCrossSection", corneaCrossSection);
                    rndProps.SetFloat("_EyeCorneaCrossSectionIrisOffset", corneaCrossSectionIrisOffset);
                    rndProps.SetFloat("_EyeCorneaCrossSectionFadeOffset",
                        Mathf.Max(0.0f, corneaCrossSectionFadeOffset));
                    rndProps.SetFloat("_EyeLimbalRingPower", limbalRingPower);
                }

                rndProps.SetFloat("_EyeCorneaIOR", corneaIOR);
                rndProps.SetFloat("_EyeCorneaIORIrisRay", corneaIORIrisRay);
                rndProps.SetFloat("_EyeCorneaSmoothness", corneaSmoothness);
                rndProps.SetFloat("_EyeCorneaSSS", corneaSSS);

                rndProps.SetFloat("_EyeIrisRefractedLighting", irisRefractedLighting ? 1 : 0);
                rndProps.SetFloat("_EyeIrisRefractedOffset", irisRefractedOffset ? 1 : 0);

                rndProps.SetVector("_EyePupilUVOffset", pupilUVOffset);
                rndProps.SetFloat("_EyePupilUVDiameter", pupilUVDiameter);
                rndProps.SetFloat("_EyePupilUVFalloff", pupilUVFalloff);
                rndProps.SetFloat("_EyePupilScale", pupilScale);
                rndProps.SetVector("_EyePupilScaleUVMinMax", new Vector4(pupilScaleUVMin, pupilScaleUVMax, 0.0f, 0.0f));

                rndProps.SetFloat("_EyeScleraIOR", scleraIOR);

                rndProps.SetFloat("_EyeAsgPower", asgPower);
                rndProps.SetFloat("_EyeAsgModulateAlbedo", asgModulateAlbedo);
                rndProps.SetBuffer("_EyeOcclusionParameters", asgParametersBuffer);


                float irisASGBlend = useExtraASGLayerOnIris ? 1 : 0;
                Vector4 asgIrisParams = new Vector4(irisASGBlend, asgPowerIris, 0,
                    asgModulateAlbedoIris * irisASGBlend);
                rndProps.SetVector("_EyeAsgIrisParams", asgIrisParams);

                rndProps.SetVector("_ScleraTextureRollSinCos",
                    new Vector4(Mathf.Sin(Mathf.Deg2Rad * scleraTextureRoll),
                        Mathf.Cos(Mathf.Deg2Rad * scleraTextureRoll), 0.0f, 0.0f));

                if (useSeparateIrisTextures)
                {
                    float angle = Mathf.Acos(corneaCrossSection / geometryRadius);

                    float sphericalEyeIrisRadius = Mathf.Sin(angle) * geometryRadius;

                    float s = irisUVExtraScale * geometryRadius / sphericalEyeIrisRadius;

                    rndProps.SetVector("_EyeIrisUVScaleBias", new Vector4(s, s, -0.5f * s + 0.5f, -0.5f * s + 0.5f));
                }
                else
                {
                    rndProps.SetVector("_EyeIrisUVScaleBias", new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
                }
            }

            EyeMaterialUpdateEvent?.Invoke(rndProps);


            rnd.SetPropertyBlock(rndProps);
        }

        

        void OnDrawGizmos()
        {
            if (!coneDebug)
                return;
#if UNITY_2021_2_OR_NEWER
            //readback from GPU to be able to draw the gizmos 
            if (!cpuOcclusionParametersValid)
            {
                ReadbackDebugDataFromGPUForDebug();
            }
#endif
            var oldColor = Gizmos.color;
            var oldMatrix = Gizmos.matrix;
            {
                Gizmos.matrix = transform.localToWorldMatrix;

                // cone markers
                Gizmos.color = Color.white;
                Gizmos.DrawRay(Vector3.zero, coneDebugData.osMarkerT);
                Gizmos.DrawRay(Vector3.zero, coneDebugData.osMarkerR);
                Gizmos.DrawRay(Vector3.zero, coneDebugData.osMarkerB);
                Gizmos.DrawRay(Vector3.zero, coneDebugData.osMarkerL);

                // cone closing axis
                if (coneMapping == ConeMapping.ClosingAxisSpaceSplit)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawRay(coneDebugData.closingPlaneOrigin, coneDebugData.closingPlanePosTop);
                    Gizmos.DrawRay(coneDebugData.closingPlaneOrigin, coneDebugData.closingPlanePosBottom);
                    Gizmos.DrawRay(coneDebugData.closingPlaneOrigin, coneDebugData.openingPosLeft);
                    Gizmos.DrawRay(coneDebugData.closingPlaneOrigin, coneDebugData.openingPosRight);
                }

                // asg frame
                Gizmos.color = Color.Lerp(Color.yellow, Color.red, 0.5f);
                Gizmos.DrawRay(eyeOcclusionParameters.asgOriginOS,
                    (1.5f * geometryRadius) * eyeOcclusionParameters.asgMeanOS);
                Gizmos.DrawRay(eyeOcclusionParameters.asgOriginOS,
                    (1.5f * geometryRadius) * eyeOcclusionParameters.asgBitangentOS);
                Gizmos.DrawRay(eyeOcclusionParameters.asgOriginOS,
                    (1.5f * geometryRadius) * eyeOcclusionParameters.asgTangentOS);
            }

            Gizmos.color = oldColor;
            Gizmos.matrix = oldMatrix;
        }
    }
}