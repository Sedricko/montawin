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

    /// <summary>Identidade única do PC: UUID da placa-mãe (SMBIOS); se a placa não tiver, o modelo.</summary>
    public static unsafe string IdPc()
    {
        const uint Rsmb = 0x52534D42; // 'RSMB': tabela SMBIOS
        uint tamanho = GetSystemFirmwareTable(Rsmb, 0, null, 0);
        var tabela = new byte[tamanho];
        fixed (byte* p = tabela)
            if (tamanho > 0 && GetSystemFirmwareTable(Rsmb, 0, p, tamanho) == tamanho && UuidSmbios(tabela) is string uuid)
                return uuid;
        return "modelo:" + Modelo();
    }

    /// <summary>UUID da estrutura tipo 1 (System Information); null se ausente ou genérico (tudo 0 ou tudo FF).</summary>
    public static string? UuidSmbios(byte[] tabela)
    {
        int o = 8; // cabeçalho RawSMBIOSData
        while (o + 4 <= tabela.Length)
        {
            byte tipo = tabela[o], comprimento = tabela[o + 1];
            if (comprimento < 4 || o + comprimento > tabela.Length || tipo == 127) break;
            if (tipo == 1 && comprimento >= 0x18)
            {
                var uuid = tabela.AsSpan(o + 8, 16);
                return uuid.IndexOfAnyExcept((byte)0) < 0 || uuid.IndexOfAnyExcept((byte)0xFF) < 0 ? null : new Guid(uuid).ToString();
            }
            int s = o + comprimento; // depois da parte fixa vêm os textos, terminados por dois zeros
            while (s + 1 < tabela.Length && (tabela[s] != 0 || tabela[s + 1] != 0)) s++;
            o = s + 2;
        }
        return null;
    }

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
}
