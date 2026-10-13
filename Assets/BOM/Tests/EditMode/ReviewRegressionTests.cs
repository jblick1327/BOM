using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BOM.Editor;
using Clipper2Lib;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace BOM.Tests
{
    [Category("ReviewRegression")]
    public sealed class ReviewRegressionTests
    {
        private readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        private Scene previousScene,testScene;
        private UnityEngine.Object[] previousSelection;
        private T Own<T>(T value) where T:UnityEngine.Object {owned.Add(value);return value;}
        [SetUp] public void Setup()
        {
            previousSelection=Selection.objects;previousScene=SceneManager.GetActiveScene();
            testScene=EditorSceneManager.NewPreviewScene();
        }
        [TearDown] public void Cleanup()
        {
            foreach(var value in owned.AsEnumerable().Reverse())
                if(value!=null) {if(value is EditorWindow window) window.Close();else UnityEngine.Object.DestroyImmediate(value);}
            owned.Clear();if(previousScene.IsValid()&&previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if(testScene.IsValid()&&testScene.isLoaded) EditorSceneManager.ClosePreviewScene(testScene);
            Selection.objects=previousSelection;
        }
        private LevelAuthoring Level()
        {
            var root=Own(new GameObject("Review regression level"));SceneManager.MoveGameObjectToScene(root,testScene);
            var level=root.AddComponent<LevelAuthoring>();
            level.testPresets.Add(Own(ScriptableObject.CreateInstance<ExplosionPreset>()));return level;
        }
        private PieceDefinition Piece()
        {
            var piece=Own(ScriptableObject.CreateInstance<PieceDefinition>());
            var material=Own(ScriptableObject.CreateInstance<MaterialDefinition>());
            var response=Own(ScriptableObject.CreateInstance<DestructionResponse>());
            Assert.That(piece.TryApprove(new PieceDraft {profile=Geometry.Rectangle(0,0,1,1),material=material,
                destruction=new DestructionSelection {response=response}},out var report),Is.True,report.ToString());return piece;
        }
        private PiecePlacement Place(LevelAuthoring level,PieceDefinition piece,Vector2 position)
        {
            var go=new GameObject("Review placement");go.transform.SetParent(level.transform,false);go.transform.position=position;
            var placement=go.AddComponent<PiecePlacement>();placement.piece=piece;return placement;
        }
        private static void SetWindowField(BomAuthoringWindow window,string name,object value)
        {typeof(BomAuthoringWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window,value);}
        private static void WindowAction(BomAuthoringWindow window,string name,params object[] args)
        {
            var method=typeof(BomAuthoringWindow).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.That(method,Is.Not.Null,"Missing workspace action "+name);method.Invoke(window,args);
        }
        private BomAuthoringWindow Workspace(LevelAuthoring level)
        {
            var window=Own(ScriptableObject.CreateInstance<BomAuthoringWindow>());
            SetWindowField(window,"level",level);SetWindowField(window,"tab",2);window.Show();
            window.rootVisualElement.Clear();window.CreateGUI();return window;
        }
        private static Button FindButton(BomAuthoringWindow window,string text)
        {return window.rootVisualElement.Query<Button>().ToList().SingleOrDefault(b=>b.text==text);}
        private static void Click(BomAuthoringWindow window,string text)
        {
            var button=FindButton(window,text);Assert.That(button,Is.Not.Null,"Missing workspace button "+text);
            using(var submit=NavigationSubmitEvent.GetPooled()) {submit.target=button;button.SendEvent(submit);}
        }
        private static void SelectPair(BomAuthoringWindow window,PiecePlacement a,PiecePlacement b)
        {Selection.objects=new UnityEngine.Object[] {a.gameObject,b.gameObject};Click(window,"Use two selected pieces");}
        private static ConnectionAuthoring SaveBond(LevelAuthoring level,PiecePlacement a,PiecePlacement b)
        {
            Assert.That(ConnectionTools.TryPropose(a,b,false,out var proposal,out var error),Is.True,error);
            var go=new GameObject("Saved review bond");go.transform.SetParent(level.transform,false);
            var bond=go.AddComponent<ConnectionAuthoring>();Assert.That(ConnectionTools.TrySet(bond,proposal,out error),Is.True,error);return bond;
        }
        [Test] public void R1_WorkspaceCreatesTwoBondsThenExplicitlyEditsOnlyOne()
        {
            var level=Level();var piece=Piece();var a=Place(level,piece,Vector2.zero);var b=Place(level,piece,Vector2.right);
            var c=Place(level,piece,new Vector2(0,3));var d=Place(level,piece,new Vector2(1,3));var window=Workspace(level);
            SelectPair(window,a,b);Click(window,"Propose shared-boundary bond");Click(window,"Confirm connection");
            var first=level.GetComponentsInChildren<ConnectionAuthoring>().Single();string firstBefore=EditorJsonUtility.ToJson(first);
            SelectPair(window,c,d);Click(window,"Propose shared-boundary bond");Click(window,"Confirm connection");
            var bonds=level.GetComponentsInChildren<ConnectionAuthoring>();Assert.That(bonds,Has.Length.EqualTo(2));
            Assert.That(EditorJsonUtility.ToJson(first),Is.EqualTo(firstBefore));var second=bonds.Single(x=>x!=first);
            string secondBefore=EditorJsonUtility.ToJson(second);WindowAction(window,"ReviewConnection",first);
            window.rootVisualElement.Query<FloatField>().ToList().Single(f=>f.label=="Strength per metre").value=321;
            Click(window,"Confirm connection");Assert.That(first.strength,Is.EqualTo(321));
            Assert.That(first.a,Is.SameAs(a));Assert.That(first.b,Is.SameAs(b));
            Assert.That(EditorJsonUtility.ToJson(second),Is.EqualTo(secondBefore));
            Assert.That(LevelCompiler.TryCompile(level,out var resolved,out var report),Is.True,report.ToString());Assert.That(resolved.Read().bonds,Has.Count.EqualTo(2));
        }
        [Test] public void R1_WorkspaceEndpointFieldChangeRemovesStaleProposalControls()
        {
            var level=Level();var piece=Piece();var a=Place(level,piece,Vector2.zero);var b=Place(level,piece,Vector2.right);
            var other=Place(level,piece,new Vector2(3,0));var window=Workspace(level);
            SelectPair(window,a,b);Click(window,"Propose shared-boundary bond");
            var oldStart=window.rootVisualElement.Query<Vector2Field>().ToList().Single(f=>f.label=="World start");
            window.rootVisualElement.Query<ObjectField>().ToList().Single(f=>f.label=="Piece B").value=other;
            Assert.That(FindButton(window,"Confirm connection"),Is.Null);Assert.That(oldStart.panel,Is.Null);
            Assert.DoesNotThrow(()=>oldStart.value=new Vector2(9,9));
            Assert.That(level.GetComponentsInChildren<ConnectionAuthoring>(),Is.Empty);
        }
        [Test] public void R2_ConfirmationRejectsNewOverlapWithoutChangingSavedBond()
        {
            var level=Level();var piece=Piece();var a=Place(level,piece,Vector2.zero);var b=Place(level,piece,Vector2.right);
            var bond=SaveBond(level,a,b);string before=EditorJsonUtility.ToJson(bond);
            Assert.That(ConnectionTools.TryPropose(a,b,false,out var proposal,out var error),Is.True,error);b.transform.position=Vector3.zero;
            Assert.That(ConnectionTools.TrySet(bond,proposal,out error),Is.False);Assert.That(error,Does.Contain("overlap"));
            Assert.That(EditorJsonUtility.ToJson(bond),Is.EqualTo(before));Assert.That(b.transform.position,Is.EqualTo(Vector3.zero));
        }
        [Test] public void R2_CompilationRejectsOverlappingSolidsEvenWithCoincidentBoundaryAndValidFit()
        {
            var level=Level();var piece=Piece();var a=Place(level,piece,Vector2.zero);var b=Place(level,piece,Vector2.right);
            var bond=SaveBond(level,a,b);b.transform.position=Vector3.zero;
            bond.b0=bond.a0;bond.b1=bond.a1;bond.restTranslation=Vector2.zero;bond.restAngle=0;
            Assert.That(LevelCompiler.TryCompile(level,out _,out var report),Is.False);
            Assert.That(report.issues.Any(i=>i.target==bond&&i.explanation.Contains("overlap")),Is.True,report.ToString());
        }
        [Test] public void R2_WorkspaceRejectsStaleSnappingProposalWithoutMovingEndpointOrChangingBond()
        {
            var level=Level();var piece=Piece();var a=Place(level,piece,Vector2.zero);var b=Place(level,piece,Vector2.right);
            var bond=SaveBond(level,a,b);string before=EditorJsonUtility.ToJson(bond);var window=Workspace(level);
            WindowAction(window,"ReviewConnection",bond);b.transform.position=new Vector3(1.2f,0,0);
            Click(window,"Preview alignment of parallel boundaries");
            var proposal=(ConnectionProposal)typeof(BomAuthoringWindow).GetField("connection",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            Assert.That(proposal,Is.Not.Null);Assert.That(proposal.translationB.sqrMagnitude,Is.GreaterThan(0));
            b.transform.position=-new Vector3(proposal.translationB.x,proposal.translationB.y,0);
            var position=b.transform.position;Click(window,"Confirm connection");
            Assert.That(window.rootVisualElement.Q<Label>("status").text,Does.Contain("overlap"));
            Assert.That(b.transform.position,Is.EqualTo(position));Assert.That(EditorJsonUtility.ToJson(bond),Is.EqualTo(before));
            Assert.That(level.GetComponentsInChildren<ConnectionAuthoring>(),Has.Length.EqualTo(1));
            Click(window,"Cancel connection proposal");Assert.That(b.transform.position,Is.EqualTo(position));
        }
        [Test] public void R2_PendingProposalUsesCurrentApprovedProfileOnConfirmation()
        {
            var level=Level();var piece=Piece();var a=Place(level,piece,Vector2.zero);var b=Place(level,piece,Vector2.right);
            var bond=SaveBond(level,a,b);string before=EditorJsonUtility.ToJson(bond);
            Assert.That(ConnectionTools.TryPropose(a,b,false,out var proposal,out var error),Is.True,error);
            var draft=piece.Draft();draft.profile=Geometry.Rectangle(0,0,2,1);Assert.That(piece.TryApprove(draft,out var report),Is.True,report.ToString());
            Assert.That(ConnectionTools.TrySet(bond,proposal,out _),Is.False);Assert.That(EditorJsonUtility.ToJson(bond),Is.EqualTo(before));
        }
        [Test] public void R3_SnapshotRejectsAllocatorThatCanReuseAParentIdentity()
        {
            var world=AcceptanceFixtures.Single(Geometry.Rectangle(-2,-.25f,2,.25f),1,2);world.nextSequence=1;
            Assert.That(SnapshotCodec.TryRead(SnapshotCodec.Serialize(world),out _,out var error),Is.False);
            Assert.That(error,Does.Contain("allocator"));
        }
        [Test] public void R3_AllocatorBoundIncludesConnectionSequences()
        {
            var world=AcceptanceFixtures.Bonded();world.nextSequence=3;
            Assert.That(SnapshotCodec.TryRead(SnapshotCodec.Serialize(world),out _,out var error),Is.False);Assert.That(error,Does.Contain("allocator"));
            world.nextSequence=4;world.bonds[0].id="acceptance:piece:3";
            Assert.That(SnapshotCodec.TryRead(SnapshotCodec.Serialize(world),out _,out _),Is.False);
        }
        [Test] public void R3_ProducerPreservesHighWaterMarkAfterAllAllocatedFragmentsRetire()
        {
            var world=AcceptanceFixtures.Single(Geometry.Rectangle(-2,-.25f,2,.25f),1,2);world.nextSequence=100;
            Assert.That(Blast.TryApply(world,new Vector2(0,.6f),0,out var split,out var error),Is.True,error);Assert.That(split.nextSequence,Is.EqualTo(102));
            split.configuration.testPresets[0].radius=10;split.configuration.testPresets[0].reachBudget=20;
            Assert.That(Blast.TryApply(split,new Vector2(0,3),0,out var empty,out error),Is.True,error);Assert.That(empty.bodies,Is.Empty);
            Assert.That(empty.nextSequence,Is.EqualTo(102));Assert.That(SnapshotCodec.TryRead(SnapshotCodec.Serialize(empty),out var recovered,out error),Is.True,error);
            Assert.That(recovered.Allocate("piece"),Is.EqualTo("acceptance:piece:102"));
        }
        [TestCase("other-match:piece:1")]
        [TestCase("acceptance:bond:1")]
        [TestCase("acceptance:piece:0")]
        [TestCase("acceptance:piece:01")]
        [TestCase("acceptance:piece:-1")]
        [TestCase("acceptance:piece:9223372036854775808")]
        public void R3_SnapshotRejectsInvalidLocalIdentityFormat(string id)
        {
            var world=AcceptanceFixtures.Single(Geometry.Rectangle(0,0,1,1));world.bodies[0].id=id;
            Assert.That(SnapshotCodec.TryRead(SnapshotCodec.Serialize(world),out _,out _),Is.False);
        }
        [Test] public void V2_DestructionOverrideAndResetRemainIndependentOfMaterialAndGrab()
        {
            var level=Level();var piece=Piece();var a=Place(level,piece,Vector2.zero);var b=Place(level,piece,new Vector2(2,0));
            b.overrideMaterial=true;b.material=Own(ScriptableObject.CreateInstance<MaterialDefinition>());b.material.density=4;
            b.overrideGrab=true;b.grabEligible=false;b.overrideDestruction=true;
            b.destruction=new DestructionSelection {mode=DestructionMode.Indestructible,impulseMultiplier=.7f};
            var draft=piece.Draft();draft.destruction.response=Own(ScriptableObject.CreateInstance<DestructionResponse>());
            draft.destruction.impulseMultiplier=2.5f;Assert.That(piece.TryApprove(draft,out var report),Is.True,report.ToString());
            Assert.That(a.EffectiveDestruction.response,Is.SameAs(draft.destruction.response));Assert.That(a.EffectiveDestruction.impulseMultiplier,Is.EqualTo(2.5f));
            Assert.That(b.EffectiveDestruction.mode,Is.EqualTo(DestructionMode.Indestructible));Assert.That(b.EffectiveDestruction.impulseMultiplier,Is.EqualTo(.7f));
            b.ResetDestruction();Assert.That(b.EffectiveDestruction.mode,Is.EqualTo(DestructionMode.Subtract));
            Assert.That(b.EffectiveDestruction.response,Is.SameAs(draft.destruction.response));Assert.That(b.EffectiveDestruction.impulseMultiplier,Is.EqualTo(2.5f));
            Assert.That(b.EffectiveMaterial.density,Is.EqualTo(4));Assert.That(b.EffectiveGrab,Is.False);
            Assert.That(LevelCompiler.TryCompile(level,out _,out report),Is.True,report.ToString());
        }
        [Test] public void V1_NearlyCollinearRotatedAttachmentRetainsBothMaterialIntervals()
        {
            var region=new Region {outer=new Contour(new[] {new Vector2(1,2.5f),new Vector2(0,2.5f),new Vector2(0,1.6f),new Vector2(.4f,1.6f),
                new Vector2(.4f,-.47f),new Vector2(0,-.47f),new Vector2(0,-1.5f),new Vector2(1,-1.5f)})};
            var a=Geometry.Rotate(new Vector2(-2.5f,0),-Mathf.PI*.5f);var b=Geometry.Rotate(new Vector2(1.5f,0),-Mathf.PI*.5f);
            var ranges=Geometry.SegmentRanges(region,a,b);Assert.That(ranges,Has.Count.EqualTo(2));
            Assert.That(ranges[0].lo,Is.EqualTo(0).Within(1e-8));Assert.That(ranges[0].hi,Is.EqualTo(.225).Within(.0001));
            Assert.That(ranges[1].lo,Is.EqualTo(.7425).Within(.0001));Assert.That(ranges[1].hi,Is.EqualTo(1).Within(1e-8));
        }
        private static BodyState AddWall(WorldState world,Region region,bool opaque,float resistance=0)
        {
            var wall=world.bodies[0].Clone();wall.id=world.Allocate("piece");wall.label="verification wall";wall.region=region;
            wall.destruction=opaque?DestructionMode.Indestructible:DestructionMode.Subtract;
            if(resistance>0)
            {
                var m=new MaterialRecord {label="explicit thin-wall resistance",density=2,blastResistance=resistance,friction=.5f};
                m.id=Fingerprints.Definition(m);world.definitions.materials.Add(m);wall.material=m.id;
            }
            world.bodies.Add(wall);return wall;
        }
        private static double ForbiddenRemoval(WorldState before,WorldState after,Region forbidden)
        {
            var remaining=new Paths64(after.bodies.Where(b=>b.label==before.bodies[0].label).SelectMany(b=>Geometry.Paths(b.region)));
            var removed=Geometry.Boolean(Geometry.Paths(before.bodies[0].region),remaining,ClipType.Difference);
            return Geometry.Boolean(new Paths64(removed.SelectMany(Geometry.Paths)),Geometry.Paths(forbidden),ClipType.Intersection).Sum(Geometry.Area);
        }
        private static void RecordBlastCase(string name,WorldState world,bool committed,string error)
        {
            Directory.CreateDirectory("Docs/Verification/Fixtures");Directory.CreateDirectory("Docs/Verification/Results");
            File.WriteAllText("Docs/Verification/Fixtures/"+name+".json",SnapshotCodec.Serialize(world,true));
            File.WriteAllText("Docs/Verification/Results/"+name+"-outcome.json","{\"committed\":"+(committed?"true":"false")+",\"error\":"+(error==null?"null":"\""+error.Replace("\\","\\\\").Replace("\"","\\\"")+"\"")+"}");
        }
        [Test] public void V3_GrazingOpaqueCoverDoesNotCarveInsideItsAnalyticShadow()
        {
            var world=AcceptanceFixtures.Single(Geometry.Rectangle(2,.28f,2.4f,.38f),3,4);
            AddWall(world,Geometry.Rectangle(1,-.15f,1.02f,.15f),true);
            Assert.That(Blast.TryApply(world,Vector2.zero,0,out var result,out var error),Is.True,error);
            var shadow=new Region {outer=new Contour(new[] {new Vector2(2,.28f),new Vector2(2.4f,.28f),new Vector2(2.4f,.36f),new Vector2(2,.3f)})};
            Assert.That(ForbiddenRemoval(world,result,shadow),Is.LessThan(Geometry.AreaTolerance));
            Assert.That(result.bodies.Where(b=>b.label==world.bodies[0].label).Sum(b=>Geometry.Area(b.region)),Is.LessThan(Geometry.Area(world.bodies[0].region)));
            RecordBlastCase("V3-grazing-cover",world,true,null);
        }
        [Test] public void V3_ThinWallRemovalCannotExceedOriginalAnalyticResistanceBudget()
        {
            var world=AcceptanceFixtures.Single(Geometry.Rectangle(2,-.1f,2.5f,.1f),5,2.5f);
            AddWall(world,Geometry.Rectangle(1,-2,1.1f,2),false,4);
            Assert.That(Blast.TryApply(world,Vector2.zero,0,out var result,out var error),Is.True,error);
            Assert.That(ForbiddenRemoval(world,result,Geometry.Rectangle(2.1002f,-.1f,2.5f,.1f)),Is.LessThan(Geometry.AreaTolerance));
            Assert.That(result.bodies.Where(b=>b.label==world.bodies[0].label).Sum(b=>Geometry.Area(b.region)),Is.LessThan(Geometry.Area(world.bodies[0].region)));
            RecordBlastCase("V3-thin-resistance-wall",world,true,null);
        }
        [Test] public void V3_RetainedThinCoverCannotCarveTheOccludedTargetAndRefusalsAreAtomic()
        {
            var world=AcceptanceFixtures.Single(Geometry.Rectangle(1.2f,-.1f,2,.1f),3,4);string target=world.bodies[0].id;
            var wall=AddWall(world,Geometry.Rectangle(1,-2,1.02f,2),false,1000);
            string before=SnapshotCodec.Serialize(world);bool committed=Blast.TryApply(world,Vector2.zero,0,out var result,out var error);
            if(!committed)
            {
                Assert.That(error,Does.Contain("retained cover"));Assert.That(result,Is.Null);Assert.That(SnapshotCodec.Serialize(world),Is.EqualTo(before));
                RecordBlastCase("V3-retained-thin-cover",world,false,error);return;
            }
            Assert.That(result.Body(target),Is.Not.Null);Assert.That(result.Body(target).geometryRevision,Is.EqualTo(1));
            Assert.That(Geometry.Area(result.Body(target).region),Is.EqualTo(Geometry.Area(world.Body(target).region)).Within(Geometry.AreaTolerance));
            Assert.That(result.Body(wall.id),Is.Not.Null);Assert.That(Geometry.Area(result.Body(wall.id).region),Is.LessThan(Geometry.Area(wall.region)));
            RecordBlastCase("V3-retained-thin-cover",world,true,null);
        }
    }
}
