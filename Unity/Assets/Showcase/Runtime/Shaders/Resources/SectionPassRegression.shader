Shader "Hidden/Showcase/SectionPassRegression"
{
    HLSLINCLUDE
    #pragma target 4.5
    #pragma vertex Vert
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Shadow/HDShadowManager.cs.hlsl"
    StructuredBuffer<HDShadowData> _HDShadowDatas;
    TEXTURE2D(_ShadowmapCascadeAtlas);
    float4 _RegressionBoundsMin;
    float4 _RegressionBoundsMax;
    int _RegressionCascade;
    int _RegressionRoiSize;

    // Same directional world-to-shadow convention as HDRP 17.3
    // HDShadowAlgorithms.hlsl EvalShadow_WorldToShadow / GetTexcoordsAtlas.
    // Coordinates are not saturated: an out-of-cascade footprint is invalid.
    float2 ProjectNative(HDShadowData sd, float3 absoluteWorld, uint2 atlasSize)
    {
        float3 p=GetCameraRelativePositionWS(absoluteWorld);
        float2 light=float2(dot(sd.rot0,p)+sd.pos.x,dot(sd.rot1,p)+sd.pos.y);
        float2 ndc=light*sd.proj.xy;
        return (ndc*.5+.5)*sd.shadowMapSize.xy+sd.atlasOffset*atlasSize;
    }
    void Region(int cascade, out HDShadowData sd, out float4 extent, out int2 origin, out uint2 atlasSize, out float valid)
    {
        _ShadowmapCascadeAtlas.GetDimensions(atlasSize.x,atlasSize.y);
        int first=_DirectionalLightDatas[0].shadowIndex;
        sd=_HDShadowDatas[max(0,first+cascade)];
        float2 lo=1e20,hi=-1e20;
        [unroll] for(int i=0;i<8;i++)
        {
            float3 world=float3((i&1)?_RegressionBoundsMax.x:_RegressionBoundsMin.x,
                               (i&2)?_RegressionBoundsMax.y:_RegressionBoundsMin.y,
                               (i&4)?_RegressionBoundsMax.z:_RegressionBoundsMin.z);
            float2 p=ProjectNative(sd,world,atlasSize);lo=min(lo,p);hi=max(hi,p);
        }
        origin=(int2)floor((lo+hi)*.5)-_RegressionRoiSize/2;
        origin=clamp(origin,int2(0,0),max(int2(0,0),(int2)atlasSize-_RegressionRoiSize));
        float2 cascadeLo=sd.atlasOffset*atlasSize,cascadeHi=cascadeLo+sd.shadowMapSize.xy;
        // The entire historical world AABB projects into the saved native ROI.
        // 32 extra texels include bias/raster boundary slack. No downsampling.
        valid=first>=0&&all(lo>=origin+32)&&all(hi<=origin+_RegressionRoiSize-32)
              &&all(lo>=cascadeLo+2)&&all(hi<=cascadeHi-2)&&all(atlasSize>=_RegressionRoiSize)?1:0;
        extent=float4(lo,hi);
    }
    float ReadNativeShadow(Varyings input):SV_Target
    {
        HDShadowData sd;float4 bounds;int2 origin;uint2 size;float valid;
        Region(_RegressionCascade,sd,bounds,origin,size,valid);
        uint2 p=(uint2)input.positionCS.xy;
        return LOAD_TEXTURE2D(_ShadowmapCascadeAtlas,(uint2)(origin+(int2)p)).r;
    }
    float4 ReadRoiProof(Varyings input):SV_Target
    {
        uint2 p=(uint2)input.positionCS.xy;
        HDShadowData sd;float4 bounds;int2 origin;uint2 size;float valid;
        Region((int)p.y,sd,bounds,origin,size,valid);
        int first=_DirectionalLightDatas[0].shadowIndex;
        if(p.x==0)return float4(size,(int)p.y,first);
        if(p.x==1)return float4(origin,_RegressionRoiSize,valid);
        if(p.x==2)return bounds;
        if(p.x==3)return float4(sd.atlasOffset*size,sd.shadowMapSize.xy);
        if(p.x==4)return float4(sd.worldTexelSize,sd.normalBias,32,valid);
        if(p.x==5)return sd.proj;
        if(p.x==6)return float4(sd.rot0,sd.pos.x);
        if(p.x==7)return float4(sd.rot1,sd.pos.y);
        if(p.x==8)return float4(sd.rot2,sd.pos.z);
        if(p.x==9)return float4(_WorldSpaceCameraPos,1);
        if(p.x==10)return _RegressionBoundsMin;
        return _RegressionBoundsMax;
    }
    ENDHLSL
    SubShader
    {
        Tags {"RenderPipeline"="HDRenderPipeline"}
        Pass
        {
            Name "Native shadow ROI with no resampling"
            ZWrite Off ZTest Always Cull Off Blend Off
            HLSLPROGRAM
            #pragma fragment ReadNativeShadow
            ENDHLSL
        }
        Pass
        {
            Name "Actual GPU shadow transform and ROI coverage proof"
            ZWrite Off ZTest Always Cull Off Blend Off
            HLSLPROGRAM
            #pragma fragment ReadRoiProof
            ENDHLSL
        }
    }
    Fallback Off
}
