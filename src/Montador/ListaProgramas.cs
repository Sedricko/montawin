using MontaWin.Implantador;

namespace MontaWin.Montador;

public record ResultadoBusca(string Nome, string Id)
{
    public string Texto => $"{Nome}   ({Id})";
}

/// <summary>Uma linha da lista de programas no app: a caixa marca se vem escolhido no painel; o selo diz de onde instala.</summary>
public class ItemLista(Programa programa, string selo) : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    string? icone = Icones.EmCache(programa);
    /// <summary>Ícone do próprio programa (PNG em cache); null enquanto não baixou: aparece a inicial.</summary>
    public string? Icone
    {
        get => icone;
        set { icone = value; PropertyChanged?.Invoke(this, new(nameof(Icone))); PropertyChanged?.Invoke(this, new(nameof(VisIcone))); }
    }
    public string VisIcone => icone != null ? "Visible" : "Collapsed"; // texto: o WPF converte, e o arquivo compila também nos testes

    public Programa Programa { get; } = programa;
    public string Nome => Programa.Nome;
    public bool Marcado { get; set; } = programa.Marcado;
    public string Selo { get; } = selo;
    public string Inicial => Nome.Length > 0 ? char.ToUpperInvariant(Nome[0]).ToString() : "?";
    public string Detalhe => Programa.Instalador != null
        ? $"arquivo {Programas.Separar(Programa.Instalador).Arquivo}"
        : $"winget {string.Join(" ", Programa.Ids.Take(2))}{(Programa.Ids.Length > 2 ? " ..." : "")}";

    /// <summary>
    /// De onde o programa instala, olhando o pendrive escolhido (pasta MontaWin\Instaladores\winget\id):
    /// "no pendrive" (sem internet), "internet" (winget online), "Store" ou "arquivo".
    /// </summary>
    public static string SeloDe(Programa p, char? dados)
    {
        if (p.Instalador != null) return "arquivo";
        if (p.Ids.Any(Programas.DaLoja)) return "Store";
        if (p.Ids.Any(id => Programas.SoInternet(id))) return "só internet";
        if (dados is char d && p.Ids.All(id => Directory.Exists($@"{d}:\MontaWin\Instaladores\winget\{id}"))) return "no pendrive";
        return "internet";
    }
}

/// <summary>
/// Ícone original de cada programa: o site do fabricante vem do "winget show" (o primeiro endereço da saída, que não
/// depende do idioma) e o ícone do site é guardado em %LOCALAPPDATA%\MontaWin\Icones.
/// </summary>
public static class Icones
{
    static string Pasta => Path.Combine(ListaProgramas.Pasta, "Icones");
    static string Arquivo(Programa p) => Path.Combine(Pasta, Perfil.NomeSeguro(p.Ids.FirstOrDefault() ?? p.Nome) + ".png");

    public static string? EmCache(Programa p) => File.Exists(Arquivo(p)) ? Arquivo(p) : null;

    // shortcut: instaladores por arquivo ficam com a inicial (sem site no winget), dá para tirar o ícone do .exe se fizer falta
    public static async Task<string?> Baixar(Programa p, System.Net.Http.HttpClient http)
    {
        if (EmCache(p) is string pronto) return pronto;
        if (p.Ids.FirstOrDefault() is not string id) return null;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("winget",
                $"show --id {id} -e --source {(Programas.DaLoja(id) ? "msstore" : "winget")} --accept-source-agreements --disable-interactivity")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
            using var winget = System.Diagnostics.Process.Start(psi)!;
            var saida = await winget.StandardOutput.ReadToEndAsync();
            await winget.WaitForExitAsync();
            if (Programas.SiteNaSaida(saida) is not string site) return null;
            var png = await http.GetByteArrayAsync($"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(site)}&sz=64");
            Directory.CreateDirectory(Pasta);
            await File.WriteAllBytesAsync(Arquivo(p), png);
            return Arquivo(p);
        }
        catch (Exception) { return null; } // sem internet ou sem winget: fica a inicial
    }
}

/// <summary>Programas populares para adicionar com um clique (id do winget).</summary>
public static class Populares
{
    public static readonly (string Nome, string Id)[] Lista =
    [
        ("Mozilla Firefox", "Mozilla.Firefox"), ("7-Zip", "7zip.7zip"), ("VLC", "VideoLAN.VLC"), ("AnyDesk", "AnyDesk.AnyDesk"),
        ("TeamViewer", "TeamViewer.TeamViewer"), ("Adobe Acrobat Reader", "Adobe.Acrobat.Reader.64-bit"), ("Zoom", "Zoom.Zoom"),
        ("Discord", "Discord.Discord"), ("Steam", "Valve.Steam"), ("Java (Temurin)", "EclipseAdoptium.Temurin.21.JRE"),
        ("K-Lite Codec Pack", "CodecGuide.K-LiteCodecPack.Standard"), ("LibreOffice", "TheDocumentFoundation.LibreOffice"),
        ("Google Drive", "Google.GoogleDrive"), ("WhatsApp", "9NKSQGP7F2NH"), ("Telegram", "Telegram.TelegramDesktop"), ("CPU-Z", "CPUID.CPU-Z"),
    ];
}

