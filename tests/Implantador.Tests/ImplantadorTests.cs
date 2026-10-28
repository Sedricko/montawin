using System.Text;
using MontaWin.Implantador;
using Xunit;

public class ImplantadorTests
{
    [Fact]
    public void LeEdicoesDoCabecalhoWim()
    {
        const string xml = "\uFEFF<WIM><IMAGE INDEX=\"1\"><NAME>Windows 11 Home</NAME><DISPLAYNAME>Windows 11 Home</DISPLAYNAME>" +
                           "<WINDOWS><LANGUAGES><LANGUAGE>pt-BR</LANGUAGE><DEFAULT>pt-BR</DEFAULT></LANGUAGES></WINDOWS></IMAGE>" +
                           "<IMAGE INDEX=\"2\"><NAME>Windows 11 Pro</NAME></IMAGE></WIM>";
        var dados = Encoding.Unicode.GetBytes(xml);
        var wim = new byte[0x100 + dados.Length];
        Encoding.ASCII.GetBytes("MSWIM\0\0\0").CopyTo(wim, 0);
        BitConverter.GetBytes((long)dados.Length).CopyTo(wim, 0x48);
        BitConverter.GetBytes(0x100L).CopyTo(wim, 0x50);
        BitConverter.GetBytes((long)dados.Length).CopyTo(wim, 0x58);
        dados.CopyTo(wim, 0x100);
        var arquivo = Path.GetTempFileName();
        File.WriteAllBytes(arquivo, wim);

        var edicoes = Wim.Ler(arquivo);

        Assert.Equal(new[] { (1, "Windows 11 Home", "pt-BR"), (2, "Windows 11 Pro", "en-US") }, edicoes.Select(e => (e.Indice, e.Nome, e.Idioma)));
    }

    [Fact]
    public void RecusaArquivoQueNaoEWim()
    {
        var arquivo = Path.GetTempFileName();
        File.WriteAllBytes(arquivo, new byte[0x60]);
        Assert.Throws<InvalidDataException>(() => Wim.Ler(arquivo));
    }

    [Theory]
    [InlineData("[==========                 25.0%                          ]", 25.0)]
    [InlineData("[=====  62,5% ]", 62.0)]
    [InlineData("100%", 100.0)]
    [InlineData("Implantação de Imagens", null)]
    public void LePercentualDoDism(string linha, double? esperado) => Assert.Equal(esperado, Comandos.Percentual(linha));

    [Fact]
    public void ListaPastasDosUsuariosSemPerfisDoSistema()
    {
        var users = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(users, "joao", "Documents"));
        Directory.CreateDirectory(Path.Combine(users, "joao", "AppData", "Local"));
        Directory.CreateDirectory(Path.Combine(users, "Public", "Documents"));
        File.WriteAllBytes(Path.Combine(users, "joao", "Documents", "a.txt"), new byte[5]);
        File.WriteAllBytes(Path.Combine(users, "joao", "AppData", "Local", "b.dat"), new byte[7]);

        var pastas = Backup.Pastas(users).OrderBy(x => x.Nome).ToList();
        var perfil = Assert.Single(Backup.PerfisCompletos(users));

