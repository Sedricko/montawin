namespace MontaWin.Implantador;

/// <summary>
/// Respostas salvas da instalação de um PC (MontaWin\Perfis\nome.txt). Com um perfil, o painel só pede o disco e o FORMATAR.
/// </summary>
public record Perfil(string Nome)
{
    /// <summary>Identidade da placa-mãe (UUID do SMBIOS) do PC ao qual o perfil foi vinculado.</summary>
    public string? Pc { get; init; }
    public string? Modelo { get; init; }
    public string Edicao { get; init; } = "";
    /// <summary>"apagar", "dividir:200" (GB do Windows) ou "particao".</summary>
    public string Instalacao { get; init; } = "apagar";
    /// <summary>null = configuração normal (telas da Microsoft).</summary>
    public string? Usuario { get; init; }
    /// <summary>null = perguntar a senha na hora; "" = sem senha.</summary>
    public string? Senha { get; init; }
    public string[] Programas { get; init; } = [];
    public bool Restaurar { get; init; } = true;
    public bool BackupDrivers { get; init; } = true;
    /// <summary>Remove apps inúteis e propaganda e ajusta o registro (ver <see cref="Otimizacao"/>).</summary>
    public bool Otimizar { get; init; } = true;

    public string Texto()
    {
        var linhas = new List<string>
        {
            "# Perfil MontaWin. Pode editar; apagar a linha Senha faz o painel perguntar a senha na hora.",
            $"Nome = {Nome}",
            $"PC = {Pc}",
            $"Modelo = {Modelo}",
            $"Edicao = {Edicao}",
            $"Instalacao = {Instalacao}",
            $"Usuario = {Usuario}",
        };
        if (Senha != null) linhas.Add($"Senha = {Senha}");
        linhas.Add($"Programas = {string.Join("; ", Programas)}");
        linhas.Add($"Restaurar = {(Restaurar ? "sim" : "nao")}");
        linhas.Add($"BackupDrivers = {(BackupDrivers ? "sim" : "nao")}");
        linhas.Add($"Otimizar = {(Otimizar ? "sim" : "nao")}");
        return string.Join("\r\n", linhas) + "\r\n";
    }

    public static Perfil? Ler(string texto)
    {
        var campos = texto.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith('#') && l.Contains('='))
            .Select(l => l.Split('=', 2))
            .GroupBy(p => p[0].Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()[1].Trim(), StringComparer.OrdinalIgnoreCase);
        string? Campo(string chave) => campos.TryGetValue(chave, out var v) && v.Length > 0 ? v : null;
        if (Campo("Nome") is not string nome) return null;
        return new Perfil(nome)
        {
            Pc = Campo("PC"),
            Modelo = Campo("Modelo"),
            Edicao = Campo("Edicao") ?? "",
            Instalacao = Campo("Instalacao") ?? "apagar",
            Usuario = Campo("Usuario"),
            Senha = campos.TryGetValue("Senha", out var senha) ? senha : null, // linha vazia = sem senha
            Programas = (Campo("Programas") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Restaurar = !string.Equals(Campo("Restaurar"), "nao", StringComparison.OrdinalIgnoreCase),
            BackupDrivers = !string.Equals(Campo("BackupDrivers"), "nao", StringComparison.OrdinalIgnoreCase),
            Otimizar = !string.Equals(Campo("Otimizar"), "nao", StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>Tira do nome os caracteres que não podem ir em nome de arquivo ou pasta.</summary>
    public static string NomeSeguro(string nome) =>
        string.Concat(nome.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c)).Trim();
}
