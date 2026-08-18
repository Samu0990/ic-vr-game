namespace VRSurgery.Surgery
{
    /// <summary>
    /// A technical mistake is not the same thing as losing. The player is meant to make and
    /// recover from most of these, so only CriticalError is allowed to end a procedure.
    /// </summary>
    public enum ErrorSeverity
    {
        Warning,
        MinorError,
        MajorError,
        CriticalError,
    }
}
