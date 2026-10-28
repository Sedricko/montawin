namespace MontaWin.Implantador;

/// <summary>
/// Respostas salvas da instalação de um PC (MontaWin\Perfis\nome.txt). Com um perfil, o painel só pede o disco e o FORMATAR.
/// </summary>
public record Perfil(string Nome)
{
    /// <summary>Identidade da placa-mãe (UUID do SMBIOS) do PC ao qual o perfil foi vinculado.</summary>
    public string? Pc { get; init; }
    /// <summary>Número de série do PC (ou da placa-mãe): a segunda identidade, quando a placa não tem UUID.</summary>
    public string? Serie { get; init; }
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
    public bool RemoverOneDrive { get; init; } = true;
    public bool AltoDesempenho { get; init; }
    /// <summary>Nome do computador na rede (null = o Windows escolhe).</summary>
    public string? NomePc { get; init; }
    /// <summary>Rede Wi-Fi do Windows novo (null = sem Wi-Fi); a senha segue a regra da senha da conta.</summary>
    public string? Wifi { get; init; }
    public string? SenhaWifi { get; init; }
    /// <summary>Chave do Windows deste PC (null = sem licença; o PC com licença de fábrica ativa sozinho).</summary>
    public string? Chave { get; init; }
    /// <summary>Ponto de restauração "Windows recém-instalado" depois dos programas.</summary>
    public bool PontoRestauracao { get; init; } = true;

    public string Texto()
    {
        var linhas = new List<string>
        {
            "# Perfil Aurora Win. Pode editar; apagar a linha Senha (ou SenhaWifi) faz o painel perguntar na hora.",
            $"Nome = {Nome}",
            $"PC = {Pc}",
            $"Serie = {Serie}",
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
        linhas.Add($"RemoverOneDrive = {(RemoverOneDrive ? "sim" : "nao")}");
        linhas.Add($"AltoDesempenho = {(AltoDesempenho ? "sim" : "nao")}");
        linhas.Add($"NomePc = {NomePc}");
        linhas.Add($"Wifi = {Wifi}");
        if (SenhaWifi != null) linhas.Add($"SenhaWifi = {SenhaWifi}");
        linhas.Add($"Chave = {Chave}");
        linhas.Add($"PontoRestauracao = {(PontoRestauracao ? "sim" : "nao")}");
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
            Pc = Campo("PC") is string pc && !pc.StartsWith("modelo:") ? pc : null, // versões antigas guardavam o modelo
            Serie = Campo("Serie"),
            Modelo = Campo("Modelo"),
            Edicao = Campo("Edicao") ?? "",
            Instalacao = Campo("Instalacao") ?? "apagar",
            Usuario = Campo("Usuario"),
            Senha = campos.TryGetValue("Senha", out var senha) ? senha : null, // linha vazia = sem senha
            Programas = (Campo("Programas") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Restaurar = !string.Equals(Campo("Restaurar"), "nao", StringComparison.OrdinalIgnoreCase),
            BackupDrivers = !string.Equals(Campo("BackupDrivers"), "nao", StringComparison.OrdinalIgnoreCase),
            Otimizar = !string.Equals(Campo("Otimizar"), "nao", StringComparison.OrdinalIgnoreCase),
            RemoverOneDrive = !string.Equals(Campo("RemoverOneDrive"), "nao", StringComparison.OrdinalIgnoreCase),
            AltoDesempenho = string.Equals(Campo("AltoDesempenho"), "sim", StringComparison.OrdinalIgnoreCase),
            NomePc = Campo("NomePc"),
            Wifi = Campo("Wifi"),
            SenhaWifi = campos.TryGetValue("SenhaWifi", out var senhaWifi) ? senhaWifi : null,
            Chave = Campo("Chave") is string chave ? Unattend.ChaveValida(chave) : null,
            PontoRestauracao = !string.Equals(Campo("PontoRestauracao"), "nao", StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>Tira do nome os caracteres que não podem ir em nome de arquivo ou pasta.</summary>
    public static string NomeSeguro(string nome) =>
        string.Concat(nome.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c)).Trim();
}
