using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace BOM
{
    [Serializable] public class DefinitionRecord { public string id, label; }
    [Serializable] public sealed class MaterialRecord : DefinitionRecord
    {
        public float density, blastResistance, friction, restitution, linearDamping, angularDamping;
    }
    [Serializable] public sealed class ResponseRecord : DefinitionRecord { public float impulseScale; }
    [Serializable] public sealed class AppearanceRecord : DefinitionRecord
    {
        public Color face, side, cut;
        public string imagePng;
        public TextureWrapMode wrapMode;
        public FilterMode filterMode;
    }
    [Serializable] public sealed class DefinitionClosure
    {
        public List<MaterialRecord> materials = new List<MaterialRecord>();
        public List<ResponseRecord> responses = new List<ResponseRecord>();
        public List<AppearanceRecord> appearances = new List<AppearanceRecord>();
        public MaterialRecord Material(string id) => materials.FirstOrDefault(m => m.id == id);
        public ResponseRecord Response(string id) => responses.FirstOrDefault(m => m.id == id);
        public AppearanceRecord Appearance(string id) => appearances.FirstOrDefault(m => m.id == id);
    }
    [Serializable] public sealed class BodyState
    {
        public string id, label, material, response, appearance;
        public uint geometryRevision = 1;
        public Region region;
        public Vector2 position, velocity;
        public float angle, angularVelocity, thickness, impulseMultiplier;
        public DestructionMode destruction;
        public bool grabEligible;
        public Vector2 uvU, uvV, uvOffset;
        public Vector2 ToWorld(Vector2 point) => position + Geometry.Rotate(point, angle);
        public Vector2 ToLocal(Vector2 point) => Geometry.Rotate(point - position, -angle);
        public Vector2 UV(Vector2 local) => new Vector2(Vector2.Dot(uvU, local), Vector2.Dot(uvV, local)) + uvOffset;
        public BodyState Clone() => JsonUtility.FromJson<BodyState>(JsonUtility.ToJson(this));
    }
    [Serializable] public sealed class BondState
    {
        public string id, a, b;
        public Vector2 a0, a1, b0, b1, restTranslation;
        public float restAngle, strength, referenceLength, minimumFraction;
        public bool attachmentLoss;
        public float Length => Mathf.Min(Vector2.Distance(a0, a1), Vector2.Distance(b0, b1));
        public float ForceCapacity => strength * Length;
        public float MomentCapacity => .5f * strength * Length * Length;
        public BondState Clone() => JsonUtility.FromJson<BondState>(JsonUtility.ToJson(this));
        public bool FailsLength(float surviving) => surviving <= Geometry.ContactTolerance
            || (attachmentLoss && surviving + Geometry.ContactTolerance < referenceLength * minimumFraction);
        public void EstablishFit()
        {
            var da = a1 - a0; var db = b1 - b0;
            restAngle = Mathf.Atan2(da.y, da.x) - Mathf.Atan2(db.y, db.x);
            restTranslation = a0 - Geometry.Rotate(b0, restAngle);
        }
    }
    [Serializable] public sealed class TestPresetRecord
    {
        public string label;
        public float radius, reachBudget, impulseSpeed;
    }
    [Serializable] public sealed class WorldConfiguration
    {
        public Vector2 gravity = new Vector2(0, -9.81f);
        public float fixedStep = .02f;
        public List<string> foundations = new List<string>();
        public List<TestPresetRecord> testPresets = new List<TestPresetRecord>();
    }
    [Serializable] public sealed class WorldState
    {
        public int schemaVersion = 1;
        public string match;
        public long nextSequence = 1, captureNumber;
        public DefinitionClosure definitions = new DefinitionClosure();
        public WorldConfiguration configuration = new WorldConfiguration();
        public List<BodyState> bodies = new List<BodyState>();
        public List<BondState> bonds = new List<BondState>();
        public BodyState Body(string id) => bodies.FirstOrDefault(b => b.id == id);
        public string Allocate(string kind)
        {
            if (nextSequence == long.MaxValue) throw new InvalidOperationException("Match identity sequence exhausted.");
            return match + ":" + kind + ":" + (nextSequence++).ToString(CultureInfo.InvariantCulture);
        }
        public WorldState Clone() => JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(this));
    }

    public sealed class ResolvedLevel
    {
        public string InitialJson { get; }
        internal ResolvedLevel(WorldState state) { InitialJson = JsonUtility.ToJson(state); }
        public WorldState Read() => JsonUtility.FromJson<WorldState>(InitialJson);
    }

    public static class Fingerprints
    {
        public static string Hash(string text)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
        public static string Definition(DefinitionRecord record)
        {
            string id = record.id; record.id = "";
            string result = Hash(JsonUtility.ToJson(record)); record.id = id; return result;
        }
    }

    public static class SnapshotCodec
    {
        private static bool ValidIdentity(string id,string match,string kind,long nextSequence,HashSet<string> ids)
        {
            string prefix=match+":"+kind+":";
            if(id==null||!id.StartsWith(prefix,StringComparison.Ordinal)) return false;
            string suffix=id.Substring(prefix.Length);
            return long.TryParse(suffix,NumberStyles.None,CultureInfo.InvariantCulture,out var sequence)&&sequence>0&&sequence<nextSequence
                &&suffix==sequence.ToString(CultureInfo.InvariantCulture)&&ids.Add(id);
        }
        public static string Serialize(WorldState world, bool pretty = false) => JsonUtility.ToJson(world, pretty);
        public static bool TryRead(string json, out WorldState world, out string error)
        {
            world = null;
            try
            {
                if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{")) throw new ArgumentException("Expected a JSON world snapshot.");
                var candidate = JsonUtility.FromJson<WorldState>(json);
                if (!TryValidate(candidate, out error)) return false;
                world = candidate; return true;
            }
            catch (Exception e) { error = "Snapshot: " + e.Message + " Supply a complete current snapshot and its definition closure."; return false; }
        }
        public static bool TryValidate(WorldState state, out string error)
        {
            error = "World envelope: unsupported schema or missing current data. Supply schema 1 and a complete definition closure.";
            if (state == null || state.schemaVersion != 1 || string.IsNullOrWhiteSpace(state.match) || state.nextSequence < 1 || state.captureNumber < 0
                || state.bodies == null || state.bonds == null || state.configuration?.foundations == null
                || state.configuration.testPresets == null || state.definitions?.materials == null || state.definitions.responses == null
                || state.definitions.appearances == null || !Geometry.Finite(state.configuration.gravity)
                || Mathf.Abs(state.configuration.fixedStep - .02f) > 1e-7f) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in state.definitions.materials.Cast<DefinitionRecord>().Concat(state.definitions.responses).Concat(state.definitions.appearances))
            {
                if (d == null || string.IsNullOrWhiteSpace(d.id) || d.id != Fingerprints.Definition(d) || !ids.Add(d.id))
                { error = "Definition closure: missing, duplicate or changed content hash. Recompile the referenced definitions."; return false; }
            }
            foreach (var m in state.definitions.materials)
                if (!Geometry.Positive(m.density) || !Geometry.Nonnegative(m.blastResistance) || !Geometry.Nonnegative(m.friction)
                    || !Geometry.Nonnegative(m.restitution) || m.restitution > 1 || !Geometry.Nonnegative(m.linearDamping) || !Geometry.Nonnegative(m.angularDamping))
                { error = "Material " + m.id + ": invalid physical inputs. Correct the material definition."; return false; }
            foreach (var r in state.definitions.responses)
                if (!Geometry.Nonnegative(r.impulseScale)) { error = "Response " + r.id + ": invalid impulse scale. Correct the response definition."; return false; }
            foreach (var a in state.definitions.appearances)
            {
                if (!Geometry.Finite(a.face) || !Geometry.Finite(a.side) || !Geometry.Finite(a.cut) || !Enum.IsDefined(typeof(TextureWrapMode), a.wrapMode)
                    || !Enum.IsDefined(typeof(FilterMode), a.filterMode))
                { error = "Appearance " + a.id + ": invalid colours or sampling. Review the appearance."; return false; }
                try { if (!string.IsNullOrEmpty(a.imagePng) && Convert.FromBase64String(a.imagePng).Length < 8) throw new FormatException(); }
                catch { error = "Appearance " + a.id + ": invalid image data. Approve the face image again."; return false; }
            }
            ids.Clear();
            foreach (var b in state.bodies)
            {
                error = "Body identity/allocator: use unique match:piece:sequence IDs and nextSequence above every allocated sequence. Supply the current identity high-water mark.";
                if (b == null || !ValidIdentity(b.id,state.match,"piece",state.nextSequence,ids)) return false;
                if (!Geometry.TryValidate(b.region, out var geometryError)) { error = "Body " + b.id + ", profile: " + geometryError; return false; }
                if (b.geometryRevision == 0 || !Geometry.Finite(b.position) || !Geometry.Finite(b.velocity) || !Geometry.Finite(b.angle)
                    || !Geometry.Finite(b.angularVelocity) || !Geometry.Positive(b.thickness) || !Geometry.Nonnegative(b.impulseMultiplier)
                    || !Geometry.Finite(b.uvU) || !Geometry.Finite(b.uvV) || !Geometry.Finite(b.uvOffset)
                    || Math.Abs(Geometry.Cross(b.uvU, b.uvV)) < 1e-12 || !Enum.IsDefined(typeof(DestructionMode), b.destruction)
                    || state.definitions.Material(b.material) == null || state.definitions.Appearance(b.appearance) == null
                    || (b.destruction == DestructionMode.Subtract && state.definitions.Response(b.response) == null))
                { error = "Body " + b.id + ": missing definition or invalid pose, appearance, thickness or response. Correct its referenced fields."; return false; }
                foreach (var contour in b.region.Contours)
                    if (contour.cutEdges.Count != 0 && contour.cutEdges.Count != contour.points.Count)
                    { error = "Body " + b.id + ": boundary appearance metadata is incomplete. Supply one edge tag per contour edge."; return false; }
            }
            foreach (var bond in state.bonds)
            {
                error = "Connection identity/allocator: use unique match:bond:sequence IDs and nextSequence above every allocated sequence. Supply the current identity high-water mark.";
                if (bond == null || !ValidIdentity(bond.id,state.match,"bond",state.nextSequence,ids)) return false;
                var a = state.Body(bond.a); var b = state.Body(bond.b);
                if (a == null || b == null || a == b || !Geometry.Positive(bond.strength) || !Geometry.Finite(bond.a0) || !Geometry.Finite(bond.a1)
                    || !Geometry.Finite(bond.b0) || !Geometry.Finite(bond.b1) || !Geometry.FullBoundary(a.region, bond.a0, bond.a1)
                    || !Geometry.FullBoundary(b.region, bond.b0, bond.b1)
                    || Math.Abs(Vector2.Distance(bond.a0,bond.a1) - Vector2.Distance(bond.b0,bond.b1)) > Geometry.ContactTolerance
                    || (bond.attachmentLoss && (!Geometry.Nonnegative(bond.minimumFraction) || bond.minimumFraction > 1
                        || !Geometry.Positive(bond.referenceLength) || bond.referenceLength + Geometry.ContactTolerance < bond.Length || bond.FailsLength(bond.Length))))
                { error = "Connection " + bond.id + ": invalid endpoint, boundary, strength or lifetime reference. Select and correct the bond."; return false; }
                var fit = bond.Clone(); fit.EstablishFit();
                if (!Geometry.Finite(bond.restTranslation) || !Geometry.Finite(bond.restAngle)
                    || Vector2.Distance(fit.restTranslation, bond.restTranslation) > Geometry.ContactTolerance
                    || Mathf.Abs(Mathf.DeltaAngle(fit.restAngle * Mathf.Rad2Deg, bond.restAngle * Mathf.Rad2Deg)) > .01f)
                { error = "Connection " + bond.id + ": intended rigid fit conflicts with paired regions. Correct the saved fit."; return false; }
            }
            if (state.configuration.foundations.Distinct().Count() != state.configuration.foundations.Count
                || state.configuration.foundations.Any(id => state.Body(id) == null))
            { error = "Foundations: unresolved or duplicate body reference. Correct the level foundation list."; return false; }
            foreach (var p in state.configuration.testPresets)
                if (p == null || !Geometry.Positive(p.radius) || !Geometry.Positive(p.reachBudget) || !Geometry.Nonnegative(p.impulseSpeed))
                { error = "Test preset: invalid radius, budget or impulse. Correct the shared explosion preset."; return false; }
            error = null; return true;
        }
    }
}
