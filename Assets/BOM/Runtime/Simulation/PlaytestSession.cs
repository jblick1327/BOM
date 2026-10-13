using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BOM
{
    public sealed class PlaytestSession : IDisposable
    {
        private PhysicsProjection projection;
        private WorldState world;
        private string initial;
        private long captureNumber;
        public int Steps {get;private set;}
        public int BodyCount => world?.bodies.Count??0;
        public int BondCount => world?.bonds.Count??0;
        public string Match => world?.match;
        public Rigidbody2D Body(string id) => projection?.Body(id);
        public WorldState ReadCommitted() => world?.Clone();
        public bool TryStart(ResolvedLevel level,out string error)
        {
            error="Missing resolved level.";if(level==null) return false;
            if(!TryRecover(level.InitialJson,out error)) return false;initial=level.InitialJson;Steps=0;return true;
        }
        public bool TryRecover(string snapshot,out string error)
        {
            if(!Application.isPlaying) {error="Enter Play mode to reconstruct an actual physics world.";return false;}
            if(!SnapshotCodec.TryRead(snapshot,out var candidate,out error)) return false;
            bool fresh=projection==null;var target=projection??new PhysicsProjection();
            if(!target.TryPrepare(candidate,out var prepared,out error)) {if(fresh) target.Dispose();return false;}
            target.Publish(prepared);projection=target;world=candidate;captureNumber=Math.Max(captureNumber,candidate.captureNumber);return true;
        }
        private bool TryCommit(WorldState candidate,out string error)
        {
            if(!projection.TryPrepare(candidate,out var prepared,out error)) return false;
            projection.Publish(prepared);world=candidate;return true;
        }
        public bool TryReset(out string error)
        {if(initial==null) {error="No captured initial level. Start a playtest first.";return false;}bool ok=TryRecover(initial,out error);if(ok) Steps=0;return ok;}
        public bool TryExplode(Vector2 origin,int preset,out string error)
        {
            if(world==null) {error="No playtest world. Start a test first.";return false;}
            if(!Blast.TryApply(world,origin,preset,out var candidate,out error)) return false;
            return TryCommit(candidate,out error);
        }
        public bool TryStep(out string error)
        {
            if(world==null) {error="No playtest world.";return false;}
            if(!projection.Step(world,out var failed,out error)) return false;
            Steps++;
            if(failed.Count==0) return true;
            var candidate=world.Clone();candidate.bonds.RemoveAll(b=>failed.Contains(b.id));return TryCommit(candidate,out error);
        }
        public string Capture(bool pretty=false)
        {
            if(world==null) throw new InvalidOperationException("No committed world to capture.");
            var current=world.Clone();current.captureNumber=checked(++captureNumber);return SnapshotCodec.Serialize(current,pretty);
        }
        public void Dispose() {projection?.Dispose();projection=null;world=null;initial=null;}
    }
}
