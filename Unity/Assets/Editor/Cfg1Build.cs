using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Cfg1Build
{
    const string ScenePath="Assets/Scenes/Cfg1.unity";
    public static void BuildWindows()
    {
        var args=Environment.GetCommandLineArgs();
        string Value(string key){int i=Array.IndexOf(args,key);if(i<0||i+1>=args.Length)throw new ArgumentException(key+" required");return args[i+1];}
        string output=Path.GetFullPath(Value("--cfg1-output"));
        string run=Path.GetFullPath(Value("--cfg1-run-root"));
        if(!Directory.Exists(run))throw new DirectoryNotFoundException(run);
        if(File.Exists(output))throw new IOException("Refusing to overwrite an existing CFG1 executable");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        PlayerSettings.companyName="piperendervt-glitch";PlayerSettings.productName="四次元将棋";
        PlayerSettings.defaultScreenWidth=1536;PlayerSettings.defaultScreenHeight=1024;
        PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.resizableWindow=true;
        PlayerSettings.runInBackground=true;PlayerSettings.SplashScreen.show=false;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
        PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone,ApiCompatibilityLevel.NET_Standard);
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Showcase.Unity.Settings.Cfg1App")).FirstOrDefault(t=>t!=null);
        if(type==null)throw new InvalidOperationException("Cfg1App did not compile");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("四次元将棋 CFG1").AddComponent(type);EditorSceneManager.SaveScene(scene,ScenePath);
        // A dedicated resource supplies this run's save/log root even when the EXE is double-clicked.
        string resource="Assets/Resources/cfg1-run.json";
        File.WriteAllText(resource,JsonUtility.ToJson(new RunConfig{runRoot=run},true));
        AssetDatabase.ImportAsset(resource,ImportAssetOptions.ForceSynchronousImport);AssetDatabase.SaveAssets();
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
        var s=report.summary;
        var result=new BuildRecord{result=s.result.ToString(),errors=(int)s.totalErrors,warnings=(int)s.totalWarnings,seconds=s.totalTime.TotalSeconds,bytes=(long)s.totalSize,unity=Application.unityVersion,buildGuid=s.guid.ToString(),output=output,runRoot=run};
        File.WriteAllText(Path.Combine(run,"build-report.json"),JsonUtility.ToJson(result,true));
        if(s.result!=BuildResult.Succeeded)throw new Exception("CFG1 build failed: "+s.result);
        Debug.Log("CFG1_BUILD_OK "+output);
    }
    [Serializable] sealed class RunConfig { public string runRoot; }
    [Serializable] sealed class BuildRecord {public string result,unity,buildGuid,output,runRoot;public int errors,warnings;public double seconds;public long bytes;}
}
