// Copyright 2017 Google Inc. All Rights Reserved.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files (the "Software"), to deal in
// the Software without restriction, including without limitation the rights to
// use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
// of the Software, and to permit persons to whom the Software is furnished to do
// so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
// FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
// IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
// CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

Shader "GoogleVR/Seurat/AlphaBlended"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
	}
	// URP SubShader - used automatically when the project uses Universal Render Pipeline (Unity 6 / Meta VR)
	SubShader
	{
		Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
		LOD 100
		Blend SrcAlpha OneMinusSrcAlpha
		Cull Off
		ZWrite Off
		ZTest Always
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

			half4 frag(Varyings i) : SV_Target
			{
				half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
				return col;
			}
			ENDHLSL
		}
	}

	// Built-in Render Pipeline fallback (legacy / non-URP projects)
	SubShader
	{
		Tags { "RenderType"="Opaque" "Queue"="Transparent" }
		LOD 100
		Blend SrcAlpha OneMinusSrcAlpha
		Cull Off
		ZWrite Off
		ZTest Always
		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			
			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0_centroid;
				float4 vertex : SV_POSITION;
			};

			sampler2D _MainTex;
			
			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;

				return o;
			}
			
			half4 frag (v2f i) : SV_Target
			{
				
				half4 col = tex2D(_MainTex, i.uv);
				return col;
			}
			ENDCG
		}
	}
}
