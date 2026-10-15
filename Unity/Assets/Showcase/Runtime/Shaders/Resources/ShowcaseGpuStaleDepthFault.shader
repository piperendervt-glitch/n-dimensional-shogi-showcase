Shader "Hidden/Showcase/GpuStaleDepthFault"
{
    HLSLINCLUDE
    #pragma target 4.5
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"
    TEXTURE2D_X_FLOAT(_ShowcaseFaultSourceDepth);
    TEXTURE2D_X(_ShowcaseFaultSourceNormal);
    float4x4 _ShowcaseFaultVP;
    float4x4 _ShowcaseFaultView;
    float4 FaultCopyDepth(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float metres=LinearEyeDepth(LOAD_TEXTURE2D_X(_ShowcaseFaultSourceDepth,(uint2)input.positionCS.xy).r,_ZBufferParams);
        return float4(metres,metres,metres,1);
    }
    float4 FaultCopyNormal(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        uint2 p=(uint2)input.positionCS.xy;
        if(LOAD_TEXTURE2D_X(_ShowcaseFaultSourceDepth,p).r==UNITY_RAW_FAR_CLIP_VALUE)return 0;
        NormalData n;DecodeFromNormalBuffer(LOAD_TEXTURE2D_X(_ShowcaseFaultSourceNormal,p),n);
        return float4(n.normalWS,1);
    }
    struct FaultAttributes { float3 positionOS : POSITION; };
    struct FaultVaryings { float4 positionCS : SV_POSITION; float eyeDepth : TEXCOORD0; };
    FaultVaryings FaultVert(FaultAttributes input)
    {
        FaultVaryings output;
        float4 world=float4(input.positionOS,1);
        output.positionCS=mul(_ShowcaseFaultVP,world);
        output.eyeDepth=-mul(_ShowcaseFaultView,world).z;
        return output;
    }
    float4 FaultDepth(FaultVaryings input) : SV_Target
    {
        clip(input.eyeDepth);
        return float4(input.eyeDepth,input.eyeDepth,input.eyeDepth,1);
    }
    ENDHLSL
    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" }
        Pass
        {
            Name "Copy actual depth to private target"
            ZWrite Off ZTest Always Cull Off Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FaultCopyDepth
            ENDHLSL
        }
        Pass
        {
            Name "Copy actual normal to private target"
            ZWrite Off ZTest Always Cull Off Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FaultCopyNormal
            ENDHLSL
        }
        Pass
        {
            Name "Rasterize historical depth into private copy only"
            ZWrite Off ZTest Always Cull Off
            Blend One One
            BlendOp Min
            HLSLPROGRAM
            #pragma vertex FaultVert
            #pragma fragment FaultDepth
            ENDHLSL
        }
    }
    Fallback Off
}
