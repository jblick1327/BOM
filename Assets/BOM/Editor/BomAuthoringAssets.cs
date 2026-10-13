using System.IO;
using UnityEditor;
using UnityEngine;

namespace BOM.Editor
{
    public static class BomAuthoringAssets
    {
        public static bool NewPath(string path,out string error)
        {
            error="Choose an unused .asset path in an existing Assets folder. Existing assets are never overwritten.";
            if(string.IsNullOrEmpty(path)||!path.StartsWith("Assets/",System.StringComparison.Ordinal)
                ||!path.EndsWith(".asset",System.StringComparison.OrdinalIgnoreCase)||path.Contains("\\")
                ||path.Contains("/../")||path.Contains("/./")||path.Contains(":"))return false;
            var folder=Path.GetDirectoryName(path)?.Replace('\\','/');
            if(!AssetDatabase.IsValidFolder(folder)||AssetDatabase.LoadMainAssetAtPath(path)!=null||File.Exists(path))return false;
            error=null;return true;
        }
        public static T Create<T>(string path,string suggestedName,out string error) where T:ScriptableObject
        {
            error=null;path=path??EditorUtility.SaveFilePanelInProject("Create BOM "+suggestedName,suggestedName,"asset","Save the editable reusable definition.");
            if(string.IsNullOrEmpty(path)||!NewPath(path,out error))return null;
            var asset=ScriptableObject.CreateInstance<T>();
            try{AssetDatabase.CreateAsset(asset,path);Undo.RegisterCreatedObjectUndo(asset,"Create BOM definition");AssetDatabase.SaveAssetIfDirty(asset);return asset;}
            catch(System.Exception e){if(!AssetDatabase.Contains(asset))Object.DestroyImmediate(asset);error="Could not create the definition: "+e.Message;return null;}
        }
        public static T Duplicate<T>(T source,string path,out string error) where T:ScriptableObject
        {
            error=null;if(source==null){error="Choose an asset to duplicate.";return null;}
            string folder=EditorPrefs.GetString("BOM.Authoring.Folder","Assets");if(!AssetDatabase.IsValidFolder(folder))folder="Assets";
            path=path??EditorUtility.SaveFilePanelInProject("Duplicate BOM asset",source.name+" Copy","asset","Save an independent copy.",folder);
            if(string.IsNullOrEmpty(path)||!NewPath(path,out error))return null;
            var copy=Object.Instantiate(source);copy.name=Path.GetFileNameWithoutExtension(path);
            try{AssetDatabase.CreateAsset(copy,path);Undo.RegisterCreatedObjectUndo(copy,"Duplicate BOM asset");AssetDatabase.SaveAssetIfDirty(copy);return copy;}
            catch(System.Exception e){if(!AssetDatabase.Contains(copy))Object.DestroyImmediate(copy);error="Could not duplicate the asset: "+e.Message;return null;}
        }
    }
}
