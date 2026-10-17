using System;
using System.Linq;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;

static class PivotTests
{
    static Action<bool,string> check=null!;
    static void Near(double a,double b,string message,double epsilon=1e-9)=>check(Math.Abs(a-b)<=epsilon,message+": "+a+" != "+b);
    static void Same(Vec3 a,Vec3 b,string message)=>Near((a-b).Length,0,message);
    static void Same(Vec4 a,Vec4 b,string message)=>Near((a-b).Length,0,message);
    static void Pose(SliceFrame a,SliceFrame b,string message)
    {Same(a.Center,b.Center,message+" C");Same(a.AxisX,b.AxisX,message+" b1");Same(a.AxisY,b.AxisY,message+" b2");Same(a.AxisZ,b.AxisZ,message+" b3");Same(a.Normal,b.Normal,message+" n");}
    static PivotViewState Initial()=>PivotViewState.CreateInitial(new Vec3(0,.86,0));
    static void Reject(Action action,string message){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}check(rejected,message);}
    public static void Run(Action<bool,string> assertions,Action<object> addCase)
    {
        check=assertions;EmbeddingAndPivots();GeneralOrientation();MiniIntersections();InputAndVersions();SnapAndCpuView();
        addCase(new{name="pivot_embedding_center_anchor",status="PASS",acceptance=new[]{"PV01","PV02","PV03","PV04","PV05"},heightCells=3,boardAnchorMetres=new[]{0,.86,0},note="Independent coordinates and actual finite wooden-solid sections; no Unity raster/pick certification."});
        addCase(new{name="general_so4_and_input_partition",status="PASS",acceptance=new[]{"PV09","PV10","PV13"},interpolation="Deterministic Givens factors, continuous SO(4), 180-degree and near-180-degree tested",note="Pure geometry and revision checks only; OS input and saved-file restart tested separately."});
    }
    static void SnapAndCpuView()
    {
        // Axis drag snapping: the dragged coordinate of C lands on whole cells, so the start cell is reachable exactly.
        var a=new SliceAnimator(Initial());var ownership=new InputOwnership();var drag=new AxisDrag(a,ownership);
        check(drag.Begin(LogicalAxis.W,1,0,0,40,0),"W drag begins");
        drag.Update(1,AxisDrag.PixelsPerCell*1.37,0,true);Same(a.Center,new Vec4(0,0,3,1),"Snap rounds W 1.37 to 1");
        drag.Update(1,AxisDrag.PixelsPerCell*.2,0,true);Same(a.Center,new Vec4(0,0,3,0),"Snap returns exactly to the start cell");
        drag.Update(1,AxisDrag.PixelsPerCell*.2,0);Same(a.Center,new Vec4(0,0,3,.2),"Without snap the drag stays continuous");
        drag.Complete(1);
        a.SetCenter(new Vec4(0,0,3,.3));check(drag.Begin(LogicalAxis.W,2,0,0,40,0),"Second W drag begins");
        drag.Update(2,AxisDrag.PixelsPerCell*.1,0,true);Same(a.Center,new Vec4(0,0,3,0),"Snap is to the absolute grid from a fractional start");
        drag.Update(2,-AxisDrag.PixelsPerCell*1.9,0,true);Same(a.Center,new Vec4(0,0,3,-2),"Snap below zero");drag.Complete(2);
        // Reference hyperplane for a CPU step.
        var p=new Vec4(1,2,3,0);
        check(SliceAxes.ViewContaining(p,new Vec4(2,3,3,0))==ReferenceView.XYZ,"W-preserving step uses XYZ");
        check(SliceAxes.ViewContaining(p,new Vec4(2,2,3,1))==ReferenceView.XZW,"W step on the board plane uses XZW");
        check(SliceAxes.ViewContaining(p,new Vec4(2,2,3,1),ReferenceView.XYW)==ReferenceView.XZW,"Current view is kept only when it holds the step");
        check(SliceAxes.ViewContaining(p,new Vec4(1,2,4,1),ReferenceView.XYW)==ReferenceView.XYW,"Current view that holds the step beats the fixed order");
        check(SliceAxes.ViewContaining(p,new Vec4(1,3,4,1))==ReferenceView.XYW,"F-preserving step uses XYW");
        check(SliceAxes.ViewContaining(p,new Vec4(4,3,4,2))==ReferenceView.XZW,"Smallest normal step wins when nothing holds the step; ties keep the order");
        check(SliceAxes.ViewContaining(p,new Vec4(2,4,5,3))==ReferenceView.XYW,"Fully 4D step picks the least-leaving view");
        foreach(ReferenceView view in Enum.GetValues(typeof(ReferenceView)))
        {
            int n=SliceAxes.NormalCoreAxis(view);var normal=SliceAxes.Reference(view).Normal;
            Near(Math.Abs(n==0?normal.X:n==1?normal.Y:n==2?normal.Z:normal.W),1,"Normal axis of "+view);
        }
        // Slow legs for the CPU presentation; a running leg keeps the duration it started with.
        var slow=new SliceAnimator(Initial());slow.SecondsPerLeg=1.6;slow.Request(ReferenceView.XZW);slow.SecondsPerLeg=SliceAnimator.SecondsPerQuarterTurn;
        slow.Advance(1.0);check(slow.IsAnimating,"1.6 s leg still turning after 1 s");slow.Advance(.6);check(!slow.IsAnimating&&slow.StableView==ReferenceView.XZW,"1.6 s leg lands on XZW");
        // Place glides C and the anchor while a turn keeps running, then the turn lands on its reference pose.
        var glide=new SliceAnimator(Initial());glide.Request(ReferenceView.XZW);glide.Advance(.2);glide.Place(new Vec4(1,0,3,2),new Vec3(.1,.8,0));
        check(glide.IsAnimating,"Place keeps the turn running");Same(glide.Center,new Vec4(1,0,3,2),"Place sets C");Same(glide.DisplayState.Anchor,new Vec3(.1,.8,0),"Place sets A");
        glide.Advance(1);check(!glide.IsAnimating&&glide.StableView==ReferenceView.XZW,"Turn lands after Place");Same(glide.Center,new Vec4(1,0,3,2),"Turn keeps the placed C");Same(glide.DisplayState.Anchor,new Vec3(.1,.8,0),"Turn keeps the placed A");
        // A queued leg keeps the duration it was requested with, even when SecondsPerLeg changes before it starts.
        var queued=new SliceAnimator(Initial());queued.SecondsPerLeg=1.6;queued.Request(ReferenceView.XZW);queued.Request(ReferenceView.XYW);queued.SecondsPerLeg=SliceAnimator.SecondsPerQuarterTurn;
        queued.Advance(1.6);check(queued.IsAnimating&&queued.TargetView==ReferenceView.XYW,"Queued leg starts after the first");
        queued.Advance(1.0);check(queued.IsAnimating,"Queued 1.6 s leg still turning after 1 s");queued.Advance(.6);check(!queued.IsAnimating&&queued.StableView==ReferenceView.XYW,"Queued leg lands on XYW");
        // Snapping is to whole core cells, so a fractional embedding height does not shift the grid on Y.
        var half=new SliceAnimator(new PivotViewState(2.5,SliceAxes.Reference(ReferenceView.XYZ,new Vec4(0,0,2.5,0)),new Vec3(0,.86,0),ShapeFactory.CellPitchMetres,FiniteSliceWindow.DefaultHalfExtent));
        var yDrag=new AxisDrag(half,new InputOwnership());check(yDrag.Begin(LogicalAxis.Y,3,0,0,40,0),"Y drag begins");
        yDrag.Update(3,AxisDrag.PixelsPerCell*1.37,0,true);Same(half.Center,new Vec4(0,0,3.5,0),"Y snap lands on core V=1, world 3.5");
        Same(half.DisplayState.WorldToCore(half.Center),new Vec4(0,0,1,0),"Snapped Y is a whole core cell");yDrag.Complete(3);
        Reject(()=>{try{slow.SecondsPerLeg=0;}catch(ArgumentOutOfRangeException e){throw new ArgumentException(e.Message);}},"Zero leg duration rejected");
    }
    static void EmbeddingAndPivots()
    {
        var initial=Initial();Same(initial.Frame.Center,new Vec4(0,0,3,0),"C=P uses Core V");Same(initial.Embedding,new Vec4(0,0,3,0),"Fixed E uses Core V");
        Same(initial.Anchor,new Vec3(0,.86,0),"Initial A=board surface: the Y=h piece plane rests on the wood");
        var q=new Vec4(2,-1,0,0);var world=initial.CoreToWorld(q);Same(world,new Vec4(2,-1,3,0),"Q4 unchanged by fixed embedding");Same(initial.WorldToCore(world),q,"Embedding inverse");
        Same(initial.WorldToDisplay(world),new Vec3(-.05,.86,.1),"Independent T and s");Same(initial.DisplayToWorld(new Vec3(-.05,.86,.1)),world,"Display inverse on H");
        var origin=initial.WithCenter(default);Same(origin.Anchor,new Vec3(0,.71,0),"P to O anchor update: O is s*h below the board");Same(origin.WorldToDisplay(world),initial.WorldToDisplay(world),"P to O preserves height");Same(origin.Embedding,initial.Embedding,"P to O preserves E");
        check(3+ShapeFactory.CreatePiece(ShapeVariant.A).Bounds.Max.Z<origin.HalfWindow,"Initial piece clearance at O");
        var pawn=new Instance4D(ShapeFactory.CreatePiece(ShapeVariant.A),default,1,"central-pawn");
        var xw=initial.WithOrientation(SliceRotations.RotateDrag(initial.Frame,90,0));
        Same(xw.Frame.Normal,new Vec4(0,-1,0,0),"XW90 normal is -Core U");
        foreach(double x in new[]{-1d,0,1})
        {
            var point=new Vec4(2,x,3,0);Near(Vec4.Dot(xw.Frame.Normal,point-xw.Frame.Center),-x,"XW90 reference-point cut x=0");
            var piece=new Instance4D(pawn.Shape,new Vec4(2,x,0,0));var result=FiniteSliceWindow.Build(xw.Embed(piece),xw.Frame,xw.HalfWindow);
            check((result.Status==SectionStatus.Volume3D)==(x==0),"XW90 actual wooden shape finite extent");
        }
        var ywP=initial.WithOrientation(SliceRotations.RotateDrag(initial.Frame,0,90));
        var ywO=origin.WithOrientation(SliceRotations.RotateDrag(origin.Frame,0,90));
        Same(ywP.Frame.Normal,new Vec4(0,0,-1,0),"YW90 normal is -Core V");
        Near(Vec4.Dot(ywP.Frame.Normal,initial.CoreToWorld(default)-ywP.Frame.Center),0,"YW90 C=P reference plane Y=h");
        Near(Vec4.Dot(ywO.Frame.Normal,origin.CoreToWorld(default)-ywO.Frame.Center),-3,"YW90 C=O reference plane Y=0");
        check(FiniteSliceWindow.Build(ywP.Embed(pawn),ywP.Frame,ywP.HalfWindow).Status==SectionStatus.Volume3D,"YW90 P intersects actual wooden bottom facet");
        check(FiniteSliceWindow.Build(ywO.Embed(pawn),ywO.Frame,ywO.HalfWindow).Status==SectionStatus.Empty,"YW90 O removes h=3 pawn completely");
        var movedW=xw.WithCenter(xw.Frame.Center+new Vec4(0,0,0,1));
        Same(movedW.Anchor,xw.Anchor+new Vec3(.05,0,0),"W tangent after XW90 maps to Unity X, not down");
        Same(movedW.WorldToDisplay(xw.Frame.Lift(new Vec3(.4,.2,.1))),xw.WorldToDisplay(xw.Frame.Lift(new Vec3(.4,.2,.1))),"W tangent displacement cancels chart translation");
        var general=initial.WithOrientation(SliceRotations.RotateDrag(initial.Frame,37,-23,true));var sample=general.Frame.Lift(new Vec3(.2,.3,-.4));
        var delta=general.Frame.AxisX*.7+general.Frame.AxisY*(-.4)+general.Frame.AxisZ*.25;
        var moved=general.WithCenter(general.Frame.Center+delta);
        Same(moved.WorldToDisplay(sample),general.WorldToDisplay(sample),"General tangent move preserves common-world display");
        Same(moved.Anchor,general.Anchor+new Vec3(-.02,.0125,.035),"General tangent anchor calculated from independent coefficients");
        Same(general.WithCenter(general.Frame.Center+general.Frame.Normal*.75).Anchor,general.Anchor,"Normal move keeps A");
        Same(general.Anchor,initial.Anchor,"Rotation holds A");
    }
    static void GeneralOrientation()
    {
        var initial=Initial();
        // Independent closed-form matrix for R_VW(beta) R_UW(alpha).
        double alpha=37*Math.PI/180,beta=-23*Math.PI/180;var expected=new SliceFrame(initial.Frame.Center,new Vec4(1,0,0,0),
            new Vec4(0,Math.Cos(alpha),-Math.Sin(beta)*Math.Sin(alpha),Math.Cos(beta)*Math.Sin(alpha)),
            new Vec4(0,0,Math.Cos(beta),Math.Sin(beta)),new Vec4(0,-Math.Sin(alpha),-Math.Sin(beta)*Math.Cos(alpha),Math.Cos(beta)*Math.Cos(alpha)));
        var actual=SliceRotations.RotateDrag(initial.Frame,37,-23);Pose(actual,expected,"Independent composed 4D rotation");
        var animator=new SliceAnimator(initial);animator.Request(ReferenceView.XYW);animator.Advance(.11);animator.Request(ReferenceView.XZW);
        var takeover=animator.Frame;var fixedAnchor=animator.DisplayState.Anchor;animator.SetOrientation(takeover);
        check(!animator.IsAnimating&&animator.QueuedView==null&&animator.StableView==null,"Direct takeover retains intermediate pose and cancels queue");Same(animator.DisplayState.Anchor,fixedAnchor,"Takeover retains A");
        foreach(double degree in new[]{0d,37,90,179.999999,180,180.000001,-180,359.999999})foreach(ReferenceView target in Enum.GetValues<ReferenceView>())
        {
            var source=initial.WithOrientation(SliceRotations.RotateDrag(initial.Frame,degree,-23,true));var a=new SliceAnimator(source);
            a.Request(target);var previous=a.Frame;
            for(int i=0;i<45;i++)
            {
                a.Advance(.01);check(a.Frame.IsOrthonormal(1e-9),"General animation orthonormal");Near(IndependentDeterminant(a.Frame),1,"General animation positive orientation");
                Same(a.Center,source.Frame.Center,"General animation holds C");Same(a.DisplayState.Anchor,source.Anchor,"General animation holds A");
                check((a.Frame.Normal-previous.Normal).Length<.45,"No discontinuous intermediate rotation");previous=a.Frame;
            }
            a.Advance(.001);Pose(a.Frame,SliceAxes.Reference(target,source.Frame.Center),"General animation endpoint");check(a.StableView==target,"Endpoint gets correct reference label");
            var end=new SliceAnimator(source);end.Request(target);end.Advance(SliceAnimator.SecondsPerQuarterTurn-1e-8);var near=end.Frame;end.Advance(1e-8);
            check((near.AxisX-end.Frame.AxisX).Length+(near.AxisY-end.Frame.AxisY).Length+(near.AxisZ-end.Frame.AxisZ).Length+(near.Normal-end.Frame.Normal).Length<1e-8,"Givens near-endpoint continuity");
        }
        var rotationStart=SliceRotations.RotateDrag(initial.Frame,-18,12);var whole=SliceRotations.RotateDrag(rotationStart,52,-41);
        foreach(int events in new[]{1,2,7,100})
        {
            SliceFrame end=rotationStart;for(int i=1;i<=events;i++)end=SliceRotations.RotateDrag(rotationStart,52d*i/events,-41d*i/events);
            Pose(end,whole,"Cumulative drag independent of event partition");
        }
        animator.Restore(initial.WithOrientation(actual).WithCenter(new Vec4(.1,.2,2.9,.4)));check(animator.StableView==null&&!animator.IsAnimating,"Arbitrary full-state restore is not reference-rounded");
        var before=animator.DisplayState;var f=before.Frame;
        Reject(()=>animator.SetOrientation(new SliceFrame(f.Center,f.AxisX*(-1),f.AxisY,f.AxisZ,f.Normal)),"Reflected frame rejected");check(ReferenceEquals(before,animator.DisplayState),"Rejected pose preserves state atomically");
        Reject(()=>animator.SetOrientation(new SliceFrame(f.Center+new Vec4(1,0,0,0),f.AxisX,f.AxisY,f.AxisZ,f.Normal)),"Rotation cannot move C");
        Reject(()=>new PivotViewState(double.NaN,initial.Frame,default),"Nonfinite E rejected");
    }
    static double IndependentDeterminant(SliceFrame frame)
    {
        var c=new[]{frame.AxisX,frame.AxisY,frame.AxisZ,frame.Normal};var a=new double[4,4];
        for(int j=0;j<4;j++){a[0,j]=c[j].X;a[1,j]=c[j].Y;a[2,j]=c[j].Z;a[3,j]=c[j].W;}
        double result=1;for(int col=0;col<4;col++){int pivot=col;for(int r=col+1;r<4;r++)if(Math.Abs(a[r,col])>Math.Abs(a[pivot,col]))pivot=r;if(Math.Abs(a[pivot,col])<1e-15)return 0;if(pivot!=col){for(int j=0;j<4;j++){double v=a[col,j];a[col,j]=a[pivot,j];a[pivot,j]=v;}result=-result;}double diagonal=a[col,col];result*=diagonal;for(int r=col+1;r<4;r++){double factor=a[r,col]/diagonal;for(int j=col;j<4;j++)a[r,j]-=factor*a[col,j];}}return result;
    }
    static void MiniIntersections()
    {
        var initial=Initial();var rotated=SliceRotations.Rotate(initial.Frame,0,1,Math.PI/4);
        var nearParallel=HyperplaneRelation.Build(SliceRotations.RotateDrag(initial.Frame,1e-8,0),ReferenceView.XYZ,1);
        check(!nearParallel.IsCoincident&&nearParallel.Vertices.Count==4,"Near-parallel noncoincident intersection remains a plane, not an eight-corner cube");
        foreach(var p in nearParallel.Vertices)Near(p.Y,0,"Near-parallel plane uses normalized distance");
        var coincident=HyperplaneRelation.Build(rotated,ReferenceView.XYZ,1,8,1);
        check(coincident.IsCoincident&&coincident.Vertices.Count==16,"Coincident rotated chart produces clipped octagonal prism, not fixed cube");
        double corner=Math.Sqrt(2)-1;
        foreach(int signZ in new[]{-1,1})foreach(int signA in new[]{-1,1})foreach(int signB in new[]{-1,1})foreach(bool swap in new[]{false,true})
        {var p=swap?new Vec3(signA*corner,signB,signZ):new Vec3(signA,signB*corner,signZ);check(coincident.Vertices.Any(v=>(v-p).Length<1e-8),"Independent analytic octagonal-prism vertex");}
        check(coincident.Edges.Count==48&&coincident.TriangleIndices.Count==84,"Octagonal prism unique edges and face triangulation");
        var xw=SliceRotations.RotateDrag(initial.Frame,45,0);var rectangle=HyperplaneRelation.Build(xw,ReferenceView.XZW,1,9,.25);
        check(!rectangle.IsCoincident&&rectangle.Vertices.Count==4,"Main window clips reference plane to rectangle");
        foreach(int a in new[]{-1,1})foreach(int b in new[]{-1,1}){var p=new Vec3(a*.25,b*.25/Math.Sqrt(2),b*.25/Math.Sqrt(2));check(rectangle.Vertices.Any(v=>(v-p).Length<1e-8),"Independent tilted rectangle vertex");}
        foreach(var frame in new[]{initial.Frame,rotated,xw,SliceRotations.RotateDrag(initial.Frame,33,-71,true)})foreach(ReferenceView reference in Enum.GetValues<ReferenceView>())
        {
            var relation=HyperplaneRelation.Build(frame,reference,4.5,72,2.25);var basis=SliceAxes.Reference(reference,frame.Center);
            check(relation.ViewRevision==72,"Mini revision matches chart");
            foreach(var p in relation.Vertices)
            {
                var delta=basis.AxisX*p.X+basis.AxisY*p.Y+basis.AxisZ*p.Z;
                Near(Vec4.Dot(frame.Normal,delta),0,"Independent H equation in C-based mini",1e-8);
                check(Math.Max(Math.Abs(p.X),Math.Max(Math.Abs(p.Y),Math.Abs(p.Z)))<=4.5+1e-8,"Reference box constraint");
                foreach(var axis in new[]{frame.AxisX,frame.AxisY,frame.AxisZ})check(Math.Abs(Vec4.Dot(axis,delta))<=2.25+1e-8,"Main window six inequalities");
            }
            check(relation.Edges.All(i=>i>=0&&i<relation.Vertices.Count)&&relation.TriangleIndices.All(i=>i>=0&&i<relation.Vertices.Count),"Relation topology uses actual clipped vertices");
        }
        Near(HyperplaneRelation.Build(initial.Frame,ReferenceView.XZW).FixedCoordinate,3,"XZW mini is Y=h initially");
        Near(HyperplaneRelation.Build(initial.WithCenter(default).Frame,ReferenceView.XZW).FixedCoordinate,0,"XZW mini is Y=0 after Home");
        // P-anchored charts: moving C along W shifts the XYW/XZW/YZW intersection by the same amount.
        var pivot=initial.Embedding;var moved=initial.WithCenter(pivot+new Vec4(0,0,0,.76)).Frame;
        foreach(ReferenceView reference in new[]{ReferenceView.XYW,ReferenceView.XZW,ReferenceView.YZW})
        {
            var still=HyperplaneRelation.Build(initial.Frame,reference,4.5,1,4.5,pivot);var shifted=HyperplaneRelation.Build(moved,reference,4.5,1,4.5,pivot);
            var basis=SliceAxes.Reference(reference);var w=new Vec3(Vec4.Dot(SliceAxes.WorldAxis(LogicalAxis.W),basis.AxisX),Vec4.Dot(SliceAxes.WorldAxis(LogicalAxis.W),basis.AxisY),Vec4.Dot(SliceAxes.WorldAxis(LogicalAxis.W),basis.AxisZ));
            check(still.Vertices.Count==4&&shifted.Vertices.Count==4,"P-anchored "+reference+" stays a square");
            foreach(var v in still.Vertices)Near(Vec3.Dot(v,w),0,"P-anchored "+reference+" initial plane W=0");
            foreach(var v in shifted.Vertices)Near(Vec3.Dot(v,w),.76,"P-anchored "+reference+" plane follows W=0.76");
            Same(shifted.ChartCenter,w*.76,"P-anchored "+reference+" reports C");
        }
        var inSpace=HyperplaneRelation.Build(moved,ReferenceView.XYZ,4.5,1,4.5,pivot);
        check(inSpace.IsCoincident&&inSpace.ChartCenter.Length<1e-12,"Normal move keeps C at the XYZ chart origin");
        var tilted=SliceRotations.RotateDrag(initial.Frame,33,-71,true);var offsetFrame=new SliceFrame(pivot+new Vec4(.4,-1.1,.7,1.3),tilted.AxisX,tilted.AxisY,tilted.AxisZ,tilted.Normal);
        foreach(ReferenceView reference in Enum.GetValues<ReferenceView>())
        {
            var relation=HyperplaneRelation.Build(offsetFrame,reference,4.5,2,2.25,pivot);var refNormal=SliceAxes.Reference(reference).Normal;
            var origin=pivot+refNormal*Vec4.Dot(offsetFrame.Center-pivot,refNormal);var basis=SliceAxes.Reference(reference,origin);
            foreach(var v in relation.Vertices)
            {
                var world=basis.Lift(v);Near(Vec4.Dot(offsetFrame.Normal,world-offsetFrame.Center),0,"P-anchored "+reference+" vertex on H",1e-8);
                check(Math.Max(Math.Abs(v.X),Math.Max(Math.Abs(v.Y),Math.Abs(v.Z)))<=4.5+1e-8,"P-anchored reference box");
                foreach(var axis in new[]{offsetFrame.AxisX,offsetFrame.AxisY,offsetFrame.AxisZ})check(Math.Abs(Vec4.Dot(axis,world-offsetFrame.Center))<=2.25+1e-8,"P-anchored main window");
            }
        }
    }
    static void InputAndVersions()
    {
        var initial=Initial();var original=initial.WithOrientation(SliceRotations.RotateDrag(initial.Frame,90,0));
        foreach(var screen in new[]{new Vec3(40,0,0),new Vec3(0,60,0),new Vec3(-30,40,0)})
        {
            var a=new SliceAnimator(original);var ownership=new InputOwnership();var drag=new AxisDrag(a,ownership);double length=Math.Sqrt(screen.X*screen.X+screen.Y*screen.Y);
            check(drag.Begin(LogicalAxis.W,3,20,30,screen.X,screen.Y),"Different mini starts same W drag");drag.Update(3,20+64*screen.X/length,30+64*screen.Y/length);
            Same(a.Center,original.Frame.Center+new Vec4(0,0,0,2),"Same world delta from different mini");Same(a.DisplayState.Anchor,original.Anchor+new Vec3(.1,0,0),"Same A from different mini");
            ownership.FocusLost();check(ReferenceEquals(a.DisplayState,original),"Focus loss restores exact original C/B/A snapshot");
        }
        var token=new PickToken(new CellId(0,0,0,0),1,2,3,4,5,6,7);var hit=new CellHit(token,new Vec3(.1,.2,.3));
        check(hit.CanAccept(token,1,2,3,5,6,7),"All current pick revisions accepted");
        check(!hit.CanAccept(token,1,2,3,9,6,7),"Camera stale rejected");check(!hit.CanAccept(token,1,2,3,5,9,7),"Layout stale rejected");check(!hit.CanAccept(token,1,2,3,5,6,9),"Session stale rejected");
    }
}
