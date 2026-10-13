using UnityEngine;

namespace BOM
{
    [CreateAssetMenu(menuName = "BOM/Gameplay material")]
    public sealed class MaterialDefinition : ScriptableObject
    {
        [Min(.00001f)] public float density = 2;
        [Min(0)] public float blastResistance = 1;
        [Min(0)] public float friction = .5f;
        [Range(0, 1)] public float restitution;
        [Min(0)] public float linearDamping = .1f, angularDamping = .1f;
        public bool TryValidate(out string error)
        {
            error = "Density must be positive; resistance, friction and damping must be finite and nonnegative; restitution must be in [0,1].";
            if (!Geometry.Positive(density) || !Geometry.Nonnegative(blastResistance) || !Geometry.Nonnegative(friction)
                || !Geometry.Nonnegative(restitution) || restitution > 1 || !Geometry.Nonnegative(linearDamping) || !Geometry.Nonnegative(angularDamping)) return false;
            error = null; return true;
        }
    }
}
