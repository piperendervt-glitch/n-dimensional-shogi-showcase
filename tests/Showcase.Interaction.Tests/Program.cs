using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;

static class Program
{
    static int assertions;
    static readonly List<object> cases=new();
    static int Main(string[] args)
    {
        var started=DateTime.UtcNow;
        try
        {
            Baselines();Animation();Sections();Relations();Input();Picking();NDimensional();PivotTests.Run(Check,value=>cases.Add(value));
            var result=new{status="PASS",assertions,startedUtc=started,finishedUtc=DateTime.UtcNow,cases,
                note="Pure C# tests; does not certify Unity input, rendering, Japanese text or Windows execution."};
            if(args.Length>0){Directory.CreateDirectory(args[0]);File.WriteAllText(Path.Combine(args[0],"interaction-results.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));}
            Console.WriteLine(JsonSerializer.Serialize(result));return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    static void Check(bool value,string message){assertions++;if(!value)throw new Exception(message);}
    static void Near(double actual,double expected,string message,double eps=1e-9)=>Check(Math.Abs(actual-expected)<=eps,message+": "+actual+" != "+expected);
    static void Vec(VecN actual,VecN expected,string message)=>Near((actual-expected).Length,0,message);
    static ReferenceView[] Views=Enum.GetValues<ReferenceView>();
    static double Determinant(SliceFrame f)
    {
        VecN[] columns={f.AxisX,f.AxisY,f.AxisZ,f.Normal};double[,] a=new double[4,4];
        for(int c=0;c<4;c++){a[0,c]=columns[c].X;a[1,c]=columns[c].Y;a[2,c]=columns[c].Z;a[3,c]=columns[c].W;}
        double sum=0;
        for(int i=0;i<4;i++)for(int j=0;j<4;j++)for(int k=0;k<4;k++)for(int l=0;l<4;l++)
        {
            int[] p={i,j,k,l};if(p.Distinct().Count()!=4)continue;int inversions=0;
            for(int x=0;x<4;x++)for(int y=x+1;y<4;y++)if(p[x]>p[y])inversions++;
            sum+=(inversions%2==0?1:-1)*a[0,i]*a[1,j]*a[2,k]*a[3,l];
        }
        return sum;
    }
    static void Baselines()
    {
        var center=new VecN(1,2,3,4);
        var f=new VecN(1,0,0,0);var u=new VecN(0,1,0,0);var v=new VecN(0,0,1,0);var w=new VecN(0,0,0,1);
        var expected=new[]{new[]{f,u,v,w},new[]{w,u,v,f*(-1)},new[]{f,u,w,v*(-1)},new[]{f,w,v,u*(-1)}};
        foreach(var view in Views)
        {
            var actual=SliceAxes.Reference(view,center);var e=expected[(int)view];
            Check(actual.IsOrthonormal(),"Reference orthonormal "+view);Near(Determinant(actual),1,"Positive orientation "+view);
            Vec(actual.AxisX,e[0],"Slot F "+view);Vec(actual.AxisY,e[1],"Slot U "+view);Vec(actual.AxisZ,e[2],"Slot V "+view);Vec(actual.Normal,e[3],"Normal "+view);Vec(actual.Center,center,"Reference center");
        }
        Check(SliceAxes.ViewForAxis(LogicalAxis.W)==ReferenceView.XYZ&&SliceAxes.ViewForAxis(LogicalAxis.Z)==ReferenceView.XYW&&SliceAxes.ViewForAxis(LogicalAxis.Y)==ReferenceView.XZW&&SliceAxes.ViewForAxis(LogicalAxis.X)==ReferenceView.YZW,"W/Z/Y/X mapping");
        Vec(SliceAxes.WorldAxis(LogicalAxis.X),u,"UI X=Core U");Vec(SliceAxes.WorldAxis(LogicalAxis.Y),v,"UI Y=Core V");Vec(SliceAxes.WorldAxis(LogicalAxis.Z),f,"UI Z=Core F");
        var legacy=SliceFrame.AtAngle(90);var current=SliceAxes.Reference(ReferenceView.YZW);
        Vec(current.AxisY,legacy.AxisY,"Legacy H90 tangent");Vec(current.Normal,legacy.Normal,"Legacy H90 normal");
        cases.Add(new{name="reference_frames",status="PASS",basis="Core(F,U,V,W), UI(X=U,Y=V,Z=F,W=W)",views=Views.Select(x=>x.ToString())});
    }
    static void Animation()
    {
        var center=new VecN(.2,-.4,.7,.9);
        foreach(var from in Views)foreach(var to in Views)
        {
            var a=new SliceAnimator(center,from);long before=a.ViewRevision;bool changed=a.Request(to);
            Check(changed==(from!=to),"Same reference no-op");
            if(changed)Check(a.StableView==null,"Intermediate pose not labelled reference");
            for(int i=0;i<100;i++)
            {
                a.Advance(.01);Check(a.Frame.IsOrthonormal(),"Animated orthonormality");Near(Determinant(a.Frame),1,"Animated positive orientation");Vec(a.Center,center,"Air pivot invariant");
            }
            Check(!a.IsAnimating&&a.StableView==to,"Animation destination "+from+" to "+to);
            var e=SliceAxes.Reference(to,center);Vec(a.Frame.AxisX,e.AxisX,"Endpoint x");Vec(a.Frame.AxisY,e.AxisY,"Endpoint y");Vec(a.Frame.AxisZ,e.AxisZ,"Endpoint z");Vec(a.Frame.Normal,e.Normal,"Endpoint normal");
            Check((a.ViewRevision>before)==changed,"Revision changes only for actual animation");
        }
        var queued=new SliceAnimator();queued.Request(ReferenceView.YZW);queued.Advance(.1);var middle=queued.Frame;
        queued.Request(ReferenceView.XZW);queued.Request(ReferenceView.XYW);Check(!queued.Request(ReferenceView.YZW),"Same active target ignored");
        Vec(queued.Frame.AxisY,middle.AxisY,"No mid-transition snap");Check(queued.QueuedView==ReferenceView.XYW,"Latest distinct request retained");
        queued.Advance(1.5);Check(queued.StableView==ReferenceView.XYW,"Queued destination");
        queued.Translate(LogicalAxis.W,1.2);var translated=queued.Center;queued.Request(ReferenceView.XYZ);queued.Advance(.45);Vec(queued.Center,translated,"W return preserves translation");
        queued.Request(ReferenceView.YZW);queued.Request(ReferenceView.XZW);queued.CancelQueuedRequest();queued.Advance(1);Check(queued.StableView==ReferenceView.YZW,"Cancel affects queued operation only");
        foreach(var view in Views)
        {
            var saved=SliceAxes.Reference(view,new VecN(.25,.5,-.5,2));queued.Restore(saved);
            Check(queued.StableView==view&&!queued.IsAnimating,"Stable saved view restored");Vec(queued.Center,saved.Center,"Saved center restored");
            queued.Request(ReferenceView.XYZ);queued.Advance(1);Check(queued.StableView==ReferenceView.XYZ,"Restored view follows supported route");
        }
        var retained=queued.Frame;long retainedRevision=queued.ViewRevision;bool rejected=false;
        try{queued.Restore(SliceAxes.OnLeg(ReferenceView.XYW,45));}catch(ArgumentException){rejected=true;}
        Check(rejected,"Arbitrary saved pose explicitly unsupported");Check(queued.ViewRevision==retainedRevision,"Rejected restore preserves revision");Vec(queued.Frame.Center,retained.Center,"Rejected restore preserves center");Vec(queued.Frame.Normal,retained.Normal,"Rejected restore preserves orientation");
        cases.Add(new{name="all_reference_transitions_and_queue",status="PASS",secondsPerLeg=SliceAnimator.SecondsPerQuarterTurn});
    }
    static ConvexShape Cell()=>ShapeFactory.CreateBox(new VecN(-.5,-.5,-.5,-.5),new VecN(.5,.5,.5,.5));
    static void MatchWorld(SectionResult a,SliceFrame fa,SectionResult b,SliceFrame fb)
    {
        foreach(var p in a.Vertices)Check(b.Vertices.Any(q=>(fa.Lift(p.Position)-fb.Lift(q.Position)).Length<1e-8),"Same world intersection vertices");
    }
    static void Sections()
    {
        var instance=new InstanceN(Cell(),default);var zero=SliceAxes.Reference(ReferenceView.XYZ);var angled=SliceAxes.OnLeg(ReferenceView.YZW,45);
        var r0=SectionBuilder.Build(instance,zero);var r45=SectionBuilder.Build(instance,angled);
        Near(r0.Volume,1,"Unit 4D cell XYZ volume");Near(r45.Volume,Math.Sqrt(2),"True intermediate widening");
        var tangent=SliceAxes.Reference(ReferenceView.XYZ,new VecN(.25,0,0,0));var rt=SectionBuilder.Build(instance,tangent);
        Near(rt.Volume,r0.Volume,"Tangential movement preserves infinite intersection");MatchWorld(r0,zero,rt,tangent);
        var normal=SliceAxes.Reference(ReferenceView.XYZ,new VecN(0,0,0,.6));Check(SectionBuilder.Build(instance,normal).Status==SectionStatus.Empty,"Normal translation changes intersection");
        Near(SectionBuilder.Build(instance,SliceAxes.Reference(ReferenceView.XYZ,new VecN(0,0,0,.5))).Volume,1,"Exact endpoint closed facet");
        Near(FiniteSliceWindow.Build(instance,zero,.25).Volume,.125,"Window clips all 3 tangent axes");
        Near(FiniteSliceWindow.Build(instance,SliceAxes.Reference(ReferenceView.XYZ,new VecN(.4,0,0,0)),.25).Volume,.0875,"Tangential finite window boundary exact clip");
        foreach(var view in Views)foreach(double degree in new[]{0d,15,45,70,90})foreach(int forward in new[]{-1,1})
        {
            var frame=SliceAxes.OnLeg(view,degree,new VecN(.15,-.12,.1,.2));
            var source=new InstanceN(ShapeFactory.CreatePiece(ShapeVariant.C),new VecN(.1,.1,.1,.1),forward);
            var clipped=FiniteSliceWindow.ClipInstance(source,frame,.22);var result=FiniteSliceWindow.Build(source,frame,.22);var oracle=TriplePlaneOracle.Build(clipped,frame);
            Check(result.Status==oracle.Status,"Finite window oracle status");
            foreach(var point in result.Vertices)
            {
                Check(point.Position.IsFinite,"Finite vertex");Check(Math.Abs(point.Position.X)<=.22000001&&Math.Abs(point.Position.Y)<=.22000001&&Math.Abs(point.Position.Z)<=.22000001,"Window halfspace containment");
                Check(oracle.Vertices.Any(q=>(q-point.Position).Length<1e-7),"Independent oracle vertex match");
            }
            foreach(var q in oracle.Vertices)Check(result.Vertices.Any(p=>(q-p.Position).Length<1e-7),"Independent oracle no missing vertex");
        }
        cases.Add(new{name="exact_cell_and_window_sections",status="PASS",h0Volume=r0.Volume,h45Volume=r45.Volume,oracle="existing independent TriplePlaneOracle"});
    }
    static void Relations()
    {
        foreach(var active in Views)foreach(var reference in Views)foreach(double degree in new[]{0d,.000001,45,89.999999,90})
        {
            var frame=SliceAxes.OnLeg(active,degree,new VecN(1,2,3,4));var result=HyperplaneRelation.Build(frame,reference,1,37);
            Check(result.ViewRevision==37,"Shared mini-view revision");Check(result.Vertices.All(v=>v.IsFinite),"Finite mini-view vertices");
            if(result.IsCoincident)Check(result.Vertices.Count==8,"Coincident space is entire cube");
            else{Check(result.Vertices.Count>=3,"Nondegenerate intersection polygon");foreach(var p in result.Vertices)Near(Vec3.Dot(result.RestrictedNormal,p),0,"Mini-view plane equation");}
        }
        foreach(var reference in Views)Check(HyperplaneRelation.Build(SliceAxes.Reference(reference),reference).IsCoincident,"Exact reference coincidence");
        cases.Add(new{name="four_reference_intersection_views",status="PASS",meaning="Current hyperplane intersected with each reference 3D space through c; not four independent slice states."});
    }
    static void Input()
    {
        var a=new SliceAnimator();var ownership=new InputOwnership();var drag=new AxisDrag(a,ownership);
        Check(ownership.TryBegin(InputOwner.Form,0),"Form claims pointer");Check(!drag.Begin(LogicalAxis.X,0,0,0,10,0),"Form blocks axis start");ownership.Release(InputOwner.Form,0);
        Check(!drag.Begin(LogicalAxis.X,0,0,0,6,0),"Degenerate axis rejected");
        Check(drag.Begin(LogicalAxis.X,0,100,100,20,0),"Axis begins");Check(!ownership.TryBegin(InputOwner.CameraPan,0),"Owner locked across other controls");
        Check(!drag.Update(1,132,100),"Wrong pointer rejected");drag.Update(0,132,100);Vec(a.Center,new VecN(0,1,0,0),"32 pixels one world X");ownership.FocusLost();Vec(a.Center,default,"Focus loss rolls back uncommitted drag");Check(!drag.IsActive&&ownership.Owner==InputOwner.None,"Focus loss releases ownership");
        Check(drag.Begin(LogicalAxis.W,0,0,0,0,50),"W drag begins");drag.Update(0,0,64);Check(drag.Complete(0),"W drag commits");ownership.FocusLost();Vec(a.Center,new VecN(0,0,0,2),"Committed displacement survives focus loss");
        foreach(var axis in Enum.GetValues<LogicalAxis>())
        {
            VecN? expected=null;
            foreach(var screenDirection in new[]{new[]{20d,0},new[]{0d,25},new[]{-30d,40}})
            {
                var s=new SliceAnimator();var o=new InputOwnership();var d=new AxisDrag(s,o);var dx=screenDirection[0];var dy=screenDirection[1];double length=Math.Sqrt(dx*dx+dy*dy);
                Check(d.Begin(axis,0,5,9,dx,dy),"Mini-view axis begins");d.Update(0,5+32*dx/length,9+32*dy/length);d.Complete(0);
                Vec(s.Center,SliceAxes.WorldAxis(axis),"Same logical axis translation across different screen views");if(expected.HasValue)Vec(s.Center,expected.Value,"View independence");expected=s.Center;
            }
        }
        a.Request(ReferenceView.XYW);Check(!drag.Begin(LogicalAxis.X,0,0,0,50,0),"Rotation owns frame before drag");
        cases.Add(new{name="input_ownership_and_rollback",status="PASS",pixelsPerCell=32,degeneratePixels=6});
    }
    static void NDimensional()
    {
        // The general reference frame reproduces the four 4D reference frames exactly (same vectors, same signs).
        var center4=new VecN(1,2,3,4);int triples=0;
        foreach(var view in Views)
        {
            var general=SliceAxes.ReferenceFrame(SliceAxes.ShownAxes(view),4,center4);var legacy=SliceAxes.Reference(view,center4);
            Check(general.Dimensions==4&&legacy.Dimensions==4,"4D frames have one normal");
            for(int i=0;i<4;i++)Vec(general.Vectors[i],legacy.Vectors[i],"N=4 general reference equals legacy "+view+" vector "+i);
            Vec(SliceAxes.Reference(view,center4,4).Normal,legacy.Normal,"Explicit N=4 reference equals legacy "+view);
        }
        // Every ordered axis triple of a 3..7 dimensional space gives a positively oriented orthonormal frame whose hidden axes are the complement.
        for(int n=3;n<=7;n++)for(int x=0;x<n;x++)for(int y=0;y<n;y++)for(int z=0;z<n;z++)
        {
            if(x==y||y==z||x==z)continue;var shown=new[]{x,y,z};var order=shown.Concat(Enumerable.Range(0,n).Except(shown)).ToArray();
            int inversions=0;for(int i=0;i<n;i++)for(int j=i+1;j<n;j++)if(order[i]>order[j])inversions++;
            if(n==3&&inversions%2==1){bool rejected=false;try{SliceAxes.ReferenceFrame(shown,3);}catch(ArgumentException){rejected=true;}Check(rejected,"Odd 3D frame rejected");continue;}
            var frame=SliceAxes.ReferenceFrame(shown,n,VecN.Zero(n).WithAxis(2,3));triples++;
            Check(frame.Dimensions==n&&frame.IsOrthonormal(),"Reference frame orthonormal N="+n);Near(SliceRotations.Determinant(frame),1,"Reference frame positively oriented N="+n);
            SliceRotations.Validate(frame);
            Vec(frame.AxisX,VecN.Unit(x,n),"Slot X shows axis "+x);Vec(frame.AxisY,VecN.Unit(y,n),"Slot Y shows axis "+y);Vec(frame.AxisZ,VecN.Unit(z,n),"Slot Z shows axis "+z);
            Check(SliceAxes.HiddenAxes(frame).SequenceEqual(Enumerable.Range(0,n).Except(shown)),"Hidden axes are the complement N="+n);
        }
        Check(triples==3+24+60+120+210,"Triple count 3..7 (only even 3D orders): "+triples);
        // Rotations in a 5D space: any plane of two core axes, including a hidden one beyond W.
        var center5=VecN.Zero(5).WithAxis(2,3);var f5=SliceAxes.ReferenceFrame(new[]{0,1,2},5,center5);
        var turned=SliceRotations.Rotate(f5,1,4,.7);Check(turned.IsOrthonormal(),"5D rotation orthonormal");Near(SliceRotations.Determinant(turned),1,"5D rotation orientation");
        Near(turned.AxisY.Axis(4),Math.Sin(.7),"U turned toward the fifth axis");Near(turned.AxisY.Axis(1),Math.Cos(.7),"U keeps its cosine");Vec(turned.AxisX,f5.AxisX,"F untouched by a U-A5 turn");
        var dragged=SliceRotations.RotateDrag(f5,30,20,false,4);Check(dragged.IsOrthonormal(),"Drag against the fifth axis orthonormal");Near(SliceRotations.Determinant(dragged),1,"Drag orientation");
        Vec(SliceRotations.RotateDrag(SliceAxes.Reference(ReferenceView.XYZ,center4),30,20).Normal,SliceRotations.RotateDrag(SliceAxes.Reference(ReferenceView.XYZ,center4),30,20,false,3).Normal,"Default drag axis is W");
        bool outside=false;try{SliceRotations.Rotate(f5,1,5,.1);}catch(ArgumentException){outside=true;}Check(outside,"Axis beyond the space rejected");
        // Sections: a 5D piece seen through the F,U,V slice is the 4D piece's section; the hidden fifth axis behaves like W.
        var piece4=ShapeFactory.CreatePiece(ShapeVariant.A);var piece5=ShapeFactory.CreatePiece(ShapeVariant.A,1,5);
        Check(piece5.Planes.Count==piece4.Planes.Count+2&&piece5.Bounds.Dimensions==5&&piece5.Bounds.CornerCount==32,"5D piece adds two end caps");
        var inst4=new InstanceN(piece4,new VecN(0,0,3,0));var inst5=new InstanceN(piece5,center5);
        var s4=SectionBuilder.Build(inst4,SliceAxes.Reference(ReferenceView.XYZ,new VecN(0,0,3,0)));var s5=SectionBuilder.Build(inst5,f5);
        Check(s4.Status==SectionStatus.Volume3D&&s5.Status==SectionStatus.Volume3D,"XYZ sections solid");Near(s5.Volume,s4.Volume,"5D section volume equals 4D");Check(s5.Vertices.Length==s4.Vertices.Length,"5D section vertex count equals 4D");
        var oracle=TriplePlaneOracle.Build(inst5,f5);Check(oracle.Status==SectionStatus.Volume3D,"Independent oracle agrees on 5D section");
        Check(FiniteSliceWindow.Build(inst5,f5).Status==SectionStatus.Volume3D,"Finite window section in 5D");
        var shifted=SectionBuilder.Build(inst5,f5.WithCenter(center5.WithAxis(4,.1)));Near(shifted.Volume,s4.Volume,"Inside the fifth-axis thickness the section is unchanged");
        Check(SectionBuilder.Build(inst5,f5.WithCenter(center5.WithAxis(4,.3))).Status==SectionStatus.Empty,"Beyond the fifth-axis thickness the piece vanishes");
        var s4w=SectionBuilder.Build(inst4,SliceAxes.Reference(ReferenceView.XYW,new VecN(0,0,3,0)));var s5a=SectionBuilder.Build(inst5,SliceAxes.ReferenceFrame(new[]{4,1,2},5,center5));
        Near(s5a.Volume,s4w.Volume,"Showing the fifth axis instead of F matches showing W instead of F");
        var board5=ShapeFactory.CreateBoard(5);var b5=SectionBuilder.Build(new InstanceN(board5,VecN.Zero(5)),SliceAxes.ReferenceFrame(new[]{0,1,2},5,VecN.Zero(5)));
        var b4=SectionBuilder.Build(new InstanceN(ShapeFactory.CreateBoard(),default),SliceAxes.Reference(ReferenceView.XYZ));
        Check(b5.Status==SectionStatus.Volume3D,"5D board section");Near(b5.Volume,b4.Volume,"5D board section volume equals 4D",1e-6);
        Check(ShapeFactory.CreateBoard(4).Planes.Count==ShapeFactory.CreateBoard().Planes.Count,"4D board unchanged");
        // Animation and display state in 5D: the XYZ to XYW turn ends on the 5D XYW reference frame; the fifth axis can be translated.
        var state=PivotViewState.CreateInitial(new Vec3(0,.86,0),dimensions:5);Check(state.Dimensions==5,"Initial 5D display state");
        Vec(state.Frame.Center,center5,"Initial 5D centre on the piece plane");
        var state4=PivotViewState.CreateInitial(new Vec3(0,.86,0));var legacy4=SliceAxes.Reference(ReferenceView.XYZ,new VecN(0,0,3,0));
        for(int i=0;i<4;i++)Vec(state4.Frame.Vectors[i],legacy4.Vectors[i],"Initial 4D display frame unchanged");
        var animator=new SliceAnimator(state);Check(animator.StableView==ReferenceView.XYZ,"5D initial pose is the XYZ reference");
        Check(animator.Request(ReferenceView.XYW),"5D turn requested");
        for(int i=0;i<100;i++){animator.Advance(.01);Check(animator.Frame.IsOrthonormal(),"5D animated orthonormality");Near(SliceRotations.Determinant(animator.Frame),1,"5D animated orientation");}
        Check(!animator.IsAnimating&&animator.StableView==ReferenceView.XYW,"5D turn reaches XYW");
        var end=SliceAxes.Reference(ReferenceView.XYW,center5,5);for(int i=0;i<5;i++)Vec(animator.Frame.Vectors[i],end.Vectors[i],"5D endpoint vector "+i);
        Check(SliceAxes.HiddenAxes(animator.Frame).SequenceEqual(new[]{0,4}),"XYW in 5D hides F and the fifth axis");
        Check(animator.Translate(LogicalAxis.A5,1.5)&&Math.Abs(animator.Center.Axis(4)-1.5)<1e-12,"Translate along the fifth axis");
        Check(SliceAxes.AxisName(LogicalAxis.A5)=="V"&&SliceAxes.AxisName(LogicalAxis.A6)=="U"&&SliceAxes.AxisName(LogicalAxis.A7)=="T"&&SliceAxes.AxisName(LogicalAxis.W)=="W","Axis labels");
        Check(SliceAxes.CoreAxis(LogicalAxis.A7)==6&&SliceAxes.AxisOfCore(4)==LogicalAxis.A5,"Fifth to seventh axes map to core 4..6");
        // VecN: missing trailing components are zero, so four-component vectors are valid in every N.
        Check(new VecN(1,2,3,4)==new VecN(1,2,3,4,0)&&new VecN(1,2,3,4)!=new VecN(1,2,3,4,1)&&default(VecN)==VecN.Zero(5)&&new VecN(1,2,3,4).ToString()=="(1,2,3,4)","VecN padded equality");
        Near(VecN.Dot(new VecN(1,2,3,4),VecN.Unit(4,5)),0,"Dot across dimensions");Near((new VecN(1,0,0,0)+VecN.Unit(4,5)).Length,Math.Sqrt(2),"Sum across dimensions");
        cases.Add(new{name="n_dimensional_frames_sections_and_turns",status="PASS",dimensions="3..7",triples,note="General reference frames reproduce the 4D ones; 5D sections of the extruded piece match the 4D sections."});
    }
    static void Picking()
    {
        var cell=new CellId(1,2,3,4);var request=new PickToken(cell,5,6,7,8);var hit=new CellHit(request,new Vec3(.1,.2,.3));
        Check(hit.CanAccept(request,5,6,7),"Current hit accepted");Check(!hit.CanAccept(request,6,6,7),"View-updated hit rejected");Check(!hit.CanAccept(request,5,7,7),"Trial-updated hit rejected");Check(!hit.CanAccept(request,5,6,8),"Draft-updated hit rejected");
        Check(!hit.CanAccept(new PickToken(cell,5,6,7,9),5,6,7),"Out-of-order request rejected");
        Check(!hit.CanAccept(new PickToken(new CellId(1,2,3,5),5,6,7,8),5,6,7),"Different 4D cell rejected");
        Check(!new CellHit(request,new Vec3(double.NaN,0,0)).CanAccept(request,5,6,7),"Nonfinite hit rejected");
        cases.Add(new{name="revision_bound_cell_pick",status="PASS",cell=cell.ToString()});
    }
}
