using Microsoft.Win32;

namespace MontaWin.Implantador;

public static class Sistema
{
    /// <summary>Ex.: "Dell Inc. Inspiron 15 · 16 GB RAM · UEFI"</summary>
    public static string Resumo()
    {
        using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        var modelo = $"{bios?.GetValue("SystemManufacturer")} {bios?.GetValue("SystemProductName")}".Trim();
        var ram = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024 * 1024);
        return $"{modelo} · {ram:0} GB RAM · {(Discos.Uefi ? "UEFI" : "BIOS")}".TrimStart(' ', '·');
    }
}
