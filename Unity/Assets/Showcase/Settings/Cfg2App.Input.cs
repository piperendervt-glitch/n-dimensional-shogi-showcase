using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using NDShogi.Showcase.Core;
using NDShogi.Showcase.Core.Settings;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;

namespace Showcase.Unity.Settings
{
    public sealed partial class Cfg2App
    {
        int pointerButton=-1;Vector2 pointerStart,lastPointer;bool pointerMoved;
        enum Gesture { ClickOrbit,Orbit,Pan,Rotate4D }
        Gesture gesture;bool rotateZw;PivotViewState pointerView;
        bool startManualCamera;float pointerScale;
        long pointerRevision,pointerState,pointerGeneration,pointerDraft,pointerCamera,pointerLayout;string pointerMatch;
        float startYaw,startPitch,startDistance;Vector3 startPan,startCameraPosition;Quaternion startCameraRotation;
        void AttachPointer(RectTransform r)
        {var p=r.gameObject.AddComponent<CfgPointerSurface>();p.Down=e=>RunPointer(()=>PreviewDown(e));p.Drag=e=>RunPointer(()=>PreviewDrag(e));p.Up=e=>RunPointer(()=>PreviewUp(e));p.Scroll=e=>RunPointer(()=>PreviewScroll(e));}
        void RunPointer(Action action){try{action();}catch(ArgumentException e){Notice(e.Message);}catch(InvalidOperationException e){Notice(e.Message);}}
        void RequestView(string axis)
        {
            if(ownership.Owner!=InputOwner.None)return;
            slice.Request(SliceAxes.SectionForAxis(slice.TargetSection,(LogicalAxis)Enum.Parse(typeof(LogicalAxis),axis)));geometryDirty=true;stableDirty=true;Record("SliceRequest",axis);
        }
        void UpdateCamera()
        {
            // Camera pan has a fixed world reference, independent from the moving
            // section display anchor. A pivot translation must not retarget it.
            var rotation=Quaternion.Euler(pitch,yaw,0);var target=CameraBase+pan;
            rig.Camera.transform.position=target+rotation*new Vector3(0,0,-distance);rig.Camera.transform.LookAt(target);cameraRevision++;stableDirty=true;saveDue=Time.unscaledTime+.3f;
            RefreshAuxiliaryViews();
        }
        void PreviewDown(PointerEventData e)
        {
            // The hand-over presentation and a CPU's shown move do not lock the camera: orbit, pan and zoom stay available in every
            // match mode (CPU vs CPU included); a cell pick during them is still refused by CommitCell.
            bool control=Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl),shift=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
            BeginPointer(e,control,shift);
        }
        void BeginPointer(PointerEventData e,bool control,bool shift)
        {
            if(ownership.Owner!=InputOwner.None)return;
            if(CfgUi.EditingText)return;
            // Modifiers and scale belong to this entire gesture, including cancel.
            gesture=e.button==PointerEventData.InputButton.Middle?Gesture.Pan:e.button==PointerEventData.InputButton.Right?Gesture.Orbit:control?Gesture.Rotate4D:shift?Gesture.Pan:Gesture.ClickOrbit;
            rotateZw=control&&shift;pointerScale=Mathf.Max(.001f,canvas.scaleFactor);
            var owner=gesture==Gesture.Pan?InputOwner.CameraPan:gesture==Gesture.Orbit?InputOwner.CameraOrbit:gesture==Gesture.Rotate4D?InputOwner.Rotation4D:InputOwner.Board;
            startYaw=yaw;startPitch=pitch;startDistance=distance;startPan=pan;startCameraPosition=rig.Camera.transform.position;startCameraRotation=rig.Camera.transform.rotation;
            startManualCamera=manualCamera;pointerView=slice.DisplayState;
            // Only a 4D turn changes the section, so only its cancel restores it: a camera gesture cancelled during a CPU's shown move
            // or a turn reset must not rewind (and stop) the turn that is running.
            var capturedSlice=slice;bool turnsSection=gesture==Gesture.Rotate4D;
            if(!ownership.TryBegin(owner,e.pointerId,()=>{if(turnsSection)capturedSlice.Restore(pointerView);yaw=startYaw;pitch=startPitch;distance=startDistance;pan=startPan;manualCamera=startManualCamera;rig.Camera.transform.SetPositionAndRotation(startCameraPosition,startCameraRotation);cameraRevision++;geometryDirty=true;RefreshAuxiliaryViews();pointerButton=-1;}))return;
            if(gesture==Gesture.Rotate4D)slice.SetOrientation(slice.Frame);
            pointerButton=(int)e.button;pointerStart=lastPointer=e.position;pointerMoved=false;pointerRevision=slice.ViewRevision;pointerState=session?.Revision??preview.Revision;pointerGeneration=session?.Generation??0;pointerDraft=draft.Revision;pointerCamera=cameraRevision;pointerLayout=layoutRevision;pointerMatch=session?.Snapshot.MatchId;
            Record("PointerDown",e.button+";view="+pointerRevision+";state="+pointerState+";generation="+pointerGeneration);
        }
        void PreviewDrag(PointerEventData e)
        {
            if(pointerButton<0||ownership.PointerId!=e.pointerId)return;var total=(e.position-pointerStart)/pointerScale;lastPointer=e.position;pointerMoved|=total.sqrMagnitude>36;
            if(!pointerMoved)return;
            if(gesture==Gesture.Rotate4D)
            {
                // A 3D board has no hidden axis to rotate against.
                if(pointerView.Dimensions>3){slice.SetOrientation(SliceRotations.RotateDrag(pointerView.Frame,total.x*.25,total.y*.25,rotateZw,RotationCoreAxis));geometryDirty=true;stableDirty=true;}
            }
            else if(gesture==Gesture.Pan)
            {pan=startPan+(-(startCameraRotation*Vector3.right)*total.x-(startCameraRotation*Vector3.up)*total.y)*startDistance*.001f;manualCamera=true;UpdateCamera();}
            else {yaw=startYaw+total.x*.25f;pitch=Mathf.Clamp(startPitch-total.y*.2f,-75,75);manualCamera=true;UpdateCamera();}
        }
        void PreviewUp(PointerEventData e)
        {
            if(pointerButton!=(int)e.button||ownership.PointerId!=e.pointerId)return;int button=pointerButton;pointerButton=-1;ownership.Release(ownership.Owner,e.pointerId);
            if(pointerMoved&&gesture==Gesture.Rotate4D&&pointerView.Dimensions>3)Record("Rotation4D",rotateZw?"ZW":"XY");
            if(button!=0||pointerMoved||gesture!=Gesture.ClickOrbit)return;
            if(pointerCamera!=cameraRevision||pointerLayout!=layoutRevision||geometryDirty||pointerDraft!=draft.Revision||pointerMatch!=session?.Snapshot.MatchId){Record("StalePointerRejected");return;}
            Ray ray;
            if(opened)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(previewRect,e.position,null,out var local);var rect=previewRect.rect;
                ray=rig.Camera.ViewportPointToRay(new Vector3((local.x-rect.xMin)/rect.width,(local.y-rect.yMin)/rect.height,0));
            }
            else ray=rig.Camera.ScreenPointToRay(e.position);
            if(TryPickCell(ray,pointerRevision,out var q))CommitCell(q,pointerRevision,pointerState,pointerGeneration);
        }
        bool TryPickCell(Ray ray,long revision,out QN q)
        {
            if(geometryDirty){q=default;return false;}
            return previewRenderer.Pick(ray,revision,out q,tool=="選択"||session!=null);
        }
        void CommitCell(QN q,long viewRevision,long stateRevision,long generation)
        {
            if(viewRevision!=slice.ViewRevision||slice.IsAnimating||stateRevision!=(session?.Revision??preview.Revision)) {Record("StalePickRejected",q.ToString());return;}
            selectedCell=q;planeReferenceCell=q;stableDirty=true;saveDue=Time.unscaledTime+.3f;
            if(session!=null&&!opened)
            {
                if(!session.CanHumanAct||generation!=session.Generation){Record("MatchPickRejected",session.Phase.ToString());return;}
                var target=session.State.At(q);var selected=session.State.Find(selectedPiece);
                if(selected!=null&&selected.Side==session.State.Turn&&result?.P.Contains(q)==true)
                {
                    // A lesson step that wants one particular move answers any other instead of playing it.
                    var refusal=LessonRefusal(selected,q);if(refusal!=null){Notice(refusal);return;}
                    bool moved=session.TryMove(new Move(selected.Id,selected.Position,q),stateRevision,generation,out var reason);Record("HumanCommit",moved+";"+reason+";"+session.State.Hash);
                    if(moved){PlayPieceSound();selectedPiece="";QueueQuery();SaveMatchRecord();RefreshMatch();}return;
                }
                if(target!=null&&target.Side==session.State.Turn){selectedPiece=target.Id;QueueQuery();Record("MatchSelected",selectedPiece+";"+q);return;}
                // A click that does nothing says why, so a first-time player knows the game heard it.
                if(selected!=null&&selected.Side==session.State.Turn)Notice(target!=null&&target.Side!=session.State.Turn?"その駒は、選んだ駒では取れません。":"そのマスには動かせません。青いマスが動ける場所です。");
                else if(target!=null)Notice("相手の駒です。自分の駒をクリックして選んでください。");
                return;
            }
            if(!opened)return;
            if(tool=="配置"){PlaceAt(q);return;}if(tool=="消去"){preview.Erase(q);AfterTrial();return;}
            if(preview.Find(selectedPiece)!=null&&result?.P.Contains(q)==true&&preset!=null)
            {preview.TryMove(preset,selectedPiece,q,out var reason);Record("TrialMove",reason);AfterTrial();}
            else if(preview.At(q)!=null)
            {selectedPiece=preview.At(q).Id;selectedDefinition=preview.At(q).DefinitionId;BuildPieces();BuildDetails();QueueQuery();}
            Record("CellPick",q+";view="+viewRevision);
        }
        void PreviewScroll(PointerEventData e)
        {if(ownership.Owner!=InputOwner.None)return;distance=Mathf.Clamp(distance-e.scrollDelta.y*.035f,.24f,2);manualCamera=true;UpdateCamera();Record("CameraZoom",distance.ToString());}
        void SetTool(string value){tool=value;historyOwner="trial";Notice("試し配置："+value+" / 陣営 "+placementSide);}
        void PlaceAt(QN q)
        {preview.Place(new TrialPiece("trial-"+Guid.NewGuid().ToString("N"),selectedDefinition,placementSide,q));AfterTrial();}
        void AfterTrial(){historyOwner="trial";QueueQuery();stableDirty=true;saveDue=Time.unscaledTime+.3f;Record("Trial",preview.Hash);}
        void OnApplicationFocus(bool focused)
        {
            if(!focused){ownership.FocusLost();pointerButton=-1;foreach(var mini in minis)mini.CancelDrag();}
            Record("Focus",focused+";clockContinues="+(session?.Clock.Running==true));
        }
        void CancelActiveInput(){ownership.Cancel();pointerButton=-1;if(inputGate!=null)inputGate.interactable=true;}
    }
}
