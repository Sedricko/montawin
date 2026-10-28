using System.Diagnostics;
using MontaWin.Implantador;

Tui.Iniciar();
Tui.InfoSistema = Sistema.Resumo() + " ";

// Pasta MontaWin no pendrive (partição NTFS): imagens e backups ficam lá.
string? raiz = DriveInfo.GetDrives()
    .Select(d => Path.Combine(d.Name, "MontaWin"))
    .FirstOrDefault(p => Directory.Exists(Path.Combine(p, "Imagens")));
Disco? alvo = null;
string? pastaDrivers = null;
string sessao = DateTime.Now.ToString("yyyy-MM-dd_HHmm");

string[] acoes = ["Escolher disco", "Backup de drivers", "Backup de arquivos", "Formatar e instalar", "Prompt de comando", "Reiniciar", "Desligar"];
int ultima = 0;
while (true)
{
    var info = new List<string>
    {
        alvo?.Descricao ?? "Nenhum disco escolhido",
        pastaDrivers != null ? "Backup de drivers feito: será reinstalado após a instalação" : "Backup de drivers: não feito",
        raiz != null ? $"Pendrive: {raiz}" : "Pasta MontaWin\\Imagens não encontrada em nenhuma unidade",
    };
    ultima = Math.Max(0, Tui.Menu("Ações", info, acoes, ultima));
    try
    {
        switch (ultima)
        {
            case 0: alvo = EscolherDisco() ?? alvo; break;
            case 1: BackupDrivers(); break;
            case 2: BackupArquivos(); break;
            case 3: Instalar(); break;
            case 4: PromptDeComando(); break;
            case 5: Comandos.Executar("wpeutil", "reboot"); break;
            case 6: Comandos.Executar("wpeutil", "shutdown"); break;
        }
    }
    catch (Exception e)
    {
        Tui.Mensagem("Erro", e.Message.Split('\n'), erro: true);
    }
}

Disco? EscolherDisco()
{
    var discos = Discos.Listar().Where(d => !d.Usb).ToList(); // pendrives e HDs USB nunca aparecem como destino
    if (discos.Count == 0)
    {
        Tui.Mensagem("Escolher disco", ["Nenhum disco interno encontrado.", "Se for NVMe/RAID (Intel RST), pode faltar driver no WinPE."], erro: true);
        return null;
    }
    int i = Tui.Menu("Escolher disco", [], discos.Select(d => d.Descricao).ToList());
    return i < 0 ? null : discos[i];
}

bool Exigir(bool condicao, string mensagem)
{
    if (!condicao) Tui.Mensagem("Atenção", [mensagem], erro: true);
    return condicao;
}

void BackupDrivers()
{
    if (!Exigir(alvo?.LetraWindows != null, "Escolha um disco que tenha Windows instalado.")) return;
    if (!Exigir(raiz != null, "Pendrive MontaWin não encontrado para salvar os drivers.")) return;
    var destino = Path.Combine(raiz!, "Backups", sessao, "Drivers");
    Directory.CreateDirectory(destino);
    Tui.Etapa("Backup de drivers", $"Exportando drivers de {alvo!.LetraWindows}:\\Windows para {destino}");
    if (Comandos.Executar("dism", $"/Image:{alvo.LetraWindows}:\\ /Export-Driver /Destination:\"{destino}\"", Tui.Progresso) != 0)
        throw new Exception(Falha("O DISM não conseguiu exportar os drivers."));
    pastaDrivers = destino;
    int total = Directory.GetDirectories(destino).Length;
    Tui.Mensagem("Backup de drivers", [$"{total} drivers salvos em {destino}", "Eles serão reinstalados automaticamente em \"Formatar e instalar\"."]);
}

void BackupArquivos()
{
    if (!Exigir(alvo?.LetraWindows != null, "Escolha um disco que tenha Windows instalado.")) return;
    // Destino: qualquer unidade fora do disco que vai ser formatado e fora do WinPE (X:)
    var destinos = DriveInfo.GetDrives()
        .Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable && d.Name[0] != 'X')
        .Where(d => Discos.DiscoDaLetra(d.Name[0]) is int n && n != alvo!.Numero)
        .OrderByDescending(d => raiz != null && raiz.StartsWith(d.Name, StringComparison.OrdinalIgnoreCase))
        .ToList();
    if (!Exigir(destinos.Count > 0, "Nenhuma unidade disponível para o backup. Conecte um HD externo ou pendrive.")) return;
    int i = Tui.Menu("Salvar backup em", [], destinos.Select(d =>
        $"{d.Name}  {Tui.Ajustar(d.VolumeLabel, 20)} livre: {Formatar.Tamanho(d.AvailableFreeSpace)}" +
        (raiz != null && raiz.StartsWith(d.Name, StringComparison.OrdinalIgnoreCase) ? "   (pendrive MontaWin)" : "")).ToList());
    if (i < 0) return;
    var destino = destinos[i];
    var pasta = Path.Combine(destino.Name, "MontaWin", "Backups", sessao, "Arquivos");

    Tui.Etapa("Backup de arquivos", "Calculando o tamanho das pastas dos usuários...");
    var itens = Backup.Planejar(alvo!.LetraWindows!.Value, pasta);
    long total = itens.Sum(x => x.Tamanho);
    if (!Exigir(total < destino.AvailableFreeSpace,
        $"Não cabe: o backup tem {Formatar.Tamanho(total)} e {destino.Name} tem {Formatar.Tamanho(destino.AvailableFreeSpace)} livres.")) return;

    long copiado = 0;
    foreach (var item in itens)
    {
        Tui.Etapa("Backup de arquivos", $"{item.Origem}  ({Formatar.Tamanho(item.Tamanho)})");
        Tui.Progresso(total == 0 ? 100 : copiado * 100.0 / total);
        if (Comandos.Executar("robocopy", $"\"{item.Origem}\" \"{item.Destino}\" /E /R:1 /W:1 /XJ /NP /NFL /NDL /NJH /NJS") >= 8)
            throw new Exception(Falha($"Falha ao copiar {item.Origem}."));
        copiado += item.Tamanho;
    }
    Tui.Mensagem("Backup de arquivos", [$"{itens.Count} pastas copiadas ({Formatar.Tamanho(total)})", $"Salvo em {pasta}"]);
}

