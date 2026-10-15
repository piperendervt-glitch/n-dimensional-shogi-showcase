Shader "Hidden/Showcase/DimensionalTransition"
{
    HLSLINCLUDE
    #pragma target 4.5
    #pragma vertex Vert
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
    TEXTURE2D_X(_DimensionSource);
    float4 _DimensionSize;
    float4 _DimensionState;
    float3 ReadCurrent(float2 pixel)
    { return LOAD_TEXTURE2D_X(_DimensionSource,(uint2)clamp(pixel,0,_DimensionSize.xy-1)).rgb; }
    float4 CopyCurrent(Varyings input):SV_Target
    { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);return float4(LOAD_TEXTURE2D_X(_DimensionSource,(uint2)input.positionCS.xy).rgb,1); }
    float4 SuspendTime(Varyings input):SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float strength=_DimensionState.x;
        float2 uv=input.positionCS.xy*_DimensionSize.zw;
        float2 centered=(uv-.5)*float2(_DimensionSize.x/_DimensionSize.y,1);
        float radius=length(centered);
        // A single slow expanding pressure wave, never a repeating flash.
        float waveRadius=lerp(.02,1.5,saturate(_DimensionState.y));
        float wave=exp(-pow((radius-waveRadius)*23,2))*(1-saturate(_DimensionState.y));
        float edge=smoothstep(.18,.72,radius);
        float2 offset=centered*(wave*2.2+edge*.6)*strength;
        float3 base=ReadCurrent(input.positionCS.xy+offset);
        float3 spectral=base;
        spectral.r=ReadCurrent(input.positionCS.xy+offset+centered*strength*1.5).r;
        spectral.b=ReadCurrent(input.positionCS.xy+offset-centered*strength*1.5).b;
        float luminance=dot(spectral,float3(.2126,.7152,.0722));
        float3 frozen=lerp(spectral,luminance*float3(.73,.87,1.10),.72);
        frozen*=1-.23*edge;
        frozen+=wave*float3(.15,.27,.34);
        // Fine stress contours stay in the border, leaving the section readable.
        float contour=pow(saturate(cos(radius*56-_DimensionState.z*2)),28)*edge*.018;
        frozen+=contour*float3(.45,.65,1);
        return float4(lerp(base,frozen,strength),1);
    }
    ENDHLSL
    SubShader
    {
        Tags {"RenderPipeline"="HDRenderPipeline"}
        Pass
        {
            Name "Copy current final color"
            ZWrite Off ZTest Always Cull Off Blend Off
            HLSLPROGRAM
            #pragma fragment CopyCurrent
            ENDHLSL
        }
        Pass
        {
            Name "Time suspension"
            ZWrite Off ZTest Always Cull Off Blend Off
            HLSLPROGRAM
            #pragma fragment SuspendTime
            ENDHLSL
        }
    }
    Fallback Off
}
