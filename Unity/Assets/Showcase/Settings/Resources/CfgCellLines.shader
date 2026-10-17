Shader "Showcase/CFG1/CellLines"
{
    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            struct Attributes { float3 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; };
            Varyings Vert(Attributes input)
            { Varyings o; o.positionCS=TransformWorldToHClip(TransformObjectToWorld(input.positionOS));o.color=input.color;return o; }
            float4 Frag(Varyings input) : SV_Target { return input.color; }
            ENDHLSL
        }
    }
}
