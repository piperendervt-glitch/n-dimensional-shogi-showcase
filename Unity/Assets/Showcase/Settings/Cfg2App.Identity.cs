using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Showcase.Unity.Settings
{
    public sealed partial class Cfg2App
    {
        [Serializable] sealed class PivotBuildIdentity
        {public string requestId,requestPath,requestHash,designPath,designHash,sourceHash,buildToken,runRoot,scene,app,entry;}
        [Serializable] sealed class PivotPlayerIdentity
        {public PivotBuildIdentity build;public string buildGuid,scene,app,executable,runRoot,dataRoot,validPath,screen,utc;public int processId,width,height;public float reportedDpi;}
        PivotBuildIdentity buildIdentity;string launchEvidenceRoot;
        void VerifyPivotIdentity()
        {
            var resource=Resources.Load<TextAsset>("pivot-build");
            if(resource==null)throw new InvalidOperationException("Missing PIVOT build identity; launch rejected");
            buildIdentity=JsonUtility.FromJson<PivotBuildIdentity>(resource.text);
            if(buildIdentity==null||buildIdentity.requestId!=Arg("--pivot-request-id","")||buildIdentity.buildToken!=Arg("--pivot-build-token","")||
                buildIdentity.sourceHash!=Arg("--pivot-source-hash","")||Application.buildGUID!=Arg("--pivot-build-guid","")||
                buildIdentity.app!=GetType().FullName||buildIdentity.scene!="Assets/Scenes/Cfg2.unity"||SceneManager.GetActiveScene().name!="Cfg2"||
                !string.Equals(Path.GetFullPath(runRoot),Path.GetFullPath(buildIdentity.runRoot),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PIVOT launcher/build/scene identity mismatch; launch rejected");
            launchEvidenceRoot=Path.GetFullPath(Arg("--pivot-launch-root",""));
            if(!launchEvidenceRoot.StartsWith(Path.GetFullPath(runRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!Directory.Exists(launchEvidenceRoot))
                throw new InvalidOperationException("Invalid launch evidence root");
        }
        void WritePlayerIdentity(string screen)
        {
            if(buildIdentity==null||string.IsNullOrEmpty(launchEvidenceRoot))return;
            var process=System.Diagnostics.Process.GetCurrentProcess();
            var identity=new PivotPlayerIdentity{build=buildIdentity,buildGuid=Application.buildGUID,scene=SceneManager.GetActiveScene().name,app=GetType().FullName,
                executable=process.MainModule.FileName,processId=process.Id,runRoot=runRoot,dataRoot=runtimeRoot,validPath=validPath,screen=screen,width=Screen.width,height=Screen.height,
                reportedDpi=Screen.dpi,utc=DateTime.UtcNow.ToString("o")};
            File.WriteAllText(Path.Combine(launchEvidenceRoot,"player-identity.json"),JsonUtility.ToJson(identity,true));
        }
    }
}
