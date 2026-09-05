Shader "BP/DepthMask"
{
    // Zapisuje pouze hloubku, žádnou barvu.
    //
    // Na původním tvaru zakryje vnitřek nafouknuté kopie (BP/OutlineHull),
    // takže z ní zbyde jen okraj. Bez tohohle by z hully byla plná silueta.

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "DepthMask"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ColorMask 0
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    Fallback Off
}
