using System;
using System.Collections.Generic;
namespace Showcase.Geometry.Interaction
{
    public static class FiniteSliceWindow
    {
        public const double DefaultHalfExtent=2.5;
        public static InstanceN ClipInstance(InstanceN source,SliceFrame frame,double halfExtent=DefaultHalfExtent)
        {
            if(!frame.IsOrthonormal()||halfExtent<=0||double.IsNaN(halfExtent)||double.IsInfinity(halfExtent))throw new ArgumentException("A finite orthonormal frame and positive finite half extent are required.");
            var planes=new List<HalfspaceN>(source.Shape.Planes);int id=1000000;
            foreach(var p in planes)if(p.FaceId>=id)id=checked(p.FaceId+1);
            foreach(var axis in new[]{frame.AxisX,frame.AxisY,frame.AxisZ})foreach(int sign in new[]{-1,1})
            {
                var world=axis*sign;var local=source.WorldToLocalDirection(world);
                planes.Add(new HalfspaceN(local,halfExtent+VecN.Dot(world,frame.Center-source.Position),id++,SurfaceKind.Side));
            }
            var shape=new ConvexShape(source.Shape.Id+"/finite-window",planes,source.Shape.Bounds);
            return new InstanceN(shape,source.Position,source.Forward,source.Id);
        }
        public static SectionResult Build(InstanceN source,SliceFrame frame,double halfExtent=DefaultHalfExtent)
            =>SectionBuilder.Build(ClipInstance(source,frame,halfExtent),frame);
    }
    public sealed class HyperplaneRelation
    {
        public ReferenceView Reference {get;}
        public Vec3 RestrictedNormal {get;}
        public bool IsCoincident {get;}
        // Coincident: clipped convex volume. Otherwise: ordered clipped intersection polygon.
        public IReadOnlyList<Vec3> Vertices {get;}
        public IReadOnlyList<int> Edges {get;}
        public IReadOnlyList<int> TriangleIndices {get;}
        public double ReferenceHalfExtent {get;}
        public double MainHalfExtent {get;}
        public double FixedCoordinate {get;}
        public long ViewRevision {get;}
        // Where C sits in the reference chart. Zero unless a fixed chart origin was supplied.
        public Vec3 ChartCenter {get;}
        HyperplaneRelation(ReferenceView reference,Vec3 normal,bool coincident,List<Vec3> vertices,List<int> edges,List<int> triangles,long revision,double referenceHalf,double mainHalf,double fixedCoordinate,Vec3 chartCenter)
        {Reference=reference;RestrictedNormal=normal;IsCoincident=coincident;Vertices=vertices.AsReadOnly();Edges=edges.AsReadOnly();TriangleIndices=triangles.AsReadOnly();ViewRevision=revision;ReferenceHalfExtent=referenceHalf;MainHalfExtent=mainHalf;FixedCoordinate=fixedCoordinate;ChartCenter=chartCenter;}
        readonly struct Plane
        {public readonly Vec3 N;public readonly double D;public Plane(Vec3 n,double d){N=n;D=d;}}
        // chartOrigin=null keeps the C-centred chart. A fixed origin (e.g. P) is first moved along the
        // reference normal into the reference space through C, so moving C shifts the drawn intersection.
        public static HyperplaneRelation Build(SliceFrame frame,ReferenceView reference,double halfExtent=1,long viewRevision=0,double? mainHalfExtent=null,VecN? chartOrigin=null)
        {
            double mainHalf=mainHalfExtent??halfExtent;
            if(!frame.IsOrthonormal(1e-6)||halfExtent<=0||double.IsNaN(halfExtent)||double.IsInfinity(halfExtent)||mainHalf<=0||double.IsNaN(mainHalf)||double.IsInfinity(mainHalf))throw new ArgumentException("Finite orthonormal frame and windows required.");
            var referenceNormal=SliceAxes.Reference(reference).Normal;
            var origin=chartOrigin.HasValue?chartOrigin.Value+referenceNormal*VecN.Dot(frame.Center-chartOrigin.Value,referenceNormal):frame.Center;
            if(!origin.IsFinite)throw new ArgumentException("Chart origin must be finite.");
            var basis=SliceAxes.Reference(reference,origin);
            var chartCenter=basis.Project(frame.Center);
            var n=new Vec3(VecN.Dot(frame.Normal,basis.AxisX),VecN.Dot(frame.Normal,basis.AxisY),VecN.Dot(frame.Normal,basis.AxisZ));
            var planes=new List<Plane>();
            foreach(var direction in new[]{new Vec3(1,0,0),new Vec3(0,1,0),new Vec3(0,0,1)})foreach(int sign in new[]{-1,1})planes.Add(new Plane(direction*sign,halfExtent));
            foreach(var axis in new[]{frame.AxisX,frame.AxisY,frame.AxisZ})
            {
                var restricted=new Vec3(VecN.Dot(axis,basis.AxisX),VecN.Dot(axis,basis.AxisY),VecN.Dot(axis,basis.AxisZ));
                foreach(int sign in new[]{-1,1})planes.Add(new Plane(restricted*sign,mainHalf+Vec3.Dot(restricted*sign,chartCenter)));
            }
            double fixedCoordinate=frame.Center.Axis(SliceAxes.NormalCoreAxis(reference));
            double eps=1e-9*Math.Max(1,Math.Max(halfExtent,mainHalf+chartCenter.Length));
            var edges=new List<int>();var triangles=new List<int>();
            if(n.Length<1e-10)
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
                return new HyperplaneRelation(reference,n,true,vertices,edges,triangles,viewRevision,halfExtent,mainHalf,fixedCoordinate,chartCenter);
            }
            var cube=new Vec3[8];for(int i=0;i<8;i++)cube[i]=new Vec3((i&1)==0?-halfExtent:halfExtent,(i&2)==0?-halfExtent:halfExtent,(i&4)==0?-halfExtent:halfExtent);
            var points=new List<Vec3>();var intersectionNormal=n.Normalized;double offset=Vec3.Dot(intersectionNormal,chartCenter);
            for(int i=0;i<8;i++)for(int axis=0;axis<3;axis++)
            {
                int j=i^(1<<axis);if(i>=j)continue;
                var a=cube[i];var b=cube[j];double da=Vec3.Dot(intersectionNormal,a)-offset,db=Vec3.Dot(intersectionNormal,b)-offset;
                if(Math.Abs(da)<=eps)Add(points,a,eps);if(Math.Abs(db)<=eps)Add(points,b,eps);
                if((da < -eps&&db > eps)||(da > eps&&db < -eps))Add(points,a+(b-a)*(da/(da-db)),eps);
            }
            // Sort around the polygon's own centroid: with a fixed origin the plane need not pass through 0.
            Vec3 middle=default;foreach(var point in points)middle+=point;if(points.Count>0)middle/=points.Count;
            Basis(n,out var px,out var py);points.Sort((a,b)=>Angle(a-middle,px,py).CompareTo(Angle(b-middle,px,py)));
            // Sutherland-Hodgman on the plane polygon; all inequalities are in the same C-based chart.
            for(int p=6;p<planes.Count&&points.Count>0;p++)
            {
                var next=new List<Vec3>();var plane=planes[p];
                for(int i=0;i<points.Count;i++)
                {
                    var a=points[i];var b=points[(i+1)%points.Count];double da=Vec3.Dot(plane.N,a)-plane.D,db=Vec3.Dot(plane.N,b)-plane.D;
                    bool ai=da<=eps,bi=db<=eps;if(ai)Add(next,a,eps);
                    if(ai!=bi)Add(next,a+(b-a)*(da/(da-db)),eps);
                }
                points=next;
            }
            for(int i=0;i<points.Count;i++){edges.Add(i);edges.Add((i+1)%points.Count);}
            for(int i=1;i+1<points.Count;i++){triangles.Add(0);triangles.Add(i);triangles.Add(i+1);}
            return new HyperplaneRelation(reference,n,false,points,edges,triangles,viewRevision,halfExtent,mainHalf,fixedCoordinate,chartCenter);
        }
        static void Basis(Vec3 normal,out Vec3 x,out Vec3 y){normal=normal.Normalized;x=Vec3.Cross(Math.Abs(normal.Z)<.8?new Vec3(0,0,1):new Vec3(0,1,0),normal).Normalized;y=Vec3.Cross(normal,x);}
        static double Angle(Vec3 p,Vec3 x,Vec3 y)=>Math.Atan2(Vec3.Dot(p,y),Vec3.Dot(p,x));
        static void Add(List<Vec3> points,Vec3 point,double eps){foreach(var p in points)if((p-point).Length<eps)return;points.Add(point);}
    }
}
