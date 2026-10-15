using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Showcase.Geometry;

namespace Showcase.Unity
{
    public sealed class GeometryGuides : IDisposable
    {
        readonly GameObject obj;
        readonly Mesh mesh;
        readonly Material material;
        readonly ConvexShape cell=ShapeFactory.CreateBox(new Vec4(-.5,-.5,-.5,-.5),new Vec4(.5,.5,.5,.5),0,"legal-cell");
        readonly List<Vector3> lines=new List<Vector3>();
        readonly List<int> indices=new List<int>();
        // Edge keys are quantised coordinates (1e-7), the same rounding the previous
        // string keys used, without per-edge string and tuple allocations every frame.
        readonly struct PointKey : IEquatable<PointKey>
        {
            readonly double x,y,z;
            public PointKey(Vec3 p){x=Math.Round(p.X,7);y=Math.Round(p.Y,7);z=Math.Round(p.Z,7);}
            public bool Equals(PointKey o)=>x==o.x&&y==o.y&&z==o.z;
            public override bool Equals(object o)=>o is PointKey k&&Equals(k);
            public override int GetHashCode()=>(x.GetHashCode()*397^y.GetHashCode())*397^z.GetHashCode();
            public int CompareTo(PointKey o){int c=x.CompareTo(o.x);if(c!=0)return c;c=y.CompareTo(o.y);return c!=0?c:z.CompareTo(o.z);}
        }
        readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            readonly PointKey a,b;
            public EdgeKey(PointKey p,PointKey q){if(p.CompareTo(q)<0){a=p;b=q;}else{a=q;b=p;}}
            public bool Equals(EdgeKey o)=>a.Equals(o.a)&&b.Equals(o.b);
            public override bool Equals(object o)=>o is EdgeKey k&&Equals(k);
            public override int GetHashCode()=>a.GetHashCode()*397^b.GetHashCode();
        }
        struct Edge {public Vec3 A,B,Normal;public bool Crease;}
        readonly Dictionary<EdgeKey,Edge> edges=new Dictionary<EdgeKey,Edge>(64);
        public bool Visible {set=>obj.SetActive(value);}
        public GeometryGuides()
        {
            obj=new GameObject("Auxiliary / actual 4D cell sections"){layer=9};obj.transform.position=SectionView.Anchor;
            mesh=new Mesh{name="Legal cell section edges"};mesh.MarkDynamic();obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            material=new Material(Shader.Find("HDRP/Unlit")){name="Auxiliary section edges"};
            material.SetColor("_UnlitColor",new Color(1,.65f,.2f));material.SetFloat("_ExposureWeight",0);
            var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        }
        public void Update(IReadOnlyList<Vec4> cells,double angle,bool purple)
        {
            lines.Clear();indices.Clear();mesh.Clear();
            material.SetColor("_UnlitColor",purple?new Color(.58f,.38f,1):new Color(.92f,.72f,.35f));
            foreach(var q in cells)
            {
                var result=SectionBuilder.Build(new Instance4D(cell,q),SliceFrame.AtAngle(angle));
                if(result.Status!=SectionStatus.Volume3D)continue;
                edges.Clear();
                for(int i=0;i<result.Indices.Length;i+=3)for(int j=0;j<3;j++)
                {
                    var a=result.Vertices[result.Indices[i+j]];var b=result.Vertices[result.Indices[i+(j+1)%3]];
                    var key=new EdgeKey(new PointKey(a.Position),new PointKey(b.Position));
                    if(edges.TryGetValue(key,out var old)){old.Crease=Math.Abs(Vec3.Dot(old.Normal,a.Normal))<.999||old.Crease;edges[key]=old;}
                    else edges.Add(key,new Edge{A=a.Position,B=b.Position,Normal=a.Normal});
                }
                foreach(var edge in edges.Values)if(edge.Crease)
                {indices.Add(lines.Count);lines.Add(SectionView.MapVector(edge.A)*SectionView.Cell);indices.Add(lines.Count);lines.Add(SectionView.MapVector(edge.B)*SectionView.Cell);}
            }
            mesh.SetVertices(lines);mesh.SetIndices(indices,MeshTopology.Lines,0);mesh.RecalculateBounds();
        }
        public void Dispose(){UnityEngine.Object.Destroy(obj);UnityEngine.Object.Destroy(mesh);UnityEngine.Object.Destroy(material);}
    }
}
