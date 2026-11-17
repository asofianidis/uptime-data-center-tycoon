using UnityEditor;

namespace DCT.EditorTools
{
    /// <summary>Blender models in Resources/Models must keep CPU-readable meshes: racks, scenery and placement
    /// previews bake them into the game's vertex-colored meshes at runtime.</summary>
    class ModelImportSettings : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Models/")) return;
            var mi = (ModelImporter)assetImporter;
            mi.isReadable = true;
            mi.addCollider = false;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
        }
    }
}
