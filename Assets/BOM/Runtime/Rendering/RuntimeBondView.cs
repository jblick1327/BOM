using UnityEngine;

namespace BOM
{
    [AddComponentMenu("")]
    public sealed class RuntimeBondView : MonoBehaviour
    {
        private Transform other;
        private BondState bond;
        internal void Initialize(Transform endpoint,BondState current) {other=endpoint;bond=current;}
        private void OnDrawGizmos()
        {
            if(other==null||bond==null) return;Gizmos.color=Color.green;
            Gizmos.DrawLine(transform.TransformPoint(bond.a0),transform.TransformPoint(bond.a1));
            Gizmos.DrawLine(other.TransformPoint(bond.b0),other.TransformPoint(bond.b1));
        }
    }
}
