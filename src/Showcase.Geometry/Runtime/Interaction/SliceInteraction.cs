using System;
using System.Collections.Generic;
using System.Linq;
namespace Showcase.Geometry.Interaction
{
    // VecN=(F,U,V,W,...); UI X=U,Y=V,Z=F,W=W, then A5..A11 = core axes 4..10 (displayed as V U T S R Q P); Unity adapter=(U,V,F).
    public enum LogicalAxis { X,Y,Z,W,A5,A6,A7,A8,A9,A10,A11 }
    public enum ReferenceView { XYZ,XYW,XZW,YZW }
    /// <summary>An axis-aligned section of an N-space: the three core axes it shows, in frame slot order (AxisX, AxisY, AxisZ).
    /// The other N-3 axes are hidden. The default value is the 4D XYZ view; a <see cref="ReferenceView"/> converts implicitly.</summary>
    public readonly struct ReferenceSection : IEquatable<ReferenceSection>
    {
        readonly int a,b,c,n;
        public ReferenceSection(IReadOnlyList<int> shown,int dimensions)
        {
            if(shown==null||shown.Count!=3||dimensions<3||dimensions>VecN.MaxDimensions||shown.Distinct().Count()!=3||shown.Any(x=>x<0||x>=dimensions))
                throw new ArgumentException("Three distinct core axes of an N-space (3.."+VecN.MaxDimensions+") are required.");
            a=shown[0];b=shown[1];c=shown[2];n=dimensions;
        }
        public int Dimensions=>n==0?4:n;
        /// <summary>Core axes in slot order: slot 0 is AxisX, 1 AxisY, 2 AxisZ of the frame.</summary>
        public int[] Shown=>n==0?new[]{0,1,2}:new[]{a,b,c};
        public int Slot(int slot)=>Shown[slot];
        public bool Shows(int coreAxis){var s=Shown;return s[0]==coreAxis||s[1]==coreAxis||s[2]==coreAxis;}
        /// <summary>Core axes the section holds fixed, ascending.</summary>
        public int[] Hidden{get{var hidden=new List<int>();for(int axis=0;axis<Dimensions;axis++)if(!Shows(axis))hidden.Add(axis);return hidden.ToArray();}}
        public static ReferenceSection Of(ReferenceView view,int dimensions=4)=>new ReferenceSection(SliceAxes.ShownAxes(view),dimensions);
        /// <summary>The initial section of an N-space: X, Y, Z shown, every other axis hidden.</summary>
        public static ReferenceSection Initial(int dimensions)=>Of(ReferenceView.XYZ,dimensions);
        /// <summary>The same three shown axes as a section of another N-space.</summary>
        public ReferenceSection In(int dimensions)=>dimensions==Dimensions?this:new ReferenceSection(Shown,dimensions);
        /// <summary>The 4D reference view with these shown axes in this slot order, when there is one (in any N: XYZ of a 5D space is still XYZ).</summary>
        public ReferenceView? View
        {
            get
            {
                var s=Shown;
                foreach(ReferenceView view in Enum.GetValues(typeof(ReferenceView))){var v=SliceAxes.ShownAxes(view);if(v[0]==s[0]&&v[1]==s[1]&&v[2]==s[2])return view;}
                return null;
            }
        }
        /// <summary>Shows a hidden axis in the slot of a shown one; the shown axis becomes hidden. For 4D this is the usual turn to another reference view.</summary>
        public ReferenceSection Swap(int shownCoreAxis,int hiddenCoreAxis)
        {
            if(!Shows(shownCoreAxis)||Shows(hiddenCoreAxis)||hiddenCoreAxis<0||hiddenCoreAxis>=Dimensions)throw new ArgumentException("Swap needs one shown and one hidden core axis of the section's space.");
            var s=Shown;for(int i=0;i<3;i++)if(s[i]==shownCoreAxis)s[i]=hiddenCoreAxis;return new ReferenceSection(s,Dimensions);
        }
        /// <summary>Sections one axis swap away: for each hidden axis ascending, the shown axes are replaced in descending UI order (Z, Y, X).
        /// From the 4D XYZ view this is XYW, XZW, YZW, the order of the original panels.</summary>
        public IReadOnlyList<ReferenceSection> Neighbours
        {
            get
            {
                var removed=Shown.OrderByDescending(axis=>(int)SliceAxes.AxisOfCore(axis)).ToArray();var list=new List<ReferenceSection>();
                foreach(int hidden in Hidden)foreach(int shown in removed)list.Add(Swap(shown,hidden));
                return list;
            }
        }
        /// <summary>Player-facing name: the shown axes in UI order, e.g. XYW or XYV.</summary>
        public string Name=>string.Concat(Shown.Select(SliceAxes.AxisOfCore).OrderBy(axis=>(int)axis).Select(SliceAxes.AxisName));
        /// <summary>The reference frame of this section through a centre; 4D views keep their exact original frames.</summary>
        public SliceFrame Frame(VecN center=default)
        {var view=View;return Dimensions==4&&view.HasValue?SliceAxes.OnLeg(view.Value,90,center):SliceAxes.ReferenceFrame(Shown,Dimensions,center);}
        public bool Equals(ReferenceSection other){var s=Shown;var t=other.Shown;return Dimensions==other.Dimensions&&s[0]==t[0]&&s[1]==t[1]&&s[2]==t[2];}
        public override bool Equals(object obj)=>obj is ReferenceSection other&&Equals(other);
        public override int GetHashCode(){var s=Shown;return ((Dimensions*31+s[0])*31+s[1])*31+s[2];}
        public static bool operator==(ReferenceSection x,ReferenceSection y)=>x.Equals(y);
        public static bool operator!=(ReferenceSection x,ReferenceSection y)=>!x.Equals(y);
        public static implicit operator ReferenceSection(ReferenceView view)=>Of(view);
        public override string ToString()=>Name;
    }
    public static class SliceAxes
    {
        public static VecN WorldAxis(LogicalAxis a)=>VecN.Unit(CoreAxis(a),Math.Max(4,CoreAxis(a)+1));
        /// <summary>UI axis of a core axis index (F=0→Z, U=1→X, V=2→Y, W=3→W, then 4→A5 ... 10→A11).</summary>
        public static LogicalAxis AxisOfCore(int coreAxis)
        {
            switch(coreAxis){case 0:return LogicalAxis.Z;case 1:return LogicalAxis.X;case 2:return LogicalAxis.Y;}
            if(coreAxis<3||coreAxis>=VecN.MaxDimensions)throw new ArgumentOutOfRangeException(nameof(coreAxis));
            return (LogicalAxis)coreAxis;
        }
        /// <summary>Label shown to the player: X Y Z W, then V U T S R Q P for the fifth to eleventh axes.</summary>
        public static string AxisName(LogicalAxis a)
        {int i=(int)a;return i<4?a.ToString():"VUTSRQP"[i-4].ToString();}
        /// <summary>UI axes of an N-space in display order.</summary>
        public static LogicalAxis[] Axes(int dimensions)
        {
            if(dimensions<3||dimensions>VecN.MaxDimensions)throw new ArgumentOutOfRangeException(nameof(dimensions));
            return Enumerable.Range(0,dimensions).Select(AxisOfCore).OrderBy(axis=>(int)axis).ToArray();
        }
        /// <summary>Core axes shown by a 4D reference view, in frame slot order (AxisX, AxisY, AxisZ).</summary>
        public static int[] ShownAxes(ReferenceView view)
        {switch(view){case ReferenceView.XYZ:return new[]{0,1,2};case ReferenceView.XYW:return new[]{3,1,2};case ReferenceView.XZW:return new[]{0,1,3};case ReferenceView.YZW:return new[]{0,3,2};default:throw new ArgumentOutOfRangeException(nameof(view));}}
        /// <summary>Reference frame of an N-space showing three core axes: slot i of the frame is the unit vector of shown[i]; the remaining axes,
        /// ascending, are the normals, the first of them negated when the permutation is odd so the basis stays positively oriented.
        /// For N=4 this reproduces <see cref="Reference(ReferenceView,VecN,int)"/> exactly.</summary>
        public static SliceFrame ReferenceFrame(int[] shown,int dimensions,VecN center=default)
        {
            if(shown==null||shown.Length!=3||dimensions<3||dimensions>VecN.MaxDimensions||shown.Distinct().Count()!=3||shown.Any(a=>a<0||a>=dimensions))throw new ArgumentException("Three distinct core axes of an N-space (3.."+VecN.MaxDimensions+") are required.");
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
        /// <summary>The section whose reference frame this is, or null when the frame is rotated away from every reference pose.</summary>
        public static ReferenceSection? SectionOf(SliceFrame frame)
        {
            int n=frame.Dimensions;var shown=new int[3];var slots=new[]{frame.AxisX,frame.AxisY,frame.AxisZ};
            for(int slot=0;slot<3;slot++)
            {
                int best=0;for(int axis=1;axis<n;axis++)if(Math.Abs(slots[slot].Axis(axis))>Math.Abs(slots[slot].Axis(best)))best=axis;
                if(slots[slot].Axis(best)<1-1e-9)return null;shown[slot]=best;
            }
            if(shown.Distinct().Count()!=3)return null;
            var section=new ReferenceSection(shown,n);
            return SameOrientation(frame,section.Frame(frame.Center))?section:(ReferenceSection?)null;
        }
        public static bool SameOrientation(SliceFrame a,SliceFrame b)
        {
            if(a.Dimensions!=b.Dimensions)return false;var x=a.Vectors;var y=b.Vectors;
            for(int i=0;i<x.Length;i++)if((x[i]-y[i]).Length>=1e-12)return false;return true;
        }
        public static ReferenceView ViewForAxis(LogicalAxis a)
        {switch(a){case LogicalAxis.W:return ReferenceView.XYZ;case LogicalAxis.Z:return ReferenceView.XYW;case LogicalAxis.Y:return ReferenceView.XZW;case LogicalAxis.X:return ReferenceView.YZW;default:throw new ArgumentOutOfRangeException(nameof(a));}}
        /// <summary>The section an axis label selects. In 4D it is the original view that hides the axis. In higher N a shown axis is swapped
        /// out for the lowest hidden axis, and a hidden axis is swapped in for the shown axis of highest UI order (Z before Y before X).</summary>
        public static ReferenceSection SectionForAxis(ReferenceSection current,LogicalAxis a)
        {
            int core=CoreAxis(a);
            if(current.Dimensions==4&&current.View.HasValue&&core<4)return ReferenceSection.Of(ViewForAxis(a));
            if(core>=current.Dimensions)return current;
            if(current.Shows(core)){var hidden=current.Hidden;return hidden.Length==0?current:current.Swap(core,hidden[0]);}
            if(current.Dimensions==4)return current;
            int removed=current.Shown.OrderByDescending(axis=>(int)AxisOfCore(axis)).First();return current.Swap(removed,core);
        }
        /// <summary>The 4D reference frame when the space is 4D; the same three shown axes with N-3 normals otherwise. N is given, or else
        /// the number of components the centre carries (at least 4), so a centre with trailing zeros still names its space.</summary>
        public static SliceFrame Reference(ReferenceView view,VecN center=default,int dimensions=0)
        {
            int n=dimensions>0?dimensions:Math.Max(4,center.Dimensions);
            return n==4?OnLeg(view,90,center):ReferenceFrame(ShownAxes(view),n,center);
        }
        public static SliceFrame Reference(ReferenceSection section,VecN center=default)=>section.Frame(center);
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
        {switch(a){case LogicalAxis.X:return 1;case LogicalAxis.Y:return 2;case LogicalAxis.Z:return 0;default:int i=(int)a;if(i<3||i>=VecN.MaxDimensions)throw new ArgumentOutOfRangeException(nameof(a));return i;}}
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
        /// <summary>The section that holds both ends of a step, chosen among the initial section, the current one and its one-swap neighbours:
        /// the first whose hidden axes the step leaves least. In 4D this is <see cref="ViewContaining"/>.</summary>
        public static ReferenceSection SectionContaining(VecN from,VecN to,ReferenceSection current)
        {
            if(!from.IsFinite||!to.IsFinite)throw new ArgumentException("Step ends must be finite.");
            if(current.Dimensions==4&&current.View.HasValue)return ReferenceSection.Of(ViewContaining(from,to,current.View));
            var order=new List<ReferenceSection>{ReferenceSection.Initial(current.Dimensions)};
            if(!order.Contains(current))order.Add(current);
            foreach(var section in current.Neighbours)if(!order.Contains(section))order.Add(section);
            var step=to-from;var best=order[0];double bestLeave=double.MaxValue;
            foreach(var section in order)
            {
                double leave=0;foreach(int hidden in section.Hidden){double d=step.Axis(hidden);leave+=d*d;}
                leave=Math.Sqrt(leave);if(leave<bestLeave-1e-9){best=section;bestLeave=leave;}
            }
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
        ReferenceSection? stable,pending;ReferenceSection target;double elapsed,pendingSeconds;bool active;SliceFrame start;
        readonly struct PlaneTurn
        {public readonly int A,B;public readonly double Angle;public PlaneTurn(int a,int b,double angle){A=a;B=b;Angle=angle;}}
        public PivotViewState DisplayState {get;private set;}
        public SliceFrame Frame=>DisplayState.Frame;
        public VecN Center=>Frame.Center;
        public long ViewRevision {get;private set;}
        public bool IsAnimating=>active;
        /// <summary>The 4D reference view the display rests in, if any; null while turning or in a section of another N-space.</summary>
        public ReferenceView? StableView=>active?null:stable?.View;
        /// <summary>The section the display rests in, if any; null while turning or when rotated away from every reference pose.</summary>
        public ReferenceSection? StableSection=>active?(ReferenceSection?)null:stable;
        public ReferenceView TargetView=>target.View??throw new InvalidOperationException("The target section is not a 4D reference view.");
        public ReferenceSection TargetSection=>target;
        public ReferenceView? QueuedView=>pending?.View;
        public ReferenceSection? QueuedSection=>pending;
        // The original constructor retains its zero embedding and chart anchor for CFG1 callers.
        public SliceAnimator(VecN center=default,ReferenceView view=ReferenceView.XYZ)
            :this(new PivotViewState(0,SliceAxes.Reference(view,center),default,ShapeFactory.CellPitchMetres,FiniteSliceWindow.DefaultHalfExtent)){legacyFramesOnly=true;}
        public SliceAnimator(PivotViewState state)
        {DisplayState=state??throw new ArgumentNullException(nameof(state));stable=SliceAxes.SectionOf(state.Frame);target=stable??ReferenceSection.Initial(Frame.Dimensions);}
        public bool Request(ReferenceView view)=>Request(ReferenceSection.Of(view,Frame.Dimensions));
        public bool Request(ReferenceSection section)
        {
            section=Fit(section);section.Frame(Center);
            if(active){if(target==section||pending==section)return false;pending=section;pendingSeconds=secondsPerLeg;return true;}
            if(stable==section)return false;Start(section,secondsPerLeg);return true;
        }
        ReferenceSection Fit(ReferenceSection section)
        {
            if(section.Dimensions==Frame.Dimensions)return section;
            if(section.Shown.Any(axis=>axis>=Frame.Dimensions))throw new ArgumentException("Reference section of a different dimension.");
            return section.In(Frame.Dimensions);
        }
        void Start(ReferenceSection section,double seconds)
        {
            target=section;start=Frame;turns.Clear();elapsed=0;active=true;legSeconds=seconds;
            var end=section.Frame(Center);
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
                if(t>=1){stable=target;DisplayState=DisplayState.WithOrientation(target.Frame(Center));active=false;if(pending.HasValue){var next=pending.Value;pending=null;if(next!=stable)Start(next,pendingSeconds);}}
            }
            return true;
        }
        public void CancelQueuedRequest(){pending=null;}
        // Legacy frame-only restore keeps its chart anchor. New saves restore the full display state.
        public void Restore(SliceFrame frame)
        {
            if(legacyFramesOnly&&!SliceAxes.SectionOf(frame).HasValue)throw new ArgumentException("Legacy CFG1 frame-only restore requires a stable reference pose. Pivot saves require the full display state.");
            Restore(new PivotViewState(DisplayState.EmbeddingHeight,frame,DisplayState.Anchor,DisplayState.CellScale,DisplayState.HalfWindow));
        }
        public void Restore(PivotViewState state)
        {
            if(state==null)throw new ArgumentNullException(nameof(state));
            SliceRotations.Validate(state.Frame);DisplayState=state;stable=SliceAxes.SectionOf(Frame);target=stable??ReferenceSection.Initial(Frame.Dimensions);
            pending=null;turns.Clear();active=false;elapsed=0;ViewRevision++;
        }
        public bool SetOrientation(SliceFrame frame)
        {
            var state=DisplayState.WithOrientation(frame);bool changed=active||pending.HasValue||!SliceAxes.SameOrientation(Frame,frame);
            DisplayState=state;active=false;pending=null;turns.Clear();elapsed=0;stable=SliceAxes.SectionOf(frame);target=stable??ReferenceSection.Initial(Frame.Dimensions);
            if(changed)ViewRevision++;return changed;
        }
        public bool SetCenter(VecN value){if(!value.IsFinite)throw new ArgumentException("Slice center must be finite.");if((value-Center).Length<1e-14)return false;DisplayState=DisplayState.WithCenter(value);ViewRevision++;return true;}
        // Sets C and its display anchor together without stopping a running turn, so a view can glide back to a saved one.
        public void Place(VecN center,Vec3 anchor)
        {
            if(!center.IsFinite||!anchor.IsFinite)throw new ArgumentException("Slice center and anchor must be finite.");
            DisplayState=new PivotViewState(DisplayState.EmbeddingHeight,Frame.WithCenter(center),anchor,DisplayState.CellScale,DisplayState.HalfWindow);ViewRevision++;
        }
        public bool Translate(LogicalAxis axis,double cells)
        {
            if(double.IsNaN(cells)||double.IsInfinity(cells))throw new ArgumentOutOfRangeException(nameof(cells));
            if(SliceAxes.CoreAxis(axis)>=Frame.Dimensions)throw new ArgumentOutOfRangeException(nameof(axis),"The axis is not part of the frame's "+Frame.Dimensions+"D space.");
            return SetCenter(Center+SliceAxes.WorldAxis(axis)*cells);
        }
    }
}
