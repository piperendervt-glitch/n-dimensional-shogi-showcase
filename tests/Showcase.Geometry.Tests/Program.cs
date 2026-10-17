using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;

internal static class Program
{
    private static int checks;
    private static readonly List<object> cases=new List<object>();
    private static readonly List<object> sweep=new List<object>();
    private static void Assert(bool condition,string message) { checks++;if(!condition)throw new Exception(message); }
    private static void Near(double a,double b,double eps,string message) => Assert(Math.Abs(a-b)<=eps,$"{message}: {a:R} versus {b:R}");
    public static int Main(string[] args)
    {
        string output=args.Length>0?args[0]:Path.Combine("evidence","geometry"); Directory.CreateDirectory(output);
        var watch=Stopwatch.StartNew();
        try
        {
            KnownCases(); PieceComparison(); FullSweeps(); SceneVisibility(); FloatTransfer(); ParallelEquivalence(); ThreeDimensional(); FaceIdsAndLocalCoordinates();
            var provenance=CaptureProvenance(output);
            watch.Stop();
            var report=new { status="TECHNICAL_PASS", testVersion="geometry-tests/0.1", geometryVersion="geometry/0.1", checks, elapsedMilliseconds=watch.ElapsedMilliseconds, runtime=Environment.Version.ToString(), provenance, primary="Incremental convex polyhedron clipping", independentOracle="Triple-plane intersections, all-halfspace feasibility, independent Gram-Schmidt rank", toleranceCells=1e-9, oracleComparisonToleranceCells=2e-6, materialFields=new { referenceA="M3(local.f, local.u, local.v)", visualC="M3(local.f, local.u + local.w, local.v)", approval="Fixed material coordinates only. Visual quality remains HUMAN_PENDING." }, cases, sweep, limitations=new [] { "Geometry CPU tests do not certify Unity render passes, GPU performance, visual appearance, or human acceptance.", "Near-zero numerical tolerance bands classify equivalent boundary points; they are not a thick viewing slab.", "Runtime implementation allocates managed lists; zero-GC optimization requires profiling and is not claimed." } };
            File.WriteAllText(Path.Combine(output,"geometry-test-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
            File.WriteAllText(Path.Combine(output,"summary.txt"),$"TECHNICAL_PASS\n{checks} assertions\n{watch.ElapsedMilliseconds} ms\nClipping and independent triple-plane oracle agree on recorded cases and sweeps.\nUnity visual / shadow / reflection / performance acceptance: NOT_RUN by this test.\n");
            Console.WriteLine($"PASS: {checks} assertions; {watch.ElapsedMilliseconds} ms; evidence {Path.GetFullPath(output)}");return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e);File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());return 1; }
    }
    private static object CaptureProvenance(string output)
    {
        var files=new List<string>(Directory.GetFiles(Path.Combine("src","Showcase.Geometry","Runtime"),"*.cs",SearchOption.AllDirectories));
        files.AddRange(new[]{"src/Showcase.Geometry/Showcase.Geometry.csproj","src/Showcase.Geometry/package.json","src/Showcase.Geometry/Runtime/Showcase.Geometry.asmdef","tests/Showcase.Geometry.Tests/Program.cs","tests/Showcase.Geometry.Tests/Showcase.Geometry.Tests.csproj","scenarios/geometry-v1.json"});
        files.Sort(StringComparer.Ordinal);var manifest=new List<object>();
        foreach(string file in files)
        {
            byte[] content=File.ReadAllBytes(file);string relative=file.Replace('\\','/');string snapshot=Path.Combine(output,"source-snapshot",relative);Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);File.WriteAllBytes(snapshot,content);
            using var sha=SHA256.Create();manifest.Add(new{path=relative,sha256=Convert.ToHexString(sha.ComputeHash(content)).ToLowerInvariant(),bytes=content.Length});
        }
        using var digest=SHA256.Create();
        var result=new { sourceFiles=manifest,testAssemblySha256=Convert.ToHexString(digest.ComputeHash(File.ReadAllBytes(typeof(Program).Assembly.Location))).ToLowerInvariant(),geometryAssemblySha256=Convert.ToHexString(digest.ComputeHash(File.ReadAllBytes(typeof(SectionBuilder).Assembly.Location))).ToLowerInvariant(),command="dotnet run --project tests/Showcase.Geometry.Tests/Showcase.Geometry.Tests.csproj --configuration Release -- evidence/geometry" };
        File.WriteAllText(Path.Combine(output,"source-provenance.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));return result;
    }
    private static void KnownCases()
    {
        var box=ShapeFactory.CreateBox(new VecN(-1,-1,-1,-.2),new VecN(1,1,1,.2));
        var instance=new InstanceN(box,default);
        var h0=Check(instance,SliceFrame.AtAngle(0),"box_H0");Near(h0.Volume,8,1e-8,"Box H0 volume");
        Near(Check(instance,SliceFrame.AtAngle(90),"box_H90").Volume,1.6,1e-8,"Box H90 volume");
        foreach(double edge in new []{-.2,.2})
        { var exact=Check(instance,SliceFrame.AtAngle(0,new VecN(0,0,0,edge)),"exact_w_endpoint_"+edge);Assert(exact.Status==SectionStatus.Volume3D,"Extrusion end is a full volume");Near(exact.Volume,8,1e-8,"Exact endpoint volume"); }
        var outside=Check(instance,SliceFrame.AtAngle(0,new VecN(0,0,0,.200001)),"beyond_endpoint");Assert(outside.Status==SectionStatus.Empty,"Outside w endpoint");
        Near(Check(instance,SliceFrame.AtAngle(0,new VecN(0,0,0,.199999)),"inside_endpoint").Volume,8,1e-8,"Inside endpoint");
        var cube=ShapeFactory.CreateBox(new VecN(-1,-1,-1,-1),new VecN(1,1,1,1));
        foreach(int support in new []{2,3,4})
        {
            VecN n=support==2?new VecN(1,1,0,0):support==3?new VecN(1,1,1,0):new VecN(1,1,1,1);n=n*(1/n.Length);
            VecN center=support==2?new VecN(1,1,0,0):support==3?new VecN(1,1,1,0):new VecN(1,1,1,1);
            var r=Check(new InstanceN(cube,default),Frame(n,center),"contact_dimension_"+(4-support));Assert(r.Status==SectionStatus.ContactLowerDimensional,"Tangent rank status");
            Assert(TriplePlaneOracle.Build(new InstanceN(cube,default),Frame(n,center)).Dimension==4-support,"Oracle tangent rank");
        }
        Assert(SectionBuilder.Build(instance,SliceFrame.AtAngle(0),new SectionOptions{MaxVertices=4}).Status==SectionStatus.CapacityExceeded,"Capacity distinguished from Empty");
        Assert(SectionBuilder.Build(instance,new SliceFrame(default,new VecN(2,0,0,0),new VecN(0,1,0,0),new VecN(0,0,1,0),new VecN(0,0,0,1))).Status==SectionStatus.NumericallyUncertain,"Invalid frame distinguished");
        Assert(SectionBuilder.Build(instance,SliceFrame.AtAngle(double.NaN)).Status==SectionStatus.NumericallyUncertain,"NaN frame distinguished");
        Assert(SectionBuilder.Build(new InstanceN(box,new VecN(double.PositiveInfinity,0,0,0)),SliceFrame.AtAngle(0)).Status==SectionStatus.NumericallyUncertain,"Nonfinite instance distinguished");
        // Two nonintersecting convex components must not be replaced by one support interval.
        var left=Check(new InstanceN(box,new VecN(0,0,0,-1)),SliceFrame.AtAngle(0),"component_left");
        var right=Check(new InstanceN(box,new VecN(0,0,0,1)),SliceFrame.AtAngle(0),"component_right");
        Assert(left.Status==SectionStatus.Empty&&right.Status==SectionStatus.Empty,"Disjoint component gap remains empty");
    }
    private static void PieceComparison()
    {
        var a=new InstanceN(ShapeFactory.CreatePawn(ShapeVariant.A),default);
        var c=new InstanceN(ShapeFactory.CreatePawn(ShapeVariant.C),default);
        var a0=Check(a,SliceFrame.AtAngle(0),"pawn_A_0");var c0=Check(c,SliceFrame.AtAngle(0),"pawn_C_0");var c90=Check(c,SliceFrame.AtAngle(90),"pawn_C_90");
        Near(a0.Volume,c0.Volume,1e-8,"A/C common center section");Near(c0.Volume,c90.Volume,1e-8,"C zero/ninety equality");
        Match(a0.Vertices.Select(x=>x.Position).ToArray(),c0.Vertices.Select(x=>x.Position).ToArray(),2e-7,"A/C central vertices");
        Match(c0.Vertices.Select(x=>x.Position).ToArray(),c90.Vertices.Select(x=>x.Position).ToArray(),2e-7,"C zero/ninety vertices");
        var c45=Check(c,SliceFrame.AtAngle(45),"pawn_C_45");Near(c45.Volume,c0.Volume*Math.Sqrt(2),2e-8,"C true intermediate widening");
        Check(a,SliceFrame.AtAngle(45),"pawn_A_45");Check(a,SliceFrame.AtAngle(90),"pawn_A_90");
        foreach(var v in c90.Vertices)Near(v.LocalPosition.Y,0,1e-8,"Original local u remains zero at H90");
        foreach(var variant in new[]{ShapeVariant.A,ShapeVariant.C})
        {
            var shape=ShapeFactory.CreatePawn(variant);
            Assert(Check(new InstanceN(shape,new VecN(2,0,1,1)),SliceFrame.AtAngle(0),"returned_"+variant).Status==SectionStatus.Empty,"Escaped pawn actually empty");
            Assert(Check(new InstanceN(shape,new VecN(2,0,1,1)),SliceFrame.AtAngle(90),"escaped_H90_"+variant).Status==SectionStatus.Volume3D,"w movement visible at fixed H90");
        }
    }
    private static void FullSweeps()
    {
        var board=new InstanceN(ShapeFactory.CreateBoard(),default);
        var a=new InstanceN(ShapeFactory.CreatePawn(ShapeVariant.A),new VecN(2,0,1,0));
        var c=new InstanceN(ShapeFactory.CreatePawn(ShapeVariant.C),new VecN(2,0,1,0));
        for(int degree=0;degree<=180;degree++)
        {
            var frame=SliceFrame.AtAngle(degree);Assert(frame.IsOrthonormal(),"Frame orthonormality "+degree);
            var testPoint=new VecN(1.3,-2.1,.7,.4);var rebuilt=frame.Lift(frame.Project(testPoint))+frame.Normal*VecN.Dot(frame.Normal,testPoint-frame.Center);
            Near((rebuilt-testPoint).Length,0,1e-10,"Orthogonal rotation inverse reconstruction");
            var br=Check(board,frame,"board_sweep_"+degree,false);var ar=Check(a,frame,"A_sweep_"+degree,false);var cr=Check(c,frame,"C_sweep_"+degree,false);
            Assert(br.Bounds.Min.Z>=-.800001&&br.Bounds.Max.Z<=.000001,"Board vertical bounds never change");
            Assert(br.Bounds.Size.Y<=9*Math.Sqrt(2)+1e-6,"Table fits maximum board section");
            Assert(ar.Status==SectionStatus.Volume3D&&cr.Status==SectionStatus.Volume3D,"Central hero stays visible");
            sweep.Add(new{degrees=degree,boardVolume=br.Volume,boardWidth=br.Bounds.Size.Y,pawnAVolume=ar.Volume,pawnCVolume=cr.Volume});
        }
        foreach(var variant in new[]{ShapeVariant.A,ShapeVariant.C})
        {
            var shape=ShapeFactory.CreatePawn(variant);
            for(int step=0;step<=40;step++)
            {
                double w=step/40.0;var result=Check(new InstanceN(shape,new VecN(2,0,1,w)),SliceFrame.AtAngle(90),"fixed_H90_move_"+variant+"_"+step,false);
                Assert(result.Status==SectionStatus.Volume3D,"Fixed H90 remains volumetric during w move");
            }
            for(int degree=90;degree>=0;degree--)Check(new InstanceN(shape,new VecN(2,0,1,1)),SliceFrame.AtAngle(degree),"return_"+variant+"_"+degree,false);
        }
        var shifted=new InstanceN(ShapeFactory.CreatePawn(ShapeVariant.C),new VecN(-3,.21,.75,.12),-1);
        foreach(double degree in new[]{-90.0,-45,-.00001,0,.00001,37,89.99999,90,90.00001,180,270,360}) Check(shifted,SliceFrame.AtAngle(degree),"translated_oriented_"+degree);
    }
    private static void SceneVisibility()
    {
        var pieces=new List<InstanceN>();var shape=ShapeFactory.CreatePawn(ShapeVariant.C);
        foreach(int side in new[]{-1,1})
        {
            for(int u=-4;u<=4;u++){pieces.Add(new InstanceN(shape,new VecN(side*4,u,0,0),-side,$"back_{side}_{u}"));pieces.Add(new InstanceN(shape,new VecN(side*2,u,0,0),-side,$"pawn_{side}_{u}"));}
            pieces.Add(new InstanceN(shape,new VecN(side*3,-2,0,0),-side,$"rook_{side}"));pieces.Add(new InstanceN(shape,new VecN(side*3,2,0,0),-side,$"bishop_{side}"));
        }
        var h0=pieces.Where(p=>SectionBuilder.Build(p,SliceFrame.AtAngle(0)).Status==SectionStatus.Volume3D).Select(p=>p.Id).ToArray();
        var h90=pieces.Where(p=>SectionBuilder.Build(p,SliceFrame.AtAngle(90)).Status==SectionStatus.Volume3D).Select(p=>p.Id).ToArray();
        Assert(h0.Length==40,"Initial 40 geometries visible");Assert(h90.Length==4,"Central-column initial visibility is four; no hide flags");
        cases.Add(new{name="initial_layout_geometry_only",atZero=h0,atNinety=h90,note="Dimensions / coordinates fixture only, not evidence of the full legal CPU scenario."});
    }
    private static void FloatTransfer()
    {
        var r=SectionBuilder.Build(new InstanceN(ShapeFactory.CreateBoard(),default),SliceFrame.AtAngle(45));double maxError=0;
        foreach(var v in r.Vertices)
        {
            var p=new Vec3((float)v.Position.X,(float)v.Position.Y,(float)v.Position.Z);maxError=Math.Max(maxError,(p-v.Position).Length);
            // The cyclic Unity permutation (f,u,v)->(u,v,f) preserves handedness.
            Vec3 unity=new Vec3(v.Position.Y,v.Position.Z,v.Position.X);Vec3 reverse=new Vec3(unity.Z,unity.X,unity.Y);Near((reverse-v.Position).Length,0,0,"Unity coordinate permutation inverse");
        }
        Assert(maxError<1e-6,"Float adapter conversion error");cases.Add(new{name="float_transfer",maxErrorCells=maxError,maxErrorMetres=maxError*ShapeFactory.CellPitchMetres});
    }
    private static void ParallelEquivalence()
    {
        var jobs=new List<(InstanceN instance,SliceFrame frame)>();
        var shapes=new[]{ShapeFactory.CreatePawn(ShapeVariant.A),ShapeFactory.CreatePawn(ShapeVariant.C),ShapeFactory.CreateBoard()};
        foreach(double angle in new[]{0.0,1,2,45,90})foreach(int side in new[]{-1,1})for(int u=-4;u<=4;u++)
            foreach(var shape in shapes)jobs.Add((new InstanceN(shape,new VecN(side*2,u,shape.Id=="board"?0:1,0),side),SliceFrame.AtAngle(angle)));
        var serial=jobs.Select(j=>SectionBuilder.Build(j.instance,j.frame)).ToArray();
        var parallel=new SectionResult[jobs.Count];int degree=Math.Min(8,Environment.ProcessorCount);
        Parallel.For(0,jobs.Count,new ParallelOptions{MaxDegreeOfParallelism=degree},i=>parallel[i]=SectionBuilder.Build(jobs[i].instance,jobs[i].frame));
        for(int i=0;i<serial.Length;i++)
        {
            var a=serial[i];var b=parallel[i];
            Assert(a.Status==b.Status&&a.Volume==b.Volume&&a.Diagnostic==b.Diagnostic,"Parallel status/volume "+i);
            Assert(a.Vertices.Length==b.Vertices.Length&&a.Indices.SequenceEqual(b.Indices),"Parallel topology "+i);
            for(int j=0;j<a.Vertices.Length;j++)
            {
                var x=a.Vertices[j];var y=b.Vertices[j];
                Assert(x.Position.X==y.Position.X&&x.Position.Y==y.Position.Y&&x.Position.Z==y.Position.Z,"Parallel exact position");
                Assert(x.Normal.X==y.Normal.X&&x.Normal.Y==y.Normal.Y&&x.Normal.Z==y.Normal.Z,"Parallel exact normal");
                Assert(x.LocalPosition.X==y.LocalPosition.X&&x.LocalPosition.Y==y.LocalPosition.Y&&x.LocalPosition.Z==y.LocalPosition.Z&&x.LocalPosition.W==y.LocalPosition.W,"Parallel exact local4D");
                Assert(x.FaceId==y.FaceId&&x.Surface==y.Surface,"Parallel provenance");
            }
        }
        cases.Add(new{name="parallel_serial_exact_equivalence",jobs=jobs.Count,maxDegreeOfParallelism=degree,angles=new[]{0,1,2,45,90},comparison="Exact status, volume, ordered indices, positions, normals, local4D, and provenance; shared immutable shapes, no shared options."});
    }
    // N=3: the board, a cell and a piece are 3D solids; the 3D frame (no normal) is the W=0 section of the 4D ones.
    private static void ThreeDimensional()
    {
        var frame=SliceAxes.ReferenceFrame(new[]{0,1,2},3,default);var frame4=SliceAxes.Reference(ReferenceView.XYZ);
        Assert(frame.Dimensions==3&&frame.Normals.Count==0&&frame.IsOrthonormal(),"3D frame");
        var board=ShapeFactory.CreateBoard(3);Assert(board.Dimensions==3&&board.Planes.Count==6+12,"3D board: six faces and twelve edge bevels");
        var b=Check(new InstanceN(board,default),frame,"board_N3");var b4=SectionBuilder.Build(new InstanceN(ShapeFactory.CreateBoard(),default),frame4);
        Assert(b.Status==SectionStatus.Volume3D,"3D board solid");Near(b.Volume,b4.Volume,1e-9,"3D board is the 4D board at W=0");
        Match(b.Vertices.Select(v=>v.Position).ToArray(),b4.Vertices.Select(v=>v.Position).ToArray(),1e-9,"3D board vertices");
        var cell=ShapeFactory.CreateBox(new VecN(-.5,-.5,-.5),new VecN(.5,.5,.5));Assert(cell.Dimensions==3&&cell.Planes.Count==6,"3D cell");
        Near(Check(new InstanceN(cell,new VecN(1,-2,3)),frame.WithCenter(new VecN(1.2,-2,3)),"cell_N3").Volume,1,1e-12,"3D cell volume");
        Near(FiniteSliceWindow.Build(new InstanceN(cell,default),frame,.25).Volume,.125,1e-12,"3D cell in a finite window");
        foreach(var variant in new[]{ShapeVariant.A,ShapeVariant.C})foreach(int forward in new[]{-1,1})
        {
            var piece=ShapeFactory.CreatePiece(variant,1,3);Assert(piece.Dimensions==3&&piece.Planes.All(x=>x.Normal.Dimensions==3),"3D piece "+variant);
            var position=new VecN(2,-1,0);var r=Check(new InstanceN(piece,position,forward),frame,"piece_N3_"+variant+"_"+forward);
            var r4=SectionBuilder.Build(new InstanceN(ShapeFactory.CreatePiece(variant),position,forward),frame4);
            Near(r.Volume,r4.Volume,1e-12,"3D piece is the 4D piece at W=0");Match(r.Vertices.Select(v=>v.Position).ToArray(),r4.Vertices.Select(v=>v.Position).ToArray(),1e-9,"3D piece vertices "+variant);
            // A turned 3D window still cuts the same solid.
            var turned=SliceRotations.Rotate(frame.WithCenter(position+new VecN(0,0,.1)),0,1,.4);Assert(Check(new InstanceN(piece,position,forward),turned,"piece_N3_turned_"+variant+"_"+forward).Status==SectionStatus.Volume3D,"Turned 3D window");
        }
        var relation=HyperplaneRelation.Build(frame,ReferenceSection.Initial(3),1);
        Assert(relation.IsCoincident&&relation.Vertices.Count==8&&relation.Edges.Count==24&&relation.TriangleIndices.Count==36,"3D relation is the whole chart cube");
        cases.Add(new{name="three_dimensional_board",boardVolume=b.Volume,boardPlanes=board.Planes.Count,note="N=3 board, cell and pieces are the W=0 sections of the 4D solids; the 3D frame has no hidden axis."});
    }
    // Bevel face ids are unique in every N and unchanged in 4D; local coordinates are those of WorldToLocal(Lift(p)) to the bit.
    private static void FaceIdsAndLocalCoordinates()
    {
        for(int n=3;n<=VecN.MaxDimensions;n++){var ids=ShapeFactory.CreateBoard(n).Planes.Select(p=>p.FaceId).ToList();Assert(ids.Distinct().Count()==ids.Count,"Unique board face ids N="+n);}
        var four=ShapeFactory.CreateBoard().Planes;int k=8;
        for(int i=0;i<8;i++)for(int j=i+1;j<8;j++)if(i/2!=j/2)Assert(four[k++].FaceId==100+i*8+j,"4D bevel id unchanged");
        var rnd=new Random(3);long bits(double x)=>BitConverter.DoubleToInt64Bits(x);int vertices=0;
        for(int job=0;job<300;job++)
        {
            int n=rnd.Next(3,VecN.MaxDimensions+1);var frame=SliceAxes.ReferenceFrame(new[]{0,1,2},n,new VecN(Enumerable.Range(0,n).Select(_=>rnd.NextDouble()-.5).ToArray()));
            for(int r=0;r<3;r++){int a=rnd.Next(n),b=rnd.Next(n);if(a!=b)frame=SliceRotations.Rotate(frame,a,b,rnd.NextDouble()*6.3);}
            var shape=n==4&&rnd.Next(2)==0?ShapeFactory.CreatePiece(ShapeVariant.C):ShapeFactory.CreatePiece(rnd.Next(2)==0?ShapeVariant.A:ShapeVariant.C,1,n);
            var instance=new InstanceN(shape,new VecN(Enumerable.Range(0,n).Select(_=>(rnd.NextDouble()-.5)*.4).ToArray()),rnd.Next(2)*2-1);
            foreach(var v in SectionBuilder.Build(instance,frame).Vertices)
            {
                var expected=instance.WorldToLocal(frame.Lift(v.Position));vertices++;
                Assert(v.LocalPosition.Dimensions==expected.Dimensions,"Local dimensions");
                for(int i=0;i<expected.Dimensions;i++)Assert(bits(v.LocalPosition.Axis(i))==bits(expected.Axis(i)),"Local coordinate bits");
            }
        }
        Assert(vertices>1000,"Local coordinate sample");
        cases.Add(new{name="face_ids_and_local_coordinates",vertices,note="Board bevel ids unique for N=3..11, 4D ids 100+8i+j; LocalPosition bit-identical to WorldToLocal(Lift(p))."});
    }
    private static SectionResult Check(InstanceN instance,SliceFrame frame,string name,bool record=true)
    {
        var r=SectionBuilder.Build(instance,frame);var oracle=TriplePlaneOracle.Build(instance,frame);
        Assert(r.Status==oracle.Status,$"Status discrepancy {name}: clip {r.Status} ({r.Diagnostic}), oracle {oracle.Status}");
        Assert(r.Status!=SectionStatus.NumericallyUncertain&&r.Status!=SectionStatus.CapacityExceeded,"Unexpected unresolved status "+name);
        if(r.Status!=SectionStatus.Empty)Match(r.Vertices.Select(v=>v.Position).ToArray(),oracle.Vertices,2e-6,"Oracle vertices "+name);
        if(r.Status==SectionStatus.Volume3D)Topology(r,instance,frame,name);
        if(record)cases.Add(new{name,status=r.Status.ToString(),vertices=r.Vertices.Length,triangles=r.Indices.Length/3,volume=r.Volume,oracleVertices=oracle.Vertices.Length});
        return r;
    }
    private static void Match(Vec3[] a,Vec3[] b,double eps,string context)
    {
        foreach(var p in a)Assert(b.Any(q=>(p-q).Length<eps),context+" missing point "+p);
        foreach(var p in b)Assert(a.Any(q=>(p-q).Length<eps),context+" additional point "+p);
    }
    private static void Topology(SectionResult result,InstanceN instance,SliceFrame frame,string name)
    {
        var unique=new List<Vec3>();var ids=new int[result.Vertices.Length];
        for(int i=0;i<ids.Length;i++)
        {
            var v=result.Vertices[i];int index=unique.FindIndex(p=>(p-v.Position).Length<1e-7);if(index<0){index=unique.Count;unique.Add(v.Position);}ids[i]=index;
            Assert(v.Position.IsFinite&&v.Normal.IsFinite,"Finite mesh "+name);Near(v.Normal.Length,1,1e-7,"Unit normal");
            Near((instance.LocalToWorld(v.LocalPosition)-frame.Lift(v.Position)).Length,0,2e-7,"Material coordinates map to source position");
            var sourceFace=instance.Shape.Planes.Single(p=>p.FaceId==v.FaceId);
            Near(VecN.Dot(sourceFace.Normal,v.LocalPosition),sourceFace.Offset,1e-7,"Face provenance identifies containing source constraint");
            var worldNormal=instance.LocalToWorldDirection(sourceFace.Normal);
            var expectedNormal=new Vec3(VecN.Dot(worldNormal,frame.AxisX),VecN.Dot(worldNormal,frame.AxisY),VecN.Dot(worldNormal,frame.AxisZ)).Normalized;
            Near((v.Normal-expectedNormal).Length,0,1e-7,"Restricted source normal");
            foreach(var plane in instance.Shape.Planes)Assert(VecN.Dot(plane.Normal,v.LocalPosition)<=plane.Offset+1e-7,"Vertex satisfies 4D source constraint "+name);
        }
        var edges=new Dictionary<(int,int),(int count,int orientation)>();var triangles=new HashSet<string>();
        for(int i=0;i<result.Indices.Length;i+=3)
        {
            int ai=result.Indices[i],bi=result.Indices[i+1],ci=result.Indices[i+2];
            var a=result.Vertices[ai];var b=result.Vertices[bi];var c=result.Vertices[ci];Vec3 cross=Vec3.Cross(b.Position-a.Position,c.Position-a.Position);
            Assert(cross.Length>1e-13,"Nonzero triangle "+name);Assert(Vec3.Dot(cross,a.Normal)>0,"Outward winding "+name);
            int[] t={ids[ai],ids[bi],ids[ci]};var sorted=(int[])t.Clone();Array.Sort(sorted);Assert(triangles.Add(string.Join(",",sorted)),"No duplicate triangles "+name);
            for(int e=0;e<3;e++){int x=t[e],y=t[(e+1)%3];var key=(Math.Min(x,y),Math.Max(x,y));edges.TryGetValue(key,out var value);edges[key]=(value.count+1,value.orientation+(x<y?1:-1));}
        }
        foreach(var e in edges)Assert(e.Value.count==2&&e.Value.orientation==0,"Closed manifold with paired edge winding "+name+" "+e.Key);
        Assert(unique.Count-edges.Count+result.Indices.Length/3==2,"Convex closed surface Euler characteristic "+name);
    }
    private static SliceFrame Frame(VecN n,VecN center)
    {
        var axes=new List<VecN>();foreach(var e in new[]{new VecN(1,0,0,0),new VecN(0,1,0,0),new VecN(0,0,1,0),new VecN(0,0,0,1)})
        {var x=e-n*VecN.Dot(e,n);foreach(var b in axes)x=x-b*VecN.Dot(x,b);if(x.Length>1e-8)axes.Add(x*(1/x.Length));if(axes.Count==3)break;}
        return new SliceFrame(center,axes[0],axes[1],axes[2],n);
    }
}
