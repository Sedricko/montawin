using System.Reflection;
using System.Runtime.InteropServices;
using MontaWin.Implantador;
using MontaWin.Montador;
using Xunit;

public class TelasTests
{
    // Fora do Windows (Linux/Mac), o GDI+ do painel é o libgdiplus.
    static TelasTests()
    {
        if (!OperatingSystem.IsWindows())
            NativeLibrary.SetDllImportResolver(typeof(Tui).Assembly, (nome, _, _) =>
                nome == "gdiplus.dll" && NativeLibrary.TryLoad("libgdiplus.so", out var h) ? h : 0);
    }

    static readonly string[] NomesProgramas = ["Google Chrome", "WinRAR", "Visual C++ 2008 a 2022 (32 e 64 bits)", "Notepad++", "Lightshot", "pCloud",
        "Spotify", "Microsoft 365 (Word, Excel, Outlook)", "Foxit PDF Editor Pro"];

    /// <summary>Uma tela de cada tipo, como o painel mostra durante uma instalação.</summary>
    public static IEnumerable<(string Nome, Tela Tela)> Exemplos()
    {
        yield return ("0-reconhecido", new Tela
        {
            Tipo = TipoTela.Lista, Titulo = "PC Escritório reconhecido", Selecionado = 0, Rodape = "Setas: navegar    Enter: escolher",
            Info = ["Dell Inc. OptiPlex 7090 · 16 GB RAM · UEFI · série 5CG1234XYZ",
                "Windows 11 Pro · Windows 200 GB + Dados · conta joao · Wi-Fi Casa", "O perfil já sabe tudo deste PC: você só escolhe o disco e confirma."],
            Itens = [new Item("Formatar com o perfil \"PC Escritório\""), new Item("Ir para o menu (outras opções)")],
        });
        yield return ("1-menu", new Tela
        {
            Tipo = TipoTela.Lista, Titulo = "O que vamos fazer?", Selecionado = 0, Rodape = "Setas: navegar    Enter: escolher",
            Info = ["Nenhum disco escolhido para os backups", "Backup de drivers: não feito", "Perfil deste PC: PC Escritório", @"Pendrive: D:\MontaWin"],
            Itens = new[] { "Formatar e instalar o Windows", "Escolher disco (para os backups)", "Backup de drivers", "Backup de arquivos",
                "Restaurar backup de arquivos", "Perfis", "Capturar imagem de um Windows", "Prompt de comando", "Reiniciar", "Desligar" }
                .Select(x => new Item(x)).ToList(),
        });
        yield return ("2-disco", new Tela
        {
            Tipo = TipoTela.Lista, Titulo = "Em qual disco instalar?", Selecionado = 0, Rodape = "Setas: navegar    Enter: escolher    Esc: voltar",
            Itens = [
                new Item($"Disco 0  {Formatar.Ajustar("Samsung SSD 980 1TB", 26)} {"954 GB",8}  {Formatar.Ajustar("Boa", 8)}  NVMe  GPT  Windows em C:"),
                new Item($"Disco 1  {Formatar.Ajustar("ST1000DM010-2EP102", 26)} {"1000 GB",8}  {Formatar.Ajustar("Atenção", 8)}  SATA  MBR"),
                new Item("← Voltar", TipoItem.Voltar)],
        });
        yield return ("3-programas", new Tela
        {
            Tipo = TipoTela.Lista, Titulo = "Quais programas instalar?", Selecionado = 3,
            Rodape = "Enter ou Espaço: marcar/desmarcar    Tab: ir para Continuar    Esc: voltar",
            Info = ["Instalam sozinhos no Windows novo: antes da tela de login, ou logo depois dela.", "Os baixados no pendrive instalam sem internet; os outros precisam de internet."],
            Itens = [new Item("Marcar todos", TipoItem.MarcarTodos), .. NomesProgramas.Select((p, i) => new Item(p, TipoItem.Caixa, i is 0 or 2 or 3)),
                new Item("Continuar →   (3 marcados)", TipoItem.Continuar), new Item("← Voltar", TipoItem.Voltar)],
        });
        yield return ("4-usuario", new Tela
        {
            Tipo = TipoTela.Pergunta, Titulo = "Nome do usuário", Rotulo = "Nome do usuário", Campo = "Joao", Rodape = "Enter: continuar    Esc: voltar",
            Info = ["Nome da conta local do Windows (ex.: Joao).", "Até 20 letras, sem \\ / [ ] : ; | = , + * ? < > \""],
            Itens = [new Item("Continuar →", TipoItem.Continuar), new Item("← Voltar", TipoItem.Voltar)],
        });
        yield return ("5-resumo", new Tela
        {
            Tipo = TipoTela.Mensagem, Titulo = "Confira antes de começar", Rodape = "Setas: trocar de botão    Enter: escolher    Esc: voltar", Selecionado = 2,
            Info = ["Windows: Windows 11 Pro  ·  UEFI (GPT)", "Disco: Disco 0 · Samsung SSD 980 1TB · 954 GB · Boa", "Partições: Windows 200 GB + Dados (o resto) + recuperação",
                "Conta: joao (com senha) · telas iniciais da Microsoft puladas", "Programas: Google Chrome, Visual C++, Notepad++",
                "Ajustes: otimizado (SSD: inicialização sem atraso), sem OneDrive", "Drivers: o backup volta no Windows novo"],
            Itens = [new Item("← Voltar e corrigir", TipoItem.Voltar), new Item("Cancelar", TipoItem.Voltar), new Item("Formatar e instalar →", TipoItem.Continuar)],
        });
        yield return ("6-confirmar", new Tela
        {
            Tipo = TipoTela.Pergunta, Titulo = "Isso vai apagar tudo", Alerta = Alerta.Perigo, Campo = "SI", Rodape = "Enter: continuar    Esc: voltar",
            Rotulo = "Para confirmar, digite SIM e aperte Enter",
            Info = ["TODOS os arquivos, programas e partições do Disco 0 · Samsung SSD 980 1TB · 954 GB serão apagados.", "Não tem como desfazer."],
            Itens = [new Item("Formatar e instalar", TipoItem.Continuar), new Item("← Voltar", TipoItem.Voltar)],
        });
        yield return ("7-instalando", new Tela
        {
            Tipo = TipoTela.Progresso, Titulo = "Instalando o Windows", EtapaAtual = 2, Detalhe = "Otimizando", Percentual = 71,
            Etapas = ["Preparando o disco", "Copiando o Windows", "Otimizando", "Instalando os drivers", "Restaurando os arquivos", "Inicialização e primeiro login", "Recuperação do Windows"],
            Registro = ["Apagando o disco 0 e criando as partições (GPT/UEFI)", "Partições prontas", "Copiando os arquivos do Windows 11 Pro para o disco",
                "Windows 11 Pro copiado em 3 min 42 s", "Conta local joao preparada; as telas iniciais da Microsoft serão puladas",
                "Removido: Notícias", "Removido: Xbox", "Removido: Paciência", "Removido: Clipchamp"],
            Inicio = DateTime.Now.AddMinutes(-4.5), Restante = TimeSpan.FromMinutes(1.8), Rodape = "Aguarde, não desligue o computador",
        });
        yield return ("8-erro", new Tela
        {
            Tipo = TipoTela.Mensagem, Titulo = "Saúde do disco 1: Atenção", Alerta = Alerta.Perigo, Rodape = "Enter: continuar",
            Info = ["SMART: o disco avisa que pode falhar em breve (setores realocados).", "O disco funciona, mas vale avisar o cliente e fazer o backup."],
            Itens = [new Item("Continuar", TipoItem.Continuar)],
        });
        yield return ("9-prompt", new Tela
        {
            Tipo = TipoTela.Terminal, Titulo = "Prompt de comando", Campo = "list volume",
            Itens = [new Item("← Voltar ao painel", TipoItem.Voltar), new Item("Parar comando", TipoItem.Continuar),
                new Item("Testes", TipoItem.Aba, true), new Item("Reparo", TipoItem.Aba), new Item("Backup e acesso", TipoItem.Aba), new Item("Geral", TipoItem.Aba),
                .. new[] { "Resumo do PC", "Discos", "Partições", "Partições sem letra", "Verificar C: (só lê)", "Rede", "Testar internet", "BitLocker" }
                    .Select((n, i) => new Item($"F{i + 1}  {n}"))],
            Rodape = "Enter: executar    Tab: trocar de aba    F1–F8: botões    Botões com … só preenchem: confira e aperte Enter    Esc: voltar",
            Registro = ["Prompt de comando do MontaWin. Digite exit ou aperte Esc para voltar ao painel.", "",
                "X:\\Windows\\System32>diskpart", "", "Microsoft DiskPart versão 10.0.26100.1", "", "No computador: MINWINPC", "",
                "DISKPART> list disk", "",
                "  Disco ###  Status         Tam.     Livre    Din  GPT",
                "  ---------  -------------  -------  -------  ---  ---",
                "  Disco 0    Online          476 GB      0 B        *",
                "  Disco 1    Online          238 GB  1024 KB        *", "",
                "DISKPART> "],
        });
        yield return ("9b-prompt-backup", new Tela
        {
            Tipo = TipoTela.Terminal, Titulo = "Prompt de comando", Campo = "robocopy \"C:\\Sistema Loja\" \"D:\\MontaWin\\Backups\\Manual\\Sistema Loja\" /E /B /R:0 /W:0 /XJ",
            Itens = [new Item("← Voltar ao painel", TipoItem.Voltar), new Item("Parar comando", TipoItem.Continuar),
                new Item("Testes", TipoItem.Aba), new Item("Reparo", TipoItem.Aba), new Item("Backup e acesso", TipoItem.Aba, true), new Item("Geral", TipoItem.Aba),
                .. new[] { "Pastas na raiz de C:", "Procurar e-mails do Outlook", "Procurar arquivo por nome …", "Copiar uma pasta (até protegida) …", "Copiar um disco inteiro …", "Dar letra a partição sem letra …", "Exportar drivers …", "Navegar nas pastas" }
                    .Select((n, i) => new Item($"F{i + 1}  {n}"))],
            Rodape = "Enter: executar    Tab: trocar de aba    F1–F8: botões    Botões com … só preenchem: confira e aperte Enter    Esc: voltar",
            Registro = ["Prompt de comando do MontaWin. Digite exit ou aperte Esc para voltar ao painel.", "",
                "X:\\Windows\\System32>diskpart", "", "Microsoft DiskPart versão 10.0.26100.1", "", "No computador: MINWINPC", "",
                "DISKPART> list disk", "",
                "  Disco ###  Status         Tam.     Livre    Din  GPT",
                "  ---------  -------------  -------  -------  ---  ---",
                "  Disco 0    Online          476 GB      0 B        *",
                "  Disco 1    Online          238 GB  1024 KB        *", "",
                "DISKPART> "],
        });
    }