        Assert.Equal(new[] { "AppData", "Documents" }, pastas.Select(x => x.Nome));
        Assert.Equal(new[] { false, true }, pastas.Select(x => x.Pessoal));
        Assert.Equal(5, pastas[1].Tamanho);
        Assert.Equal(Path.Combine("D:", "joao", "Documents"), pastas[1].Destino("D:"));
        Assert.Equal(("joao", 12L, "Perfil completo"), (perfil.Usuario, perfil.Tamanho, perfil.Nome));
        Assert.Equal(Path.Combine("D:", "joao"), perfil.Destino("D:"));
    }

    static Particao Dados(int n, char? letra, long gb = 200) => new(n, n * 1000L, gb << 30, TipoParticao.Dados, "", false) { Letra = letra };

    [Fact]
    public void ApagaDiscoUefiComRecuperacaoNoFim()
    {
        var p = Particionamento.ApagarDisco(2, uefi: true, 'S', 'W', 'R');
        var linhas = p.Script.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(new[] { "select disk 2", "clean", "convert gpt", "create partition efi size=300" }, linhas[..4]);
        Assert.Contains("shrink minimum=1024", linhas);
        Assert.Contains("set id=de94bba4-06d1-4d40-a16a-bfd50179d6ac", linhas);
        Assert.DoesNotContain("format quick fs=ntfs label=Dados", linhas);
        Assert.Equal(('S', 'W', (char?)'R'), (p.Sistema, p.Windows, p.Recuperacao));
    }

    [Fact]
    public void DivideDiscoMbrEmWindowsEDados()
    {
        var p = Particionamento.ApagarDisco(0, uefi: false, 'S', 'W', 'R', windowsGb: 150);
        Assert.Contains("convert mbr", p.Script);
        Assert.Contains("active", p.Script);
        Assert.Contains("create partition primary size=153600", p.Script);
        Assert.Contains("set id=27", p.Script);
        Assert.Contains("format quick fs=ntfs label=Dados", p.Script);
        Assert.DoesNotContain("shrink", p.Script);
    }

    [Fact]
    public void InstalaEmParticaoExistenteReaproveitandoEfiERecuperacao()
    {
        var efi = new Particao(1, 1, 300 << 20, TipoParticao.Efi, "", false);
        var rec = new Particao(4, 4, 1L << 30, TipoParticao.RecuperacaoWindows, "", false);
        var fabrica = new Particao(5, 5, 20L << 30, TipoParticao.Fabricante, "DELLSUPPORT", false);
        var disco = new Disco(0, 500L << 30, "SSD", false, true, [efi, Dados(3, 'C'), rec, fabrica, Dados(6, 'D')]);

        var (plano, erro) = Particionamento.ParticaoExistente(disco, disco.Particoes[1], uefi: true, 'S', 'W', 'R');

        Assert.Equal("", erro);
        Assert.Contains("select volume C", plano!.Script);
        Assert.Contains("select partition 1\nassign letter=S", plano.Script.Replace("\r", ""));
        Assert.Contains("select partition 4\nassign letter=R", plano.Script.Replace("\r", ""));
        Assert.DoesNotContain("clean", plano.Script);
        Assert.DoesNotContain("select partition 5", plano.Script);
        Assert.DoesNotContain("select volume D", plano.Script);
        Assert.Equal(('S', 'C', (char?)'R'), (plano.Sistema, plano.Windows, plano.Recuperacao));
    }

    [Fact]
    public void RecusaParticaoProtegidaOuEstiloErrado()
    {
        var fabrica = new Particao(5, 5, 20L << 30, TipoParticao.Fabricante, "DELLSUPPORT", false);
        var gpt = new Disco(0, 500L << 30, "SSD", false, true, [fabrica, Dados(2, 'C')]);
        Assert.Null(Particionamento.ParticaoExistente(gpt, fabrica, true, 'S', 'W', 'R').Plano);
        Assert.Contains("BIOS", Particionamento.ParticaoExistente(gpt, gpt.Particoes[1], false, 'S', 'W', 'R').Erro);
        Assert.Contains("EFI", Particionamento.ParticaoExistente(gpt, gpt.Particoes[1], true, 'S', 'W', 'R').Erro);
    }

    [Fact]
    public void MbrSemParticaoAtivaTornaOWindowsAtivo()
    {
        var disco = new Disco(0, 500L << 30, "HD", false, false, [Dados(1, null)]);
        var (plano, _) = Particionamento.ParticaoExistente(disco, disco.Particoes[0], false, 'S', 'W', 'R');
        Assert.Contains("select partition 1", plano!.Script);
        Assert.Contains("assign letter=W", plano.Script);
        Assert.Contains("active", plano.Script);
        Assert.Equal(('W', 'W', (char?)null), (plano.Sistema, plano.Windows, plano.Recuperacao));
    }

    [Theory]
    [InlineData("c12a7328-f81f-11d2-ba4b-00a0c93ec93b", 0UL, 1L, TipoParticao.Efi)]
    [InlineData("de94bba4-06d1-4d40-a16a-bfd50179d6ac", 0UL, 1L, TipoParticao.RecuperacaoWindows)]
    [InlineData("de94bba4-06d1-4d40-a16a-bfd50179d6ac", 0UL, 20L, TipoParticao.Fabricante)]
    [InlineData("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7", 0UL, 200L, TipoParticao.Dados)]
    [InlineData("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7", 0x8000000000000000UL, 20L, TipoParticao.Fabricante)]
    [InlineData("0fc63daf-8483-4772-8e79-3d69d8477de4", 0UL, 50L, TipoParticao.Outra)]
    public void ClassificaParticoesGpt(string tipo, ulong atributos, long gb, TipoParticao esperado) =>
        Assert.Equal(esperado, Discos.ClassificarGpt(new Guid(tipo), atributos, gb << 30));

    [Theory]
    [InlineData(0x07, true, 1L, TipoParticao.SistemaMbr)]
    [InlineData(0x07, true, 200L, TipoParticao.Dados)]
    [InlineData(0x27, false, 1L, TipoParticao.RecuperacaoWindows)]
    [InlineData(0x27, false, 15L, TipoParticao.Fabricante)]
    [InlineData(0xDE, false, 1L, TipoParticao.Fabricante)]
    public void ClassificaParticoesMbr(int tipo, bool ativa, long gb, TipoParticao esperado) =>
        Assert.Equal(esperado, Discos.ClassificarMbr((byte)tipo, ativa, gb << 30));

    [Fact]
    public void LeLayoutGptDoDisco()
    {
        var b = new byte[48 + 2 * 144];
        BitConverter.GetBytes(1).CopyTo(b, 0);   // GPT
        BitConverter.GetBytes(2).CopyTo(b, 4);
        void Entrada(int i, long inicio, long tamanho, int numero, string tipo, string nome)
        {
            int o = 48 + i * 144;
            BitConverter.GetBytes(inicio).CopyTo(b, o + 8);
            BitConverter.GetBytes(tamanho).CopyTo(b, o + 16);
            BitConverter.GetBytes(numero).CopyTo(b, o + 24);
            new Guid(tipo).ToByteArray().CopyTo(b, o + 32);
            Encoding.Unicode.GetBytes(nome).CopyTo(b, o + 72);
        }
        Entrada(0, 1 << 20, 300 << 20, 1, "c12a7328-f81f-11d2-ba4b-00a0c93ec93b", "EFI system partition");
        Entrada(1, 400 << 20, 100L << 30, 3, "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7", "Basic data partition");

        var (gpt, particoes) = Discos.LerLayout(b);

        Assert.True(gpt);
        Assert.Equal(new[] { (1, TipoParticao.Efi, 1L << 20), (3, TipoParticao.Dados, 400L << 20) },
            particoes.Select(p => (p.Numero, p.Tipo, p.Inicio)));
        Assert.Equal("Basic data partition", particoes[1].Nome);
    }

    [Fact]
    public void LeClasseEFabricanteDoInf()
    {
        const string inf = "; comentário\r\n[Version]\r\nSignature=\"$WINDOWS NT$\"\r\nClass = Net ; rede\r\nProvider=%Realtek%\r\n" +
                           "[Strings]\r\nRealtek = \"Realtek Semiconductor\"\r\n";
        Assert.Equal(("Rede", "Realtek Semiconductor"), Drivers.LerInf(inf));
        Assert.Equal(("Outros", "Contoso"), Drivers.LerInf("[Version]\nProvider=Contoso\n"));
    }

    [Fact]
    public void ListaDriversExportados()
    {
        var pasta = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(pasta, "nv.inf_amd64_1"));
        File.WriteAllText(Path.Combine(pasta, "nv.inf_amd64_1", "nv.inf"), "[Version]\nClass=Display\nProvider=NVIDIA\n");
        Directory.CreateDirectory(Path.Combine(pasta, "vazia"));

        var d = Assert.Single(Drivers.Listar(pasta));
        Assert.Equal(("Vídeo", "NVIDIA", "nv.inf"), (d.Classe, d.Fabricante, d.Inf));
    }

    [Fact]
    public void GeraUnattendValidoComContaLocal()
    {
        var xml = System.Xml.Linq.XDocument.Parse(Unattend.Gerar("pt-BR", "João & Cia", "s<nh>a"));
        var ns = (System.Xml.Linq.XNamespace)"urn:schemas-microsoft-com:unattend";
        var conta = xml.Descendants(ns + "LocalAccount").Single();
        Assert.Equal("João & Cia", conta.Element(ns + "Name")!.Value);
        Assert.Equal("Administradores", conta.Element(ns + "Group")!.Value);
        Assert.Equal("s<nh>a", conta.Descendants(ns + "Value").Single().Value);
        Assert.Equal("true", xml.Descendants(ns + "HideOnlineAccountScreens").Single().Value);
        Assert.Equal("pt-BR", xml.Descendants(ns + "UILanguage").Single().Value);
        Assert.Equal("Administrators", System.Xml.Linq.XDocument.Parse(Unattend.Gerar("en-US", "a", "")).Descendants(ns + "Group").Single().Value);
    }

    [Fact]
    public void ParticionaPendriveComBootFat32EDadosNtfs()
    {
        var linhas = MontaWin.Montador.Pendrive.ScriptParticionar(3, 'S', 'W').Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(new[] { "select disk 3", "clean", "convert mbr", "create partition primary size=2048", "format quick fs=fat32 label=MONTAWIN", "active", "assign letter=S" }, linhas[..7]);
        Assert.Contains("format quick fs=ntfs label=MONTAWIN-DADOS", linhas);
        Assert.Contains("assign letter=W", linhas);
    }

    [Fact]
    public void WinpeAbreOImplantadorDepoisDoWpeinit()
    {
        var linhas = MontaWin.Montador.Pendrive.WinpeshlIni.Replace("\r", "").Split('\n');
        Assert.Equal(new[] { "[LaunchApps]", @"%SYSTEMROOT%\System32\wpeinit.exe", @"%SYSTEMDRIVE%\MontaWin\Implantador.exe" }, linhas);
    }

    [Theory]
    [InlineData("Rede", true)]
    [InlineData("Vídeo", true)]
    [InlineData("Áudio", true)]
    [InlineData("Chipset", true)]
    [InlineData("Impressora", false)]
    [InlineData("Firmware", false)]
    [InlineData("Sensor", false)]
    public void MarcaDriversBasicosPorPadrao(string classe, bool basico) => Assert.Equal(basico, Drivers.EhBasico(classe));

    [Fact]
    public void AvisaPendriveLentoComEstimativa()
    {
        Assert.Null(Sistema.AvisoPendriveLento(120, 5_000_000_000));
        var aviso = Sistema.AvisoPendriveLento(25, 5_000_000_000)!; // 5 GB a 25 MB/s = 200 s
        Assert.Contains("25 MB/s", aviso);
        Assert.Contains("USB 3.0", aviso);
        Assert.Contains("~4 min", aviso);
    }

    [Fact]
    public void UnattendRodaOScriptDoPrimeiroLogin()
    {
        var xml = System.Xml.Linq.XDocument.Parse(Unattend.Gerar("pt-BR", "Maria", ""));
        var ns = (System.Xml.Linq.XNamespace)"urn:schemas-microsoft-com:unattend";
        Assert.Equal(@"conhost.exe --headless cmd /c %SystemDrive%\Windows\Setup\Scripts\MontaWin-primeiro-login.cmd", // sem janela preta
            xml.Descendants(ns + "CommandLine").Single().Value);
    }

    [Fact]
    public void ScriptDoPrimeiroLoginDevolvePastasEApagaASenha()
    {
        var script = Unattend.GerarScriptPrimeiroLogin([@"joao\Documents", @"joao\Desktop", @"maria\Pictures"]);
        var linhas = script.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(@"robocopy ""%SystemDrive%\Arquivos restaurados\joao\Documents"" ""%USERPROFILE%\Documents"" /E /MOVE /MT:16 /R:1 /W:1 >nul", linhas);
        Assert.Contains(@"robocopy ""%SystemDrive%\Arquivos restaurados\maria\Pictures"" ""%USERPROFILE%\Pictures"" /E /MOVE /MT:16 /R:1 /W:1 >nul", linhas);
        Assert.Contains(@"rd ""%SystemDrive%\Arquivos restaurados\joao"" 2>nul", linhas);
        Assert.Equal(@"del /f /q ""%WINDIR%\Panther\unattend.xml""", linhas[^2]); // a senha sai logo; por último, a busca de drivers
        Assert.Equal(3, Unattend.GerarScriptPrimeiroLogin([]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Count(l => !l.StartsWith('@') && !l.StartsWith("rem") && !l.StartsWith("chcp")));
    }

    [Fact]
    public void LeAListaDeProgramas()
    {
        var lista = Programas.Ler("# comentário\r\nGoogle Chrome = Google.Chrome\r\n- Visual C++ = Microsoft.VCRedist.2015+.x86 Microsoft.VCRedist.2015+.x64\r\nsem igual\r\n= sem nome\r\n");
        Assert.Equal(2, lista.Count);
        Assert.Equal(("Google Chrome", true), (lista[0].Nome, lista[0].Marcado));
        Assert.Equal(("Visual C++", false), (lista[1].Nome, lista[1].Marcado));
        Assert.Equal(new[] { "Microsoft.VCRedist.2015+.x86", "Microsoft.VCRedist.2015+.x64" }, lista[1].Ids);
        Assert.Contains(Programas.Ler(Programas.Padrao), p => p.Nome == "Lightshot" && p.Ids.Single() == "Skillbrains.Lightshot" && p.Marcado);
    }

    [Fact]
    public void ScriptDeProgramasEsperaOWingetEInstalaCadaUm()
    {
        var script = Unattend.GerarScriptProgramas(["Google.Chrome", "RARLab.WinRAR", "9NCBCSZSJRSB"]);
        Assert.Contains("where winget >nul 2>nul && goto usuario\r\n", script);
        Assert.Contains("if not exist \"%FEITOS%\\Google.Chrome\" \"%WINGET%\" install --id Google.Chrome -e --source winget %ESCOPO% --silent --accept-package-agreements --accept-source-agreements && type nul>\"%FEITOS%\\Google.Chrome\"\r\n", script);
        Assert.Contains("install --id RARLab.WinRAR -e --source winget", script);
        // Loja só no primeiro login (precisa do usuário), com a origem certa
        Assert.Contains("if \"%MODO%\"==\"usuario\" if not exist \"%FEITOS%\\9NCBCSZSJRSB\" \"%WINGET%\" install --id 9NCBCSZSJRSB -e --source msstore", script);
        Assert.False(Programas.DaLoja("Google.Chrome"));
        // Antes do login: winget do Windows, só com internet e só para todos os usuários
        Assert.Contains("curl.exe -s -m 10 -o nul https://cdn.winget.microsoft.com/cache/source.msix && goto temrede", script);
        Assert.Contains("set ESCOPO=--scope machine", script);
        Assert.Contains(@"if not ""%3""==""log"" (call ""%~f0"" %1 ""%~2"" log >>""%~dp0MontaWin-programas.log"" 2>&1 & exit /b)", script);
        Assert.Contains(@"start """" conhost.exe --headless cmd /c %SystemDrive%\Windows\Setup\Scripts\MontaWin-programas.cmd usuario",
            Unattend.GerarScriptPrimeiroLogin([], temProgramas: true));
        Assert.DoesNotContain("start", Unattend.GerarScriptPrimeiroLogin([]));
        Assert.Equal("call %SystemDrive%\\Windows\\Setup\\Scripts\\MontaWin-antes-do-login.cmd\r\n", Unattend.ChamadaSetupComplete);
    }


    static byte[] LogNvme(byte critico = 0, byte reserva = 100, byte desgaste = 3, long errosMidia = 0)
    {
        var log = new byte[512];
        log[0] = critico;
        BitConverter.GetBytes((ushort)(273 + 40)).CopyTo(log, 1);
        log[3] = reserva;
        log[4] = 10;
        log[5] = desgaste;
        BitConverter.GetBytes(1234L).CopyTo(log, 128);
        BitConverter.GetBytes(errosMidia).CopyTo(log, 160);
        return log;
    }

    [Fact]
    public void AvaliaSaudeDoNvme()
    {
        var boa = Saude.Nvme(LogNvme());
        Assert.Equal(NivelSaude.Boa, boa.Nivel);
        Assert.Contains("3% de desgaste · 1234 h ligado · 40 °C", boa.Texto);
        Assert.Equal(NivelSaude.Falhando, Saude.Nvme(LogNvme(critico: 0x04)).Nivel);
        Assert.Equal(NivelSaude.Falhando, Saude.Nvme(LogNvme(reserva: 5)).Nivel);
        Assert.Equal(NivelSaude.Atencao, Saude.Nvme(LogNvme(desgaste: 95)).Nivel);
        Assert.Equal(NivelSaude.Atencao, Saude.Nvme(LogNvme(errosMidia: 2)).Nivel);
    }

    [Fact]
    public void AvaliaSaudeDoSata()
    {
        var smart = new byte[512];
        void Atributo(int pos, byte id, uint bruto) { smart[2 + pos * 12] = id; BitConverter.GetBytes(bruto).CopyTo(smart, 2 + pos * 12 + 5); }
        Assert.Equal(NivelSaude.Desconhecida, Saude.Ata(false, smart).Nivel); // controladora não devolveu atributos
        Atributo(0, 9, 5000);   // horas ligado
        Atributo(1, 5, 0);      // realocados
        Assert.Equal(NivelSaude.Boa, Saude.Ata(false, smart).Nivel);
        Atributo(2, 197, 8);    // pendentes
        var atencao = Saude.Ata(false, smart);
        Assert.Equal(NivelSaude.Atencao, atencao.Nivel);
        Assert.Contains("8 pendentes", atencao.Texto);
        Assert.Equal(NivelSaude.Falhando, Saude.Ata(true, smart).Nivel);
    }

    [Fact]
    public void LeInstaladorDoPendriveComComandoSilencioso()
    {
        var nvidia = Assert.Single(Programas.Ler("-NVIDIA App = arquivo: NVIDIA_app*.exe -s\r\n"));
        Assert.Equal(("NVIDIA App", false, "NVIDIA_app*.exe -s"), (nvidia.Nome, nvidia.Marcado, nvidia.Instalador));
        Assert.Empty(nvidia.Ids);
        Assert.Equal(("NVIDIA_app*.exe", "-s"), Programas.Separar(nvidia.Instalador!));
        Assert.Equal(("setup.exe", ""), Programas.Separar("setup.exe"));
    }

    [Fact]
    public void ScriptRodaInstaladoresAntesDoWinget()
    {
        var script = Unattend.GerarScriptProgramas(["Google.Chrome"], [("NVIDIA_app_v11.exe", "-s"), ("setup.exe", "")]);
        int nvidia = script.IndexOf("if not exist \"%FEITOS%\\NVIDIA_app_v11.exe\" \"%SystemDrive%\\Windows\\Setup\\Scripts\\Instaladores\\NVIDIA_app_v11.exe\" -s && type nul>\"%FEITOS%\\NVIDIA_app_v11.exe\"\r\n");
        Assert.True(nvidia > 0);
        Assert.Contains("\\Instaladores\\setup.exe\" && type nul>", script);
        Assert.True(script.IndexOf("install --id Google.Chrome") > nvidia);
        Assert.True(script.IndexOf("rd /s /q") > script.IndexOf("install --id Google.Chrome")); // apaga no fim, no primeiro login
    }

    [Fact]
    public void PerfilVaiEVoltaDoArquivo()
    {
        var perfil = new Perfil("PC Escritório")
        {
            Pc = "4c4c4544-0042-3510-8051-b4c04f4a3333", Modelo = "Dell Inc. OptiPlex 7090", Edicao = "Windows 11 Pro",
            Instalacao = "dividir:200", Usuario = "Joao", Senha = "", Programas = ["Google Chrome", "Microsoft 365 (Word, Excel, Outlook)"],
            Restaurar = false, BackupDrivers = true,
        };
        var lido = Perfil.Ler(perfil.Texto())!;
        Assert.Equal(perfil with { Programas = lido.Programas }, lido);
        Assert.Equal(perfil.Programas, lido.Programas);
        Assert.Equal("", lido.Senha); // senha vazia guardada = sem senha

        var semSenha = Perfil.Ler(perfil.Texto().Replace("Senha = \r\n", ""))!;
        Assert.Null(semSenha.Senha); // sem a linha = perguntar na hora
        Assert.Null(Perfil.Ler("Edicao = Windows 11 Pro"));
        Assert.Equal("PC_Escrit_rio", Perfil.NomeSeguro("PC/Escrit:rio"));
    }

    [Fact]
    public void LeAIdentidadeDaPlacaMaeNoSmbios()
    {
        var uuid = new Guid("4c4c4544-0042-3510-8051-b4c04f4a3333");
        var tabela = new List<byte>(new byte[8]);                        // cabeçalho RawSMBIOSData
        tabela.AddRange([0, 0x18, 0, 0]); tabela.AddRange(new byte[0x14]); tabela.AddRange("Dell\0\0"u8.ToArray()); // tipo 0 (BIOS) com um texto
        var tipo1 = new byte[0x1B]; tipo1[0] = 1; tipo1[1] = 0x1B; uuid.ToByteArray().CopyTo(tipo1, 8);
        tabela.AddRange(tipo1); tabela.AddRange(new byte[] { 0, 0 });
        Assert.Equal(uuid.ToString(), Sistema.UuidSmbios(tabela.ToArray()));

        tipo1.AsSpan(8, 16).Fill(0xFF);                                  // placa genérica sem UUID
        var generica = tabela.Take(8 + 4 + 0x14 + 6).Concat(tipo1).Concat(new byte[] { 0, 0 }).ToArray();
        Assert.Null(Sistema.UuidSmbios(generica));
    }

    [Fact]
    public void AchaOsAppsInuteisNaSaidaDoDism()
    {
        // Saída em português: os rótulos mudam com o idioma, os nomes dos pacotes não.
        const string saida = """
            Nome de Exibição : Microsoft.BingNews
            Nome do Pacote : Microsoft.BingNews_4.55.62231.0_neutral_~_8wekyb3d8bbwe

            Nome do Pacote : Microsoft.WindowsStore_22409.1401.5.0_neutral_~_8wekyb3d8bbwe
            Nome do Pacote : Microsoft.XboxGamingOverlay_7.124.5142.0_neutral_~_8wekyb3d8bbwe
            Nome do Pacote : Clipchamp.Clipchamp_3.1.13190.0_neutral_~_yxz26nhyzhsrt
            Nome do Pacote : Microsoft.WindowsCalculator_11.2405.2.0_neutral_~_8wekyb3d8bbwe
            """;
        Assert.Equal(new[]
        {
            "Microsoft.BingNews_4.55.62231.0_neutral_~_8wekyb3d8bbwe",
            "Microsoft.XboxGamingOverlay_7.124.5142.0_neutral_~_8wekyb3d8bbwe",
            "Clipchamp.Clipchamp_3.1.13190.0_neutral_~_yxz26nhyzhsrt",
        }, Otimizacao.PacotesParaRemover(saida));
    }

    [Fact]
    public void OtimizacaoNaoMexeNaSeguranca()
    {
        var tudo = string.Join("\n", Otimizacao.Registro.Select(r => $"{r.Chave}\\{r.Valor}"))
                   + string.Join("\n", Otimizacao.ComandosOpcionais(true, true));
        foreach (var proibido in new[] { "Defender", "Firewall", "WindowsUpdate", "EnableLUA", "SmartScreen", "wuauserv", "Store" })
            Assert.DoesNotContain(proibido, tudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WindowsStore", string.Join(" ", Otimizacao.AppsRemovidos));
    }

    [Fact]
    public void AtrasoDaInicializacaoSoSaiEmSsd()
    {
        Assert.Contains(Otimizacao.AjustesPara(ssd: true), r => r.Valor == "StartupDelayInMSec");
        Assert.DoesNotContain(Otimizacao.AjustesPara(ssd: false), r => r.Valor == "StartupDelayInMSec");
        Assert.Empty(Otimizacao.ComandosOpcionais(false, false));
        Assert.Contains("OneDriveSetup", Assert.Single(Otimizacao.ComandosOpcionais(true, false)));
        Assert.Contains("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", Assert.Single(Otimizacao.ComandosOpcionais(false, true)));
    }

    [Fact]
    public void MultiBootCriaUmaParticaoPorWindows()
    {
        var p = Particionamento.ApagarDiscoMultiboot(1, uefi: true, 'S', ['W', 'V', 'U'], [150, 100]);
        var linhas = p.Script.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("select disk 1", linhas[0]);
        Assert.Contains("create partition primary size=153600", linhas);
        Assert.Contains("create partition primary size=102400", linhas);
        Assert.Contains("format quick fs=ntfs label=Windows3", linhas);
        Assert.Equal(1, linhas.Count(l => l == "create partition primary")); // o último fica com o resto
        Assert.Equal('W', p.Windows);
        Assert.Equal(new[] { 'V', 'U' }, p.WindowsExtras);
        Assert.Null(p.Recuperacao);
    }
}
