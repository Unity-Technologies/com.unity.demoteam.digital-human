    Shader "Hidden/DigitalHuman/ColorBufferBlurPass"
{

	HLSLINCLUDE
		#pragma target 4.5

		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
		#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
		#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
		#include "Packages/com.unity.render-pipelines.core\ShaderLibrary\Color.hlsl"

		float _DepthOffset;
		float4 _BlurParams[4];
		Texture2D _BlurMask;
		SamplerState _Blur_Mask_Sampler_trilinear_clamp;

		
	
		struct SurfaceAttributes
		{
			float3 positionOS : POSITION;
			float2 texCoord : TEXCOORD0;
		};

		struct FullScreenVaryings
		{
			float4 positionCS : SV_POSITION;
			float2 texcoord : TEXCOORD0;
			UNITY_VERTEX_OUTPUT_STEREO
		};

		FullScreenVaryings VertFullScreen(SurfaceAttributes input)
		{
			FullScreenVaryings output;
			UNITY_SETUP_INSTANCE_ID(input);
			UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
			float3 positionWS = TransformObjectToWorld(input.positionOS) - GetViewForwardDir() * _DepthOffset;
			output.positionCS = TransformWorldToHClip(positionWS);
			output.texcoord = input.texCoord;
			return output;
		}

		float GetBlurStrength(float2 uv)
		{
#ifdef USE_BLUR_MASK_TEXTURE
			return SAMPLE_TEXTURE2D(_BlurMask, _Blur_Mask_Sampler_trilinear_clamp, uv).x * _BlurParams[0].w;
#else
			int indexHor = uv.x < 0.5f ? 0 : 1;
			int indexVer = uv.y < 0.5f ? 2 : 3;

			float4 blurParams1 = _BlurParams[indexHor];
			float4 blurParams2 = _BlurParams[indexVer];

			float2 d = abs(uv - 0.5f);

			//blur shift
			float2 s = d + float2(blurParams1.z, blurParams2.z);
			//pingpong [0, 0.5]
			s = 0.5f - abs(0.5f - saturate(s - floor(s)));

			//blur width
			float2 blur = 1.f - smoothstep(0.0f, float2(blurParams1.y, blurParams2.y), abs(s - 0.5f));
			blur.xy *= float2(blurParams1.x, blurParams2.x);

			//lerp between vertical and horizontal
			float t = saturate(d.y / max(d.x, 0.000001f));

			return lerp(blur.x, blur.y, t);
#endif
		}
	
		float3 ColorBlur(float2 screenUV, float2 meshUV)
		{
			return SampleCameraColor(screenUV, GetBlurStrength(meshUV));
		}

		[earlydepthstencil]
		float4 FragFullScreen_Blur(FullScreenVaryings input) : COLOR
		{
			float2 uv = input.positionCS.xy * (_ScreenParams.zw - 1.f); 
			float3 c = ColorBlur(uv, input.texcoord);
			return float4(c, 1.f);
		}

	ENDHLSL

	SubShader
	{
		Tags { "RenderPipeline" = "HDRenderPipeline" }
		Cull Off
		Blend Off
		ZTest LEqual
		ZWrite Off

		Pass
		{
			Name "ColorBlur"
			HLSLPROGRAM
			#pragma multi_compile __ USE_BLUR_MASK_TEXTURE
			#pragma vertex VertFullScreen
			#pragma fragment FragFullScreen_Blur

			ENDHLSL
		}
			}
}
