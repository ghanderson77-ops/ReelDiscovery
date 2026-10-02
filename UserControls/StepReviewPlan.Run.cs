using ReelDiscovery.Models;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Review Plan step, run side (lifecycle, planning run, re-plan, keep plan, approval and the Next gate).
/// </summary>
public partial class StepReviewPlan
{
    public void BindState(WizardState state)
    {
        _state = state;
    }

    public Task OnEnterStepAsync() => Task.CompletedTask;

    public Task OnLeaveStepAsync() => Task.CompletedTask;

    public Task<bool> ValidateStepAsync() => Task.FromResult(false);

    private Task ReplanAsync() => Task.CompletedTask;

    private void KeepPlan()
    {
    }

    private Task<bool> ApproveAsync() => Task.FromResult(false);

    private Task PlanFromStateCardAsync() => Task.CompletedTask;

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
    }
}
