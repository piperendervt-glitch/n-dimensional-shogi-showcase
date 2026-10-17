using System;
using System.Collections.Generic;

namespace Showcase.Geometry
{
    public static class ShapeFactory
    {
        public const double CellPitchMetres=0.05;
        public static ConvexShape CreatePawn(ShapeVariant variant=ShapeVariant.A) => CreatePiece(variant);
        /// <summary>The piece solid of an N-dimensional board: the 4D solid, with the W thickness repeated along every further axis.</summary>
        public static ConvexShape CreatePiece(ShapeVariant variant,double size,int dimensions)
        {
            var piece=CreatePiece(variant,size); if(dimensions==4) return piece;
            if(dimensions<3||dimensions>VecN.MaxDimensions) throw new ArgumentOutOfRangeException(nameof(dimensions));
            double halfW=(variant==ShapeVariant.A?.2:.29)*size;
            var planes=new List<HalfspaceN>(piece.Planes);
            for(int axis=4;axis<dimensions;axis++) { planes.Add(new HalfspaceN(VecN.Unit(axis,dimensions),halfW,100+2*axis,SurfaceKind.End)); planes.Add(new HalfspaceN(-VecN.Unit(axis,dimensions),halfW,101+2*axis,SurfaceKind.End)); }
            var min=piece.Bounds.Min.ToArray(dimensions);var max=piece.Bounds.Max.ToArray(dimensions);
            for(int axis=4;axis<dimensions;axis++) { min[axis]=-halfW; max[axis]=halfW; }
            // A 3D board has no W: the piece is the W=0 section of the 4D solid, which the W end caps already bound.
            return new ConvexShape(piece.Id+"/N"+dimensions,planes,new VecBounds(new VecN(min),new VecN(max)));
        }
        public static ConvexShape CreatePiece(ShapeVariant variant,double size=1)
        {
            if(size<=0) throw new ArgumentOutOfRangeException(nameof(size));
            // A symmetric pentagon, with a sloped upper surface and real edge chamfers.
            // All dimensions are fixed in the source solid, never functions of the slice angle.
            var basePlanes=new List<HalfspaceN> {
                new HalfspaceN(new VecN(-1,0,0,0),.36,0),
                new HalfspaceN(new VecN(0,0,-1,0),0,1,SurfaceKind.Bottom),
                new HalfspaceN(new VecN(1.0/18,0,1,0),.17,2,SurfaceKind.Top),
                new HalfspaceN(new VecN(1.0/13,1,0,0),.2623076923076923,3),
                new HalfspaceN(new VecN(1.0/13,-1,0,0),.2623076923076923,4),
                new HalfspaceN(new VecN(1.25,1,0,0),.45,5),
                new HalfspaceN(new VecN(1.25,-1,0,0),.45,6)
            };
            var beveled=new List<HalfspaceN>(basePlanes);
            int nextId=10;
            for(int i=0;i<basePlanes.Count;i++) for(int j=i+1;j<basePlanes.Count;j++)
            {
                VecN n=basePlanes[i].Normal+basePlanes[j].Normal;
                if(n.Length<.05) continue;
                beveled.Add(new HalfspaceN(n,basePlanes[i].Offset+basePlanes[j].Offset-.012*n.Length,nextId++,SurfaceKind.Bevel));
            }
            var result=new List<HalfspaceN>();
            foreach(var p in beveled)
            {
                result.Add(new HalfspaceN(p.Normal,p.Offset*size,p.FaceId,p.Surface));
                if(variant==ShapeVariant.C && Math.Abs(p.Normal.Y)>1e-12)
                    result.Add(new HalfspaceN(new VecN(p.Normal.X,0,p.Normal.Z,p.Normal.Y),p.Offset*size,1000+p.FaceId,p.Surface));
            }
            double halfW=variant==ShapeVariant.A?.2:.29;
            if(variant==ShapeVariant.A)
            { result.Add(new HalfspaceN(new VecN(0,0,0,1),halfW*size,100,SurfaceKind.End)); result.Add(new HalfspaceN(new VecN(0,0,0,-1),halfW*size,101,SurfaceKind.End)); }
            return new ConvexShape("shogi-pentagon-"+variant+"-"+size.ToString("R",System.Globalization.CultureInfo.InvariantCulture),result,new VecBounds(new VecN(-.36,-.29,0,-halfW)*size,new VecN(.36,.29,.19,halfW)*size));
        }
        public static ConvexShape CreateBoard() => CreateBox(new VecN(-4.5,-4.5,-.8,-4.5),new VecN(4.5,4.5,0,4.5),.04,"board");
        /// <summary>The board of an N-dimensional game: 9 cells along every axis but the height, which keeps the wooden thickness.</summary>
        public static ConvexShape CreateBoard(int dimensions)
        {
            if(dimensions==4) return CreateBoard();
            var min=new double[dimensions];var max=new double[dimensions];
            for(int i=0;i<dimensions;i++) { min[i]=i==2?-.8:-4.5; max[i]=i==2?0:4.5; }
            return CreateBox(new VecN(min),new VecN(max),.04,"board/N"+dimensions);
        }
        // The N of the box is the larger declared N of its corners, at least 4; the 4D form is bit-identical to before.
        public static ConvexShape CreateBox(VecN min,VecN max,double bevel=0,string id="box")
        {
            int n=Math.Max(4,Math.Max(min.Dimensions,max.Dimensions)); if(n>VecN.MaxDimensions) throw new ArgumentOutOfRangeException(nameof(min));
            for(int i=0;i<n;i++) if(min.Axis(i)>=max.Axis(i)) throw new ArgumentException("Box extents must be positive.");
            var p=new List<HalfspaceN>();
            for(int i=0;i<n;i++)
            {
                var kinds=i==2?(SurfaceKind.Top,SurfaceKind.Bottom):i>=3?(SurfaceKind.End,SurfaceKind.End):(SurfaceKind.Side,SurfaceKind.Side);
                p.Add(new HalfspaceN(VecN.Unit(i,n),max.Axis(i),2*i,kinds.Item1)); p.Add(new HalfspaceN(-VecN.Unit(i,n),-min.Axis(i),2*i+1,kinds.Item2));
            }
            if(bevel<0) throw new ArgumentOutOfRangeException(nameof(bevel));
            int faces=2*n;
            if(bevel>0) for(int i=0;i<faces;i++) for(int j=i+1;j<faces;j++)
            { if(Math.Abs(VecN.Dot(p[i].Normal,p[j].Normal))>.5) continue; p.Add(new HalfspaceN(p[i].Normal+p[j].Normal,p[i].Offset+p[j].Offset-bevel,100+i*8+j,SurfaceKind.Bevel)); }
            return new ConvexShape(id,p,new VecBounds(min,max));
        }
    }
}
