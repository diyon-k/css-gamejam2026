using Blink.TimeSkip;
using UnityEngine;

namespace Blink.Mobs
{
    /// <summary>
    /// Makes falling ragdolls jump forward with a blink: on a skip it steps the physics scene by hand
    /// for the skipped time (capped at <see cref="maxSimulatedSeconds"/>), so a body that was mid-fall
    /// is on the floor when the eyes open. Only runs while a <see cref="ZombieRagdoll"/> is still
    /// moving. Put one in the scene (it's on GameSystems).
    ///
    /// Note: this steps the whole physics scene, so any other loose Rigidbody jumps forward too.
    /// </summary>
    public sealed class RagdollSkipper : MonoBehaviour, ISkippable
    {
        [Tooltip("Most world seconds of physics simulated for one skip. Each fixed step costs a full physics step.")]
        [SerializeField, Min(0f)] float maxSimulatedSeconds = 0.5f;

        void OnEnable() => TimeSkipController.Register(this);
        void OnDisable() => TimeSkipController.Unregister(this);

        public void Skip(float seconds)
        {
            if (ZombieRagdoll.ActiveCount <= 0) return;
            float step = Time.fixedDeltaTime;
            int steps = RagdollMath.SkipSteps(seconds, maxSimulatedSeconds, step);
            if (steps == 0) return;

            // Physics.Simulate only works in Script mode, so switch for the duration of the skip.
            var mode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                for (int i = 0; i < steps; i++) Physics.Simulate(step);
            }
            finally
            {
                Physics.simulationMode = mode;
            }
        }
    }
}
