using MontaWin.Implantador;

namespace MontaWin.Montador;

public record IsoAberta(string Iso, char Letra, string Install, List<Edicao> Edicoes);

public record Gravacao(IsoAberta Iso, int[] Indices, Disco Pendrive, string Implantador);

/// <summary>
/// Monta o pendrive: partição FAT32 de boot (UEFI e BIOS) com o WinPE da ISO abrindo o Implantador,
/// e partição NTFS com as imagens (sem o limite de 4 GB do FAT32) e os backups.
/// </summary>
public static class Pendrive
{
    public const int BootMb = 2048;

    public static string ScriptParticionar(int disco, char boot, char dados) => $"""
        select disk {disco}
        clean
        convert mbr
        create partition primary size={BootMb}
        format quick fs=fat32 label=MONTAWIN
        active
        assign letter={boot}
        create partition primary
        format quick fs=ntfs label=MONTAWIN-DADOS
        assign letter={dados}
        exit
        """;

    // O WinPE lê este arquivo ao iniciar: em vez do instalador da Microsoft, abre o nosso painel.
    // wpeinit vem antes porque o winpeshl.ini substitui o startnet.cmd (rede, drivers PnP).
    public const string WinpeshlIni = """
        [LaunchApps]
        %SYSTEMROOT%\System32\wpeinit.exe
        %SYSTEMDRIVE%\MontaWin\Implantador.exe
        """;

    static void Exigir(int codigo, string mensagem)
    {
        if (codigo != 0) lock (Comandos.Saida) throw new Exception(mensagem + "\n" + string.Join("\n", Comandos.Saida));
    }

    static string Aspas(string s) => s.Replace("'", "''");

    public static IsoAberta AbrirIso(string iso)
    {
        FecharIso(iso);
        Exigir(Comandos.Executar("powershell", $"-NoProfile -Command \"(Mount-DiskImage -ImagePath '{Aspas(iso)}' -PassThru | Get-Volume).DriveLetter\""),
            "Não consegui abrir a ISO.");
        string letra;
        lock (Comandos.Saida) letra = Comandos.Saida.LastOrDefault() ?? "";
        if (letra.Length != 1) throw new Exception("Não consegui descobrir a letra da ISO montada.");
        var install = new[] { "install.wim", "install.esd" }.Select(n => $@"{letra}:\sources\{n}").FirstOrDefault(File.Exists)
                      ?? throw new Exception("Essa ISO não tem sources\\install.wim nem install.esd. Use uma ISO oficial do Windows.");
        return new IsoAberta(iso, letra[0], install, Wim.Ler(install));
    }

    public static void FecharIso(string iso) =>
        Comandos.Executar("powershell", $"-NoProfile -Command \"Dismount-DiskImage -ImagePath '{Aspas(iso)}' -ErrorAction SilentlyContinue\"");

    /// <summary>Grava tudo. <paramref name="progresso"/> recebe a etapa (null = mesma etapa) e o percentual geral.</summary>
    public static void Gravar(Gravacao g, Action<string?, double> progresso)
    {
        var letras = Comandos.LetrasLivres(2);
        if (letras.Length < 2) throw new Exception("Não há letras de unidade livres.");
        var (boot, dados) = (letras[0], letras[1]);
        char iso = g.Iso.Letra;
        var trabalho = Path.Combine(Path.GetTempPath(), "MontaWin");
        var mount = Path.Combine(trabalho, "mount");
        if (Directory.Exists(trabalho))
        {
            Comandos.Executar("dism", $"/Unmount-Image /MountDir:\"{mount}\" /Discard"); // sobra de uma gravação interrompida
            Directory.Delete(trabalho, recursive: true);
        }
        Directory.CreateDirectory(mount);

        progresso("Apagando e particionando o pendrive", 0);
        var script = Path.Combine(trabalho, "diskpart.txt");
        File.WriteAllText(script, ScriptParticionar(g.Pendrive.Numero, boot, dados));
        Exigir(Comandos.Executar("diskpart", $"/s \"{script}\""), "O diskpart não conseguiu preparar o pendrive.");
        Exigir(Comandos.Executar($@"{iso}:\boot\bootsect.exe", $"/nt60 {boot}: /mbr /force"), "O bootsect não conseguiu tornar o pendrive bootável.");

        progresso("Copiando os arquivos de inicialização", 3);
        if (Comandos.Executar("robocopy", $@"{iso}:\ {boot}:\ /E /XD {iso}:\sources {iso}:\support /R:1 /W:1 /NP /NFL /NDL /NJH /NJS") >= 8)
            throw new Exception("Falha ao copiar os arquivos de inicialização.");

        progresso("Preparando o painel de inicialização (WinPE)", 6);
        var bootWim = Path.Combine(trabalho, "boot.wim");
        // Índice 2 do boot.wim é o WinPE do instalador; exporta só ele para o pendrive ficar menor.
        Exigir(Comandos.Executar("dism", $@"/Export-Image /SourceImageFile:{iso}:\sources\boot.wim /SourceIndex:2 /DestinationImageFile:""{bootWim}"" /Bootable"),
            "O DISM não conseguiu copiar o boot.wim da ISO.");
        Exigir(Comandos.Executar("dism", $"/Mount-Image /ImageFile:\"{bootWim}\" /Index:1 /MountDir:\"{mount}\"", p => progresso(null, 6 + p * 0.06)),
            "O DISM não conseguiu abrir o boot.wim.");
        try
        {
            Directory.CreateDirectory(Path.Combine(mount, "MontaWin"));
            File.Copy(g.Implantador, Path.Combine(mount, "MontaWin", "Implantador.exe"), overwrite: true);
            var ini = Path.Combine(mount, "Windows", "System32", "winpeshl.ini");
            if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
            File.WriteAllText(ini, WinpeshlIni);
        }
        catch
        {
            Comandos.Executar("dism", $"/Unmount-Image /MountDir:\"{mount}\" /Discard");
            throw;
        }
        Exigir(Comandos.Executar("dism", $"/Unmount-Image /MountDir:\"{mount}\" /Commit"), "O DISM não conseguiu salvar o boot.wim.");
        Directory.CreateDirectory($@"{boot}:\sources");
        File.Copy(bootWim, $@"{boot}:\sources\boot.wim", overwrite: true);

        var pastaImagens = $@"{dados}:\MontaWin\Imagens";
        Directory.CreateDirectory(pastaImagens);
        var destino = Path.Combine(pastaImagens, Path.GetFileNameWithoutExtension(g.Iso.Iso) + ".wim");
        double passo = 85.0 / g.Indices.Length;
        for (int k = 0; k < g.Indices.Length; k++)
        {
            double inicio = 15 + k * passo;
            var nome = g.Iso.Edicoes.First(e => e.Indice == g.Indices[k]).Nome;
            progresso($"Copiando {nome} ({k + 1} de {g.Indices.Length})", inicio);
            Exigir(Comandos.Executar("dism",
                $"/Export-Image /SourceImageFile:\"{g.Iso.Install}\" /SourceIndex:{g.Indices[k]} /DestinationImageFile:\"{destino}\" /Compress:max",
                p => progresso(null, inicio + p * passo / 100)), $"O DISM não conseguiu copiar {nome}.");
        }

        Directory.Delete(trabalho, recursive: true);
        progresso("Pendrive pronto", 100);
    }
}
