Shader "Hidden/DigitalHuman/NormalBufferBlur"
{
	Properties
	{
		[HideInInspector] _StencilBit("_StencilBit", Int) = 4
	}

	HLSLINCLUDE
		#pragma target 4.5

		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
		#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
		#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"
		#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Decal/DecalUtilities.hlsl"

		TEXTURE2D_X(_InputDepth);

		Texture2D<float> _NormalBufferBlur_Regions;
		Texture2D<float4> _NormalBufferBlur_Decoded;

#if defined(PLATFORM_NEEDS_UNORM_UAV_SPECIFIER) && defined(PLATFORM_SUPPORTS_EXPLICIT_BINDING)
		RW_TEXTURE2D_X(unorm float4, _NormalBuffer) : register(u1);
#else
		RW_TEXTURE2D_X(float4, _NormalBuffer);
#endif

		struct SurfaceAttributes
		{
			float3 positionOS : POSITION;
		};
	
		struct SurfaceVaryings
		{
			float4 positionCS : SV_POSITION;
			UNITY_VERTEX_OUTPUT_STEREO
		};

		struct FullScreenAttributes
		{
			uint vertexID : SV_VertexID;
			UNITY_VERTEX_INPUT_INSTANCE_ID
		};
	
		struct FullScreenVaryings
		{
			float4 positionCS : SV_POSITION;
			float2 texcoord : TEXCOORD0;
			UNITY_VERTEX_OUTPUT_STEREO
		};

		SurfaceVaryings VertSurface(SurfaceAttributes input)
		{
			SurfaceVaryings output;
			UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
			float depthOffset = 0.0002;// world space offset towards camera
			float3 positionWS = TransformObjectToWorld(input.positionOS) - GetViewForwardDir() * depthOffset;
			output.positionCS = TransformWorldToHClip(positionWS);
			return output;
		}

		float FragSurface_Mark(SurfaceVaryings input) : SV_Target0
		{
			return 0.0;// 0 == interior
		}

		FullScreenVaryings VertFullScreen(FullScreenAttributes input)
		{
			FullScreenVaryings output;
			UNITY_SETUP_INSTANCE_ID(input);
			UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
			output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
			output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
			return output;
		}

		float FragFullScreen_CopyDepth(FullScreenVaryings input) : SV_Depth
		{
			UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
			uint2 positionSS = uint2(input.positionCS.xy);
			return _InputDepth[COORD_TEXTURE2D_X(positionSS)].x;
		}

		[earlydepthstencil]
		float4 FragFullScreen_Decode(FullScreenVaryings input) : SV_Target0
		{
			uint2 positionSS = uint2(input.positionCS.xy);
			float4 normalBuffer = _NormalBuffer[COORD_TEXTURE2D_X(positionSS)];

			NormalData normalData;
			DecodeFromNormalBuffer(normalBuffer, uint2(0, 0), normalData);
			return float4(normalData.normalWS, normalData.perceptualRoughness);
		}

		[earlydepthstencil]
		void FragFullScreen_BlurAndEncode(FullScreenVaryings input)
		{
			int2 positionSS = input.positionCS.xy;

			const int MAX_EXT = 20;// PACKAGETODO variable?
			const float RCP_MAX_EXT = 1.0 / float(MAX_EXT);

			// binary search for edge of blur region
			int ext_hi = MAX_EXT;
			int ext_lo = 0;
			while (ext_hi > ext_lo)
			{
				int mid = (ext_lo + ext_hi + 1) >> 1;

				float4 outside = float4(
					_NormalBufferBlur_Regions[positionSS + int2(-mid,    0)],
					_NormalBufferBlur_Regions[positionSS + int2( mid,    0)],
					_NormalBufferBlur_Regions[positionSS + int2(   0, -mid)],
					_NormalBufferBlur_Regions[positionSS + int2(   0,  mid)]);

				if (any(outside))// if any of the samples are outside the blur region
					ext_hi = mid - 1;
				else
					ext_lo = mid;
			}
			int ext = ext_lo;

			// gaussian blur within region
			//
			//                             x^2 + y^2
			//                1          - ---------
			// G(x,y) = ------------ exp   2 sigma^2
			//          2 PI sigma^2
#define ADJUST_SIGMA
#ifdef ADJUST_SIGMA
			const float sigma = ext / 1.5 + FLT_EPS;
#else
			const float sigma = 0.5;
#endif
			const float rcp_2sigmasq = -1.0 / (2.0 * sigma * sigma);
			const float rcp_2PIsigmasq = 1.0 / (2 * 3.14159265 * sigma * sigma);

#ifndef ADJUST_SIGMA
			float rcp_ext = 1.0 / (ext + FLT_EPS);
			float rcp_ext_sq = rcp_ext * rcp_ext;
#endif
			float4 sum = 0.0;
			float gsum = 0.0;

			for (int dy = -ext; dy <= ext; dy++)
			{
				for (int dx = -ext; dx <= ext; dx++)
				{
#ifdef ADJUST_SIGMA
					float dd = (dx * dx + dy * dy);
#else
					float dd = (dx * dx + dy * dy) * rcp_ext_sq;
#endif
					float g = rcp_2PIsigmasq * exp(dd * rcp_2sigmasq);
					sum += g * _NormalBufferBlur_Decoded[positionSS + int2(dx, dy)];
					gsum += g;
				}
			}

			sum.xyz = normalize(sum.xyz);
			sum.a = sum.a / gsum;

//#define STRICT_TEARLINE
#ifdef STRICT_TEARLINE
			float4 raw = _NormalBufferBlur_Decoded[positionSS];

			const float tearlineRoughness = 0.3;
			float tearlineDotN = abs(dot(sum.xyz, raw.xyz));
			float tearline = 2.0 * saturate((1.0 - pow(tearlineDotN, 16)) - 0.5);
#else
			float tearline = ext * RCP_MAX_EXT;
#endif

			// write to gbuffer
			NormalData normalData;
			normalData.normalWS = sum.xyz;
			normalData.perceptualRoughness = sum.a;// lerp(sum.a, 0.2, tearline);

			float4 normalBuffer;
			EncodeIntoNormalBuffer(normalData, uint2(0, 0), normalBuffer);
			_NormalBuffer[COORD_TEXTURE2D_X(positionSS)] = normalBuffer;
		}
	ENDHLSL

	SubShader
	{
		Tags { "RenderPipeline" = "HDRenderPipeline" }

		Cull Off
		Blend Off

		Pass// == 0
		{
			Name "CopyDepth"

			ZTest Always
			ZWrite On

			HLSLPROGRAM
				#pragma vertex VertFullScreen
				#pragma fragment FragFullScreen_CopyDepth
			ENDHLSL
		}

		Pass// == 1
		{
			Name "Mark"

			ZTest LEqual
			ZWrite Off

			Stencil
			{
				WriteMask [_StencilBit]
				ReadMask 0
				Ref [_StencilBit]
				Comp Equal
				Pass Replace
			}

			HLSLPROGRAM
				#pragma vertex VertSurface
				#pragma fragment FragSurface_Mark
			ENDHLSL
		}

		Pass// == 2
		{
			Name "Decode"

			ZTest Always
			ZWrite Off

			Stencil
			{
				ReadMask [_StencilBit]
				Ref [_StencilBit]
				Comp Equal
				Pass Keep
			}

			HLSLPROGRAM
				#pragma vertex VertFullScreen
				#pragma fragment FragFullScreen_Decode
			ENDHLSL
		}

		Pass// == 3
		{
			Name "BlurAndEncode"

			ZTest Always
			ZWrite Off

			Stencil
			{
				WriteMask [_StencilBit]
				ReadMask [_StencilBit]
				Ref [_StencilBit]
				Comp Equal
				Pass Zero
			}

			HLSLPROGRAM
				#pragma vertex VertFullScreen
				#pragma fragment FragFullScreen_BlurAndEncode
			ENDHLSL
		}
	}
}
