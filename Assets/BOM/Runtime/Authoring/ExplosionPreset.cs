using UnityEngine;

namespace BOM
{
    [CreateAssetMenu(menuName = "BOM/Shared explosion preset")]
    public sealed class ExplosionPreset : ScriptableObject
    {
        [Tooltip("Maximum radius in metres. Resistance and shielding can shorten the blast.")]
        [Min(.001f)] public float radius = 2;
        [Tooltip("Distance budget along each blast ray. Air costs 1 per metre; solid material adds its blast resistance.")]
        [Min(.001f)] public float reachBudget = 4;
        [Tooltip("Speed added to affected surviving pieces, in metres per second, multiplied by the response and piece push settings.")]
        [Min(0)] public float impulseSpeed = 1;
        public bool TryValidate(out string error)
        {
            error = Geometry.Positive(radius) && Geometry.Positive(reachBudget) && Geometry.Nonnegative(impulseSpeed)
                ? null : "Radius and reach budget must be positive; impulse speed must be nonnegative. All values must be finite.";
            return error == null;
        }
    }
}
