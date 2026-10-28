using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MontaWin.Implantador;

public record Disco(int Numero, long Tamanho, string Modelo, bool Usb, char? LetraWindows)
{
    public string Descricao => $"Disco {Numero}  {Tui.Ajustar(Modelo, 30)} {Formatar.Tamanho(Tamanho),9}" +
                               (LetraWindows is char l ? $"   Windows detectado ({l}:)" : "");
}

// Lê os discos direto pelo Windows (DeviceIoControl): não depende de WMI nem do idioma do diskpart.
public static partial class Discos
{
    const uint GenericRead = 0x80000000, CompartilharLeituraEscrita = 3, AbrirExistente = 3;
    const uint IoctlTamanho = 0x0007405C;          // IOCTL_DISK_GET_LENGTH_INFO
    const uint IoctlPropriedades = 0x002D1400;     // IOCTL_STORAGE_QUERY_PROPERTY
    const uint IoctlExtentesVolume = 0x00560000;   // IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS
    static readonly uint[] BarramentosUsb = [7, 0xC, 0xD]; // USB, SD, MMC

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string nome, uint acesso, uint compartilhar, nint seguranca, uint disposicao, uint flags, nint modelo);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DeviceIoControl(SafeFileHandle h, uint codigo, void* entrada, int tamEntrada, void* saida, int tamSaida, out int retornado, nint overlapped);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFirmwareType(out int tipo);

    public static bool Uefi => GetFirmwareType(out int t) && t == 2;

    static unsafe bool Ioctl(SafeFileHandle h, uint codigo, byte[] entrada, byte[] saida)
    {
        fixed (byte* e = entrada, s = saida)
            return DeviceIoControl(h, codigo, e, entrada.Length, s, saida.Length, out _, 0);
    }

    public static List<Disco> Listar()
    {
        var windowsPorDisco = new Dictionary<int, char>();
        foreach (var d in DriveInfo.GetDrives())
        {
            char letra = d.Name[0];
            if (File.Exists($@"{letra}:\Windows\System32\config\SYSTEM") && DiscoDaLetra(letra) is int n)
                windowsPorDisco.TryAdd(n, letra);
        }

        var discos = new List<Disco>();
        for (int n = 0; n < 32; n++)
        {
            using var h = CreateFile($@"\\.\PhysicalDrive{n}", GenericRead, CompartilharLeituraEscrita, 0, AbrirExistente, 0, 0);
            if (h.IsInvalid) continue;
            var tam = new byte[8];
            if (!Ioctl(h, IoctlTamanho, [], tam)) continue;
            var desc = new byte[1024];
            Ioctl(h, IoctlPropriedades, new byte[12], desc); // StorageDeviceProperty + PropertyStandardQuery = zeros
            string modelo = $"{Texto(desc, 12)} {Texto(desc, 16)}".Trim();
            bool usb = BarramentosUsb.Contains(BitConverter.ToUInt32(desc, 28));
            discos.Add(new Disco(n, BitConverter.ToInt64(tam), modelo.Length > 0 ? modelo : "Disco", usb,
                windowsPorDisco.TryGetValue(n, out var l) ? l : null));
        }
        return discos;
    }

    /// <summary>Número do disco físico onde fica a letra (null para CD, RAM disk etc.).</summary>
    public static int? DiscoDaLetra(char letra)
    {
        using var vol = CreateFile($@"\\.\{letra}:", 0, CompartilharLeituraEscrita, 0, AbrirExistente, 0, 0);
        var ext = new byte[64]; // VOLUME_DISK_EXTENTS: o número do disco do 1º trecho fica no byte 8
        return !vol.IsInvalid && Ioctl(vol, IoctlExtentesVolume, [], ext) ? BitConverter.ToInt32(ext, 8) : null;
    }

    // STORAGE_DEVICE_DESCRIPTOR guarda deslocamentos para textos ASCII terminados em zero.
    static string Texto(byte[] buf, int campo)
    {
        int ini = BitConverter.ToInt32(buf, campo);
        if (ini <= 0 || ini >= buf.Length) return "";
        int fim = Array.IndexOf(buf, (byte)0, ini);
        return System.Text.Encoding.ASCII.GetString(buf, ini, (fim < 0 ? buf.Length : fim) - ini).Trim();
    }
}

public static class Formatar
{
    public static string Tamanho(long bytes) => bytes >= 1_000_000_000_000 ? $"{bytes / 1e12:0.0} TB" : $"{bytes / 1e9:0} GB";
}
