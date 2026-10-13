using UnityEngine;

namespace BOM
{
    [CreateAssetMenu(menuName = "BOM/Shared explosion preset")]
    public sealed class ExplosionPreset : ScriptableObject
    {
        [Min(.001f)] public float radius = 2;
        [Min(.001f)] public float reachBudget = 2;
        [Min(0)] public float impulseSpeed = 1;
        public bool TryValidate(out string error)
        {
            error = Geometry.Positive(radius) && Geometry.Positive(reachBudget) && Geometry.Nonnegative(impulseSpeed)
                ? null : "Radius and reach budget must be positive; impulse speed must be nonnegative. All values must be finite.";
            return error == null;
        }
    }
}
