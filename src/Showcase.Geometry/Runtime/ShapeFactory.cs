using System;
using System.Collections.Generic;

namespace Showcase.Geometry
{
    public static class ShapeFactory
    {
        public const double CellPitchMetres=0.05;
        public static ConvexShape CreatePawn(ShapeVariant variant=ShapeVariant.A) => CreatePiece(variant);
        public static ConvexShape CreatePiece(ShapeVariant variant,double size=1)
        {
            if(size<=0) throw new ArgumentOutOfRangeException(nameof(size));
            // A symmetric pentagon, with a sloped upper surface and real edge chamfers.
            // All dimensions are fixed in the source solid, never functions of the slice angle.
            var basePlanes=new List<Halfspace4> {
                new Halfspace4(new Vec4(-1,0,0,0),.36,0),
                new Halfspace4(new Vec4(0,0,-1,0),0,1,SurfaceKind.Bottom),
                new Halfspace4(new Vec4(1.0/18,0,1,0),.17,2,SurfaceKind.Top),
                new Halfspace4(new Vec4(1.0/13,1,0,0),.2623076923076923,3),
                new Halfspace4(new Vec4(1.0/13,-1,0,0),.2623076923076923,4),
                new Halfspace4(new Vec4(1.25,1,0,0),.45,5),
                new Halfspace4(new Vec4(1.25,-1,0,0),.45,6)
            };
            var beveled=new List<Halfspace4>(basePlanes);
            int nextId=10;
            for(int i=0;i<basePlanes.Count;i++) for(int j=i+1;j<basePlanes.Count;j++)
            {
                Vec4 n=basePlanes[i].Normal+basePlanes[j].Normal;
                if(n.Length<.05) continue;
                beveled.Add(new Halfspace4(n,basePlanes[i].Offset+basePlanes[j].Offset-.012*n.Length,nextId++,SurfaceKind.Bevel));
            }
            var result=new List<Halfspace4>();
            foreach(var p in beveled)
            {
                result.Add(new Halfspace4(p.Normal,p.Offset*size,p.FaceId,p.Surface));
                if(variant==ShapeVariant.C && Math.Abs(p.Normal.Y)>1e-12)
                    result.Add(new Halfspace4(new Vec4(p.Normal.X,0,p.Normal.Z,p.Normal.Y),p.Offset*size,1000+p.FaceId,p.Surface));
            }
            double halfW=variant==ShapeVariant.A?.2:.29;
            if(variant==ShapeVariant.A)
            { result.Add(new Halfspace4(new Vec4(0,0,0,1),halfW*size,100,SurfaceKind.End)); result.Add(new Halfspace4(new Vec4(0,0,0,-1),halfW*size,101,SurfaceKind.End)); }
            return new ConvexShape("shogi-pentagon-"+variant+"-"+size.ToString("R",System.Globalization.CultureInfo.InvariantCulture),result,new Bounds4(new Vec4(-.36,-.29,0,-halfW)*size,new Vec4(.36,.29,.19,halfW)*size));
        }
        public static ConvexShape CreateBoard() => CreateBox(new Vec4(-4.5,-4.5,-.8,-4.5),new Vec4(4.5,4.5,0,4.5),.04,"board");
        public static ConvexShape CreateBox(Vec4 min,Vec4 max,double bevel=0,string id="box")
        {
            if(min.X>=max.X||min.Y>=max.Y||min.Z>=max.Z||min.W>=max.W) throw new ArgumentException("Box extents must be positive.");
            var p=new List<Halfspace4> {
                new Halfspace4(new Vec4(1,0,0,0),max.X,0),new Halfspace4(new Vec4(-1,0,0,0),-min.X,1),
                new Halfspace4(new Vec4(0,1,0,0),max.Y,2),new Halfspace4(new Vec4(0,-1,0,0),-min.Y,3),
                new Halfspace4(new Vec4(0,0,1,0),max.Z,4,SurfaceKind.Top),new Halfspace4(new Vec4(0,0,-1,0),-min.Z,5,SurfaceKind.Bottom),
                new Halfspace4(new Vec4(0,0,0,1),max.W,6,SurfaceKind.End),new Halfspace4(new Vec4(0,0,0,-1),-min.W,7,SurfaceKind.End)
            };
            if(bevel<0) throw new ArgumentOutOfRangeException(nameof(bevel));
            if(bevel>0) for(int i=0;i<8;i++) for(int j=i+1;j<8;j++)
            { if(Math.Abs(Vec4.Dot(p[i].Normal,p[j].Normal))>.5) continue; p.Add(new Halfspace4(p[i].Normal+p[j].Normal,p[i].Offset+p[j].Offset-bevel,100+i*8+j,SurfaceKind.Bevel)); }
            return new ConvexShape(id,p,new Bounds4(min,max));
        }
    }
}
