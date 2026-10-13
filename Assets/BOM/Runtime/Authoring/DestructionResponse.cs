using UnityEngine;

namespace BOM
{
    [CreateAssetMenu(menuName = "BOM/Destruction response")]
    public sealed class DestructionResponse : ScriptableObject
    {
        [Tooltip("Added blast speed multiplier. 0 disables push; 1 uses the preset speed. Density affects mass, not this added speed.")]
        [Min(0)] public float impulseScale = 1;
        public bool TryValidate(out string error)
        {
            error = Geometry.Nonnegative(impulseScale) ? null : "Impulse scale must be finite and nonnegative.";
            return error == null;
        }
    }
}
