using System.Text.Json;

namespace SpotifyTB;

/// <summary>Preferências salvas em %APPDATA%\SpotifyTB\settings.json</summary>
internal sealed class Settings
{
    /// <summary>"right" = encostado na bandeja/relógio, "left" = canto esquerdo da barra.</summary>
    public string Anchor { get; set; } = "right";

    /// <summary>Deslocamento horizontal em pixels (negativo = para a esquerda).</summary>
    public int OffsetX { get; set; }

    /// <summary>Cor de fundo fixa (ARGB). Mantida para compatibilidade; o layout padrão é transparente.</summary>
    public int? BackColorArgb { get; set; }

    /// <summary>Layout transparente padrão do widget.</summary>
    public bool Transparent { get; set; } = true;

    /// <summary>Adiciona sombra e destaque nos controles.</summary>
    public bool ContrastBoost { get; set; } = true;

    /// <summary>Cor da borda. Nulo remove a borda.</summary>
    public int? BorderColorArgb { get; set; }


    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpotifyTB", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
                s.Transparent = true;
                return s;
            }
        }
        catch { /* arquivo corrompido: usa o padrão */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* sem permissão: ignora */ }
    }
}
