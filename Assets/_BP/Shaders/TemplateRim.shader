Shader "BP/TemplateRim"
{
    // Obrys počítaný z úhlu mezi normálou a pohledem (Fresnel).
    //
    // Proti nafouknuté kopii s hloubkovou maskou má zásadní výhodu:
    // funguje stejně na hranatých i hladkých tvarech. Koule a torus,
    // na kterých maska selhávala, se chovají úplně stejně jako kvádr.
    // Navíc je to jediný průchod, bez řešení pořadí front.

    Properties
    {
        _RimColor     ("Barva obrysu", Color) = (1,1,1,1)
        _RimPower     ("Ostrost obrysu", Range(0.5, 12)) = 4
        _RimIntensity ("Síla obrysu", Range(0, 3)) = 1.4
        _FillAlpha    ("Alfa výplně", Range(0, 0.5)) = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Rim"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _RimColor;
                float  _RimPower;
                float  _RimIntensity;
                float  _FillAlpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewWS     : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);

                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS   = normalize(TransformObjectToWorldNormal(v.normalOS));
                o.viewWS     = normalize(GetCameraPositionWS() - posWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float ndv = saturate(abs(dot(normalize(i.normalWS), normalize(i.viewWS))));

                // U okraje tvaru jde ndv k nule -> rim k jedné.
                float rim = pow(1.0 - ndv, _RimPower) * _RimIntensity;

                float a = saturate(_FillAlpha + rim) * _RimColor.a;
                return half4(_RimColor.rgb, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
