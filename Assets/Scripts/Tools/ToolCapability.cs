using System;

namespace VRSurgery.Tools
{
    [Flags]
    public enum ToolCapability
    {
        None = 0,
        Cut = 1 << 0,
        GrabTissue = 1 << 1,
        Inject = 1 << 2,
        Suture = 1 << 3,
        Retract = 1 << 4,
        Suction = 1 << 5,
        Cauterize = 1 << 6,
    }

    public enum ToolType
    {
        Scalpel,
        Forceps,
        Retractor,
        Syringe,
        NeedleHolder,
        SutureNeedle,
        Scissors,
        Suction,
        Cautery,
    }
}
