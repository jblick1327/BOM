using UnityEngine;

namespace BOM
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class PiecePlacement : MonoBehaviour
    {
        public PieceDefinition piece;
        public bool overrideMaterial, overrideDestruction, overrideGrab;
        public MaterialDefinition material;
        public DestructionSelection destruction = new DestructionSelection();
        public bool grabEligible;
        private GameObject preview;
        private Mesh previewMesh;
        private Material[] previewMaterials;
        private Texture2D previewTexture;
        private string previewKey;
        public MaterialDefinition EffectiveMaterial => overrideMaterial ? material : piece != null ? piece.Draft().material : null;
        public DestructionSelection EffectiveDestruction => overrideDestruction ? destruction.Clone() : piece != null ? piece.Draft().destruction : null;
        public bool EffectiveGrab => overrideGrab ? grabEligible : piece != null && piece.Draft().grabEligible;
        public void ResetMaterial() { overrideMaterial = false; material = null; }
        public void ResetDestruction() { overrideDestruction = false; destruction = new DestructionSelection(); }
        public void ResetGrab() { overrideGrab = false; grabEligible = false; }
        private void Update()
        {
            if (Application.isPlaying) { if (preview != null) preview.SetActive(false); return; }
            string key = piece != null ? piece.GetEntityId().ToString() + ":" + piece.Version : "none";
            if (previewKey == key && preview != null) return;
            previewKey = key; ClearPreview();
            if (piece == null || !piece.IsApproved) return;
            var draft = piece.Draft();
            if (!AuthoringValidation.Piece(draft, piece).IsValid) return;
            try
            {
                preview = new GameObject("BOM approved extrusion (managed preview)") { hideFlags = HideFlags.HideAndDontSave };
                preview.transform.SetParent(transform, false);
                var body = Extrusion.PreviewState(draft);
                previewMesh = Extrusion.Build(body);
                previewMaterials = Extrusion.Materials(new AppearanceRecord { face = draft.appearance.face, side = draft.appearance.side,
                    cut = draft.appearance.cut, imagePng = draft.appearance.approvedImagePng, wrapMode = draft.appearance.wrapMode,
                    filterMode = draft.appearance.filterMode }, out previewTexture);
                preview.AddComponent<MeshFilter>().sharedMesh = previewMesh;
                preview.AddComponent<MeshRenderer>().sharedMaterials = previewMaterials;
            }
            catch { ClearPreview(); }
        }
        private void OnDisable() => ClearPreview();
        private void ClearPreview()
        {
            if (preview != null) ObjectLifetime.Release(preview);
            if (previewMesh != null) ObjectLifetime.Release(previewMesh);
            if (previewMaterials != null) foreach (var m in previewMaterials) ObjectLifetime.Release(m);
            if (previewTexture != null) ObjectLifetime.Release(previewTexture);
            preview = null; previewMesh = null; previewMaterials = null; previewTexture = null;
        }
        private void OnDrawGizmos()
        {
            if (Application.isPlaying) return;
            if (piece == null || !piece.IsApproved) return;
            var region = piece.Draft().profile;
            Gizmos.color = new Color(.1f,.85f,1,1);
            foreach (var contour in region.Contours)
            {
                foreach (var edge in Geometry.Edges(contour)) Gizmos.DrawLine(transform.TransformPoint(edge.a), transform.TransformPoint(edge.b));
                Gizmos.color = new Color(1,.65f,.15f,1);
            }
        }
    }
}
