namespace MontaWin.Implantador;

/// <summary>Uma pasta (ou o perfil inteiro) de um usuário para copiar.</summary>
public record ItemBackup(string Perfil, string Origem, long Tamanho)
{
    public string Usuario => Path.GetFileName(Perfil);
    public string Nome => Origem == Perfil ? "Perfil completo" : Path.GetRelativePath(Perfil, Origem);
    public bool Pessoal => Backup.PastasPessoais.Contains(Nome, StringComparer.OrdinalIgnoreCase);
    public string Destino(string pasta) => Origem == Perfil ? Path.Combine(pasta, Usuario) : Path.Combine(pasta, Usuario, Nome);
}

public static class Backup
{
    public static readonly string[] PastasPessoais = ["Desktop", "Documents", "Downloads", "Pictures", "Music", "Videos"];
    static readonly string[] PerfisDoSistema = ["Public", "Default", "Default User", "All Users"];
    static readonly EnumerationOptions Tudo = new() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };

    // Atalhos de compatibilidade ("Meus Documentos", "Application Data"...) são pontos de junção: pula para não copiar em dobro.
    static IEnumerable<string> SemAtalhos(IEnumerable<string> pastas) =>
        pastas.Where(p => !File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint));

    public static IEnumerable<string> Perfis(string pastaUsers) =>
        SemAtalhos(Directory.GetDirectories(pastaUsers))
            .Where(p => !PerfisDoSistema.Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase));

    /// <summary>Cada pasta de primeiro nível de cada perfil (Desktop, Documents, AppData, OneDrive...).</summary>
    public static List<ItemBackup> Pastas(string pastaUsers) =>
        Perfis(pastaUsers).SelectMany(perfil => SemAtalhos(Directory.GetDirectories(perfil)).Select(p => new ItemBackup(perfil, p, Tamanho(p)))).ToList();

    public static List<ItemBackup> PerfisCompletos(string pastaUsers) =>
        Perfis(pastaUsers).Select(perfil => new ItemBackup(perfil, perfil, Tamanho(perfil))).ToList();

    public static long Tamanho(string pasta) => new DirectoryInfo(pasta).EnumerateFiles("*", Tudo).Sum(f => f.Length);
}
