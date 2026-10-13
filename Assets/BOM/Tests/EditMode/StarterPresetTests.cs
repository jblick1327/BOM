using System.Linq;
using BOM.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BOM.Tests
{
    [Category("StarterPresets")]
    public sealed class StarterPresetTests
    {
        private static WorldState World(string material="Default",string response="Standard")
        {
            var world=AcceptanceFixtures.Single(Geometry.Rectangle(0,0,3,1));var source=StarterPresets.Get<MaterialDefinition>(material);
            var m=new MaterialRecord {label=source.name,density=source.density,blastResistance=source.blastResistance,friction=source.friction,
                restitution=source.restitution,linearDamping=source.linearDamping,angularDamping=source.angularDamping};m.id=Fingerprints.Definition(m);
            world.definitions.materials.Clear();world.definitions.materials.Add(m);world.bodies[0].material=m.id;
            var r=new ResponseRecord {label=response,impulseScale=StarterPresets.Get<DestructionResponse>(response).impulseScale};r.id=Fingerprints.Definition(r);
            world.definitions.responses.Clear();world.definitions.responses.Add(r);world.bodies[0].response=r.id;world.configuration.testPresets.Clear();
            foreach(var p in StarterPresets.All<ExplosionPreset>())world.configuration.testPresets.Add(new TestPresetRecord {label=p.name,radius=p.radius,reachBudget=p.reachBudget,impulseSpeed=p.impulseSpeed});
            return world;
        }
        private static WorldState Explode(WorldState world,string preset)
        {
            int index=world.configuration.testPresets.FindIndex(p=>p.label==preset);
            Assert.That(Blast.TryApply(world,new Vector2(-.3f,.5f),index,out var result,out var error),Is.True,error);return result;
        }
        [Test] public void StarterLibraryIsValidAndRestoringMissingAssetsPreservesExistingValuesAndGuids()
        {
            var assets=StarterPresets.All<MaterialDefinition>().Cast<ScriptableObject>().Concat(StarterPresets.All<DestructionResponse>()).Concat(StarterPresets.All<ExplosionPreset>()).ToArray();
            Assert.That(assets,Has.Length.EqualTo(9));var before=assets.Select(a=>JsonUtility.ToJson(a)+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(a))).ToArray();
            StarterPresets.Create();Assert.That(assets.Select(a=>JsonUtility.ToJson(a)+AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(a))),Is.EqualTo(before));
            foreach(var m in assets.OfType<MaterialDefinition>())Assert.That(m.TryValidate(out var error),Is.True,error);
            foreach(var r in assets.OfType<DestructionResponse>())Assert.That(r.TryValidate(out var error),Is.True,error);
            foreach(var p in assets.OfType<ExplosionPreset>())Assert.That(p.TryValidate(out var error),Is.True,error);
        }
        [Test] public void MaterialChoicesVaryOnePropertyAndKeepMotionSettingsConsistent()
        {
            var standard=StarterPresets.Get<MaterialDefinition>("Default");
            foreach(var material in StarterPresets.All<MaterialDefinition>())
            {
                Assert.That(material.friction,Is.EqualTo(standard.friction));Assert.That(material.restitution,Is.EqualTo(standard.restitution));
                Assert.That(material.linearDamping,Is.EqualTo(standard.linearDamping));Assert.That(material.angularDamping,Is.EqualTo(standard.angularDamping));
                if(material.name=="Tough"){Assert.That(material.density,Is.EqualTo(standard.density));Assert.That(material.blastResistance,Is.GreaterThan(standard.blastResistance));}
                else Assert.That(material.blastResistance,Is.EqualTo(standard.blastResistance));
            }
            Assert.That(StarterPresets.Get<MaterialDefinition>("Light").density,Is.LessThan(standard.density));Assert.That(StarterPresets.Get<MaterialDefinition>("Heavy").density,Is.GreaterThan(standard.density));
        }
        [Test] public void BlastsMakeProgressivelyLargerCutsOnTheSameDefaultPlate()
        {
            var world=World();double small=Explode(world,"Small").bodies.Sum(b=>Geometry.Area(b.region));
            double medium=Explode(world,"Medium").bodies.Sum(b=>Geometry.Area(b.region));double large=Explode(world,"Large").bodies.Sum(b=>Geometry.Area(b.region));
            Assert.That(small,Is.InRange(.01,2.99));Assert.That(medium,Is.LessThan(small));Assert.That(large,Is.LessThan(medium));
            Assert.That(Explode(World("Tough"),"Medium").bodies.Sum(b=>Geometry.Area(b.region)),Is.GreaterThan(medium));
        }
        [Test] public void HeavyChangesMassSettingsWhileNoPushChangesOnlyAddedSpeed()
        {
            var normal=Explode(World(),"Small");var heavy=Explode(World("Heavy"),"Small");var noPush=Explode(World(response:"No push"),"Small");
            Assert.That(heavy.definitions.Material(heavy.bodies[0].material).density,Is.GreaterThan(normal.definitions.Material(normal.bodies[0].material).density));
            Assert.That(Geometry.Area(heavy.bodies[0].region),Is.EqualTo(Geometry.Area(normal.bodies[0].region)).Within(.0001));
            Assert.That(Vector2.Distance(heavy.bodies[0].velocity,normal.bodies[0].velocity),Is.LessThan(.0001));Assert.That(normal.bodies[0].velocity.magnitude,Is.GreaterThan(0));
            Assert.That(noPush.bodies.Sum(b=>Geometry.Area(b.region)),Is.EqualTo(normal.bodies.Sum(b=>Geometry.Area(b.region))).Within(.0001));
            Assert.That(noPush.bodies.All(b=>b.velocity==Vector2.zero),Is.True);
        }
        [TestCase("Medium"),TestCase("Large")] public void ToughConcavePieceCanBeCarvedWithoutFalseReachOrCoverFailures(string preset)
        {
            var world=World("Tough");world.bodies[0].region=FixtureBuilder.Concave();var result=Explode(world,preset);
            Assert.That(result.bodies.Sum(b=>Geometry.Area(b.region)),Is.LessThan(Geometry.Area(world.bodies[0].region)));
            foreach(var body in result.bodies)Assert.That(Geometry.TryValidate(body.region,out var error),Is.True,error);
        }
    }
}
