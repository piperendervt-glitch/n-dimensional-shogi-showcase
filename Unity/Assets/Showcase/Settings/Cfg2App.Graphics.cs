using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using NDimensionalShogi.Showcase.Environment;

namespace Showcase.Unity.Settings
{
    public sealed partial class Cfg2App
    {
        // Graphics quality is a machine preference like the volumes: kept in PlayerPrefs, out of the settings hash and match records.
        // 高 is the shipped look. 中 and 低 lower the GPU passes that --gpu-ablation measured as the costliest (RENDER_OPTIMIZATION_JA.md);
        // カスタム keeps 高's resolutions and lets each effect be switched on its own. None of this depends on N: the cost that grew
        // with N was the section geometry on the CPU, not rendering.
        enum GraphicsQuality{High,Medium,Low,Custom}
        const string QualityKey="cfg2.graphics-quality",QualityAutoKey="cfg2.graphics-quality-auto",EffectKeyPrefix="cfg2.graphics-effect.";
        static readonly string[] QualityNames={"高","中","低","カスタム"};
        static readonly EnvironmentEffect[] QualityEffects={EnvironmentEffect.SunShadows,EnvironmentEffect.DappledShadows,EnvironmentEffect.Reflections,
            EnvironmentEffect.AmbientOcclusion,EnvironmentEffect.ContactShadows,EnvironmentEffect.Bloom,EnvironmentEffect.DepthOfField};
        static readonly string[] QualityEffectNames={"太陽の影","木漏れ日","反射","環境光の遮蔽（AO）","接地影","ブルーム（光のにじみ）","被写界深度（背景のぼけ）"};
        // 45 fps is the frame cap; a first-start median slower than 38 fps steps the automatic choice down once.
        const float AutoQualitySlowFrame=1f/38;
        int shippedShadowResolution,shippedBlocker,shippedFilter,shippedContactSamples;bool shippedAoFull,shippedAoBilateral,qualityReady,qualityStored;
        GraphicsQuality quality;string qualityReason="";Coroutine qualityCheck;

