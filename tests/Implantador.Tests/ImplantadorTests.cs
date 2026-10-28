using System.Text;
using MontaWin.Implantador;
using Xunit;

public class ImplantadorTests
{
    [Fact]
    public void LeEdicoesDoCabecalhoWim()
    {
        const string xml = "﻿<WIM><IMAGE INDEX=\"1\"><NAME>Windows 11 Home</NAME><DISPLAYNAME>Windows 11 Home</DISPLAYNAME></IMAGE>" +
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

        Assert.Equal(new[] { (1, "Windows 11 Home"), (2, "Windows 11 Pro") }, edicoes.Select(e => (e.Indice, e.Nome)));
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

    [Fact]
    public void ParticionaUefiComGpt()
    {
        var s = Comandos.ScriptParticionamento(2, uefi: true, 'S', 'W');
        Assert.StartsWith("select disk 2\n", s.Replace("\r", ""));
        Assert.Contains("convert gpt", s);
        Assert.Contains("create partition efi size=300", s);
        Assert.Contains("assign letter=W", s);
    }

    [Fact]
    public void ParticionaBiosComMbrAtiva()
    {
        var s = Comandos.ScriptParticionamento(0, uefi: false, 'S', 'W');
        Assert.Contains("convert mbr", s);
        Assert.Contains("active", s);
        Assert.DoesNotContain("efi", s);
    }
}
