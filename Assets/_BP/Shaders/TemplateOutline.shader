Shader "BP/OutlineHull"
{
    // Nafouknutá kopie tvaru s odříznutými předními stěnami.
    // Sama o sobě dá plnou siluetu; obrys z ní vznikne až ve spojení
    // s materiálem BP/DepthMask na původním tvaru, který zakryje vnitřek.
    //
    // Každý průchod má explicitní LightMode. URP totiž vícepr��chodový shader
    // bez těchto tagů vůbec nevykreslí — na tom předchozí pokus ztroskotal.

    Properties
    {
        _OutlineColor ("Barva obrysu", Color) = (1,1,1,1)
        _OutlineWidth ("Tloušťka obrysu (m)", Range(0.0, 0.05)) = 0.004
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent+1"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "OutlineHull"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;

                // Posun po normále ve world space — tloušťka pak nezávisí
                // na měřítku objektu.
                float3 posWS    = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(v.normalOS));

                o.positionCS = TransformWorldToHClip(posWS + normalWS * _OutlineWidth);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
