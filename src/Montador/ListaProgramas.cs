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
/// depende do idioma) e o ícone do site é guardado em %LOCALAPPDATA%\AuroraWin\Icones.
/// </summary>
public static class Icones
{
    static string Pasta => Path.Combine(ListaProgramas.Pasta, "Icones3"); // 3: as versões antigas guardaram ícones errados (da empresa ou genéricos)

    // Site do produto (não da empresa) dos programas conhecidos: o ícone sai certo e não depende do winget
    static readonly Dictionary<string, string> Sites = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Google.Chrome"] = "chrome.google.com", ["RARLab.WinRAR"] = "win-rar.com", ["Notepad++.Notepad++"] = "notepad-plus-plus.org",
        ["Skillbrains.Lightshot"] = "app.prntscr.com", ["pCloudAG.pCloudDrive"] = "pcloud.com", ["9NCBCSZSJRSB"] = "spotify.com",
        ["Microsoft.Office"] = "microsoft365.com", ["Foxit.PhantomPDF"] = "foxit.com", ["Mozilla.Firefox"] = "firefox.com",
        ["7zip.7zip"] = "7-zip.org", ["VideoLAN.VLC"] = "www.videolan.org", ["AnyDesk.AnyDesk"] = "anydesk.com",
        ["TeamViewer.TeamViewer"] = "teamviewer.com", ["Adobe.Acrobat.Reader.64-bit"] = "acrobat.adobe.com", ["Zoom.Zoom"] = "zoom.us",
        ["Discord.Discord"] = "discord.com", ["Valve.Steam"] = "store.steampowered.com", ["EclipseAdoptium.Temurin.21.JRE"] = "www.java.com",
        ["CodecGuide.K-LiteCodecPack.Standard"] = "www.codecguide.com", ["TheDocumentFoundation.LibreOffice"] = "www.libreoffice.org",
        ["Google.GoogleDrive"] = "drive.google.com", ["9NKSQGP7F2NH"] = "whatsapp.com", ["Telegram.TelegramDesktop"] = "telegram.org",
        ["CPUID.CPU-Z"] = "cpuid.com", ["Microsoft.VCRedist.2008.x86"] = "visualstudio.microsoft.com", ["NVIDIA"] = "nvidia.com",
    };
    public static string Arquivo(Programa p) => Path.Combine(Pasta, Perfil.NomeSeguro(p.Ids.FirstOrDefault() ?? p.Nome) + ".png");

    public static string? EmCache(Programa p) => File.Exists(Arquivo(p)) ? Arquivo(p) : null;

    /// <summary>
    /// Ícone do programa, na ordem: site do produto (programas conhecidos), ícone do próprio instalador
    /// (<paramref name="doInstalador"/>, quando ele está no pendrive ou na pasta de instaladores) e o site do winget.
    /// Ícone pequeno demais (o genérico que os serviços devolvem quando não acham) não vale: tenta o próximo.
    /// </summary>
    public static async Task<string?> Baixar(Programa p, System.Net.Http.HttpClient http, Func<Programa, string?>? doInstalador = null)
    {
        if (EmCache(p) is string pronto) return pronto;
        var conhecido = p.Ids.Select(i => Sites.GetValueOrDefault(i)).FirstOrDefault(s => s != null)
                        ?? Sites.FirstOrDefault(s => p.Nome.Contains(s.Key, StringComparison.OrdinalIgnoreCase)).Value;
        if (conhecido != null && await DoSite(conhecido, p, http) is string a) return a;
        if (doInstalador?.Invoke(p) is string b) return b;
        try { if (p.Ids.FirstOrDefault() is string id && await SiteDoWinget(id) is string site && await DoSite(site, p, http) is string c) return c; }
        catch (Exception) { } // sem winget: fica a inicial
        return null;
    }

    // Google primeiro, DuckDuckGo se o do Google vier genérico ou falhar
    static async Task<string?> DoSite(string site, Programa p, System.Net.Http.HttpClient http)
    {
        foreach (var url in new[] { $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(site)}&sz=128",
                                    $"https://icons.duckduckgo.com/ip3/{Uri.EscapeDataString(site)}.ico",
                                    $"https://{site}/favicon.ico" }) // o próprio site: o VLC só tem ícone aqui (os serviços respondem 404)
        {
            try
            {
                var imagem = await http.GetByteArrayAsync(url);
                if (!IconeDeVerdade(imagem)) continue;
                Directory.CreateDirectory(Pasta);
                await File.WriteAllBytesAsync(Arquivo(p), imagem);
                return Arquivo(p);
            }
            catch (Exception) { } // sem internet ou o site não tem ícone
        }
        return null;
    }

    /// <summary>PNG de pelo menos 20 px (o WhatsApp tem 23; os serviços devolvem um globo de 16 px quando não acham), JPEG ou .ico.</summary>
    public static bool IconeDeVerdade(byte[] imagem) =>
        imagem.Length > 24 && imagem[0] == 0x89 && imagem[1] == 'P' ? (imagem[16] << 24 | imagem[17] << 16 | imagem[18] << 8 | imagem[19]) >= 20
        : imagem.Length > 1500 && imagem[0] == 0xFF && imagem[1] == 0xD8 // JPEG (o do CPU-Z): o WPF lê pelo conteúdo, não pelo nome
        || imagem.Length > 200 && imagem[0] == 0 && imagem[1] == 0 && imagem[2] == 1; // cabeçalho do .ico

    static async Task<string?> SiteDoWinget(string id)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("winget",
            $"show --id {id} -e --source {(Programas.DaLoja(id) ? "msstore" : "winget")} --accept-source-agreements --disable-interactivity")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
        using var winget = System.Diagnostics.Process.Start(psi)!;
        var saida = await winget.StandardOutput.ReadToEndAsync();
        await winget.WaitForExitAsync();
        return Programas.SiteNaSaida(saida);
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
    public static readonly string Pasta = PastaDoApp();

    // Pasta do app neste PC: AuroraWin. A antiga (MontaWin: configurações, ícones, atualizações baixadas) é movida na primeira vez.
    static string PastaDoApp()
    {
        var raiz = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var nova = Path.Combine(raiz, "AuroraWin");
        try { if (Directory.Exists(Path.Combine(raiz, "MontaWin")) && !Directory.Exists(nova)) Directory.Move(Path.Combine(raiz, "MontaWin"), nova); }
        catch (Exception) { } // em uso, antivírus ou sem permissão: segue com a pasta nova vazia (não pode derrubar a abertura)
        return nova;
    }
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
        var linhas = saida.Split(['\r', '\n']) // a saída do winget pode vir só com \r entre as linhas (visto no winget-busca.txt)
            .Where(l => l.Trim().Length > 0) // o \r\n vira duas quebras: sem as linhas vazias o cabeçalho não fica colado nos traços
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
