using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BOM.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BOM.Tests
{
    [Category("AuthoringUsability")]
    public sealed class AuthoringUsabilityTests
    {
        private readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        private UnityEngine.Object[] selection;
        private Scene scene;
        private string folder;
        private readonly Dictionary<string,string> preferences=new Dictionary<string,string>();
        private T Own<T>(T item) where T:UnityEngine.Object {owned.Add(item);return item;}
        [SetUp] public void Setup()
        {foreach(string key in new[]{"BOM.Authoring.Material","BOM.Authoring.Response","BOM.Authoring.Folder"}){preferences[key]=EditorPrefs.HasKey(key)?EditorPrefs.GetString(key):null;EditorPrefs.DeleteKey(key);}
            selection=Selection.objects;scene=EditorSceneManager.NewPreviewScene();folder="Assets/BOM/Tests/UX-Temporary-"+Guid.NewGuid().ToString("N");AssetDatabase.CreateFolder("Assets/BOM/Tests",folder.Substring("Assets/BOM/Tests/".Length));}
        [TearDown] public void Cleanup()
        {
            foreach(var item in owned.AsEnumerable().Reverse())if(item!=null){Undo.ClearUndo(item);if(item is EditorWindow window){window.DiscardChanges();window.Close();}else UnityEngine.Object.DestroyImmediate(item);}
            owned.Clear();if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.ClosePreviewScene(scene);
            if(AssetDatabase.IsValidFolder(folder))AssetDatabase.DeleteAsset(folder);Selection.objects=selection;
            foreach(var pair in preferences){if(pair.Value==null)EditorPrefs.DeleteKey(pair.Key);else EditorPrefs.SetString(pair.Key,pair.Value);}preferences.Clear();
        }
        private static void Set(BomAuthoringWindow w,string field,object value) => typeof(BomAuthoringWindow).GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(w,value);
        private static T Get<T>(BomAuthoringWindow w,string field) => (T)typeof(BomAuthoringWindow).GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(w);
        private static object Act(BomAuthoringWindow w,string method,params object[] args) => typeof(BomAuthoringWindow).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(w,args);
        private static Button Find(BomAuthoringWindow w,string label) => w.rootVisualElement.Query<Button>().ToList().Single(b=>b.text==label);
        private static void Click(BomAuthoringWindow w,string label)
        {var button=Find(w,label);Assert.That(button.enabledInHierarchy,Is.True,label);using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}}
        private BomAuthoringWindow Window(PieceDefinition piece=null,int tab=0,LevelAuthoring level=null)
        {
            var w=Own(ScriptableObject.CreateInstance<BomAuthoringWindow>());
            Set(w,"tab",tab);Set(w,"level",level);
            if(piece!=null){Set(w,"editing",piece);Set(w,"draft",piece.Draft());}
            w.position=new Rect(100,100,680,850);w.Show();w.rootVisualElement.Clear();w.CreateGUI();return w;
        }
        private PieceDefinition Piece(bool hole=false)
        {
            var p=Own(ScriptableObject.CreateInstance<PieceDefinition>());var material=Own(ScriptableObject.CreateInstance<MaterialDefinition>());
            var response=Own(ScriptableObject.CreateInstance<DestructionResponse>());
            var region=Geometry.Rectangle(0,0,4,4);if(hole)region.holes.Add(Geometry.Rectangle(1,1,2,2).outer);
            Assert.That(p.TryApprove(new PieceDraft {profile=region,material=material,destruction=new DestructionSelection {response=response}},out var report),Is.True,report.ToString());return p;
        }
        private LevelAuthoring Level()
        {var go=Own(new GameObject("Beginner test level"));SceneManager.MoveGameObjectToScene(go,scene);return go.AddComponent<LevelAuthoring>();}
        private static BomProfileEditor Canvas(BomAuthoringWindow w) => w.rootVisualElement.Q<BomProfileEditor>("profileEditor");
        private static void Pointer(BomProfileEditor editor,EventType type,Vector2 local,int clicks=1)
        {
            var source=new Event {type=type,mousePosition=editor.LocalToWorld(local),button=0,clickCount=clicks};
            if(type==EventType.MouseDown){using(var e=PointerDownEvent.GetPooled(source)){e.target=editor;editor.SendEvent(e);}}
            else if(type==EventType.MouseUp){using(var e=PointerUpEvent.GetPooled(source)){e.target=editor;editor.SendEvent(e);}}
            else {using(var e=PointerMoveEvent.GetPooled(source)){e.target=editor;editor.SendEvent(e);}}
        }
        private static void Key(BomProfileEditor editor,KeyCode key)
        {using(var e=KeyDownEvent.GetPooled('\0',key,EventModifiers.None)){e.target=editor;editor.SendEvent(e);}}
        private static void Drag(BomProfileEditor editor,Vector2 from,Vector2 to)
        {Assert.That(editor.contentRect.width,Is.GreaterThan(100));Pointer(editor,EventType.MouseDown,editor.ProfileToCanvas(from));Pointer(editor,EventType.MouseDrag,editor.ProfileToCanvas(to));Pointer(editor,EventType.MouseUp,editor.ProfileToCanvas(to));}
        private static Foldout Coordinates(BomAuthoringWindow w,int ring=0)
        {return w.rootVisualElement.Query<Foldout>().ToList().Single(f=>f.text==(ring==0?"Outer contour coordinates":"Hole "+ring+" coordinates"));}
        private static void OpenCoordinates(BomAuthoringWindow w,int ring=0)
        {w.rootVisualElement.Query<Foldout>().ToList().Single(f=>f.text.StartsWith("Advanced:")).value=true;Coordinates(w,ring).value=true;}
        private static Vector2Field[] CoordinateFields(BomAuthoringWindow w,int ring=0) => Coordinates(w,ring).Query<Vector2Field>().ToList().ToArray();
        private static void AssertCoordinates(BomAuthoringWindow w,Contour contour,int ring=0)
        {
            var fields=CoordinateFields(w,ring);Assert.That(fields,Has.Length.EqualTo(contour.points.Count));
            for(int i=0;i<fields.Length;i++){Assert.That(fields[i].label,Is.EqualTo("Vertex "+i));Assert.That(Vector2.Distance(fields[i].value,contour.points[i]),Is.LessThan(.0001f),"Vertex "+i);}
        }
        private BomAuthoringWindow FailedInitialProjection()
        {
            var w=Window();var model=AssetDatabase.LoadAssetAtPath<GameObject>(FixtureBuilder.Folder+"/Source/EdgeOnPlate.fbx");Assert.That(model,Is.Not.Null);
            var proposal=ImportService.Propose(model);Assert.That(proposal.report.issues.Any(i=>i.field=="Projection"&&i.explanation.Contains("No occupied area")),Is.True,proposal.report.ToString());
            w.rootVisualElement.Query<ObjectField>().ToList().Single(f=>f.label=="Model (FBX)").value=model;
            Assert.That(Get<PieceDraft>(w,"draft").profile.outer.points,Is.Empty);Assert.That(Get<PieceDefinition>(w,"editing"),Is.Null);
            Assert.That(Get<bool>(w,"conversionDirty"),Is.True);return w;
        }

        [Test,Category("ImportUX")] public void SizeControlsPreserveTextureCoordinatesAndLeaveApprovedDataUntouched()
        {
            var piece=Piece(true);var before=JsonUtility.ToJson(piece.Draft());var w=Window(piece);
            w.rootVisualElement.Q<FloatField>("targetWidth").value=8;
            var resized=Get<PieceDraft>(w,"draft");Assert.That(Geometry.Area(resized.profile),Is.EqualTo(60).Within(.0001));
            Assert.That(resized.profile.holes[0].points.Min(p=>p.x),Is.EqualTo(2));Assert.That(resized.profile.holes[0].points.Max(p=>p.y),Is.EqualTo(4));
            Assert.That(Vector2.Dot(resized.appearance.u,new Vector2(8,8)),Is.EqualTo(4));
            Assert.That(resized.thickness,Is.EqualTo(.25f));Assert.That(resized.import.projectedScale,Is.EqualTo(new Vector2(2,2)));
            Set(w,"lockProportions",false);w.rootVisualElement.Q<FloatField>("targetHeight").value=3;
            Assert.That(Get<PieceDraft>(w,"draft").profile.outer.points.Max(p=>p.y),Is.EqualTo(3));
            Assert.That(w.rootVisualElement.Q<Label>("dimensions").text,Does.Contain("8 × 3 m"));
            Assert.That(JsonUtility.ToJson(piece.Draft()),Is.EqualTo(before));
        }
        [Test,Category("ImportUX")] public void ResizedImportedOutlineMatchesExplicitReprojection()
        {
            var w=Window();Act(w,"SelectSource",AssetDatabase.LoadAssetAtPath<GameObject>(FixtureBuilder.SourcePath));
            w.rootVisualElement.Q<FloatField>("targetWidth").value=6;
            var resized=Get<PieceDraft>(w,"draft");Assert.That(resized.import.manuallyCorrected,Is.False);
            var regenerated=ImportService.Propose(Get<GameObject>(w,"source"),settings:resized.import).draft;
            Assert.That(Geometry.Area(regenerated.profile),Is.EqualTo(Geometry.Area(resized.profile)).Within(.0001));
            Assert.That(regenerated.profile.outer.points.Max(p=>p.x)-regenerated.profile.outer.points.Min(p=>p.x),Is.EqualTo(6).Within(.0001));
        }
        [Test,Category("ImportUX")] public void ProjectionThicknessUsesChosenDepthAxisAndSourceOrientation()
        {
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(FixtureBuilder.Folder+"/Source/EdgeOnPlate.fbx");
            var side=ImportService.Propose(model,settings:new ImportSettings {plane=ProjectionPlane.YZ});
            Assert.That(Geometry.TryValidate(side.draft.profile,out var error),Is.True,error);
            Assert.That(side.draft.thickness,Is.EqualTo(.05f).Within(.0001));
            var rotated=ImportService.Propose(model,settings:new ImportSettings {orientation=new Vector3(0,90,0)});
            Assert.That(Geometry.TryValidate(rotated.draft.profile,out error),Is.True,error);
            Assert.That(rotated.draft.thickness,Is.EqualTo(.05f).Within(.0001));
        }
        [Test,Category("ImportUX")] public void ImportedRootUnitScaleIsPreservedAndSourceTranslationDoesNotMoveTheOutline()
        {
            var root=Own(new GameObject("Centimetre source"));root.transform.position=new Vector3(10,20,30);root.transform.localScale=Vector3.one*.01f;
            var child=new GameObject("Offset mesh");child.transform.SetParent(root.transform,false);child.transform.localPosition=new Vector3(100,0,0);
            var filter=child.AddComponent<MeshFilter>();
            var projected=ImportService.ToAuthoring(ImportService.MeshToSource(root,filter).MultiplyPoint3x4(new Vector3(100,50,0)),new ImportSettings());
            Assert.That(Vector3.Distance(projected,new Vector3(2,.5f,0)),Is.LessThan(.0001));
        }
        [UnityTest,Category("ImportUX")] public IEnumerator OriginalModelRemainsVisibleWhenOutlineIsInvalidAndViewSwitchKeepsSelection()
        {
            var w=Window();Act(w,"SelectSource",AssetDatabase.LoadAssetAtPath<GameObject>(FixtureBuilder.SourcePath));yield return null;yield return null;
            var draft=Get<PieceDraft>(w,"draft");var editor=Canvas(w);editor.SelectProfilePoint(draft.profile.outer.points[0],.01f);
            int selected=editor.SelectedVertex;Click(w,"Result");Assert.That(Canvas(w),Is.SameAs(editor));Assert.That(Canvas(w).SelectedVertex,Is.EqualTo(selected));Click(w,"Source + outline");
            draft.profile.outer.points[1]=draft.profile.outer.points[0];draft.profile.outer.points[2]=draft.profile.outer.points[0];
            Assert.That(Geometry.TryValidate(draft.profile,out _),Is.False);Set(w,"nextPreview",0d);Act(w,"Tick");
            var preview=Get<BomModelPreview>(w,"modelPreview");Assert.That(preview,Is.Not.Null);Assert.That(preview.HasSource,Is.True);
            Assert.That(w.rootVisualElement.Q<Image>("modelPreview").image,Is.Not.Null);
        }
        [UnityTest,Category("ImportUX")] public IEnumerator ClickingDisconnectedRegionKeepsThatRegionWithoutChangingSavedPiece()
        {
            var piece=Piece();var before=JsonUtility.ToJson(piece.Draft());var w=Window(piece);var draft=Get<PieceDraft>(w,"draft");
            draft.disconnectedProposal.Add(Geometry.Rectangle(6,0,8,2));Act(w,"Build");yield return null;yield return null;
            var editor=Canvas(w);Pointer(editor,EventType.MouseDown,editor.ProfileToCanvas(new Vector2(7,1)));Pointer(editor,EventType.MouseUp,editor.ProfileToCanvas(new Vector2(7,1)));
            Assert.That(editor.SelectedRegion,Is.EqualTo(1));Click(w,"Keep selected region");
            Assert.That(Get<PieceDraft>(w,"draft").profile.outer.points.Min(p=>p.x),Is.EqualTo(6));
            Assert.That(Get<PieceDraft>(w,"draft").disconnectedProposal,Is.Empty);Assert.That(JsonUtility.ToJson(piece.Draft()),Is.EqualTo(before));
        }
        [UnityTest,Category("ImportUX")] public IEnumerator InlineMaterialEditingUpdatesSharedDefinitionAndSupportsUndo()
        {
            var w=Window(Piece());Act(w,"CreateMaterial",folder+"/InlineMaterial.asset");yield return null;yield return null;
            var material=Get<PieceDraft>(w,"draft").material;var property=w.rootVisualElement.Q<PropertyField>("physics-density");
            var field=property.Q<FloatField>();Assert.That(field,Is.Not.Null);
            Undo.IncrementCurrentGroup();field.value=7;yield return null;yield return null;
            Assert.That(material.density,Is.EqualTo(7));Act(w,"UpdateValidation");Assert.That(w.rootVisualElement.Q<Label>("dimensions").text,Does.Contain("112 kg"));
            Undo.PerformUndo();yield return null;Assert.That(material.density,Is.EqualTo(2));
        }
        [Test,Category("ImportUX")] public void DetachedSizeFieldCannotResizeANewImport()
        {
            var w=Window(Piece());var old=w.rootVisualElement.Q<FloatField>("targetWidth");
            Act(w,"SelectSource",AssetDatabase.LoadAssetAtPath<GameObject>(FixtureBuilder.SourcePath));
            var before=JsonUtility.ToJson(Get<PieceDraft>(w,"draft"));old.value=50;
            Assert.That(JsonUtility.ToJson(Get<PieceDraft>(w,"draft")),Is.EqualTo(before));
        }

        [UnityTest,Category("V3Review")] public IEnumerator CanvasDeletionSynchronizesAdvancedCoordinatesAndTargetsCurrentVertex()
        {
            var piece=Piece();var before=JsonUtility.ToJson(piece.Draft());var w=Window(piece);yield return null;yield return null;
            OpenCoordinates(w);yield return null;var editor=Canvas(w);var draft=Get<PieceDraft>(w,"draft");var obsolete=CoordinateFields(w)[3];
            var corner=draft.profile.outer.points[0];Pointer(editor,EventType.MouseDown,editor.ProfileToCanvas(corner));Pointer(editor,EventType.MouseUp,editor.ProfileToCanvas(corner));Key(editor,KeyCode.Delete);
            AssertCoordinates(w,draft.profile.outer);Assert.That(draft.profile.outer.points,Has.Count.EqualTo(3));Assert.That(Coordinates(w).value,Is.True);Assert.That(Canvas(w),Is.SameAs(editor));
            var target=draft.profile.outer.points[2]+new Vector2(-.1f,.1f);CoordinateFields(w)[2].value=target;Assert.That(draft.profile.outer.points[2],Is.EqualTo(target));
            var current=draft.profile.outer.points.ToArray();Assert.DoesNotThrow(()=>obsolete.value=new Vector2(99,99));Assert.That(draft.profile.outer.points,Is.EqualTo(current));
            Assert.That(JsonUtility.ToJson(piece.Draft()),Is.EqualTo(before));
        }
        [UnityTest,Category("V3Review")] public IEnumerator CanvasInsertionSynchronizesAdvancedCoordinatesAfterCaptureWithoutRebuildingCanvas()
        {
            var piece=Piece();var before=JsonUtility.ToJson(piece.Draft());var w=Window(piece);yield return null;yield return null;OpenCoordinates(w);yield return null;
            var editor=Canvas(w);editor.Snap=true;editor.GridSize=.25f;var draft=Get<PieceDraft>(w,"draft");var fold=Coordinates(w);var view=editor.ProfileToCanvas(Vector2.zero);
            var mid=(draft.profile.outer.points[0]+draft.profile.outer.points[1])*.5f;
            Pointer(editor,EventType.MouseDown,editor.ProfileToCanvas(mid),2);Assert.That(editor.IsDragging,Is.True);Assert.That(Coordinates(w),Is.SameAs(fold));
            var moved=mid+new Vector2(0,-.25f);Pointer(editor,EventType.MouseDrag,editor.ProfileToCanvas(moved));Assert.That(editor.IsDragging,Is.True);
            Pointer(editor,EventType.MouseUp,editor.ProfileToCanvas(moved));Assert.That(editor.IsDragging,Is.False);AssertCoordinates(w,draft.profile.outer);
            Assert.That(Coordinates(w).value,Is.True);Assert.That(Canvas(w),Is.SameAs(editor));Assert.That(editor.ProfileToCanvas(Vector2.zero),Is.EqualTo(view));Assert.That(editor.Snap,Is.True);
            Assert.That(editor.SelectedVertex,Is.EqualTo(1));var target=moved+new Vector2(0,-.1f);CoordinateFields(w)[1].value=target;
            Assert.That(draft.profile.outer.points[1],Is.EqualTo(target));Assert.That(editor.SelectedPoint,Is.EqualTo(target));Assert.That(JsonUtility.ToJson(piece.Draft()),Is.EqualTo(before));
        }
        [UnityTest,Category("V3Review")] public IEnumerator CanvasDraggingSynchronizesOuterAndHoleCoordinatesDuringCapture()
        {
            var piece=Piece(true);var before=JsonUtility.ToJson(piece.Draft());var w=Window(piece);yield return null;yield return null;OpenCoordinates(w);OpenCoordinates(w,1);yield return null;
            var editor=Canvas(w);var draft=Get<PieceDraft>(w,"draft");
            for(int ring=0;ring<2;ring++)
            {
                var contour=ring==0?draft.profile.outer:draft.profile.holes[0];var field=CoordinateFields(w,ring)[0];var from=contour.points[0];var to=from+new Vector2(-.1f,.1f);
                Pointer(editor,EventType.MouseDown,editor.ProfileToCanvas(from));Pointer(editor,EventType.MouseDrag,editor.ProfileToCanvas(to));
                Assert.That(editor.IsDragging,Is.True);Assert.That(CoordinateFields(w,ring)[0],Is.SameAs(field));Assert.That(Vector2.Distance(field.value,to),Is.LessThan(.0001f));AssertCoordinates(w,contour,ring);
                Pointer(editor,EventType.MouseUp,editor.ProfileToCanvas(to));var exact=to+new Vector2(.02f,.03f);field.value=exact;
                Assert.That(contour.points[0],Is.EqualTo(exact));Assert.That(editor.SelectedPoint,Is.EqualTo(exact));
            }
            Assert.That(Canvas(w),Is.SameAs(editor));Assert.That(JsonUtility.ToJson(piece.Draft()),Is.EqualTo(before));
        }
        [UnityTest,Category("V3Review")] public IEnumerator CanvasEscapeRebindsAdvancedCoordinatesToRestoredProfile()
        {
            var piece=Piece(true);var before=JsonUtility.ToJson(piece.Draft());var w=Window(piece);yield return null;yield return null;OpenCoordinates(w);OpenCoordinates(w,1);yield return null;
            var editor=Canvas(w);var draft=Get<PieceDraft>(w,"draft");var discarded=draft.profile;var from=discarded.holes[0].points[0];var oldField=CoordinateFields(w,1)[0];
            Pointer(editor,EventType.MouseDown,editor.ProfileToCanvas(from));Pointer(editor,EventType.MouseDrag,editor.ProfileToCanvas(from+new Vector2(-.1f,.1f)));
            var moved=discarded.holes[0].points[0];Key(editor,KeyCode.Escape);Pointer(editor,EventType.MouseUp,editor.ProfileToCanvas(from));
            Assert.That(draft.profile,Is.Not.SameAs(discarded));AssertCoordinates(w,draft.profile.outer);AssertCoordinates(w,draft.profile.holes[0],1);
            Assert.That(Get<bool>(w,"draftEdited"),Is.False);Assert.That(Coordinates(w,1).value,Is.True);Assert.That(Canvas(w),Is.SameAs(editor));
            var target=from+new Vector2(.05f,.05f);CoordinateFields(w,1)[0].value=target;
            Assert.That(draft.profile.holes[0].points[0],Is.EqualTo(target));Assert.That(discarded.holes[0].points[0],Is.EqualTo(moved));Assert.That(Get<bool>(w,"draftEdited"),Is.True);
            oldField.value=new Vector2(99,99);Assert.That(draft.profile.holes[0].points[0],Is.EqualTo(target));Assert.That(discarded.holes[0].points[0],Is.EqualTo(moved));
            Assert.That(JsonUtility.ToJson(piece.Draft()),Is.EqualTo(before));
        }
        [Test,Category("V3Review")] public void FailedInitialProjectionRetainsExplicitSelectionsAndAppearance()
        {
            var approved=Piece();var before=JsonUtility.ToJson(approved.Draft());var w=FailedInitialProjection();
            Act(w,"CreateMaterial",folder+"/RepairMaterial.asset");Act(w,"CreateResponse",folder+"/RepairResponse.asset");var pending=Get<PieceDraft>(w,"draft");var material=pending.material;var response=pending.destruction.response;
            w.rootVisualElement.Query<FloatField>().ToList().Single(f=>f.label=="Impulse multiplier").value=3.25f;
            w.rootVisualElement.Query<Toggle>().ToList().Single(f=>f.label=="Grabbable").value=false;
            w.rootVisualElement.Query<FloatField>().ToList().Single(f=>f.label=="Thickness (m)").value=.45f;
            w.rootVisualElement.Query<ColorField>().ToList().Single(f=>f.label=="Face").value=Color.magenta;
            w.rootVisualElement.Query<ColorField>().ToList().Single(f=>f.label=="Sides").value=Color.black;
            w.rootVisualElement.Query<ColorField>().ToList().Single(f=>f.label=="Cuts").value=Color.yellow;
            var image=Own(new Texture2D(2,2));image.Apply();w.rootVisualElement.Query<ObjectField>().ToList().Single(f=>f.label=="Face texture").value=image;
            w.rootVisualElement.Query<Vector2Field>().ToList().Single(f=>f.label=="Face U vector").value=new Vector2(2,.5f);
            w.rootVisualElement.Query<Vector2Field>().ToList().Single(f=>f.label=="Face V vector").value=new Vector2(-.5f,3);
            w.rootVisualElement.Query<Vector2Field>().ToList().Single(f=>f.label=="UV offset").value=new Vector2(.1f,.3f);
            w.rootVisualElement.Query<EnumField>().ToList().Single(f=>f.label=="Image wrap").value=TextureWrapMode.Clamp;
            w.rootVisualElement.Query<EnumField>().ToList().Single(f=>f.label=="Image filtering").value=FilterMode.Point;
            var appearance=JsonUtility.ToJson(pending.appearance);
            w.rootVisualElement.Query<EnumField>().ToList().Single(f=>f.label=="Projection plane").value=ProjectionPlane.YZ;
            var updated=Get<PieceDraft>(w,"draft");Assert.That(Geometry.TryValidate(updated.profile,out var error),Is.True,error);
            Assert.That(updated.material,Is.SameAs(material));Assert.That(updated.destruction.response,Is.SameAs(response));Assert.That(updated.destruction.mode,Is.EqualTo(DestructionMode.Subtract));
            Assert.That(updated.destruction.impulseMultiplier,Is.EqualTo(3.25f));Assert.That(updated.grabEligible,Is.False);Assert.That(updated.thickness,Is.EqualTo(.45f));
            Assert.That(JsonUtility.ToJson(updated.appearance),Is.EqualTo(appearance));Assert.That(Get<bool>(w,"conversionDirty"),Is.False);Assert.That(Get<PieceDefinition>(w,"editing"),Is.Null);
            Assert.That(Find(w,"Save piece").enabledSelf,Is.True);Assert.That(JsonUtility.ToJson(approved.Draft()),Is.EqualTo(before));
        }
        [Test,Category("V3Review")] public void FailedInitialProjectionInfersUntouchedAppearanceAfterRepair()
        {
            var w=FailedInitialProjection();var settings=Get<PieceDraft>(w,"draft").import.Clone();settings.plane=ProjectionPlane.YZ;
            var expected=ImportService.Propose(Get<GameObject>(w,"source"),settings:settings).draft;
            w.rootVisualElement.Query<EnumField>().ToList().Single(f=>f.label=="Projection plane").value=ProjectionPlane.YZ;
            var updated=Get<PieceDraft>(w,"draft");Assert.That(Geometry.TryValidate(updated.profile,out var error),Is.True,error);
            Assert.That(JsonUtility.ToJson(updated.appearance),Is.EqualTo(JsonUtility.ToJson(expected.appearance)));Assert.That(updated.thickness,Is.EqualTo(expected.thickness));
            Assert.That(updated.appearance.u,Is.Not.EqualTo(Vector2.right));Assert.That(Find(w,"Save piece").enabledSelf,Is.False);
        }
        [Test,Category("V3Review")] public void FailedInitialProjectionRetainsColourWithoutSuppressingFirstUvInference()
        {
            var w=FailedInitialProjection();Act(w,"CreateMaterial",folder+"/PartialMaterial.asset");Act(w,"CreateResponse",folder+"/PartialResponse.asset");
            var pending=Get<PieceDraft>(w,"draft");var material=pending.material;var response=pending.destruction.response;
            w.rootVisualElement.Query<ColorField>().ToList().Single(f=>f.label=="Face").value=Color.magenta;
            var settings=pending.import.Clone();settings.plane=ProjectionPlane.YZ;var expected=ImportService.Propose(Get<GameObject>(w,"source"),settings:settings).draft;
            w.rootVisualElement.Query<EnumField>().ToList().Single(f=>f.label=="Projection plane").value=ProjectionPlane.YZ;
            var updated=Get<PieceDraft>(w,"draft");Assert.That(updated.material,Is.SameAs(material));Assert.That(updated.destruction.response,Is.SameAs(response));Assert.That(updated.appearance.face,Is.EqualTo(Color.magenta));
            Assert.That(updated.appearance.u,Is.EqualTo(expected.appearance.u));Assert.That(updated.appearance.v,Is.EqualTo(expected.appearance.v));Assert.That(updated.appearance.offset,Is.EqualTo(expected.appearance.offset));
            Assert.That(updated.thickness,Is.EqualTo(expected.thickness));Assert.That(AuthoringValidation.Piece(updated).IsValid,Is.True);
        }
        [UnityTest,Category("V3Review")] public IEnumerator ClosedWorkspaceCannotAllocatePreviewFromLateUpdateCallback()
        {
            var w=Window(Piece());yield return null;yield return null;Set(w,"nextPreview",0d);Act(w,"Tick");
            var utility=Get<PreviewRenderUtility>(w,"preview");Assert.That(utility,Is.Not.Null);var camera=utility.camera;
            w.Close();Assert.That(camera==null,Is.True);Set(w,"nextPreview",0d);Act(w,"Tick");
            bool recreated=Get<PreviewRenderUtility>(w,"preview")!=null;
            if(recreated)Act(w,"OnDisable");
            Assert.That(recreated,Is.False,"A late callback must not recreate an unowned preview after the window closes.");
        }

        [UnityTest] public IEnumerator OuterPointDragEditsOnlyDraftAndCanUndoRedoAndCancelProposal()
        {
            var piece=Piece();var before=JsonUtility.ToJson(piece.Draft());var w=Window(piece);yield return null;yield return null;
            var editor=Canvas(w);var draft=Get<PieceDraft>(w,"draft");var from=draft.profile.outer.points[0];var to=from+new Vector2(-.3f,.2f);
            Undo.IncrementCurrentGroup();Drag(editor,from,to);
            Assert.That(Vector2.Distance(draft.profile.outer.points[0],to),Is.LessThan(.0001f));Assert.That(JsonUtility.ToJson(piece.Draft()),Is.EqualTo(before));
            Undo.PerformUndo();yield return null;Assert.That(Get<PieceDraft>(w,"draft").profile.outer.points[0],Is.EqualTo(from));
            Undo.PerformRedo();yield return null;Assert.That(Vector2.Distance(Get<PieceDraft>(w,"draft").profile.outer.points[0],to),Is.LessThan(.0001f));
            Click(w,"Discard edits");Assert.That(JsonUtility.ToJson(Get<PieceDraft>(w,"draft")),Is.EqualTo(before));Assert.That(JsonUtility.ToJson(piece.Draft()),Is.EqualTo(before));
        }
        [UnityTest] public IEnumerator HolePointDragDoesNotChangeOuterOrApprovedAsset()
        {
            var piece=Piece(true);var w=Window(piece);yield return null;yield return null;
            var draft=Get<PieceDraft>(w,"draft");var outer=draft.profile.outer.points.ToArray();var from=draft.profile.holes[0].points[0];var to=from+new Vector2(.1f,.1f);
            Drag(Canvas(w),from,to);Assert.That(Vector2.Distance(draft.profile.holes[0].points[0],to),Is.LessThan(.0001f));
            Assert.That(draft.profile.outer.points,Is.EqualTo(outer));Assert.That(piece.Draft().profile.holes[0].points[0],Is.EqualTo(from));
            Assert.That(Geometry.TryValidate(draft.profile,out var error),Is.True,error);
        }
        [UnityTest] public IEnumerator EscapeRestoresWholeContourIncludingTagsAndCorrectionFlag()
        {
            var w=Window(Piece());yield return null;yield return null;
            var draft=Get<PieceDraft>(w,"draft");draft.profile.outer.cutEdges=new List<bool> {true,false,false,true};var before=JsonUtility.ToJson(draft.profile);
            var editor=Canvas(w);var from=draft.profile.outer.points[0];Pointer(editor,EventType.MouseDown,editor.ProfileToCanvas(from));
            Pointer(editor,EventType.MouseDrag,editor.ProfileToCanvas(from+new Vector2(-.2f,.1f)));Key(editor,KeyCode.Escape);
            Pointer(editor,EventType.MouseUp,editor.ProfileToCanvas(from));Assert.That(JsonUtility.ToJson(draft.profile),Is.EqualTo(before));
            Assert.That(draft.import.manuallyCorrected,Is.False);Assert.That(Get<bool>(w,"draftEdited"),Is.False);Assert.That(editor.IsDragging,Is.False);
        }
        [UnityTest] public IEnumerator DoubleClickEdgeInsertsPointAndDeleteRemovesIt()
        {
            var w=Window(Piece());yield return null;yield return null;var editor=Canvas(w);var draft=Get<PieceDraft>(w,"draft");
            var mid=(draft.profile.outer.points[0]+draft.profile.outer.points[1])*.5f;
            Pointer(editor,EventType.MouseDown,editor.ProfileToCanvas(mid),2);Pointer(editor,EventType.MouseUp,editor.ProfileToCanvas(mid),2);
            Assert.That(draft.profile.outer.points,Has.Count.EqualTo(5));Assert.That(Vector2.Distance(editor.SelectedPoint,mid),Is.LessThan(.0001f));
            Key(editor,KeyCode.Delete);Assert.That(draft.profile.outer.points,Has.Count.EqualTo(4));Assert.That(Geometry.TryValidate(draft.profile,out _),Is.True);
        }
        [UnityTest] public IEnumerator InvalidDragBlocksApprovalAndUndoRestoresReadyState()
        {
            var piece=Piece();var w=Window(piece);yield return null;yield return null;var draft=Get<PieceDraft>(w,"draft");
            Undo.IncrementCurrentGroup();Drag(Canvas(w),draft.profile.outer.points[0],new Vector2(4.5f,2));
            Assert.That(Geometry.TryValidate(draft.profile,out _),Is.False);Assert.That(Find(w,"Save piece").enabledSelf,Is.False);
            Assert.That(Get<bool>(w,"draftEdited"),Is.True);Assert.That(piece.Version,Is.EqualTo(1));
            Undo.PerformUndo();yield return null;Assert.That(Find(w,"Save piece").enabledSelf,Is.True);
        }
        [Test] public void AddHoleChoosesAnActuallySolidLocationInConcaveOrHoledShape()
        {
            var w=Window(Piece(true));Click(w,"Add hole");var draft=Get<PieceDraft>(w,"draft");
            Assert.That(draft.profile.holes,Has.Count.EqualTo(2));Assert.That(Geometry.TryValidate(draft.profile,out var error),Is.True,error);
            Assert.That(Canvas(w).SelectedRing,Is.EqualTo(2));Click(w,"Delete hole");Assert.That(Get<PieceDraft>(w,"draft").profile.holes,Has.Count.EqualTo(1));
        }
        [Test] public void MissingInputsDisableActionsAndKeepAdvancedDetailsCollapsed()
        {
            var w=Window();Assert.That(Find(w,"Rebuild outline").enabledSelf,Is.False);
            Set(w,"tab",1);Act(w,"Build");Assert.That(Find(w,"Place at view centre").enabledSelf,Is.False);
            Set(w,"tab",3);Act(w,"Build");Assert.That(Find(w,"Enter Play mode").enabledSelf,Is.False);Assert.That(Find(w,"Run explosion").enabledSelf,Is.False);
            Assert.That(w.rootVisualElement.Query<IntegerField>().ToList().Any(f=>f.label=="Captured preset index"),Is.False);
            Assert.That(w.rootVisualElement.Query<Foldout>().ToList().All(f=>!f.value),Is.True);
        }
        [Test] public void ChangingConversionOfCorrectedOutlineCannotApproveStaleGeometry()
        {
            var piece=Piece();var w=Window(piece);var draft=Get<PieceDraft>(w,"draft");draft.import.manuallyCorrected=true;var old=JsonUtility.ToJson(draft.profile);
            w.rootVisualElement.Query<EnumField>().ToList().Single(f=>f.label=="Projection plane").value=ProjectionPlane.XZ;
            Assert.That(Get<bool>(w,"conversionDirty"),Is.True);Assert.That(JsonUtility.ToJson(draft.profile),Is.EqualTo(old));
            Assert.That(Find(w,"Save piece").enabledSelf,Is.False);
            Assert.That((bool)Act(w,"Approve",folder+"/MustNotSave.asset"),Is.False);Assert.That(AssetDatabase.LoadMainAssetAtPath(folder+"/MustNotSave.asset"),Is.Null);
        }
        [Test] public void SourceConversionRefreshPreservesPendingMaterialResponseAndAppearance()
        {
            var w=Window();var model=AssetDatabase.LoadAssetAtPath<GameObject>(FixtureBuilder.Folder+"/Source/MultiMeshFrame.fbx");Act(w,"SelectSource",model);
            Act(w,"CreateMaterial",folder+"/Material.asset");Act(w,"CreateResponse",folder+"/Response.asset");
            var draft=Get<PieceDraft>(w,"draft");var material=draft.material;var response=draft.destruction.response;draft.appearance.face=Color.magenta;double area=Geometry.Area(draft.profile);
            w.rootVisualElement.Query<FloatField>().ToList().Single(f=>f.label=="Source scale (metres per imported unit)").value=2;
            var updated=Get<PieceDraft>(w,"draft");Assert.That(Geometry.Area(updated.profile),Is.EqualTo(area*4).Within(.001));
            Assert.That(updated.material,Is.SameAs(material));Assert.That(updated.destruction.response,Is.SameAs(response));Assert.That(updated.appearance.face,Is.EqualTo(Color.magenta));Assert.That(Get<bool>(w,"conversionDirty"),Is.False);
        }
        [Test] public void FreshImportCanCreateRequirementsApprovePlaceSetSupportsAndAddNamedPreset()
        {
            var level=Level();var w=Window(null,0,level);var model=AssetDatabase.LoadAssetAtPath<GameObject>(FixtureBuilder.Folder+"/Source/MultiMeshFrame.fbx");
            w.rootVisualElement.Query<ObjectField>().ToList().Single(f=>f.label=="Model (FBX)").value=model;
            Assert.That(Get<PieceDraft>(w,"draft"),Is.Not.Null);Assert.That(Find(w,"Save piece").enabledSelf,Is.False);
            Act(w,"CreateMaterial",folder+"/Material.asset");Act(w,"CreateResponse",folder+"/Response.asset");Assert.That(Find(w,"Save piece").enabledSelf,Is.True);
            Assert.That((bool)Act(w,"Approve",folder+"/Piece.asset"),Is.True);var approved=AssetDatabase.LoadAssetAtPath<PieceDefinition>(folder+"/Piece.asset");Assert.That(approved.IsApproved,Is.True);
            Set(w,"tab",1);Act(w,"Build");Click(w,"Place at view centre");Assert.That(level.GetComponentsInChildren<PiecePlacement>(),Has.Length.EqualTo(1));
            Click(w,"Make selected pieces fixed supports");Assert.That(level.foundations,Has.Count.EqualTo(1));Click(w,"Make selected pieces movable");Assert.That(level.foundations,Is.Empty);
            Set(w,"tab",3);Act(w,"Build");Assert.That(Find(w,"Enter Play mode").enabledSelf,Is.False);
            Act(w,"CreatePreset",folder+"/NamedBlast.asset");var dropdown=w.rootVisualElement.Q<DropdownField>("explosionPreset");Assert.That(dropdown.choices.Single(),Does.Contain("NamedBlast"));
            Assert.That(Find(w,"Enter Play mode").enabledSelf,Is.True);Assert.That(LevelCompiler.TryCompile(level,out var compiled,out var report),Is.True,report.ToString());Assert.That(compiled.Read().bodies,Has.Count.EqualTo(1));
        }
        [Test] public void DefinitionCreationRefusesExistingOrOutsideAssetPaths()
        {
            var first=BomAuthoringAssets.Create<MaterialDefinition>(folder+"/Keep.asset","Keep",out var error);Assert.That(first,Is.Not.Null,error);first.density=7;
            Assert.That(BomAuthoringAssets.Create<MaterialDefinition>(folder+"/Keep.asset","Keep",out error),Is.Null);Assert.That(first.density,Is.EqualTo(7));
            Assert.That(BomAuthoringAssets.NewPath("Assets/../Outside.asset",out _),Is.False);Assert.That(BomAuthoringAssets.NewPath("C:/Outside.asset",out _),Is.False);
        }
        [UnityTest] public IEnumerator DragSnapsThePointAndRetainsUnsavedCloseProtection()
        {
            var w=Window(Piece());yield return null;yield return null;var editor=Canvas(w);editor.Snap=true;editor.GridSize=.25f;
            var from=Get<PieceDraft>(w,"draft").profile.outer.points[0];Drag(editor,from,from+new Vector2(-.36f,.13f));
            Assert.That(Vector2.Distance(editor.SelectedPoint,from+new Vector2(-.25f,.25f)),Is.LessThan(.0001f));Assert.That(w.hasUnsavedChanges,Is.True);
            w.DiscardChanges();Assert.That(w.hasUnsavedChanges,Is.False);Assert.That(Get<PieceDraft>(w,"draft").profile.outer.points[0],Is.EqualTo(from));
        }
        [UnityTest] public IEnumerator BackgroundClickDeselectsSoDeleteCannotRemoveAnOldPoint()
        {
            var w=Window(Piece());yield return null;yield return null;var editor=Canvas(w);var point=Get<PieceDraft>(w,"draft").profile.outer.points[0];
            Pointer(editor,EventType.MouseDown,editor.ProfileToCanvas(point));Pointer(editor,EventType.MouseUp,editor.ProfileToCanvas(point));Assert.That(editor.HasSelection,Is.True);
            Pointer(editor,EventType.MouseDown,editor.ProfileToCanvas(new Vector2(2,2)));Pointer(editor,EventType.MouseUp,editor.ProfileToCanvas(new Vector2(2,2)));Key(editor,KeyCode.Delete);
            Assert.That(editor.HasSelection,Is.False);Assert.That(Get<PieceDraft>(w,"draft").profile.outer.points,Has.Count.EqualTo(4));
        }
    }
}
