using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.DemoTeam.DigitalHuman
{
    [GenerateHLSL(needAccessors = false, packingRules = PackingRules.Exact)]
    struct EyeOcclusionParameters
    {
        public Vector4 asgOriginOS;
        public Vector4 asgMeanOS;
        public Vector4 asgTangentOS;
        public Vector4 asgBitangentOS;
        public Vector2 asgSharpness;
        public Vector2 asgThresholdScaleBias;
    }
}