void Instalar()
{
    if (!Exigir(alvo != null, "Escolha o disco primeiro.")) return;
    if (!Exigir(raiz != null, "Pendrive MontaWin não encontrado (pasta MontaWin\\Imagens).")) return;
    var edicoes = Directory.GetFiles(Path.Combine(raiz!, "Imagens"), "*.wim").SelectMany(f => Wim.Ler(f)).ToList();
    if (!Exigir(edicoes.Count > 0, "Nenhuma imagem .wim na pasta MontaWin\\Imagens.")) return;
    int e = Tui.Menu("Escolher a edição", [], edicoes.Select(x => $"{x.Nome}   ({Path.GetFileName(x.Arquivo)})").ToList());
    if (e < 0) return;
    var edicao = edicoes[e];

    var aviso = new List<string>
    {
        $"TODOS os dados de {alvo!.Descricao.Trim()} serão apagados.",
        $"Instalar: {edicao.Nome}   ·   Modo: {(Discos.Uefi ? "UEFI (GPT)" : "BIOS (MBR)")}",
    };
    if (alvo.LetraWindows != null && pastaDrivers == null) aviso.Add("Atenção: o backup de drivers ainda não foi feito.");
    if (!Tui.Confirmar("Formatar e instalar", aviso, "FORMATAR")) return;

    var letras = Comandos.LetrasLivres(2);
    if (!Exigir(letras.Length == 2, "Não há letras de unidade livres.")) return;
    var (sistema, windows) = (letras[0], letras[1]);

    Tui.Etapa("Instalando", "1/4 · Particionando o disco");
    var script = Path.Combine(Path.GetTempPath(), "montawin-diskpart.txt");
    File.WriteAllText(script, Comandos.ScriptParticionamento(alvo.Numero, Discos.Uefi, sistema, windows));
    if (Comandos.Executar("diskpart", $"/s \"{script}\"") != 0) throw new Exception(Falha("O diskpart não conseguiu particionar o disco."));

    Tui.Etapa("Instalando", $"2/4 · Aplicando {edicao.Nome}");
    if (Comandos.Executar("dism", $"/Apply-Image /ImageFile:\"{edicao.Arquivo}\" /Index:{edicao.Indice} /ApplyDir:{windows}:\\", Tui.Progresso) != 0)
        throw new Exception(Falha("O DISM não conseguiu aplicar a imagem."));

    if (pastaDrivers != null)
    {
        Tui.Etapa("Instalando", "3/4 · Reinstalando os drivers do backup");
        // Driver que falhar não impede a instalação: o Windows Update completa depois.
        Comandos.Executar("dism", $"/Image:{windows}:\\ /Add-Driver /Driver:\"{pastaDrivers}\" /Recurse", Tui.Progresso);
    }

    Tui.Etapa("Instalando", "4/4 · Criando a inicialização");
    if (Comandos.Executar("bcdboot", $"{windows}:\\Windows /s {sistema}: /f {(Discos.Uefi ? "UEFI" : "BIOS")}") != 0)
        throw new Exception(Falha("O bcdboot não conseguiu criar a inicialização."));
    Tui.Progresso(100);

    alvo = null;
    if (Tui.Menu("Instalação concluída", [$"{edicao.Nome} instalado.", "Remova o pendrive e reinicie."], ["Reiniciar agora", "Voltar ao menu"]) == 0)
        Comandos.Executar("wpeutil", "reboot");
}

static string Falha(string mensagem)
{
    lock (Comandos.Saida) return mensagem + "\n" + string.Join("\n", Comandos.Saida);
}

static void PromptDeComando()
{
    Console.Clear();
    Console.CursorVisible = true;
    Console.WriteLine("Digite exit para voltar ao MontaWin.");
    Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = false })!.WaitForExit();
    Console.CursorVisible = false;
}
