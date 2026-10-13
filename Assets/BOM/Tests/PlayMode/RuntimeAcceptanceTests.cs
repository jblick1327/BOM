using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BOM.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BOM.Tests
{
    public sealed class RuntimeAcceptanceTests
    {
        private readonly List<PlaytestSession> sessions=new List<PlaytestSession>();
        private readonly List<GameObject> objects=new List<GameObject>();
        private PlaytestSession Session(WorldState state)
        {
            var session=new PlaytestSession();sessions.Add(session);
            Assert.That(session.TryRecover(SnapshotCodec.Serialize(state),out var error),Is.True,error);return session;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach(var session in sessions) session.Dispose();sessions.Clear();foreach(var go in objects) if(go!=null) UnityEngine.Object.Destroy(go);objects.Clear();yield return null;
        }
        [UnityTest] public IEnumerator AC01_ApprovedMultiMeshAssetCreatesExactlyOneInitialPhysicsBody()
        {
            var piece=AssetDatabase.LoadAssetAtPath<PieceDefinition>(FixtureBuilder.Folder+"/ImportedMultiMeshFrame.asset");
            Assert.That(piece,Is.Not.Null);Assert.That(piece.IsApproved,Is.True);Assert.That(piece.Draft().import.manuallyCorrected,Is.True);
            Assert.That(AssetDatabase.GUIDToAssetPath(piece.Draft().import.sourceGuid),Is.EqualTo(FixtureBuilder.SourcePath));
            var root=new GameObject("Imported acceptance level");objects.Add(root);var level=root.AddComponent<LevelAuthoring>();level.startOnPlay=false;level.enabled=false;
            level.testPresets.Add(AssetDatabase.LoadAssetAtPath<ExplosionPreset>(FixtureBuilder.Folder+"/Test_SmallBite.asset"));
            var placed=new GameObject("One imported piece");placed.transform.SetParent(root.transform,false);placed.AddComponent<PiecePlacement>().piece=piece;
            Assert.That(LevelCompiler.TryCompile(level,out var resolved,out var report),Is.True,report.ToString());var session=new PlaytestSession();sessions.Add(session);
            Assert.That(session.TryStart(resolved,out var error),Is.True,error);Assert.That(session.BodyCount,Is.EqualTo(1));
            Assert.That(session.Body(session.ReadCommitted().bodies[0].id).gameObject.GetComponents<Rigidbody2D>(),Has.Length.EqualTo(1));
            yield return null;
        }
        [UnityTest] public IEnumerator AC03_FrameHoleIsEmptyForPhysicsProbeAndThicknessDoesNotChangeMass()
        {
            var world=AcceptanceFixtures.Single(FixtureBuilder.Frame());var session=Session(world);string id=world.bodies[0].id;
            var body=session.Body(id);Assert.That(body.mass,Is.EqualTo(16).Within(.0001));
            Assert.That(body.GetComponents<PolygonCollider2D>().Any(c=>c.OverlapPoint(Vector2.zero)),Is.False);
            var probeObject=new GameObject("Temporary hole traversal probe");objects.Add(probeObject);SceneManager.MoveGameObjectToScene(probeObject,body.gameObject.scene);
            probeObject.transform.position=new Vector2(-.3f,0);probeObject.AddComponent<CircleCollider2D>().radius=.08f;var probe=probeObject.AddComponent<Rigidbody2D>();probe.gravityScale=0;probe.linearVelocity=new Vector2(1.5f,0);
            for(int i=0;i<20;i++) Assert.That(session.TryStep(out var error),Is.True,error);
            Assert.That(probe.position.x,Is.GreaterThan(.25f));Assert.That(Mathf.Abs(probe.position.y),Is.LessThan(.01f));
            world.bodies[0].thickness=2;Assert.That(session.TryRecover(SnapshotCodec.Serialize(world),out var reloadError),Is.True,reloadError);
            Assert.That(session.Body(id).mass,Is.EqualTo(16).Within(.0001));yield return null;
        }
        [UnityTest] public IEnumerator AC03_ConcavityHasNoColliderOrFaceInItsMissingCorner()
        {
            var world=AcceptanceFixtures.Single(FixtureBuilder.Concave());var session=Session(world);var body=session.Body(world.bodies[0].id);
            Assert.That(body.GetComponents<PolygonCollider2D>().Any(c=>c.OverlapPoint(new Vector2(2,2))),Is.False);
            Assert.That(body.GetComponents<PolygonCollider2D>().Any(c=>c.OverlapPoint(new Vector2(.2f,2))),Is.True);
            Assert.That(body.mass,Is.EqualTo((float)Geometry.Area(world.bodies[0].region)*2).Within(.0001));
            Assert.That(session.TryExplode(new Vector2(1.5f,1.5f),0,out var error),Is.True,error);yield return null;
        }
        [UnityTest] public IEnumerator AC04_CutRebuildsColliderMeshAndStableUVsAsOneCommit()
        {
            var world=AcceptanceFixtures.Single(Geometry.Rectangle(-2,-1,2,1),.8f,2);var session=Session(world);string id=world.bodies[0].id;
            Assert.That(session.TryExplode(new Vector2(0,1.4f),0,out var error),Is.True,error);var state=session.ReadCommitted().bodies[0];var body=session.Body(id);
            Assert.That(body.GetComponents<PolygonCollider2D>().Any(c=>c.OverlapPoint(new Vector2(0,.9f))),Is.False);
            Assert.That(body.GetComponents<PolygonCollider2D>().Any(c=>c.OverlapPoint(new Vector2(-1.5f,-.5f))),Is.True);
            var mesh=body.GetComponent<MeshFilter>().sharedMesh;var vertices=mesh.vertices;var uv=mesh.uv;
            foreach(int index in mesh.GetTriangles(0).Distinct()) Assert.That(Vector2.Distance(uv[index],state.UV(vertices[index])),Is.LessThan(.0001));
            Assert.That(mesh.GetTriangles(2).Length,Is.GreaterThan(0));Assert.That(body.mass,Is.EqualTo((float)Geometry.Area(state.region)*2).Within(.0001));yield return null;
        }
        [UnityTest] public IEnumerator AC09_RealSolverLoadBreaksSingleStrengthBond()
        {
            var world=AcceptanceFixtures.Bonded();world.configuration.gravity=new Vector2(0,-9.81f);world.configuration.foundations.Add(world.bodies[0].id);world.bonds[0].strength=.0001f;
            var session=Session(world);
            for(int i=0;i<10&&session.BondCount>0;i++) Assert.That(session.TryStep(out var error),Is.True,error);
            Assert.That(session.BondCount,Is.EqualTo(0));Assert.That(session.BodyCount,Is.EqualTo(2));yield return null;
        }
        [UnityTest] public IEnumerator AC10_StartResetFreezeAuthoredSettingsAndNeverPersistRuntimeDamage()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(FixtureBuilder.AssemblyPath);var instance=UnityEngine.Object.Instantiate(prefab);objects.Add(instance);
            var level=instance.GetComponent<LevelAuthoring>();level.startOnPlay=false;level.enabled=false;
            Assert.That(LevelCompiler.TryCompile(level,out var resolved,out var report),Is.True,report.ToString());
            var session=new PlaytestSession();sessions.Add(session);Assert.That(session.TryStart(resolved,out var error),Is.True,error);
            var initial=session.ReadCommitted();Assert.That(initial.bodies,Has.Count.EqualTo(6));
            Assert.That(session.TryExplode(new Vector2(2,1.5f),0,out error),Is.True,error);
            Assert.That(session.ReadCommitted().bodies.Sum(b=>Geometry.Area(b.region)),Is.LessThan(initial.bodies.Sum(b=>Geometry.Area(b.region))));
            var placement=instance.GetComponentsInChildren<PiecePlacement>().First(p=>p.piece.name=="Plate_3x1");var originalProfile=JsonUtility.ToJson(placement.piece.Draft().profile);
            placement.overrideGrab=true;placement.grabEligible=false;placement.transform.position+=Vector3.right*10;
            Assert.That(session.TryReset(out error),Is.True,error);Assert.That(SnapshotCodec.Serialize(session.ReadCommitted()),Is.EqualTo(SnapshotCodec.Serialize(initial)));
            Assert.That(JsonUtility.ToJson(placement.piece.Draft().profile),Is.EqualTo(originalProfile));
            foreach(var body in initial.bodies) Assert.That(session.Body(body.id),Is.Not.Null);yield return null;
        }
        [UnityTest] public IEnumerator AC11_FreshPostSplitReconstructionUsesOnlyCurrentStateDefinitionsAndImageBytes()
        {
            var world=AcceptanceFixtures.Single(Geometry.Rectangle(-2,-.25f,2,.25f),1,2);var appearance=world.definitions.appearances[0];
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(FixtureBuilder.Folder+"/FaceChecker.asset");appearance.imagePng=ImportService.ImagePng(texture);appearance.id=Fingerprints.Definition(appearance);world.bodies[0].appearance=appearance.id;
            var session=Session(world);string parent=world.bodies[0].id;Assert.That(session.TryExplode(new Vector2(0,.6f),0,out var error),Is.True,error);
            var captureClock=System.Diagnostics.Stopwatch.StartNew();string capture=session.Capture(true);captureClock.Stop();
            var current=session.ReadCommitted();Assert.That(current.bodies,Has.Count.EqualTo(2));Assert.That(current.bodies.All(b=>b.id!=parent),Is.True);
            var fresh=new PlaytestSession();sessions.Add(fresh);var clock=System.Diagnostics.Stopwatch.StartNew();Assert.That(fresh.TryRecover(capture,out error),Is.True,error);clock.Stop();
            Assert.That(fresh.ReadCommitted().bodies.Select(b=>b.id),Is.EquivalentTo(current.bodies.Select(b=>b.id)));
            foreach(var body in current.bodies)
            {
                var actual=fresh.ReadCommitted().Body(body.id);Assert.That(JsonUtility.ToJson(actual),Is.EqualTo(JsonUtility.ToJson(body)));
                var renderer=fresh.Body(body.id).GetComponent<MeshRenderer>();Assert.That(renderer.sharedMaterials[0].GetTexture("_BaseMap").width,Is.EqualTo(16));
            }
            Directory.CreateDirectory("Docs/Verification/Results");File.WriteAllText("Docs/Verification/Results/AC11-post-split-world.json",capture);
            File.WriteAllText("Docs/Verification/Results/AC11-reconstruction-metrics.json","{\"unity\":\""+Application.unityVersion+"\",\"bodyCount\":"+fresh.BodyCount+",\"connectorCount\":"+fresh.BondCount
                +",\"vertexCount\":"+current.bodies.Sum(b=>b.region.Contours.Sum(c=>c.points.Count))+",\"captureMilliseconds\":"+captureClock.Elapsed.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
                +",\"reconstructionMilliseconds\":"+clock.Elapsed.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}");
            yield return null;
        }
        [UnityTest] public IEnumerator AC12_InvalidReconstructionRetainsPreviousUsablePhysicsWorld()
        {
            var world=AcceptanceFixtures.Single(FixtureBuilder.Frame());var session=Session(world);var original=session.Body(world.bodies[0].id);
            var invalid=world.Clone();invalid.bodies[0].material="missing definition";
            Assert.That(session.TryRecover(SnapshotCodec.Serialize(invalid),out var error),Is.False);Assert.That(error,Does.Contain("definition"));
            Assert.That(session.Body(world.bodies[0].id),Is.SameAs(original));Assert.That(session.BodyCount,Is.EqualTo(1));
            string first=session.Capture(),second=session.Capture();Assert.That(SnapshotCodec.TryRead(first,out var a,out _),Is.True);Assert.That(SnapshotCodec.TryRead(second,out var b,out _),Is.True);
            Assert.That(b.captureNumber,Is.GreaterThan(a.captureNumber));yield return null;
        }
    }
}
