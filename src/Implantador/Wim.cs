using System.Text;
using System.Xml.Linq;

namespace MontaWin.Implantador;

public record Edicao(string Arquivo, int Indice, string Nome, string Idioma);

// Lê a lista de edições direto do cabeçalho do .wim: não depende da saída (traduzida) do DISM.
public static class Wim
{
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
                (string?)i.Element("WINDOWS")?.Element("LANGUAGES")?.Element("DEFAULT") ?? "en-US"))
            .ToList();
}
