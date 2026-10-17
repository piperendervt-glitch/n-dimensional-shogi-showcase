using System;
using System.Collections.Generic;
using System.Linq;
namespace Showcase.Geometry.Interaction
{
    // VecN=(F,U,V,W,...); UI X=U,Y=V,Z=F,W=W, then A5..A7 = core axes 4..6 (displayed as V, U, T); Unity adapter=(U,V,F).
    public enum LogicalAxis { X,Y,Z,W,A5,A6,A7 }
    public enum ReferenceView { XYZ,XYW,XZW,YZW }
    public static class SliceAxes
    {
        public static VecN WorldAxis(LogicalAxis a)=>VecN.Unit(CoreAxis(a));
        /// <summary>UI axis of a core axis index (F=0→Z, U=1→X, V=2→Y, W=3→W, 4→A5, 5→A6, 6→A7).</summary>
        public static LogicalAxis AxisOfCore(int coreAxis)
        {switch(coreAxis){case 0:return LogicalAxis.Z;case 1:return LogicalAxis.X;case 2:return LogicalAxis.Y;case 3:return LogicalAxis.W;case 4:return LogicalAxis.A5;case 5:return LogicalAxis.A6;case 6:return LogicalAxis.A7;default:throw new ArgumentOutOfRangeException(nameof(coreAxis));}}
        /// <summary>Label shown to the player: X Y Z W, then V U T for the fifth to seventh axes.</summary>
        public static string AxisName(LogicalAxis a)
        {switch(a){case LogicalAxis.A5:return "V";case LogicalAxis.A6:return "U";case LogicalAxis.A7:return "T";default:return a.ToString();}}
        /// <summary>Core axes shown by a 4D reference view, in frame slot order (AxisX, AxisY, AxisZ).</summary>
        public static int[] ShownAxes(ReferenceView view)
        {switch(view){case ReferenceView.XYZ:return new[]{0,1,2};case ReferenceView.XYW:return new[]{3,1,2};case ReferenceView.XZW:return new[]{0,1,3};case ReferenceView.YZW:return new[]{0,3,2};default:throw new ArgumentOutOfRangeException(nameof(view));}}
        /// <summary>Reference frame of an N-space showing three core axes: slot i of the frame is the unit vector of shown[i]; the remaining axes,
        /// ascending, are the normals, the first of them negated when the permutation is odd so the basis stays positively oriented.
        /// For N=4 this reproduces <see cref="Reference(ReferenceView,VecN)"/> exactly.</summary>
        public static SliceFrame ReferenceFrame(int[] shown,int dimensions,VecN center=default)
        {
            if(shown==null||shown.Length!=3||dimensions<3||dimensions>VecN.MaxDimensions||shown.Distinct().Count()!=3||shown.Any(a=>a<0||a>=dimensions))throw new ArgumentException("Three distinct core axes of an N-space (3..7) are required.");
            if(!center.IsFinite||center.Rank>dimensions)throw new ArgumentException("Frame centre must be finite and within the space.");
            var order=new List<int>(shown);for(int a=0;a<dimensions;a++)if(!order.Contains(a))order.Add(a);
            int inversions=0;for(int i=0;i<order.Count;i++)for(int j=i+1;j<order.Count;j++)if(order[i]>order[j])inversions++;
            var vectors=new VecN[dimensions];for(int i=0;i<dimensions;i++)vectors[i]=VecN.Unit(order[i],dimensions);
            if(inversions%2==1){if(dimensions==3)throw new ArgumentException("A 3D frame cannot show its axes in an odd order.");vectors[3]=-vectors[3];}
            return SliceFrame.FromVectors(center,vectors);
        }
        /// <summary>Core axes a frame holds fixed, i.e. the axes none of its three slice axes points along; empty when the frame is not axis-aligned.</summary>
        public static int[] HiddenAxes(SliceFrame frame)
        {
            var hidden=new List<int>();
            for(int a=0;a<frame.Dimensions;a++)if(Math.Abs(frame.AxisX.Axis(a))<1e-9&&Math.Abs(frame.AxisY.Axis(a))<1e-9&&Math.Abs(frame.AxisZ.Axis(a))<1e-9)hidden.Add(a);
            return hidden.Count==frame.Dimensions-3?hidden.ToArray():Array.Empty<int>();
        }
        public static ReferenceView ViewForAxis(LogicalAxis a)
        {switch(a){case LogicalAxis.W:return ReferenceView.XYZ;case LogicalAxis.Z:return ReferenceView.XYW;case LogicalAxis.Y:return ReferenceView.XZW;case LogicalAxis.X:return ReferenceView.YZW;default:throw new ArgumentOutOfRangeException(nameof(a));}}
        /// <summary>The 4D reference frame when the space is 4D; the same three shown axes with N-3 normals otherwise (N given, or taken from the centre).</summary>
        public static SliceFrame Reference(ReferenceView view,VecN center=default,int dimensions=0)
        {
            int n=dimensions>0?dimensions:Math.Max(4,center.Rank);
            return n==4?OnLeg(view,90,center):ReferenceFrame(ShownAxes(view),n,center);
        }
        public static SliceFrame OnLeg(ReferenceView view,double degrees,VecN center=default)
        {
            if(!center.IsFinite||double.IsNaN(degrees)||double.IsInfinity(degrees))throw new ArgumentException("Frame values must be finite.");
            var f=new VecN(1,0,0,0);var u=new VecN(0,1,0,0);var v=new VecN(0,0,1,0);var w=new VecN(0,0,0,1);
            double a=degrees*Math.PI/180,c=Math.Cos(a),s=Math.Sin(a);
            if(degrees==0){c=1;s=0;}else if(degrees==90){c=0;s=1;}
            switch(view){case ReferenceView.XYZ:return new SliceFrame(center,f,u,v,w);case ReferenceView.XYW:return new SliceFrame(center,f*c+w*s,u,v,f*(-s)+w*c);case ReferenceView.XZW:return new SliceFrame(center,f,u,v*c+w*s,v*(-s)+w*c);case ReferenceView.YZW:return new SliceFrame(center,f,u*c+w*s,v,u*(-s)+w*c);default:throw new ArgumentOutOfRangeException(nameof(view));}
        }
        public static string DisplayName(ReferenceView view)=>view.ToString();
        // VecN components are core axes, not UI axes: VecN.X is UI Z. Use these instead of writing the permutation out.
        public static int CoreAxis(LogicalAxis a)
        {switch(a){case LogicalAxis.X:return 1;case LogicalAxis.Y:return 2;case LogicalAxis.Z:return 0;case LogicalAxis.W:return 3;case LogicalAxis.A5:return 4;case LogicalAxis.A6:return 5;case LogicalAxis.A7:return 6;default:throw new ArgumentOutOfRangeException(nameof(a));}}
        // UI axis that each reference hyperplane holds fixed (the axis whose button selects it).
        public static LogicalAxis NormalAxis(ReferenceView view)
        {switch(view){case ReferenceView.XYZ:return LogicalAxis.W;case ReferenceView.XYW:return LogicalAxis.Z;case ReferenceView.XZW:return LogicalAxis.Y;case ReferenceView.YZW:return LogicalAxis.X;default:throw new ArgumentOutOfRangeException(nameof(view));}}
        public static double Coordinate(VecN value,int coreAxis)=>SliceRotations.Component(value,coreAxis);
        public static VecN WithCoordinate(VecN value,int coreAxis,double coordinate)=>value.WithAxis(coreAxis,coordinate);
        // Core axis (F=0,U=1,V=2,W=3) that each reference hyperplane holds fixed.
        public static int NormalCoreAxis(ReferenceView view)
        {switch(view){case ReferenceView.XYZ:return 3;case ReferenceView.XYW:return 0;case ReferenceView.XZW:return 2;case ReferenceView.YZW:return 1;default:throw new ArgumentOutOfRangeException(nameof(view));}}
        // Reference hyperplane that holds both ends of a step: XYZ first, then the current view, then XZW, XYW, YZW.
        // A step that leaves every reference hyperplane gets the one it leaves least.
        public static ReferenceView ViewContaining(VecN from,VecN to,ReferenceView? current=null)
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
        // Duration of legs requested from now on; a running or queued leg keeps the duration it was requested with.
        public double SecondsPerLeg {get=>secondsPerLeg;set{if(!(value>0)||double.IsInfinity(value))throw new ArgumentOutOfRangeException(nameof(value));secondsPerLeg=value;}}
        readonly List<PlaneTurn> turns=new List<PlaneTurn>();
        readonly bool legacyFramesOnly;
        ReferenceView? stable,pending;ReferenceView target;double elapsed,pendingSeconds;bool active;SliceFrame start;
        readonly struct PlaneTurn
        {public readonly int A,B;public readonly double Angle;public PlaneTurn(int a,int b,double angle){A=a;B=b;Angle=angle;}}
        public PivotViewState DisplayState {get;private set;}
        public SliceFrame Frame=>DisplayState.Frame;
        public VecN Center=>Frame.Center;
        public long ViewRevision {get;private set;}
        public bool IsAnimating=>active;
        public ReferenceView? StableView=>active?(ReferenceView?)null:stable;
        public ReferenceView TargetView=>target;
        public ReferenceView? QueuedView=>pending;
        // The original constructor retains its zero embedding and chart anchor for CFG1 callers.
        public SliceAnimator(VecN center=default,ReferenceView view=ReferenceView.XYZ)
            :this(new PivotViewState(0,SliceAxes.Reference(view,center),default,ShapeFactory.CellPitchMetres,FiniteSliceWindow.DefaultHalfExtent)){legacyFramesOnly=true;}
        public SliceAnimator(PivotViewState state)
        {DisplayState=state??throw new ArgumentNullException(nameof(state));stable=FindReference(state.Frame);target=stable??ReferenceView.XYZ;}
        public bool Request(ReferenceView view)
        {
            SliceAxes.Reference(view,Center,Frame.Dimensions);
            if(active){if(target==view||pending==view)return false;pending=view;pendingSeconds=secondsPerLeg;return true;}
            if(stable==view)return false;Start(view,secondsPerLeg);return true;
        }
        void Start(ReferenceView view,double seconds)
        {
            target=view;start=Frame;turns.Clear();elapsed=0;active=true;legSeconds=seconds;
            var end=SliceAxes.Reference(view,Center,Frame.Dimensions);
            var a=start.Vectors;var b=end.Vectors;int n=a.Length;
            if(b.Length!=n)throw new InvalidOperationException("Reference frame of a different dimension.");
            var relative=new double[n,n];
            for(int r=0;r<n;r++)for(int c=0;c<n;c++)for(int k=0;k<n;k++)
                relative[r,c]+=SliceRotations.Component(b[k],r)*SliceRotations.Component(a[k],c);
            // Deterministic QR by left Givens rotations. R=G1^-1 ... Gm^-1 (m=N(N-1)/2).
            // Scaling each factor's angle stays in SO(N), including 180-degree cases.
            for(int column=0;column<n-1;column++)for(int row=n-1;row>column;row--)
            {
                double x=relative[column,column],y=relative[row,column],length=Math.Sqrt(x*x+y*y);
                if(length<1e-14)continue;
                if(Math.Abs(y)<1e-15)y=0; // deterministic +pi branch for an exact negative pivot
                double angle=Math.Atan2(y,x),c=Math.Cos(angle),s=Math.Sin(angle);
                for(int k=0;k<n;k++){double top=relative[column,k],bottom=relative[row,k];relative[column,k]=c*top+s*bottom;relative[row,k]=-s*top+c*bottom;}
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
                var pose=start.WithCenter(Center);
                for(int i=turns.Count-1;i>=0;i--)pose=SliceRotations.Rotate(pose,turns[i].A,turns[i].B,turns[i].Angle*s);
                DisplayState=DisplayState.WithOrientation(pose);ViewRevision++;
                if(t>=1){stable=target;DisplayState=DisplayState.WithOrientation(SliceAxes.Reference(target,Center,Frame.Dimensions));active=false;if(pending.HasValue){var next=pending.Value;pending=null;if(next!=stable)Start(next,pendingSeconds);}}
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
                var reference=SliceAxes.Reference(view,frame.Center,frame.Dimensions);
                if(SameOrientation(frame,reference))return view;
            }
            return null;
        }
        static bool SameOrientation(SliceFrame a,SliceFrame b)
        {
            if(a.Dimensions!=b.Dimensions)return false;var x=a.Vectors;var y=b.Vectors;
            for(int i=0;i<x.Length;i++)if((x[i]-y[i]).Length>=1e-12)return false;return true;
        }
        public bool SetCenter(VecN value){if(!value.IsFinite)throw new ArgumentException("Slice center must be finite.");if((value-Center).Length<1e-14)return false;DisplayState=DisplayState.WithCenter(value);ViewRevision++;return true;}
        // Sets C and its display anchor together without stopping a running turn, so a view can glide back to a saved one.
        public void Place(VecN center,Vec3 anchor)
        {
            if(!center.IsFinite||!anchor.IsFinite)throw new ArgumentException("Slice center and anchor must be finite.");
            DisplayState=new PivotViewState(DisplayState.EmbeddingHeight,Frame.WithCenter(center),anchor,DisplayState.CellScale,DisplayState.HalfWindow);ViewRevision++;
        }
        public bool Translate(LogicalAxis axis,double cells){if(double.IsNaN(cells)||double.IsInfinity(cells))throw new ArgumentOutOfRangeException(nameof(cells));return SetCenter(Center+SliceAxes.WorldAxis(axis)*cells);}
    }
}