    [Fact]
    public void ConferenciaDoPrimeiroLoginEntraNosScripts()
    {
        var ps = Unattend.GerarConferencia([("Google Chrome", ["Google.Chrome"]), ("Sistema d'Loja", ["loja.exe"])]);
        Assert.Contains("[pscustomobject]@{ Nome = 'Google Chrome'; Marcas = @('Google.Chrome') }", ps);
        Assert.Contains("'Sistema d''Loja'", ps); // aspas simples escapadas no PowerShell
        Assert.Contains("Get-PnpDevice", ps);
        Assert.Contains(@"Join-Path $env:SystemDrive 'MontaWin\Drivers'", ps); // segunda tentativa dos drivers
        if (OperatingSystem.IsWindows()) // no CI: o PowerShell precisa conseguir ler o script (erro de sintaxe falha aqui)
        {
            var arquivo = Path.Combine(Path.GetTempPath(), "montawin-conferencia.ps1");
            File.WriteAllText(arquivo, ps, new System.Text.UTF8Encoding(true));
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell",
                $"-NoProfile -Command \"[ScriptBlock]::Create((Get-Content -Raw '{arquivo}')) | Out-Null\"") { RedirectStandardError = true })!;
            var erro = p.StandardError.ReadToEnd();
            p.WaitForExit();
            Assert.True(p.ExitCode == 0 && erro.Length == 0, erro);
        }
        Assert.Contains(Unattend.ChamadaConferencia, Unattend.GerarScriptProgramas(["Google.Chrome"], conferencia: true));
        // UTF-8: sem o chcp, o cmd não acha instaladores com acento no nome ("Pacotes Redistribuíveis do Visual C++")
        Assert.StartsWith("@echo off\r\nrem Os scripts", Unattend.GerarScriptProgramas([]));
        Assert.Contains("chcp 65001 >nul", Unattend.GerarScriptProgramas([]));
        Assert.Contains("chcp 65001 >nul", Unattend.GerarScriptAntesDoLogin(null, null, true));
        Assert.Contains(Unattend.ChamadaConferencia, Unattend.GerarScriptPrimeiroLogin([], temProgramas: false, conferencia: true));
        Assert.DoesNotContain(Unattend.ChamadaConferencia, Unattend.GerarScriptPrimeiroLogin([], temProgramas: true, conferencia: true));
        Assert.Contains(@"-File %SystemDrive%\Windows\Setup\Scripts\MontaWin-conferencia.ps1", Unattend.ChamadaConferencia);
    }

    [Fact]
    public void SoApagaSeAindaForOMesmoPendrive()
    {
        var pendrive = new Disco(2, 64_000_000_000, "Kingston DataTraveler", true, false, []);
        Assert.True(Discos.MesmoPendrive(pendrive, pendrive with { Particoes = [] }));
        Assert.False(Discos.MesmoPendrive(pendrive, new Disco(2, 2_000_000_000_000, "WD Elements", true, true, []))); // HD dos backups
        Assert.False(Discos.MesmoPendrive(pendrive, pendrive with { Usb = false }));
        Assert.False(Discos.MesmoPendrive(pendrive, null)); // pendrive tirado
    }

    [Fact]
    public void DadosDoPendriveEscondidosComPin()
    {
        var boot = new Particao(1, 1 << 20, 2L << 30, TipoParticao.Dados, "", true) { Letra = 'M' };
        var escondida = new Particao(2, 2L << 30, 200L << 30, TipoParticao.Fabricante, "", false); // tipo 0x27, sem letra
        var pendrive = new Disco(3, 256L << 30, "Kingston", true, false, [boot, escondida]);
        Assert.Equal(2, Discos.DadosEscondidos(pendrive)?.Numero);
        Assert.Null(Discos.DadosEscondidos(pendrive with { Usb = false })); // disco interno nunca
        Assert.Null(Discos.DadosEscondidos(pendrive with { Particoes = [boot, escondida with { Letra = 'N' }] })); // já visível
        Assert.Contains("set id=27 override", Discos.ScriptEsconderDados(3, 2, 'N'));
        Assert.Contains("remove letter=N", Discos.ScriptEsconderDados(3, 2, 'N'));
        Assert.DoesNotContain("set id", Discos.ScriptMostrarDados(3, 2, 'P', mostrar: false)); // só a letra: continua escondida no Windows
        Assert.Contains("set id=07 override", Discos.ScriptMostrarDados(3, 2, 'P', mostrar: true));
    }

    [Fact]
    public void ScriptsAguentamNomesDificeis()
    {
        Assert.Contains("name=\"Casa%%20\"", Unattend.GerarScriptAntesDoLogin("Casa%20", null, false));
        Assert.True(Programas.SoInternet("Microsoft.Office") && Programas.SoInternet("9NCBCSZSJRSB") && !Programas.SoInternet("Google.Chrome"));
    }

    [Fact]
    public void AssistenteSugereSemDecidir()
    {
        Assert.True(Sugestoes.CpuAntiga("Intel(R) Core(TM) i5-7200U CPU @ 2.50GHz"));
        Assert.False(Sugestoes.CpuAntiga("Intel(R) Core(TM) i7-10700 CPU @ 2.90GHz")); // 10ª geração: 5 dígitos
        Assert.False(Sugestoes.CpuAntiga("Intel(R) Core(TM) i5-8250U CPU"));
        Assert.True(Sugestoes.CpuAntiga("AMD Ryzen 5 1600 Six-Core Processor"));
        Assert.False(Sugestoes.CpuAntiga("AMD Ryzen 5 3600 6-Core Processor"));
        var avisos = Sugestoes.Instalacao("Windows 11 Pro", uefi: false, secureBoot: null, ramGb: 2, ssd: false, discoBytes: 32_000_000_000, cpu: "Intel(R) Core(TM) i3-6100");
        Assert.Equal(5, avisos.Count); // RAM, BIOS, processador, HD, disco pequeno
        Assert.Empty(Sugestoes.Instalacao("Windows 11 Pro", true, true, 16, true, 512_000_000_000, "Intel(R) Core(TM) i7-12700"));
        Assert.Contains("pendrive", Sugestoes.Explicar("Erro: 0x8007000d dados inválidos")!);
        Assert.Null(Sugestoes.Explicar("qualquer outra coisa"));
    }

    [Fact]
    public void SiteDoProgramaVemDoWingetShow()
    {
        var saida = "Encontrado Google Chrome [Google.Chrome]\r\nVersão: 130.0\r\nFornecedor: Google LLC\r\nUrl do Fornecedor: https://www.google.com/\r\n" +
                    "Página Inicial: https://www.google.com/chrome\r\nLicença: https://github.com/x\r\n";
        Assert.Equal("www.google.com", Programas.SiteNaSaida(saida));
        Assert.Equal("notepad-plus-plus.org", Programas.SiteNaSaida("Url: https://github.com/notepad-plus-plus\nHome: https://notepad-plus-plus.org/"));
        Assert.Null(Programas.SiteNaSaida("Nenhum pacote encontrado."));
    }

    [Fact]
    public void RedesVisiveisVemDoNetsh()
    {
        var saida = "Nome da interface : Wi-Fi\r\nHá 2 redes visíveis no momento.\r\n\r\nSSID 1 : Casa 5G\r\n    Tipo de rede : Infraestrutura\r\n"
            + "SSID 2 : \r\nSSID 3 : Loja\r\nSSID 4 : Casa 5G\r\n";
        Assert.Equal(["Casa 5G", "Loja"], Sistema.RedesNaSaida(saida));
    }

    [Fact]
    public void PainelGraficoDesenhaTodasAsTelasNosTresTemas()
    {
        if (!OperatingSystem.IsWindows() && !NativeLibrary.TryLoad("libgdiplus.so", out _)) return; // sem GDI+ aqui
        var pasta = Environment.GetEnvironmentVariable("MONTAWIN_PREVIA") ?? Path.Combine(Path.GetTempPath(), "montawin-previa");
        Directory.CreateDirectory(pasta);
        Tui.InfoSistema = "Dell Inc. OptiPlex 7090 · 16 GB RAM · UEFI";
        foreach (var tema in Tui.Temas)
            foreach (var (nome, tela) in Exemplos())
            {
                var arquivo = Path.Combine(pasta, $"painel-{tema}-{nome}.png");
                // Fundos gerados pelo Montador (MONTAWIN_FUNDOS\<tema>\fundo.png); sem eles, o painel usa o degradê do tema.
                var fundos = Environment.GetEnvironmentVariable("MONTAWIN_FUNDOS") is string f ? Path.Combine(f, tema) : null;
                TelaGrafica.SalvarImagem(tema, fundos, tela, 1280, 720, arquivo);
                Assert.True(new FileInfo(arquivo).Length > 10_000, arquivo);
            }
    }

    [Fact]
    public void PercentualTotalSegueOsPesosDasEtapas()
    {
        Assert.Equal(0, Tui.PercentualTotal([2, 55, 8], -1, 0));
        Assert.Equal(2 / 65.0 * 100 + 55 / 65.0 * 50, Tui.PercentualTotal([2, 55, 8], 1, 50), 3);
        Assert.Equal(100, Tui.PercentualTotal([2, 55, 8], 3, 0));
        Assert.Equal(40, Tui.PercentualTotal([], 0, 40));
        Assert.Null(Tui.Restante(TimeSpan.FromSeconds(10), 50)); // cedo demais para estimar
        Assert.Equal(TimeSpan.FromMinutes(3), Tui.Restante(TimeSpan.FromMinutes(1), 25));
    }

    [Fact]
    public void ProgramaBaixadoInstalaSemInternetComOsParametrosDoManifesto()
    {
        Assert.Equal(@"""C:\i\Google.Chrome\setup.exe"" /silent /install",
            Programas.ComandoSilencioso("InstallerType: exe\nInstallerSwitches:\n  Silent: /silent /install\n", @"C:\i\Google.Chrome\setup.exe"));
        Assert.Equal(@"msiexec /i ""C:\i\x.msi"" /qn /norestart", Programas.ComandoSilencioso("Installers:\n- InstallerType: wix\n", @"C:\i\x.msi"));
        Assert.Equal(@"""C:\i\npp.exe"" /S", Programas.ComandoSilencioso("InstallerType: nullsoft\n", @"C:\i\npp.exe"));
        Assert.EndsWith("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /MERGETASKS=!runcode",
            Programas.ComandoSilencioso("InstallerType: inno\nInstallerSwitches:\n  Custom: /MERGETASKS=!runcode\n", "a.exe"));
        Assert.Null(Programas.ComandoSilencioso("InstallerType: zip\nNestedInstallerType: exe\n", "a.zip")); // fica para o winget online
        Assert.Null(Programas.ComandoSilencioso("InstallerType: exe\n", "a.exe")); // exe sem parâmetro silencioso conhecido
    }

    [Fact]
    public void ScriptDeProgramasTentaPeloWingetOQueFalharOffline()
    {
        var script = Unattend.GerarScriptProgramas(["Notepad++.Notepad++"], null,
            [("Notepad++.Notepad++", @"""C:\x\npp.exe"" /S", false), ("Spotify.Spotify", @"""C:\x\spotify.exe"" /silent", true)]);
        int offline = script.IndexOf("if not exist \"%FEITOS%\\Notepad++.Notepad++\" \"C:\\x\\npp.exe\" /S && type nul>\"%FEITOS%\\Notepad++.Notepad++\"");
        Assert.True(offline > 0);
        Assert.True(offline < script.IndexOf(":esperar")); // sem internet não espera o winget para instalar
        Assert.True(script.IndexOf("install --id Notepad++.Notepad++") > offline); // marcado como feito, o winget pula
        Assert.Contains("if \"%MODO%\"==\"usuario\" if not exist \"%FEITOS%\\Spotify.Spotify\" \"C:\\x\\spotify.exe\" /silent", script); // instalador só do usuário
    }

    [Fact]
    public void AppsRemovidosTemNomeQueOTecnicoReconhece()
    {
        Assert.Equal("Notícias", Otimizacao.NomeAmigavel("Microsoft.BingNews_4.55.62231.0_neutral_~_8wekyb3d8bbwe"));
        Assert.Equal("Xbox (sobreposição de jogos)", Otimizacao.NomeAmigavel("Microsoft.XboxGamingOverlay_7.0.0.0_x64__8wekyb3d8bbwe"));
        Assert.All(Otimizacao.AppsRemovidos, a => Assert.NotEqual(a, Otimizacao.NomeAmigavel(a + "_1.0.0.0_x64__8wekyb3d8bbwe")));
    }

    const string PesquisaCatalogo = """
        <tr id="62c21efa-416e-430c-84ee-bba3c8d583c2_R0"><td id="62c21efa-416e-430c-84ee-bba3c8d583c2_C1_R0">
        <a id='62c21efa-416e-430c-84ee-bba3c8d583c2_link' href= "javascript:void(0);" class="x">
            2026-10 Cumulative Update for Windows 11, version 25H2 for x64-based Systems (KB5134133) (26200.9725)
        </a></td><td id="62c21efa-416e-430c-84ee-bba3c8d583c2_C4_R0">
            10/26/2026
        </td></tr>
        <tr><td><a id='badf2b59-071c-43f8-9be6-61df51759b9d_link' href= "javascript:void(0);">
            2026-10 Safe OS Dynamic Update for Windows 11, version 25H2 for x64-based Systems (KB5127654)
        </a></td><td id="badf2b59-071c-43f8-9be6-61df51759b9d_C4_R2">
            10/13/2026
        </td></tr>
        <tr><td><a id='98c8f52b-7e9d-4fae-a8f8-7ad617da56e6_link' href= "javascript:void(0);">
            2026-10 Cumulative Update for .NET Framework 3.5 and 4.8.1 for Windows 11, version 25H2 for x64 (KB5127690)
        </a></td><td id="98c8f52b-7e9d-4fae-a8f8-7ad617da56e6_C4_R3">
            10/13/2026
        </td></tr>
        <tr><td><a id='522b53b9-7eea-415d-9a2d-d4cbd0a37abf_link' href= "javascript:void(0);">
            2026-11 Cumulative Update Preview for Windows 11, version 25H2 for x64-based Systems (KB5124010) (26200.9550)
        </a></td><td id="522b53b9-7eea-415d-9a2d-d4cbd0a37abf_C4_R4">
            11/22/2026
        </td></tr>
        """;

    [Fact]
    public void AtualizacoesEscolheACumulativaEoNetMaisNovosSemPreview()
    {
        Assert.Equal("25H2", Atualizacoes.Versao(26200));
        Assert.Equal("24H2", Atualizacoes.Versao(26100));
        Assert.Null(Atualizacoes.Versao(0));
        var escolhidas = Atualizacoes.Escolher(Atualizacoes.LerPesquisa(PesquisaCatalogo), 26200);
        Assert.Equal(2, escolhidas.Count);
        Assert.Contains("KB5134133", escolhidas[0].Titulo);
        Assert.Contains(".NET", escolhidas[1].Titulo);
        Assert.Equal(new DateTime(2026, 10, 26), escolhidas[0].Data);
        Assert.Empty(Atualizacoes.Escolher(Atualizacoes.LerPesquisa(PesquisaCatalogo), 22631)); // 23H2: nenhuma dessas serve
        var arquivos = Atualizacoes.LerArquivos("""
            downloadInformation[0].files[0].url = 'https://catalog.sf.dl.delivery.mp.microsoft.com/a/windows11.0-kb5043080-x64_95.msu';
            downloadInformation[0].files[1].url = 'https://catalog.sf.dl.delivery.mp.microsoft.com/b/windows11.0-kb5134133-x64_0e.msu';
            """);
        Assert.Equal(2, arquivos.Count);
        Assert.EndsWith("kb5134133-x64_0e.msu", arquivos[1]);
    }

    [Fact]
    public void WimInformaOBuildDaEdicao()
    {
        var xml = "<WIM><IMAGE INDEX=\"1\"><NAME>Windows 11 Pro</NAME><WINDOWS><VERSION><BUILD>26200</BUILD></VERSION></WINDOWS></IMAGE></WIM>";
        Assert.Equal(26200, Wim.Edicoes("x.wim", xml).Single().Build);
    }

    [Fact]
    public void ListaDeProgramasAdicionaERemove()
    {
        var texto = ListaProgramas.Adicionar(Programas.Padrao, "AnyDesk", "AnyDesk.AnyDesk");
        texto = ListaProgramas.Adicionar(texto, "Meu ERP", "arquivo: setup_erp.exe /S");
        var lista = Programas.Ler(texto);
        Assert.Contains(lista, p => p.Nome == "AnyDesk" && p.Ids.Single() == "AnyDesk.AnyDesk");
        Assert.Contains(lista, p => p.Nome == "Meu ERP" && p.Instalador == "setup_erp.exe /S");
        texto = ListaProgramas.Adicionar(texto, "AnyDesk", "AnyDeskSoftwareGmbH.AnyDesk"); // mesmo nome: substitui
        Assert.Single(Programas.Ler(texto), p => p.Nome == "AnyDesk");
        texto = ListaProgramas.Remover(texto, "anydesk");
        Assert.DoesNotContain(Programas.Ler(texto), p => p.Nome == "AnyDesk");
        Assert.Contains("# MontaWin", texto); // comentários ficam
        Assert.Equal("/qn /norestart", ListaProgramas.ParametrosSugeridos(@"C:\x\app.MSI"));
    }

    [Fact]
    public void BuscaDoWingetEmPortugues()
    {
        var saida = "\r   - \r   \\ \r" + """
            Nome                       ID                          Versão        Correspondência    Origem
            -----------------------------------------------------------------------------------------------
            AnyDesk                    AnyDesk.AnyDesk             9.0.4                            winget
            AnyDesk MSI                AnyDeskSoftwareGmbH.AnyDes… 9.0.4         Tag: anydesk       winget
            """.ReplaceLineEndings("\r\n");
        var r = ListaProgramas.LerBusca(saida);
        Assert.Equal(2, r.Count);
        Assert.Equal(new ResultadoBusca("AnyDesk", "AnyDesk.AnyDesk"), r[0]);
        Assert.Equal("AnyDesk MSI", r[1].Nome);
        Assert.Empty(ListaProgramas.LerBusca("Nenhum pacote encontrado correspondente aos critérios de entrada."));
        // Como chega de verdade, redirecionado: códigos de cursor e o "girando" grudados no cabeçalho
        var real = "\x1B[0G   - \r   \\ \r\x1B[2K\x1B[0GNome   ID            Versão Origem\r\n----------------------------------------\r\nWinRAR RARLab.WinRAR 7.13   winget\r\n";
        Assert.Equal(new ResultadoBusca("WinRAR", "RARLab.WinRAR"), ListaProgramas.LerBusca(real).Single());
    }

    [Fact]
    public void InstaladorMsiDaListaRodaPeloMsiexec()
    {
        var script = Unattend.GerarScriptProgramas([], [("erp.msi", "/qn /norestart")]);
        Assert.Contains(@"msiexec /i ""%SystemDrive%\Windows\Setup\Scripts\Instaladores\erp.msi"" /qn /norestart && type nul>", script);
    }

    [Fact]
    public void NomeDoComputadorVaiNaFaseSpecialize()
    {
        Assert.True(Unattend.NomePcValido("JOAO-ESCRITORIO"));
        Assert.False(Unattend.NomePcValido("NOME-GRANDE-DEMAIS")); // mais de 15
        Assert.False(Unattend.NomePcValido("PC JOAO"));
        Assert.False(Unattend.NomePcValido("12345"));
        var ns = (System.Xml.Linq.XNamespace)"urn:schemas-microsoft-com:unattend";
        var xml = System.Xml.Linq.XDocument.Parse(Unattend.Gerar("pt-BR", "Joao", "", "JOAO-PC"));
        Assert.Equal("specialize", xml.Descendants(ns + "ComputerName").Single().Parent!.Parent!.Attribute("pass")!.Value);
        Assert.Empty(System.Xml.Linq.XDocument.Parse(Unattend.Gerar("pt-BR", "Joao", "")).Descendants(ns + "ComputerName"));
    }

    [Fact]
    public void WifiDoWindowsNovo()
    {
        var ns = (System.Xml.Linq.XNamespace)"http://www.microsoft.com/networking/WLAN/profile/v1";
        var xml = System.Xml.Linq.XDocument.Parse(Unattend.GerarPerfilWifi("Casa & Cia", "senha<123>"));
        Assert.Equal("Casa & Cia", xml.Descendants(ns + "name").First().Value);
        Assert.Equal("senha<123>", xml.Descendants(ns + "keyMaterial").Single().Value);
        Assert.Equal("auto", xml.Descendants(ns + "connectionMode").Single().Value);
        var aberta = System.Xml.Linq.XDocument.Parse(Unattend.GerarPerfilWifi("Loja", ""));
        Assert.Equal("open", aberta.Descendants(ns + "authentication").Single().Value);
        Assert.Empty(aberta.Descendants(ns + "keyMaterial"));
    }

    [Fact]
    public void AntesDoLoginConectaAtivaInstalaEApagaTudo()
    {
        Assert.Equal("ABCDE-12345-FGHIJ-67890-KLMNO", Unattend.ChaveValida(" abcde-12345-fghij-67890-klmno "));
        Assert.Null(Unattend.ChaveValida("ABCDE-12345"));
        var script = Unattend.GerarScriptAntesDoLogin("Casa", "ABCDE-12345-FGHIJ-67890-KLMNO", programas: true);
        Assert.Contains(@"netsh wlan add profile filename=""%SystemDrive%\Windows\Setup\Scripts\MontaWin-wifi.xml"" user=all", script);
        Assert.Contains(@"del /f /q ""%SystemDrive%\Windows\Setup\Scripts\MontaWin-wifi.xml""", script); // a senha não fica no disco
        Assert.Contains("ProductKey='ABCDE-12345-FGHIJ-67890-KLMNO'", script);
        Assert.Contains(@"call %SystemDrive%\Windows\Setup\Scripts\MontaWin-programas.cmd sistema esperar", script); // espera o Wi-Fi conectar
        Assert.EndsWith("(goto) 2>nul & del /f /q \"%~f0\"\r\n", script); // a chave não fica no disco
        var soPrograma = Unattend.GerarScriptAntesDoLogin(null, null, programas: true);
        Assert.DoesNotContain("netsh", soPrograma);
        Assert.DoesNotContain("ProductKey", soPrograma);
        Assert.Contains(@"MontaWin-programas.cmd sistema """"", soPrograma);
    }

    [Fact]
    public void PerfilGuardaNomeWifiELicenca()
    {
        var p = Perfil.Ler(new Perfil("Casa") { NomePc = "JOAO-PC", Wifi = "Casa", SenhaWifi = "abc", Chave = "ABCDE-12345-FGHIJ-67890-KLMNO", Serie = "5CG1234XYZ" }.Texto())!;
        Assert.Equal(("JOAO-PC", "Casa", "abc", "ABCDE-12345-FGHIJ-67890-KLMNO", "5CG1234XYZ"), (p.NomePc, p.Wifi, p.SenhaWifi, p.Chave, p.Serie));
        var sem = Perfil.Ler(new Perfil("Outro") { Wifi = "Casa" }.Texto())!;
        Assert.Null(sem.SenhaWifi); // perguntada na hora
        Assert.Null(sem.Chave);
        Assert.Null(Perfil.Ler("Nome = Velho\r\nPC = modelo:Dell Inc. OptiPlex")!.Pc); // versão antiga: o modelo não identifica o PC
    }

    [Fact]
    public void ImagemCorrompidaNaoPassaNaConferencia()
    {
        var pasta = Directory.CreateTempSubdirectory().FullName;
        var wim = Path.Combine(pasta, "win.wim");
        File.WriteAllBytes(wim, Enumerable.Range(0, 100_000).Select(i => (byte)i).ToArray());
        Assert.Null(Wim.Conferir(wim)); // sem a impressão digital do Montador: não dá para conferir
        using (var f = File.OpenRead(wim))
            File.WriteAllText(wim + ".sha256", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(f)));
        Assert.True(Wim.Conferir(wim));
        Assert.True(File.Exists(wim + ".conferido"));
        using (var f = new FileStream(wim, FileMode.Open)) { f.Position = 5000; f.WriteByte(0xFF); } // um byte estragado
        File.SetLastWriteTimeUtc(wim, DateTime.UtcNow.AddMinutes(1));
        Assert.False(Wim.Conferir(wim));
    }

    [Fact]
    public void PrimeiroLoginProcuraDriversERelogioCertoAntesDosProgramas()
    {
        var login = Unattend.GerarScriptPrimeiroLogin([], temProgramas: false, pontoRestauracao: true);
        Assert.Contains("UsoClient.exe StartScan", login);
        Assert.Contains("Checkpoint-Computer -Description 'Windows recem-instalado (Aurora Win)'", login); // sem programas: já cria
        Assert.DoesNotContain("Checkpoint", Unattend.GerarScriptPrimeiroLogin([], temProgramas: true, pontoRestauracao: true)); // espera os programas
        var programas = Unattend.GerarScriptProgramas(["Google.Chrome"], pontoRestauracao: true);
        Assert.True(programas.IndexOf("w32tm /resync /force") < programas.IndexOf("install --id Google.Chrome"));
        Assert.True(programas.IndexOf("Checkpoint-Computer") > programas.IndexOf(":fim"));
        Assert.DoesNotContain("Checkpoint", Unattend.GerarScriptProgramas(["Google.Chrome"]));
        Assert.False(Perfil.Ler(new Perfil("X") { PontoRestauracao = false }.Texto())!.PontoRestauracao);
    }

    [Fact]
    public void IdentidadeDuplaDoPc()
    {
        var uuid = new Guid("4c4c4544-0042-3510-8051-b4c04f4a3333");
        byte[] Tabela(string serie)
        {
            var t = new List<byte>(new byte[8]);
            var tipo1 = new byte[0x1B]; tipo1[0] = 1; tipo1[1] = 0x1B; tipo1[4] = 1; tipo1[7] = 2; uuid.ToByteArray().CopyTo(tipo1, 8);
            t.AddRange(tipo1); t.AddRange(System.Text.Encoding.ASCII.GetBytes($"Dell Inc.\0{serie}\0\0"));
            var tipo2 = new byte[0x0F]; tipo2[0] = 2; tipo2[1] = 0x0F; tipo2[7] = 1;
            t.AddRange(tipo2); t.AddRange("PLACA998877\0\0"u8.ToArray());
            return t.ToArray();
        }
        Assert.Equal(uuid.ToString(), Sistema.UuidSmbios(Tabela("5CG1234XYZ")));
        Assert.Equal("5CG1234XYZ", Sistema.SerieSmbios(Tabela("5CG1234XYZ")));
        Assert.Equal("PLACA998877", Sistema.SerieSmbios(Tabela("To be filled by O.E.M."))); // genérico: usa o da placa-mãe
        Assert.False(Sistema.SerieValida("0000000"));
        Assert.False(Sistema.SerieValida("Default string"));

        Assert.True(Sistema.MesmoPc("a", "S1", "a", "S2"));      // UUID manda quando os dois têm
        Assert.False(Sistema.MesmoPc("a", "S1", "b", "S1"));     // placas diferentes, mesma série: não
        Assert.True(Sistema.MesmoPc(null, "S1", "b", "S1"));     // sem UUID de um lado: vale a série
        Assert.False(Sistema.MesmoPc(null, null, null, null));   // sem identidade nenhuma, nunca reconhece
    }
}
