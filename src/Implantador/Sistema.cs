using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MontaWin.Implantador;

public static partial class Sistema
{
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetSystemPowerStatus(byte* status);

    [LibraryImport("kernel32.dll")]
    private static unsafe partial uint GetSystemFirmwareTable(uint provedor, uint id, byte* buffer, uint tamanho);

    /// <summary>Ex.: "Dell Inc. Inspiron 15 3520"</summary>
    public static string Modelo()
    {
        using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        return $"{bios?.GetValue("SystemManufacturer")} {bios?.GetValue("SystemProductName")}".Trim();
    }

    /// <summary>
    /// As duas identidades de fábrica do PC, gravadas na BIOS: o UUID da placa-mãe e o número de série (o da etiqueta do
    /// notebook ou PC de marca; em PC montado, o da placa-mãe). Valores genéricos de fábrica viram null.
    /// </summary>
    public static unsafe (string? Uuid, string? Serie) Identidade()
    {
        const uint Rsmb = 0x52534D42; // 'RSMB': tabela SMBIOS
        uint tamanho = GetSystemFirmwareTable(Rsmb, 0, null, 0);
        var tabela = new byte[tamanho];
        fixed (byte* p = tabela)
            if (tamanho == 0 || GetSystemFirmwareTable(Rsmb, 0, p, tamanho) != tamanho) return (null, null);
        return (UuidSmbios(tabela), SerieSmbios(tabela));
    }

    /// <summary>
    /// Mesmo PC? Compara o UUID quando os dois têm; senão, o número de série. Sem nenhum dos dois, nunca é o mesmo
    /// (o modelo sozinho confundiria dois PCs iguais).
    /// </summary>
    public static bool MesmoPc(string? uuidA, string? serieA, string? uuidB, string? serieB) =>
        uuidA != null && uuidB != null ? uuidA.Equals(uuidB, StringComparison.OrdinalIgnoreCase)
        : serieA != null && serieB != null && serieA.Equals(serieB, StringComparison.OrdinalIgnoreCase);

    // Estruturas SMBIOS: (tipo, parte fixa, textos). Os textos vêm depois da parte fixa, terminados por dois zeros.
    static IEnumerable<(byte Tipo, int Inicio, int Comprimento, List<string> Textos)> Estruturas(byte[] tabela)
    {
        int o = 8; // cabeçalho RawSMBIOSData
        while (o + 4 <= tabela.Length)
        {
            byte tipo = tabela[o], comprimento = tabela[o + 1];
            if (comprimento < 4 || o + comprimento > tabela.Length || tipo == 127) yield break;
            var textos = new List<string>();
            int s = o + comprimento;
            while (s < tabela.Length && tabela[s] != 0)
            {
                int fim = Array.IndexOf(tabela, (byte)0, s);
                if (fim < 0) fim = tabela.Length;
                textos.Add(System.Text.Encoding.ASCII.GetString(tabela, s, fim - s).Trim());
                s = fim + 1;
            }
            yield return (tipo, o, comprimento, textos);
            o = (textos.Count == 0 ? s + 1 : s) + 1;
        }
    }

    /// <summary>UUID da estrutura tipo 1 (System Information); null se ausente ou genérico (tudo 0 ou tudo FF).</summary>
    public static string? UuidSmbios(byte[] tabela)
    {
        foreach (var (tipo, inicio, comprimento, _) in Estruturas(tabela))
            if (tipo == 1 && comprimento >= 0x18)
            {
                var uuid = tabela.AsSpan(inicio + 8, 16);
                return uuid.IndexOfAnyExcept((byte)0) < 0 || uuid.IndexOfAnyExcept((byte)0xFF) < 0 ? null : new Guid(uuid).ToString();
            }
        return null;
    }

    /// <summary>Número de série do PC (tipo 1) ou, se for genérico, o da placa-mãe (tipo 2).</summary>
    public static string? SerieSmbios(byte[] tabela)
    {
        string? Serie(byte tipoProcurado) => Estruturas(tabela)
            .Where(e => e.Tipo == tipoProcurado && e.Comprimento > 7)
            .Select(e => tabela[e.Inicio + 7] is var i && i > 0 && i <= e.Textos.Count ? e.Textos[i - 1] : null)
            .FirstOrDefault(SerieValida);
        return Serie(1) ?? Serie(2);
    }

    static readonly string[] SeriesGenericas = ["to be filled by o.e.m.", "default string", "system serial number", "chassis serial number",
        "not specified", "not applicable", "none", "n/a", "na", "serial", "serial number", "invalid", "123456789", "0123456789", "base board serial number"];

    /// <summary>Número de série de verdade (não "To be filled by O.E.M.", "Default string", "0000..." e parecidos).</summary>
    public static bool SerieValida(string? serie) =>
        serie is { Length: >= 3 } && !SeriesGenericas.Contains(serie.ToLowerInvariant()) && serie.Distinct().Count() > 1;

    /// <summary>Ex.: "Dell Inc. Inspiron 15 · 16 GB RAM · UEFI"</summary>
    public static string Resumo()
    {
        var modelo = Modelo();
        var ram = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024 * 1024);
        return $"{modelo} · {ram:0} GB RAM · {(Discos.Uefi ? "UEFI" : "BIOS")}".TrimStart(' ', '·');
    }

    /// <summary>Notebook fora da tomada (SYSTEM_POWER_STATUS.ACLineStatus = 0). PCs de mesa respondem 1 ou 255.</summary>
    public static unsafe bool NaBateria
    {
        get
        {
            byte* status = stackalloc byte[12];
            return GetSystemPowerStatus(status) && status[0] == 0;
        }
    }

    /// <summary>Lê o começo do arquivo e mede a velocidade do pendrive em MB/s.</summary>
    public static double VelocidadeLeitura(string arquivo, int megabytes = 128)
    {
        using var f = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        var buffer = new byte[1 << 20];
        var tempo = Stopwatch.StartNew();
        long lido = 0;
        for (int n; lido < (long)megabytes << 20 && (n = f.Read(buffer)) > 0;) lido += n;
        return lido / 1e6 / Math.Max(tempo.Elapsed.TotalSeconds, 0.001);
    }

    /// <summary>Aviso para pendrive lento (USB 2.0 ou porta lenta), com a estimativa de tempo; null se estiver bom.</summary>
    public static string? AvisoPendriveLento(double mbPorSegundo, long bytesImagem) =>
        mbPorSegundo >= 60 ? null
        : $"Pendrive lento ({mbPorSegundo:0} MB/s): use uma porta USB 3.0 (azul). Só a imagem deve levar ~{Math.Ceiling(bytesImagem / 1e6 / mbPorSegundo / 60):0} min.";

    /// <summary>Nomes das redes na saída do "netsh wlan show networks" (linhas "SSID 1 : nome"), sem repetir.</summary>
    public static List<string> RedesNaSaida(string saida) =>
        saida.Split('\n').Select(l => System.Text.RegularExpressions.Regex.Match(l.Trim(), @"^SSID \d+\s*:\s*(.+)$"))
            .Where(m => m.Success).Select(m => m.Groups[1].Value.Trim()).Where(n => n.Length > 0).Distinct().ToList();
}
