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
}
