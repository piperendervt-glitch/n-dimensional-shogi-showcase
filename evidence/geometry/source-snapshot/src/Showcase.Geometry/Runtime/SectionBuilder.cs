using System;
using System.Collections.Generic;

namespace Showcase.Geometry
{
    public static class SectionBuilder
    {
        private sealed class Face
        {
            public List<Vec3> Points;
            public readonly Vec3 Normal;
            public readonly int Id;
            public readonly SurfaceKind Surface;
            public Face(List<Vec3> points,Vec3 normal,int id,SurfaceKind surface) { Points=points; Normal=normal; Id=id; Surface=surface; }
        }
        public static SectionResult Build(Instance4D instance,SliceFrame frame,SectionOptions? options=null)
        {
            options=options??new SectionOptions(); double eps=options.Tolerance;
            if(eps<=0||double.IsNaN(eps)||double.IsInfinity(eps)||!frame.IsOrthonormal()||!instance.Position.IsFinite) return Failure(SectionStatus.NumericallyUncertain,instance,"Invalid tolerance, position, or nonorthonormal frame.");
            if(instance.Shape.Planes.Count>options.MaxConstraints) return Failure(SectionStatus.CapacityExceeded,instance,"Constraint capacity exceeded.");
            // Projection of the source AABB encloses the true intersection; expand it so
            // seed faces can never be mistaken for physical source faces.
            var corners=new List<Vec3>(16);
            for(int i=0;i<16;i++) corners.Add(frame.Project(instance.LocalToWorld(instance.Shape.Bounds.Corner(i))));
            Bounds3 bound=BoundsOf(corners); double margin=Math.Max(.01,bound.Size.Length*.1);
            Vec3 lo=bound.Min-new Vec3(margin,margin,margin),hi=bound.Max+new Vec3(margin,margin,margin);
            var faces=Seed(lo,hi);
            // Invocation-local scratch: reuse classifications and signed distances,
            // never shared state. The seed is not yet epsilon-deduplicated.
            var distances=new double[128];var faceKinds=new byte[32];bool facesCanonical=false;
            foreach(var plane in instance.Shape.Planes)
            {
                Vec4 worldN=instance.LocalToWorldDirection(plane.Normal);
                double worldB=plane.Offset+Vec4.Dot(worldN,instance.Position);
                Vec3 n=new Vec3(Vec4.Dot(frame.AxisX,worldN),Vec4.Dot(frame.AxisY,worldN),Vec4.Dot(frame.AxisZ,worldN));
                double b=worldB-Vec4.Dot(worldN,frame.Center),length=n.Length;
                if(length<1e-12)
                {
                    if(b < -eps) return Failure(SectionStatus.Empty,instance,"Excluded by a parallel source constraint.");
                    continue; // Coincident source facets can leave a full 3D solid.
                }
                n=n/length; b/=length;
                if(faceKinds.Length<faces.Count) Array.Resize(ref faceKinds,Math.Max(faceKinds.Length*2,faces.Count));
                int total=0;bool hadInside=false,hadOutside=false;
                for(int f=0;f<faces.Count;f++)
                {
                    var points=faces[f].Points;byte kind=0;
                    if(distances.Length<total+points.Count) Array.Resize(ref distances,Math.Max(distances.Length*2,total+points.Count));
                    foreach(var point in points)
                    {
                        double d=Vec3.Dot(n,point)-b;if(Math.Abs(d)<=eps)d=0;
                        distances[total++]=d;
                        if(d<=0){kind|=1;hadInside=true;}else{kind|=2;hadOutside=true;}
                    }
                    faceKinds[f]=kind;
                }
                if(!hadInside) return Failure(SectionStatus.Empty,instance,"Halfspace intersection is empty.");
                if(!hadOutside&&facesCanonical)
                {
                    // Capacity checks still occur at the same plane, even when it
                    // leaves geometry unchanged. Contact vertices remain in place.
                    if(faces.Count>options.MaxFaces) return Failure(SectionStatus.CapacityExceeded,instance,"Face capacity exceeded.");
                    if(total>options.MaxVertices) return Failure(SectionStatus.CapacityExceeded,instance,"Working vertex capacity exceeded.");
                    continue;
                }
                var cap=new List<Vec3>(); var clipped=new List<Face>(faces.Count+1);int offset=0;
                for(int f=0;f<faces.Count;f++)
                {
                    var face=faces[f];int count=face.Points.Count,startOffset=offset;offset+=count;
                    if(faceKinds[f]==2)continue;
                    if(faceKinds[f]==1&&facesCanonical)
                    {
                        // Coplanar vertices must enter the cap in the old traversal
                        // order, even though this unchanged face itself is reused.
                        for(int i=0;i<count;i++)if(distances[startOffset+i]==0)AddUnique(cap,face.Points[i],eps);
                        clipped.Add(face);continue;
                    }
                    var output=new List<Vec3>();
                    for(int i=0;i<count;i++)
                    {
                        Vec3 a=face.Points[i],z=face.Points[(i+1)%count];
                        double da=distances[startOffset+i],dz=distances[startOffset+(i+1)%count];
                        bool ina=da<=0,inz=dz<=0;
                        if(ina) { AddUnique(output,a,eps); if(da==0) AddUnique(cap,a,eps); }
                        if(ina!=inz)
                        {
                            double t=da/(da-dz); Vec3 hit=a+(z-a)*t;
                            AddUnique(output,hit,eps); AddUnique(cap,hit,eps);
                        }
                    }
                    if(output.Count>0) clipped.Add(new Face(output,face.Normal,face.Id,face.Surface));
                }
                if(hadOutside&&cap.Count>0) clipped.Add(new Face(Order(cap,n,eps),n,plane.FaceId,plane.Surface));
                faces=clipped;facesCanonical=true;
                if(faces.Count==0) return Failure(SectionStatus.Empty,instance,"Halfspace intersection is empty.");
                if(faces.Count>options.MaxFaces) return Failure(SectionStatus.CapacityExceeded,instance,"Face capacity exceeded.");
                total=0; foreach(var f in faces) total+=f.Points.Count;
                if(total>options.MaxVertices) return Failure(SectionStatus.CapacityExceeded,instance,"Working vertex capacity exceeded.");
            }
            var unique=new List<Vec3>(); foreach(var f in faces) foreach(var p in f.Points) AddUnique(unique,p,eps*4);
            if(unique.Count==0) return Failure(SectionStatus.Empty,instance,"No feasible vertices.");
            if(Dimension(unique,eps*8)<3)
            {
                var contacts=new SectionVertex[unique.Count]; for(int i=0;i<unique.Count;i++) contacts[i]=new SectionVertex(unique[i],default,instance.WorldToLocal(frame.Lift(unique[i])),-1,SurfaceKind.Side);
                return new SectionResult(SectionStatus.ContactLowerDimensional,contacts,Array.Empty<int>(),BoundsOf(unique),0,"Feasible intersection has dimension below three.",instance.Shape.Version);
            }
            var vertices=new List<SectionVertex>(); var indices=new List<int>(); double volume=0;
            foreach(var face in faces)
            {
                var ring=Order(face.Points,face.Normal,eps*4);
                if(ring.Count<3) continue;
                if(face.Id<0) return Failure(SectionStatus.NumericallyUncertain,instance,"Source bounds failed to enclose the solid.");
                int start=vertices.Count;
                foreach(var p in ring)
                {
                    if(!p.IsFinite) return Failure(SectionStatus.NumericallyUncertain,instance,"Nonfinite section vertex.");
                    vertices.Add(new SectionVertex(p,face.Normal,instance.WorldToLocal(frame.Lift(p)),face.Id,face.Surface));
                }
                for(int i=1;i<ring.Count-1;i++)
                {
                    indices.Add(start);indices.Add(start+i);indices.Add(start+i+1);
                    volume+=Vec3.Dot(ring[0],Vec3.Cross(ring[i],ring[i+1]))/6;
                }
            }
            if(vertices.Count>options.MaxVertices) return Failure(SectionStatus.CapacityExceeded,instance,"Output vertex capacity exceeded.");
            if(volume<=eps*eps*eps||double.IsNaN(volume)) return Failure(SectionStatus.NumericallyUncertain,instance,"Nonpositive oriented volume.");
            return new SectionResult(SectionStatus.Volume3D,vertices.ToArray(),indices.ToArray(),BoundsOf(unique),volume,"",instance.Shape.Version);
        }
        private static SectionResult Failure(SectionStatus status,Instance4D instance,string message) => new SectionResult(status,Array.Empty<SectionVertex>(),Array.Empty<int>(),default,0,message,instance.Shape.Version);
        private static List<Face> Seed(Vec3 lo,Vec3 hi)
        {
            var f=new List<Face>();
            f.Add(new Face(new List<Vec3>{new Vec3(lo.X,lo.Y,lo.Z),new Vec3(lo.X,lo.Y,hi.Z),new Vec3(lo.X,hi.Y,hi.Z),new Vec3(lo.X,hi.Y,lo.Z)},new Vec3(-1,0,0),-1,SurfaceKind.Side));
            f.Add(new Face(new List<Vec3>{new Vec3(hi.X,lo.Y,lo.Z),new Vec3(hi.X,hi.Y,lo.Z),new Vec3(hi.X,hi.Y,hi.Z),new Vec3(hi.X,lo.Y,hi.Z)},new Vec3(1,0,0),-2,SurfaceKind.Side));
            f.Add(new Face(new List<Vec3>{new Vec3(lo.X,lo.Y,lo.Z),new Vec3(hi.X,lo.Y,lo.Z),new Vec3(hi.X,lo.Y,hi.Z),new Vec3(lo.X,lo.Y,hi.Z)},new Vec3(0,-1,0),-3,SurfaceKind.Side));
            f.Add(new Face(new List<Vec3>{new Vec3(lo.X,hi.Y,lo.Z),new Vec3(lo.X,hi.Y,hi.Z),new Vec3(hi.X,hi.Y,hi.Z),new Vec3(hi.X,hi.Y,lo.Z)},new Vec3(0,1,0),-4,SurfaceKind.Side));
            f.Add(new Face(new List<Vec3>{new Vec3(lo.X,lo.Y,lo.Z),new Vec3(lo.X,hi.Y,lo.Z),new Vec3(hi.X,hi.Y,lo.Z),new Vec3(hi.X,lo.Y,lo.Z)},new Vec3(0,0,-1),-5,SurfaceKind.Bottom));
            f.Add(new Face(new List<Vec3>{new Vec3(lo.X,lo.Y,hi.Z),new Vec3(hi.X,lo.Y,hi.Z),new Vec3(hi.X,hi.Y,hi.Z),new Vec3(lo.X,hi.Y,hi.Z)},new Vec3(0,0,1),-6,SurfaceKind.Top));
            return f;
        }
        internal static void AddUnique(List<Vec3> points,Vec3 p,double eps) { foreach(var q in points) if((p-q).LengthSquared<=eps*eps) return; points.Add(p); }
        internal static Bounds3 BoundsOf(IReadOnlyList<Vec3> p)
        {
            if(p.Count==0) return default;
            double x=p[0].X,y=p[0].Y,z=p[0].Z,X=x,Y=y,Z=z;
            foreach(var a in p) { x=Math.Min(x,a.X);y=Math.Min(y,a.Y);z=Math.Min(z,a.Z);X=Math.Max(X,a.X);Y=Math.Max(Y,a.Y);Z=Math.Max(Z,a.Z); }
            return new Bounds3(new Vec3(x,y,z),new Vec3(X,Y,Z));
        }
        internal static int Dimension(IReadOnlyList<Vec3> p,double eps)
        {
            if(p.Count==0) return -1; Vec3 a=p[0],axis=default; double max=0;
            foreach(var v in p) if((v-a).LengthSquared>max) { axis=v-a; max=axis.LengthSquared; }
            if(max<=eps*eps) return 0;
            Vec3 n=default; max=0; axis=axis.Normalized;
            foreach(var v in p) { Vec3 c=Vec3.Cross(axis,v-a); if(c.LengthSquared>max) { n=c;max=c.LengthSquared; } }
            if(max<=eps*eps) return 1;
            n=n.Normalized; foreach(var v in p) if(Math.Abs(Vec3.Dot(n,v-a))>eps) return 3;
            return 2;
        }
        private static List<Vec3> Order(List<Vec3> input,Vec3 n,double eps)
        {
            var p=new List<Vec3>();foreach(var v in input) AddUnique(p,v,eps); if(p.Count<3) return p;
            Vec3 center=default;foreach(var v in p) center+=v;center/=p.Count;
            Vec3 x=Vec3.Cross(Math.Abs(n.Z)<.8?new Vec3(0,0,1):new Vec3(0,1,0),n).Normalized;
            Vec3 y=Vec3.Cross(n,x); Vec3 c=center;
            p.Sort((a,b)=>Math.Atan2(Vec3.Dot(a-c,y),Vec3.Dot(a-c,x)).CompareTo(Math.Atan2(Vec3.Dot(b-c,y),Vec3.Dot(b-c,x))));
            bool removed=true;while(removed&&p.Count>=3)
            { removed=false;for(int i=0;i<p.Count;i++) { Vec3 a=p[(i+p.Count-1)%p.Count],b=p[i],d=p[(i+1)%p.Count]; if(Vec3.Cross(b-a,d-b).Length<=eps*((b-a).Length+(d-b).Length)) { p.RemoveAt(i);removed=true;break; } } }
            return p;
        }
    }
}
