namespace ReelDiscovery.Models;

/// <summary>
/// Which step list WizardForm shows after the API step. CaseDriven is the default (research A2); Legacy is removed in Phase 7.
/// </summary>
public enum WizardFlow
{
    CaseDriven,
    Legacy
}
