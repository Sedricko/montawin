using MontaWin.Implantador;

namespace MontaWin.Montador;

public record IsoAberta(string Iso, char Letra, string Install, List<Edicao> Edicoes);

/// <param name="Rapida">Compressão rápida: imagem ~30% maior, mas a instalação descomprime mais depressa.</param>
/// <param name="Manter">Letras de um pendrive MontaWin existente: só adiciona a ISO e atualiza o painel, sem apagar.</param>
/// <param name="Tema">Visual do painel no boot: ondas, bloom ou pastel (gravado em MontaWin\tema.txt).</param>
/// <param name="Fundos">Imagens do fundo do tema (normal e desfocada) já desenhadas pelo Montador, para o painel gráfico.</param>
/// <param name="BaixarProgramas">Baixa os programas da lista para o pendrive: no primeiro login instalam sem internet.</param>
/// <param name="NetFx3">Ativa o .NET Framework 3.5 em cada edição (vem da própria ISO; muitos programas antigos pedem).</param>
/// <param name="Licenca">Chave do produto do técnico (opcional): o painel ativa o Windows com ela; null apaga a do pendrive.</param>
/// <param name="Programas">Lista de programas do técnico (texto do Programas.txt) e a pasta com os instaladores adicionados por arquivo.</param>
/// <param name="BootNovo">Boot UEFI assinado com o certificado novo (Windows UEFI CA 2023), tirado do próprio boot.wim da ISO.</param>
/// <param name="Esconder">Esconde do Windows a parte de dados no fim (perfis, licença, backups e imagens); o painel continua achando.</param>
/// <param name="Atualizacoes">Pasta com as atualizações .msu do Microsoft Update Catalog, injetadas em cada edição (null = nenhuma).</param>
public record Gravacao(IsoAberta Iso, int[] Indices, Disco Pendrive, string Implantador, bool Rapida, (char Boot, char Dados)? Manter,
    string? Atualizacoes = null, string Tema = "ondas", (string Fundo, string Desfocado)? Fundos = null, bool BaixarProgramas = false,
    (string Lista, string PastaInstaladores)? Programas = null, bool NetFx3 = true, string? Licenca = null, bool BootNovo = false, bool Esconder = false);

/// <summary>
/// Monta o pendrive: partição FAT32 de boot (UEFI e BIOS) com o WinPE da ISO abrindo o Implantador,
/// e partição NTFS com as imagens (sem o limite de 4 GB do FAT32) e os backups.
/// </summary>
public static class Pendrive
{
    public const int BootMb = 2048;

