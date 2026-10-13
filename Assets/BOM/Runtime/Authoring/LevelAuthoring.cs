using System.Collections.Generic;
using UnityEngine;

namespace BOM
{
    [DisallowMultipleComponent]
    public sealed class LevelAuthoring : MonoBehaviour
    {
        public List<PiecePlacement> foundations = new List<PiecePlacement>();
        public List<ExplosionPreset> testPresets = new List<ExplosionPreset>();
        public Vector2 gravity = new Vector2(0,-9.81f);
        public bool startOnPlay = true;
        public PlaytestSession Session { get; private set; }
        public string LastReport { get; private set; }
        private void Start() { if (startOnPlay) StartTest(); }
        public bool StartTest()
        {
            if (!Application.isPlaying) { LastReport = "Enter Play mode to run the actual 2D simulation."; return false; }
            if (!LevelCompiler.TryCompile(this, out var resolved, out var report)) { LastReport = report.ToString(); return false; }
            var candidate = new PlaytestSession();
            if (!candidate.TryStart(resolved, out var error)) { candidate.Dispose(); LastReport = error; return false; }
            Session?.Dispose(); Session = candidate; LastReport = "Playtest started."; return true;
        }
        public bool ResetTest()
        {
            if (Session == null) { LastReport = "Start a playtest first."; return false; }
            bool result = Session.TryReset(out var error); LastReport = result ? "Reset to the configuration captured at start." : error; return result;
        }
        public bool Explode(Vector2 origin, int preset = 0)
        {
            if (Session == null) { LastReport = "Start a playtest first."; return false; }
            bool result = Session.TryExplode(origin, preset, out var error); LastReport = result ? "Explosion committed." : error; return result;
        }
        private void FixedUpdate()
        {
            if (Session != null && !Session.TryStep(out var error)) { LastReport = error; enabled = false; }
        }
        private void OnDestroy() { Session?.Dispose(); Session = null; }
    }
}
