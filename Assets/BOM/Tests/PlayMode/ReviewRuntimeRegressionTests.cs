using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BOM.Tests
{
    [Category("ReviewRegression")]
    public sealed class ReviewRuntimeRegressionTests
    {
        private readonly List<PlaytestSession> sessions=new List<PlaytestSession>();
        private PlaytestSession Session(WorldState state)
        {
            var session=new PlaytestSession();sessions.Add(session);
            Assert.That(session.TryRecover(SnapshotCodec.Serialize(state),out var error),Is.True,error);return session;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {foreach(var session in sessions) session.Dispose();sessions.Clear();yield return null;}
        [UnityTest] public IEnumerator R3_RegressedAllocatorRecoveryRetainsWorldAndValidHighWaterAllocatesFreshFragments()
        {
            var world=AcceptanceFixtures.Single(Geometry.Rectangle(-2,-.25f,2,.25f),1,2);var session=Session(world);
            string parent=world.bodies[0].id;var original=session.Body(parent);string before=SnapshotCodec.Serialize(session.ReadCommitted());
            var invalid=world.Clone();invalid.nextSequence=1;
            Assert.That(session.TryRecover(SnapshotCodec.Serialize(invalid),out var error),Is.False);Assert.That(error,Does.Contain("allocator"));
            Assert.That(session.Body(parent),Is.SameAs(original));Assert.That(SnapshotCodec.Serialize(session.ReadCommitted()),Is.EqualTo(before));
            var valid=world.Clone();valid.nextSequence=100;
            Assert.That(session.TryRecover(SnapshotCodec.Serialize(valid),out error),Is.True,error);
            Assert.That(session.TryExplode(new Vector2(0,.6f),0,out error),Is.True,error);
            var split=session.ReadCommitted();Assert.That(split.bodies.Select(b=>b.id),Is.EquivalentTo(new[] {"acceptance:piece:100","acceptance:piece:101"}));
            Assert.That(split.nextSequence,Is.EqualTo(102));Assert.That(split.Body(parent),Is.Null);yield return null;
        }
        private static double AnchorError(WorldState world)
        {return world.bonds.Max(b=>Math.Max(Vector2.Distance(world.Body(b.a).ToWorld(b.a0),world.Body(b.b).ToWorld(b.b0)),
            Vector2.Distance(world.Body(b.a).ToWorld(b.a1),world.Body(b.b).ToWorld(b.b1))));}
        [UnityTest] public IEnumerator V1_RealConnectedBlastFreshReconstructionPreservesBondsLifetimeAndDisplacedIntendedFit()
        {
            var world=AcceptanceFixtures.ConnectedSplit();var originalBond=world.bonds[0].Clone();string parent=world.bodies[0].id,upperId=world.bodies[1].id;
            world.bodies[1].position+=new Vector2(.04f,.03f);world.bodies[1].angle+=.02f;
            var source=Session(world);Assert.That(source.TryExplode(new Vector2(0,-.2f),0,out var error),Is.True,error);
            var current=source.ReadCommitted();Assert.That(current.bodies,Has.Count.EqualTo(3));Assert.That(current.bonds,Has.Count.EqualTo(2));
            Assert.That(current.Body(parent),Is.Null);Assert.That(current.configuration.foundations,Has.Count.EqualTo(2));
            Assert.That(current.bonds.Sum(b=>b.Length),Is.GreaterThan(originalBond.referenceLength*originalBond.minimumFraction));
            foreach(var bond in current.bonds)
            {
                Assert.That(bond.id,Is.Not.EqualTo(originalBond.id));Assert.That(bond.a,Is.Not.EqualTo(parent));Assert.That(bond.b,Is.EqualTo(upperId));
                Assert.That(bond.restTranslation,Is.EqualTo(originalBond.restTranslation));Assert.That(bond.restAngle,Is.EqualTo(originalBond.restAngle));
                Assert.That(bond.attachmentLoss,Is.True);Assert.That(bond.minimumFraction,Is.EqualTo(originalBond.minimumFraction));
                Assert.That(bond.referenceLength,Is.EqualTo(bond.Length).Within(Geometry.ContactTolerance));
                Assert.That(Geometry.FullBoundary(current.Body(bond.a).region,bond.a0,bond.a1),Is.True);
                Assert.That(Geometry.FullBoundary(current.Body(bond.b).region,bond.b0,bond.b1),Is.True);
            }
            Assert.That(AnchorError(current),Is.GreaterThan(.02));
            var captureClock=System.Diagnostics.Stopwatch.StartNew();string capture=source.Capture(true);captureClock.Stop();
            var fresh=new PlaytestSession();sessions.Add(fresh);var rebuildClock=System.Diagnostics.Stopwatch.StartNew();
            Assert.That(fresh.TryRecover(capture,out error),Is.True,error);rebuildClock.Stop();var reconstructed=fresh.ReadCommitted();
            Assert.That(SnapshotCodec.TryRead(capture,out var captured,out error),Is.True,error);
            Assert.That(SnapshotCodec.Serialize(reconstructed),Is.EqualTo(SnapshotCodec.Serialize(captured)));
            foreach(var bond in reconstructed.bonds)
            {
                var pins=fresh.Body(bond.b).GetComponents<HingeJoint2D>().Where(p=>p.connectedBody==fresh.Body(bond.a)).ToArray();
                Assert.That(pins,Has.Length.EqualTo(2));Assert.That(pins.All(p=>!p.autoConfigureConnectedAnchor&&!p.enableCollision),Is.True);
                Assert.That(pins.Any(p=>Vector2.Distance(p.anchor,bond.b0)<Geometry.ContactTolerance&&Vector2.Distance(p.connectedAnchor,bond.a0)<Geometry.ContactTolerance),Is.True);
                Assert.That(pins.Any(p=>Vector2.Distance(p.anchor,bond.b1)<Geometry.ContactTolerance&&Vector2.Distance(p.connectedAnchor,bond.a1)<Geometry.ContactTolerance),Is.True);
            }
            double before=AnchorError(reconstructed);
            for(int i=0;i<25;i++) Assert.That(fresh.TryStep(out error),Is.True,error);
            Assert.That(fresh.BondCount,Is.EqualTo(2));Assert.That(AnchorError(fresh.ReadCommitted()),Is.LessThan(.003));
            foreach(var bond in fresh.ReadCommitted().bonds)
            {Assert.That(bond.restTranslation,Is.EqualTo(originalBond.restTranslation));Assert.That(bond.restAngle,Is.EqualTo(originalBond.restAngle));}
            Directory.CreateDirectory("Docs/Verification/Results");File.WriteAllText("Docs/Verification/Results/AC11-connected-post-split-world.json",capture);
            File.WriteAllText("Docs/Verification/Results/AC11-connected-reconstruction-metrics.json","{\"unity\":\""+Application.unityVersion+"\",\"bodyCount\":"+reconstructed.bodies.Count
                +",\"connectorCount\":"+reconstructed.bonds.Count+",\"vertexCount\":"+reconstructed.bodies.Sum(b=>b.region.Contours.Sum(c=>c.points.Count))
                +",\"generatedPinCount\":"+fresh.Body(upperId).GetComponents<HingeJoint2D>().Length+",\"initialAnchorError\":"+before.ToString(CultureInfo.InvariantCulture)
                +",\"settledAnchorError\":"+AnchorError(fresh.ReadCommitted()).ToString(CultureInfo.InvariantCulture)+",\"captureMilliseconds\":"+captureClock.Elapsed.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)
                +",\"reconstructionMilliseconds\":"+rebuildClock.Elapsed.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)+"}");yield return null;
        }
        [UnityTest] public IEnumerator V3_MomentDominatedActualSolverLoadFailsBondBelowItsForceCapacity()
        {
            var world=AcceptanceFixtures.Bonded();world.configuration.foundations.Add(world.bodies[0].id);var bond=world.bonds[0];
            bond.a0=bond.b0=new Vector2(-.25f,1);bond.a1=bond.b1=new Vector2(.25f,1);bond.referenceLength=.5f;bond.EstablishFit();
            using(var projection=new PhysicsProjection())
            {
                Assert.That(projection.TryPrepare(world,out var prepared,out var error),Is.True,error);projection.Publish(prepared);
                var body=projection.Body(bond.b);var pins=body.GetComponents<HingeJoint2D>();body.AddTorque(50);
                Assert.That(projection.Step(world,out var failed,out error),Is.True,error);
                var midpoint=(Vector2)body.transform.TransformPoint((pins[0].anchor+pins[1].anchor)*.5f);var force=Vector2.zero;double moment=0;
                foreach(var pin in pins)
                {var reaction=pin.GetReactionForce(world.configuration.fixedStep);force+=reaction;
                    moment+=Geometry.Cross((Vector2)pin.transform.TransformPoint(pin.anchor)-midpoint,reaction)+pin.GetReactionTorque(world.configuration.fixedStep);}
                Assert.That(force.magnitude,Is.LessThan(bond.ForceCapacity));Assert.That(Math.Abs(moment),Is.GreaterThan(bond.MomentCapacity));
                Assert.That(failed,Does.Contain(bond.id));Directory.CreateDirectory("Docs/Verification/Results");
                File.WriteAllText("Docs/Verification/Results/moment-load-metrics.json","{\"appliedTorque\":50,\"pairedLength\":0.5,\"strength\":100,\"reactionForce\":"+force.magnitude.ToString(CultureInfo.InvariantCulture)
                    +",\"forceCapacity\":"+bond.ForceCapacity.ToString(CultureInfo.InvariantCulture)+",\"reactionMoment\":"+Math.Abs(moment).ToString(CultureInfo.InvariantCulture)
                    +",\"momentCapacity\":"+bond.MomentCapacity.ToString(CultureInfo.InvariantCulture)+",\"failed\":true}");
            }
            yield return null;
        }
    }
}
