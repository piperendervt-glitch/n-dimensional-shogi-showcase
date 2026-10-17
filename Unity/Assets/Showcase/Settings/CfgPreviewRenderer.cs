using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using NDShogi.Showcase.Core;
using Showcase.Geometry;
using Showcase.Geometry.Interaction;
using NDimensionalShogi.Showcase.Environment;

namespace Showcase.Unity.Settings
{
    internal sealed class CfgVisualPiece
    { public string Id,DefinitionId;public QN Position;public Side Side;public VecN? Glide; }

    // Presentation only: all candidate sets arrive from the common pure-C# query.
    internal sealed class CfgPreviewRenderer : IDisposable
    {
        public static readonly Vector3 AirAnchor=new Vector3(0,1.1f,0);
        readonly EnvironmentRig rig;
        // One unit cell per N; the 4D cell is the original one.
        readonly Dictionary<int,ConvexShape> cellShapes=new Dictionary<int,ConvexShape>();
        ConvexShape Cell(int dimensions)
        {
            if(!cellShapes.TryGetValue(dimensions,out var shape))
            {
                var half=new double[dimensions];for(int i=0;i<dimensions;i++)half[i]=.5;
                shape=ShapeFactory.CreateBox(new VecN(half)*-1,new VecN(half),0,"cfg1-logical-cell/1");cellShapes.Add(dimensions,shape);
            }
            return shape;
        }
        readonly Dictionary<string,SectionView> pieces=new Dictionary<string,SectionView>();
        // N of the piece solids in use; a match of another N replaces them.
        int pieceDimensions=4;
        readonly Dictionary<string,Material> labels=new Dictionary<string,Material>();
        readonly List<CellMesh> cells=new List<CellMesh>();
        readonly Dictionary<string,QN> pieceCells=new Dictionary<string,QN>();
        readonly HashSet<QN> candidateCells=new HashSet<QN>();
        readonly Mesh lineMesh;
        readonly Material lineMaterial;
        readonly GameObject lineObject;
        readonly Mesh fillMesh;
        readonly Material fillMaterial;
        readonly GameObject fillObject;
        readonly SectionView board;
        readonly List<Vector3> lines=new List<Vector3>();
        readonly List<Color> colors=new List<Color>();
        readonly List<int> indices=new List<int>();
        readonly List<Vector3> fillVertices=new List<Vector3>();
        readonly List<Color> fillColors=new List<Color>();
        readonly List<int> fillIndices=new List<int>();
        // Quantisation is only an identity key. Rendered endpoints remain the exact
        // section vertices; it never rounds or changes the source geometry.
        readonly struct PointKey : IEquatable<PointKey>
        {
            readonly double x,y,z;
            public PointKey(Vec3 p){x=Math.Round(p.X,7);y=Math.Round(p.Y,7);z=Math.Round(p.Z,7);}
            public bool Equals(PointKey other)=>x==other.x&&y==other.y&&z==other.z;
            public override bool Equals(object other)=>other is PointKey key&&Equals(key);
            public override int GetHashCode()=>(x.GetHashCode()*397^y.GetHashCode())*397^z.GetHashCode();
            public int CompareTo(PointKey other){int c=x.CompareTo(other.x);if(c!=0)return c;c=y.CompareTo(other.y);return c!=0?c:z.CompareTo(other.z);}
        }
        readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            readonly PointKey a,b;
            public EdgeKey(PointKey p,PointKey q){if(p.CompareTo(q)<0){a=p;b=q;}else{a=q;b=p;}}
            public bool Equals(EdgeKey other)=>a.Equals(other.a)&&b.Equals(other.b);
            public override bool Equals(object other)=>other is EdgeKey key&&Equals(key);
            public override int GetHashCode()=>a.GetHashCode()*397^b.GetHashCode();
        }
        struct CellEdge {public Vec3 A,B,Normal;public bool Crease;}
        struct DisplayEdge {public Vec3 A,B;public Color Color;public int Priority;}
        readonly Dictionary<EdgeKey,CellEdge> cellEdges=new Dictionary<EdgeKey,CellEdge>(64);
        readonly Dictionary<EdgeKey,DisplayEdge> displayEdges=new Dictionary<EdgeKey,DisplayEdge>(2048);
        public int VisibleG {get;private set;} public int VisibleP {get;private set;}
        public int VisibleAttacks {get;private set;} public int VisiblePieces {get;private set;}
        public long ViewRevision {get;private set;}
        public int VisibleCells=>cells.Count;
        public string Diagnostic {get;private set;}="";
        public bool Grid=true,Candidates=true;
        public bool Attacks=true,Fill=false,ShowPieces=true;
        public double HalfWindow=FiniteSliceWindow.DefaultHalfExtent;
        public Vector3 Anchor=AirAnchor;
        // CFG1 keeps its original zero embedding. The pivot entry supplies the
        // same immutable display state to cells, pieces and picking.
        VecN embedding;
        float displayCell=SectionView.Cell;
        public IReadOnlyDictionary<string,SectionView> PieceSections=>pieces;
        public void SetDisplayState(PivotViewState state)
        {
            if(state==null)throw new ArgumentNullException(nameof(state));
            embedding=state.Embedding;Anchor=new Vector3((float)state.Anchor.X,(float)state.Anchor.Y,(float)state.Anchor.Z);
            HalfWindow=state.HalfWindow;displayCell=(float)state.CellScale;
        }
        public bool TryGetCellSection(QN id,out SectionResult section)
        {foreach(var entry in cells)if(entry.Id==id){section=entry.Section;return true;}section=null;return false;}
        public double LastBuildMilliseconds {get;private set;}
        struct CellMesh {public QN Id;public SectionResult Section;}
        public CfgPreviewRenderer(EnvironmentRig environment)
        {
            rig=environment;
            foreach(string name in new[]{"Pawn","Lance","Knight","Silver","Gold","Bishop","Rook","KingA","KingB","Board"})
            {
                var m=new Material(rig.Materials.BrightWood){name="CFG1 wood "+name};var texture=Resources.Load<Texture2D>("PieceTextures/"+name);
                if(texture==null)throw new InvalidOperationException("Missing existing piece texture "+name);
                m.SetTexture("_BaseColorMap",texture);m.SetColor("_BaseColor",Color.white);EnvironmentMaterials.ApplyPieceSurface(m,name);labels.Add(name,m);
            }
            board=new SectionView("CFG1 physical wood board",ShapeFactory.CreateBoard(),labels["Board"],rig.Materials.BrightWood,true);
            board.UpdateSection(default,1,0,0);
            var shader=Resources.Load<Shader>("CfgCellLines");if(shader==null)throw new InvalidOperationException("CFG1 cell shader missing");lineMaterial=new Material(shader);
            lineObject=new GameObject("CFG1 exact cell sections");lineObject.transform.position=AirAnchor;
            lineMesh=new Mesh{name="CFG1 finite cell edges",indexFormat=IndexFormat.UInt32};lineMesh.MarkDynamic();
            lineObject.AddComponent<MeshFilter>().sharedMesh=lineMesh;lineObject.AddComponent<MeshRenderer>().sharedMaterial=lineMaterial;
            fillMaterial=new Material(shader){renderQueue=2999};
            fillObject=new GameObject("CFG2 exact cell faces");fillObject.transform.position=AirAnchor;
            fillMesh=new Mesh{name="CFG2 finite cell faces",indexFormat=IndexFormat.UInt32};fillMesh.MarkDynamic();
            fillObject.AddComponent<MeshFilter>().sharedMesh=fillMesh;fillObject.AddComponent<MeshRenderer>().sharedMaterial=fillMaterial;
        }
        public static VecN V(QN p){var q=p.ToArray();var v=new double[Math.Max(4,q.Length)];for(int i=0;i<q.Length;i++)v[i]=q[i];return new VecN(v);}
        static double L1(VecN p){double sum=0;for(int i=0;i<p.Dimensions;i++)sum+=Math.Abs(p.Axis(i));return sum;}
        public void Rebuild(SliceFrame frame,long revision,IEnumerable<CfgVisualPiece> source,IEnumerable<QN> g,IEnumerable<QN> p,QN? selected)
            =>Rebuild(frame,revision,source,g,p,Array.Empty<QN>(),selected);
        public void Rebuild(SliceFrame frame,long revision,IEnumerable<CfgVisualPiece> source,IEnumerable<QN> g,IEnumerable<QN> p,IEnumerable<QN> attacks,QN? selected)
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();ViewRevision=revision;Diagnostic="";
            var gs=new HashSet<QN>(g??Array.Empty<QN>());var ps=new HashSet<QN>(p??Array.Empty<QN>());
            var attacked=new HashSet<QN>(attacks??Array.Empty<QN>());
            candidateCells.Clear();candidateCells.UnionWith(ps);pieceCells.Clear();
            VisibleG=VisibleP=VisibleAttacks=VisiblePieces=0;cells.Clear();lines.Clear();colors.Clear();indices.Clear();displayEdges.Clear();fillVertices.Clear();fillColors.Clear();fillIndices.Clear();
            double half=HalfWindow;if(half<=0||double.IsNaN(half)||double.IsInfinity(half))throw new ArgumentOutOfRangeException(nameof(HalfWindow));
            lineObject.transform.position=fillObject.transform.position=Anchor;int n=frame.Dimensions;var cell=Cell(n);
            // Enumerate only temporary geometric candidates; no 9^N state array is allocated. The N nested loops of the 4D code
            // become one axis-by-axis recursion that drops a prefix as soon as no completion can reach the section window,
            // so an axis-aligned view visits about 9^3 cells whatever N is. For N=4 the surviving cells and their order are unchanged.
            var constraints=new List<VecN>();var bounds=new List<double>();
            foreach(var normal in frame.Normals){constraints.Add(normal);bounds.Add(.5*L1(normal)+1e-8);}
            foreach(var axis in new[]{frame.AxisX,frame.AxisY,frame.AxisZ}){constraints.Add(axis);bounds.Add(half+.5*L1(axis));}
            var offset=embedding-frame.Center;int k=constraints.Count;
            // Largest |coefficient*d| any remaining axis can still add: d ranges over -4..4 plus the axis offset.
            var remaining=new double[k,n+1];
            for(int c=0;c<k;c++)for(int axis=n-1;axis>=0;axis--)remaining[c,axis]=remaining[c,axis+1]+Math.Abs(constraints[c].Axis(axis))*Math.Max(Math.Abs(-4+offset.Axis(axis)),Math.Abs(4+offset.Axis(axis)));
            var coordinates=new int[n];var sums=new double[k];
            void Visit(int axis)
            {
                for(int c=0;c<k;c++)if(Math.Abs(sums[c])-remaining[c,axis]>bounds[c])return;
                if(axis<n)
                {
                    for(int value=-4;value<=4;value++)
                    {
                        coordinates[axis]=value;double d=value+offset.Axis(axis);
                        for(int c=0;c<k;c++)sums[c]+=constraints[c].Axis(axis)*d;
                        Visit(axis+1);
                        for(int c=0;c<k;c++)sums[c]-=constraints[c].Axis(axis)*d;
                    }
                    return;
                }
                var q=new QN(coordinates);
                var section=FiniteSliceWindow.Build(new InstanceN(cell,V(q)+embedding,1,q.ToString()),frame,half);
                if(section.Status==SectionStatus.NumericallyUncertain||section.Status==SectionStatus.CapacityExceeded){Diagnostic=section.Status.ToString();return;}
                if(section.Status!=SectionStatus.Volume3D)return;
                cells.Add(new CellMesh{Id=q,Section=section});if(gs.Contains(q))VisibleG++;if(ps.Contains(q))VisibleP++;if(attacked.Contains(q))VisibleAttacks++;
                bool highlighted=Candidates&&(gs.Contains(q)||ps.Contains(q));bool attack=Attacks&&attacked.Contains(q);if(!Grid&&!highlighted&&!attack&&(!selected.HasValue||selected.Value!=q))return;
                bool isSelected=selected.HasValue&&selected.Value==q;
                var color=isSelected?new Color(1,.76f,.26f,.95f):attack?new Color(1,.28f,.23f,.84f):highlighted?(ps.Contains(q)?new Color(.32f,.58f,1,.78f):Fill?new Color(.44f,.63f,.87f,.40f):new Color(.68f,.42f,.8f,.55f)):new Color(.65f,.76f,.83f,Fill?.065f:.16f);
                AddEdges(section,color,isSelected?4:attack?3:highlighted?(ps.Contains(q)?2:1):0);
                if(Fill&&(isSelected||highlighted||attack))AddFaces(section,new Color(color.r,color.g,color.b,isSelected?.12f:attack?.055f:ps.Contains(q)?.045f:.015f));
            }
            Visit(0);
            foreach(var edge in displayEdges.Values)
            {
                indices.Add(lines.Count);lines.Add(SectionView.MapVector(edge.A)*displayCell);colors.Add(edge.Color);
                indices.Add(lines.Count);lines.Add(SectionView.MapVector(edge.B)*displayCell);colors.Add(edge.Color);
            }
            lineMesh.Clear();lineMesh.SetVertices(lines);lineMesh.SetColors(colors);lineMesh.SetIndices(indices,MeshTopology.Lines,0);lineMesh.RecalculateBounds();
            fillMesh.Clear();fillMesh.SetVertices(fillVertices);fillMesh.SetColors(fillColors);fillMesh.SetIndices(fillIndices,MeshTopology.Triangles,0);fillMesh.RecalculateBounds();
            var live=new HashSet<string>();
            if(pieceDimensions!=n){foreach(var stale in pieces.Values)stale.Dispose();pieces.Clear();pieceDimensions=n;}
            foreach(var item in source)
            {
                live.Add(item.Id);pieceCells[item.Id]=item.Position;if(!pieces.TryGetValue(item.Id,out var view))
                {
                    string texture=TextureFor(item.DefinitionId,item.Side,StrictDefinitions);view=new SectionView("CFG1 "+item.Id,n==4?ShapeFactory.CreatePiece(ShapeVariant.C):ShapeFactory.CreatePiece(ShapeVariant.C,1,n),labels[texture],rig.Materials.BrightWood);
                    pieces[item.Id]=view;
                }
                view.Object.transform.position=Anchor;
                view.Object.transform.localScale=Vector3.one*(displayCell/SectionView.Cell);
                view.Renderer.sharedMaterials=new[]{rig.Materials.BrightWood,labels[TextureFor(item.DefinitionId,item.Side,StrictDefinitions)]};
                view.UpdateSection((item.Glide??V(item.Position))+embedding,item.Side==Side.A?1:-1,frame,revision,half);
                var section=view.Last;
                view.Renderer.enabled=ShowPieces&&section.Status==SectionStatus.Volume3D;
                if(ShowPieces&&section.Status==SectionStatus.Volume3D)VisiblePieces++;
            }
            foreach(var id in pieces.Keys.Where(id=>!live.Contains(id)).ToArray()){pieces[id].Dispose();pieces.Remove(id);}
            LastBuildMilliseconds=watch.Elapsed.TotalMilliseconds;
        }
        // CFG1's read-only unsupported-definition visual placeholder is retained.
        // The new match entry enables strict mapping and never aliases an unknown kind.
        public bool StrictDefinitions;
        internal static string TextureFor(string id,Side side,bool strict=true)
        {
            if(id.StartsWith("king"))return side==Side.A?"KingA":"KingB";
            if(id.StartsWith("rook"))return "Rook";if(id.StartsWith("bishop"))return "Bishop";
            if(id.StartsWith("gold"))return "Gold";if(id.StartsWith("silver"))return "Silver";
            if(id.StartsWith("knight"))return "Knight";if(id.StartsWith("lance"))return "Lance";if(id.StartsWith("pawn"))return "Pawn";
            if(!strict)return "Pawn";
            throw new ArgumentException("Unsupported visual definition: "+id,nameof(id));
        }
        void AddFaces(SectionResult section,Color color)
        {
            int offset=fillVertices.Count;
            foreach(var vertex in section.Vertices){fillVertices.Add(SectionView.MapVector(vertex.Position)*displayCell);fillColors.Add(color);}
            foreach(int index in section.Indices)fillIndices.Add(offset+index);
        }
        void AddEdges(SectionResult result,Color color,int priority)
        {
            // Triangulation diagonals have the same plane normal on both sides.
            // Keep only geometric creases, then merge edges shared by adjacent cells.
            cellEdges.Clear();
            for(int i=0;i<result.Indices.Length;i+=3)for(int e=0;e<3;e++)
            {
                var a=result.Vertices[result.Indices[i+e]];var b=result.Vertices[result.Indices[i+(e+1)%3]];
                var key=new EdgeKey(new PointKey(a.Position),new PointKey(b.Position));
                if(cellEdges.TryGetValue(key,out var edge))
                {edge.Crease|=Math.Abs(Vec3.Dot(edge.Normal,a.Normal))<1-1e-8;cellEdges[key]=edge;}
                else cellEdges.Add(key,new CellEdge{A=a.Position,B=b.Position,Normal=a.Normal});
            }
            foreach(var entry in cellEdges)
            {
                var edge=entry.Value;if(!edge.Crease)continue;
                if(displayEdges.TryGetValue(entry.Key,out var displayed))
                {
                    // Explicit selection wins over P, G and background grid. A tie
                    // retains the more opaque colour without blending duplicates.
                    if(priority>displayed.Priority||(priority==displayed.Priority&&color.a>displayed.Color.a))
                    {displayed.Color=color;displayed.Priority=priority;displayEdges[entry.Key]=displayed;}
                }
                else displayEdges.Add(entry.Key,new DisplayEdge{A=edge.A,B=edge.B,Color=color,Priority=priority});
            }
        }
        public bool Pick(Ray ray,long expectedRevision,out QN id,bool prioritizeSelection=true)
        {
            id=default;if(expectedRevision!=ViewRevision)return false;float nearest=float.PositiveInfinity;bool found=false;
            if(prioritizeSelection)
            {
                if(PickPieces(ray,expectedRevision,out id,out nearest))return true;
                foreach(var c in cells.Where(c=>candidateCells.Contains(c.Id)))
                for(int i=0;i<c.Section.Indices.Length;i+=3)
                {
                    Vector3 At(int n)=>Anchor+SectionView.MapVector(c.Section.Vertices[c.Section.Indices[i+n]].Position)*displayCell;
                    if(RayTriangle(ray,At(0),At(1),At(2),out float distance)&&distance<nearest){nearest=distance;id=c.Id;found=true;}
                }
                if(found)return true;
            }
            foreach(var c in cells)
            for(int i=0;i<c.Section.Indices.Length;i+=3)
            {
                Vector3 At(int n)=>Anchor+SectionView.MapVector(c.Section.Vertices[c.Section.Indices[i+n]].Position)*displayCell;
                if(RayTriangle(ray,At(0),At(1),At(2),out float distance)&&distance<nearest){nearest=distance;id=c.Id;found=true;}
            }
            return found;
        }
        public bool PickPieces(Ray ray,long expectedRevision,out QN id,out float distance)
        {
            id=default;distance=float.PositiveInfinity;if(!ShowPieces||expectedRevision!=ViewRevision)return false;bool found=false;
            foreach(var piece in pieces)
            {
                var section=piece.Value.Last;if(section==null||section.Status!=SectionStatus.Volume3D)continue;
                for(int i=0;i<section.Indices.Length;i+=3)
                {
                    Vector3 At(int n)=>Anchor+SectionView.MapVector(section.Vertices[section.Indices[i+n]].Position)*displayCell;
                    if(RayTriangle(ray,At(0),At(1),At(2),out float hit)&&hit<distance){distance=hit;id=pieceCells[piece.Key];found=true;}
                }
            }
            return found;
        }
        internal static bool RayTriangle(Ray r,Vector3 a,Vector3 b,Vector3 c,out float distance)
        {
            distance=0;var e1=b-a;var e2=c-a;var h=Vector3.Cross(r.direction,e2);float det=Vector3.Dot(e1,h);
            if(Mathf.Abs(det)<1e-9f)return false;float inv=1/det;var s=r.origin-a;float u=inv*Vector3.Dot(s,h);if(u<0||u>1)return false;
            var q=Vector3.Cross(s,e1);float v=inv*Vector3.Dot(r.direction,q);if(v<0||u+v>1)return false;
            distance=inv*Vector3.Dot(e2,q);return distance>0;
        }
        public void Dispose()
        {foreach(var p in pieces.Values)p.Dispose();board.Dispose();UnityEngine.Object.Destroy(lineObject);UnityEngine.Object.Destroy(lineMesh);UnityEngine.Object.Destroy(lineMaterial);UnityEngine.Object.Destroy(fillObject);UnityEngine.Object.Destroy(fillMesh);UnityEngine.Object.Destroy(fillMaterial);foreach(var m in labels.Values)UnityEngine.Object.Destroy(m);}
    }
}
