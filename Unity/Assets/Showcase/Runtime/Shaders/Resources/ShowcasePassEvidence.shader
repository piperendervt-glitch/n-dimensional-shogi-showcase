Shader "Hidden/Showcase/PassEvidence"
{
    HLSLINCLUDE
    #pragma target 4.5
    #pragma vertex Vert
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"
    TEXTURE2D_X(_ShowcaseCaptureColor);
    TEXTURE2D_X_FLOAT(_ShowcaseCaptureDepth);
    TEXTURE2D_X(_ShowcaseCaptureNormal);
    TEXTURE2D(_ShadowmapCascadeAtlas);
    TEXTURECUBE(_ShowcaseCaptureCube);
    TEXTURE2D(_ShowcaseCapturePlanar);
    SAMPLER(sampler_ShowcaseCaptureCube);
    int _ShowcaseCaptureMode;
    float4 _ShowcaseCaptureSize;
    float4 CaptureBuffer(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        uint2 p=(uint2)input.positionCS.xy;
        if(_ShowcaseCaptureMode==10)return float4(LOAD_TEXTURE2D(_ShowcaseCapturePlanar,p).rgb,1);
        if(_ShowcaseCaptureMode==3)
        {
            uint w,h;_ShadowmapCascadeAtlas.GetDimensions(w,h);
            uint2 source=min((uint2)((float2(p)+0.5)*_ShowcaseCaptureSize.zw*float2(w,h)),uint2(w-1,h-1));
            return float4(LOAD_TEXTURE2D(_ShadowmapCascadeAtlas,source).r,w,h,1);
        }
        if(_ShowcaseCaptureMode>=4)
        {
            float2 uv=(float2(p)+0.5)*_ShowcaseCaptureSize.zw*2-1;
            int face=_ShowcaseCaptureMode-4;float3 d;
            if(face==0)d=float3(1,-uv.y,-uv.x);
            else if(face==1)d=float3(-1,-uv.y,uv.x);
            else if(face==2)d=float3(uv.x,1,uv.y);
            else if(face==3)d=float3(uv.x,-1,-uv.y);
            else if(face==4)d=float3(uv.x,-uv.y,1);
            else d=float3(-uv.x,-uv.y,-1);
            return float4(SAMPLE_TEXTURECUBE_LOD(_ShowcaseCaptureCube,sampler_ShowcaseCaptureCube,d,0).rgb,1);
        }
        if(_ShowcaseCaptureMode==0)return float4(LOAD_TEXTURE2D_X(_ShowcaseCaptureColor,p).rgb,1);
        float rawDepth=LOAD_TEXTURE2D_X(_ShowcaseCaptureDepth,p).r;
        if(_ShowcaseCaptureMode==1)
        {
            float metres=LinearEyeDepth(rawDepth,_ZBufferParams);
            return float4(metres,metres,metres,1);
        }
        if(rawDepth==UNITY_RAW_FAR_CLIP_VALUE)return 0;
        NormalData normalData;
        DecodeFromNormalBuffer(LOAD_TEXTURE2D_X(_ShowcaseCaptureNormal,p),normalData);
        return float4(normalData.normalWS,1);
    }
    ENDHLSL
    SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" }
        Pass
        {
            Name "Read actual camera buffers"
            ZWrite Off ZTest Always Cull Off Blend Off
            HLSLPROGRAM
            #pragma fragment CaptureBuffer
            ENDHLSL
        }
    }
    Fallback Off
}
