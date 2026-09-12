using System;
using System.Collections.Generic;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// The steps of going on and coming off cardiopulmonary bypass, in the order they are done.
    /// </summary>
    public enum BypassStep
    {
        /// <summary>Patient's own circulation, heart beating. Nothing has been touched.</summary>
        NotStarted,

        /// <summary>Cannulae into the venae cavae and the aorta, so the pump can take over.</summary>
        Cannulate,

        /// <summary>Cross-clamp on the aorta, separating the heart from the pump's pressure.</summary>
        ClampAorta,

        /// <summary>Cardioplegia into the aortic root, which stops and protects the myocardium.</summary>
        Cardioplegia,

        /// <summary>On pump, heart arrested. The only state in which it may be cut out.</summary>
        Arrested,

        /// <summary>Cross-clamp off, coronary flow restored to the new heart.</summary>
        Unclamp,

        /// <summary>Air evacuated from the chambers before it can reach the brain.</summary>
        DeAir,

        /// <summary>Pump flow handed back to the heart.</summary>
        Wean,

        /// <summary>Off bypass. The patient's own circulation again.</summary>
        Off,
    }

    /// <summary>What happened when a step was attempted, and why if it was refused.</summary>
    public readonly struct BypassAttempt
    {
        public readonly bool Accepted;

        /// <summary>Empty when accepted. Otherwise the clinical reason, in pt-BR, for the player.</summary>
        public readonly string Reason;

        public BypassAttempt(bool accepted, string reason = "")
        {
            Accepted = accepted;
            Reason = reason;
        }

        public static BypassAttempt Ok => new BypassAttempt(true);
        public static BypassAttempt No(string reason) => new BypassAttempt(false, reason);
    }

    /// <summary>
    /// Cardiopulmonary bypass as pure state, with no Unity in it.
    ///
    /// This exists because the procedure was clinically wrong without it: between taking the old
    /// heart out and starting the new one, the patient had no circulation at all. Bypass is what
    /// keeps them alive across that gap, and the order of its steps is not a convention — each one
    /// is only safe because the one before it happened.
    ///
    /// Refusals carry the reason rather than just failing, because the reason is the teaching. A
    /// visitor who tries to infuse cardioplegia before clamping should be told that unclamped it
    /// simply washes out of the coronaries, not that the button did nothing.
    ///
    /// No UnityEngine here on purpose: every rule below is testable without a scene, a rig or a
    /// frame, and the MonoBehaviour that knows where the surgeon's hands are lives elsewhere.
    /// </summary>
    public sealed class BypassProcedure
    {
        private static readonly List<BypassStep> Order = new List<BypassStep>
        {
            BypassStep.Cannulate,
            BypassStep.ClampAorta,
            BypassStep.Cardioplegia,
            BypassStep.Unclamp,
            BypassStep.DeAir,
            BypassStep.Wean,
        };

        private int _done;

        public BypassStep Step { get; private set; } = BypassStep.NotStarted;

        /// <summary>Steps completed so far, of six.</summary>
        public int CompletedSteps => _done;

        public int TotalSteps => Order.Count;

        /// <summary>
        /// True once the heart is stopped and the pump is carrying the patient. The only window in
        /// which the native heart may be cut out.
        /// </summary>
        public bool IsArrested => Step == BypassStep.Arrested;

        /// <summary>True once the patient is back on their own circulation.</summary>
        public bool IsOff => Step == BypassStep.Off;

        /// <summary>
        /// True while the pump is carrying the patient — from cannulation through to weaning.
        /// Anything that stops during this window is survivable; before or after, it is not.
        /// </summary>
        public bool IsOnPump => _done >= 1 && Step != BypassStep.Off;

        /// <summary>The step the surgeon should be doing now, or NotStarted/Off at the ends.</summary>
        public BypassStep NextStep => _done < Order.Count ? Order[_done] : BypassStep.Off;

        public event Action<BypassStep> StepCompleted;
        public event Action Arrested;
        public event Action WeanedOff;

        /// <summary>
        /// Tries to perform a step. Out of order, it is refused with the clinical reason.
        ///
        /// The exit half cannot begin until the caller says the implant is finished, because
        /// unclamping onto a heart that is not sewn in would empty the patient into the chest.
        /// </summary>
        public BypassAttempt Attempt(BypassStep step, bool implantComplete = false)
        {
            if (Step == BypassStep.Off)
            {
                return BypassAttempt.No("O paciente já saiu de bomba.");
            }

            BypassAttempt verdict = Why(step, implantComplete);
            if (!verdict.Accepted)
            {
                return verdict;
            }

            _done++;
            Step = step;
            StepCompleted?.Invoke(step);

            // Cardioplegia is the moment the heart stops: the arrest is the consequence of the
            // step, not a step of its own that the surgeon performs.
            if (step == BypassStep.Cardioplegia)
            {
                Step = BypassStep.Arrested;
                Arrested?.Invoke();
            }
            else if (step == BypassStep.Wean)
            {
                Step = BypassStep.Off;
                WeanedOff?.Invoke();
            }

            return BypassAttempt.Ok;
        }

        /// <summary>
        /// What Attempt would say to this step, without doing it.
        ///
        /// The worker needs this so a wrong move is refused the instant the hand arrives, instead
        /// of after the visitor has held still for six seconds waiting to be told it was never
        /// going to work.
        /// </summary>
        public BypassAttempt Why(BypassStep step, bool implantComplete = false)
        {
            if (Step == BypassStep.Off)
            {
                return BypassAttempt.No("O paciente já saiu de bomba.");
            }

            if (step != NextStep)
            {
                return BypassAttempt.No(ReasonFor(step, NextStep));
            }

            if (step == BypassStep.Unclamp && !implantComplete)
            {
                return BypassAttempt.No(
                    "Desclampar antes de terminar as anastomoses esvazia o paciente no tórax — " +
                    "os vasos ainda estão abertos.");
            }

            return BypassAttempt.Ok;
        }

        /// <summary>
        /// Why a step is wrong here. Each of these is the actual clinical consequence, which is
        /// the only part of this worth a visitor's attention.
        /// </summary>
        private static string ReasonFor(BypassStep attempted, BypassStep expected)
        {
            switch (attempted)
            {
                case BypassStep.Cannulate:
                    return "As cânulas já estão no lugar.";

                case BypassStep.ClampAorta:
                    return expected == BypassStep.Cannulate
                        ? "Clampear a aorta sem estar em bomba interrompe a circulação do paciente."
                        : "A aorta já está clampeada.";

                case BypassStep.Cardioplegia:
                    return expected == BypassStep.ClampAorta
                        ? "A cardioplegia só protege com a aorta clampeada — sem o clampe ela é " +
                          "lavada pela circulação e o coração não para."
                        : "Não é o momento da cardioplegia.";

                case BypassStep.Unclamp:
                    return expected == BypassStep.Cannulate || expected == BypassStep.ClampAorta
                        ? "Não há clampe para retirar."
                        : "O coração ainda não foi implantado.";

                case BypassStep.DeAir:
                    return expected == BypassStep.Unclamp
                        ? "Desarejar com a aorta ainda clampeada não remove o ar das câmaras."
                        : "Ainda não há o que desarejar.";

                case BypassStep.Wean:
                    return expected == BypassStep.DeAir
                        ? "Sair de bomba com ar nas câmaras manda êmbolo gasoso para o cérebro."
                        : "O coração ainda não está pronto para assumir a circulação.";

                default:
                    return "Fora de ordem.";
            }
        }

        public void Reset()
        {
            _done = 0;
            Step = BypassStep.NotStarted;
        }

        /// <summary>What the room should be telling the surgeon to do, in pt-BR.</summary>
        public string CurrentInstruction => Step == BypassStep.Off
            ? "Fora de bomba"
            : NextStep switch
            {
                BypassStep.Cannulate => "Canule as cavas e a aorta",
                BypassStep.ClampAorta => "Clampeie a aorta",
                BypassStep.Cardioplegia => "Infunda a cardioplegia",
                BypassStep.Unclamp => "Retire o clampe da aorta",
                BypassStep.DeAir => "Desareje as câmaras",
                BypassStep.Wean => "Saia de bomba",
                _ => "Aguardando",
            };
    }
}
