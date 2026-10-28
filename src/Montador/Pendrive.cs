using MontaWin.Implantador;

namespace MontaWin.Montador;

public record IsoAberta(string Iso, char Letra, string Install, List<Edicao> Edicoes);

/// <param name="Rapida">Compressão rápida: imagem ~30% maior, mas a instalação descomprime mais depressa.</param>
/// <param name="Manter">Letras de um pendrive MontaWin existente: só adiciona a ISO e atualiza o painel, sem apagar.</param>
/// <param name="Tema">Visual do painel no boot: ondas, bloom ou pastel (gravado em MontaWin\tema.txt).</param>
/// <param name="Atualizacoes">Pasta com as atualizações .msu do Microsoft Update Catalog, injetadas em cada edição (null = nenhuma).</param>
public record Gravacao(IsoAberta Iso, int[] Indices, Disco Pendrive, string Implantador, bool Rapida, (char Boot, char Dados)? Manter,
    string? Atualizacoes = null, string Tema = "ondas");

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

    const string LeiaMeDrivers = """
        Coloque aqui drivers extras (pastas com arquivos .inf), por exemplo Intel RST/RAID ou VMD,
        quando o disco do PC não aparece no painel. O painel carrega estes drivers ao abrir e
        também os instala no Windows novo.
        """;

    /// <summary>Letras da partição de boot e de dados se o pendrive já foi gravado pelo MontaWin.</summary>
    public static (char Boot, char Dados)? Existente(Disco pendrive)
    {
        var letras = pendrive.Particoes.Where(p => p.Letra != null).Select(p => p.Letra!.Value).ToList();
        char boot = letras.FirstOrDefault(l => File.Exists($@"{l}:\sources\boot.wim"));
        char dados = letras.FirstOrDefault(l => Directory.Exists($@"{l}:\MontaWin\Imagens"));
        return boot != '\0' && dados != '\0' ? (boot, dados) : null;
    }

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
        char boot, dados;
        if (g.Manter is (char b, char d)) (boot, dados) = (b, d);
        else
        {
            var letras = Comandos.LetrasLivres(2);
            if (letras.Length < 2) throw new Exception("Não há letras de unidade livres.");
            (boot, dados) = (letras[0], letras[1]);
        }
        char iso = g.Iso.Letra;
        var trabalho = Path.Combine(Path.GetTempPath(), "MontaWin");
        var mount = Path.Combine(trabalho, "mount");
        if (Directory.Exists(trabalho))
        {
            Comandos.Executar("dism", $"/Unmount-Image /MountDir:\"{mount}\" /Discard"); // sobra de uma gravação interrompida
            Directory.Delete(trabalho, recursive: true);
        }
        Directory.CreateDirectory(mount);

        if (g.Manter == null)
        {
            progresso("Apagando e particionando o pendrive", 0);
            var script = Path.Combine(trabalho, "diskpart.txt");
            File.WriteAllText(script, ScriptParticionar(g.Pendrive.Numero, boot, dados));
            Exigir(Comandos.Executar("diskpart", $"/s \"{script}\""), "O diskpart não conseguiu preparar o pendrive.");
            Exigir(Comandos.Executar($@"{iso}:\boot\bootsect.exe", $"/nt60 {boot}: /mbr /force"), "O bootsect não conseguiu tornar o pendrive bootável.");
        }

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
        var leiaMe = $@"{dados}:\MontaWin\Drivers\LEIA-ME.txt";
        Directory.CreateDirectory(Path.GetDirectoryName(leiaMe)!);
        if (!File.Exists(leiaMe)) File.WriteAllText(leiaMe, LeiaMeDrivers);
        Directory.CreateDirectory($@"{dados}:\MontaWin\Instaladores");
        File.WriteAllText($@"{dados}:\MontaWin\tema.txt", g.Tema);
        var listaProgramas = $@"{dados}:\MontaWin\Programas.txt";
        if (!File.Exists(listaProgramas)) File.WriteAllText(listaProgramas, Programas.Padrao); // não sobrescreve a lista editada pelo técnico
        var destino = Path.Combine(pastaImagens, Path.GetFileNameWithoutExtension(g.Iso.Iso) + ".wim");
        if (File.Exists(destino)) File.Delete(destino); // mesma ISO de novo: substitui em vez de duplicar as edições
        // Com atualizações, a imagem é montada no disco do PC (bem mais rápido que no pendrive) e copiada no fim.
        var imagem = g.Atualizacoes != null ? Path.Combine(trabalho, "imagem.wim") : destino;
        double fatia = (g.Atualizacoes != null ? 45.0 : 85.0) / g.Indices.Length;
        for (int k = 0; k < g.Indices.Length; k++)
        {
            double inicio = 15 + k * fatia;
            var nome = g.Iso.Edicoes.First(e => e.Indice == g.Indices[k]).Nome;
            progresso($"Copiando {nome} ({k + 1} de {g.Indices.Length})", inicio);
            Exigir(Comandos.Executar("dism",
                $"/Export-Image /SourceImageFile:\"{g.Iso.Install}\" /SourceIndex:{g.Indices[k]} /DestinationImageFile:\"{imagem}\" /Compress:{(g.Rapida ? "fast" : "max")}",
                p => progresso(null, inicio + p * fatia / 100)), $"O DISM não conseguiu copiar {nome}.");
        }
        if (g.Atualizacoes != null)
        {
            for (int k = 0; k < g.Indices.Length; k++)
            {
                double inicio = 60 + k * 30.0 / g.Indices.Length;
                var nome = g.Iso.Edicoes.First(e => e.Indice == g.Indices[k]).Nome;
                progresso($"Atualizando {nome} ({k + 1} de {g.Indices.Length})", inicio);
                Exigir(Comandos.Executar("dism", $"/Mount-Image /ImageFile:\"{imagem}\" /Index:{k + 1} /MountDir:\"{mount}\""), $"O DISM não conseguiu abrir {nome}.");
                int codigo = Comandos.Executar("dism", $"/Image:\"{mount}\" /Add-Package /PackagePath:\"{g.Atualizacoes}\"",
                    p => progresso(null, inicio + p * 30.0 / g.Indices.Length / 100));
                if (codigo != 0)
                {
                    Comandos.Executar("dism", $"/Unmount-Image /MountDir:\"{mount}\" /Discard");
                    Exigir(codigo, $"O DISM não conseguiu instalar as atualizações em {nome}. Confira se são da mesma versão do Windows da ISO (ex.: 24H2).");
                }
                Exigir(Comandos.Executar("dism", $"/Unmount-Image /MountDir:\"{mount}\" /Commit"), $"O DISM não conseguiu salvar {nome}.");
            }
            progresso("Copiando a imagem atualizada para o pendrive", 90);
            File.Copy(imagem, destino, overwrite: true);
        }

        Directory.Delete(trabalho, recursive: true);
        progresso("Pendrive pronto", 100);
    }
}
