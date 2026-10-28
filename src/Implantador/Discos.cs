using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MontaWin.Implantador;

public enum TipoParticao { Efi, Msr, SistemaMbr, RecuperacaoWindows, Fabricante, Dados, Outra }

public record Particao(int Numero, long Inicio, long Tamanho, TipoParticao Tipo, string Nome, bool Ativa)
{
    public char? Letra { get; init; }
    public string Rotulo { get; init; } = "";
    public bool TemWindows { get; init; }

    /// <summary>Só partições de dados podem receber o Windows: EFI, recuperação e fabricante ficam protegidas.</summary>
    public bool Selecionavel => Tipo == TipoParticao.Dados;

    public string Descricao
    {
        get
        {
            var tipo = Tipo switch
            {
                TipoParticao.Efi => "EFI (inicialização)",
                TipoParticao.Msr => "MSR (reservada)",
                TipoParticao.SistemaMbr => "Sistema (inicialização)",
                TipoParticao.RecuperacaoWindows => "Recuperação do Windows",
                TipoParticao.Fabricante => "Oculta/fabricante · protegida",
                TipoParticao.Dados => Rotulo.Length > 0 ? Rotulo : "Dados",
                _ => "Outro sistema · protegida",
            };
            var nome = Tipo == TipoParticao.Fabricante && Nome.Length > 0 ? $" ({Nome})" : "";
            return $"Partição {Numero,-2} {(Letra is char l ? l + ":" : "  ")}  {Formatar.Ajustar(tipo + nome, 38)} {Formatar.Tamanho(Tamanho),9}" +
                   (TemWindows ? "   Windows detectado" : "");
        }
    }
}

public record Disco(int Numero, long Tamanho, string Modelo, bool Usb, bool Gpt, IReadOnlyList<Particao> Particoes)
{
    public Saude Saude { get; init; } = Saude.Desconhecida;
    /// <summary>SSD (NVMe ou sem "penalidade de busca"): alguns ajustes só valem para SSD.</summary>
    public bool Ssd { get; init; }
    public char? LetraWindows => Particoes.FirstOrDefault(p => p.TemWindows)?.Letra;
    public string Descricao => $"Disco {Numero}  {Formatar.Ajustar(Modelo, 26)} {Formatar.Tamanho(Tamanho),8}  {Formatar.Ajustar(Saude.Curta, 8)}" +
                               (LetraWindows is char l ? $"  Windows ({l}:)" : "");
}

// Lê os discos direto pelo Windows (DeviceIoControl): não depende de WMI nem do idioma do diskpart.
public static partial class Discos
{
    const uint GenericRead = 0x80000000, CompartilharLeituraEscrita = 3, AbrirExistente = 3;
    const uint IoctlTamanho = 0x0007405C;          // IOCTL_DISK_GET_LENGTH_INFO
    const uint IoctlLayout = 0x00070050;           // IOCTL_DISK_GET_DRIVE_LAYOUT_EX
    const uint IoctlPropriedades = 0x002D1400;     // IOCTL_STORAGE_QUERY_PROPERTY
    const uint IoctlExtentesVolume = 0x00560000;   // IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS
    const uint IoctlPrevisaoFalha = 0x002D1100;    // IOCTL_STORAGE_PREDICT_FAILURE (SMART de discos SATA)
    const uint BarramentoNvme = 17;
    static readonly uint[] BarramentosUsb = [7, 0xC, 0xD]; // USB, SD, MMC

    static readonly Guid GuidEfi = new("c12a7328-f81f-11d2-ba4b-00a0c93ec93b");
    static readonly Guid GuidMsr = new("e3c9e316-0b5c-4db8-817d-f92df00215ae");
    static readonly Guid GuidDados = new("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7");
    static readonly Guid GuidRecuperacao = new("de94bba4-06d1-4d40-a16a-bfd50179d6ac");
    // Até 2 GB é o WinRE do Windows; maior que isso é imagem de fábrica (Dell, HP, Lenovo...).
    const long LimiteWinRe = 2L << 30;
    // Atributos GPT: obrigatória (bit 0), oculta (62), sem letra (63)
    const ulong AtributosOcultos = 0xC000000000000001;

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

