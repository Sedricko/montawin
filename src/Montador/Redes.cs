using System.Text.RegularExpressions;

namespace MontaWin.Montador;

/// <summary>Leitura da saída do netsh: nomes das redes Wi-Fi ao alcance e das já salvas neste PC (só nomes, nunca senhas).</summary>
public static class Redes
{
    // "SSID 1 : nome" é igual em qualquer idioma do Windows
    static readonly Regex NoAlcance = new(@"^SSID \d+\s*:\s*(.+)$", RegexOptions.Compiled);
    // "Perfil de Todos os Usuários : nome" / "All User Profile : nome"
    static readonly Regex Salva = new(@"^\s*[^:]*(?:Perfil|Profile)[^:]*:\s*(.+?)\s*$", RegexOptions.Compiled);

    static IEnumerable<string> Linhas(string saida) => saida.Split(['\r', '\n']);

    /// <summary>Redes das duas saídas ("wlan show networks" e "wlan show profiles"), sem repetir.</summary>
    public static List<string> Ler(string saidaAlcance, string saidaSalvas) =>
        Linhas(saidaAlcance).Select(l => NoAlcance.Match(l.Trim())).Where(m => m.Success).Select(m => m.Groups[1].Value.Trim())
            .Concat(Linhas(saidaSalvas).Select(l => Salva.Match(l)).Where(m => m.Success).Select(m => m.Groups[1].Value))
            .Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
