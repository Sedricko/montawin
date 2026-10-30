namespace MontaWin.Montador;

/// <summary>
/// Configurações internas do Aurora Win (engrenagem), guardadas neste PC em %LOCALAPPDATA%\AuroraWin\config.txt.
/// O tema também vai para o pendrive: o painel do boot fica igual ao app.
/// </summary>
public sealed class Configuracoes
{
    public string Tema { get; set; } = "ondas";
    /// <summary>Boot com o certificado novo (Windows UEFI CA 2023), quando a ISO traz os arquivos dele.</summary>
    public bool BootNovo { get; set; }
    public bool Rapida { get; set; }
    /// <summary>Esconder do Windows a parte de dados do pendrive (perfis, licença, backups, imagens). Só muda com o PIN.</summary>
    public bool Esconder { get; set; }
    /// <summary>PIN de administrador: só o resumo (PBKDF2 com sal), nunca o PIN.</summary>
    public string? Pin { get; set; }

    public bool TemPin => Pin != null;

    public void DefinirPin(string pin)
    {
        var sal = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        Pin = Convert.ToBase64String(sal) + ":" + Convert.ToBase64String(Resumo(pin, sal));
    }

    public bool PinCerto(string pin)
    {
        if (Pin?.Split(':') is not [var sal, var resumo]) return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Resumo(pin, Convert.FromBase64String(sal)), Convert.FromBase64String(resumo));
    }

    static byte[] Resumo(string pin, byte[] sal) =>
        System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(pin, sal, 200_000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);

    static string Arquivo => Path.Combine(ListaProgramas.Pasta, "config.txt");

    public static Configuracoes Ler()
    {
        var c = new Configuracoes();
        if (!File.Exists(Arquivo)) return c;
        foreach (var linha in File.ReadAllLines(Arquivo))
        {
            var p = linha.Split('=', 2, StringSplitOptions.TrimEntries);
            if (p.Length < 2) continue;
            switch (p[0])
            {
                case "Tema": c.Tema = p[1]; break;
                case "BootNovo": c.BootNovo = p[1] == "sim"; break;
                case "Rapida": c.Rapida = p[1] == "sim"; break;
                case "Esconder": c.Esconder = p[1] == "sim"; break;
                case "Pin": c.Pin = p[1].Length > 0 ? p[1] : null; break;
            }
        }
        return c;
    }

    public void Salvar()
    {
        Directory.CreateDirectory(ListaProgramas.Pasta);
        File.WriteAllLines(Arquivo, [$"Tema = {Tema}", $"BootNovo = {(BootNovo ? "sim" : "nao")}", $"Rapida = {(Rapida ? "sim" : "nao")}",
            $"Esconder = {(Esconder ? "sim" : "nao")}", $"Pin = {Pin}"]);
    }
}
