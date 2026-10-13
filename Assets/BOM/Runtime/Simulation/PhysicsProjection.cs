using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BOM
{
    internal sealed class PhysicsProjection : IDisposable
    {
        public Scene Scene {get;}
        private Prepared active;
        public PhysicsProjection()
        {
            if(!Application.isPlaying) throw new InvalidOperationException("BOM's physics projection requires Play mode.");
            Scene=SceneManager.CreateScene("BOM local test "+Guid.NewGuid().ToString("N"),new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        }
        internal sealed class Prepared : IDisposable
        {
            public GameObject root;
            public readonly Dictionary<string,Rigidbody2D> bodies=new Dictionary<string,Rigidbody2D>();
            public readonly Dictionary<string,HingeJoint2D[]> bonds=new Dictionary<string,HingeJoint2D[]>();
            public readonly HashSet<UnityEngine.Object> owned=new HashSet<UnityEngine.Object>();
            public void Dispose()
            {if(root!=null) {root.SetActive(false);ObjectLifetime.Release(root);}foreach(var asset in owned) ObjectLifetime.Release(asset);root=null;owned.Clear();}
        }
        public bool TryPrepare(WorldState world,out Prepared prepared,out string error)
        {
            prepared=null;if(!SnapshotCodec.TryValidate(world,out error)) return false;
            var stage=new Prepared();
            try
            {
                stage.root=new GameObject("BOM runtime (managed)") {hideFlags=HideFlags.DontSave};stage.root.SetActive(false);
                SceneManager.MoveGameObjectToScene(stage.root,Scene);
                foreach(var state in world.bodies)
                {
                    var entity=new GameObject(state.label+" ["+state.id+"]");entity.transform.SetParent(stage.root.transform,false);
                    entity.transform.SetPositionAndRotation(new Vector3(state.position.x,state.position.y,0),Quaternion.Euler(0,0,state.angle*Mathf.Rad2Deg));
                    var mesh=Extrusion.Build(state);stage.owned.Add(mesh);entity.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var materials=Extrusion.Materials(world.definitions.Appearance(state.appearance),out var texture);
                    foreach(var material in materials) stage.owned.Add(material);if(texture!=null) stage.owned.Add(texture);
                    entity.AddComponent<MeshRenderer>().sharedMaterials=materials;
                    entity.AddComponent<RuntimePieceView>().Initialize(state.region);
                    var definition=world.definitions.Material(state.material);
                    var physical=new PhysicsMaterial2D("BOM resolved gameplay material") {friction=definition.friction,bounciness=definition.restitution,hideFlags=HideFlags.DontSave};stage.owned.Add(physical);
                    foreach(var triangle in Geometry.Triangles(state.region))
                    {var collider=entity.AddComponent<PolygonCollider2D>();collider.SetPath(0,triangle);collider.sharedMaterial=physical;}
                    var body=entity.AddComponent<Rigidbody2D>();
                    body.bodyType=world.configuration.foundations.Contains(state.id)?RigidbodyType2D.Static:RigidbodyType2D.Dynamic;
                    body.useAutoMass=false;body.mass=(float)(Geometry.Area(state.region)*definition.density);
                    body.centerOfMass=Geometry.Centroid(state.region);body.inertia=(float)(Geometry.Moment(state.region)*definition.density);
                    body.gravityScale=0;body.linearDamping=definition.linearDamping;body.angularDamping=definition.angularDamping;
                    body.interpolation=RigidbodyInterpolation2D.None;body.collisionDetectionMode=CollisionDetectionMode2D.Continuous;
                    if(body.bodyType==RigidbodyType2D.Dynamic)
                    {
                        float expected=(float)(Geometry.Area(state.region)*definition.density);
                        if(Mathf.Abs(body.mass-expected)>Mathf.Max(.000001f,expected*.00001f))
                            throw new InvalidOperationException("Body "+state.id+": Unity cannot represent its derived mass. Use representable fixture dimensions/density.");
                        body.linearVelocity=state.velocity;body.angularVelocity=state.angularVelocity*Mathf.Rad2Deg;
                    }
                    stage.bodies.Add(state.id,body);
                }
                foreach(var bond in world.bonds)
                {
                    var a=stage.bodies[bond.a];var b=stage.bodies[bond.b];
                    a.gameObject.AddComponent<RuntimeBondView>().Initialize(b.transform,bond);
                    if(a.bodyType==RigidbodyType2D.Static&&b.bodyType==RigidbodyType2D.Static) {stage.bonds.Add(bond.id,Array.Empty<HingeJoint2D>());continue;}
                    bool reverse=a.bodyType==RigidbodyType2D.Static;
                    var owner=reverse?b:a;var connected=reverse?a:b;
                    HingeJoint2D Pin(Vector2 anchor,Vector2 other)
                    {
                        var pin=owner.gameObject.AddComponent<HingeJoint2D>();pin.connectedBody=connected;
                        pin.autoConfigureConnectedAnchor=false;pin.anchor=anchor;pin.connectedAnchor=other;
                        pin.useLimits=false;pin.useMotor=false;pin.enableCollision=false;
                        pin.breakForce=float.PositiveInfinity;pin.breakTorque=float.PositiveInfinity;return pin;
                    }
                    stage.bonds.Add(bond.id,new[] {Pin(reverse?bond.b0:bond.a0,reverse?bond.a0:bond.b0),Pin(reverse?bond.b1:bond.a1,reverse?bond.a1:bond.b1)});
                }
                prepared=stage;error=null;return true;
            }
            catch(Exception e) {stage.Dispose();error="Runtime preparation: "+e.Message;return false;}
        }
        public void Publish(Prepared next)
        {var old=active;if(old?.root!=null) old.root.SetActive(false);active=next;active.root.SetActive(true);old?.Dispose();}
        public Rigidbody2D Body(string id) => active!=null&&active.bodies.TryGetValue(id,out var body)?body:null;
        public bool Step(WorldState world,out List<string> failed,out string error)
        {
            failed=new List<string>();error=null;
            if(active==null) {error="No prepared runtime world. Start a playtest.";return false;}
            foreach(var body in active.bodies.Values) if(body.bodyType==RigidbodyType2D.Dynamic) body.AddForce(body.mass*world.configuration.gravity);
            Physics2D.SyncTransforms();
            if(!Scene.GetPhysicsScene2D().Simulate(world.configuration.fixedStep)) {error="Local PhysicsScene2D refused its configured step.";return false;}
            foreach(var state in world.bodies)
            {var body=active.bodies[state.id];state.position=body.position;state.angle=body.rotation*Mathf.Deg2Rad;state.velocity=body.linearVelocity;state.angularVelocity=body.angularVelocity*Mathf.Deg2Rad;}
            foreach(var bond in world.bonds)
            {
                var pins=active.bonds[bond.id];if(pins.Length==0) continue;
                var midpoint=(Vector2)pins[0].transform.TransformPoint((pins[0].anchor+pins[1].anchor)*.5f);
                var force=Vector2.zero;double moment=0;
                foreach(var pin in pins)
                {var reaction=pin.GetReactionForce(world.configuration.fixedStep);force+=reaction;
                    moment+=Geometry.Cross((Vector2)pin.transform.TransformPoint(pin.anchor)-midpoint,reaction)+pin.GetReactionTorque(world.configuration.fixedStep);}
                if(force.magnitude>bond.ForceCapacity||Math.Abs(moment)>bond.MomentCapacity) failed.Add(bond.id);
            }
            return true;
        }
        public void Dispose()
        {active?.Dispose();active=null;if(Scene.IsValid()&&Scene.isLoaded) SceneManager.UnloadSceneAsync(Scene);}
    }
}