    /// <summary>
    /// O painel do pendrive vem dentro deste exe (um arquivo só): extrai para a pasta temporária e devolve o caminho.
    /// null quando não está embutido (compilação local), aí o Implantador.exe precisa estar ao lado.
    /// </summary>
    public static string? Painel()
    {
        using var embutido = typeof(Pendrive).Assembly.GetManifestResourceStream("Implantador.exe");
        if (embutido == null) return null;
        var destino = Path.Combine(Path.GetTempPath(), "AuroraWin", "Implantador.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        using (var arquivo = File.Create(destino)) embutido.CopyTo(arquivo);
        return destino;
    }

    public static string ScriptParticionar(int disco, char boot, char dados) => $"""
        select disk {disco}
        online disk noerr
        attributes disk clear readonly noerr
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

    /// <summary>
    /// Quem assinou o boot da ISO (efi\boot\bootx64.efi): a Microsoft troca o certificado do Secure Boot de 2011 pelo de 2023.
    /// PCs que já receberam a troca completa podem recusar mídia com o certificado antigo.
    /// </summary>
    public static string? CertificadoDoBoot(char letra)
    {
        var efi = $@"{letra}:\efi\boot\bootx64.efi";
        if (!File.Exists(efi)) return null;
        try
        {
#pragma warning disable SYSLIB0057 // só lê o emissor da assinatura, não carrega certificado
            var emissor = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(efi).Issuer;
#pragma warning restore SYSLIB0057
            return emissor.Contains("2023") ? "novo (2023)" : emissor.Contains("2011") ? "antigo (2011)" : null;
        }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }

    public static void FecharIso(string iso) =>
        Comandos.Executar("powershell", $"-NoProfile -Command \"Dismount-DiskImage -ImagePath '{Aspas(iso)}' -ErrorAction SilentlyContinue\"");

    /// <summary>
    /// Grava tudo. <paramref name="progresso"/> recebe a etapa (null = mesma etapa) e o percentual geral.
    /// Retorna os programas que não deu para baixar (ficam para o winget online no primeiro login).
    /// </summary>
    public static List<string> Gravar(Gravacao g, Action<string?, double> progresso)
    {
        // A lista é lida bem antes de gravar: se o pendrive foi trocado nesse meio tempo, outro disco USB
        // (o HD dos backups) pode ter ficado com o mesmo número
        if (!Discos.MesmoPendrive(g.Pendrive, Discos.Listar().FirstOrDefault(d => d.Numero == g.Pendrive.Numero)))
            throw new Exception("O pendrive mudou desde que foi escolhido (tirado ou trocado). Clique em Atualizar e escolha de novo: nada foi apagado.");
        char boot, dados;
        if (g.Manter is (char b, char d)) (boot, dados) = (b, d);
        else
        {
            var letras = Comandos.LetrasLivres(2, "MNOPQTUVYZ"); // longe de S/W/R, que o painel usa na instalação
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

        // Perfis e relatórios de um pendrive MontaWin sobrevivem à regravação: guardados aqui e devolvidos depois.
        var guardados = Path.Combine(Path.GetTempPath(), "MontaWin-guardados");
        if (g.Manter == null && Existente(g.Pendrive) is (_, char antigo))
            foreach (var pasta in PastasGuardadas)
                if (Directory.Exists($@"{antigo}:\MontaWin\{pasta}"))
                    Copiar($@"{antigo}:\MontaWin\{pasta}", Path.Combine(guardados, pasta));
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
            if (g.BootNovo) BootComCertificadoNovo(mount, boot);
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

        if (Directory.Exists(guardados))
        {
            foreach (var pasta in PastasGuardadas)
                if (Directory.Exists(Path.Combine(guardados, pasta))) Copiar(Path.Combine(guardados, pasta), $@"{dados}:\MontaWin\{pasta}");
            Directory.Delete(guardados, recursive: true);
        }
        File.WriteAllText($@"{dados}:\MontaWin\tema.txt", g.Tema);
        // Fontes do painel (o WinPE não tem a Segoe UI completa); sem elas o painel usa a fonte que o WinPE tiver
        Directory.CreateDirectory($@"{dados}:\MontaWin\Fontes");
        foreach (var fonte in new[] { "segoeui.ttf", "seguisb.ttf", "consola.ttf" })
            if (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), fonte) is var origem && File.Exists(origem))
                File.Copy(origem, $@"{dados}:\MontaWin\Fontes\{fonte}", overwrite: true);
        // Ícone do Aurora Win no canto do painel
        using (var icone = typeof(Pendrive).Assembly.GetManifestResourceStream("icone.png"))
        using (var arquivo = icone == null ? null : File.Create($@"{dados}:\MontaWin\icone.png"))
            if (icone != null) icone.CopyTo(arquivo!);
        var licenca = $@"{dados}:\MontaWin\licenca.txt";
        if (g.Licenca != null) File.WriteAllText(licenca, g.Licenca);
        else File.Delete(licenca);
        if (g.Fundos is (string fundo, string desfocado))
        {
            File.Copy(fundo, $@"{dados}:\MontaWin\fundo.png", overwrite: true);
            File.Copy(desfocado, $@"{dados}:\MontaWin\fundo-desfocado.png", overwrite: true);
        }
        if (g.Programas is (string lista, string instaladores)) SalvarProgramas(dados, lista, instaladores);
        else if (!File.Exists($@"{dados}:\MontaWin\Programas.txt")) SalvarProgramas(dados, Programas.Padrao, null);
        var destino = Path.Combine(pastaImagens, Path.GetFileNameWithoutExtension(g.Iso.Iso) + ".wim");
        if (File.Exists(destino)) File.Delete(destino); // mesma ISO de novo: substitui em vez de duplicar as edições
        File.Delete(destino + ".sha256");
        // Para mexer nas edições (.NET 3.5, atualizações), a imagem fica no disco do PC (bem mais rápido que no pendrive)
        // e no fim cada edição é exportada de novo para o pendrive: assim o que as atualizações substituíram não ocupa espaço.
        bool modificar = g.Atualizacoes != null || g.NetFx3;
        var imagem = modificar ? Path.Combine(trabalho, "imagem.wim") : destino;
        double fatia = (modificar ? 30.0 : 85.0) / g.Indices.Length;
        for (int k = 0; k < g.Indices.Length; k++)
        {
            double inicio = 15 + k * fatia;
            var nome = g.Iso.Edicoes.First(e => e.Indice == g.Indices[k]).Nome;
            progresso($"Copiando {nome} ({k + 1} de {g.Indices.Length})", inicio);
            Exigir(Comandos.Executar("dism",
                $"/Export-Image /SourceImageFile:\"{g.Iso.Install}\" /SourceIndex:{g.Indices[k]} /DestinationImageFile:\"{imagem}\" /Compress:{(g.Rapida || modificar ? "fast" : "max")}",
                p => progresso(null, inicio + p * fatia / 100)), $"O DISM não conseguiu copiar {nome}.");
        }
        if (modificar)
        {
            for (int k = 0; k < g.Indices.Length; k++)
            {
                double inicio = 45 + k * 40.0 / g.Indices.Length;
                var nome = g.Iso.Edicoes.First(e => e.Indice == g.Indices[k]).Nome;
                progresso($"Preparando {nome} ({k + 1} de {g.Indices.Length})", inicio);
                Exigir(Comandos.Executar("dism", $"/Mount-Image /ImageFile:\"{imagem}\" /Index:{k + 1} /MountDir:\"{mount}\""), $"O DISM não conseguiu abrir {nome}.");
                void Passo(string etapa, string argumentos, string erro)
                {
                    progresso($"{etapa}: {nome} ({k + 1} de {g.Indices.Length})", inicio);
                    int codigo = Comandos.Executar("dism", $"/Image:\"{mount}\" {argumentos}");
                    if (codigo == 0) return;
                    Comandos.Executar("dism", $"/Unmount-Image /MountDir:\"{mount}\" /Discard");
                    Exigir(codigo, erro);
                }
                // O .NET 3.5 vem antes das atualizações, como a Microsoft manda (depois, a atualização cobre ele também).
                if (g.NetFx3)
                    Passo("Ativando o .NET Framework 3.5", $"/Enable-Feature /FeatureName:NetFx3 /All /LimitAccess /Source:\"{iso}:\\sources\\sxs\"",
                        $"O DISM não conseguiu ativar o .NET 3.5 em {nome}. Desmarque \".NET Framework 3.5\" para criar sem ele.");
                if (g.Atualizacoes != null)
                {
                    Passo("Instalando as atualizações", $"/Add-Package /PackagePath:\"{g.Atualizacoes}\"",
                        $"O DISM não conseguiu instalar as atualizações em {nome}. Confira se são da mesma versão do Windows da ISO (ex.: 24H2).");
                    Passo("Limpando o que as atualizações substituíram", "/Cleanup-Image /StartComponentCleanup /ResetBase",
                        $"O DISM não conseguiu limpar {nome}.");
                }
                Exigir(Comandos.Executar("dism", $"/Unmount-Image /MountDir:\"{mount}\" /Commit"), $"O DISM não conseguiu salvar {nome}.");
            }
            for (int k = 0; k < g.Indices.Length; k++)
            {
                double inicio = 85 + k * 7.0 / g.Indices.Length;
                var nome = g.Iso.Edicoes.First(e => e.Indice == g.Indices[k]).Nome;
                progresso($"Gravando {nome} no pendrive ({k + 1} de {g.Indices.Length})", inicio);
                Exigir(Comandos.Executar("dism",
                    $"/Export-Image /SourceImageFile:\"{imagem}\" /SourceIndex:{k + 1} /DestinationImageFile:\"{destino}\" /Compress:{(g.Rapida ? "fast" : "max")}",
                    p => progresso(null, inicio + p * 7.0 / g.Indices.Length / 100)), $"O DISM não conseguiu gravar {nome} no pendrive.");
            }
        }

        // Impressão digital da imagem: o painel confere antes de formatar e não instala uma cópia corrompida.
        progresso("Conferindo a imagem gravada no pendrive", 93);
        File.Delete(destino + ".conferido");
        using (var f = File.OpenRead(destino))
            File.WriteAllText(destino + ".sha256", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(f)));
        Directory.Delete(trabalho, recursive: true);
        var falhas = g.BaixarProgramas ? BaixarProgramas(dados, progresso) : [];
        if (g.Esconder)
        {
            progresso("Escondendo os dados do pendrive no Windows", 99);
            Discos.Diskpart(Discos.ScriptEsconderDados(g.Pendrive.Numero, 2, dados));
        }
        progresso("Pendrive pronto", 100);
        return falhas;
    }

    /// <summary>
    /// Troca o boot UEFI do pendrive pelo assinado com o certificado de 2023, como o script da Microsoft
    /// (Make2023BootableMedia): os arquivos "_EX" vêm dentro do próprio WinPE das ISOs atualizadas.
    /// PCs que ainda não receberam o certificado novo não dão boot com ele: por isso o padrão é o compatível (2011).
    /// </summary>
    static void BootComCertificadoNovo(string mount, char boot)
    {
        var ex = Path.Combine(mount, "Windows", "Boot", "EFI_EX");
        if (!File.Exists(Path.Combine(ex, "bootmgfw_EX.efi")))
            throw new Exception("Esta ISO não traz o boot com o certificado novo (2023). Use uma ISO mais nova da Microsoft, " +
                                "ou volte o certificado do boot para \"Compatível (2011)\" na engrenagem.");
        File.Copy(Path.Combine(ex, "bootmgfw_EX.efi"), $@"{boot}:\efi\boot\bootx64.efi", overwrite: true);
        if (File.Exists(Path.Combine(ex, "bootmgr_EX.efi"))) File.Copy(Path.Combine(ex, "bootmgr_EX.efi"), $@"{boot}:\bootmgr.efi", overwrite: true);
        var fontes = Path.Combine(mount, "Windows", "Boot", "Fonts_EX");
        if (Directory.Exists(fontes))
            foreach (var fonte in Directory.GetFiles(fontes, "*_EX.ttf"))
                File.Copy(fonte, $@"{boot}:\efi\microsoft\boot\fonts\{Path.GetFileName(fonte).Replace("_EX", "")}", overwrite: true);
    }

    /// <summary>O que volta para o pendrive quando ele é apagado e gravado de novo (pequeno: os backups não cabem aqui).</summary>
    public static readonly string[] PastasGuardadas = ["Perfis", "Relatorios"];

    static void Copiar(string origem, string destino)
    {
        foreach (var arquivo in Directory.GetFiles(origem, "*", SearchOption.AllDirectories))
        {
            var alvo = Path.Combine(destino, Path.GetRelativePath(origem, arquivo));
            Directory.CreateDirectory(Path.GetDirectoryName(alvo)!);
            File.Copy(arquivo, alvo, overwrite: true);
        }
    }

    /// <summary>
    /// Chrome direto do Google (o instalador corporativo .msi): pelo winget o download falha toda hora, porque o Google
    /// troca o arquivo e o hash do manifesto não bate. O manifesto mínimo diz ao painel que é msi (instala com /qn).
    /// </summary>
    static bool BaixarChrome(string pasta)
    {
        try
        {
            Directory.CreateDirectory(pasta);
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using (var origem = http.GetStreamAsync("https://dl.google.com/dl/chrome/install/googlechromestandaloneenterprise64.msi").Result)
            using (var arquivo = File.Create(Path.Combine(pasta, "GoogleChromeEnterprise64.msi")))
                origem.CopyTo(arquivo);
            File.WriteAllText(Path.Combine(pasta, "Google.Chrome.yaml"), "PackageIdentifier: Google.Chrome\nInstallerType: msi\nScope: machine\n");
            return true;
        }
        catch (Exception)
        {
            if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true); // sem pasta, o painel usa o winget online
            return false;
        }
    }

    /// <summary>Aviso antes de apagar um pendrive MontaWin: o que fica (perfis) e o que se perde (backups).</summary>
    public static string? AvisoAoApagar(char dados)
    {
        int perfis = Directory.Exists($@"{dados}:\MontaWin\Perfis") ? Directory.GetFiles($@"{dados}:\MontaWin\Perfis", "*.txt").Length : 0;
        int backups = Directory.Exists($@"{dados}:\MontaWin\Backups") ? Directory.GetDirectories($@"{dados}:\MontaWin\Backups").Length : 0;
        if (perfis + backups == 0) return null;
        return (perfis > 0 ? $"Os {perfis} perfis ficam guardados e voltam para o pendrive." : "") +
               (backups > 0 ? $"\nOs backups de {backups} PC(s) (drivers e arquivos) serão APAGADOS. Copie a pasta MontaWin\\Backups antes, se precisar deles." : "");
    }

    /// <summary>Grava a lista no pendrive e copia os instaladores adicionados por arquivo (só os que mudaram).</summary>
    public static void SalvarProgramas(char dados, string lista, string? pastaInstaladores)
    {
        var destino = $@"{dados}:\MontaWin\Instaladores";
        Directory.CreateDirectory(destino);
        File.WriteAllText($@"{dados}:\MontaWin\Programas.txt", lista);
        if (pastaInstaladores == null || !Directory.Exists(pastaInstaladores)) return;
        foreach (var arquivo in Directory.GetFiles(pastaInstaladores))
        {
            var copia = Path.Combine(destino, Path.GetFileName(arquivo));
            if (!File.Exists(copia) || new FileInfo(copia).Length != new FileInfo(arquivo).Length) File.Copy(arquivo, copia, overwrite: true);
        }
    }

    /// <summary>
    /// "winget download" de cada programa da lista (menos os da Microsoft Store, que só instalam online) para
    /// MontaWin\Instaladores\winget\<id>: instalador + manifesto com os parâmetros silenciosos.
    /// </summary>
    public static List<string> BaixarProgramas(char dados, Action<string?, double> progresso)
    {
        var ids = Programas.Ler(File.ReadAllText($@"{dados}:\MontaWin\Programas.txt")).SelectMany(p => p.Ids).Where(id => !Programas.SoInternet(id)).Distinct().ToList();
        var falhas = new List<string>();
        for (int i = 0; i < ids.Count; i++)
        {
            progresso($"Baixando programas para o pendrive: {ids[i]} ({i + 1} de {ids.Count})", 92 + i * 8.0 / ids.Count);
            var pasta = $@"{dados}:\MontaWin\Instaladores\winget\{ids[i]}";
            if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true); // sempre a versão mais nova
            if (ids[i].Equals("Google.Chrome", StringComparison.OrdinalIgnoreCase))
            {
                if (!BaixarChrome(pasta)) falhas.Add(ids[i]);
                continue;
            }
            if (Comandos.Executar("winget", $"download --id {ids[i]} -e --source winget --download-directory \"{pasta}\" --accept-package-agreements --accept-source-agreements") != 0)
            {
                falhas.Add(ids[i]);
                if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true); // sem pasta, o painel usa o winget online
            }
        }
        return falhas;
    }
}
