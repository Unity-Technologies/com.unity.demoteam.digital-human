using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.DemoTeam.DigitalHuman
{
    public static partial class SkinAttachmentSystem
    {
        private static ComputeShader s_resolveAttachmentsCS;
        private static int s_resolveAttachmentsPosKernel = 0;
        private static int s_resolveAttachmentsPosNormalKernel = 0;
        private static int s_resolveAttachmentsPosNormalMovecKernel = 0;

        private static bool s_initialized = false;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
		[RuntimeInitializeOnLoadMethod]
#endif
        static void StaticInitialize()
        {
            if (s_initialized == false)
            {
                if (s_resolveAttachmentsCS == null)
                {
                    s_resolveAttachmentsCS = Resources.Load<ComputeShader>("SkinAttachmentCS");
                }

                s_resolveAttachmentsPosKernel = s_resolveAttachmentsCS.FindKernel("ResolveAttachmentPositions");
                s_resolveAttachmentsPosNormalKernel =
                    s_resolveAttachmentsCS.FindKernel("ResolveAttachmentPositionsNormals");
                s_resolveAttachmentsPosNormalMovecKernel =
                    s_resolveAttachmentsCS.FindKernel("ResolveAttachmentPositionsNormalsMovecs");

                s_initialized = true;
            }
        }

    }
}