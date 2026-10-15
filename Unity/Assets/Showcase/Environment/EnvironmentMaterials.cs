using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace NDimensionalShogi.Showcase.Environment
{
    /// <summary>CC0 scanned wood/stone and original ceramic/botanical PBR fields. UVs belong to the source solid.</summary>
    public sealed class EnvironmentMaterials
    {
        public Material BrightWood, DarkWood, Stone, PaleStone, Ink, Water, Brass, Soil, Ceramic;
        public Material[] Foliage, Flower, Paving;
        public const string Version = "pbr-courtyard/3.0";

        public EnvironmentMaterials()
        {
            BrightWood = ScannedSurface("Honey kaya", "Honey", .46f, .12f);
            DarkWood = ScannedSurface("Oiled walnut", "Walnut", .55f, .18f);
            Stone = ScannedSurface("Warm sandstone", "Sandstone", .26f, 0, new Color(.88f,.87f,.84f));
            PaleStone = ScannedSurface("Pale sandstone", "Sandstone", .26f, 0);
            Soil = Mineral("Garden earth", new Color(.065f,.043f,.027f), new Color(.13f,.10f,.053f), .05f);
            Ink = Lit("Sumi ink", new Color(.013f,.008f,.004f), .24f);
            Brass = Lit("Brushed antique brass", new Color(.42f,.28f,.085f), .53f, .85f);
            Water = Lit("Fountain jade water", new Color(.15f,.25f,.24f,.16f), .96f, 0);
            Water.SetFloat("_SurfaceType",1);Water.SetFloat("_BlendMode",0);
            Water.SetFloat("_EnableBlendModePreserveSpecularLighting",1);
            Water.SetFloat("_TransparentDepthPrepassEnable",0);Water.SetFloat("_TransparentDepthPostpassEnable",0);
            Water.SetFloat("_RefractionModel",0);HDMaterial.ValidateMaterial(Water);
            Ceramic = Mineral("Charcoal ceramic", new Color(.025f,.024f,.020f), new Color(.13f,.12f,.10f), .56f);
            Ceramic.SetFloat("_CoatMask",.35f);HDMaterial.ValidateMaterial(Ceramic);
            Foliage = new Material[4];
            for(int i=0;i<Foliage.Length;i++) Foliage[i] = LeafMaterial(i);
            Flower = new[] {Lit("Ivory petals",new Color(.93f,.89f,.71f),.18f),Lit("Soft lavender petals",new Color(.38f,.26f,.53f),.16f)};
            foreach(var mat in Foliage)DoubleSided(mat);
            foreach(var mat in Flower)DoubleSided(mat);
            Paving = new Material[7];
            for(int i=0;i<Paving.Length;i++)
            {
                Paving[i] = new Material(Stone) {name="Stone variation " + i};
                Paving[i].SetColor("_BaseColor",Color.Lerp(new Color(.72f,.71f,.66f),Color.white,i/6f));
            }
        }

        static void DoubleSided(Material mat)
        {
            mat.SetFloat("_DoubleSidedEnable",1);
            HDMaterial.ValidateMaterial(mat);
        }

        public static Material Lit(string name, Color color, float smoothness=.3f,float metallic=0)
        {
            var cached=Application.isPlaying?Resources.Load<Material>("ShowcaseMaterialVariants/"+name.Replace(' ','_')):null;
            if(cached!=null)return cached;
            var mat = new Material(Shader.Find("HDRP/Lit")) { name=name, enableInstancing=true };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic",metallic);
            HDMaterial.ValidateMaterial(mat);
            return mat;
        }

        static Texture2D Texture(string name,int size,Color[] data,bool linear=false)
        {
            var tex=new Texture2D(size,size,TextureFormat.RGBA32,true,linear){name=name,wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=8};
            tex.SetPixels(data);tex.Apply(true,false);return tex;
        }
        static float Noise(float x,float y) => Mathf.PerlinNoise(x+31.37f,y+17.71f);
        static Material ScannedSurface(string name,string textureName,float smoothness,float coat,Color? tint=null)
        {
            var cached=Application.isPlaying?Resources.Load<Material>("ShowcaseMaterialVariants/"+name.Replace(' ','_')):null;
            if(cached!=null&&cached.GetTexture("_BaseColorMap")!=null)return cached;
            var mat=Lit(name,tint??Color.white,smoothness);
            mat.SetTexture("_BaseColorMap",Required("ShowcasePbr/"+textureName+"_Color"));
            mat.SetTexture("_MaskMap",Required("ShowcasePbr/"+textureName+"_Mask"));mat.EnableKeyword("_MASKMAP");
            mat.SetTexture("_NormalMap",Required("ShowcasePbr/"+textureName+"_Normal"));
            mat.SetFloat("_NormalScale",1);mat.EnableKeyword("_NORMALMAP_TANGENT_SPACE");mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_SmoothnessRemapMin",0);mat.SetFloat("_SmoothnessRemapMax",1);
            mat.SetFloat("_CoatMask",coat);HDMaterial.ValidateMaterial(mat);
            return mat;
        }
        static Texture2D Required(string resource)
        {
            var texture=Resources.Load<Texture2D>(resource);
            if(texture==null)throw new System.InvalidOperationException("Required fixed PBR field missing: "+resource);
            return texture;
        }
        public static void ApplyPieceSurface(Material material,string name)
        {
            material.SetTexture("_BaseColorMap",Required("PieceTextures/"+name));
            material.SetTexture("_NormalMap",Required("PieceTextures/"+name+"_Normal"));
            material.SetTexture("_MaskMap",Required("PieceTextures/"+name+"_Mask"));
            material.SetColor("_BaseColor",Color.white);material.SetFloat("_Metallic",0);
            material.SetFloat("_SmoothnessRemapMin",0);material.SetFloat("_SmoothnessRemapMax",1);
            material.SetFloat("_NormalScale",1);material.SetFloat("_CoatMask",.08f);
            material.EnableKeyword("_NORMALMAP_TANGENT_SPACE");material.EnableKeyword("_NORMALMAP");material.EnableKeyword("_MASKMAP");
            HDMaterial.ValidateMaterial(material);
        }
        static Material LeafMaterial(int variant)
        {
            string name="Laurel foliage "+variant;
            var cached=Application.isPlaying?Resources.Load<Material>("ShowcaseMaterialVariants/"+name.Replace(' ','_')):null;
            if(cached!=null&&cached.GetTexture("_BaseColorMap")!=null)return cached;
            const int size=256;var colors=new Color[size*size];var heights=new float[size*size];
            Color dark=Color.Lerp(new Color(.025f,.060f,.009f),new Color(.075f,.16f,.020f),variant/3f);
            Color light=Color.Lerp(new Color(.12f,.23f,.025f),new Color(.26f,.36f,.055f),variant/3f);
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float u=x/(float)size,v=y/(float)size;
                float rib=Mathf.Exp(-Mathf.Abs(u-.5f)*140),veins=Mathf.Pow(.5f+.5f*Mathf.Sin((v-Mathf.Abs(u-.5f)*.5f)*90),24);
                float noise=Noise(u*19,v*29),tone=.35f+noise*.35f+(1-Mathf.Abs(u-.5f)*2)*.15f;
                colors[y*size+x]=Color.Lerp(dark,light,tone+rib*.2f+veins*.06f);
                heights[y*size+x]=noise*.05f+rib*.15f+veins*.025f;
            }
            var mat=Lit(name,Color.white,.37f);mat.SetTexture("_BaseColorMap",Texture(name+" leaf veins",size,colors));SetNormal(mat,name,size,heights,.7f);return mat;
        }
        static Material Mineral(string name,Color dark,Color light,float smoothness)
        {
            var cached=Application.isPlaying?Resources.Load<Material>("ShowcaseMaterialVariants/"+name.Replace(' ','_')):null;
            if(cached!=null&&cached.GetTexture("_BaseColorMap")!=null)return cached;
            const int size=1024; var colors=new Color[size*size];var height=new float[size*size];var masks=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float u=x/(float)size,v=y/(float)size;
                float broad=Noise(u*3,v*3),cloud=Noise(u*13,v*11),fine=Noise(u*110,v*105);
                float pores=Mathf.Pow(Noise(u*190,v*173),9)*.25f;
                float vein=Mathf.Pow(.5f+.5f*Mathf.Sin(u*23+v*9+Noise(u*7,v*5)*10),18)*.04f;
                float h=cloud*.12f+fine*.035f-pores-vein;
                float tone=Mathf.Clamp01(.32f+broad*.42f+cloud*.18f+fine*.06f-pores*.5f);
                height[y*size+x]=h;colors[y*size+x]=Color.Lerp(dark,light,tone);
                masks[y*size+x]=new Color(0,1-pores*.4f,1,Mathf.Clamp01(smoothness+(cloud-.5f)*.16f-pores*.3f));
            }
            var mat=Lit(name,Color.white,smoothness);mat.SetTexture("_BaseColorMap",Texture(name+" mineral albedo",size,colors));
            mat.SetTexture("_MaskMap",Texture(name+" polish and pores",size,masks,true));mat.EnableKeyword("_MASKMAP");
            mat.SetFloat("_SmoothnessRemapMin",0);mat.SetFloat("_SmoothnessRemapMax",1);
            SetNormal(mat,name,size,height,.55f);return mat;
        }
        static void SetNormal(Material mat,string name,int size,float[] h,float strength)
        {
            var colors=new Color[h.Length];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float dx=(h[y*size+(x+1)%size]-h[y*size+(x+size-1)%size])*strength;
                float dy=(h[((y+1)%size)*size+x]-h[((y+size-1)%size)*size+x])*strength;
                var n=new Vector3(-dx,-dy,1).normalized;
                // Runtime normal maps use RGB normal packing with x also in alpha for platform compatibility.
                colors[y*size+x]=new Color(1,n.y*.5f+.5f,n.z*.5f+.5f,n.x*.5f+.5f);
            }
            mat.SetTexture("_NormalMap",Texture(name+" micro normal",size,colors,true));
            mat.SetFloat("_NormalScale",1);mat.EnableKeyword("_NORMALMAP_TANGENT_SPACE");mat.EnableKeyword("_NORMALMAP");
            HDMaterial.ValidateMaterial(mat);
        }
    }
}
