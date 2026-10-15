using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NDimensionalShogi.Showcase.Environment
{
    /// <summary>Original modeled profiles and vegetation, batched by material after construction.</summary>
    internal sealed class CourtyardMeshes
    {
        readonly Dictionary<Material,List<CombineInstance>> batches=new Dictionary<Material,List<CombineInstance>>();
        public void Add(Mesh mesh,Material material,Vector3 position,Quaternion rotation,Vector3 scale)
        {
            if(!batches.TryGetValue(material,out var list))batches.Add(material,list=new List<CombineInstance>());
            list.Add(new CombineInstance{mesh=mesh,transform=Matrix4x4.TRS(position,rotation,scale)});
        }
        public void Box(Material material,Vector3 position,Vector3 size,float bevel=.015f,Quaternion? rotation=null)
        {
            var mesh=BeveledBox(size,bevel);
            // Each physical cut comes from a different location in the same material.
            // This is fixed construction-time UV placement, independent of camera/time.
            var coordinates=mesh.uv;
            var offset=new Vector2(position.z*.73f+position.x*.31f,position.x*.23f+position.y*.47f);
            for(int i=0;i<coordinates.Length;i++)coordinates[i]+=offset;
            mesh.uv=coordinates;
            Add(mesh,material,position,rotation??Quaternion.identity,Vector3.one);
        }
        public void Finish(Transform parent)
        {
            foreach(var pair in batches)
            {
                var mesh=new Mesh{name=pair.Key.name+" courtyard batch",indexFormat=IndexFormat.UInt32};
                mesh.CombineMeshes(pair.Value.ToArray(),true,true);mesh.RecalculateBounds();
                var go=new GameObject(pair.Key.name){layer=8,isStatic=true};go.transform.SetParent(parent,false);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=pair.Key;
                renderer.shadowCastingMode=ShadowCastingMode.On;renderer.receiveShadows=true;
                renderer.lightProbeUsage=LightProbeUsage.BlendProbes;
            }
        }
        public static Mesh BeveledBox(Vector3 size,float bevel)
        {
            var v=new List<Vector3>();var n=new List<Vector3>();var uv=new List<Vector2>();var tri=new List<int>();
            Vector3 h=size*.5f,inner=new Vector3(Mathf.Max(0,h.x-bevel),Mathf.Max(0,h.y-bevel),Mathf.Max(0,h.z-bevel));
            var normal=new[]{Vector3.right,Vector3.left,Vector3.up,Vector3.down,Vector3.forward,Vector3.back};
            var tangent=new[]{Vector3.back,Vector3.forward,Vector3.right,Vector3.right,Vector3.right,Vector3.left};
            var up=new[]{Vector3.up,Vector3.up,Vector3.back,Vector3.forward,Vector3.up,Vector3.up};
            for(int f=0;f<6;f++)
            {
                int start=v.Count;
                float hu=Mathf.Abs(Vector3.Dot(tangent[f],h)),hv=Mathf.Abs(Vector3.Dot(up[f],h)),hn=Mathf.Abs(Vector3.Dot(normal[f],h));
                float[] xu={-hu,-Mathf.Max(0,hu-bevel),0,Mathf.Max(0,hu-bevel),hu};
                float[] yv={-hv,-Mathf.Max(0,hv-bevel),0,Mathf.Max(0,hv-bevel),hv};
                for(int y=0;y<5;y++)for(int x=0;x<5;x++)
                {
                    var p=normal[f]*hn+tangent[f]*xu[x]+up[f]*yv[y];
                    var q=new Vector3(Mathf.Clamp(p.x,-inner.x,inner.x),Mathf.Clamp(p.y,-inner.y,inner.y),Mathf.Clamp(p.z,-inner.z,inner.z));
                    Vector3 nn=(p-q).sqrMagnitude<1e-10f?normal[f]:(p-q).normalized;
                    v.Add(q+nn*bevel);n.Add(nn);uv.Add(hu>=hv?new Vector2(yv[y]+hv,xu[x]+hu):new Vector2(xu[x]+hu,yv[y]+hv));
                }
                for(int y=0;y<4;y++)for(int x=0;x<4;x++)
                { int a=start+y*5+x;tri.Add(a);tri.Add(a+1);tri.Add(a+5);tri.Add(a+1);tri.Add(a+6);tri.Add(a+5); }
            }
            var mesh=new Mesh{name="Modeled beveled cuboid"};mesh.SetVertices(v);mesh.SetNormals(n);mesh.SetUVs(0,uv);mesh.SetTriangles(tri,0);mesh.RecalculateTangents();return mesh;
        }
        public static Mesh Lathe(Vector2[] profile,int segments=48)
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var tri=new List<int>();
            for(int y=0;y<profile.Length;y++)for(int x=0;x<=segments;x++)
            {
                float a=x/(float)segments*Mathf.PI*2;
                v.Add(new Vector3(Mathf.Sin(a)*profile[y].x,profile[y].y,Mathf.Cos(a)*profile[y].x));
                uv.Add(new Vector2(x/(float)segments*profile[y].x*6.283f,profile[y].y));
            }
            for(int y=0;y<profile.Length-1;y++)for(int x=0;x<segments;x++)
            {int a=y*(segments+1)+x,b=a+segments+1;tri.Add(a);tri.Add(a+1);tri.Add(b);tri.Add(a+1);tri.Add(b+1);tri.Add(b);}
            var mesh=new Mesh{name="Turned stone/wood profile"};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tri,0);mesh.RecalculateNormals();
            var normals=mesh.normals;
            for(int y=0;y<profile.Length;y++)
            {int a=y*(segments+1),b=a+segments;var smooth=(normals[a]+normals[b]).normalized;normals[a]=smooth;normals[b]=smooth;}
            mesh.normals=normals;mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
        }
        public static Mesh Leaf()
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var tri=new List<int>();
            for(int i=0;i<9;i++)
            {
                float t=i/8f,w=Mathf.Sin(t*Mathf.PI)*.32f,z=Mathf.Sin(t*Mathf.PI)*.12f;
                v.Add(new Vector3(-w,t,z));v.Add(new Vector3(0,t,z+Mathf.Sin(t*Mathf.PI)*.055f));v.Add(new Vector3(w,t,z));
                uv.Add(new Vector2(0,t));uv.Add(new Vector2(.5f,t));uv.Add(new Vector2(1,t));
            }
            for(int i=0;i<8;i++)for(int side=0;side<2;side++)
            {int a=i*3+side;tri.Add(a);tri.Add(a+3);tri.Add(a+1);tri.Add(a+1);tri.Add(a+3);tri.Add(a+4);}
            var mesh=new Mesh{name="Curved botanical leaf"};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tri,0);mesh.RecalculateNormals();mesh.RecalculateTangents();return mesh;
        }
        public void Stem(Material material,Vector3 a,Vector3 b,float radius)
        {
            var dir=b-a;
            Add(Lathe(new[]{new Vector2(radius,0),new Vector2(radius*.6f,dir.magnitude)},8),material,a,Quaternion.FromToRotation(Vector3.up,dir),Vector3.one);
        }
    }
}
