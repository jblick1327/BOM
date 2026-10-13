using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BOM
{
    [Serializable]
    public sealed class Contour
    {
        public List<Vector2> points = new List<Vector2>();
        public List<bool> cutEdges = new List<bool>();
        public Contour() { }
        public Contour(IEnumerable<Vector2> vertices) { points = vertices.ToList(); }
        public Contour Clone() => new Contour(points) { cutEdges = new List<bool>(cutEdges) };
    }

    [Serializable]
    public sealed class Region
    {
        public Contour outer = new Contour();
        public List<Contour> holes = new List<Contour>();
        public IEnumerable<Contour> Contours => new[] { outer }.Concat(holes);
        public Region Clone() => new Region { outer = outer.Clone(), holes = holes.Select(h => h.Clone()).ToList() };
    }

    public enum ProjectionPlane { XY, XZ, YZ }
    public enum DestructionMode { Indestructible, Subtract }

    [Serializable]
    public sealed class ImportSettings
    {
        public string sourceGuid, sourcePath, sourceHash;
        public ProjectionPlane plane;
        public Vector3 orientation;
        public float scale = 1;
        public Vector2 origin;
        public bool manuallyCorrected;
        public ImportSettings Clone() => (ImportSettings)MemberwiseClone();
    }

    [Serializable]
    public sealed class DestructionSelection
    {
        public DestructionMode mode = DestructionMode.Subtract;
        public DestructionResponse response;
        public float impulseMultiplier = 1;
        public DestructionSelection Clone() => (DestructionSelection)MemberwiseClone();
    }

    [Serializable]
    public sealed class FaceAppearance
    {
        public Texture2D image;
        public string approvedImagePng;
        public Color face = new Color(.72f, .65f, .48f, 1);
        public Color side = new Color(.36f, .3f, .2f, 1);
        public Color cut = new Color(.85f, .75f, .54f, 1);
        public Vector2 u = Vector2.right, v = Vector2.up, offset;
        public TextureWrapMode wrapMode = TextureWrapMode.Repeat;
        public FilterMode filterMode = FilterMode.Bilinear;
        public FaceAppearance Clone() => (FaceAppearance)MemberwiseClone();
    }

    [Serializable]
    public sealed class PieceDraft
    {
        public ImportSettings import = new ImportSettings();
        public Region profile = new Region();
        public List<Region> disconnectedProposal = new List<Region>();
        public float thickness = .25f;
        public FaceAppearance appearance = new FaceAppearance();
        public MaterialDefinition material;
        public DestructionSelection destruction = new DestructionSelection();
        public bool grabEligible = true;
        public PieceDraft Clone() => new PieceDraft { import = import.Clone(), profile = profile.Clone(),
            disconnectedProposal = disconnectedProposal.Select(r => r.Clone()).ToList(), thickness = thickness,
            appearance = appearance.Clone(), material = material, destruction = destruction.Clone(), grabEligible = grabEligible };
    }

    public sealed class ValidationIssue
    {
        public UnityEngine.Object target;
        public string field, explanation, correction;
        public override string ToString() => field + ": " + explanation + " " + correction;
    }

    public sealed class ValidationReport
    {
        public readonly List<ValidationIssue> issues = new List<ValidationIssue>();
        public bool IsValid => issues.Count == 0;
        public void Add(UnityEngine.Object target, string field, string explanation, string correction) =>
            issues.Add(new ValidationIssue { target = target, field = field, explanation = explanation, correction = correction });
        public override string ToString() => string.Join("\n", issues.Select(i => i.ToString()));
    }

    public static class AuthoringValidation
    {
        public static ValidationReport Piece(PieceDraft draft, UnityEngine.Object target = null)
        {
            var report = new ValidationReport();
            if (draft == null) { report.Add(target, "Piece", "Missing draft.", "Create an import proposal."); return report; }
            if (!Geometry.TryValidate(draft.profile, out var error))
                report.Add(target, "Profile", error, "Correct the named contour or vertex in the profile editor.");
            if (draft.disconnectedProposal == null || draft.disconnectedProposal.Count > 0)
                report.Add(target, "Disconnected projection", "One imported asset must have one connected solid region.", "Correct the profile, then explicitly use the corrected outer contour as the sole region.");
            if (!Geometry.Positive(draft.thickness)) report.Add(target, "Visual thickness", "Must be finite and positive.", "Enter a thickness in metres.");
            if (draft.material == null) report.Add(target, "Material", "A gameplay material is required.", "Select a material definition.");
            else if (!draft.material.TryValidate(out error)) report.Add(draft.material, "Material", error, "Correct density, resistance or physical settings.");
            ValidateDestruction(draft.destruction, target, report);
            if (draft.import == null || !Geometry.Positive(draft.import.scale) || !Geometry.Finite(draft.import.origin)
                || !Geometry.Finite(draft.import.orientation) || !Enum.IsDefined(typeof(ProjectionPlane), draft.import.plane))
                report.Add(target, "Import transform", "Invalid projection, orientation, scale or origin.", "Use finite values and a positive source scale.");
            var a = draft.appearance;
            if (a == null || !Geometry.Finite(a.u) || !Geometry.Finite(a.v) || !Geometry.Finite(a.offset)
                || Math.Abs(Geometry.Cross(a.u, a.v)) < Geometry.AreaTolerance || !Geometry.Finite(a.face)
                || !Geometry.Finite(a.side) || !Geometry.Finite(a.cut))
                report.Add(target, "Appearance", "Colours and a nonsingular planar UV mapping are required.", "Correct the face U/V vectors, offset and colours.");
            return report;
        }

        public static void ValidateDestruction(DestructionSelection selection, UnityEngine.Object target, ValidationReport report)
        {
            if (selection == null || !Enum.IsDefined(typeof(DestructionMode), selection.mode)
                || !Geometry.Nonnegative(selection.impulseMultiplier))
                report.Add(target, "Destruction", "Invalid response selection or impulse multiplier.", "Choose a working response and a nonnegative multiplier.");
            else if (selection.mode == DestructionMode.Subtract && (selection.response == null || !selection.response.TryValidate(out var error)))
                report.Add(target, "Destruction response", "Select a valid subtraction response.", "Create or select a BOM destruction response asset.");
        }
    }
}
