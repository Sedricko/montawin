using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace MontaWin.Montador;

public record AtualizacaoCatalogo(string Id, string Titulo, DateTime Data);

/// <summary>
/// Baixa do Microsoft Update Catalog a atualização cumulativa mais nova (e a do .NET) da versão da ISO.
/// A Microsoft não tem API para isso: lê a página de pesquisa como um navegador. Se o site mudar e falhar,
/// o Montador abre a pesquisa no navegador para baixar na mão (<see cref="UrlPesquisa"/>).
/// </summary>
public static partial class Atualizacoes
{
    const string Catalogo = "https://www.catalog.update.microsoft.com/";

    /// <summary>Versão de manutenção pelo build da imagem (26200 = 25H2...). null se não souber.</summary>
    public static string? Versao(int build) => build switch
    {
        >= 26200 and < 26300 => "25H2",
        >= 26100 and < 26200 => "24H2",
        22631 => "23H2",
        22621 => "22H2",
        19045 => "22H2",
        _ => null,
    };

    static string Windows(int build) => build >= 22000 ? "Windows 11" : "Windows 10";

    public static string UrlPesquisa(int build) =>
        Catalogo + "Search.aspx?q=" + Uri.EscapeDataString($"Cumulative Update {Windows(build)} Version {Versao(build)} x64");

    [GeneratedRegex(@"id='([0-9a-f-]{36})_link'[^>]*>\s*(.*?)\s*</a>.*?_C4_R\d+"">\s*(\d{1,2}/\d{1,2}/\d{4})\s*<", RegexOptions.Singleline)]
    private static partial Regex RegexLinha();

    [GeneratedRegex(@"downloadInformation\[\d+\]\.files\[\d+\]\.url\s*=\s*'([^']+)'")]
    private static partial Regex RegexArquivo();

    public static List<AtualizacaoCatalogo> LerPesquisa(string html) =>
        RegexLinha().Matches(html).Select(m => new AtualizacaoCatalogo(m.Groups[1].Value, System.Net.WebUtility.HtmlDecode(m.Groups[2].Value),
            DateTime.ParseExact(m.Groups[3].Value, "M/d/yyyy", CultureInfo.InvariantCulture))).ToList();

    /// <summary>A cumulativa do Windows e a do .NET mais novas (sem as "Preview"), da versão e arquitetura certas.</summary>
    public static List<AtualizacaoCatalogo> Escolher(IEnumerable<AtualizacaoCatalogo> resultados, int build)
    {
        var versao = Regex.Escape(Versao(build) ?? "?");
        var windows = Windows(build);
        var lista = resultados.Where(r => !r.Titulo.Contains("Preview", StringComparison.OrdinalIgnoreCase)).ToList();
        AtualizacaoCatalogo? Mais(string padrao) => lista.Where(r => Regex.IsMatch(r.Titulo, padrao, RegexOptions.IgnoreCase))
            .OrderByDescending(r => r.Data).FirstOrDefault();
        return new[]
        {
            Mais($@"^\d{{4}}-\d{{2}} Cumulative Update for {windows},? Version {versao} for x64-based Systems"),
            Mais($@"Cumulative Update for \.NET Framework .* for {windows},? version {versao} for x64"),
        }.OfType<AtualizacaoCatalogo>().ToList();
    }

    public static List<string> LerArquivos(string html) => RegexArquivo().Matches(html).Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>
    /// Baixa para uma pasta local (reaproveitada se a mesma atualização já foi baixada) e devolve a pasta com os .msu.
    /// A cumulativa do Windows 11 24H2/25H2 vem em 2 arquivos (a base "checkpoint" e a do mês); o DISM usa os dois.
    /// </summary>
    public static async Task<(string Pasta, string Descricao)> Baixar(int build, Action<string, double> progresso)
    {
        if (Versao(build) == null) throw new Exception($"Não sei qual atualização serve para o build {build} desta ISO.");
        using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) MontaWin");
        progresso("Procurando as atualizações do mês no Microsoft Update Catalog", 0);
        var escolhidas = Escolher(LerPesquisa(await http.GetStringAsync(UrlPesquisa(build))), build);
        if (escolhidas.Count == 0) throw new Exception("Não encontrei a atualização cumulativa no Microsoft Update Catalog.");

        var arquivos = new List<string>();
        foreach (var a in escolhidas)
        {
            var corpo = new FormUrlEncodedContent([new("updateIDs", $"[{{\"size\":0,\"languages\":\"\",\"uidInfo\":\"{a.Id}\",\"updateID\":\"{a.Id}\"}}]")]);
            var resposta = await http.PostAsync(Catalogo + "DownloadDialog.aspx", corpo);
            arquivos.AddRange(LerArquivos(await resposta.Content.ReadAsStringAsync()));
        }
        if (arquivos.Count == 0) throw new Exception("O Microsoft Update Catalog não informou os arquivos para baixar.");

        var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MontaWin", "Atualizacoes", escolhidas[0].Id);
        Directory.CreateDirectory(pasta);
        foreach (var antigo in Directory.GetFiles(pasta, "*.msu").Where(f => !arquivos.Any(u => u.EndsWith("/" + Path.GetFileName(f), StringComparison.OrdinalIgnoreCase))))
            File.Delete(antigo);
        for (int i = 0; i < arquivos.Count; i++)
        {
            var destino = Path.Combine(pasta, Path.GetFileName(new Uri(arquivos[i]).LocalPath));
            if (File.Exists(destino)) continue; // baixado completo antes (o parcial fica com .parte)
            var parte = destino + ".parte";
            using (var r = await http.GetAsync(arquivos[i], HttpCompletionOption.ResponseHeadersRead))
            {
                r.EnsureSuccessStatusCode();
                long total = r.Content.Headers.ContentLength ?? 0, lido = 0;
                await using var origem = await r.Content.ReadAsStreamAsync();
                await using var saida = File.Create(parte);
                var buffer = new byte[1 << 20];
                int n;
                var nome = $"Baixando {Path.GetFileName(destino)} ({i + 1} de {arquivos.Count})";
                while ((n = await origem.ReadAsync(buffer)) > 0)
                {
                    await saida.WriteAsync(buffer.AsMemory(0, n));
                    lido += n;
                    if (total > 0) progresso($"{nome}: {lido >> 20} de {total >> 20} MB", (i + (double)lido / total) * 100 / arquivos.Count);
                }
            }
            File.Move(parte, destino, overwrite: true);
        }
        return (pasta, string.Join(" + ", escolhidas.Select(a => Regex.Match(a.Titulo, @"KB\d+").Value)));
    }
}
