using UnityEngine;

namespace BOM
{
    [CreateAssetMenu(menuName = "BOM/Piece definition")]
    public sealed class PieceDefinition : ScriptableObject
    {
        [SerializeField, HideInInspector] private PieceDraft approved;
        [SerializeField, HideInInspector] private int approvedVersion;
        public int Version => approvedVersion;
        public bool IsApproved => approved != null && approvedVersion > 0;
        public PieceDraft Draft() => approved?.Clone() ?? new PieceDraft();
        public bool TryApprove(PieceDraft draft, out ValidationReport report)
        {
            report = AuthoringValidation.Piece(draft, this);
            if (!report.IsValid) return false;
            if (approvedVersion == int.MaxValue)
            { report.Add(this,"Version","Asset version sequence exhausted.","Create a new piece definition."); return false; }
            approved = draft.Clone();
            approved.profile = Geometry.Normalize(approved.profile);
            checked { approvedVersion++; }
            return true;
        }
    }
}
