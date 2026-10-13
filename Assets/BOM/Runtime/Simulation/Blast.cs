using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using UnityEngine;

namespace BOM
{
    public static class Blast
    {
        public const double RadialTolerance = .001;
        public const int MaximumRays = 8192;
        private readonly struct MaterialSpan
        {
            public readonly double lo,hi,resistance;
            public readonly bool opaque;
            public MaterialSpan(double lo,double hi,double resistance,bool opaque)
            {this.lo=lo;this.hi=hi;this.resistance=resistance;this.opaque=opaque;}
        }
        private static List<MaterialSpan> Spans(WorldState world,Vector2 origin,double angle,float radius)
        {
            var direction=new Vector2((float)Math.Cos(angle),(float)Math.Sin(angle));var result=new List<MaterialSpan>();
            foreach(var body in world.bodies)
                foreach(var span in Geometry.SegmentRanges(body.region,body.ToLocal(origin),body.ToLocal(origin+direction*radius)))
                    result.Add(new MaterialSpan(span.lo*radius,span.hi*radius,world.definitions.Material(body.material).blastResistance,body.destruction==DestructionMode.Indestructible));
            return result;
        }
        public static double Reach(WorldState world,Vector2 origin,double angle,TestPresetRecord preset)
        {
            var spans=Spans(world,origin,angle,preset.radius);var events=new List<double> {0,preset.radius};
            foreach(var s in spans) {events.Add(s.lo);events.Add(s.hi);}events=events.Distinct().OrderBy(v=>v).ToList();
            double budget=preset.reachBudget;
            for(int i=0;i+1<events.Count;i++)
            {
                double lo=events[i],hi=events[i+1],mid=(lo+hi)*.5;
                var material=spans.Where(s=>mid>s.lo&&mid<s.hi).ToArray();
                if(material.Any(s=>s.opaque)) return lo;
                double rate=1+material.Sum(s=>s.resistance),cost=(hi-lo)*rate;
                if(cost>=budget) return lo+budget/rate;
                budget-=cost;
            }
            return preset.radius;
        }
        public static bool ReachableOrigin(WorldState world,Vector2 origin,out string error)
        {
            error="Explosion origin must be reachable free space. Choose an exterior air position, outside solid material and enclosed cavities.";
            if(!Geometry.Finite(origin)) return false;
            var occupied=new Paths64();
            foreach(var body in world.bodies)
            {
                var region=Geometry.Transform(body.region,body.ToWorld);
                if(Geometry.Contains(region,origin)) return false;
                occupied.AddRange(Geometry.Paths(region));
            }
            foreach(var region in Geometry.Boolean(occupied,null,ClipType.Union))
                if(Clipper.PointInPolygon(Geometry.Point(origin),Geometry.Path(region.outer))!=PointInPolygonResult.IsOutside) return false;
            error=null;return true;
        }
        private static double Angle(Vector2 p)
        {double angle=Math.Atan2(p.y,p.x);return angle<0?angle+2*Math.PI:angle;}
        private static Region Cutter(WorldState world,Vector2 origin,TestPresetRecord preset)
        {
            var events=new SortedSet<double> {0,2*Math.PI};
            int count=Math.Max(128,(int)Math.Ceiling(Math.PI*Math.Sqrt(preset.radius/(RadialTolerance*.25))));
            if(count>MaximumRays) throw new InvalidOperationException("Explosion radius exceeds the 8192-ray geometry budget. Use a smaller local test preset.");
            for(int i=1;i<count;i++) events.Add(2*Math.PI*i/count);
            foreach(var body in world.bodies) foreach(var contour in body.region.Contours) foreach(var point in contour.points)
            {
                double angle=Angle(body.ToWorld(point)-origin);events.Add(angle);
                if(angle>1e-7) events.Add(angle-1e-7);if(angle<2*Math.PI-1e-7) events.Add(angle+1e-7);
            }
            var cache=new Dictionary<double,double>();
            double Radius(double angle)
            {if(!cache.TryGetValue(angle,out var r)) cache.Add(angle,r=Math.Max(0,Reach(world,origin,angle,preset)-Geometry.Tolerance*8));return r;}
            double Chord(double a,double b,double theta)
            {
                var p=new Vector2((float)(Math.Cos(a)*Radius(a)),(float)(Math.Sin(a)*Radius(a)));
                var q=new Vector2((float)(Math.Cos(b)*Radius(b)),(float)(Math.Sin(b)*Radius(b)));
                var d=new Vector2((float)Math.Cos(theta),(float)Math.Sin(theta));
                double divisor=Geometry.Cross(q-p,d);
                return Math.Abs(divisor)<1e-15?Math.Min(Radius(a),Radius(b)):Geometry.Cross(q-p,p)/divisor;
            }
            bool changed=true;
            while(changed)
            {
                changed=false;var angles=events.ToArray();
                for(int i=0;i+1<angles.Length;i++)
                {
                    double a=angles[i],b=angles[i+1];if(b-a<1e-6) continue;
                    foreach(double fraction in new[] {.25,.5,.75})
                    {
                        double theta=a+(b-a)*fraction,r=Radius(theta),represented=Chord(a,b,theta);
                        if(represented>r+Geometry.Tolerance||r-represented>RadialTolerance*.5)
                        {events.Add((a+b)*.5);changed=true;break;}
                    }
                }
                if(events.Count>MaximumRays) throw new InvalidOperationException("Blast geometry exceeds its explicit ray budget near an occlusion. Move the test origin or simplify this fixture.");
            }
            var points=events.Where(a=>a<2*Math.PI).Select(a=>origin+new Vector2((float)(Math.Cos(a)*Radius(a)),(float)(Math.Sin(a)*Radius(a))));
            var cutter=Geometry.Normalize(new Region {outer=new Contour(points)});
            var ring=cutter.outer;int vertex=0;
            while(vertex<ring.points.Count&&ring.points.Count>3)
            {
                int next=(vertex+1)%ring.points.Count;
                if(Vector2.Distance(ring.points[vertex],ring.points[next])>Geometry.Tolerance*2){vertex++;continue;}
                int remove=(ring.points[vertex]-origin).sqrMagnitude<=(ring.points[next]-origin).sqrMagnitude?next:vertex;
                ring.points.RemoveAt(remove);ring.cutEdges.RemoveAt(remove);vertex=Math.Max(0,vertex-1);
            }
            if(!Geometry.TryValidate(cutter,out var error)) throw new InvalidOperationException("Explosion field: "+error+" Move the origin farther from a surface.");
            return cutter;
        }
        public static bool TryApply(WorldState current,Vector2 origin,int presetIndex,out WorldState candidate,out string error)
        {
            candidate=null;error=null;
            try
            {
                if(presetIndex<0||presetIndex>=current.configuration.testPresets.Count) throw new ArgumentException("Select a shared preset captured at playtest start.");
                if(!ReachableOrigin(current,origin,out error)) return false;
                var preset=current.configuration.testPresets[presetIndex];var cutter=Cutter(current,origin,preset);
                var next=current.Clone();var replacements=new Dictionary<string,List<BodyState>>();
                foreach(var source in current.bodies)
                {
                    if(source.destruction==DestructionMode.Indestructible) continue;
                    var pieces=Geometry.Difference(source.region,Geometry.Transform(cutter,source.ToLocal));
                    if(Math.Abs(pieces.Sum(Geometry.Area)-Geometry.Area(source.region))<=Geometry.AreaTolerance) continue;
                    var result=new List<BodyState>();var oldCentre=Geometry.Centroid(source.region);
                    foreach(var piece in pieces)
                    {
                        if(!Geometry.TryValidate(piece,out error)) throw new InvalidOperationException("Body "+source.id+", surviving profile: "+error);
                        var child=source.Clone();child.id=pieces.Count==1?source.id:next.Allocate("piece");
                        child.region=piece;child.geometryRevision=checked(source.geometryRevision+1);
                        var change=Geometry.Rotate(Geometry.Centroid(piece)-oldCentre,source.angle);
                        child.velocity+=new Vector2(-source.angularVelocity*change.y,source.angularVelocity*change.x);
                        if(!current.configuration.foundations.Contains(source.id))
                        {
                            var direction=(source.ToWorld(Geometry.Centroid(piece))-origin).normalized;
                            child.velocity+=direction*preset.impulseSpeed*source.impulseMultiplier*current.definitions.Response(source.response).impulseScale;
                        }
                        result.Add(child);
                    }
                    replacements.Add(source.id,result);
                }
                next.bodies=current.bodies.SelectMany(b=>replacements.TryGetValue(b.id,out var result)?result:new List<BodyState> {b.Clone()}).ToList();
                next.configuration.foundations=current.configuration.foundations.SelectMany(id=>replacements.TryGetValue(id,out var result)
                    ?result.Select(b=>b.id):new[] {id}).ToList();
                RemapBonds(current,next,replacements);
                if(!ValidateBreach(current,next,origin,preset,out error)) return false;
                if(!SnapshotCodec.TryValidate(next,out error)) return false;
                candidate=next;return true;
            }
            catch(Exception e) {error="Explosion: "+e.Message+" The previous committed world is retained.";return false;}
        }
        internal static void RemapBonds(WorldState current,WorldState next,Dictionary<string,List<BodyState>> replacements)
        {
            next.bonds.Clear();
            IEnumerable<BodyState> Endpoints(string id) => replacements.TryGetValue(id,out var bodies)?bodies:new[] {next.Body(id)};
            foreach(var old in current.bonds)
            {
                var survivors=new List<BondState>();
                foreach(var a in Endpoints(old.a)) foreach(var b in Endpoints(old.b))
                {
                    if(a==null||b==null) continue;
                    foreach(var ar in Geometry.SegmentRanges(a.region,old.a0,old.a1))
                        foreach(var br in Geometry.SegmentRanges(b.region,old.b0,old.b1))
                        {
                            double lo=Math.Max(ar.lo,br.lo),hi=Math.Min(ar.hi,br.hi);if((hi-lo)*old.Length<=Geometry.ContactTolerance) continue;
                            var bond=old.Clone();bond.a=a.id;bond.b=b.id;
                            bond.a0=Vector2.Lerp(old.a0,old.a1,(float)lo);bond.a1=Vector2.Lerp(old.a0,old.a1,(float)hi);
                            bond.b0=Vector2.Lerp(old.b0,old.b1,(float)lo);bond.b1=Vector2.Lerp(old.b0,old.b1,(float)hi);
                            survivors.Add(bond);
                        }
                }
                if(old.FailsLength(survivors.Sum(b=>b.Length))) continue;
                if(survivors.Count==1) next.bonds.Add(survivors[0]);
                else foreach(var bond in survivors)
                {bond.id=next.Allocate("bond");bond.referenceLength=bond.Length;next.bonds.Add(bond);}
            }
        }
        private static bool ValidateBreach(WorldState original,WorldState result,Vector2 origin,TestPresetRecord preset,out string error)
        {
            var events=new SortedSet<double> {0,2*Math.PI};
            foreach(var body in original.bodies.Concat(result.bodies)) foreach(var contour in body.region.Contours)
                foreach(var p in contour.points) events.Add(Angle(body.ToWorld(p)-origin));
            var angles=events.ToArray();
            if(angles.Length>MaximumRays*4) {error="Breach validation exceeds its geometry budget. Simplify the fixture.";return false;}
            for(int i=0;i+1<angles.Length;i++)
            {
                double theta=(angles[i]+angles[i+1])*.5;var before=Spans(original,origin,theta,preset.radius);
                var after=Spans(result,origin,theta,preset.radius);var stops=new SortedSet<double> {0,preset.radius};
                foreach(var s in before.Concat(after)) {stops.Add(s.lo);stops.Add(s.hi);}var t=stops.ToArray();
                bool retained=false;double reach=Reach(original,origin,theta,preset);
                for(int j=0;j+1<t.Length;j++)
                {
                    double mid=(t[j]+t[j+1])*.5;
                    bool wasSolid=before.Any(s=>mid>s.lo&&mid<s.hi),isSolid=after.Any(s=>mid>s.lo&&mid<s.hi);
                    if(wasSolid&&isSolid) retained=true;
                    else if(wasSolid&&!isSolid&&(retained||t[j+1]>reach+Geometry.ContactTolerance))
                    {error="Blast result would carve beyond retained cover or its original-material reach cost. Move the origin or adjust the shared preset; no state was committed.";return false;}
                }
            }
            error=null;return true;
        }
    }
}
