Shader "BP/WireUnlit"
{
    // Prostá konstantní barva bez osvětlení, s průhledností.
    //
    // STEREO: Quest kreslí obě oči jedním průchodem (single-pass instanced).
    // Vlastní shader to musí podporovat explicitně — bez maker níže se
    // geometrie vykreslí POUZE DO LEVÉHO OKA a pravé zůstane prázdné.
    // Je to tichá chyba: v editoru i na screenshotu vypadá všechno správně.
    //
    // Vlastní shader místo URP/Unlit záměrně: URP validátor u standardního
    // materiálu přepisuje nastavení průhlednosti a zápisu hloubky, takže se
    // stav nastavený ze skriptu neudrží.

    Properties
    {
        _Color ("Barva", Color) = (1,1,1,1)
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
            Name "Wire"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return _Color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
