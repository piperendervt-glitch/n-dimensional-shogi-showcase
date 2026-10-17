using System;
using System.Collections.Generic;
using System.Linq;

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
    /// <summary>Vector of an N-dimensional logical space (axis 0 = F, 1 = U, 2 = V, 3 = W, 4.. = further axes).
    /// Axes a vector does not carry are zero, so the four-component form new VecN(f,u,v,w) is valid in every N
    /// and default(VecN) is the origin. Operations between vectors of different N work on the larger N.</summary>
    public readonly struct VecN : IEquatable<VecN>
    {
        public const int MaxDimensions = 7;
        private readonly double[]? c;
        public VecN(double f, double u, double v, double w) { c = new[] { f, u, v, w }; }
        public VecN(params double[] components)
        {
            if (components == null || components.Length == 0 || components.Length > MaxDimensions) throw new ArgumentException("A vector has 1 to " + MaxDimensions + " components.");
            c = (double[])components.Clone();
        }
        private VecN(double[] owned, bool _) { c = owned; }
        public static VecN Zero(int dimensions) => new VecN(new double[Check(dimensions)], true);
        /// <summary>Unit vector along a core axis, carrying <paramref name="dimensions"/> components (at least axis+1).</summary>
        public static VecN Unit(int axis, int dimensions = 4)
        {
            var v = new double[Check(Math.Max(dimensions, axis + 1))]; v[axis] = 1; return new VecN(v, true);
        }
        static int Check(int dimensions) => dimensions >= 1 && dimensions <= MaxDimensions ? dimensions : throw new ArgumentOutOfRangeException(nameof(dimensions));
        public int Dimensions => c?.Length ?? 0;
        /// <summary>Number of leading components up to the last nonzero one.</summary>
        public int Rank { get { int n = Dimensions; while (n > 0 && c![n - 1] == 0) n--; return n; } }
        public double this[int i] => Axis(i);
        /// <summary>Component on <paramref name="axis"/>, 0 when the vector carries fewer components.</summary>
        public double Axis(int axis) => c != null && axis >= 0 && axis < c.Length ? c[axis] : 0;
        public double X => Axis(0);
        public double Y => Axis(1);
        public double Z => Axis(2);
        public double W => Axis(3);
        public double[] ToArray() => c == null ? Array.Empty<double>() : (double[])c.Clone();
        /// <summary>The components padded (or, when the extra ones are zero, trimmed) to exactly <paramref name="dimensions"/>.</summary>
        public double[] ToArray(int dimensions)
        {
            var v = new double[Check(dimensions)];
            for (int i = 0; i < Dimensions; i++) { if (i < dimensions) v[i] = c![i]; else if (c![i] != 0) throw new ArgumentException("Vector has more dimensions than requested."); }
            return v;
        }
        public VecN Padded(int dimensions) => new VecN(ToArray(dimensions), true);
        public VecN WithAxis(int axis, double value)
        {
            var v = new double[Math.Max(Dimensions, axis + 1)]; for (int i = 0; i < Dimensions; i++) v[i] = c![i]; v[axis] = value; return new VecN(v, true);
        }
        public double Length => Math.Sqrt(Dot(this, this));
        public bool IsFinite { get { if (c == null) return true; foreach (var x in c) if (double.IsNaN(x) || double.IsInfinity(x)) return false; return true; } }
        public VecN Normalized { get { double l = Length; return l > 0 ? this * (1 / l) : this; } }
        public static VecN operator +(VecN a, VecN b) { int n = Math.Max(a.Dimensions, b.Dimensions); if (n == 0) return default; var v = new double[n]; for (int i = 0; i < n; i++) v[i] = a.Axis(i) + b.Axis(i); return new VecN(v, true); }
        public static VecN operator -(VecN a, VecN b) { int n = Math.Max(a.Dimensions, b.Dimensions); if (n == 0) return default; var v = new double[n]; for (int i = 0; i < n; i++) v[i] = a.Axis(i) - b.Axis(i); return new VecN(v, true); }
        public static VecN operator -(VecN a) => a * -1;
        public static VecN operator *(VecN a, double b) { if (a.c == null) return a; var v = new double[a.c.Length]; for (int i = 0; i < v.Length; i++) v[i] = a.c[i] * b; return new VecN(v, true); }
        public static VecN operator /(VecN a, double b) => a * (1 / b);
        public static double Dot(VecN a, VecN b) { int n = Math.Min(a.Dimensions, b.Dimensions); double d = 0; for (int i = 0; i < n; i++) d += a.c![i] * b.c![i]; return d; }
        public bool Equals(VecN x) { int n = Math.Max(Dimensions, x.Dimensions); for (int i = 0; i < n; i++) if (Axis(i) != x.Axis(i)) return false; return true; }
        public override bool Equals(object? o) => o is VecN v && Equals(v);
        public override int GetHashCode() { int n = Rank, h = 17; for (int i = 0; i < n; i++) h = h * 31 + c![i].GetHashCode(); return h; }
        public static bool operator ==(VecN a, VecN b) => a.Equals(b);
        public static bool operator !=(VecN a, VecN b) => !a.Equals(b);
        public override string ToString() => "(" + string.Join(",", (c ?? Array.Empty<double>()).Select(x => x.ToString("R"))) + ")";
    }
    public readonly struct Bounds3
    {
        public readonly Vec3 Min, Max;
        public Bounds3(Vec3 min, Vec3 max) { Min=min; Max=max; }
        public Vec3 Size => Max-Min;
    }
    /// <summary>Axis-aligned box of a logical N-space; <see cref="Corner"/> enumerates its 2^N corners.</summary>
    public readonly struct VecBounds
    {
        public readonly VecN Min, Max;
        public VecBounds(VecN min, VecN max) { Min=min; Max=max; }
        public int Dimensions => Math.Max(4, Math.Max(Min.Dimensions, Max.Dimensions));
        public int CornerCount => 1 << Dimensions;
        public VecN Corner(int bits)
        {
            int n = Dimensions; var v = new double[n];
            for (int i = 0; i < n; i++) v[i] = (bits & (1 << i)) == 0 ? Min.Axis(i) : Max.Axis(i);
            return new VecN(v);
        }
    }
    public enum ShapeVariant { A, C }
    public enum SurfaceKind { Side, Top, Bottom, End, Bevel }
    public enum SectionStatus { Empty, ContactLowerDimensional, Volume3D, NumericallyUncertain, CapacityExceeded }
    public readonly struct HalfspaceN
    {
        public readonly VecN Normal;
        public readonly double Offset;
        public readonly int FaceId;
        public readonly SurfaceKind Surface;
        public HalfspaceN(VecN normal,double offset,int faceId,SurfaceKind surface=SurfaceKind.Side)
        { double len=normal.Length; if(len==0||!normal.IsFinite||double.IsNaN(offset)||double.IsInfinity(offset)) throw new ArgumentException("Source constraint must have a finite, nonzero normal and finite offset."); Normal=normal*(1/len); Offset=offset/len; FaceId=faceId; Surface=surface; }
    }
    public sealed class ConvexShape
    {
        private readonly IReadOnlyList<HalfspaceN> planes;
        public string Id { get; }
        public string Version => "geometry/0.1";
        public IReadOnlyList<HalfspaceN> Planes => planes;
        public VecBounds Bounds { get; }
        public int Dimensions => Bounds.Dimensions;
        public ConvexShape(string id,IEnumerable<HalfspaceN> constraints,VecBounds bounds) { Id=id; planes=new List<HalfspaceN>(constraints).AsReadOnly(); Bounds=bounds; if(planes.Count<4) throw new ArgumentException("A bounded solid requires constraints."); }
    }
    public readonly struct InstanceN
    {
        public readonly ConvexShape Shape;
        public readonly VecN Position;
        public readonly int Forward;
        public readonly string Id;
        public InstanceN(ConvexShape shape,VecN position,int forward=1,string id="") { if(forward!=1&&forward!=-1) throw new ArgumentOutOfRangeException(nameof(forward)); Shape=shape; Position=position; Forward=forward; Id=id; }
        // Side B faces the other way along F and U; height and every hidden axis keep their sign.
        public VecN LocalToWorldDirection(VecN p) => Forward == 1 ? p : p.WithAxis(0, -p.X).WithAxis(1, -p.Y);
        public VecN WorldToLocalDirection(VecN p) => LocalToWorldDirection(p);
        public VecN LocalToWorld(VecN p) => Position+LocalToWorldDirection(p);
        public VecN WorldToLocal(VecN p) => WorldToLocalDirection(p-Position);
    }
    /// <summary>A 3-dimensional slice of an N-space: three orthonormal axes through <see cref="Center"/> and the N-3 normals that complete the basis.
    /// A frame with one normal is the original 4D frame; <see cref="Normal"/> is that first normal.</summary>
    public readonly struct SliceFrame
    {
        public readonly VecN Center, AxisX, AxisY, AxisZ;
        private readonly VecN[]? normals;
        public SliceFrame(VecN center,VecN x,VecN y,VecN z,VecN normal) { Center=center; AxisX=x; AxisY=y; AxisZ=z; normals=new[]{normal}; }
        public SliceFrame(VecN center,VecN x,VecN y,VecN z,IReadOnlyList<VecN> normals)
        {
            if(normals==null||normals.Count>VecN.MaxDimensions-3) throw new ArgumentException("A slice frame has 0 to "+(VecN.MaxDimensions-3)+" normals.");
            Center=center; AxisX=x; AxisY=y; AxisZ=z; this.normals=normals.ToArray();
        }
        /// <summary>Frame from its full basis: the first three vectors are the slice axes, the rest its normals.</summary>
        public static SliceFrame FromVectors(VecN center,IReadOnlyList<VecN> vectors)
        {
            if(vectors==null||vectors.Count<3) throw new ArgumentException("A slice frame needs at least three vectors.");
            return new SliceFrame(center,vectors[0],vectors[1],vectors[2],vectors.Skip(3).ToArray());
        }
        public IReadOnlyList<VecN> Normals => normals ?? Array.Empty<VecN>();
        public VecN Normal => normals!=null&&normals.Length>0?normals[0]:default;
        /// <summary>N of the space this frame slices: 3 + the number of normals.</summary>
        public int Dimensions => 3+Normals.Count;
        /// <summary>AxisX, AxisY, AxisZ, then the normals: a full basis of the N-space.</summary>
        public VecN[] Vectors { get { var v=new VecN[Dimensions]; v[0]=AxisX; v[1]=AxisY; v[2]=AxisZ; for(int i=3;i<v.Length;i++) v[i]=normals![i-3]; return v; } }
        public SliceFrame WithCenter(VecN center)=>new SliceFrame(center,AxisX,AxisY,AxisZ,Normals);
        public static SliceFrame AtAngle(double degrees,VecN center=default)
        { double a=degrees*Math.PI/180,c=Math.Cos(a),s=Math.Sin(a); return new SliceFrame(center,new VecN(1,0,0,0),new VecN(0,c,0,s),new VecN(0,0,1,0),new VecN(0,-s,0,c)); }
        public VecN Lift(Vec3 p) => Center+AxisX*p.X+AxisY*p.Y+AxisZ*p.Z;
        public Vec3 Project(VecN p) { VecN d=p-Center; return new Vec3(VecN.Dot(d,AxisX),VecN.Dot(d,AxisY),VecN.Dot(d,AxisZ)); }
        public bool IsOrthonormal(double tolerance=1e-9)
        {
            if(!Center.IsFinite) return false; var a=Vectors; int n=a.Length;
            for(int i=0;i<n;i++) { if(!a[i].IsFinite||a[i].Rank>n) return false; for(int j=0;j<n;j++) if(Math.Abs(VecN.Dot(a[i],a[j])-(i==j?1:0))>tolerance) return false; }
            return true;
        }
    }
    public readonly struct SectionVertex
    {
        public readonly Vec3 Position, Normal;
        public readonly VecN LocalPosition;
        public readonly int FaceId;
        public readonly SurfaceKind Surface;
        public SectionVertex(Vec3 position,Vec3 normal,VecN localPosition,int faceId,SurfaceKind surface) { Position=position; Normal=normal; LocalPosition=localPosition; FaceId=faceId; Surface=surface; }
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
