using ReelDiscovery.Forms;
using ReelDiscovery.Services;

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

        // Migrate legacy Base64 API key file to the DPAPI key store (D-09)
        try
        {
            new ApiKeyStore().MigrateLegacyKey();
        }
        catch (Exception ex)
        {
            // Not fatal: the user can re-enter the key, but a failed migration must be visible
            MessageBox.Show(
                $"Could not migrate the saved OpenAI API key: {ex.Message}\n\nYou can re-enter the key on the API Keys step.",
                "ReelDiscovery", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        Application.Run(new WizardForm());
    }
}
