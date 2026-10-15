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
                var edges=new Dictionary<string,Tuple<Vec3,Vec3,Vec3,bool>>();
                for(int i=0;i<result.Indices.Length;i+=3)for(int j=0;j<3;j++)
                {
                    var a=result.Vertices[result.Indices[i+j]];var b=result.Vertices[result.Indices[i+(j+1)%3]];
                    string ka=Key(a.Position),kb=Key(b.Position),key=string.CompareOrdinal(ka,kb)<0?ka+"|"+kb:kb+"|"+ka;
                    if(edges.TryGetValue(key,out var old))edges[key]=Tuple.Create(old.Item1,old.Item2,old.Item3,Math.Abs(Vec3.Dot(old.Item3,a.Normal))<.999||old.Item4);
                    else edges.Add(key,Tuple.Create(a.Position,b.Position,a.Normal,false));
                }
                foreach(var edge in edges.Values)if(edge.Item4)
                {indices.Add(lines.Count);lines.Add(SectionView.MapVector(edge.Item1)*SectionView.Cell);indices.Add(lines.Count);lines.Add(SectionView.MapVector(edge.Item2)*SectionView.Cell);}
            }
            mesh.SetVertices(lines);mesh.SetIndices(indices,MeshTopology.Lines,0);mesh.RecalculateBounds();
        }
        static string Key(Vec3 p)=>Math.Round(p.X,7)+","+Math.Round(p.Y,7)+","+Math.Round(p.Z,7);
        public void Dispose(){UnityEngine.Object.Destroy(obj);UnityEngine.Object.Destroy(mesh);UnityEngine.Object.Destroy(material);}
    }
}
