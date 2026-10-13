using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BOM.Tests
{
    public static class AcceptanceFixtures
    {
        public const float PositionTolerance=.0001f;
        public static WorldState Single(Region region,float radius=.8f,float budget=5,Vector2? position=null)
        {
            var world=new WorldState {match="acceptance"};world.configuration.gravity=Vector2.zero;
            var material=new MaterialRecord {label="verification density 2, resistance 0",density=2,blastResistance=0,friction=.5f};material.id=Fingerprints.Definition(material);
            var response=new ResponseRecord {label="verification subtraction",impulseScale=0};response.id=Fingerprints.Definition(response);
            var appearance=new AppearanceRecord {label="verification planar appearance",face=Color.white,side=Color.blue,cut=Color.red,
                wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Point};appearance.id=Fingerprints.Definition(appearance);
            world.definitions.materials.Add(material);world.definitions.responses.Add(response);world.definitions.appearances.Add(appearance);
            world.configuration.testPresets.Add(new TestPresetRecord {label="explicit acceptance input",radius=radius,reachBudget=budget,impulseSpeed=0});
            world.bodies.Add(new BodyState {id=world.Allocate("piece"),label="verification piece",region=Geometry.Normalize(region),position=position??Vector2.zero,
                material=material.id,response=response.id,appearance=appearance.id,destruction=DestructionMode.Subtract,thickness=.25f,impulseMultiplier=1,
                uvU=new Vector2(.25f,.1f),uvV=new Vector2(-.05f,.5f),uvOffset=new Vector2(.3f,.2f),grabEligible=true});
            return world;
        }
        public static WorldState Bonded(bool percentage=false,float fraction=.6f)
        {
            var world=Single(Geometry.Rectangle(-2,0,2,1),1.2f,5);var a=world.bodies[0];a.label="lower piece";
            var b=a.Clone();b.id=world.Allocate("piece");b.label="upper piece";b.region=Geometry.Rectangle(-2,1,2,2);world.bodies.Add(b);
            var bond=new BondState {id=world.Allocate("bond"),a=a.id,b=b.id,a0=new Vector2(-2,1),a1=new Vector2(2,1),
                b0=new Vector2(-2,1),b1=new Vector2(2,1),strength=100,attachmentLoss=percentage,minimumFraction=fraction,referenceLength=4};
            bond.EstablishFit();world.bonds.Add(bond);return world;
        }
        public static WorldState ConnectedSplit()
        {
            var world=Bonded(true,.35f);world.configuration.testPresets[0].radius=1.6f;
            var upper=world.bodies[1];var offset=new Vector2(.5f,1);float angle=Mathf.PI*.5f;
            upper.region=Geometry.Transform(upper.region,p=>Geometry.Rotate(p-offset,-angle));upper.position=offset;upper.angle=angle;
            var bond=world.bonds[0];bond.b0=Geometry.Rotate(bond.b0-offset,-angle);bond.b1=Geometry.Rotate(bond.b1-offset,-angle);
            bond.EstablishFit();bond.strength=10000000;world.configuration.foundations.Add(world.bodies[0].id);return world;
        }
        public static string Save()
        {
            Directory.CreateDirectory("Docs/Verification/Fixtures");
            var fixtures=new[] {
                ("AC03-frame",Single(BOM.Editor.FixtureBuilder.Frame())),
                ("AC03-concave-L",Single(BOM.Editor.FixtureBuilder.Concave())),
                ("AC04-small-bite",Single(Geometry.Rectangle(-2,-1,2,1),.8f,2)),
                ("AC05-split",Single(Geometry.Rectangle(-2,-.25f,2,.25f),1,2)),
                ("AC05-removal",Single(Geometry.Rectangle(-.5f,-.5f,.5f,.5f),3,10)),
                ("AC09-bond",Bonded()),("AC09-percentage-reference",Bonded(true,.8f)),("AC11-connected-split",ConnectedSplit())};
            foreach(var fixture in fixtures) File.WriteAllText("Docs/Verification/Fixtures/"+fixture.Item1+".json",SnapshotCodec.Serialize(fixture.Item2,true));
            var manifest=new {unity=Application.unityVersion,positionTolerance=PositionTolerance,radialTolerance=Blast.RadialTolerance,
                frameMass=16,fixtureNames=fixtures.Select(f=>f.Item1).ToArray()};
            return string.Join(", ",manifest.fixtureNames);
        }
    }
}
