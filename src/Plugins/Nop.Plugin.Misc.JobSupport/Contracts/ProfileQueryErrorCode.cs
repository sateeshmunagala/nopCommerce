namespace Nop.Plugin.Misc.JobSupport.Contracts;

public enum ProfileQueryErrorCode
{
    None = 0,
    Disabled = 1,
    UnsupportedProvider = 2,
    MissingProcedureName = 3,
    InvalidRequest = 4,
    NotSupported = 5,
    ProcedureExecutionFailed = 6
}