    /// <summary>O disco com esse número agora ainda é o pendrive escolhido antes (USB, mesmo modelo e tamanho)?</summary>
    public static bool MesmoPendrive(Disco escolhido, Disco? agora) =>
        agora is { Usb: true } && agora.Modelo == escolhido.Modelo && agora.Tamanho == escolhido.Tamanho;

    public static List<Disco> Listar()
    {
        // (disco, início da partição) -> letra
        var letras = new Dictionary<(int, long), char>();
        foreach (var d in DriveInfo.GetDrives())
            if (Extente(d.Name[0]) is (int disco, long inicio)) letras.TryAdd((disco, inicio), d.Name[0]);

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
            uint barramento = BitConverter.ToUInt32(desc, 28);
            bool usb = BarramentosUsb.Contains(barramento);
            var layout = new byte[48 + 128 * 144];
            var (gpt, particoes) = Ioctl(h, IoctlLayout, [], layout) ? LerLayout(layout) : (false, new List<Particao>());
            particoes = particoes.Select(p => letras.TryGetValue((n, p.Inicio), out var l) ? p with
            {
                Letra = l,
                Rotulo = Rotulo(l),
                TemWindows = File.Exists($@"{l}:\Windows\System32\config\SYSTEM"),
            } : p).ToList();
            discos.Add(new Disco(n, BitConverter.ToInt64(tam), modelo.Length > 0 ? modelo : "Disco", usb, gpt, particoes)
            {
                Saude = usb ? Saude.Desconhecida : LerSaude(h, barramento), // adaptadores USB quase nunca repassam o SMART
                Ssd = barramento == BarramentoNvme || SemPenalidadeDeBusca(h),
            });
        }
        return discos;
    }

    // StorageDeviceSeekPenaltyProperty (7): DEVICE_SEEK_PENALTY_DESCRIPTOR.IncursSeekPenalty no byte 8; false = SSD.
    static bool SemPenalidadeDeBusca(SafeFileHandle h)
    {
        var consulta = new byte[12];
        BitConverter.GetBytes(7).CopyTo(consulta, 0);
        var resposta = new byte[12];
        return Ioctl(h, IoctlPropriedades, consulta, resposta) && resposta[8] == 0;
    }

    static Saude LerSaude(SafeFileHandle h, uint barramento)
    {
        if (barramento == BarramentoNvme)
        {
            // STORAGE_PROPERTY_QUERY (8 bytes) + STORAGE_PROTOCOL_SPECIFIC_DATA (40) + página de saúde do NVMe (512)
            var consulta = new byte[8 + 40 + 512];
            BitConverter.GetBytes(50).CopyTo(consulta, 0);   // StorageDeviceProtocolSpecificProperty
            BitConverter.GetBytes(3).CopyTo(consulta, 8);    // ProtocolTypeNvme
            BitConverter.GetBytes(2).CopyTo(consulta, 12);   // NVMeDataTypeLogPage
            BitConverter.GetBytes(2).CopyTo(consulta, 16);   // página 02h: SMART / Health Information
            BitConverter.GetBytes(40).CopyTo(consulta, 24);  // os dados vêm logo depois desta estrutura
            BitConverter.GetBytes(512).CopyTo(consulta, 28);
            var resposta = new byte[consulta.Length];
            return Ioctl(h, IoctlPropriedades, consulta, resposta) ? Saude.Nvme(resposta[48..]) : Saude.Desconhecida;
        }
        var previsao = new byte[4 + 512]; // STORAGE_PREDICT_FAILURE: PredictFailure + dados do SMART
        return Ioctl(h, IoctlPrevisaoFalha, [], previsao)
            ? Saude.Ata(BitConverter.ToUInt32(previsao, 0) != 0, previsao[4..])
            : Saude.Desconhecida;
    }

    static string Rotulo(char letra)
    {
        try { return new DriveInfo(letra.ToString()).VolumeLabel; } catch { return ""; }
    }

    /// <summary>Lê DRIVE_LAYOUT_INFORMATION_EX: cabeçalho de 48 bytes + entradas de 144 bytes.</summary>
    public static (bool Gpt, List<Particao> Particoes) LerLayout(byte[] b)
    {
        bool gpt = BitConverter.ToInt32(b, 0) == 1;
        int quantidade = BitConverter.ToInt32(b, 4);
        var lista = new List<Particao>();
        for (int i = 0; i < quantidade && 48 + (i + 1) * 144 <= b.Length; i++)
        {
            int o = 48 + i * 144;
            long inicio = BitConverter.ToInt64(b, o + 8), tamanho = BitConverter.ToInt64(b, o + 16);
            int numero = BitConverter.ToInt32(b, o + 24);
            if (numero == 0 || tamanho == 0) continue; // entradas vazias do MBR e a partição estendida
            if (gpt)
            {
                var tipo = new Guid(b.AsSpan(o + 32, 16));
                var nome = Encoding.Unicode.GetString(b, o + 72, 72).TrimEnd('\0');
                lista.Add(new Particao(numero, inicio, tamanho, ClassificarGpt(tipo, BitConverter.ToUInt64(b, o + 64), tamanho), nome, false));
            }
            else
            {
                bool ativa = b[o + 33] != 0;
                lista.Add(new Particao(numero, inicio, tamanho, ClassificarMbr(b[o + 32], ativa, tamanho), "", ativa));
            }
        }
        return (gpt, lista);
    }

    public static TipoParticao ClassificarGpt(Guid tipo, ulong atributos, long tamanho) =>
        tipo == GuidEfi ? TipoParticao.Efi
        : tipo == GuidMsr ? TipoParticao.Msr
        : tipo == GuidRecuperacao ? (tamanho <= LimiteWinRe ? TipoParticao.RecuperacaoWindows : TipoParticao.Fabricante)
        : tipo == GuidDados ? ((atributos & AtributosOcultos) != 0 ? TipoParticao.Fabricante : TipoParticao.Dados)
        : TipoParticao.Outra;

    public static TipoParticao ClassificarMbr(byte tipo, bool ativa, long tamanho) => tipo switch
    {
        0x07 when ativa && tamanho <= LimiteWinRe => TipoParticao.SistemaMbr, // "Reservado pelo Sistema"
        0x07 or 0x0B or 0x0C => TipoParticao.Dados,
        0x27 => tamanho <= LimiteWinRe ? TipoParticao.RecuperacaoWindows : TipoParticao.Fabricante,
        0x12 or 0xDE or 0x17 or 0x1C or 0x84 => TipoParticao.Fabricante, // diagnóstico/recuperação de fábrica, ocultas, hibernação
        _ => TipoParticao.Outra,
    };

    /// <summary>Disco e início da partição onde fica a letra (null para CD, RAM disk etc.).</summary>
    static (int Disco, long Inicio)? Extente(char letra)
    {
        using var vol = CreateFile($@"\\.\{letra}:", 0, CompartilharLeituraEscrita, 0, AbrirExistente, 0, 0);
        var ext = new byte[64]; // VOLUME_DISK_EXTENTS: 1º trecho com o disco no byte 8 e o início no byte 16
        return !vol.IsInvalid && Ioctl(vol, IoctlExtentesVolume, [], ext) ? (BitConverter.ToInt32(ext, 8), BitConverter.ToInt64(ext, 16)) : null;
    }

    public static int? DiscoDaLetra(char letra) => Extente(letra)?.Disco;

    // STORAGE_DEVICE_DESCRIPTOR guarda deslocamentos para textos ASCII terminados em zero.
    static string Texto(byte[] buf, int campo)
    {
        int ini = BitConverter.ToInt32(buf, campo);
        if (ini <= 0 || ini >= buf.Length) return "";
        int fim = Array.IndexOf(buf, (byte)0, ini);
        return Encoding.ASCII.GetString(buf, ini, (fim < 0 ? buf.Length : fim) - ini).Trim();
    }
}

public static class Formatar
{
    public static string Tamanho(long bytes) =>
        bytes >= 1_000_000_000_000 ? $"{bytes / 1e12:0.0} TB" : bytes >= 1_000_000_000 ? $"{bytes / 1e9:0} GB" : $"{bytes / 1e6:0} MB";

    /// <summary>Corta ou completa com espaços: colunas alinhadas (o painel gráfico também usa as colunas).</summary>
    public static string Ajustar(string s, int n) => s.Length > n ? s[..n] : s.PadRight(n);
}
