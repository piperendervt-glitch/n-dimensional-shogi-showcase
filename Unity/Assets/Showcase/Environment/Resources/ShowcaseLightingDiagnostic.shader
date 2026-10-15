Shader "Hidden/Showcase/LightingDiagnostic"
{
    HLSLINCLUDE
    #pragma target 4.5
    #pragma vertex Vert
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Shadow/HDShadowManager.cs.hlsl"
    StructuredBuffer<HDShadowData> _HDShadowDatas;
    TEXTURE2D_X(_EnvironmentDiagnostic);
    int _EnvironmentDiagnosticMode;
    float4 ReadLightingBuffer(Varyings input):SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        uint2 pixel=(uint2)input.positionCS.xy;
        float visibility=1;
        if(_EnvironmentDiagnosticMode==1)
        {
            // HDRP MaterialEvaluation.hlsl stores occlusion, not visibility, in R.
            visibility=1-LOAD_TEXTURE2D_X(_AmbientOcclusionTexture,pixel).r;
        }
        else if(_EnvironmentDiagnosticMode==2)
        {
            // Exact HDRP LightLoopDef.hlsl layout: low 24 bits = light mask,
            // upper eight bits = fade. Black is a contact occlusion, white unoccluded.
            uint packed=LOAD_TEXTURE2D_X(_ContactShadowTexture,pixel).r;
            uint mask=packed&0x00ffffffu;
            float fade=(float)(packed>>24)/255.0;
            visibility=1-(mask!=0?fade:0);
        }
        return float4(visibility.xxx,1);
    }
    float4 PresentLightingBuffer(Varyings input):SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        return LOAD_TEXTURE2D_X(_EnvironmentDiagnostic,(uint2)input.positionCS.xy);
    }
    float4 ReadShadowMetadata(Varyings input):SV_Target
    {
        uint2 pixel=(uint2)input.positionCS.xy;
        DirectionalLightData light=_DirectionalLightDatas[0];
        int index=light.shadowIndex+(int)pixel.y;
        if(light.shadowIndex<0)return float4(-1,-1,-1,-1);
        HDShadowData shadow=_HDShadowDatas[index];
        if(pixel.x==0)return float4(index,shadow.worldTexelSize,shadow.normalBias,shadow.worldTexelSize*shadow.normalBias);
        if(pixel.x==1)return shadow.shadowMapSize;
        if(pixel.x==2)return float4(asint(shadow.shadowFilterParams0.y),asint(shadow.shadowFilterParams0.z),shadow.shadowFilterParams0.x,shadow.shadowFilterParams0.w);
        if(pixel.x==3)return shadow.dirLightPCSSParams0;
        if(pixel.x==4)return shadow.dirLightPCSSParams1;
        if(pixel.x==5)return shadow.zBufferParam;
        if(pixel.x==6)return shadow.proj;
        return float4(light.color,light.shadowDimmer);
    }
    ENDHLSL
    SubShader
    {
        Tags {"RenderPipeline"="HDRenderPipeline"}
        Pass
        {
            Name "Copy actual HDRP lighting buffer"
            ZWrite Off ZTest Always Cull Off Blend Off
            HLSLPROGRAM
            #pragma fragment ReadLightingBuffer
            ENDHLSL
        }
        Pass
        {
            Name "Display diagnostic without tone mapping"
            ZWrite Off ZTest Always Cull Off Blend Off
            HLSLPROGRAM
            #pragma fragment PresentLightingBuffer
            ENDHLSL
        }
        Pass
        {
            Name "Read actual directional shadow parameters"
            ZWrite Off ZTest Always Cull Off Blend Off
            HLSLPROGRAM
            #pragma fragment ReadShadowMetadata
            ENDHLSL
        }
    }
    Fallback Off
}
