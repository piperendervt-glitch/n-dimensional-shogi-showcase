using UnityEditor;
using UnityEngine;

// Packed normals are imported as linear Default textures (X in alpha), not repacked twice.
public sealed class ShowcaseTextureImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if(!assetPath.Contains("/PieceTextures/")&&!assetPath.Contains("/ShowcasePbr/"))return;
        var importer=(TextureImporter)assetImporter;
        bool normal=assetPath.EndsWith("_Normal.png"),mask=assetPath.EndsWith("_Mask.png");
        importer.textureType=TextureImporterType.Default;
        importer.sRGBTexture=!(normal||mask);
        importer.mipmapEnabled=true;importer.mipmapFilter=TextureImporterMipFilter.KaiserFilter;
        importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=16;
        importer.mipMapBias=-.25f;importer.maxTextureSize=2048;
        importer.wrapMode=assetPath.Contains("/PieceTextures/")?TextureWrapMode.Clamp:TextureWrapMode.Repeat;
        importer.textureCompression=normal?TextureImporterCompression.Uncompressed:TextureImporterCompression.CompressedHQ;
        importer.compressionQuality=100;importer.alphaSource=TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency=false;
    }
}
