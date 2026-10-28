using MontaWin.Implantador;

namespace MontaWin.Montador;

public record ResultadoBusca(string Nome, string Id)
{
    public string Texto => $"{Nome}   ({Id})";
}

public record ItemLista(Programa Programa)
{
    public string Texto => Programa.Instalador != null
        ? $"{Programa.Nome}   ·   arquivo {Programas.Separar(Programa.Instalador).Arquivo} (sem internet)"
        : $"{Programa.Nome}   ·   winget {string.Join(" ", Programa.Ids.Take(2))}{(Programa.Ids.Length > 2 ? " ..." : "")}";
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

    public static string Remover(string texto, string nome) =>
        string.Join(Environment.NewLine, texto.Split('\n').Select(l => l.TrimEnd('\r'))
            .Where(l => l.TrimStart().StartsWith('#') || !l.Contains('=') || !NomeDaLinha(l).Equals(nome, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Resultado do "winget search" (tabela com colunas alinhadas; os títulos vêm traduzidos, então usa as posições
    /// do cabeçalho, que fica logo acima da linha de traços).
    /// </summary>
    public static List<ResultadoBusca> LerBusca(string saida)
    {
        var linhas = saida.Split('\n').Select(l => l.TrimEnd('\r')).Select(l => l[(l.LastIndexOf('\r') + 1)..]).ToList();
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
