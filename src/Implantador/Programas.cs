using System.Text.RegularExpressions;

namespace MontaWin.Implantador;

/// <summary>
/// Programa instalado no primeiro login: pelo winget (um item pode ter vários pacotes, ex.: Visual C++)
/// ou por um instalador da pasta MontaWin\Instaladores com o comando silencioso (<paramref name="Instalador"/>).
/// </summary>
public record Programa(string Nome, string[] Ids, bool Marcado, string? Instalador = null);

public static class Programas
{
    /// <summary>Lista padrão; o Montador grava como MontaWin\Programas.txt para o técnico editar.</summary>
    public const string Padrao = """
        # MontaWin: programas instalados no primeiro login do Windows novo (pelo winget, precisa de internet).
        # Formato:  Nome = id do winget   (vários ids separados por espaço)
        # Linha começando com # é comentário. Começando com - vem desmarcado no painel.
        # Para achar o id de um programa: no Windows, rode  winget search nome-do-programa
        #
        # Programa fora do winget: coloque o instalador na pasta MontaWin\Instaladores e escreva
        #   Nome = arquivo: nome-do-instalador.exe parâmetros-silenciosos
        # O nome aceita * (ex.: NVIDIA_app*.exe pega qualquer versão). Só aparece no painel se o arquivo estiver na pasta.
        Google Chrome = Google.Chrome
        WinRAR = RARLab.WinRAR
        Visual C++ 2008 a 2022 (32 e 64 bits) = Microsoft.VCRedist.2008.x86 Microsoft.VCRedist.2008.x64 Microsoft.VCRedist.2010.x86 Microsoft.VCRedist.2010.x64 Microsoft.VCRedist.2012.x86 Microsoft.VCRedist.2012.x64 Microsoft.VCRedist.2013.x86 Microsoft.VCRedist.2013.x64 Microsoft.VCRedist.2015+.x86 Microsoft.VCRedist.2015+.x64
        Notepad++ = Notepad++.Notepad++
        Lightshot = Skillbrains.Lightshot
        pCloud = pCloudAG.pCloudDrive
        # Spotify pela Microsoft Store: o instalador do site recusa instalar como administrador.
        Spotify = 9NCBCSZSJRSB
        # Microsoft 365: Word, Excel, Outlook, PowerPoint... Ativa com a conta/assinatura do cliente.
        Microsoft 365 (Word, Excel, Outlook) = Microsoft.Office
        # Foxit PDF Editor (o nome antigo no winget é PhantomPDF). A versão Pro é a mesma, ativada com a licença.
        Foxit PDF Editor Pro = Foxit.PhantomPDF
        # NVIDIA App: baixe em nvidia.com/pt-br/software/nvidia-app e coloque em MontaWin\Instaladores
        -NVIDIA App = arquivo: NVIDIA_app*.exe -s
        """;

    public static List<Programa> Ler(string texto) =>
        texto.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#') && l.Contains('='))
            .Select(l =>
            {
                bool marcado = !l.StartsWith('-');
                var partes = l.TrimStart('-').Split('=', 2);
                var valor = partes[1].Trim();
                return valor.StartsWith("arquivo:", StringComparison.OrdinalIgnoreCase)
                    ? new Programa(partes[0].Trim(), [], marcado, valor["arquivo:".Length..].Trim())
                    : new Programa(partes[0].Trim(), valor.Split(' ', StringSplitOptions.RemoveEmptyEntries), marcado);
            })
            .Where(p => p.Nome.Length > 0 && (p.Ids.Length > 0 || p.Instalador?.Length > 0))
            .ToList();

    /// <summary>Id de app da Microsoft Store (ex.: 9NCBCSZSJRSB): 12 letras/números começando com 9 ou X.</summary>
    public static bool DaLoja(string id) => id.Length == 12 && id[0] is '9' or 'X' && id.All(char.IsAsciiLetterOrDigit);

    /// <summary>Não dá para baixar para o pendrive: Microsoft Store e pacotes que o winget proíbe baixar (o Microsoft 365).</summary>
    public static bool SoInternet(string id) => DaLoja(id) || id.Equals("Microsoft.Office", StringComparison.OrdinalIgnoreCase);

    /// <summary>"NVIDIA_app*.exe -s" vira ("NVIDIA_app*.exe", "-s").</summary>
    public static (string Arquivo, string Parametros) Separar(string instalador)
    {
        var partes = instalador.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return (partes[0], partes.Length > 1 ? partes[1].Trim() : "");
    }

    /// <summary>
    /// Programa baixado pelo Montador com "winget download" (instalador + manifesto .yaml numa pasta por id).
    /// Retorna a linha que instala sem internet, com os parâmetros silenciosos do manifesto, ou null se não der.
    /// SoUsuario: o instalador é só para o usuário (Scope: user), então não roda como sistema antes do login.
    /// </summary>
    public static (string Comando, bool SoUsuario)? Offline(string pasta, string pastaNoWindows)
    {
        var arquivos = Directory.GetFiles(pasta);
        var manifesto = arquivos.FirstOrDefault(f => f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));
        var instalador = arquivos.FirstOrDefault(f => !f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase));
        if (manifesto == null || instalador == null) return null;
        var texto = File.ReadAllText(manifesto);
        return ComandoSilencioso(texto, $@"{pastaNoWindows}\{Path.GetFileName(instalador)}") is string comando
            ? (comando, Regex.IsMatch(texto, @"^\s*-?\s*Scope:\s*user\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)) : null;
    }

    /// <summary>Comando silencioso pelo tipo de instalador do manifesto do winget (null para zip, portátil ou msix: ficam para o winget).</summary>
    public static string? ComandoSilencioso(string manifesto, string caminho)
    {
        string? Campo(string nome) =>
            Regex.Match(manifesto, $@"^\s*-?\s*{nome}:\s*(.+?)\s*$", RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value.Trim('\'', '"') : null;
        if (Campo("NestedInstallerType") != null) return null;
        var tipo = Campo("InstallerType")?.ToLowerInvariant();
        var parametros = Campo("Silent") ?? tipo switch
        {
            "msi" or "wix" => "/qn /norestart",
            "inno" => "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
            "nullsoft" => "/S",
            "burn" => "/quiet /norestart",
            _ => null,
        };
        if (parametros == null || tipo is null or "zip" or "portable" or "msix" or "appx") return null;
        if (Campo("Custom") is string extra) parametros += " " + extra;
        return tipo is "msi" or "wix" ? $"msiexec /i \"{caminho}\" {parametros}" : $"\"{caminho}\" {parametros}";
    }
}
