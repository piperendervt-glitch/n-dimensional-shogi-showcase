using System;
using System.Collections.Generic;

namespace Showcase.Geometry
{
    public sealed class OracleResult
    {
        public SectionStatus Status { get; }
        public Vec3[] Vertices { get; }
        public int Dimension { get; }
        internal OracleResult(SectionStatus status,Vec3[] vertices,int dimension) { Status=status;Vertices=vertices;Dimension=dimension; }
    }
    // Independent construction: enumerate intersections of three planes and test
    // all source inequalities. No clipping polygons, seed AABB, or cap code is used.
    public static class TriplePlaneOracle
    {
        public static OracleResult Build(InstanceN instance,SliceFrame frame,double tolerance=1e-8)
        {
            var normals=new List<Vec3>();var offsets=new List<double>();
            VecN origin=instance.WorldToLocal(frame.Center);
            VecN bx=instance.WorldToLocalDirection(frame.AxisX),by=instance.WorldToLocalDirection(frame.AxisY),bz=instance.WorldToLocalDirection(frame.AxisZ);
            foreach(var p in instance.Shape.Planes)
            {
                Vec3 n=new Vec3(VecN.Dot(p.Normal,bx),VecN.Dot(p.Normal,by),VecN.Dot(p.Normal,bz));
                double b=p.Offset-VecN.Dot(p.Normal,origin),len=n.Length;
                if(len<1e-12) { if(b < -tolerance) return new OracleResult(SectionStatus.Empty,Array.Empty<Vec3>(),-1); continue; }
                normals.Add(n/len); offsets.Add(b/len);
            }
            var vertices=new List<Vec3>(); int count=normals.Count;
            for(int i=0;i<count;i++)for(int j=i+1;j<count;j++)for(int k=j+1;k<count;k++)
            {
                Vec3 c=Vec3.Cross(normals[j],normals[k]); double det=Vec3.Dot(normals[i],c);
                if(Math.Abs(det)<1e-11) continue;
                Vec3 v=(c*offsets[i]+Vec3.Cross(normals[k],normals[i])*offsets[j]+Vec3.Cross(normals[i],normals[j])*offsets[k])/det;
                bool feasible=v.IsFinite;
                for(int l=0;l<count&&feasible;l++) if(Vec3.Dot(normals[l],v)>offsets[l]+tolerance) feasible=false;
                if(!feasible) continue;
                bool duplicate=false;foreach(var existing in vertices)if((v-existing).Length<tolerance*4) { duplicate=true;break; }
                if(!duplicate) vertices.Add(v);
            }
            int rank=Rank(vertices,tolerance*8);
            return new OracleResult(rank<0?SectionStatus.Empty:rank<3?SectionStatus.ContactLowerDimensional:SectionStatus.Volume3D,vertices.ToArray(),rank);
        }
        private static int Rank(List<Vec3> points,double eps)
        {
            if(points.Count==0)return -1;
            var basis=new List<Vec3>();Vec3 origin=points[0];
            foreach(var p in points)
            { Vec3 d=p-origin;foreach(var b in basis)d-=b*Vec3.Dot(d,b);if(d.Length>eps)basis.Add(d.Normalized);if(basis.Count==3)break; }
            return basis.Count;
        }
    }
}
