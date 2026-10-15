using System;
using System.Collections.Generic;

namespace Showcase.Geometry
{
    public readonly struct Vec3
    {
        public readonly double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double LengthSquared => Dot(this, this);
        public double Length => Math.Sqrt(LengthSquared);
        public bool IsFinite => !(double.IsNaN(X) || double.IsNaN(Y) || double.IsNaN(Z) || double.IsInfinity(X) || double.IsInfinity(Y) || double.IsInfinity(Z));
        public Vec3 Normalized => Length > 0 ? this / Length : default;
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static Vec3 operator -(Vec3 a) => a * -1;
        public static Vec3 operator *(Vec3 a, double b) => new Vec3(a.X*b,a.Y*b,a.Z*b);
        public static Vec3 operator /(Vec3 a, double b) => a*(1/b);
        public static double Dot(Vec3 a, Vec3 b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
        public override string ToString() => $"({X:R},{Y:R},{Z:R})";
    }
    public readonly struct Vec4
    {
        public readonly double X, Y, Z, W;
        public Vec4(double f, double u, double v, double w) { X=f; Y=u; Z=v; W=w; }
        public double Length => Math.Sqrt(Dot(this,this));
        public bool IsFinite => !(double.IsNaN(X)||double.IsNaN(Y)||double.IsNaN(Z)||double.IsNaN(W)||double.IsInfinity(X)||double.IsInfinity(Y)||double.IsInfinity(Z)||double.IsInfinity(W));
        public static Vec4 operator +(Vec4 a, Vec4 b) => new Vec4(a.X+b.X,a.Y+b.Y,a.Z+b.Z,a.W+b.W);
        public static Vec4 operator -(Vec4 a, Vec4 b) => new Vec4(a.X-b.X,a.Y-b.Y,a.Z-b.Z,a.W-b.W);
        public static Vec4 operator *(Vec4 a, double b) => new Vec4(a.X*b,a.Y*b,a.Z*b,a.W*b);
        public static double Dot(Vec4 a, Vec4 b) => a.X*b.X+a.Y*b.Y+a.Z*b.Z+a.W*b.W;
        public override string ToString() => $"({X:R},{Y:R},{Z:R},{W:R})";
    }
    public readonly struct Bounds3
    {
        public readonly Vec3 Min, Max;
        public Bounds3(Vec3 min, Vec3 max) { Min=min; Max=max; }
        public Vec3 Size => Max-Min;
    }
    public readonly struct Bounds4
    {
        public readonly Vec4 Min, Max;
        public Bounds4(Vec4 min, Vec4 max) { Min=min; Max=max; }
        public Vec4 Corner(int bits) => new Vec4((bits&1)==0?Min.X:Max.X,(bits&2)==0?Min.Y:Max.Y,(bits&4)==0?Min.Z:Max.Z,(bits&8)==0?Min.W:Max.W);
    }
    public enum ShapeVariant { A, C }
    public enum SurfaceKind { Side, Top, Bottom, End, Bevel }
    public enum SectionStatus { Empty, ContactLowerDimensional, Volume3D, NumericallyUncertain, CapacityExceeded }
    public readonly struct Halfspace4
    {
        public readonly Vec4 Normal;
        public readonly double Offset;
        public readonly int FaceId;
        public readonly SurfaceKind Surface;
        public Halfspace4(Vec4 normal,double offset,int faceId,SurfaceKind surface=SurfaceKind.Side)
        { double len=normal.Length; if(len==0||!normal.IsFinite||double.IsNaN(offset)||double.IsInfinity(offset)) throw new ArgumentException("4D source constraint must have a finite, nonzero normal and finite offset."); Normal=normal*(1/len); Offset=offset/len; FaceId=faceId; Surface=surface; }
    }
    public sealed class ConvexShape
    {
        private readonly IReadOnlyList<Halfspace4> planes;
        public string Id { get; }
        public string Version => "geometry/0.1";
        public IReadOnlyList<Halfspace4> Planes => planes;
        public Bounds4 Bounds { get; }
        public ConvexShape(string id,IEnumerable<Halfspace4> constraints,Bounds4 bounds) { Id=id; planes=new List<Halfspace4>(constraints).AsReadOnly(); Bounds=bounds; if(planes.Count<4) throw new ArgumentException("A bounded 4D solid requires constraints."); }
    }
    public readonly struct Instance4D
    {
        public readonly ConvexShape Shape;
        public readonly Vec4 Position;
        public readonly int Forward;
        public readonly string Id;
        public Instance4D(ConvexShape shape,Vec4 position,int forward=1,string id="") { if(forward!=1&&forward!=-1) throw new ArgumentOutOfRangeException(nameof(forward)); Shape=shape; Position=position; Forward=forward; Id=id; }
        public Vec4 LocalToWorldDirection(Vec4 p) => new Vec4(p.X*Forward,p.Y*Forward,p.Z,p.W);
        public Vec4 WorldToLocalDirection(Vec4 p) => LocalToWorldDirection(p);
        public Vec4 LocalToWorld(Vec4 p) => Position+LocalToWorldDirection(p);
        public Vec4 WorldToLocal(Vec4 p) => WorldToLocalDirection(p-Position);
    }
    public readonly struct SliceFrame
    {
        public readonly Vec4 Center, AxisX, AxisY, AxisZ, Normal;
        public SliceFrame(Vec4 center,Vec4 x,Vec4 y,Vec4 z,Vec4 normal) { Center=center; AxisX=x; AxisY=y; AxisZ=z; Normal=normal; }
        public static SliceFrame AtAngle(double degrees,Vec4 center=default)
        { double a=degrees*Math.PI/180,c=Math.Cos(a),s=Math.Sin(a); return new SliceFrame(center,new Vec4(1,0,0,0),new Vec4(0,c,0,s),new Vec4(0,0,1,0),new Vec4(0,-s,0,c)); }
        public Vec4 Lift(Vec3 p) => Center+AxisX*p.X+AxisY*p.Y+AxisZ*p.Z;
        public Vec3 Project(Vec4 p) { Vec4 d=p-Center; return new Vec3(Vec4.Dot(d,AxisX),Vec4.Dot(d,AxisY),Vec4.Dot(d,AxisZ)); }
        public bool IsOrthonormal(double tolerance=1e-9)
        { if(!Center.IsFinite) return false; Vec4[] a={AxisX,AxisY,AxisZ,Normal}; for(int i=0;i<4;i++) { if(!a[i].IsFinite) return false; for(int j=0;j<4;j++) if(Math.Abs(Vec4.Dot(a[i],a[j])-(i==j?1:0))>tolerance) return false; } return true; }
    }
    public readonly struct SectionVertex
    {
        public readonly Vec3 Position, Normal;
        public readonly Vec4 LocalPosition;
        public readonly int FaceId;
        public readonly SurfaceKind Surface;
        public SectionVertex(Vec3 position,Vec3 normal,Vec4 localPosition,int faceId,SurfaceKind surface) { Position=position; Normal=normal; LocalPosition=localPosition; FaceId=faceId; Surface=surface; }
    }
    public sealed class SectionResult
    {
        public SectionStatus Status { get; }
        public SectionVertex[] Vertices { get; }
        public int[] Indices { get; }
        public Bounds3 Bounds { get; }
        public double Volume { get; }
        public string Diagnostic { get; }
        public string ShapeVersion { get; }
        internal SectionResult(SectionStatus status,SectionVertex[] vertices,int[] indices,Bounds3 bounds,double volume,string diagnostic,string version)
        { Status=status; Vertices=vertices; Indices=indices; Bounds=bounds; Volume=volume; Diagnostic=diagnostic; ShapeVersion=version; }
    }
    public sealed class SectionOptions
    {
        public double Tolerance { get; set; }=1e-9;
        public int MaxVertices { get; set; }=8192;
        public int MaxFaces { get; set; }=1024;
        public int MaxConstraints { get; set; }=256;
    }
}
