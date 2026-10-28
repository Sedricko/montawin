namespace MontaWin.Implantador;

public record ItemBackup(string Origem, string Destino, long Tamanho);

public static class Backup
{
    static readonly string[] Pastas = ["Desktop", "Documents", "Downloads", "Pictures", "Music", "Videos"];
    static readonly string[] PerfisDoSistema = ["Public", "Default", "Default User", "All Users"];
    static readonly EnumerationOptions Tudo = new() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };

    /// <summary>Pastas pessoais de cada usuário do Windows antigo e o tamanho de cada uma.</summary>
    // shortcut: não copia a pasta do OneDrive (arquivos só na nuvem falham no WinPE); incluir se os clientes pedirem.
    public static List<ItemBackup> Planejar(char letraWindows, string destino)
    {
        var itens = new List<ItemBackup>();
        foreach (var usuario in Directory.GetDirectories($@"{letraWindows}:\Users"))
        {
            var nome = Path.GetFileName(usuario);
            if (PerfisDoSistema.Contains(nome, StringComparer.OrdinalIgnoreCase)) continue;
            if (File.GetAttributes(usuario).HasFlag(FileAttributes.ReparsePoint)) continue;
            foreach (var pasta in Pastas)
            {
                var origem = Path.Combine(usuario, pasta);
                if (Directory.Exists(origem))
                    itens.Add(new ItemBackup(origem, Path.Combine(destino, nome, pasta), Tamanho(origem)));
            }
        }
        return itens;
    }

    public static long Tamanho(string pasta) => new DirectoryInfo(pasta).EnumerateFiles("*", Tudo).Sum(f => f.Length);
}
