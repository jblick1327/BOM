using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BOM.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BOM.Tests
{
    public sealed class AuthoringAcceptanceTests
    {
        private readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        private readonly List<string> temporaryAssets=new List<string>();
        private T Own<T>(T value) where T:UnityEngine.Object {owned.Add(value);return value;}
        private MaterialDefinition Material(float density=2)
        {var m=Own(ScriptableObject.CreateInstance<MaterialDefinition>());m.density=density;return m;}
        private PieceDefinition Piece(Region region=null)
        {
            var piece=Own(ScriptableObject.CreateInstance<PieceDefinition>());var response=Own(ScriptableObject.CreateInstance<DestructionResponse>());
            Assert.That(piece.TryApprove(new PieceDraft {profile=region??Geometry.Rectangle(0,0,1,1),material=Material(),
                destruction=new DestructionSelection {response=response}},out var report),Is.True,report.ToString());return piece;
        }
        private LevelAuthoring Level()
        {
            var root=Own(new GameObject("Acceptance level"));var level=root.AddComponent<LevelAuthoring>();
            var preset=Own(ScriptableObject.CreateInstance<ExplosionPreset>());level.testPresets.Add(preset);return level;
        }
        private PiecePlacement Place(LevelAuthoring level,PieceDefinition piece,Vector2 position=default)
        {var go=new GameObject("Acceptance placement");go.transform.SetParent(level.transform,false);go.transform.position=position;
            var placement=go.AddComponent<PiecePlacement>();placement.piece=piece;return placement;}
        [TearDown] public void Cleanup() {foreach(var value in owned.AsEnumerable().Reverse()) if(value!=null) UnityEngine.Object.DestroyImmediate(value);owned.Clear();
            foreach(var path in temporaryAssets) AssetDatabase.DeleteAsset(path);temporaryAssets.Clear();}

        [Test] public void AC01_MultiMeshFbxProposesOneConnectedFrameAndOneInitialBody()
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(FixtureBuilder.SourcePath);Assert.That(source,Is.Not.Null);
            var proposal=ImportService.Propose(source);Assert.That(proposal.sourceMeshes,Is.EqualTo(2));
            Assert.That(proposal.draft.disconnectedProposal,Is.Empty,proposal.report.ToString());
            Assert.That(proposal.draft.profile.holes,Has.Count.EqualTo(1));Assert.That(Geometry.Area(proposal.draft.profile),Is.EqualTo(8).Within(.0001));
            proposal.draft.material=Material();proposal.draft.destruction.response=Own(ScriptableObject.CreateInstance<DestructionResponse>());
            proposal.draft.profile.outer.points[0]+=new Vector2(.05f,0);proposal.draft.import.manuallyCorrected=true;
            var piece=Own(ScriptableObject.CreateInstance<PieceDefinition>());Assert.That(piece.TryApprove(proposal.draft,out var report),Is.True,report.ToString());
            var level=Level();Place(level,piece);Assert.That(LevelCompiler.TryCompile(level,out var resolved,out report),Is.True,report.ToString());
            Assert.That(resolved.Read().bodies,Has.Count.EqualTo(1));
            var disconnected=piece.Draft();disconnected.disconnectedProposal.Add(Geometry.Rectangle(5,5,6,6));
            Assert.That(piece.TryApprove(disconnected,out report),Is.False);Assert.That(piece.Version,Is.EqualTo(1));
        }
        [Test] public void AC02_ReimportAndCancelPreserveCorrectionsAndGameplayUntilApproval()
        {
            var piece=Piece(FixtureBuilder.Frame());var draft=piece.Draft();draft.import.manuallyCorrected=true;
            string path=AssetDatabase.GenerateUniqueAssetPath(FixtureBuilder.Folder+"/Source/AcceptanceReimport.fbx");
            Assert.That(AssetDatabase.CopyAsset(FixtureBuilder.SourcePath,path),Is.True);temporaryAssets.Add(path);
            draft.import.sourcePath=path;draft.import.sourceGuid=AssetDatabase.AssetPathToGUID(path);draft.import.sourceHash=AssetDatabase.GetAssetDependencyHash(path).ToString();
            draft.profile.outer.points[0]+=new Vector2(.1f,0);Assert.That(piece.TryApprove(draft,out _),Is.True);
            int version=piece.Version;string prior=JsonUtility.ToJson(piece.Draft());
            var settings=draft.import.Clone();
            string content=File.ReadAllText(path).Replace("1.5,1.5,0","1.75,1.5,0");File.WriteAllText(path,content);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var pending=ImportService.Propose(source,piece,true,settings);Assert.That(Geometry.Area(pending.draft.profile),Is.Not.EqualTo(Geometry.Area(draft.profile)));
            Assert.That(JsonUtility.ToJson(piece.Draft()),Is.EqualTo(prior));Assert.That(piece.Version,Is.EqualTo(version));
            var keeping=ImportService.Propose(source,piece,false,settings);
            Assert.That(JsonUtility.ToJson(keeping.draft.profile),Is.EqualTo(JsonUtility.ToJson(draft.profile)));
            Assert.That(keeping.draft.import.manuallyCorrected,Is.True);Assert.That(keeping.draft.material,Is.SameAs(draft.material));
            Assert.That(keeping.draft.import.sourceHash,Is.Not.EqualTo(draft.import.sourceHash));
            Assert.That(keeping.draft.destruction.response,Is.SameAs(draft.destruction.response));
            Assert.That(piece.TryApprove(keeping.draft,out var report),Is.True,report.ToString());Assert.That(piece.Version,Is.EqualTo(version+1));
        }
        [Test] public void AC03_ConcavityAndHoleAreaAgreeWithTriangulationAndMass()
        {
            foreach(var region in new[] {FixtureBuilder.Frame(),FixtureBuilder.Concave()})
                Assert.That(Geometry.Triangles(region).Sum(t=>Math.Abs(Geometry.SignedArea(t))),Is.EqualTo(Geometry.Area(region)).Within(.0001));
            Assert.That(Geometry.Area(FixtureBuilder.Frame())*2,Is.EqualTo(16).Within(.0001));
            Assert.That(Geometry.Contains(FixtureBuilder.Frame(),Vector2.zero),Is.False);
            Assert.That(Geometry.Contains(FixtureBuilder.Concave(),new Vector2(2,2)),Is.False);
        }
        [Test] public void AC04_SmallBitePreservesIdentityOutlineAndStableTextureCoordinates()
        {
            var world=AcceptanceFixtures.Single(Geometry.Rectangle(-2,-1,2,1),.8f,2);var old=world.bodies[0];var sample=new Vector2(-1.7f,-.7f);
            Assert.That(Blast.TryApply(world,new Vector2(0,1.4f),0,out var result,out var error),Is.True,error);
            Assert.That(result.bodies,Has.Count.EqualTo(1));var body=result.bodies[0];Assert.That(body.id,Is.EqualTo(old.id));
            Assert.That(Geometry.Area(body.region),Is.LessThan(Geometry.Area(old.region)));Assert.That(Geometry.Contains(body.region,sample),Is.True);
            Assert.That(body.UV(sample),Is.EqualTo(old.UV(sample)));Assert.That(body.region.Contours.SelectMany(c=>c.cutEdges).Any(c=>c),Is.True);
            Assert.That(Geometry.Triangles(body.region).Sum(t=>Math.Abs(Geometry.SignedArea(t))),Is.EqualTo(Geometry.Area(body.region)).Within(.0001));
        }
        [Test] public void AC05_CompleteRemovalAndSplitRetireParentAndRetainDefinitionClosure()
        {
            var split=AcceptanceFixtures.Single(Geometry.Rectangle(-2,-.25f,2,.25f),1,2);string parent=split.bodies[0].id;
            Assert.That(Blast.TryApply(split,new Vector2(0,.6f),0,out var children,out var error),Is.True,error);
            Assert.That(children.bodies,Has.Count.EqualTo(2));Assert.That(children.bodies.All(b=>b.id!=parent),Is.True);
            Assert.That(children.bodies.All(b=>b.material==split.bodies[0].material&&b.appearance==split.bodies[0].appearance&&b.response==split.bodies[0].response),Is.True);
            Assert.That(SnapshotCodec.TryRead(SnapshotCodec.Serialize(children),out var reconstructed,out error),Is.True,error);
            Assert.That(reconstructed.bodies.Select(b=>b.id),Is.EquivalentTo(children.bodies.Select(b=>b.id)));
            var removal=AcceptanceFixtures.Single(Geometry.Rectangle(-.5f,-.5f,.5f,.5f),3,10);
            Assert.That(Blast.TryApply(removal,new Vector2(0,2),0,out var empty,out error),Is.True,error);Assert.That(empty.bodies,Is.Empty);
        }
        [Test] public void AC06_IndependentPlacementOverridesAndPerFieldResetResumeInheritance()
        {
            var level=Level();var piece=Piece();var a=Place(level,piece);var b=Place(level,piece,new Vector2(2,0));
            var different=Material(4);b.overrideMaterial=true;b.material=different;b.overrideGrab=true;b.grabEligible=false;
            var draft=piece.Draft();draft.material=Material(3);draft.grabEligible=true;Assert.That(piece.TryApprove(draft,out _),Is.True);
            Assert.That(a.EffectiveMaterial.density,Is.EqualTo(3));Assert.That(b.EffectiveMaterial.density,Is.EqualTo(4));Assert.That(b.EffectiveGrab,Is.False);
            b.ResetMaterial();Assert.That(b.EffectiveMaterial.density,Is.EqualTo(3));Assert.That(b.EffectiveGrab,Is.False);
            b.ResetGrab();Assert.That(b.EffectiveGrab,Is.True);
            Assert.That(LevelCompiler.TryCompile(level,out var frozen,out var report),Is.True,report.ToString());
            draft=piece.Draft();draft.material.density=7;Assert.That(frozen.Read().definitions.materials.All(m=>m.density==3),Is.True);
        }
        [Test] public void AC07_SavedAssemblyReloadsReferencesOverridesFoundationsAndBonds()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(FixtureBuilder.AssemblyPath);Assert.That(prefab,Is.Not.Null);
            var instance=Own((GameObject)PrefabUtility.InstantiatePrefab(prefab));var level=instance.GetComponent<LevelAuthoring>();
            Assert.That(LevelCompiler.TryCompile(level,out var result,out var report),Is.True,report.ToString());
            Assert.That(result.Read().bodies,Has.Count.EqualTo(6));Assert.That(result.Read().bonds,Has.Count.EqualTo(2));Assert.That(result.Read().configuration.foundations,Has.Count.EqualTo(2));
            Assert.That(instance.GetComponentsInChildren<PiecePlacement>().Any(p=>p.overrideMaterial&&p.overrideGrab&&!p.grabEligible),Is.True);
        }
        [Test] public void AC08_BondRejectsGapAndPointContactAndSnapCancellationKeepsTransforms()
        {
            var level=Level();var piece=Piece();var a=Place(level,piece);var b=Place(level,piece,new Vector2(1,0));
            Assert.That(ConnectionTools.TryPropose(a,b,false,out var valid,out var error),Is.True,error);
            var go=new GameObject("bond");go.transform.SetParent(level.transform,false);var bond=go.AddComponent<ConnectionAuthoring>();
            Assert.That(ConnectionTools.TrySet(bond,valid,out error),Is.True,error);Assert.That(LevelCompiler.TryCompile(level,out _,out var report),Is.True,report.ToString());
            b.transform.position=new Vector2(1.2f,0);var position=b.transform.position;
            Assert.That(ConnectionTools.TryPropose(a,b,false,out _,out _),Is.False);
            Assert.That(ConnectionTools.TryPropose(a,b,true,out var snap,out error),Is.True,error);Assert.That(snap.translationB.sqrMagnitude,Is.GreaterThan(0));
            Assert.That(b.transform.position,Is.EqualTo(position));Assert.That(LevelCompiler.TryCompile(level,out _,out report),Is.False);
            b.transform.position=new Vector2(1,1);Assert.That(ConnectionTools.TryPropose(a,b,false,out _,out _),Is.False);
            b.transform.position=new Vector2(1,0);bond.b=null;Assert.That(LevelCompiler.TryCompile(level,out _,out report),Is.False);
            Assert.That(report.issues.Any(i=>i.target==bond),Is.True);
        }
        [Test] public void AC09_OldLifetimeFailureIsCheckedBeforeAllocatingReplacementBonds()
        {
            foreach(bool failure in new[] {false,true})
            {
                var current=AcceptanceFixtures.Bonded(true,failure ? .8f : .4f);var next=current.Clone();var old=current.bodies[0];
                var left=old.Clone();left.id=next.Allocate("piece");left.region=Geometry.Rectangle(-2,0,-.5f,1);
                var right=old.Clone();right.id=next.Allocate("piece");right.region=Geometry.Rectangle(.5f,0,2,1);
                next.bodies.RemoveAll(b=>b.id==old.id);next.bodies.AddRange(new[] {left,right});
                Blast.RemapBonds(current,next,new Dictionary<string,List<BodyState>> {{old.id,new List<BodyState> {left,right}}});
                if(failure) Assert.That(next.bonds,Is.Empty);
                else {Assert.That(next.bonds,Has.Count.EqualTo(2));Assert.That(next.bonds.All(b=>b.id!=current.bonds[0].id),Is.True);
                    Assert.That(next.bonds.All(b=>Mathf.Abs(b.referenceLength-b.Length)<.0001),Is.True);
                    Assert.That(next.bonds.All(b=>b.restTranslation==current.bonds[0].restTranslation&&b.restAngle==current.bonds[0].restAngle),Is.True);}
            }
            var currentShort=AcceptanceFixtures.Bonded(true,.4f);var shortened=currentShort.Clone();shortened.bodies[0].region=Geometry.Rectangle(-1,0,1,1);
            Blast.RemapBonds(currentShort,shortened,new Dictionary<string,List<BodyState>> {{currentShort.bodies[0].id,new List<BodyState> {shortened.bodies[0]}}});
            Assert.That(shortened.bonds[0].id,Is.EqualTo(currentShort.bonds[0].id));Assert.That(shortened.bonds[0].referenceLength,Is.EqualTo(4));
            Assert.That(shortened.bonds[0].ForceCapacity,Is.EqualTo(200));Assert.That(shortened.bonds[0].MomentCapacity,Is.EqualTo(200));
        }
        [Test] public void AC12_InvalidContoursAndMissingDefinitionsPreserveApprovedState()
        {
            var piece=Piece();string original=JsonUtility.ToJson(piece.Draft());int version=piece.Version;
            var invalid=piece.Draft();invalid.profile.outer=new Contour(new[] {Vector2.zero,Vector2.one,Vector2.up,Vector2.right});
            Assert.That(piece.TryApprove(invalid,out var report),Is.False);Assert.That(report.issues[0].target,Is.SameAs(piece));
            Assert.That(piece.Version,Is.EqualTo(version));Assert.That(JsonUtility.ToJson(piece.Draft()),Is.EqualTo(original));
            var level=Level();var placement=Place(level,piece);placement.overrideMaterial=true;placement.material=null;
            Assert.That(LevelCompiler.TryCompile(level,out var resolved,out report),Is.False);Assert.That(resolved,Is.Null);
            Assert.That(report.issues.Any(i=>i.target==placement),Is.True);
            var world=AcceptanceFixtures.Single(FixtureBuilder.Frame());world.definitions.materials.Clear();
            Assert.That(SnapshotCodec.TryRead(SnapshotCodec.Serialize(world),out _,out _),Is.False);
        }
        [Test] public void GeometryRejectsTouchingNestedAndSelfIntersectingHoles()
        {
            var frame=FixtureBuilder.Frame();frame.holes[0].points[0]=frame.outer.points[0];Assert.That(Geometry.TryValidate(frame,out _),Is.False);
            frame=FixtureBuilder.Frame();frame.holes.Add(Geometry.Rectangle(-.2f,-.2f,.2f,.2f).outer);Assert.That(Geometry.TryValidate(frame,out _),Is.False);
            var nonfinite=FixtureBuilder.Frame();nonfinite.outer.points[0]=new Vector2(float.NaN,0);Assert.That(Geometry.TryValidate(nonfinite,out _),Is.False);
        }
        [Test] public void OriginAndBlastCostUseReachableAirOriginalResistanceAndOpaqueCover()
        {
            var frame=AcceptanceFixtures.Single(FixtureBuilder.Frame());Assert.That(Blast.ReachableOrigin(frame,Vector2.zero,out _),Is.False);
            Assert.That(Blast.ReachableOrigin(frame,new Vector2(0,2),out _),Is.True);
            var world=AcceptanceFixtures.Single(Geometry.Rectangle(1,-1,2,1),5,4);
            var m=world.definitions.materials[0];m.blastResistance=2;m.id=Fingerprints.Definition(m);world.bodies[0].material=m.id;
            Assert.That(Blast.Reach(world,Vector2.zero,0,world.configuration.testPresets[0]),Is.EqualTo(2).Within(.0001));
            world.bodies[0].destruction=DestructionMode.Indestructible;
            Assert.That(Blast.Reach(world,Vector2.zero,0,world.configuration.testPresets[0]),Is.EqualTo(1).Within(.0001));
        }
        [Test] public void SupportedScaleIsBakedIntoGeometryAndTextureMapWhileInvalidTransformsBlock()
        {
            var level=Level();var piece=Piece();var placement=Place(level,piece);placement.transform.localScale=new Vector3(2,3,1);
            Assert.That(LevelCompiler.TryCompile(level,out var result,out var report),Is.True,report.ToString());var body=result.Read().bodies[0];
            Assert.That(Geometry.Area(body.region),Is.EqualTo(6).Within(.0001));Assert.That(body.UV(new Vector2(2,3)),Is.EqualTo(Vector2.one));
            placement.transform.localScale=new Vector3(-1,1,1);Assert.That(LevelCompiler.TryCompile(level,out _,out _),Is.False);
            placement.transform.localScale=Vector3.one;placement.transform.rotation=Quaternion.Euler(10,0,0);Assert.That(LevelCompiler.TryCompile(level,out _,out _),Is.False);
        }
        [Test] public void ConflictingManagedPhysicsBlocksCompilation()
        {
            var level=Level();var placement=Place(level,Piece());placement.gameObject.AddComponent<Rigidbody2D>();
            Assert.That(LevelCompiler.TryCompile(level,out _,out var report),Is.False);Assert.That(report.issues.Any(i=>i.field=="Managed components"),Is.True);
        }
        [Test] public void GeometryDerivedPropertiesRemainStableForOffsetLocalOrigins()
        {
            var frame=FixtureBuilder.Frame();var shifted=Geometry.Transform(frame,p=>p+new Vector2(9990,9990));
            Assert.That(Geometry.Area(shifted),Is.EqualTo(8).Within(.00001));
            Assert.That(Geometry.Centroid(shifted),Is.EqualTo(new Vector2(9990,9990)));
            Assert.That(Geometry.Moment(shifted),Is.EqualTo(40d/3).Within(.00001));
            Assert.That(Geometry.Moment(shifted),Is.EqualTo(Geometry.Moment(frame)).Within(.00001));
        }
    }
}
