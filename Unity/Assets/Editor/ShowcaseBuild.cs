using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using NDimensionalShogi.Showcase.Environment;

public static class ShowcaseBuild
{
    const string ScenePath="Assets/Scenes/Showcase.unity";
    static byte[] sourceManifestSnapshot;
    [MenuItem("Showcase/Prepare native HDRP project")]
    public static void PrepareProject()
    {
        PlayerSettings.companyName="piperendervt-glitch";
        PlayerSettings.productName="N-dimensional Shogi — Showcase";
        PlayerSettings.defaultScreenWidth=1920;PlayerSettings.defaultScreenHeight=1080;
        PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.resizableWindow=true;
        PlayerSettings.runInBackground=true;PlayerSettings.colorSpace=ColorSpace.Linear;
        PlayerSettings.enableFrameTimingStats=true;
        PlayerSettings.SplashScreen.show=false;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
        PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone,ApiCompatibilityLevel.NET_Standard);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});
        var pipeline=AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>("Assets/Settings/HDRP Balanced.asset");
        var pipelineSettings=pipeline.currentPlatformRenderPipelineSettings;pipelineSettings.supportSSR=false;
        pipelineSettings.hdShadowInitParams.directionalShadowFilteringQuality=HDShadowFilteringQuality.High;
        pipelineSettings.hdShadowInitParams.maxDirectionalShadowMapResolution=4096;
        pipelineSettings.hdShadowInitParams.directionalShadowsDepthBits=DepthBits.Depth32;
        pipelineSettings.lightLoopSettings.cookieAtlasSize=CookieAtlasResolution.CookieResolution4096;
        pipeline.currentPlatformRenderPipelineSettings=pipelineSettings;EditorUtility.SetDirty(pipeline);
        GraphicsSettings.defaultRenderPipeline=pipeline;
        for(int i=0;i<QualitySettings.names.Length;i++)
        { QualitySettings.SetQualityLevel(i,false);QualitySettings.renderPipeline=pipeline;QualitySettings.vSyncCount=0; }
        QualitySettings.SetQualityLevel(1,false);
        EnsureMaterialVariants();
        Directory.CreateDirectory("Assets/Scenes");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var controllerType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Showcase.Unity.ShowcaseController")).FirstOrDefault(t=>t!=null);
        if(controllerType==null)throw new InvalidOperationException("Showcase.Unity.ShowcaseController is not compiled yet.");
        new GameObject("Showcase — rule/geometry/presentation integration").AddComponent(controllerType);
        EditorSceneManager.SaveScene(scene,ScenePath);
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
        AssetDatabase.SaveAssets();
        Debug.Log("SHOWCASE_PREPARE_OK Unity="+Application.unityVersion+" HDRP="+UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(HDRenderPipeline).Assembly).version);
    }
    static void EnsureMaterialVariants()
    {
        const string folder="Assets/Resources/ShowcaseMaterialVariants";
        Directory.CreateDirectory(folder);
        // Resources references keep actual Lit variants in the Windows player without including every HDRP shader variant.
        var palette=new EnvironmentMaterials();
        var mats=new[]{palette.BrightWood,palette.DarkWood,palette.Stone,palette.PaleStone,palette.Ink,palette.Water,palette.Ceramic,palette.Brass,palette.Soil,palette.Foliage[0],palette.Flower[0]};
        foreach(var mat in mats)
        {
            string path=folder+"/"+mat.name.Replace(' ','_')+".mat";
            // Persist maps BEFORE the material. CreateAsset(material) imports immediately and
            // drops references to transient textures, so adding subassets afterwards loses them.
            foreach(string property in mat.GetTexturePropertyNames())
            {
                var tex=mat.GetTexture(property) as Texture2D;
                if(tex==null||AssetDatabase.Contains(tex))continue;
                string texturePath=folder+"/"+mat.name.Replace(' ','_')+property+".png";
                File.WriteAllBytes(texturePath,tex.EncodeToPNG());
                AssetDatabase.ImportAsset(texturePath,ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);
                importer.textureType=TextureImporterType.Default;
                importer.sRGBTexture=property=="_BaseColorMap"||property=="_MainTex";
                importer.textureCompression=property=="_NormalMap"?TextureImporterCompression.Uncompressed:TextureImporterCompression.CompressedHQ;
                importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Repeat;importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=8;importer.maxTextureSize=2048;
                importer.SaveAndReimport();
                mat.SetTexture(property,AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            }
            HDMaterial.ValidateMaterial(mat);
            var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(existing==null){AssetDatabase.CreateAsset(mat,path);existing=mat;}
            else EditorUtility.CopySerialized(mat,existing);
            EditorUtility.SetDirty(existing);
            if((mat.name.Contains("kaya")||mat.name.Contains("walnut")||mat.name.Contains("stone")||mat.name.Contains("travertine"))&&existing.GetTexture("_BaseColorMap")==null)
                throw new InvalidOperationException("Procedural material lost its persistent albedo: "+mat.name);
        }
        string unlitPath=folder+"/Guide_Unlit.mat";
        if(AssetDatabase.LoadAssetAtPath<Material>(unlitPath)==null)
        {
            var unlit=new Material(Shader.Find("HDRP/Unlit"));unlit.SetColor("_UnlitColor",new Color(.28f,.16f,.60f));HDMaterial.ValidateMaterial(unlit);AssetDatabase.CreateAsset(unlit,unlitPath);
        }
    }
    [MenuItem("Showcase/Build Windows x64")]
    public static void BuildWindows()
    {
        if(Resources.Load<TextAsset>("verified-replay")==null)throw new InvalidOperationException("Resources/verified-replay.json is required for the distributable.");
        PrepareProject();
        WriteBuildStamp();
        var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../builds/Showcase-Windows/NDShogiShowcase.exe"));
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {scenes=new[]{ScenePath},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
        var summary=report.summary;
        string record="{\n  \"unity\": \""+Application.unityVersion+"\",\n  \"buildGuid\": \""+summary.guid+"\",\n  \"platform\": \"Windows x64 / D3D11 / Mono\",\n  \"result\": \""+summary.result+"\",\n  \"totalBytes\": "+summary.totalSize+",\n  \"errors\": "+summary.totalErrors+",\n  \"warnings\": "+summary.totalWarnings+",\n  \"durationSeconds\": "+summary.totalTime.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+"\n}\n";
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(output),"build-report.json"),record);
        File.Copy(Path.Combine(Application.dataPath,"Resources/showcase-build.json"),Path.Combine(Path.GetDirectoryName(output),"showcase-build.json"),true);
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(output),"source-manifest.json"),sourceManifestSnapshot);
        if(summary.result!=BuildResult.Succeeded)throw new Exception("Windows build failed: "+summary.result);
        Debug.Log("SHOWCASE_BUILD_OK "+output);
    }
    [Serializable] sealed class BuildProvenance {public string sourceManifestSha256,sourceHash,builtUtc;}
    static void WriteBuildStamp()
    {
        string repo=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
        var start=new System.Diagnostics.ProcessStartInfo
        {
            FileName="python",Arguments="\""+Path.Combine(repo,"tools/export_source_manifest.py")+"\"",WorkingDirectory=repo,
            UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true
        };
        using(var process=System.Diagnostics.Process.Start(start))
        {
            string stdout=process.StandardOutput.ReadToEnd(),stderr=process.StandardError.ReadToEnd();process.WaitForExit();
            if(process.ExitCode!=0)throw new InvalidOperationException("Source manifest export failed: "+stderr);
            Debug.Log("SHOWCASE_SOURCE_MANIFEST "+stdout.Trim());
        }
        string manifestPath=Path.Combine(repo,"evidence/environment/source-manifest.json");
        sourceManifestSnapshot=File.ReadAllBytes(manifestPath);
        var stamp=JsonUtility.FromJson<BuildProvenance>(System.Text.Encoding.UTF8.GetString(sourceManifestSnapshot));
        using(var sha=SHA256.Create())stamp.sourceManifestSha256=BitConverter.ToString(sha.ComputeHash(sourceManifestSnapshot)).Replace("-","").ToLowerInvariant();
        stamp.builtUtc=DateTime.UtcNow.ToString("o");
        string path="Assets/Resources/showcase-build.json";
        File.WriteAllText(path,JsonUtility.ToJson(stamp,true));
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);AssetDatabase.SaveAssets();
    }
}
