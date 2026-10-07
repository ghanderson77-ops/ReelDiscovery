using ReelDiscovery.Forms;

namespace ReelDiscovery;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Show disclaimer dialog first
        using var disclaimer = new DisclaimerDialog();
        if (disclaimer.ShowDialog() != DialogResult.OK)
        {
            return; // User declined, exit application
        }

        Application.Run(new WizardForm());
    }
}
