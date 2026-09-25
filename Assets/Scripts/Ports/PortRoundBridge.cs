using UnityEngine;
using VRSurgery.Session;

namespace VRSurgery.Ports
{
    /// <summary>
    /// Joins the booth's round to the procedure.
    ///
    /// Neither side knows about the other by design: the session owns the clock, the states and
    /// the scoreboard, and the procedure owns what "all four ports dealt with" means. This is the
    /// only place that says finishing the procedure wins the round, and that a new visitor gets
    /// four untouched sites.
    /// </summary>
    public class PortRoundBridge : MonoBehaviour
    {
        [SerializeField] private EventSessionController session;
        [SerializeField] private PortProcedure procedure;

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (procedure != null) { procedure.ProcedureCompleted += HandleProcedureCompleted; }
            if (session != null) { session.StateChanged += HandleSessionState; }
        }

        private void Unsubscribe()
        {
            if (procedure != null) { procedure.ProcedureCompleted -= HandleProcedureCompleted; }
            if (session != null) { session.StateChanged -= HandleSessionState; }
        }

        /// <summary>
        /// Every site resolved. Raised on the shared bus rather than called on the session
        /// directly, because that is the signal the session already listens for to end a round
        /// as a win — and it keeps this class from having to know how winning is implemented.
        /// </summary>
        private void HandleProcedureCompleted() => Surgery.SurgeryEvents.RaiseSurgeryCompleted();

        private void HandleSessionState(SessionState state)
        {
            // Wiped at the briefing, not at the end of the previous round: the result and the
            // scoreboard are still on the projection then, and clearing the wall underneath them
            // would show the audience the next patient before the last one's time is read out.
            if (state == SessionState.Briefing && procedure != null)
            {
                procedure.ResetProcedure();
            }
        }

        public void Bind(EventSessionController controller, PortProcedure portProcedure)
        {
            Unsubscribe();
            session = controller;
            procedure = portProcedure;

            if (isActiveAndEnabled)
            {
                Subscribe();
            }
        }
    }
}
