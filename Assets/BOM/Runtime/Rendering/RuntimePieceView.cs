using UnityEngine;

namespace BOM
{
    [AddComponentMenu("")]
    public sealed class RuntimePieceView : MonoBehaviour
    {
        private Region region;
        internal void Initialize(Region current) {region=current;}
        private void OnDrawGizmos()
        {
            if(region==null) return;Gizmos.color=Color.cyan;
            foreach(var contour in region.Contours)
            {
                foreach(var edge in Geometry.Edges(contour)) Gizmos.DrawLine(transform.TransformPoint(edge.a),transform.TransformPoint(edge.b));
                Gizmos.color=new Color(1,.65f,.15f);
            }
        }
    }
}
