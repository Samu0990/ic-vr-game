using System;
using System.Collections.Generic;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// How much of the operation the visitor performs themselves.
    ///
    /// The clinical rules are identical at every level — what changes is where the visitor joins
    /// the procedure, not what the procedure is. A heart still cannot come out of a patient who
    /// is not arrested on Fácil; the difference is only that the team arrested them before the
    /// visitor put the headset on.
    /// </summary>
    public enum SurgicalDifficulty
    {
        /// <summary>Patient arrives on pump. The visitor starts at the cardiectomy.</summary>
        Facil,

        /// <summary>Going on bypass is one chained gesture; coming off is done properly.</summary>
        Medio,

        /// <summary>All six steps, each one its own gesture.</summary>
        Dificil,
    }

    /// <summary>
    /// One hold at one place, which performs the steps it carries in order.
    ///
    /// A gesture can be worth more than one step: on Fácil and Médio several steps collapse into
    /// a single hold. They still run through the same validation one at a time, so a collapsed
    /// gesture cannot smuggle a patient past a rule that a full one would have caught.
    /// </summary>
    public sealed class BypassGesture
    {
        public readonly BypassStep[] Steps;
        public readonly float Seconds;
        public readonly string Label;

        public BypassGesture(string label, float seconds, params BypassStep[] steps)
        {
            Label = label;
            Seconds = seconds;
            Steps = steps;
        }

        /// <summary>The step this gesture is named for — the last one it performs.</summary>
        public BypassStep Anchor => Steps[Steps.Length - 1];
    }

    /// <summary>
    /// What a difficulty level means in practice: which bypass steps the team did before the
    /// visitor arrived, which ones the visitor performs, and how long the round is allowed to run.
    ///
    /// The round length lives here rather than in a constant because it is a consequence of the
    /// level, not a separate decision. Fácil is ninety seconds because there is a third less to
    /// do, not because someone typed ninety somewhere.
    ///
    /// No UnityEngine, for the same reason BypassProcedure has none: a level is a set of rules
    /// about an operation, and rules are worth checking without a scene.
    /// </summary>
    public sealed class BypassPlan
    {
        public SurgicalDifficulty Difficulty { get; }

        /// <summary>Steps already done when the visitor arrives. Never more than the entry half.</summary>
        public IReadOnlyList<BypassStep> DoneByTeam { get; }

        /// <summary>Gestures the visitor performs, in order.</summary>
        public IReadOnlyList<BypassGesture> Gestures { get; }

        public float RoundSeconds { get; }

        /// <summary>What the room tells the visitor about what was done before they arrived.</summary>
        public string Briefing { get; }

        private BypassPlan(SurgicalDifficulty difficulty, BypassStep[] doneByTeam,
            BypassGesture[] gestures, float roundSeconds, string briefing)
        {
            Difficulty = difficulty;
            DoneByTeam = doneByTeam;
            Gestures = gestures;
            RoundSeconds = roundSeconds;
            Briefing = briefing;
        }

        /// <summary>Seconds of holding this level asks for, across every gesture.</summary>
        public float GestureSeconds
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < Gestures.Count; i++) { total += Gestures[i].Seconds; }
                return total;
            }
        }

        public static BypassPlan For(SurgicalDifficulty difficulty)
        {
            switch (difficulty)
            {
                case SurgicalDifficulty.Facil:
                    // The team put the patient on pump. Coming off is one hold, so the visitor
                    // still gets the moment the heart takes over — that is the point of the
                    // operation, and removing it to save time would leave a shorter version of
                    // nothing.
                    return new BypassPlan(
                        difficulty,
                        new[] { BypassStep.Cannulate, BypassStep.ClampAorta, BypassStep.Cardioplegia },
                        new[]
                        {
                            new BypassGesture("Sair de bomba", 7f,
                                BypassStep.Unclamp, BypassStep.DeAir, BypassStep.Wean),
                        },
                        90f,
                        "A equipe já colocou o paciente em circulação extracorpórea: cânulas no " +
                        "lugar, aorta clampeada e cardioplegia infundida. O coração está parado e " +
                        "a bomba mantém o paciente. Comece pela retirada do coração doente.");

                case SurgicalDifficulty.Medio:
                    // Entry as one chained hold, exit done properly — the half where a mistake
                    // costs the patient their brain is the half worth performing step by step.
                    return new BypassPlan(
                        difficulty,
                        Array.Empty<BypassStep>(),
                        new[]
                        {
                            new BypassGesture("Entrar em bomba", 9f,
                                BypassStep.Cannulate, BypassStep.ClampAorta, BypassStep.Cardioplegia),
                            new BypassGesture("Retirar o clampe", 5.5f, BypassStep.Unclamp),
                            new BypassGesture("Desarejar", 7f, BypassStep.DeAir),
                            new BypassGesture("Sair de bomba", 4f, BypassStep.Wean),
                        },
                        150f,
                        "A equipe preparou o campo. Você entra em bomba, faz o transplante e sai.");

                default:
                    return new BypassPlan(
                        SurgicalDifficulty.Dificil,
                        Array.Empty<BypassStep>(),
                        new[]
                        {
                            new BypassGesture("Canular", 7f, BypassStep.Cannulate),
                            new BypassGesture("Clampear a aorta", 6f, BypassStep.ClampAorta),
                            new BypassGesture("Infundir cardioplegia", 7f, BypassStep.Cardioplegia),
                            new BypassGesture("Retirar o clampe", 5.5f, BypassStep.Unclamp),
                            new BypassGesture("Desarejar", 7f, BypassStep.DeAir),
                            new BypassGesture("Sair de bomba", 4f, BypassStep.Wean),
                        },
                        240f,
                        "Circulação extracorpórea completa, do primeiro ponto de cânula à saída " +
                        "de bomba. Cada etapa na sua vez.");
            }
        }
    }
}
