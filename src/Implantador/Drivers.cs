namespace MontaWin.Implantador;

/// <summary>Um driver exportado pelo DISM (uma pasta com o .inf).</summary>
public record Driver(string Pasta, string Classe, string Fabricante, string Inf)
{
    /// <summary>Vem marcado no backup: o que faz o PC funcionar. Impressora, firmware e tipos desconhecidos ficam desmarcados.</summary>
    public bool Basico => Drivers.EhBasico(Classe);

    public string Descricao => $"{Tui.Ajustar(Classe, 14)} {Tui.Ajustar(Fabricante, 26)} {Inf}";
}

public static class Drivers
{
    static readonly Dictionary<string, string> Classes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Net"] = "Rede", ["Display"] = "Vídeo", ["Media"] = "Áudio", ["AudioEndpoint"] = "Áudio",
        ["Printer"] = "Impressora", ["System"] = "Chipset", ["Bluetooth"] = "Bluetooth", ["Camera"] = "Câmera",
        ["Image"] = "Câmera", ["HIDClass"] = "Teclado/mouse", ["Keyboard"] = "Teclado/mouse", ["Mouse"] = "Teclado/mouse",
        ["SCSIAdapter"] = "Armazenamento", ["HDC"] = "Armazenamento", ["USB"] = "USB", ["Biometric"] = "Biometria",
        ["Extension"] = "Extensão", ["SoftwareComponent"] = "Componente", ["Firmware"] = "Firmware",
    };

    static readonly string[] NaoBasicos = ["Impressora", "Firmware"];

    public static bool EhBasico(string classe) => Classes.ContainsValue(classe) && !NaoBasicos.Contains(classe);

    public static List<Driver> Listar(string pastaExportada) =>
        Directory.GetDirectories(pastaExportada)
            .Select(pasta => (pasta, inf: Directory.GetFiles(pasta, "*.inf").FirstOrDefault()))
            .Where(x => x.inf != null)
            .Select(x =>
            {
                var (classe, fabricante) = LerInf(File.ReadAllText(x.inf!));
                return new Driver(x.pasta, classe, fabricante, Path.GetFileName(x.inf!));
            })
            .OrderBy(d => d.Classe).ThenBy(d => d.Fabricante)
            .ToList();

    /// <summary>Lê Class e Provider da seção [Version], resolvendo %variáveis% pela seção [Strings].</summary>
    public static (string Classe, string Fabricante) LerInf(string texto)
    {
        var secoes = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? atual = null;
        foreach (var bruta in texto.Split('\n'))
        {
            var linha = bruta.Split(';')[0].Trim();
            if (linha.StartsWith('[') && linha.EndsWith(']'))
            {
                var nome = linha[1..^1].Trim();
                if (!secoes.TryGetValue(nome, out atual)) secoes[nome] = atual = new(StringComparer.OrdinalIgnoreCase);
            }
            else if (atual != null && linha.IndexOf('=') is int i and > 0)
                atual.TryAdd(linha[..i].Trim(), linha[(i + 1)..].Trim().Trim('"'));
        }
        string Valor(string chave)
        {
            var v = secoes.GetValueOrDefault("Version")?.GetValueOrDefault(chave) ?? "";
            if (v.Length > 2 && v.StartsWith('%') && v.EndsWith('%'))
                v = secoes.GetValueOrDefault("Strings")?.GetValueOrDefault(v[1..^1]) ?? v;
            return v;
        }
        var classe = Valor("Class");
        return (Classes.GetValueOrDefault(classe, classe.Length > 0 ? classe : "Outros"), Valor("Provider"));
    }
}
