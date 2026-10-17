using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Cfg2Build
{
    public static void BuildWindows()
    {
        var args=Environment.GetCommandLineArgs();string Value(string key){int i=Array.IndexOf(args,key);if(i<0||i+1>=args.Length)throw new ArgumentException(key+" required");return args[i+1];}
        string output=Path.GetFullPath(Value("--cfg2-output")),run=Path.GetFullPath(Value("--cfg2-run-root"));
        string request=Path.GetFullPath(Value("--pivot-request")),design=Path.GetFullPath(Value("--pivot-design")),requestId=Value("--pivot-request-id");
        string repo=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..")),buildRoot=Path.GetDirectoryName(output);
        if(!output.StartsWith(run+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!request.StartsWith(repo+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Build and request must belong to the explicit run/repository");
        if(Directory.Exists(buildRoot)&&Directory.EnumerateFileSystemEntries(buildRoot).Any())throw new IOException("Build directory must be new and empty");
        if(!Directory.Exists(run))throw new DirectoryNotFoundException(run);if(File.Exists(output))throw new IOException("Existing CFG2 build must not be overwritten");Directory.CreateDirectory(Path.GetDirectoryName(output));
        PlayerSettings.companyName="piperendervt-glitch";PlayerSettings.productName="四次元将棋";PlayerSettings.defaultScreenWidth=1536;PlayerSettings.defaultScreenHeight=1024;
        PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.resizableWindow=true;PlayerSettings.runInBackground=true;PlayerSettings.SplashScreen.show=false;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone,ApiCompatibilityLevel.NET_Standard);
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Showcase.Unity.Settings.Cfg2App")).FirstOrDefault(t=>t!=null);if(type==null)throw new InvalidOperationException("Cfg2App did not compile");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("四次元将棋 CFG2").AddComponent(type);EditorSceneManager.SaveScene(scene,"Assets/Scenes/Cfg2.unity");
        string resource="Assets/Resources/cfg2-run.json";File.WriteAllText(resource,JsonUtility.ToJson(new RunConfig{runRoot=run},true));AssetDatabase.ImportAsset(resource,ImportAssetOptions.ForceSynchronousImport);AssetDatabase.SaveAssets();
        const string identityPath="Assets/Resources/pivot-build.json";
        File.WriteAllText(identityPath,"{}");AssetDatabase.ImportAsset(identityPath,ImportAssetOptions.ForceSynchronousImport);AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);AssetDatabase.SaveAssets();
        var manifest=SourceFiles(repo).Select(p=>new SourceFile{path=p.Substring(repo.Length+1).Replace('\\','/'),sha256=Hash(File.ReadAllBytes(p)),bytes=new FileInfo(p).Length}).OrderBy(p=>p.path,StringComparer.Ordinal).ToArray();
        string sourceHash=Hash(Encoding.UTF8.GetBytes(string.Join("\n",manifest.Select(p=>p.path+"\t"+p.sha256))));
        var identity=new Identity{requestId=requestId,requestPath=request,requestHash=Hash(File.ReadAllBytes(request)),designPath=design,designHash=Hash(File.ReadAllBytes(design)),sourceHash=sourceHash,buildToken=Guid.NewGuid().ToString("N"),runRoot=run,scene="Assets/Scenes/Cfg2.unity",app="Showcase.Unity.Settings.Cfg2App",entry="Cfg2Build.BuildWindows"};
        string identityJson=JsonUtility.ToJson(identity,true);File.WriteAllText(identityPath,identityJson);AssetDatabase.ImportAsset(identityPath,ImportAssetOptions.ForceSynchronousImport);AssetDatabase.SaveAssets();
        File.WriteAllText(Path.Combine(buildRoot,"embedded-identity.json"),identityJson);File.WriteAllText(Path.Combine(buildRoot,"source-manifest.json"),JsonUtility.ToJson(new SourceManifest{identity=identity,files=manifest},true));
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/Cfg2.unity"},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});var s=report.summary;
        var afterPaths=SourceFiles(repo).Select(p=>p.Substring(repo.Length+1).Replace('\\','/')).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        string[] drift=manifest.Where(p=>!File.Exists(Path.Combine(repo,p.path))||Hash(File.ReadAllBytes(Path.Combine(repo,p.path)))!=p.sha256).Select(p=>p.path)
            .Concat(afterPaths.Except(manifest.Select(p=>p.path),StringComparer.Ordinal).Select(p=>"ADDED:"+p)).ToArray();
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(output),"build-report.json"),JsonUtility.ToJson(new BuildRecord{result=s.result.ToString(),errors=(int)s.totalErrors,warnings=(int)s.totalWarnings,seconds=s.totalTime.TotalSeconds,bytes=(long)s.totalSize,unity=Application.unityVersion,buildGuid=s.guid.ToString(),output=output,runRoot=run,identity=identity,sourceDrift=drift},true));
        if(drift.Length>0)throw new Exception("Build source changed during build: "+string.Join(",",drift));
        if(s.result!=BuildResult.Succeeded)throw new Exception("CFG2 build failed: "+s.result);Debug.Log("CFG2_BUILD_OK "+output);
    }
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static IEnumerable<string> SourceFiles(string repo)
    {
        foreach(string folder in new[]{"Unity/Assets","Unity/Packages","Unity/ProjectSettings","src/Showcase.Core/Runtime","src/Showcase.Geometry/Runtime"})
        foreach(string file in Directory.EnumerateFiles(Path.Combine(repo,folder),"*",SearchOption.AllDirectories))
        {string relative=file.Substring(repo.Length+1).Replace('\\','/');if(relative!="Unity/Assets/Resources/pivot-build.json"&&relative!="Unity/Assets/Resources/pivot-build.json.meta")yield return file;}
        yield return Path.Combine(repo,"src/Showcase.Core/package.json");yield return Path.Combine(repo,"src/Showcase.Geometry/package.json");
    }
    [Serializable] sealed class SourceFile{public string path,sha256;public long bytes;}
    [Serializable] sealed class SourceManifest{public Identity identity;public SourceFile[] files;}
    [Serializable] sealed class Identity{public string requestId,requestPath,requestHash,designPath,designHash,sourceHash,buildToken,runRoot,scene,app,entry;}
    [Serializable] sealed class RunConfig{public string runRoot;}
    [Serializable] sealed class BuildRecord{public string result,unity,buildGuid,output,runRoot;public int errors,warnings;public double seconds;public long bytes;public Identity identity;public string[] sourceDrift;}
}
