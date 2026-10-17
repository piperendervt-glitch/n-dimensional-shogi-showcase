using System;
namespace Showcase.Geometry.Interaction
{
    public enum InputOwner { None,Form,Board,Axis,CameraOrbit,CameraPan,Rotation4D }
    public sealed class InputOwnership
    {
        Action? rollback;
        public InputOwner Owner {get;private set;}
        public int PointerId {get;private set;}=-1;
        public long Revision {get;private set;}
        public bool TryBegin(InputOwner owner,int pointerId,Action? onCancel=null)
        {if(owner==InputOwner.None||Owner!=InputOwner.None)return false;Owner=owner;PointerId=pointerId;rollback=onCancel;Revision++;return true;}
        public bool IsOwner(InputOwner owner,int pointerId)=>Owner==owner&&PointerId==pointerId;
        public bool Release(InputOwner owner,int pointerId)
        {if(!IsOwner(owner,pointerId))return false;Owner=InputOwner.None;PointerId=-1;rollback=null;Revision++;return true;}
        public void Cancel(){var action=rollback;rollback=null;Owner=InputOwner.None;PointerId=-1;Revision++;action?.Invoke();}
        public void FocusLost()=>Cancel();
    }
    public sealed class AxisDrag
    {
        public const double PixelsPerCell=32,MinimumProjectedAxisPixels=6;
        readonly SliceAnimator slice;readonly InputOwnership ownership;
        VecN original;PivotViewState? originalState;LogicalAxis axis;int pointer;double startX,startY,unitX,unitY;
        public bool IsActive {get;private set;}
        public LogicalAxis Axis=>axis;
        public string RejectionReason {get;private set;}="";
        public AxisDrag(SliceAnimator slice,InputOwnership ownership){this.slice=slice;this.ownership=ownership;}
        public bool Begin(LogicalAxis axis,int pointerId,double x,double y,double projectedAxisX,double projectedAxisY)
        {
            RejectionReason="";double length=Math.Sqrt(projectedAxisX*projectedAxisX+projectedAxisY*projectedAxisY);
            if(!Finite(x)||!Finite(y)||!Finite(length)||length<=MinimumProjectedAxisPixels){RejectionReason="Axis is nearly perpendicular to the screen; choose another view.";return false;}
            if(IsActive||slice.IsAnimating){RejectionReason="Wait for the current slice rotation or drag.";return false;}
            if(SliceAxes.CoreAxis(axis)>=slice.Frame.Dimensions){RejectionReason="This board has no "+SliceAxes.AxisName(axis)+" axis.";return false;}
            if(!ownership.TryBegin(InputOwner.Axis,pointerId,Rollback)){RejectionReason="Another control owns the pointer.";return false;}
            this.axis=axis;pointer=pointerId;startX=x;startY=y;unitX=projectedAxisX/length;unitY=projectedAxisY/length;original=slice.Center;originalState=slice.DisplayState;IsActive=true;return true;
        }
        // snap rounds the dragged coordinate of C to whole cells, like grid snapping in a 3D editor.
        public bool Update(int pointerId,double x,double y,bool snap=false)
        {
            if(!IsActive||pointerId!=pointer||!ownership.IsOwner(InputOwner.Axis,pointerId)||!Finite(x)||!Finite(y))return false;
            double amount=((x-startX)*unitX+(y-startY)*unitY)/PixelsPerCell;
            // Board cells are whole numbers in core coordinates; the world Y axis also carries the embedding height.
            if(snap){double start=VecN.Dot(slice.DisplayState.WorldToCore(original),SliceAxes.WorldAxis(axis));amount=Math.Round(start+amount,MidpointRounding.AwayFromZero)-start;}
            return slice.SetCenter(original+SliceAxes.WorldAxis(axis)*amount);
        }
        public bool Complete(int pointerId)
        {if(!IsActive||pointerId!=pointer||!ownership.Release(InputOwner.Axis,pointerId))return false;IsActive=false;return true;}
        public void Cancel(){if(IsActive)ownership.Cancel();}
        void Rollback(){if(!IsActive)return;if(originalState!=null)slice.Restore(originalState);IsActive=false;}
        static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    }
    // CellId and PickToken are the 4D cells of the CFG1 pick tests; N-dimensional cells are QN (Showcase.Core).
    public readonly struct CellId : IEquatable<CellId>
    {
        public readonly int F,U,V,W;
        public CellId(int f,int u,int v,int w){F=f;U=u;V=v;W=w;}
        public VecN Position=>new VecN(F,U,V,W);
        public bool Equals(CellId other)=>F==other.F&&U==other.U&&V==other.V&&W==other.W;
        public override bool Equals(object? other)=>other is CellId id&&Equals(id);
        public override int GetHashCode()=>(((F*397)^U)*397^V)*397^W;
        public override string ToString()=>F+","+U+","+V+","+W;
    }
    public readonly struct PickToken
    {
        public readonly CellId Cell;
        public readonly long RequestId,ViewRevision,TrialRevision,DraftRevision,CameraRevision,LayoutRevision,SessionRevision;
        public PickToken(CellId cell,long viewRevision,long trialRevision,long draftRevision,long requestId=0,long cameraRevision=0,long layoutRevision=0,long sessionRevision=0)
        {Cell=cell;ViewRevision=viewRevision;TrialRevision=trialRevision;DraftRevision=draftRevision;RequestId=requestId;CameraRevision=cameraRevision;LayoutRevision=layoutRevision;SessionRevision=sessionRevision;}
        public bool IsCurrent(long viewRevision,long trialRevision,long draftRevision)=>ViewRevision==viewRevision&&TrialRevision==trialRevision&&DraftRevision==draftRevision;
        public bool IsCurrent(long viewRevision,long trialRevision,long draftRevision,long cameraRevision,long layoutRevision,long sessionRevision)
            =>IsCurrent(viewRevision,trialRevision,draftRevision)&&CameraRevision==cameraRevision&&LayoutRevision==layoutRevision&&SessionRevision==sessionRevision;
    }
    public readonly struct CellHit
    {
        public readonly PickToken Token;
        public readonly Vec3 SectionPosition;
        public CellHit(PickToken token,Vec3 position){Token=token;SectionPosition=position;}
        public bool CanAccept(PickToken request,long currentView,long currentTrial,long currentDraft)
            =>SectionPosition.IsFinite&&Token.RequestId==request.RequestId&&Token.Cell.Equals(request.Cell)&&Token.IsCurrent(currentView,currentTrial,currentDraft)&&request.IsCurrent(currentView,currentTrial,currentDraft);
        public bool CanAccept(PickToken request,long currentView,long currentTrial,long currentDraft,long currentCamera,long currentLayout,long currentSession)
            =>CanAccept(request,currentView,currentTrial,currentDraft)&&Token.IsCurrent(currentView,currentTrial,currentDraft,currentCamera,currentLayout,currentSession)&&request.IsCurrent(currentView,currentTrial,currentDraft,currentCamera,currentLayout,currentSession);
    }
}
