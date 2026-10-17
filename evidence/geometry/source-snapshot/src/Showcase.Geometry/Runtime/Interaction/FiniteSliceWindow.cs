using System;
using System.Collections.Generic;
namespace Showcase.Geometry.Interaction
{
    public static class FiniteSliceWindow
    {
        public const double DefaultHalfExtent=2.5;
        public static InstanceN ClipInstance(InstanceN source,SliceFrame frame,double halfExtent=DefaultHalfExtent)
        {
            Check(frame,halfExtent);var planes=new List<HalfspaceN>();WindowPlanes(planes,source,frame,halfExtent);
            var shape=new ConvexShape(source.Shape.Id+"/finite-window",planes,source.Shape.Bounds);
            return new InstanceN(shape,source.Position,source.Forward,source.Id);
        }
        static void Check(SliceFrame frame,double halfExtent)
        {if(!frame.IsOrthonormal()||halfExtent<=0||double.IsNaN(halfExtent)||double.IsInfinity(halfExtent))throw new ArgumentException("A finite orthonormal frame and positive finite half extent are required.");}
        // The source's constraints followed by the six window planes, as ClipInstance's shape holds them.
        static void WindowPlanes(List<HalfspaceN> planes,InstanceN source,SliceFrame frame,double halfExtent)
        {
            planes.AddRange(source.Shape.Planes);int id=1000000;
            foreach(var p in planes)if(p.FaceId>=id)id=checked(p.FaceId+1);
            foreach(var axis in new[]{frame.AxisX,frame.AxisY,frame.AxisZ})foreach(int sign in new[]{-1,1})
            {
                var world=axis*sign;var local=source.WorldToLocalDirection(world);
                planes.Add(new HalfspaceN(local,halfExtent+VecN.Dot(world,frame.Center-source.Position),id++,SurfaceKind.Side));
            }
        }
        [ThreadStatic] static List<HalfspaceN>? windowPlanes;
        // Same cut as SectionBuilder.Build(ClipInstance(...)), without building a ConvexShape per call.
        public static SectionResult Build(InstanceN source,SliceFrame frame,double halfExtent=DefaultHalfExtent){Check(frame,halfExtent);return Cut(source,frame,halfExtent);}
        /// <summary><see cref="Build"/> for many solids under one frame and window (the cell grid): the frame is checked here, once,
        /// instead of twice per cut. The function may be called from several threads at once.</summary>
        public static Func<InstanceN,SectionResult> Cutter(SliceFrame frame,double halfExtent=DefaultHalfExtent){Check(frame,halfExtent);return source=>Cut(source,frame,halfExtent);}
        static SectionResult Cut(InstanceN source,SliceFrame frame,double halfExtent)
        {
            var planes=windowPlanes??=new List<HalfspaceN>();planes.Clear();WindowPlanes(planes,source,frame,halfExtent);
            return SectionBuilder.Build(source,planes,frame,null,true);
        }
        /// <summary>The cells q in [min,max]^N whose unit cube about origin+q meets the window (|t|&lt;=halfExtent on the frame's three axes),
        /// passed to <paramref name="cell"/> in lexicographic order (the array is reused; copy it). A cell whose cut can be solid is never
        /// missed: the slabs and the window are widened by 1e-7, far beyond the cut's 1e-9 tolerance. Returns false, after <paramref name="cap"/>
        /// cells, when there are more.</summary>
        public static bool Cells(SliceFrame frame,VecN origin,double halfExtent,int min,int max,int cap,Action<int[]> cell)
        {
            Check(frame,halfExtent);if(!origin.IsFinite||origin.Rank>frame.Dimensions||min>max||cap<0||cell==null)throw new ArgumentException("A finite origin in the frame's space, a range and a callback are required.");
            return new CellWalk(frame,origin,halfExtent,min,max,cap,cell).Run();
        }
        // The cells are walked in the window's own coordinates t, not over the 9^N board: fixing q on one more axis keeps the part of the
        // window inside that cell's slab, and the next axis takes only the values that part reaches. Every board axis already bounds the
        // window at the root, so nearly every step leads to a cell and the work follows the cells met. Regions are convex polyhedra kept
        // as face polygons and clipped like SectionBuilder's cut.
        sealed class CellWalk
        {
            const double Slack=1e-7,Snap=1e-11;
            readonly int n,min,max,cap;readonly double half;readonly Vec3[] rows;readonly double[] offsets;readonly int[] q;readonly Action<int[]> emit;int found;
            readonly Stack<List<Vec3>> freePoints=new Stack<List<Vec3>>();readonly Stack<List<List<Vec3>>> freeRegions=new Stack<List<List<Vec3>>>();
            readonly Angle angle=new Angle();
            sealed class Angle : IComparer<Vec3>
            {
                public Vec3 C,X,Y;
                public int Compare(Vec3 a,Vec3 b)=>Math.Atan2(Vec3.Dot(a-C,Y),Vec3.Dot(a-C,X)).CompareTo(Math.Atan2(Vec3.Dot(b-C,Y),Vec3.Dot(b-C,X)));
            }
            public CellWalk(SliceFrame frame,VecN origin,double half,int min,int max,int cap,Action<int[]> emit)
            {
                n=frame.Dimensions;this.half=half;this.min=min;this.max=max;this.cap=cap;this.emit=emit;rows=new Vec3[n];offsets=new double[n];q=new int[n];
                // Axis i of the point at t is offsets[i]+rows[i].t in cell units.
                for(int i=0;i<n;i++){rows[i]=new Vec3(frame.AxisX.Axis(i),frame.AxisY.Axis(i),frame.AxisZ.Axis(i));offsets[i]=frame.Center.Axis(i)-origin.Axis(i);}
            }
            public bool Run()
            {
                double h=half+Slack;var region=RentRegion();
                foreach(var (axis,sign) in new[]{(0,-1),(0,1),(1,-1),(1,1),(2,-1),(2,1)})
                {
                    var face=RentPoints();
                    for(int corner=0;corner<4;corner++)
                    {
                        // Corners in cyclic order on the face where t[axis]=sign*h.
                        double a=corner==0||corner==3?-h:h,b=corner<2?-h:h;
                        face.Add(axis==0?new Vec3(sign*h,a,b):axis==1?new Vec3(a,sign*h,b):new Vec3(a,b,sign*h));
                    }
                    region.Add(face);
                }
                for(int i=0;i<n;i++){var next=Slab(region,i,min-.5-Slack,max+.5+Slack);if(next!=region)Release(region);if(next==null)return true;region=next;}
                bool complete=Visit(0,region);Release(region);return complete;
            }
            bool Visit(int axis,List<List<Vec3>> region)
            {
                if(axis==n){if(found==cap)return false;found++;emit(q);return true;}
                var r=rows[axis];double c=offsets[axis],lo=c,hi=c;
                if(r.Length>=1e-12){lo=double.PositiveInfinity;hi=double.NegativeInfinity;foreach(var face in region)foreach(var p in face){double g=Vec3.Dot(r,p)+c;if(g<lo)lo=g;if(g>hi)hi=g;}}
                int first=Math.Max(min,(int)Math.Ceiling(lo-.5-Slack)),last=Math.Min(max,(int)Math.Floor(hi+.5+Slack));
                for(int value=first;value<=last;value++)
                {
                    var part=Slab(region,axis,value-.5-Slack,value+.5+Slack);if(part==null)continue;
                    q[axis]=value;bool more=Visit(axis+1,part);if(part!=region)Release(part);
                    if(!more)return false;
                }
                return true;
            }
            // The part of the region whose coordinate on the axis lies in [lo,hi]: the region itself when nothing is cut off, null when empty.
            List<List<Vec3>>? Slab(List<List<Vec3>> region,int axis,double lo,double hi)
            {
                var r=rows[axis];double length=r.Length,c=offsets[axis];
                if(length<1e-12)return c>=lo&&c<=hi?region:null;
                var normal=r/length;var below=Clip(region,normal,(hi-c)/length);if(below==null)return null;
                var part=Clip(below,-normal,(c-lo)/length);if(below!=region&&part!=below)Release(below);
                return part;
            }
            List<List<Vec3>>? Clip(List<List<Vec3>> region,Vec3 normal,double offset)
            {
                bool inside=false,outside=false;
                foreach(var face in region)foreach(var p in face){if(Distance(normal,p,offset)>0)outside=true;else inside=true;}
                if(!outside)return region;if(!inside)return null;
                var result=RentRegion();var cap=RentPoints();
                foreach(var face in region)
                {
                    var kept=RentPoints();int count=face.Count;
                    for(int i=0;i<count;i++)
                    {
                        Vec3 a=face[i],z=face[(i+1)%count];double da=Distance(normal,a,offset),dz=Distance(normal,z,offset);
                        if(da<=0){Add(kept,a);if(da==0)Add(cap,a);}
                        if((da<=0)!=(dz<=0)){var hit=a+(z-a)*(da/(da-dz));Add(kept,hit);Add(cap,hit);}
                    }
                    if(kept.Count>0)result.Add(kept);else Return(kept);
                }
                if(cap.Count>2)
                {
                    Vec3 center=default;foreach(var p in cap)center+=p;center/=cap.Count;
                    angle.C=center;angle.X=Vec3.Cross(Math.Abs(normal.Z)<.8?new Vec3(0,0,1):new Vec3(0,1,0),normal).Normalized;angle.Y=Vec3.Cross(normal,angle.X);cap.Sort(angle);
                }
                if(cap.Count>0)result.Add(cap);else Return(cap);
                return result;
            }
            static double Distance(Vec3 normal,Vec3 p,double offset){double d=Vec3.Dot(normal,p)-offset;return Math.Abs(d)<=Snap?0:d;}
            static void Add(List<Vec3> points,Vec3 p){foreach(var x in points)if((x-p).LengthSquared<=Snap*Snap)return;points.Add(p);}
            List<Vec3> RentPoints()=>freePoints.Count>0?freePoints.Pop():new List<Vec3>(8);
            void Return(List<Vec3> points){points.Clear();freePoints.Push(points);}
            List<List<Vec3>> RentRegion()=>freeRegions.Count>0?freeRegions.Pop():new List<List<Vec3>>(16);
            void Release(List<List<Vec3>> region){foreach(var face in region)Return(face);region.Clear();freeRegions.Push(region);}
        }
    }
    public sealed class HyperplaneRelation
    {
        public ReferenceSection Reference {get;}
        // The current frame's first normal restricted to the reference chart; zero when the frame lies in the reference space,
        // and also (N>=5) when that normal is perpendicular to the chart while a later one still cuts it.
        public Vec3 RestrictedNormal {get;}
        public bool IsCoincident {get;}
        // Coincident: clipped convex volume. One constraint: ordered clipped intersection polygon.
        // Two constraints (N>=5, frame turned away from the chart): the clipped intersection segment. More: at most the point C.
        public IReadOnlyList<Vec3> Vertices {get;}
        public IReadOnlyList<int> Edges {get;}
        public IReadOnlyList<int> TriangleIndices {get;}
        public double ReferenceHalfExtent {get;}
        public double MainHalfExtent {get;}
        // C's coordinate along the first hidden axis of the reference section (its normal in 4D).
        public double FixedCoordinate {get;}
        public long ViewRevision {get;}
        // Where C sits in the reference chart. Zero unless a fixed chart origin was supplied.
        public Vec3 ChartCenter {get;}
        HyperplaneRelation(ReferenceSection reference,Vec3 normal,bool coincident,List<Vec3> vertices,List<int> edges,List<int> triangles,long revision,double referenceHalf,double mainHalf,double fixedCoordinate,Vec3 chartCenter)
        {Reference=reference;RestrictedNormal=normal;IsCoincident=coincident;Vertices=vertices.AsReadOnly();Edges=edges.AsReadOnly();TriangleIndices=triangles.AsReadOnly();ViewRevision=revision;ReferenceHalfExtent=referenceHalf;MainHalfExtent=mainHalf;FixedCoordinate=fixedCoordinate;ChartCenter=chartCenter;}
        readonly struct Plane
        {public readonly Vec3 N;public readonly double D;public Plane(Vec3 n,double d){N=n;D=d;}}
        // chartOrigin=null keeps the C-centred chart. A fixed origin (e.g. P) is first moved along the
        // reference normals into the reference space through C, so moving C shifts the drawn intersection.
        // A 4D reference view passed for a frame of higher N means the section with the same three shown axes.
        public static HyperplaneRelation Build(SliceFrame frame,ReferenceSection reference,double halfExtent=1,long viewRevision=0,double? mainHalfExtent=null,VecN? chartOrigin=null)
        {
            double mainHalf=mainHalfExtent??halfExtent;
            if(!frame.IsOrthonormal(1e-6)||halfExtent<=0||double.IsNaN(halfExtent)||double.IsInfinity(halfExtent)||mainHalf<=0||double.IsNaN(mainHalf)||double.IsInfinity(mainHalf))throw new ArgumentException("Finite orthonormal frame and windows required.");
            if(reference.Dimensions!=frame.Dimensions)reference=reference.In(frame.Dimensions);
            var referenceNormals=reference.Frame().Normals;
            var origin=frame.Center;
            if(chartOrigin.HasValue){origin=chartOrigin.Value;foreach(var normal in referenceNormals)origin+=normal*VecN.Dot(frame.Center-chartOrigin.Value,normal);}
            if(!origin.IsFinite)throw new ArgumentException("Chart origin must be finite.");
            var basis=reference.Frame(origin);
            var chartCenter=basis.Project(frame.Center);
            // The frame's normals restricted to the chart, made orthonormal; each one is a plane constraint on the intersection.
            // planeNormal is the vector constraints[0] was normalised from: the first normal's own restriction in 4D, but a later
            // normal's when the first one is perpendicular to the chart (N>=5, the Ctrl rotation axis turned away from W).
            var constraints=new List<Vec3>();Vec3 first=default,planeNormal=default;bool firstSet=false;
            foreach(var normal in frame.Normals)
            {
                var restrictedNormal=new Vec3(VecN.Dot(normal,basis.AxisX),VecN.Dot(normal,basis.AxisY),VecN.Dot(normal,basis.AxisZ));
                if(!firstSet){first=restrictedNormal;firstSet=true;}
                foreach(var c in constraints)restrictedNormal-=c*Vec3.Dot(restrictedNormal,c);
                if(restrictedNormal.Length>1e-10){if(constraints.Count==0)planeNormal=restrictedNormal;constraints.Add(restrictedNormal.Normalized);}
            }
            var planes=new List<Plane>();
            foreach(var direction in new[]{new Vec3(1,0,0),new Vec3(0,1,0),new Vec3(0,0,1)})foreach(int sign in new[]{-1,1})planes.Add(new Plane(direction*sign,halfExtent));
            foreach(var axis in new[]{frame.AxisX,frame.AxisY,frame.AxisZ})
            {
                var restricted=new Vec3(VecN.Dot(axis,basis.AxisX),VecN.Dot(axis,basis.AxisY),VecN.Dot(axis,basis.AxisZ));
                foreach(int sign in new[]{-1,1})planes.Add(new Plane(restricted*sign,mainHalf+Vec3.Dot(restricted*sign,chartCenter)));
            }
            var hiddenAxes=reference.Hidden;double fixedCoordinate=hiddenAxes.Length>0?frame.Center.Axis(hiddenAxes[0]):0;
            double eps=1e-9*Math.Max(1,Math.Max(halfExtent,mainHalf+chartCenter.Length));
            var edges=new List<int>();var triangles=new List<int>();
            if(constraints.Count==0)
            {
                var vertices=new List<Vec3>();
                for(int i=0;i<planes.Count;i++)for(int j=i+1;j<planes.Count;j++)for(int k=j+1;k<planes.Count;k++)
                {
                    var a=planes[i];var b=planes[j];var c=planes[k];var bc=Vec3.Cross(b.N,c.N);double determinant=Vec3.Dot(a.N,bc);
                    if(Math.Abs(determinant)<1e-12)continue;
                    var point=(bc*a.D+Vec3.Cross(c.N,a.N)*b.D+Vec3.Cross(a.N,b.N)*c.D)/determinant;
                    bool inside=point.IsFinite;foreach(var p in planes)if(Vec3.Dot(p.N,point)>p.D+eps){inside=false;break;}
                    if(inside)Add(vertices,point,eps);
                }
                var faceKeys=new HashSet<string>();var edgeKeys=new HashSet<string>();
                foreach(var plane in planes)
                {
                    if(plane.N.Length<1e-12)continue;var face=new List<int>();
                    for(int i=0;i<vertices.Count;i++)if(Math.Abs(Vec3.Dot(plane.N,vertices[i])-plane.D)<=eps*2)face.Add(i);
                    if(face.Count<3)continue;
                    var sorted=new List<int>(face);sorted.Sort();string key=string.Join(",",sorted);if(!faceKeys.Add(key))continue;
                    Vec3 center=default;foreach(int index in face)center+=vertices[index];center/=face.Count;
                    Basis(plane.N,out var x,out var y);var centroid=center;
                    face.Sort((a,b)=>Angle(vertices[a]-centroid,x,y).CompareTo(Angle(vertices[b]-centroid,x,y)));
                    for(int i=1;i+1<face.Count;i++){triangles.Add(face[0]);triangles.Add(face[i]);triangles.Add(face[i+1]);}
                    for(int i=0;i<face.Count;i++){int a=face[i],b=face[(i+1)%face.Count];string edge=Math.Min(a,b)+","+Math.Max(a,b);if(edgeKeys.Add(edge)){edges.Add(a);edges.Add(b);}}
                }
                return new HyperplaneRelation(reference,first,true,vertices,edges,triangles,viewRevision,halfExtent,mainHalf,fixedCoordinate,chartCenter);
            }
            if(constraints.Count>=2)
            {
                // The intersection is the line through C along the direction every constraint leaves free (a point when nothing is free).
                var points=new List<Vec3>();
                if(constraints.Count==2)
                {
                    var direction=Vec3.Cross(constraints[0],constraints[1]).Normalized;double t0=double.NegativeInfinity,t1=double.PositiveInfinity;bool empty=false;
                    foreach(var plane in planes)
                    {
                        double s=Vec3.Dot(plane.N,chartCenter)-plane.D,rate=Vec3.Dot(plane.N,direction);
                        if(Math.Abs(rate)<1e-12){if(s>eps){empty=true;break;}continue;}
                        double t=-s/rate;if(rate>0)t1=Math.Min(t1,t);else t0=Math.Max(t0,t);
                    }
                    if(!empty&&t1-t0>eps){points.Add(chartCenter+direction*t0);points.Add(chartCenter+direction*t1);edges.Add(0);edges.Add(1);}
                }
                else
                {
                    bool inside=true;foreach(var plane in planes)if(Vec3.Dot(plane.N,chartCenter)>plane.D+eps){inside=false;break;}
                    if(inside)points.Add(chartCenter);
                }
                return new HyperplaneRelation(reference,first,false,points,edges,triangles,viewRevision,halfExtent,mainHalf,fixedCoordinate,chartCenter);
            }
            var n=first;
            var cube=new Vec3[8];for(int i=0;i<8;i++)cube[i]=new Vec3((i&1)==0?-halfExtent:halfExtent,(i&2)==0?-halfExtent:halfExtent,(i&4)==0?-halfExtent:halfExtent);
            var polygon=new List<Vec3>();var intersectionNormal=constraints[0];double offset=Vec3.Dot(intersectionNormal,chartCenter);
            for(int i=0;i<8;i++)for(int axis=0;axis<3;axis++)
            {
                int j=i^(1<<axis);if(i>=j)continue;
                var a=cube[i];var b=cube[j];double da=Vec3.Dot(intersectionNormal,a)-offset,db=Vec3.Dot(intersectionNormal,b)-offset;
                if(Math.Abs(da)<=eps)Add(polygon,a,eps);if(Math.Abs(db)<=eps)Add(polygon,b,eps);
                if((da < -eps&&db > eps)||(da > eps&&db < -eps))Add(polygon,a+(b-a)*(da/(da-db)),eps);
            }
            // Sort around the polygon's own centroid: with a fixed origin the plane need not pass through 0.
            Vec3 middle=default;foreach(var point in polygon)middle+=point;if(polygon.Count>0)middle/=polygon.Count;
            Basis(planeNormal,out var px,out var py);polygon.Sort((a,b)=>Angle(a-middle,px,py).CompareTo(Angle(b-middle,px,py)));
            // Sutherland-Hodgman on the plane polygon; all inequalities are in the same C-based chart.
            for(int p=6;p<planes.Count&&polygon.Count>0;p++)
            {
                var next=new List<Vec3>();var plane=planes[p];
                for(int i=0;i<polygon.Count;i++)
                {
                    var a=polygon[i];var b=polygon[(i+1)%polygon.Count];double da=Vec3.Dot(plane.N,a)-plane.D,db=Vec3.Dot(plane.N,b)-plane.D;
                    bool ai=da<=eps,bi=db<=eps;if(ai)Add(next,a,eps);
                    if(ai!=bi)Add(next,a+(b-a)*(da/(da-db)),eps);
                }
                polygon=next;
            }
            for(int i=0;i<polygon.Count;i++){edges.Add(i);edges.Add((i+1)%polygon.Count);}
            for(int i=1;i+1<polygon.Count;i++){triangles.Add(0);triangles.Add(i);triangles.Add(i+1);}
            return new HyperplaneRelation(reference,n,false,polygon,edges,triangles,viewRevision,halfExtent,mainHalf,fixedCoordinate,chartCenter);
        }
        static void Basis(Vec3 normal,out Vec3 x,out Vec3 y){normal=normal.Normalized;x=Vec3.Cross(Math.Abs(normal.Z)<.8?new Vec3(0,0,1):new Vec3(0,1,0),normal).Normalized;y=Vec3.Cross(normal,x);}
        static double Angle(Vec3 p,Vec3 x,Vec3 y)=>Math.Atan2(Vec3.Dot(p,y),Vec3.Dot(p,x));
        static void Add(List<Vec3> points,Vec3 point,double eps){foreach(var p in points)if((p-point).Length<eps)return;points.Add(point);}
    }
}
