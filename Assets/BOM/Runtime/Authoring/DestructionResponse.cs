using UnityEngine;

namespace BOM
{
    [CreateAssetMenu(menuName = "BOM/Destruction response")]
    public sealed class DestructionResponse : ScriptableObject
    {
        [Tooltip("Velocity added to surviving material per unit of the shared test preset's impulse speed.")]
        [Min(0)] public float impulseScale = 1;
        public bool TryValidate(out string error)
        {
            error = Geometry.Nonnegative(impulseScale) ? null : "Impulse scale must be finite and nonnegative.";
            return error == null;
        }
    }
}
