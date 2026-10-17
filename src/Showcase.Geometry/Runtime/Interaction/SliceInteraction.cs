using System;
using System.Collections.Generic;
namespace Showcase.Geometry.Interaction
{
    // Vec4=(F,U,V,W); UI X=U,Y=V,Z=F,W=W; Unity adapter=(U,V,F).
    public enum LogicalAxis { X,Y,Z,W }
    public enum ReferenceView { XYZ,XYW,XZW,YZW }
    public static class SliceAxes
    {
        public static Vec4 WorldAxis(LogicalAxis a)
        {switch(a){case LogicalAxis.X:return new Vec4(0,1,0,0);case LogicalAxis.Y:return new Vec4(0,0,1,0);case LogicalAxis.Z:return new Vec4(1,0,0,0);case LogicalAxis.W:return new Vec4(0,0,0,1);default:throw new ArgumentOutOfRangeException(nameof(a));}}
        public static ReferenceView ViewForAxis(LogicalAxis a)
        {switch(a){case LogicalAxis.W:return ReferenceView.XYZ;case LogicalAxis.Z:return ReferenceView.XYW;case LogicalAxis.Y:return ReferenceView.XZW;case LogicalAxis.X:return ReferenceView.YZW;default:throw new ArgumentOutOfRangeException(nameof(a));}}
        public static SliceFrame Reference(ReferenceView view,Vec4 center=default)=>OnLeg(view,90,center);
        public static SliceFrame OnLeg(ReferenceView view,double degrees,Vec4 center=default)
        {
            if(!center.IsFinite||double.IsNaN(degrees)||double.IsInfinity(degrees))throw new ArgumentException("Frame values must be finite.");
            var f=new Vec4(1,0,0,0);var u=new Vec4(0,1,0,0);var v=new Vec4(0,0,1,0);var w=new Vec4(0,0,0,1);
            double a=degrees*Math.PI/180,c=Math.Cos(a),s=Math.Sin(a);
            if(degrees==0){c=1;s=0;}else if(degrees==90){c=0;s=1;}
            switch(view){case ReferenceView.XYZ:return new SliceFrame(center,f,u,v,w);case ReferenceView.XYW:return new SliceFrame(center,f*c+w*s,u,v,f*(-s)+w*c);case ReferenceView.XZW:return new SliceFrame(center,f,u,v*c+w*s,v*(-s)+w*c);case ReferenceView.YZW:return new SliceFrame(center,f,u*c+w*s,v,u*(-s)+w*c);default:throw new ArgumentOutOfRangeException(nameof(view));}
        }
        public static string DisplayName(ReferenceView view)=>view.ToString();
        // Core axis (F=0,U=1,V=2,W=3) that each reference hyperplane holds fixed.
        public static int NormalCoreAxis(ReferenceView view)
        {switch(view){case ReferenceView.XYZ:return 3;case ReferenceView.XYW:return 0;case ReferenceView.XZW:return 2;case ReferenceView.YZW:return 1;default:throw new ArgumentOutOfRangeException(nameof(view));}}
        // Reference hyperplane that holds both ends of a step: XYZ first, then the current view, then XZW, XYW, YZW.
        // A step that leaves every reference hyperplane gets the one it leaves least.
        public static ReferenceView ViewContaining(Vec4 from,Vec4 to,ReferenceView? current=null)
        {
            if(!from.IsFinite||!to.IsFinite)throw new ArgumentException("Step ends must be finite.");
            var order=new List<ReferenceView>{ReferenceView.XYZ};
            if(current.HasValue&&!order.Contains(current.Value))order.Add(current.Value);
            foreach(var view in new[]{ReferenceView.XZW,ReferenceView.XYW,ReferenceView.YZW})if(!order.Contains(view))order.Add(view);
            var best=order[0];double bestStep=double.MaxValue;
            foreach(var view in order){double step=Math.Abs(SliceRotations.Component(to-from,NormalCoreAxis(view)));if(step<bestStep-1e-9){best=view;bestStep=step;}}
            return best;
        }
    }
    public sealed class SliceAnimator
    {
        public const double SecondsPerQuarterTurn=.45;
        double secondsPerLeg=SecondsPerQuarterTurn,legSeconds=SecondsPerQuarterTurn;
        // Duration of legs started from now on; a running leg keeps the duration it started with.
        public double SecondsPerLeg {get=>secondsPerLeg;set{if(!(value>0)||double.IsInfinity(value))throw new ArgumentOutOfRangeException(nameof(value));secondsPerLeg=value;}}
        readonly List<PlaneTurn> turns=new List<PlaneTurn>();
        readonly bool legacyFramesOnly;
        ReferenceView? stable,pending;ReferenceView target;double elapsed;bool active;SliceFrame start;
        readonly struct PlaneTurn
        {public readonly int A,B;public readonly double Angle;public PlaneTurn(int a,int b,double angle){A=a;B=b;Angle=angle;}}
        public PivotViewState DisplayState {get;private set;}
        public SliceFrame Frame=>DisplayState.Frame;
        public Vec4 Center=>Frame.Center;
        public long ViewRevision {get;private set;}
        public bool IsAnimating=>active;
        public ReferenceView? StableView=>active?(ReferenceView?)null:stable;
        public ReferenceView TargetView=>target;
        public ReferenceView? QueuedView=>pending;
        // The original constructor retains its zero embedding and chart anchor for CFG1 callers.
        public SliceAnimator(Vec4 center=default,ReferenceView view=ReferenceView.XYZ)
            :this(new PivotViewState(0,SliceAxes.Reference(view,center),default,ShapeFactory.CellPitchMetres,FiniteSliceWindow.DefaultHalfExtent)){legacyFramesOnly=true;}
        public SliceAnimator(PivotViewState state)
        {DisplayState=state??throw new ArgumentNullException(nameof(state));stable=FindReference(state.Frame);target=stable??ReferenceView.XYZ;}
        public bool Request(ReferenceView view)
        {
            SliceAxes.Reference(view,Center);
            if(active){if(target==view||pending==view)return false;pending=view;return true;}
            if(stable==view)return false;Start(view);return true;
        }
        void Start(ReferenceView view)
        {
            target=view;start=Frame;turns.Clear();elapsed=0;active=true;legSeconds=secondsPerLeg;
            var end=SliceAxes.Reference(view,Center);
            var a=new[]{start.AxisX,start.AxisY,start.AxisZ,start.Normal};var b=new[]{end.AxisX,end.AxisY,end.AxisZ,end.Normal};
            var relative=new double[4,4];
            for(int r=0;r<4;r++)for(int c=0;c<4;c++)for(int k=0;k<4;k++)
                relative[r,c]+=SliceRotations.Component(b[k],r)*SliceRotations.Component(a[k],c);
            // Deterministic QR by left Givens rotations. R=G1^-1 ... G6^-1.
            // Scaling each factor's angle stays in SO(4), including 180-degree cases.
            for(int column=0;column<3;column++)for(int row=3;row>column;row--)
            {
                double x=relative[column,column],y=relative[row,column],length=Math.Sqrt(x*x+y*y);
                if(length<1e-14)continue;
                if(Math.Abs(y)<1e-15)y=0; // deterministic +pi branch for an exact negative pivot
                double angle=Math.Atan2(y,x),c=Math.Cos(angle),s=Math.Sin(angle);
                for(int k=0;k<4;k++){double top=relative[column,k],bottom=relative[row,k];relative[column,k]=c*top+s*bottom;relative[row,k]=-s*top+c*bottom;}
                if(Math.Abs(angle)>1e-15)turns.Add(new PlaneTurn(column,row,angle));
            }
            ViewRevision++;
        }
        public bool Advance(double seconds)
        {
            if(seconds<0||double.IsNaN(seconds)||double.IsInfinity(seconds))throw new ArgumentOutOfRangeException(nameof(seconds));
            if(!active||seconds==0)return false;
            while(active&&seconds>0)
            {
                double take=Math.Min(seconds,legSeconds-elapsed);elapsed+=take;seconds-=take;
                double t=Math.Min(1,elapsed/legSeconds),s=t*t*(3-2*t);
                var pose=new SliceFrame(Center,start.AxisX,start.AxisY,start.AxisZ,start.Normal);
                for(int i=turns.Count-1;i>=0;i--)pose=SliceRotations.Rotate(pose,turns[i].A,turns[i].B,turns[i].Angle*s);
                DisplayState=DisplayState.WithOrientation(pose);ViewRevision++;
                if(t>=1){stable=target;DisplayState=DisplayState.WithOrientation(SliceAxes.Reference(target,Center));active=false;if(pending.HasValue){var next=pending.Value;pending=null;if(next!=stable)Start(next);}}
            }
            return true;
        }
        public void CancelQueuedRequest(){pending=null;}
        // Legacy frame-only restore keeps its chart anchor. New saves restore the full display state.
        public void Restore(SliceFrame frame)
        {
            if(legacyFramesOnly&&!FindReference(frame).HasValue)throw new ArgumentException("Legacy CFG1 frame-only restore requires a stable reference pose. Pivot saves require the full display state.");
            Restore(new PivotViewState(DisplayState.EmbeddingHeight,frame,DisplayState.Anchor,DisplayState.CellScale,DisplayState.HalfWindow));
        }
        public void Restore(PivotViewState state)
        {
            if(state==null)throw new ArgumentNullException(nameof(state));
            SliceRotations.Validate(state.Frame);DisplayState=state;stable=FindReference(Frame);target=stable??ReferenceView.XYZ;
            pending=null;turns.Clear();active=false;elapsed=0;ViewRevision++;
        }
        public bool SetOrientation(SliceFrame frame)
        {
            var state=DisplayState.WithOrientation(frame);bool changed=active||pending.HasValue||!SameOrientation(Frame,frame);
            DisplayState=state;active=false;pending=null;turns.Clear();elapsed=0;stable=FindReference(frame);target=stable??ReferenceView.XYZ;
            if(changed)ViewRevision++;return changed;
        }
        static ReferenceView? FindReference(SliceFrame frame)
        {
            foreach(ReferenceView view in Enum.GetValues(typeof(ReferenceView)))
            {
                var reference=SliceAxes.Reference(view,frame.Center);
                if(SameOrientation(frame,reference))return view;
            }
            return null;
        }
        static bool SameOrientation(SliceFrame a,SliceFrame b)=>(a.AxisX-b.AxisX).Length<1e-12&&(a.AxisY-b.AxisY).Length<1e-12&&(a.AxisZ-b.AxisZ).Length<1e-12&&(a.Normal-b.Normal).Length<1e-12;
        public bool SetCenter(Vec4 value){if(!value.IsFinite)throw new ArgumentException("Slice center must be finite.");if((value-Center).Length<1e-14)return false;DisplayState=DisplayState.WithCenter(value);ViewRevision++;return true;}
        // Sets C and its display anchor together without stopping a running turn, so a view can glide back to a saved one.
        public void Place(Vec4 center,Vec3 anchor)
        {
            if(!center.IsFinite||!anchor.IsFinite)throw new ArgumentException("Slice center and anchor must be finite.");
            var f=Frame;DisplayState=new PivotViewState(DisplayState.EmbeddingHeight,new SliceFrame(center,f.AxisX,f.AxisY,f.AxisZ,f.Normal),anchor,DisplayState.CellScale,DisplayState.HalfWindow);ViewRevision++;
        }
        public bool Translate(LogicalAxis axis,double cells){if(double.IsNaN(cells)||double.IsInfinity(cells))throw new ArgumentOutOfRangeException(nameof(cells));return SetCenter(Center+SliceAxes.WorldAxis(axis)*cells);}
    }
}
