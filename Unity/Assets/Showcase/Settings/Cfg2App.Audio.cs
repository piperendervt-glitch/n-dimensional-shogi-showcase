using UnityEngine;

namespace Showcase.Unity.Settings
{
    public sealed partial class Cfg2App
    {
        // Sounds are synthesized by tools/synth_audio.py. Volumes are machine preferences like the CPU show toggles,
        // kept in PlayerPrefs and out of the settings hash and match records.
        const string BgmVolumeKey="cfg2.bgm-volume",SfxVolumeKey="cfg2.sfx-volume";
        const float DefaultBgmVolume=.5f,DefaultSfxVolume=.7f;
        AudioSource bgmSource,sfxSource,uiSource;AudioClip pieceClip,selectClip,confirmClip;float lastSfxPreview;
        // A slider drag sets the level live; the preference is written once, when the slider is let go (or at quit).
        float? bgmPending,sfxPending;
        float BgmVolume{get=>bgmPending??StoredVolume(BgmVolumeKey,DefaultBgmVolume);set{bgmPending=Finite01(value,BgmVolume);ApplyVolumes();}}
        float SfxVolume{get=>sfxPending??StoredVolume(SfxVolumeKey,DefaultSfxVolume);set{sfxPending=Finite01(value,SfxVolume);ApplyVolumes();}}
        // A stored NaN or infinity (Clamp01 lets NaN through) reads as the default level.
        static float Finite01(float value,float fallback)=>float.IsNaN(value)||float.IsInfinity(value)?fallback:Mathf.Clamp01(value);
        static float StoredVolume(string key,float fallback)=>Finite01(PlayerPrefs.GetFloat(key,fallback),fallback);
        void FlushVolumes()
        {
            if(!bgmPending.HasValue&&!sfxPending.HasValue)return;
            if(bgmPending.HasValue)PlayerPrefs.SetFloat(BgmVolumeKey,bgmPending.Value);if(sfxPending.HasValue)PlayerPrefs.SetFloat(SfxVolumeKey,sfxPending.Value);
            bgmPending=sfxPending=null;PlayerPrefs.Save();Record("Pref","volumes bgm="+BgmVolume.ToString("0.00")+";sfx="+SfxVolume.ToString("0.00"));
        }
        // Slider positions are squared so the lower half of the slider stays usefully quiet.
        static float Gain(float position)=>position*position;
        void CreateAudio()
        {
            var go=new GameObject("CFG2 audio");
            var bgm=Resources.Load<AudioClip>("Cfg2Audio/bgm");pieceClip=Resources.Load<AudioClip>("Cfg2Audio/piece");
            selectClip=Resources.Load<AudioClip>("Cfg2Audio/menu-select");confirmClip=Resources.Load<AudioClip>("Cfg2Audio/menu-confirm");
            if(bgm==null||pieceClip==null||selectClip==null||confirmClip==null)Record("AudioMissing","bgm="+(bgm!=null)+";piece="+(pieceClip!=null)+";select="+(selectClip!=null)+";confirm="+(confirmClip!=null));
            bgmSource=go.AddComponent<AudioSource>();bgmSource.clip=bgm;bgmSource.loop=true;bgmSource.playOnAwake=false;bgmSource.spatialBlend=0;bgmSource.priority=0;
            sfxSource=go.AddComponent<AudioSource>();sfxSource.playOnAwake=false;sfxSource.spatialBlend=0;
            // UI sounds get their own source so the piece sound's pitch spread never touches them.
            uiSource=go.AddComponent<AudioSource>();uiSource.playOnAwake=false;uiSource.spatialBlend=0;
            ApplyVolumes();if(bgm!=null)bgmSource.Play();
            Record("Audio","bgm="+BgmVolume.ToString("0.00")+";sfx="+SfxVolume.ToString("0.00")+";playing="+bgmSource.isPlaying+";output="+AudioSettings.outputSampleRate);
        }
        void ApplyVolumes()
        {
            if(bgmSource!=null)bgmSource.volume=Gain(BgmVolume);
            if(sfxSource!=null)sfxSource.volume=Gain(SfxVolume);
            if(uiSource!=null)uiSource.volume=Gain(SfxVolume);
        }
        void PlayPieceSound()
        {
            if(sfxSource==null||pieceClip==null)return;
            // A slight pitch spread keeps repeated moves from sounding mechanical.
            sfxSource.pitch=Random.Range(.96f,1.04f);sfxSource.PlayOneShot(pieceClip);Record("PieceSound",sfxSource.volume.ToString("0.00"));
        }
        void PlayMenuSelect(){if(uiSource!=null&&selectClip!=null)uiSource.PlayOneShot(selectClip);}
        void PlayMenuConfirm(){if(uiSource!=null&&confirmClip!=null){uiSource.PlayOneShot(confirmClip);Record("ConfirmSound",uiSource.volume.ToString("0.00"));}}
        void BuildSound(RectTransform c,float y)
        {
            CfgUi.Label(c,"サウンド",8,y,330,34,21,Gold);
            var bgmLabel=CfgUi.Label(c,"",8,y+42,330,30,18);void ShowBgm(float v)=>bgmLabel.text="BGM の音量："+Mathf.RoundToInt(v*100)+"%";ShowBgm(BgmVolume);
            Cfg2Ui.Slider(c,8,y+76,330,0,1,BgmVolume,v=>{ShowBgm(v);BgmVolume=v;},FlushVolumes);
            var sfxLabel=CfgUi.Label(c,"",8,y+124,330,30,18);void ShowSfx(float v)=>sfxLabel.text="効果音の音量："+Mathf.RoundToInt(v*100)+"%";ShowSfx(SfxVolume);
            Cfg2Ui.Slider(c,8,y+158,330,0,1,SfxVolume,v=>{ShowSfx(v);SfxVolume=v;
                // Let the player hear the new level while dragging, without a burst of clicks.
                if(Time.unscaledTime-lastSfxPreview>.25f){lastSfxPreview=Time.unscaledTime;PlayPieceSound();}},FlushVolumes);
            CfgUi.Label(c,"効果音は駒音とメニュー音。0% で消音。",8,y+206,330,30,16,Muted);
        }
    }
}
