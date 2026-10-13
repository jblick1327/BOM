using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BOM
{
    public readonly struct PlacementPose
    {
        public readonly Vector2 position, scale;
        public readonly float angle;
        public PlacementPose(Vector2 position, Vector2 scale, float angle) { this.position = position; this.scale = scale; this.angle = angle; }
        public Vector2 Bake(Vector2 p) => Vector2.Scale(p, scale);
        public Vector2 ToWorld(Vector2 p) => position + Geometry.Rotate(Bake(p), angle);
    }

    public static class LevelCompiler
    {
        public static bool TryPose(PiecePlacement placement, out PlacementPose pose, out string error)
        {
            pose = default; error = "Use positive X/Y scale, Z scale 1, position in XY, and rotation only around Z. Remove parent shear.";
            for (var t = placement.transform; t != null; t = t.parent)
                if (!Geometry.Finite(t.localScale) || t.localScale.x <= 0 || t.localScale.y <= 0 || Mathf.Abs(t.localScale.z - 1) > Geometry.ContactTolerance) return false;
            var x = placement.transform.TransformVector(Vector3.right); var y = placement.transform.TransformVector(Vector3.up);
            var position = placement.transform.position;
            if (!Geometry.Finite(position) || !Geometry.Finite(x) || !Geometry.Finite(y) || Mathf.Abs(position.z) > Geometry.ContactTolerance
                || Mathf.Abs(x.z) > Geometry.ContactTolerance || Mathf.Abs(y.z) > Geometry.ContactTolerance
                || Vector3.Dot(x.normalized, y.normalized) > Geometry.ContactTolerance
                || Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) > Geometry.ContactTolerance
                || Vector3.Cross(x,y).z <= 0) return false;
            pose = new PlacementPose(position, new Vector2(x.magnitude,y.magnitude), Mathf.Atan2(x.y,x.x));
            error = null; return true;
        }

        public static bool TryCompile(LevelAuthoring level, out ResolvedLevel resolved, out ValidationReport report)
        {
            resolved = null; report = new ValidationReport();
            if (level == null) { report.Add(null, "Level", "No level selected.", "Add or select a BOM level authoring component."); return false; }
            var world = new WorldState { match = Guid.NewGuid().ToString("N") };
            world.configuration.gravity = level.gravity;
            var placements = level.GetComponentsInChildren<PiecePlacement>(true);
            if (placements.Length == 0) report.Add(level, "Placements", "The level contains no pieces.", "Place an approved piece below this level.");
            if (!Geometry.Finite(level.gravity)) report.Add(level, "Gravity", "Gravity must be finite.", "Correct the world gravity vector.");
            var map = new Dictionary<PiecePlacement,BodyState>(); var poses = new Dictionary<PiecePlacement,PlacementPose>();
            foreach (var placement in placements)
            {
                if (placement.piece == null || !placement.piece.IsApproved)
                { report.Add(placement, "Piece", "Missing approved piece asset.", "Select and approve a piece definition."); continue; }
                var draft = placement.piece.Draft();
                var pieceReport = AuthoringValidation.Piece(draft, placement.piece); report.issues.AddRange(pieceReport.issues);
                var material = placement.EffectiveMaterial; var destruction = placement.EffectiveDestruction;
                if (material == null || !material.TryValidate(out _)) report.Add(placement, "Effective material", "The selected material is missing or invalid.", "Select a valid material or reset the override.");
                AuthoringValidation.ValidateDestruction(destruction, placement, report);
                if (!TryPose(placement, out var pose, out var poseError)) { report.Add(placement, "Placement transform", poseError, "Correct the transform in the inspector."); continue; }
                var conflicts = placement.GetComponentsInChildren<Component>(true).Where(c => c != null
                    && c.GetComponentInParent<PiecePlacement>() == placement
                    && (c is Rigidbody2D || c is Collider2D || c is Joint2D || c is MeshFilter || c is MeshRenderer)
                    && (c.gameObject.hideFlags & HideFlags.DontSaveInEditor) == 0).ToArray();
                if (conflicts.Length > 0) report.Add(placement, "Managed components", "Found designer-added physics or render components: "
                    + string.Join(", ",conflicts.Select(c=>c.GetType().Name)), "Remove the conflicting components; BOM generates them from the approved profile.");
                if (!pieceReport.IsValid || material == null || !material.TryValidate(out _) || destruction == null
                    || (destruction.mode == DestructionMode.Subtract && destruction.response == null)) continue;
                var m = new MaterialRecord { label = material.name, density = material.density, blastResistance = material.blastResistance,
                    friction = material.friction, restitution = material.restitution, linearDamping = material.linearDamping, angularDamping = material.angularDamping };
                m.id = Fingerprints.Definition(m); if (world.definitions.Material(m.id) == null) world.definitions.materials.Add(m);
                ResponseRecord response = null;
                if (destruction.mode == DestructionMode.Subtract)
                {
                    response = new ResponseRecord { label = destruction.response.name, impulseScale = destruction.response.impulseScale };
                    response.id = Fingerprints.Definition(response); if (world.definitions.Response(response.id) == null) world.definitions.responses.Add(response);
                }
                var appearance = new AppearanceRecord { label = placement.piece.name + " v" + placement.piece.Version,
                    face = draft.appearance.face, side = draft.appearance.side, cut = draft.appearance.cut,
                    imagePng = draft.appearance.approvedImagePng, wrapMode = draft.appearance.wrapMode, filterMode = draft.appearance.filterMode };
                appearance.id = Fingerprints.Definition(appearance); if (world.definitions.Appearance(appearance.id) == null) world.definitions.appearances.Add(appearance);
                var body = new BodyState { id = world.Allocate("piece"), label = placement.name,
                    region = Geometry.Transform(draft.profile,pose.Bake), position = pose.position, angle = pose.angle,
                    material = m.id, response = response?.id, appearance = appearance.id, thickness = draft.thickness,
                    destruction = destruction.mode, impulseMultiplier = destruction.impulseMultiplier, grabEligible = placement.EffectiveGrab,
                    uvU = new Vector2(draft.appearance.u.x/pose.scale.x,draft.appearance.u.y/pose.scale.y),
                    uvV = new Vector2(draft.appearance.v.x/pose.scale.x,draft.appearance.v.y/pose.scale.y), uvOffset = draft.appearance.offset };
                if (!Geometry.TryValidate(body.region,out var scaledError)) report.Add(placement,"Scaled profile",scaledError,"Correct the placement scale or approved profile.");
                else try { Geometry.Triangles(body.region); } catch(Exception e) { report.Add(placement,"Triangulation",e.Message,"Correct the profile or use representable placement dimensions."); }
                world.bodies.Add(body); map.Add(placement,body); poses.Add(placement,pose);
            }
            if (level.foundations == null) report.Add(level,"Foundations","Missing foundation list.","Restore the level foundation list.");
            else foreach (var foundation in level.foundations)
            {
                if (foundation == null || !map.TryGetValue(foundation,out var fixedBody)) report.Add(level,"Foundations","Unresolved fixed support.","Select a valid placement inside this level.");
                else if (world.configuration.foundations.Contains(fixedBody.id)) report.Add(level,"Foundations","Duplicate fixed support.","Remove the duplicate foundation reference.");
                else world.configuration.foundations.Add(fixedBody.id);
            }
            foreach (var connection in level.GetComponentsInChildren<ConnectionAuthoring>(true))
            {
                if (connection.a == null || connection.b == null || !map.TryGetValue(connection.a,out var a) || !map.TryGetValue(connection.b,out var b) || a == b)
                { report.Add(connection,"Endpoints","Missing, identical or outside-level endpoints.","Select two distinct valid placements in this level."); continue; }
                var pa=poses[connection.a]; var pb=poses[connection.b];
                var bond = new BondState { id = world.Allocate("bond"), a = a.id, b = b.id,
                    a0 = pa.Bake(connection.a0), a1 = pa.Bake(connection.a1), b0 = pb.Bake(connection.b0), b1 = pb.Bake(connection.b1),
                    strength = connection.strength, attachmentLoss = connection.attachmentLoss, minimumFraction = connection.minimumFraction,
                    referenceLength = connection.referenceLength, restTranslation = connection.restTranslation, restAngle = connection.restAngle };
                if (!Geometry.FullBoundary(a.region,bond.a0,bond.a1) || !Geometry.FullBoundary(b.region,bond.b0,bond.b1)
                    || Vector2.Distance(a.ToWorld(bond.a0),b.ToWorld(bond.b0)) > Geometry.ContactTolerance
                    || Vector2.Distance(a.ToWorld(bond.a1),b.ToWorld(bond.b1)) > Geometry.ContactTolerance)
                    report.Add(connection,"Attachment regions","The paired boundary no longer exists or fits after editing.","Review and confirm a new paired region, or remove this bond.");
                var expected = bond.Clone(); expected.EstablishFit();
                if(!ConnectionTools.TryValidateSeparation(Geometry.Transform(a.region,a.ToWorld),Geometry.Transform(b.region,b.ToWorld),out var overlapError))
                    report.Add(connection,"Solid overlap",overlapError,"Move the endpoints and review a new paired region before confirming this bond.");
                if (Vector2.Distance(expected.restTranslation,bond.restTranslation)>Geometry.ContactTolerance
                    || Mathf.Abs(Mathf.DeltaAngle(expected.restAngle*Mathf.Rad2Deg,bond.restAngle*Mathf.Rad2Deg))>.01f)
                    report.Add(connection,"Intended fit","Scaling or attachment edits changed the saved rigid fit.","Review and confirm the updated bond.");
                world.bonds.Add(bond);
            }
            if (level.testPresets == null || level.testPresets.Count == 0) report.Add(level,"Test presets","At least one explicit shared preset is required.","Assign a BOM explosion preset asset.");
            else foreach(var preset in level.testPresets)
            {
                if (preset == null || !preset.TryValidate(out _)) report.Add(level,"Test preset","Missing or invalid shared preset.","Correct or select a radius/budget/impulse preset.");
                else world.configuration.testPresets.Add(new TestPresetRecord { label=preset.name,radius=preset.radius,reachBudget=preset.reachBudget,impulseSpeed=preset.impulseSpeed });
            }
            if (!report.IsValid) return false;
            if (!SnapshotCodec.TryValidate(world,out var error)) { report.Add(level,"Resolved scene",error,"Correct the referenced authoring fields and compile again."); return false; }
            resolved = new ResolvedLevel(world); return true;
        }
    }
}
