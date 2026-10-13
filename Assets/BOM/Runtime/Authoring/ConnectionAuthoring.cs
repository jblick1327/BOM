using UnityEngine;

namespace BOM
{
    [DisallowMultipleComponent]
    public sealed class ConnectionAuthoring : MonoBehaviour
    {
        public PiecePlacement a, b;
        public Vector2 a0, a1, b0, b1;
        [Min(.00001f)] public float strength = 100;
        public bool attachmentLoss;
        [Range(0,1)] public float minimumFraction;
        [HideInInspector] public float referenceLength;
        [HideInInspector] public Vector2 restTranslation;
        [HideInInspector] public float restAngle;
        private void OnDrawGizmos()
        {
            if (Application.isPlaying) return;
            Gizmos.color = a != null && b != null ? Color.green : Color.red;
            if (a != null) { Gizmos.DrawLine(a.transform.TransformPoint(a0), a.transform.TransformPoint(a1));
                Gizmos.DrawSphere(a.transform.TransformPoint((a0+a1)*.5f), .06f); }
            if (b != null) Gizmos.DrawLine(b.transform.TransformPoint(b0), b.transform.TransformPoint(b1));
        }
    }
}
