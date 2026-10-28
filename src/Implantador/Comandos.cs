using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace MontaWin.Implantador;

public static partial class Comandos
{
    /// <summary>Últimas linhas de saída do último comando, para mostrar em caso de erro.</summary>
    public static readonly List<string> Saida = [];

    /// <summary>Saída inteira (stdout) do último comando, para quem precisa ler uma lista longa (ex.: apps do DISM).</summary>
    public static string UltimaSaidaCompleta { get; private set; } = "";

    /// <summary>Arquivo de registro no pendrive (null = não registra). Ajuda a descobrir o que falhou num PC.</summary>
    public static string? Log;

    public static void Registrar(string texto)
    {
        if (Log == null) return;
        try { File.AppendAllText(Log, $"[{DateTime.Now:HH:mm:ss}] {texto}{Environment.NewLine}"); }
        catch (IOException) { } // pendrive removido: o registro não pode derrubar a instalação
    }

    /// <summary>Roda um programa e repassa o percentual que ele imprimir (DISM, robocopy).</summary>
    public static int Executar(string exe, string argumentos, Action<double>? progresso = null)
    {
        lock (Saida) Saida.Clear();
        var psi = new ProcessStartInfo(exe, argumentos)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Guardar(e.Data); };
        p.BeginErrorReadLine();
        // O DISM atualiza a barra com '\r' sem quebrar linha, então lê caractere a caractere.
        var linha = new StringBuilder();
        var completa = new StringBuilder();
        int c;
        while ((c = p.StandardOutput.Read()) != -1)
        {
            if (c is '\r' or '\n')
            {
                if (linha.Length == 0) continue;
                var texto = linha.ToString();
                linha.Clear();
                Guardar(texto);
                if (progresso != null && Percentual(texto) is double pct) progresso(pct);
            }
            else linha.Append((char)c);
            completa.Append((char)c);
        }
        if (linha.Length > 0) Guardar(linha.ToString());
        p.WaitForExit();
        UltimaSaidaCompleta = completa.ToString();
        lock (Saida) Registrar($"{exe} {argumentos} -> código {p.ExitCode}" + (p.ExitCode == 0 ? "" : "\n    " + string.Join("\n    ", Saida)));
        return p.ExitCode;
    }

    static void Guardar(string linha)
    {
        if (string.IsNullOrWhiteSpace(linha)) return;
        lock (Saida)
        {
            Saida.Add(linha.Trim());
            if (Saida.Count > 12) Saida.RemoveAt(0);
        }
    }

    [GeneratedRegex(@"(\d{1,3})(?:[.,]\d+)?\s*%")]
    private static partial Regex RegexPercentual();

    public static double? Percentual(string linha) =>
        RegexPercentual().Match(linha) is { Success: true } m ? double.Parse(m.Groups[1].Value) : null;

    /// <summary>Letras livres para as partições novas (pula A, B e X, que é o próprio WinPE).</summary>
    public static char[] LetrasLivres(int quantidade)
    {
        var usadas = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        return "SWRDEFGHIJKLMNOPQTUVYZ".Where(l => !usadas.Contains(l)).Take(quantidade).ToArray();
    }
}
