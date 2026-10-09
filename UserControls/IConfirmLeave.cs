namespace ReelDiscovery.UserControls;

/// <summary>
/// A wizard step that may refuse to be left (a blind review or discovery run is active).
/// </summary>
public interface IConfirmLeave
{
    /// <summary>True when the step may be left now; asks the user while a run is active.</summary>
    bool ConfirmLeave();
}