        void InitializeGraphicsQuality(bool store)
        {
            var sun=rig.SunData;var profile=rig.Volume.profile;
            shippedShadowResolution=sun.shadowResolution.@override;shippedBlocker=sun.dirLightPCSSBlockerSampleCount;shippedFilter=sun.dirLightPCSSFilterSampleCount;
            if(profile.TryGet<ScreenSpaceAmbientOcclusion>(out var ao)){shippedAoFull=ao.fullResolution;shippedAoBilateral=ao.bilateralUpsample;}
            if(profile.TryGet<ContactShadows>(out var contact))shippedContactSamples=contact.sampleCount;
            qualityReady=true;qualityStored=store;
            // Verification runs keep the shipped look and leave this machine's preference alone.
            if(!store){SetQuality(GraphicsQuality.High,"検証実行のため固定");return;}
            if(PlayerPrefs.HasKey(QualityKey)&&System.Enum.TryParse(PlayerPrefs.GetString(QualityKey),out GraphicsQuality saved))
            {SetQuality(saved,PlayerPrefs.GetInt(QualityAutoKey,0)!=0?"自動選択":"手動で選択");return;}
            ChooseQualityAutomatically();
        }
        // Start from the GPU's name and memory, then confirm by measuring frames on whatever screen is showing.
        void ChooseQualityAutomatically()
        {
            var q=QualityFromGpu(SystemInfo.graphicsDeviceName??"",SystemInfo.graphicsMemorySize);
            StoreQuality(q,true);SetQuality(q,"自動選択（"+GpuSummary()+"）");
            if(qualityCheck!=null)StopCoroutine(qualityCheck);qualityCheck=StartCoroutine(ConfirmAutomaticQuality());
        }
        static GraphicsQuality QualityFromGpu(string name,int memoryMegabytes)
        {
            string lower=name.ToLowerInvariant();
            // Integrated GPUs share system memory and report little or none of their own.
            bool integrated=lower.Contains("intel")&&!lower.Contains("arc")||lower.EndsWith("radeon(tm) graphics")||lower.EndsWith("radeon graphics")||lower.Contains("vega")&&!lower.Contains("rx")||memoryMegabytes<2048;
            return integrated?GraphicsQuality.Low:memoryMegabytes<6000?GraphicsQuality.Medium:GraphicsQuality.High;
        }
        static string GpuSummary()=>SystemInfo.graphicsDeviceName+"、"+Mathf.RoundToInt(SystemInfo.graphicsMemorySize/1024f)+" GB";
        IEnumerator ConfirmAutomaticQuality()
        {
            for(int attempt=0;attempt<2;attempt++)
            {
                yield return new WaitForSecondsRealtime(3);
                var frames=new List<float>();
                while(frames.Count<90){yield return null;frames.Add(Time.unscaledDeltaTime);}
                frames.Sort();float median=frames[frames.Count/2];
                Record("GraphicsQualityCheck",quality+";medianMs="+(median*1000).ToString("0.0"));
                // A choice the player made since then is left alone.
                if(PlayerPrefs.GetInt(QualityAutoKey,0)==0||median<=AutoQualitySlowFrame||quality==GraphicsQuality.Low)break;
                var lower=quality==GraphicsQuality.High?GraphicsQuality.Medium:GraphicsQuality.Low;
                StoreQuality(lower,true);SetQuality(lower,"自動選択（"+GpuSummary()+"、実測 "+Mathf.RoundToInt(1/median)+" fps）");
                if(opened&&category==5)BuildDetails();
            }
            qualityCheck=null;
        }
        void StoreQuality(GraphicsQuality q,bool automatic)
        {
            if(!qualityStored)return;
            PlayerPrefs.SetString(QualityKey,q.ToString());PlayerPrefs.SetInt(QualityAutoKey,automatic?1:0);PlayerPrefs.Save();
        }
        bool EffectEnabled(GraphicsQuality q,EnvironmentEffect effect)
        {
            if(q==GraphicsQuality.Custom)return PlayerPrefs.GetInt(EffectKeyPrefix+effect,1)!=0;
            if(q!=GraphicsQuality.Low)return true;
            return effect!=EnvironmentEffect.SunShadows&&effect!=EnvironmentEffect.Reflections&&effect!=EnvironmentEffect.AmbientOcclusion&&effect!=EnvironmentEffect.ContactShadows;
        }
        void SetQuality(GraphicsQuality q,string reason)
        {
            quality=q;qualityReason=reason;if(!qualityReady)return;
            var sun=rig.SunData;var profile=rig.Volume.profile;bool reduced=q==GraphicsQuality.Medium||q==GraphicsQuality.Low;
            sun.SetShadowResolution(reduced?2048:shippedShadowResolution);
            sun.dirLightPCSSBlockerSampleCount=reduced?32:shippedBlocker;sun.dirLightPCSSFilterSampleCount=reduced?32:shippedFilter;
            if(profile.TryGet<ScreenSpaceAmbientOcclusion>(out var ao)){ao.fullResolution=!reduced&&shippedAoFull;ao.bilateralUpsample=reduced||shippedAoBilateral;}
            if(profile.TryGet<ContactShadows>(out var contact))contact.sampleCount=reduced?12:shippedContactSamples;
            foreach(var effect in QualityEffects)if(rig.Debug.GetEffect(effect)!=EffectEnabled(q,effect))rig.Debug.SetEffect(effect,EffectEnabled(q,effect));
            Record("GraphicsQuality",q+";"+reason);
        }
        void SetEffectByPlayer(EnvironmentEffect effect,bool enabled)
        {
            // Switching one effect turns the current preset into カスタム with the same starting effects.
            if(quality!=GraphicsQuality.Custom)foreach(var e in QualityEffects)PlayerPrefs.SetInt(EffectKeyPrefix+e,EffectEnabled(quality,e)?1:0);
            PlayerPrefs.SetInt(EffectKeyPrefix+effect,enabled?1:0);StoreQuality(GraphicsQuality.Custom,false);SetQuality(GraphicsQuality.Custom,"手動で選択");
        }
        void BuildGraphics(RectTransform c,float y)
        {
            CfgUi.Label(c,"画質",8,y,330,34,21,Gold);
            for(int i=0;i<QualityNames.Length;i++)
            {
                var q=(GraphicsQuality)i;
                Button(c,"quality-"+i,QualityNames[i],8+i*84,y+42,78,40,()=>{StoreQuality(q,false);SetQuality(q,"手動で選択");BuildDetails();},q==quality);
            }
            CfgUi.Label(c,qualityReason,8,y+90,330,44,15,Muted);
            for(int i=0;i<QualityEffects.Length;i++)
            {
                var effect=QualityEffects[i];
                CfgUi.Toggle(c,QualityEffectNames[i],EffectEnabled(quality,effect),8,y+140+i*40,330,v=>{SetEffectByPlayer(effect,v);BuildDetails();});
            }
            float below=y+140+QualityEffects.Length*40+8;
            Button(c,"quality-auto","この PC に合わせて自動で選び直す",8,below,330,42,()=>{ChooseQualityAutomatically();BuildDetails();});
            CfgUi.Label(c,"中：影の解像度と柔らかさ、AO・接地影の精度を下げます。\n低：さらに太陽の影・反射・AO・接地影を切ります。\n効果を1つ切り替えるとカスタムになります。",8,below+52,330,80,15,Muted);
        }
    }
}