/// <summary>
/// Lista de programas do técnico (MontaWin\Programas.txt), guardada também neste PC para valer em todo pendrive novo.
/// Instaladores adicionados por arquivo ficam numa pasta ao lado e vão para MontaWin\Instaladores no pendrive.
/// </summary>
public static class ListaProgramas
{
    public static readonly string Pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MontaWin");
    public static string Arquivo => Path.Combine(Pasta, "Programas.txt");
    public static string PastaInstaladores => Path.Combine(Pasta, "Instaladores");

    public static string Ler() => File.Exists(Arquivo) ? File.ReadAllText(Arquivo) : Programas.Padrao;

    public static void Salvar(string texto)
    {
        Directory.CreateDirectory(Pasta);
        File.WriteAllText(Arquivo, texto);
    }

    static string NomeDaLinha(string linha) => linha.Trim().TrimStart('-', '+').Split('=', 2)[0].Trim();

    /// <summary>Acrescenta "Nome = valor" (substitui se já existir um programa com esse nome).</summary>
    public static string Adicionar(string texto, string nome, string valor)
    {
        nome = nome.Replace("=", "-").Trim();
        return Remover(texto, nome).TrimEnd() + Environment.NewLine + $"{nome} = {valor}" + Environment.NewLine;
    }

    /// <summary>Marca (+) ou desmarca a linha do programa: marcado = já vem escolhido no painel.</summary>
    public static string Marcar(string texto, string nome, bool marcado) =>
        string.Join(Environment.NewLine, texto.Split('\n').Select(l => l.TrimEnd('\r')).Select(l =>
            l.TrimStart().StartsWith('#') || !l.Contains('=') || !NomeDaLinha(l).Equals(nome, StringComparison.OrdinalIgnoreCase)
                ? l : (marcado ? "+ " : "") + l.Trim().TrimStart('-', '+').TrimStart()));

    public static string Remover(string texto, string nome) =>
        string.Join(Environment.NewLine, texto.Split('\n').Select(l => l.TrimEnd('\r'))
            .Where(l => l.TrimStart().StartsWith('#') || !l.Contains('=') || !NomeDaLinha(l).Equals(nome, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Resultado do "winget search" (tabela com colunas alinhadas; os títulos vêm traduzidos, então usa as posições
    /// do cabeçalho, que fica logo acima da linha de traços).
    /// </summary>
    public static List<ResultadoBusca> LerBusca(string saida)
    {
        // Sem os códigos de cor/cursor e o "girando" que o winget escreve antes da tabela: eles deslocam as colunas do cabeçalho
        saida = System.Text.RegularExpressions.Regex.Replace(saida, @"\x1B\[[0-9;?]*[A-Za-z]|\x1B\][^\x07]*\x07", "");
        var linhas = saida.Split('\n').Select(l => l.TrimEnd('\r')).Select(l => l[(l.LastIndexOf('\r') + 1)..])
            .Select(l => l.TrimStart('-', '\\', '|', '/', ' ').Length == 0 ? l : System.Text.RegularExpressions.Regex.Replace(l, @"^\s*[-\\|/]\s+(?=\S)", "")).ToList();
        int tracos = linhas.FindIndex(l => l.Length >= 10 && l.Trim().All(c => c == '-'));
        if (tracos < 1) return [];
        var cabecalho = linhas[tracos - 1];
        var colunas = Enumerable.Range(0, cabecalho.Length).Where(i => cabecalho[i] != ' ' && (i == 0 || cabecalho[i - 1] == ' ')).ToList();
        if (colunas.Count < 3) return [];
        return linhas.Skip(tracos + 1)
            .Where(l => l.Length > colunas[1])
            .Select(l => new ResultadoBusca(l[..colunas[1]].Trim(), l[colunas[1]..Math.Min(colunas[2], l.Length)].Trim()))
            .Where(r => r.Id.Length > 0 && !r.Id.Contains(' '))
            .ToList();
    }

    /// <summary>Sugestão de parâmetros silenciosos pelo tipo do arquivo (.exe varia de programa para programa).</summary>
    public static string ParametrosSugeridos(string arquivo) =>
        Path.GetExtension(arquivo).Equals(".msi", StringComparison.OrdinalIgnoreCase) ? "/qn /norestart" : "/S";
}
