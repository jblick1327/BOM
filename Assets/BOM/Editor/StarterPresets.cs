using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BOM.Editor
{
    public static class StarterPresets
    {
        public const string Folder="Assets/BOM/Presets";
        private static string[] Names(Type type) => type==typeof(MaterialDefinition)?new[] {"Default","Light","Heavy","Tough"}:
            type==typeof(DestructionResponse)?new[] {"Standard","No push"}:type==typeof(ExplosionPreset)?new[] {"Small","Medium","Large"}:Array.Empty<string>();
        private static string Subfolder(Type type) => type==typeof(MaterialDefinition)?"Materials":
            type==typeof(DestructionResponse)?"DestructionResponses":"ExplosionPresets";
        public static T Get<T>(string name) where T:ScriptableObject => AssetDatabase.LoadAssetAtPath<T>(Folder+"/"+Subfolder(typeof(T))+"/"+name+".asset");
        public static T[] All<T>() where T:ScriptableObject => Names(typeof(T)).Select(Get<T>).Where(asset=>asset!=null).ToArray();
        public static bool IsStarter(ScriptableObject asset) => asset!=null&&Names(asset.GetType()).Any(name=>
            AssetDatabase.GetAssetPath(asset)==Folder+"/"+Subfolder(asset.GetType())+"/"+name+".asset");
        public static string Description(ScriptableObject asset)
        {
            if(!IsStarter(asset))return "Shared asset: edits immediately affect every use. Duplicate to make an independent copy.";
            if(asset is MaterialDefinition)
                return asset.name=="Light"?"Half the mass of Default; the same resistance and motion settings.":
                    asset.name=="Heavy"?"Twice the mass of Default; the same resistance and motion settings.":
                    asset.name=="Tough"?"Three times the blast resistance of Default; the same mass and motion settings.":"Balanced mass and blast resistance.";
            if(asset is DestructionResponse)return asset.name=="No push"?"Destruction without added blast speed.":"Use the explosion preset's added speed.";
            return "Maximum blast radius and added speed increase together. Resistance and shielding can shorten the blast.";
        }
        private static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path))return;
            var parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        private static void Ensure<T>(string name,Action<T> configure) where T:ScriptableObject
        {
            string folder=Folder+"/"+Subfolder(typeof(T));EnsureFolder(folder);string path=folder+"/"+name+".asset";
            if(AssetDatabase.LoadMainAssetAtPath(path)!=null||File.Exists(path))return;
            var asset=ScriptableObject.CreateInstance<T>();configure(asset);AssetDatabase.CreateAsset(asset,path);AssetDatabase.SaveAssetIfDirty(asset);
        }
        [MenuItem("BOM/Create missing starter presets")]
        public static void Create()
        {
            Ensure<MaterialDefinition>("Default",_=>{});
            Ensure<MaterialDefinition>("Light",m=>m.density=1);
            Ensure<MaterialDefinition>("Heavy",m=>m.density=4);
            Ensure<MaterialDefinition>("Tough",m=>m.blastResistance=3);
            Ensure<DestructionResponse>("Standard",_=>{});
            Ensure<DestructionResponse>("No push",r=>r.impulseScale=0);
            Ensure<ExplosionPreset>("Small",p=>{p.radius=.8f;p.reachBudget=1.6f;p.impulseSpeed=.3f;});
            Ensure<ExplosionPreset>("Medium",_=>{});
            Ensure<ExplosionPreset>("Large",p=>{p.radius=4;p.reachBudget=8;p.impulseSpeed=2;});
        }
    }
}
