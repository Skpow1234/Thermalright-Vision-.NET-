namespace ThermalrightVision;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\ThermalrightVisionDashboard", out bool created);
        if (!created)
        {
            MessageBox.Show("Thermalright Vision ya está en ejecución (icono de la bandeja).", "Thermalright Vision");
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }
}
