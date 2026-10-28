namespace MontaWin.Implantador;

/// <summary>Programa instalado pelo winget no primeiro login. Um item pode ter vários pacotes (ex.: Visual C++).</summary>
public record Programa(string Nome, string[] Ids, bool Marcado);

public static class Programas
{
    /// <summary>Lista padrão; o Montador grava como MontaWin\Programas.txt para o técnico editar.</summary>
    public const string Padrao = """
        # MontaWin: programas instalados no primeiro login do Windows novo (pelo winget, precisa de internet).
        # Formato:  Nome = id do winget   (vários ids separados por espaço)
        # Linha começando com # é comentário. Começando com - vem desmarcado no painel.
        # Para achar o id de um programa: no Windows, rode  winget search nome-do-programa
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
        """;

    public static List<Programa> Ler(string texto) =>
        texto.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#') && l.Contains('='))
            .Select(l =>
            {
                bool marcado = !l.StartsWith('-');
                var partes = l.TrimStart('-').Split('=', 2);
                return new Programa(partes[0].Trim(), partes[1].Split(' ', StringSplitOptions.RemoveEmptyEntries), marcado);
            })
            .Where(p => p.Nome.Length > 0 && p.Ids.Length > 0)
            .ToList();
}
