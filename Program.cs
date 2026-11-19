namespace SpotifyTB;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Impede abrir duas instâncias
        using var mutex = new Mutex(true, "SpotifyTB.SingleInstance", out bool created);
        if (!created) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TaskbarWidget());
    }
}
