// Seurat atlas shader for a LINEAR color-space project, using STRAIGHT alpha.
//
// Premultiplied alpha (Seurat's default) is baked in GAMMA space, but Unity
// filters textures in LINEAR space. That mismatch has no clean fix in a Linear
// project: a premultiplied blend darkens genuine semi-transparent texels
// (overlap "patches"), while un-premultiplying in the shader over-brightens
// bilinear-filtered silhouette edges (bright rims).
//
// The clean fix is straight (non-premultiplied) alpha: bake with
// -premultiply_alpha=false, or un-premultiply the atlas and dilate its color
// into the transparent texels once (both covered in
// README-captura-seurat.md), so a normal sRGB import + straight-alpha blend
// (SrcAlpha, OneMinusSrcAlpha) accumulates correctly in linear space with no
// patches and no bright edges. No per-pixel color math needed.
//
// URP only (Unity 6). Assign to a material with _MainTex = the (straight-alpha)
// Seurat atlas, imported as a normal sRGB color texture with Alpha Is
// Transparency ON.
Shader "Seurat/AlphaBlendedLinearCorrect"
{
	Properties
	{
		_MainTex ("Atlas (straight alpha)", 2D) = "white" {}
	}
	SubShader
	{
		Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
		LOD 100
		Blend SrcAlpha OneMinusSrcAlpha
		Cull Off
		ZWrite Off
		ZTest LEqual
		Pass
		{
			Name "Forward"
			Tags { "LightMode" = "UniversalForward" }
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D(_MainTex);
			SAMPLER(sampler_MainTex);

			CBUFFER_START(UnityPerMaterial)
				float4 _MainTex_ST;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv         : TEXCOORD0;
			};

			struct Varyings
			{
				centroid float2 uv : TEXCOORD0;
				float4 positionHCS : SV_POSITION;
			};

			Varyings vert(Attributes v)
			{
				Varyings o;
				o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				return o;
			}

			float4 frag(Varyings i) : SV_Target
			{
				// sRGB import => rgb is the straight linear color, a is coverage.
				// Straight-alpha blend does a*rgb + (1-a)*bg in linear = correct.
				return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
			}
			ENDHLSL
		}
	}
	FallBack Off
}
