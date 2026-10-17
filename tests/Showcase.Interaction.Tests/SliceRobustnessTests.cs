using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;

// The cell walk of the finite window, intersection polygons whose first normal misses the chart, frames between the
// saved and the cutting tolerance, axes outside the space, and the 3D board.
static class SliceRobustnessTests
{
    static Action<bool,string> check=null!;
    static void Near(double a,double b,string message,double epsilon=1e-9)=>check(Math.Abs(a-b)<=epsilon,message+": "+a+" != "+b);
    static void Reject<T>(Action action,string message) where T:Exception{bool rejected=false;try{action();}catch(T){rejected=true;}check(rejected,message);}
    public static void Run(Action<bool,string> assertions,Action<object> addCase)
    {
        check=assertions;
        var walk=CellWalk();RelationPolygons();FrameCleanup();AxesOutsideTheSpace();ThreeDimensionalSpace();
        addCase(new{name="finite_window_cell_walk",status="PASS",note="Cells met by the window, lexicographic, never missing a solid cut (all 9^N cells checked for N=3..5); worst Ctrl+drag view of 11D",worstCase=walk});
        addCase(new{name="relation_polygon_first_normal_off_chart",status="PASS",note="5D Ctrl rotation axis V: XYZ/XYV/XZV/YZV polygons convex, fan area equals hull area"});
        addCase(new{name="frame_tolerance_and_axis_guards",status="PASS",note="1e-8-off frames cleaned on load, 1e-9 frames kept bit for bit; axes and centres beyond N rejected; 3D board view"});
    }
    static VecN Cell(int[] q){var v=new double[Math.Max(4,q.Length)];for(int i=0;i<q.Length;i++)v[i]=q[i];return new VecN(v);}
    static ConvexShape UnitCell(int n)=>ShapeFactory.CreateBox(new VecN(Enumerable.Repeat(-.5,n).ToArray()),new VecN(Enumerable.Repeat(.5,n).ToArray()));
    static List<int[]> Walk(SliceFrame frame,VecN origin,double half,int cap,out bool complete)
    {var cells=new List<int[]>();complete=FiniteSliceWindow.Cells(frame,origin,half,-4,4,cap,c=>cells.Add((int[])c.Clone()));return cells;}
    static int Compare(int[] a,int[] b){for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return a[i].CompareTo(b[i]);return 0;}
    static SliceFrame Random(Random rnd,int n,VecN center)
    {
        var frame=SliceAxes.ReferenceFrame(new[]{0,1,2},n,center);
        for(int r=rnd.Next(4);r>0;r--){int a=rnd.Next(n),b=rnd.Next(n);if(a!=b)frame=SliceRotations.Rotate(frame,a,b,rnd.Next(3)==0?Math.PI/2:rnd.NextDouble()*6.3);}
        return frame;
    }
    static object CellWalk()
    {
        var rnd=new Random(17);var embedding=new VecN(0,0,3,0);
        // Small N: every one of the 9^N cells is cut, so a missed solid cell would show.
        foreach(int n in new[]{3,4,4,4,5})for(int j=0;j<(n==5?2:n==3?12:6);j++)
        {
            var center=VecN.Zero(n).WithAxis(2,3+(j%3==0?.5:0)).WithAxis(0,j%2==0?0:rnd.NextDouble()*2-1);
            var frame=Random(rnd,n,center);double half=j%2==0?4.5:1+rnd.NextDouble()*3;
            var cells=Walk(frame,embedding,half,int.MaxValue,out bool complete);check(complete,"Uncapped walk completes");
            for(int i=1;i<cells.Count;i++)check(Compare(cells[i-1],cells[i])<0,"Cells in strict lexicographic order");
            var walked=new HashSet<string>(cells.Select(c=>string.Join(",",c)));var cut=FiniteSliceWindow.Cutter(frame,half);var shape=UnitCell(n);
            var q=new int[n];int total=(int)Math.Pow(9,n);
            for(int index=0;index<total;index++)
            {
                for(int i=0,rest=index;i<n;i++,rest/=9)q[i]=rest%9-4;
                if(cut(new InstanceN(shape,Cell(q)+embedding)).Status==SectionStatus.Volume3D)check(walked.Contains(string.Join(",",q)),"Solid cell "+string.Join(",",q)+" walked in "+n+"D");
            }
        }
        // Every walked cell lies within the window widened by its own cube, and the cutter matches FiniteSliceWindow.Build exactly.
        for(int n=6;n<=11;n++)
        {
            var frame=Random(rnd,n,VecN.Zero(n).WithAxis(2,3));var cells=Walk(frame,embedding,4.5,int.MaxValue,out _);var shape=UnitCell(n);var cut=FiniteSliceWindow.Cutter(frame,4.5);
            check(cells.Count>0,"Walk finds cells in "+n+"D");
            foreach(var c in cells.Take(200))
            {
                var instance=new InstanceN(shape,Cell(c)+embedding);var a=cut(instance);var b=FiniteSliceWindow.Build(instance,frame,4.5);
                check(a.Status==b.Status&&a.Volume==b.Volume&&a.Vertices.Length==b.Vertices.Length&&a.Indices.SequenceEqual(b.Indices),"Cutter equals Build");
                var d=Cell(c)+embedding-frame.Center;
                foreach(var axis in new[]{frame.AxisX,frame.AxisY,frame.AxisZ}){double l1=0;for(int i=0;i<n;i++)l1+=Math.Abs(axis.Axis(i));check(Math.Abs(VecN.Dot(axis,d))<=4.5+.5*l1+1e-6,"Walked cell reaches the window");}
            }
        }
        // The worst view of the review: N=11 after one Ctrl+drag about each hidden axis 3..10 (218M visits, 11.9 s before).
        var worst=SliceAxes.ReferenceFrame(new[]{0,1,2},11,VecN.Zero(11).WithAxis(2,3));
        for(int h=3;h<11;h++)worst=SliceRotations.RotateDrag(worst,25,15,false,h);
        var watch=Stopwatch.StartNew();var found=Walk(worst,embedding,4.5,int.MaxValue,out bool done);double ms=watch.Elapsed.TotalMilliseconds;
        var worstCut=FiniteSliceWindow.Cutter(worst,4.5);var worstShape=UnitCell(11);
        int solid=found.Count(c=>worstCut(new InstanceN(worstShape,Cell(c)+embedding)).Status==SectionStatus.Volume3D);
        check(done&&found.Count==3375&&solid==3375,"Worst 11D view walks exactly its 3375 solid cells: "+found.Count+"/"+solid);
        // The cap stops the walk and says so.
        var capped=Walk(worst,embedding,4.5,100,out bool all);check(!all&&capped.Count==100&&capped.Select(c=>string.Join(",",c)).SequenceEqual(found.Take(100).Select(c=>string.Join(",",c))),"Cap keeps the first cells and reports truncation");
        var exact=Walk(worst,embedding,4.5,3375,out bool fits);check(fits&&exact.Count==3375,"A cap equal to the count is not a truncation");
        Reject<ArgumentException>(()=>FiniteSliceWindow.Cells(new SliceFrame(default,new VecN(2,0,0,0),new VecN(0,1,0,0),new VecN(0,0,1,0),new VecN(0,0,0,1)),default,1,-4,4,10,_=>{}),"Nonorthonormal frame rejected by the walk");
        Reject<ArgumentException>(()=>FiniteSliceWindow.Cutter(SliceFrame.AtAngle(double.NaN),1),"Nonfinite frame rejected by the cutter");
        return new{dimensions=11,cells=found.Count,milliseconds=Math.Round(ms,1)};
    }
    // Independent area of a convex point set: Andrew's monotone chain in a basis of the plane.
    static double HullArea(IReadOnlyList<Vec3> points,Vec3 normal)
    {
        var x=Vec3.Cross(Math.Abs(normal.X)<.6?new Vec3(1,0,0):new Vec3(0,1,0),normal).Normalized;var y=Vec3.Cross(normal,x);
        var p=points.Select(v=>(X:Vec3.Dot(v,x),Y:Vec3.Dot(v,y))).OrderBy(v=>v.X).ThenBy(v=>v.Y).ToList();
        double Cross((double X,double Y) o,(double X,double Y) a,(double X,double Y) b)=>(a.X-o.X)*(b.Y-o.Y)-(a.Y-o.Y)*(b.X-o.X);
        var hull=new List<(double X,double Y)>();
        foreach(var pass in new[]{p,Enumerable.Reverse(p).ToList()}){int start=hull.Count;foreach(var v in pass){while(hull.Count>=start+2&&Cross(hull[hull.Count-2],hull[hull.Count-1],v)<=0)hull.RemoveAt(hull.Count-1);hull.Add(v);}hull.RemoveAt(hull.Count-1);}
        double area=0;for(int i=0;i<hull.Count;i++){var a=hull[i];var b=hull[(i+1)%hull.Count];area+=a.X*b.Y-a.Y*b.X;}return Math.Abs(area)/2;
    }
    // A simple convex polygon: the edges walk the vertices once, every fan triangle turns the same way, and the fan covers the hull.
    static double ConvexPolygon(HyperplaneRelation relation,Vec3 normal,string name)
    {
        var v=relation.Vertices;var t=relation.TriangleIndices;int count=v.Count;check(count>=3&&relation.Edges.Count==2*count,"Polygon "+name);
        for(int i=0;i<count;i++)check(relation.Edges[2*i]==i&&relation.Edges[2*i+1]==(i+1)%count,"Polygon edges form one cycle "+name);
        double area=0;int sign=0;
        for(int i=0;i<t.Count;i+=3)
        {
            double turn=Vec3.Dot(Vec3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]),normal);check(Math.Abs(turn)>1e-9,"Nondegenerate fan triangle "+name);
            int s=Math.Sign(turn);check(sign==0||s==sign,"Fan triangles turn one way (no bow-tie) "+name);sign=s;area+=Math.Abs(turn)/2;
        }
        for(int i=0;i<count;i++){double turn=Vec3.Dot(Vec3.Cross(v[(i+1)%count]-v[i],v[(i+2)%count]-v[(i+1)%count]),normal);check(Math.Sign(turn)==sign,"Convex turn at each vertex "+name);}
        Near(area,HullArea(v,normal),"Fan area equals hull area "+name,1e-9);
        return area;
    }
    static void RelationPolygons()
    {
        // 5D with the Ctrl rotation axis on V: the first normal (W) stays perpendicular to the XYZ chart, the second one cuts it.
        var c5=new VecN(0,0,3,0,0);var xyz=ReferenceSection.Initial(5);var start=xyz.Frame(c5);
        foreach(var (horizontal,vertical) in new[]{(30.0,0.0),(30,20),(-50,35),(10,-70),(89,1)})
        {
            var frame=SliceRotations.RotateDrag(start,horizontal,vertical,false,4);
            var relation=HyperplaneRelation.Build(frame,xyz,1,3,10);
            check(!relation.IsCoincident&&relation.RestrictedNormal.Length==0&&relation.Vertices.Count==4,"First normal off the chart, polygon from the second "+horizontal+","+vertical);
            var n=new Vec3(VecN.Dot(frame.Normals[1],VecN.Unit(0,5)),VecN.Dot(frame.Normals[1],VecN.Unit(1,5)),VecN.Dot(frame.Normals[1],VecN.Unit(2,5))).Normalized;
            foreach(var p in relation.Vertices)Near(Vec3.Dot(n,p),0,"Polygon lies on the frame's trace");
            // The trace is the plane n.p=0 with n=(0,a,b): a 2 x 2/max(|a|,|b|) rectangle of the unit chart cube.
            Near(ConvexPolygon(relation,n,"XYZ "+horizontal+","+vertical),4/Math.Max(Math.Abs(n.Y),Math.Abs(n.Z)),"Exact rectangle area");
        }
        // The panels of the review: XYZ and the three Ctrl-rotation-axis panels XYV, XZV, YZV, at the panels' sizes and through P.
        var dragged=SliceRotations.RotateDrag(start,37,-23,false,4);var pivot=new VecN(0,0,3,0,0);
        foreach(var reference in new[]{xyz,xyz.Swap(0,4),xyz.Swap(1,4),xyz.Swap(2,4)})
        {
            var relation=HyperplaneRelation.Build(dragged,reference,4.5,1,4.5,pivot);
            var basis=reference.Frame();var n=default(Vec3);
            foreach(var normal in dragged.Normals){var r=new Vec3(VecN.Dot(normal,basis.AxisX),VecN.Dot(normal,basis.AxisY),VecN.Dot(normal,basis.AxisZ));if(r.Length>1e-10){n=r.Normalized;break;}}
            check(!relation.IsCoincident&&relation.Vertices.Count>=3,"Panel "+reference.Name+" is a polygon");
            ConvexPolygon(relation,n,"panel "+reference.Name);
        }
        // Random 5D..8D frames after Ctrl+drags about hidden axes: every one-constraint polygon is convex.
        var rnd=new Random(5);int polygons=0;
        for(int j=0;j<400;j++)
        {
            int n=rnd.Next(5,9);var frame=ReferenceSection.Initial(n).Frame(VecN.Zero(n).WithAxis(2,3));
            for(int d=rnd.Next(1,4);d>0;d--)frame=SliceRotations.RotateDrag(frame,(rnd.NextDouble()-.5)*120,(rnd.NextDouble()-.5)*120,rnd.Next(2)==0,rnd.Next(3,n));
            var reference=ReferenceSection.Initial(n).Neighbours[rnd.Next(3*(n-3))];var relation=HyperplaneRelation.Build(frame,reference,1,0,rnd.Next(2)==0?1:2.5);
            var basis=reference.Frame();var constraints=new List<Vec3>();
            foreach(var normal in frame.Normals){var r=new Vec3(VecN.Dot(normal,basis.AxisX),VecN.Dot(normal,basis.AxisY),VecN.Dot(normal,basis.AxisZ));foreach(var c in constraints)r-=c*Vec3.Dot(r,c);if(r.Length>1e-10)constraints.Add(r.Normalized);}
            if(constraints.Count!=1||relation.Vertices.Count<3)continue;
            ConvexPolygon(relation,constraints[0],"random "+n+"D "+j);polygons++;
        }
        check(polygons>50,"Enough random one-constraint polygons: "+polygons);
    }
    static SliceFrame Skew(SliceFrame frame,double amount)
    {var v=frame.Vectors;for(int i=0;i<v.Length;i++)v[i]=v[i]+v[(i+1)%v.Length]*amount;return SliceFrame.FromVectors(frame.Center,v);}
    static void FrameCleanup()
    {
        foreach(int n in new[]{4,5,11})
        {
            var initial=PivotViewState.CreateInitial(new Vec3(0,.86,0),dimensions:n);
            var turned=initial.WithOrientation(SliceRotations.RotateDrag(initial.Frame,33,-21));
            // About 1e-8 off: inside the 1e-6 a saved file may carry, outside the 1e-9 every cut needs.
            var loose=Skew(turned.Frame,1e-8);check(!loose.IsOrthonormal()&&loose.IsOrthonormal(1e-6),"Fixture is between the two tolerances");
            Reject<ArgumentException>(()=>FiniteSliceWindow.Build(new InstanceN(ShapeFactory.CreatePiece(ShapeVariant.C,1,n),turned.Frame.Center),loose),"The raw frame cannot be cut");
            var state=new PivotViewState(turned.EmbeddingHeight,loose,turned.Anchor,turned.CellScale,turned.HalfWindow);
            check(state.Frame.IsOrthonormal(1e-12)&&state.Frame.Dimensions==n,"Loaded frame cleaned to the cutting tolerance "+n+"D");
            Near(SliceRotations.Determinant(state.Frame),1,"Cleaned frame keeps its orientation");
            for(int i=0;i<n;i++)check((state.Frame.Vectors[i]-loose.Vectors[i]).Length<1e-7,"Cleaning moves each vector by about its error");
            check(state.Frame.Center==loose.Center,"Cleaning keeps C");
            check(FiniteSliceWindow.Build(new InstanceN(ShapeFactory.CreatePiece(ShapeVariant.C,1,n),state.Frame.Center-new VecN(0,0,.1,0)),state.Frame,state.HalfWindow).Status==SectionStatus.Volume3D,"Cleaned frame cuts");
            var animator=new SliceAnimator(initial);animator.Restore(state);check(animator.Frame.IsOrthonormal(),"Restored state cuts");
            animator.SetOrientation(loose);check(animator.Frame.IsOrthonormal(),"Directly set loose orientation is cleaned too");
            // A frame within 1e-9 is kept bit for bit.
            var kept=new PivotViewState(turned.EmbeddingHeight,turned.Frame,turned.Anchor,turned.CellScale,turned.HalfWindow);
            for(int i=0;i<n;i++)for(int a=0;a<n;a++)check(BitConverter.DoubleToInt64Bits(kept.Frame.Vectors[i].Axis(a))==BitConverter.DoubleToInt64Bits(turned.Frame.Vectors[i].Axis(a)),"Orthonormal frame untouched");
        }
        Reject<ArgumentException>(()=>new PivotViewState(3,Skew(SliceAxes.Reference(ReferenceView.XYZ),1e-4),default),"A frame beyond the saved tolerance is still rejected");
    }
    static void AxesOutsideTheSpace()
    {
        foreach(int n in new[]{3,4,5,10})
        {
            var animator=new SliceAnimator(PivotViewState.CreateInitial(default,dimensions:n));var before=animator.DisplayState;
            foreach(var axis in Enum.GetValues<LogicalAxis>())
            {
                bool inside=SliceAxes.CoreAxis(axis)<n;var drag=new AxisDrag(animator,new InputOwnership());
                check(drag.Begin(axis,1,0,0,40,0)==inside,"Axis drag begins only on the space's axes "+axis+" in "+n+"D");
                if(inside){drag.Cancel();continue;}
                check(drag.RejectionReason.Contains(SliceAxes.AxisName(axis)),"Refusal names the axis");
                Reject<ArgumentOutOfRangeException>(()=>animator.Translate(axis,1),"Translate refuses "+axis+" in "+n+"D");
                Reject<ArgumentException>(()=>animator.DisplayState.WithCenter(animator.Center+SliceAxes.WorldAxis(axis)),"WithCenter refuses a centre of rank "+(SliceAxes.CoreAxis(axis)+1)+" in "+n+"D");
                Reject<ArgumentException>(()=>animator.SetCenter(animator.Center+SliceAxes.WorldAxis(axis)*.5),"SetCenter refuses it too");
            }
            check(ReferenceEquals(animator.DisplayState,before),"Refused moves leave the state untouched");
            check(animator.Translate(LogicalAxis.X,1)&&animator.DisplayState.WithCenter(animator.Center+VecN.Zero(VecN.MaxDimensions)).Dimensions==n,"Trailing zero components are still the same space");
        }
        // N from the centre's components when none is given: a 5-component centre names a 5D space even with V=0.
        check(SliceAxes.Reference(ReferenceView.XYW,VecN.Zero(5)).Dimensions==5&&SliceAxes.Reference(ReferenceView.XYW,new VecN(1,2,3,4)).Dimensions==4&&SliceAxes.Reference(ReferenceView.XYZ).Dimensions==4,"Reference N from the centre");
        bool message=false;try{SliceRotations.Validate(new SliceFrame(default,new VecN(2,0,0,0,0),VecN.Unit(1,5),VecN.Unit(2,5),new[]{VecN.Unit(3,5),VecN.Unit(4,5)}));}catch(ArgumentException e){message=!e.Message.Contains("4D");}
        check(message,"Validation message is not about 4D frames");
    }
    static void ThreeDimensionalSpace()
    {
        // N=3: no hidden axis. The board, a cell and a piece are cut by the one 3D frame at W=0, which is also the panel.
        var state=PivotViewState.CreateInitial(new Vec3(0,.86,0),dimensions:3);var frame=state.Frame;
        check(state.Dimensions==3&&frame.Normals.Count==0&&frame.IsOrthonormal(),"3D display state");
        var animator=new SliceAnimator(state);check(animator.StableSection==ReferenceSection.Initial(3)&&animator.StableView==ReferenceView.XYZ,"3D view is its XYZ section");
        Reject<ArgumentException>(()=>SliceRotations.RotateDrag(frame,10,0),"A 3D frame cannot turn");
        Reject<ArgumentException>(()=>animator.Request(ReferenceView.XYW),"No W panel in 3D");
        var board=ShapeFactory.CreateBoard(3);var cell=UnitCell(3);var piece=ShapeFactory.CreatePiece(ShapeVariant.C,1,3);
        check(board.Dimensions==3&&cell.Dimensions==3&&piece.Dimensions==3,"3D solids");
        var b=FiniteSliceWindow.Build(new InstanceN(board,state.Embedding),frame,state.HalfWindow);
        var b4=FiniteSliceWindow.Build(new InstanceN(ShapeFactory.CreateBoard(),new VecN(0,0,3,0)),SliceAxes.Reference(ReferenceView.XYZ,new VecN(0,0,3,0)),4.5);
        check(b.Status==SectionStatus.Volume3D,"3D board shown");Near(b.Volume,b4.Volume,"3D board equals the 4D board at W=0",1e-6);
        var cut=FiniteSliceWindow.Cutter(frame,state.HalfWindow);int solid=0;var cells=new List<int[]>();
        FiniteSliceWindow.Cells(frame,state.Embedding,state.HalfWindow,-4,4,int.MaxValue,c=>cells.Add((int[])c.Clone()));
        foreach(var c in cells){var r=cut(new InstanceN(cell,Cell(c)+state.Embedding));if(r.Status==SectionStatus.Volume3D){solid++;Near(r.Volume,1,"Whole 3D cell");}}
        check(cells.Count==729&&solid==729,"All 9^3 cells of the 3D board: "+cells.Count+"/"+solid);
        var p=FiniteSliceWindow.Build(new InstanceN(piece,new VecN(2,-1,0)+state.Embedding,-1),frame,state.HalfWindow);
        var p4=FiniteSliceWindow.Build(new InstanceN(ShapeFactory.CreatePiece(ShapeVariant.C),new VecN(2,-1,3,0),-1),SliceAxes.Reference(ReferenceView.XYZ,new VecN(0,0,3,0)),4.5);
        check(p.Status==SectionStatus.Volume3D&&p.Vertices.Length==p4.Vertices.Length,"3D piece shown like the 4D piece at W=0");Near(p.Volume,p4.Volume,"3D piece volume",1e-12);
        foreach(var v in p.Vertices)check(p4.Vertices.Any(w=>(w.Position-v.Position).Length<1e-12),"3D piece vertex of the 4D section");
        var relation=HyperplaneRelation.Build(frame,ReferenceSection.Initial(3),4.5,1,4.5,state.Embedding);
        check(relation.IsCoincident&&relation.Vertices.Count==8&&relation.FixedCoordinate==0,"The 3D panel is the whole view");
        var axis=new AxisDrag(animator,new InputOwnership());check(!axis.Begin(LogicalAxis.W,1,0,0,40,0),"No W drag in 3D");
        check(axis.Begin(LogicalAxis.Y,1,0,0,40,0)&&axis.Update(1,40,0)&&axis.Complete(1)&&animator.Center.Dimensions<=4&&animator.Center.Rank==3,"Y drag in 3D");
    }
}
