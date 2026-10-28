using System.Text;
using System.Xml.Linq;

namespace MontaWin.Implantador;

/// <param name="Build">Número da versão (ex.: 26200 = Windows 11 25H2); usado para achar as atualizações certas.</param>
public record Edicao(string Arquivo, int Indice, string Nome, string Idioma, int Build = 0);

// Lê a lista de edições direto do cabeçalho do .wim: não depende da saída (traduzida) do DISM.
public static class Wim
{
    /// <summary>
    /// Confere a imagem pelo SHA-256 que o Montador gravou ao lado (arquivo.wim.sha256), para não formatar o PC com uma
    /// imagem corrompida no pendrive. Conferida uma vez, fica marcada (arquivo.wim.conferido) até o arquivo mudar.
    /// Retorna null se não há como conferir (imagem antiga ou capturada), true se está boa, false se está corrompida.
    /// </summary>
    public static bool? Conferir(string arquivo, Action<double>? progresso = null)
    {
        var esperado = arquivo + ".sha256";
        if (!File.Exists(esperado)) return null;
        var info = new FileInfo(arquivo);
        var marca = arquivo + ".conferido";
        var assinatura = $"{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        if (File.Exists(marca) && File.ReadAllText(marca) == assinatura) return true;
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        using (var f = File.OpenRead(arquivo))
        {
            var buffer = new byte[4 << 20];
            long lido = 0;
            int n;
            while ((n = f.Read(buffer)) > 0)
            {
                hash.AppendData(buffer, 0, n);
                lido += n;
                progresso?.Invoke(lido * 100.0 / Math.Max(info.Length, 1));
            }
        }
        bool ok = Convert.ToHexString(hash.GetHashAndReset()).Equals(File.ReadAllText(esperado).Trim(), StringComparison.OrdinalIgnoreCase);
        if (ok) File.WriteAllText(marca, assinatura);
        return ok;
    }

    public static List<Edicao> Ler(string arquivo)
    {
        using var f = File.OpenRead(arquivo);
        var cab = new byte[0x60];
        f.ReadExactly(cab);
        if (Encoding.ASCII.GetString(cab, 0, 5) != "MSWIM") throw new InvalidDataException($"{Path.GetFileName(arquivo)} não é um arquivo .wim");
        // rhXmlData (0x48): tamanho no disco, deslocamento (0x50) e tamanho original (0x58); o XML nunca é comprimido
        long deslocamento = BitConverter.ToInt64(cab, 0x50);
        var xml = new byte[BitConverter.ToInt64(cab, 0x58)];
        f.Position = deslocamento;
        f.ReadExactly(xml);
        return Edicoes(arquivo, Encoding.Unicode.GetString(xml));
    }

    public static List<Edicao> Edicoes(string arquivo, string xml) =>
        XDocument.Parse(xml.TrimStart('\uFEFF')).Root!.Elements("IMAGE")
            .Select(i => new Edicao(arquivo, (int)i.Attribute("INDEX")!,
                (string?)i.Element("DISPLAYNAME") ?? (string?)i.Element("NAME") ?? $"Imagem {(int)i.Attribute("INDEX")!}",
                (string?)i.Element("WINDOWS")?.Element("LANGUAGES")?.Element("DEFAULT") ?? "en-US",
                (int?)i.Element("WINDOWS")?.Element("VERSION")?.Element("BUILD") ?? 0))
            .ToList();
}
