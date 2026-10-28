namespace MontaWin.Montador;

/// <summary>
/// Configurações internas do Aurora Win (engrenagem), guardadas neste PC em %LOCALAPPDATA%\MontaWin\config.txt.
/// O tema também vai para o pendrive: o painel do boot fica igual ao app.
/// </summary>
public sealed class Configuracoes
{
    public string Tema { get; set; } = "ondas";
    /// <summary>Boot com o certificado novo (Windows UEFI CA 2023), quando a ISO traz os arquivos dele.</summary>
    public bool BootNovo { get; set; }
    public bool Rapida { get; set; }

    static string Arquivo => Path.Combine(ListaProgramas.Pasta, "config.txt");

    public static Configuracoes Ler()
    {
        var c = new Configuracoes();
        if (!File.Exists(Arquivo)) return c;
        foreach (var linha in File.ReadAllLines(Arquivo))
        {
            var p = linha.Split('=', 2, StringSplitOptions.TrimEntries);
            if (p.Length < 2) continue;
            switch (p[0])
            {
                case "Tema": c.Tema = p[1]; break;
                case "BootNovo": c.BootNovo = p[1] == "sim"; break;
                case "Rapida": c.Rapida = p[1] == "sim"; break;
            }
        }
        return c;
    }

    public void Salvar()
    {
        Directory.CreateDirectory(ListaProgramas.Pasta);
        File.WriteAllLines(Arquivo, [$"Tema = {Tema}", $"BootNovo = {(BootNovo ? "sim" : "nao")}", $"Rapida = {(Rapida ? "sim" : "nao")}"]);
    }
}
