using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MontaWin.Implantador;

public static partial class Sistema
{
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetSystemPowerStatus(byte* status);

    /// <summary>Ex.: "Dell Inc. Inspiron 15 · 16 GB RAM · UEFI"</summary>
    public static string Resumo()
    {
        using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        var modelo = $"{bios?.GetValue("SystemManufacturer")} {bios?.GetValue("SystemProductName")}".Trim();
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
}
