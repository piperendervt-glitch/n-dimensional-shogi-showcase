using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using NDShogi.Showcase.Core;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;
using NDimensionalShogi.Showcase.Environment;

namespace Showcase.Unity.Settings
{
    internal sealed class Cfg2CapturedPiece
    {public Side Owner;public string DefinitionId;public int Count;}

    // Only presentation data enters this layer. Captured ownership is the capturer,
    // and the two wooden trays never swap owners when the camera turns.
    internal sealed class Cfg2Visuals : IDisposable
    {
        readonly EnvironmentRig rig;
        readonly Dictionary<string,Material> labels=new Dictionary<string,Material>();
        readonly Dictionary<string,SectionView> boardPieces=new Dictionary<string,SectionView>();
        readonly Dictionary<string,Q4> boardCells=new Dictionary<string,Q4>();
        readonly Dictionary<string,SectionView> capturedPieces=new Dictionary<string,SectionView>();
        readonly Dictionary<string,Text> counts=new Dictionary<string,Text>();
        readonly Dictionary<string,RectTransform> countRoots=new Dictionary<string,RectTransform>();
        readonly Dictionary<Side,int> totals=new Dictionary<Side,int>{{Side.A,0},{Side.B,0}};
        readonly ConvexShape shape=ShapeFactory.CreatePiece(ShapeVariant.C);
        readonly Mesh boardCandidateMesh;
        readonly Material boardCandidateMaterial;
        readonly List<Vector3> candidateVertices=new List<Vector3>();
        readonly List<Color> candidateColors=new List<Color>();
        readonly List<int> candidateIndices=new List<int>();
        readonly HashSet<Q4> boardCandidateCells=new HashSet<Q4>();
        public readonly GameObject Root;
        public int VisibleBoardPieces {get;private set;}
        public int VisibleBoardCandidates=>boardCandidateCells.Count;
        public IReadOnlyCollection<Q4> BoardCandidateCells=>boardCandidateCells;
        public Vector3 TrayPosition(Side side)=>side==Side.A?new Vector3(.35f,.865f,-.25f):new Vector3(-.35f,.865f,.25f);
        public int CountFor(Side owner)=>totals[owner];
        public string CapturedLabel(Side owner,string definition)=>counts.TryGetValue(owner+":"+definition,out var label)?label.text:"";
        public Cfg2Visuals(EnvironmentRig environment)
        {
            rig=environment;Root=new GameObject("CFG2 physical pieces and captured trays");
            foreach(string name in new[]{"Pawn","Lance","Knight","Silver","Gold","Bishop","Rook","KingA","KingB"})
            {
                var mat=new Material(rig.Materials.BrightWood){name="CFG2 wood "+name};var texture=Resources.Load<Texture2D>("PieceTextures/"+name);
                if(texture==null)throw new InvalidOperationException("Missing existing wood texture "+name);
                mat.SetTexture("_BaseColorMap",texture);mat.SetColor("_BaseColor",Color.white);EnvironmentMaterials.ApplyPieceSurface(mat,name);labels.Add(name,mat);
            }
            foreach(Side owner in Enum.GetValues(typeof(Side)))
            {
                var pos=TrayPosition(owner);Box("Captured tray "+owner,pos-new Vector3(0,.012f,0),new Vector3(.19f,.024f,.145f),rig.Materials.DarkWood);
                Box("Captured tray surface "+owner,pos+new Vector3(0,.001f,0),new Vector3(.18f,.004f,.135f),rig.Materials.BrightWood);
                foreach(int sign in new[]{-1,1})
                {
                    Box("Captured tray rim "+owner,pos+new Vector3(sign*.094f,.005f,0),new Vector3(.006f,.012f,.15f),rig.Materials.DarkWood);
                    Box("Captured tray rim "+owner,pos+new Vector3(0,.005f,sign*.071f),new Vector3(.19f,.012f,.006f),rig.Materials.DarkWood);
                }
            }
            var shader=Resources.Load<Shader>("CfgCellLines");if(shader==null)throw new InvalidOperationException("Existing cell overlay shader missing.");
            boardCandidateMaterial=new Material(shader){name="CFG2 physical board candidates",renderQueue=2998};boardCandidateMesh=new Mesh{name="CFG2 physical board candidate tiles"};boardCandidateMesh.MarkDynamic();
            var hints=new GameObject("CFG2 fixed V=0 W=0 candidate tiles");hints.transform.SetParent(Root.transform,false);hints.transform.position=SectionView.Anchor+Vector3.up*.0008f;
            hints.AddComponent<MeshFilter>().sharedMesh=boardCandidateMesh;var renderer=hints.AddComponent<MeshRenderer>();renderer.sharedMaterial=boardCandidateMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        }
        void Box(string name,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(Root.transform,false);go.transform.position=position;go.transform.localScale=scale;
            go.GetComponent<MeshRenderer>().sharedMaterial=material;UnityEngine.Object.Destroy(go.GetComponent<Collider>());
        }
        public void RefreshBoard(IEnumerable<CfgVisualPiece> source)
        {
            var live=new HashSet<string>();VisibleBoardPieces=0;boardCells.Clear();var frame=SliceAxes.Reference(ReferenceView.XYZ);
            foreach(var item in source??Array.Empty<CfgVisualPiece>())
            {
                live.Add(item.Id);boardCells[item.Id]=item.Position;string texture=CfgPreviewRenderer.TextureFor(item.DefinitionId,item.Side);
                if(!boardPieces.TryGetValue(item.Id,out var view))
                {
                    view=new SectionView("CFG2 physical "+item.Id,shape,labels[texture],rig.Materials.BrightWood);view.Object.transform.SetParent(Root.transform,true);boardPieces.Add(item.Id,view);
                }
                view.Renderer.sharedMaterials=new[]{rig.Materials.BrightWood,labels[texture]};
                // Fixed W=0 is an actual XYZ section, never a many-to-one projection.
                view.UpdateSection(CfgPreviewRenderer.V(item.Position),item.Side==Side.A?1:-1,frame,0,4.5);
                if(view.Last!=null&&view.Last.Status==SectionStatus.Volume3D)VisibleBoardPieces++;
            }
            foreach(var id in boardPieces.Keys.Where(id=>!live.Contains(id)).ToArray()){boardPieces[id].Dispose();boardPieces.Remove(id);}
        }
        public bool PickBoard(Ray ray,out Q4 cell,out float distance,bool piecesOnly=false)
        {
            cell=default;distance=float.PositiveInfinity;bool found=false;
            foreach(var entry in boardPieces)
            {
                var section=entry.Value.Last;if(section==null||section.Status!=SectionStatus.Volume3D)continue;
                for(int i=0;i<section.Indices.Length;i+=3)
                {
                    Vector3 At(int n)=>entry.Value.Object.transform.position+SectionView.MapVector(section.Vertices[section.Indices[i+n]].Position)*SectionView.Cell;
                    if(CfgPreviewRenderer.RayTriangle(ray,At(0),At(1),At(2),out float hit)&&hit<distance){distance=hit;cell=boardCells[entry.Key];found=true;}
                }
            }
            if(found||piecesOnly)return found;
            var plane=new Plane(Vector3.up,SectionView.Anchor);if(!plane.Raycast(ray,out float floor)||floor<=0)return false;var point=ray.GetPoint(floor)-SectionView.Anchor;
            var q=new Q4(Mathf.RoundToInt(point.z/SectionView.Cell),Mathf.RoundToInt(point.x/SectionView.Cell),0,0);if(!q.InBounds)return false;
            cell=q;distance=floor;return true;
        }
        public void RefreshBoardCandidates(IEnumerable<Q4> p,Q4? selected,bool visible)
        {
            boardCandidateCells.Clear();candidateVertices.Clear();candidateColors.Clear();candidateIndices.Clear();
            if(visible)foreach(var q in p??Array.Empty<Q4>())if(q.InBounds&&q.V==0&&q.W==0)boardCandidateCells.Add(q);
            foreach(var q in boardCandidateCells)if(!selected.HasValue||selected.Value!=q)CandidateTile(q,new Color(.12f,.43f,1,.22f),new Color(.24f,.57f,1,.8f));
            if(selected.HasValue&&selected.Value.InBounds&&selected.Value.V==0&&selected.Value.W==0)CandidateTile(selected.Value,new Color(1,.74f,.18f,.26f),new Color(1,.75f,.2f,.95f));
            boardCandidateMesh.Clear();boardCandidateMesh.SetVertices(candidateVertices);boardCandidateMesh.SetColors(candidateColors);boardCandidateMesh.SetTriangles(candidateIndices,0);boardCandidateMesh.RecalculateBounds();
        }
        void CandidateTile(Q4 q,Color fill,Color border)
        {
            float x=q.U*SectionView.Cell,z=q.F*SectionView.Cell,half=SectionView.Cell*.465f,width=.001f;
            CandidateQuad(x-half,z-half,half*2,half*2,fill);
            CandidateQuad(x-half,z-half,half*2,width,border);CandidateQuad(x-half,z+half-width,half*2,width,border);
            CandidateQuad(x-half,z-half,width,half*2,border);CandidateQuad(x+half-width,z-half,width,half*2,border);
        }
        void CandidateQuad(float x,float z,float width,float height,Color color)
        {
            int n=candidateVertices.Count;candidateVertices.Add(new Vector3(x,0,z));candidateVertices.Add(new Vector3(x+width,0,z));candidateVertices.Add(new Vector3(x+width,0,z+height));candidateVertices.Add(new Vector3(x,0,z+height));
            for(int i=0;i<4;i++)candidateColors.Add(color);candidateIndices.Add(n);candidateIndices.Add(n+1);candidateIndices.Add(n+2);candidateIndices.Add(n);candidateIndices.Add(n+2);candidateIndices.Add(n+3);
        }
        public void RefreshCaptured(IEnumerable<Cfg2CapturedPiece> source)
        {
            totals[Side.A]=totals[Side.B]=0;var live=new HashSet<string>();var grouped=(source??Array.Empty<Cfg2CapturedPiece>()).GroupBy(x=>x.Owner+":"+x.DefinitionId).Select(g=>new Cfg2CapturedPiece{Owner=g.First().Owner,DefinitionId=g.First().DefinitionId,Count=g.Sum(x=>x.Count)}).ToArray();
            foreach(Side owner in Enum.GetValues(typeof(Side)))
            {
                int slot=0;
                foreach(var item in grouped.Where(x=>x.Owner==owner).OrderBy(x=>x.DefinitionId,StringComparer.Ordinal))
                {
                    if(item.Count<0)throw new ArgumentOutOfRangeException(nameof(source),"A captured count cannot be negative.");if(item.Count==0)continue;
                    string texture=CfgPreviewRenderer.TextureFor(item.DefinitionId,item.Owner);if(texture.StartsWith("King",StringComparison.Ordinal))throw new ArgumentException("Captured kings belong to the result, not a tray.",nameof(source));
                    string key=item.Owner+":"+item.DefinitionId;live.Add(key);totals[owner]+=item.Count;
                    if(!capturedPieces.TryGetValue(key,out var view))
                    {
                        view=new SectionView("CFG2 captured "+key,shape,labels[texture],rig.Materials.BrightWood);view.Object.transform.SetParent(Root.transform,true);view.UpdateSection(default,owner==Side.A?1:-1,0,0);capturedPieces.Add(key,view);
                        var textObject=new GameObject("Captured count "+key,typeof(RectTransform),typeof(Canvas));textObject.transform.SetParent(Root.transform,false);
                        var countRoot=(RectTransform)textObject.transform;countRoot.sizeDelta=new Vector2(100,48);countRoot.localScale=Vector3.one*.00025f;
                        var canvas=textObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=rig.Camera;canvas.sortingOrder=10;
                        var plate=CfgUi.Panel(countRoot,"Captured count plate",0,0,100,48,new Color(.025f,.035f,.05f,.88f));plate.GetComponent<Image>().raycastTarget=false;
                        var text=CfgUi.Label(plate,"",4,2,92,44,34,Color.white);text.alignment=TextAnchor.MiddleCenter;text.supportRichText=false;
                        textObject.AddComponent<Cfg2Billboard>().Camera=rig.Camera;counts.Add(key,text);countRoots.Add(key,countRoot);
                    }
                    float sign=owner==Side.A?1:-1;var offset=new Vector3(((slot%4)-1.5f)*.041f,.005f,((slot/4)-.5f)*.062f);offset.x*=sign;offset.z*=sign;
                    view.Object.transform.position=TrayPosition(owner)+offset;countRoots[key].position=view.Object.transform.position+new Vector3(0,.02f,-.025f*sign);counts[key].text="×"+item.Count;slot++;
                }
            }
            foreach(var key in capturedPieces.Keys.Where(key=>!live.Contains(key)).ToArray()){capturedPieces[key].Dispose();capturedPieces.Remove(key);UnityEngine.Object.Destroy(countRoots[key].gameObject);counts.Remove(key);countRoots.Remove(key);}
        }
        public void Dispose()
        {
            foreach(var p in boardPieces.Values)p.Dispose();foreach(var p in capturedPieces.Values)p.Dispose();foreach(var mat in labels.Values)UnityEngine.Object.Destroy(mat);UnityEngine.Object.Destroy(boardCandidateMesh);UnityEngine.Object.Destroy(boardCandidateMaterial);UnityEngine.Object.Destroy(Root);
        }
    }
    internal sealed class Cfg2Billboard : MonoBehaviour
    {
        public Camera Camera;
        void LateUpdate(){if(Camera!=null)transform.rotation=Camera.transform.rotation;}
    }
}
