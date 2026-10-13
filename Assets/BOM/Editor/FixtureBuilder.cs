using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BOM.Editor
{
    public static class FixtureBuilder
    {
        public const string Folder="Assets/BOM/Fixtures";
        public const string MaterialsFolder=Folder+"/Materials";
        public const string ResponsesFolder=Folder+"/DestructionResponses";
        public const string PresetsFolder=Folder+"/ExplosionPresets";
        public const string PiecesFolder=Folder+"/Pieces";
        public const string TexturesFolder=Folder+"/Textures";
        public const string ScenePath=Folder+"/ReferenceLevel.unity";
        public const string AssemblyPath=Folder+"/ReferenceAssembly.prefab";
        public const string WorldPath=Folder+"/ReferenceWorld.json";
        public const string SourcePath=Folder+"/Source/MultiMeshFrame.fbx";
        private static void Ensure(string path)
        {
            if(AssetDatabase.IsValidFolder(path)) return;string parent=Path.GetDirectoryName(path).Replace('\\','/');Ensure(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        private static T Asset<T>(string name,Action<T> configure) where T:ScriptableObject
        {
            string folder=typeof(T)==typeof(MaterialDefinition)?MaterialsFolder:typeof(T)==typeof(DestructionResponse)?ResponsesFolder:
                typeof(T)==typeof(ExplosionPreset)?PresetsFolder:PiecesFolder;
            Ensure(folder);string path=folder+"/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<T>(path);if(existing!=null) return existing;
            var asset=ScriptableObject.CreateInstance<T>();asset.name=name;configure(asset);AssetDatabase.CreateAsset(asset,path);return asset;
        }
        public static Region Frame()
        {var region=Geometry.Rectangle(-1.5f,-1.5f,1.5f,1.5f);region.holes.Add(Geometry.Rectangle(-.5f,-.5f,.5f,.5f).outer);return region;}
        public static Region Concave() => new Region {outer=new Contour(new[] {new Vector2(0,0),new Vector2(2.5f,0),new Vector2(2.5f,.7f),new Vector2(.7f,.7f),new Vector2(.7f,2.5f),new Vector2(0,2.5f)})};
        private static PieceDefinition Piece(string name,Region profile,MaterialDefinition material,DestructionResponse response,Texture2D image,bool fixedAppearance=false)
        {
            return Asset<PieceDefinition>(name,piece=>
            {
                var draft=new PieceDraft {profile=profile,material=material,destruction=new DestructionSelection {mode=fixedAppearance?DestructionMode.Indestructible:DestructionMode.Subtract,response=response},
                    appearance=new FaceAppearance {image=image,face=fixedAppearance?new Color(.22f,.3f,.4f):Color.white,side=new Color(.3f,.4f,.5f),cut=new Color(1,.64f,.26f),u=Vector2.right*.4f,v=Vector2.up*.4f},thickness=.25f};
                if(!ImportService.Approve(piece,draft,out var report)) throw new InvalidOperationException(report.ToString());
            });
        }
        public static string Create()
        {
            Ensure(Folder);var material=Asset<MaterialDefinition>("Material_Density2_Resistance05",m=>{m.density=2;m.blastResistance=.5f;m.linearDamping=0;m.angularDamping=0;});
            var alternative=Asset<MaterialDefinition>("Material_Density4_Resistance2",m=>{m.density=4;m.blastResistance=2;});
            var response=Asset<DestructionResponse>("RayCostSubtraction",r=>r.impulseScale=1);
            var bite=Asset<ExplosionPreset>("Test_SmallBite",p=>{p.radius=.8f;p.reachBudget=2;p.impulseSpeed=.3f;});
            var split=Asset<ExplosionPreset>("Test_Split",p=>{p.radius=1.2f;p.reachBudget=5;p.impulseSpeed=1;});
            var removal=Asset<ExplosionPreset>("Test_Removal",p=>{p.radius=5;p.reachBudget=20;p.impulseSpeed=0;});
            Ensure(TexturesFolder);string texturePath=TexturesFolder+"/FaceChecker.asset";var image=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if(image==null)
            {
                image=new Texture2D(16,16,TextureFormat.RGBA32,false) {name="FaceChecker",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Point};
                for(int y=0;y<16;y++) for(int x=0;x<16;x++) image.SetPixel(x,y,(x/4+y/4)%2==0?new Color(.86f,.93f,.98f):new Color(.22f,.48f,.63f));
                image.Apply();AssetDatabase.CreateAsset(image,texturePath);
            }
            var frame=Piece("Frame_3x3_Hole1x1",Frame(),material,response,image);
            var concave=Piece("Concave_L",Concave(),material,response,image);
            var support=Piece("FixedSupport",Geometry.Rectangle(-.5f,-3,.5f,1),alternative,response,null,true);
            var ground=Piece("FoundationFloor",Geometry.Rectangle(-8,-1,8,0),alternative,response,null,true);
            var plate=Piece("Plate_3x1",Geometry.Rectangle(0,0,3,1),material,response,image);
            var extension=Piece("Plate_2x1",Geometry.Rectangle(0,0,2,1),material,response,image);
            Asset<PieceDefinition>("ImportedMultiMeshFrame",piece=>
            {
                var proposal=ImportService.Propose(AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath));
                if(proposal.sourceMeshes!=2||proposal.draft.profile.holes.Count!=1) throw new InvalidOperationException("Restore the two-mesh FBX source fixture before creating its approved asset.");
                proposal.draft.material=material;proposal.draft.destruction.response=response;proposal.draft.appearance.image=image;
                proposal.draft.profile.outer.points[0]+=new Vector2(.01f,0);proposal.draft.import.manuallyCorrected=true;
                if(!ImportService.Approve(piece,proposal.draft,out var report)) throw new InvalidOperationException(report.ToString());
            });
            AssetDatabase.SaveAssets();
            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)!=null) return ScenePath;
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var root=new GameObject("BOM Reference Level");var level=root.AddComponent<LevelAuthoring>();level.testPresets.AddRange(new[] {bite,split,removal});
            PiecePlacement Place(string name,PieceDefinition piece,Vector2 position)
            {var go=new GameObject(name);go.transform.SetParent(root.transform,false);go.transform.position=position;var placement=go.AddComponent<PiecePlacement>();placement.piece=piece;return placement;}
            var floor=Place("Fixed floor",ground,new Vector2(0,-3));var wall=Place("Fixed support",support,Vector2.zero);
            Place("Frame with enclosed hole",frame,new Vector2(-5,0));Place("Concave L",concave,new Vector2(3,-2.5f));
            var a=Place("Connected plate A",plate,new Vector2(.5f,0));var b=Place("Connected plate B",extension,new Vector2(3.5f,0));
            b.overrideMaterial=true;b.material=alternative;b.overrideGrab=true;b.grabEligible=false;
            level.foundations.AddRange(new[] {floor,wall});
            void Connect(PiecePlacement first,PiecePlacement second)
            {
                if(!ConnectionTools.TryPropose(first,second,false,out var proposal,out var error)) throw new InvalidOperationException(error);
                proposal.strength=10000;var go=new GameObject("Bond "+first.name+" to "+second.name);go.transform.SetParent(root.transform,false);
                if(!ConnectionTools.TrySet(go.AddComponent<ConnectionAuthoring>(),proposal,out error)) throw new InvalidOperationException(error);
            }
            Connect(wall,a);Connect(a,b);
            var cameraObject=new GameObject("BOM test camera");cameraObject.tag="MainCamera";var camera=cameraObject.AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=6;camera.transform.position=new Vector3(0,0,-20);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.085f,.12f);
            if(!LevelCompiler.TryCompile(level,out var resolved,out var validation)) throw new InvalidOperationException(validation.ToString());
            PrefabUtility.SaveAsPrefabAsset(root,AssemblyPath);EditorSceneManager.SaveScene(scene,ScenePath);
            File.WriteAllText(WorldPath,SnapshotCodec.Serialize(resolved.Read(),true));AssetDatabase.ImportAsset(WorldPath);
            Selection.activeGameObject=root;
            return ScenePath;
        }
    }
}
