using System;
using System.IO;
using System.Linq;
using UnityEngine;
using NDShogi.Showcase.Core;
using NDShogi.Showcase.Core.Settings;
using NDShogi.Showcase.Core.Match;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;

namespace Showcase.Unity.Settings
{
    public sealed partial class Cfg2App
    {
        QN planeReferenceCell;bool migratedView;
        string trialSelectedPiece="subject";QN trialSelectedCell,trialPlaneReference;
        PivotViewSnapshot CapturePivotView(SliceAnimator animator,float cameraYaw,float cameraPitch,float cameraDistance,Vector3 cameraPan,string selected,QN cell,QN plane)
        {
            var state=animator.DisplayState;var f=state.Frame;
            double[] V(VecN v)=>new[]{v.X,v.Y,v.Z,v.W};
            return new PivotViewSnapshot(state.EmbeddingHeight,V(f.Center),V(f.AxisX).Concat(V(f.AxisY)).Concat(V(f.AxisZ)).Concat(V(f.Normal)).ToArray(),
                new[]{state.Anchor.X,state.Anchor.Y,state.Anchor.Z},state.HalfWindow,state.CellScale,
                new double[]{cameraYaw,cameraPitch,cameraDistance,cameraPan.x,cameraPan.y,cameraPan.z},selected??"",cell,plane);
        }
        PivotViewSnapshot CapturePivotView()
        {var source=session!=null&&!opened?session.ToPreview():preview;return CapturePivotView(slice,yaw,pitch,distance,pan,source.Find(selectedPiece)!=null?selectedPiece:"",selectedCell,planeReferenceCell);}
        static SliceAnimator RestorePivotView(PivotViewSnapshot saved)
        {
            var c=saved.Center;var b=saved.Basis;var a=saved.Anchor;
            VecN V(double[] values,int n)=>new VecN(values[n],values[n+1],values[n+2],values[n+3]);
            return new SliceAnimator(new PivotViewState(saved.Height,new SliceFrame(V(c,0),V(b,0),V(b,4),V(b,8),V(b,12)),new Vec3(a[0],a[1],a[2]),saved.CellScale,saved.HalfWindow));
        }
        string DraftPathForBundle(string bundlePath)=>Path.Combine(runtimeRoot,Path.GetFileNameWithoutExtension(bundlePath)+"-draft.xml");
        void LoadStartup()
        {
            try
            {
                string selectedPath=PivotSettingsLocation.Resolve(runtimeRoot,validPath);
                if(!string.Equals(selectedPath,validPath,StringComparison.OrdinalIgnoreCase))
                {validPath=selectedPath;draftPath=DraftPathForBundle(selectedPath);Record("PivotBundleSelected",selectedPath);}
                LoadedPivotSettings saved=null;
                if(File.Exists(validPath))saved=PivotSettingsStore.Load(validPath);
                else
                {
                    string old=Path.Combine(runtimeRoot,"next-match.xml");
                    if(File.Exists(old))saved=PivotSettingsStore.ImportLegacy(old,File.Exists(trialPath)?trialPath:null,CapturePivotView(slice,yaw,pitch,distance,pan,"",default,default));
                }
                if(saved==null)return;
                if(!saved.IsValidSettings)throw new InvalidDataException("正常設定の保存先が下書き形式です。");
                // Construct and validate every component before changing live state.
                var restored=RestorePivotView(saved.View);var camera=saved.View.Camera;
                draft=saved.Draft;options=saved.Options;preview=saved.Preview;slice=restored;
                yaw=(float)camera[0];pitch=(float)camera[1];distance=(float)camera[2];pan=new Vector3((float)camera[3],(float)camera[4],(float)camera[5]);
                selectedPiece=saved.View.SelectedPieceId;selectedCell=saved.View.SelectedCell;planeReferenceCell=saved.View.PlaneReferenceCell;
                if(preview.Find(selectedPiece)!=null)selectedDefinition=preview.Find(selectedPiece).DefinitionId;
                notice=saved.MigrationNotice;migratedView=saved.MigrationNotice!="";Record("RestoredPivotBundle",saved.LogicalHash+";preview="+saved.PreviewHash+";"+saved.MigrationNotice);
            }
            catch(Exception e){startupLoadError=e.Message;notice="保存の読込に失敗："+e.Message;Record("LoadRejected",e.Message);}
        }
        bool AutoSave()
        {
            if(startupLoadError!=""){settingsDirty=false;Notice("元保存を保持しています。プリセット画面から別ファイルへ復旧できます。");return false;}
            if(ownership.Owner!=InputOwner.None){saveDue=Time.unscaledTime+.3f;return false;}
            try
            {
                bool inMatch=session!=null&&!opened;
                var view=inMatch?CapturePivotView(trialSlice,trialYaw,trialPitch,trialDistance,trialPan,
                    preview.Find(trialSelectedPiece)!=null?trialSelectedPiece:"",trialSelectedCell,trialPlaneReference):CapturePivotView();
                if(config!=null){PivotSettingsStore.SaveValid(validPath,draft,options,preview,view);Record("SettingsSaved",config.RuleHash);}
                else {PivotSettingsStore.SaveDraft(draftPath,draft,options,preview,view);Record("InvalidDraftPreserved",draft.ContentHash);}
                settingsDirty=false;if(!inMatch)stableDirty=false;UpdateHeader();return true;
            }
            catch(Exception e){settingsDirty=true;saveDue=Time.unscaledTime+2;Notice("自動保存に失敗："+e.Message);return false;}
        }
        bool SaveStableView()
        {
            if(ownership.Owner!=InputOwner.None)return false;
            if(!opened&&session==null)return true;
            if(session==null||opened)return AutoSave();
            try
            {
                // A match display is bound to that actual position and is never
                // silently combined with the independent settings trial board.
                string path=Path.Combine(runtimeRoot,"pivot-match-"+session.Snapshot.MatchId+".xml");
                PivotSettingsStore.SaveValid(path,new SettingsDraft(session.Config.Preset.ToDraftData()),session.Config.ToRaw(),session.ToPreview(),CapturePivotView());
                stableDirty=false;Record("MatchViewSaved",path);return true;
            }
            catch(Exception e){stableDirty=true;saveDue=Time.unscaledTime+2;Notice("表示の保存に失敗："+e.Message);return false;}
        }
        bool RestoreSavedMatchView(string basename)
        {
            try
            {
                if(session==null||opened||ownership.Owner!=InputOwner.None)throw new InvalidOperationException("対局表示を復元できる状態ではありません。");
                if(string.IsNullOrWhiteSpace(basename)||Path.GetFileName(basename)!=basename||basename.Contains("..")||!basename.EndsWith(".xml",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("今回の保存先内のXML名を指定してください。");
                var saved=PivotSettingsStore.Load(Path.Combine(runtimeRoot,basename));
                var matchDraft=new SettingsDraft(session.Config.Preset.ToDraftData());
                if(!saved.IsValidSettings||saved.LogicalHash!=PivotSettingsStore.LogicalContentHash(matchDraft,session.Config.ToRaw())||saved.PreviewHash!=session.ToPreview().Hash)
                    throw new InvalidDataException("保存表示と現在の対局設定・駒配置が一致しません。表示を適用していません。");
                var next=RestorePivotView(saved.View);var camera=saved.View.Camera;
                slice=next;yaw=(float)camera[0];pitch=(float)camera[1];distance=(float)camera[2];pan=new Vector3((float)camera[3],(float)camera[4],(float)camera[5]);
                selectedPiece=saved.View.SelectedPieceId;selectedCell=saved.View.SelectedCell;planeReferenceCell=saved.View.PlaneReferenceCell;
                manualCamera=true;RebindMinis();previewRenderer.SetDisplayState(slice.DisplayState);UpdateCamera();QueueQuery();Record("MatchViewRestored",basename);return true;
            }
            catch(Exception e){Notice("対局表示の読込を拒否："+e.Message);return false;}
        }
        void RecoverToNewBundle()
        {
            try
            {
                if(ownership.Owner!=InputOwner.None||session!=null&&!opened)throw new InvalidOperationException("操作を終えてから設定を復旧してください。");
                string recovered=Path.Combine(runtimeRoot,"pivot-recovered-"+Guid.NewGuid().ToString("N")+".xml");
                var view=CapturePivotView();
                PivotSettingsLocation.SaveRecovery(runtimeRoot,recovered,draft,options,preview,view);
                // Both durable writes succeeded. Only now switch the live paths;
                // the current edit, undo history, camera and section stay intact.
                string previous=validPath;validPath=recovered;draftPath=DraftPathForBundle(recovered);startupLoadError="";
                settingsDirty=false;stableDirty=false;ValidateSettings();BuildPieces();BuildDetails();
                Record("PivotBundleRecovered",previous+" -> "+validPath+";logical="+PivotSettingsStore.LogicalContentHash(draft,options));
                Notice("元ファイルと編集中の内容を保持し、新しい保存先を作成しました："+Path.GetFileName(validPath));
            }
            catch(Exception e){Notice("復旧保存に失敗。元ファイルと編集中の内容を保持しています："+e.Message);}
        }
    }
}
