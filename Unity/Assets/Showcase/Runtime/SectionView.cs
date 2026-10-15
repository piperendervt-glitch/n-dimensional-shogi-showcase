using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Showcase.Geometry;

namespace Showcase.Unity
{
    // One Mesh is authoritative for colour, depth, normals, shadows and reflections.
    // Only the mathematical section result controls whether triangles exist.
    public sealed class SectionView : IDisposable
    {
        public const float Cell = .05f;
        public static readonly Vector3 Anchor = new Vector3(0,.86f,0);
        readonly ConvexShape shape;
        readonly bool board, symmetricMaterial;
        readonly float size;
        readonly Mesh mesh;
        readonly List<Vector3> positions = new List<Vector3>(512), normals = new List<Vector3>(512);
        readonly List<Vector2> uv = new List<Vector2>(512);
        readonly List<Vector4> local4 = new List<Vector4>(512), provenance = new List<Vector4>(512);
        readonly List<int> top = new List<int>(1024), side = new List<int>(1024);
        readonly List<Vector4> tangents = new List<Vector4>(512);
        public readonly GameObject Object;
        public readonly MeshRenderer Renderer;
        public SectionResult Last { get; private set; }
        public long SectionVersion { get; private set; }
        public string Id { get; }
        Vec4 previous;
        double previousAngle = double.NaN;
        int previousForward;

        public SectionView(string id, ConvexShape shape, Material topMaterial, Material sideMaterial, bool isBoard=false, float scale=1, bool useSymmetricMaterial=true)
        {
            Id=id;this.shape=shape;board=isBoard;size=scale;symmetricMaterial=useSymmetricMaterial;
            Object=new GameObject(id);Object.transform.position=Anchor;
            mesh=new Mesh{name=id+" section"};mesh.MarkDynamic();
            Object.AddComponent<MeshFilter>().sharedMesh=mesh;
            Renderer=Object.AddComponent<MeshRenderer>();Renderer.sharedMaterials=new[]{sideMaterial,topMaterial};
            Renderer.shadowCastingMode=ShadowCastingMode.On;Renderer.receiveShadows=true;
            Renderer.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;
        }
        public void UpdateSection(Vec4 q, int forward, double angle, long version)
        {
            if(PrepareSection(q,forward,angle,version))ApplySection(ComputeSection(q,forward,angle));
        }
        // Cache and Unity mesh mutation stay on the main thread. Only the immutable
        // pure-C# intersection is dispatched to workers by the controller.
        public bool PrepareSection(Vec4 q,int forward,double angle,long version)
        {
            SectionVersion=version;
            if(previousAngle==angle&&previousForward==forward&&Vec4.Dot(q-previous,q-previous)<1e-22)return false;
            previous=q;previousAngle=angle;previousForward=forward;SectionVersion=version;
            return true;
        }
        public SectionResult ComputeSection(Vec4 q,int forward,double angle)
            =>SectionBuilder.Build(new Instance4D(shape,q,forward,Id),SliceFrame.AtAngle(angle));
        public void ApplySection(SectionResult result)
        {
            Last=result;
            if(Last.Status==SectionStatus.CapacityExceeded||Last.Status==SectionStatus.NumericallyUncertain)
                throw new InvalidOperationException(Id+": "+Last.Status+" "+Last.Diagnostic);
            mesh.Clear();
            if(Last.Status!=SectionStatus.Volume3D)return;
            positions.Clear();normals.Clear();uv.Clear();local4.Clear();provenance.Clear();top.Clear();side.Clear();
            foreach(var v in Last.Vertices)
            {
                positions.Add(MapVector(v.Position)*Cell);normals.Add(MapVector(v.Normal));
                var r=v.LocalPosition;
                // Affine material field defined once in local 4D; no dependence on angle/camera.
                double lateral=symmetricMaterial?r.Y+r.W:r.Y;
                if(board)uv.Add(v.Surface==SurfaceKind.Top?new Vector2((float)(lateral/9+.5),(float)(r.X/9+.5)):new Vector2((float)(r.X*.25),(float)(r.Z*.8+lateral*.1)));
                else uv.Add(v.Surface==SurfaceKind.Top?new Vector2((float)(lateral/(.58*size)+.5),(float)(r.X/(.72*size)+.5)):new Vector2((float)(r.X/size+.5),(float)((r.Z+lateral*.15)/size)));
                local4.Add(new Vector4((float)r.X,(float)r.Y,(float)r.Z,(float)r.W));
                provenance.Add(new Vector4(v.FaceId,(int)v.Surface,0,0));
            }
            for(int i=0;i<Last.Indices.Length;i+=3)
            {
                var list=Last.Vertices[Last.Indices[i]].Surface==SurfaceKind.Top?top:side;
                list.Add(Last.Indices[i]);list.Add(Last.Indices[i+1]);list.Add(Last.Indices[i+2]);
            }
            mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetUVs(2,local4);mesh.SetUVs(3,provenance);
            mesh.subMeshCount=2;mesh.SetTriangles(side,0,false);mesh.SetTriangles(top,1,false);mesh.RecalculateBounds();mesh.RecalculateTangents();
            // Rank-deficient fixed UV fields (e.g. A at 90 degrees) have no material tangent.
            // Use a finite geometric tangent, never a remapped glyph or camera-dependent UV.
            mesh.GetTangents(tangents);
            for(int i=0;i<tangents.Count;i++)
            {
                var t=tangents[i];var v=new Vector3(t.x,t.y,t.z);
                if(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsNaN(t.w)||float.IsInfinity(t.w)||float.IsInfinity(v.sqrMagnitude)||v.sqrMagnitude<.1f||Mathf.Abs(Vector3.Dot(v,normals[i]))>.01f)
                {var n=normals[i];v=Vector3.Cross(n,Mathf.Abs(n.y)<.9f?Vector3.up:Vector3.right).normalized;tangents[i]=new Vector4(v.x,v.y,v.z,1);}
            }
            mesh.SetTangents(tangents);
        }
        public static Vector3 MapVector(Vec3 p)=>new Vector3((float)p.Y,(float)p.Z,(float)p.X);
        public static Vector3 MapPoint(Vec4 p,double angle)=>Anchor+MapVector(SliceFrame.AtAngle(angle).Project(p))*Cell;
        public void Dispose(){UnityEngine.Object.Destroy(Object);UnityEngine.Object.Destroy(mesh);}
    }
}
