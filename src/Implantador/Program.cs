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
var backupsFeitos = new List<string>(); // pastas de backup: nunca podem ficar no disco que vai ser formatado
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
            case 0: alvo = EscolherDisco("Escolher disco (Windows atual, para os backups)", alvo) ?? alvo; break;
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

Disco? EscolherDisco(string titulo, Disco? sugerido)
{
    var discos = Discos.Listar().Where(d => !d.Usb).ToList(); // pendrives e HDs USB nunca aparecem como destino
    if (discos.Count == 0)
    {
        Tui.Mensagem("Escolher disco", ["Nenhum disco interno encontrado.", "Se for NVMe/RAID (Intel RST), pode faltar driver no WinPE."], erro: true);
        return null;
    }
    int i = Tui.Menu(titulo, [], discos.Select(d => d.Descricao).ToList(), Math.Max(0, discos.FindIndex(d => d.Numero == sugerido?.Numero)));
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
    backupsFeitos.Add(destino);
    int total = Directory.GetDirectories(destino).Length;
    Tui.Mensagem("Backup de drivers", [$"{total} drivers salvos em {destino}", "Eles serão reinstalados automaticamente em \"Formatar e instalar\"."]);
}

void BackupArquivos()
{
    if (!Exigir(alvo?.LetraWindows != null, "Escolha um disco que tenha Windows instalado.")) return;
    var users = $@"{alvo!.LetraWindows}:\Users";
    int modo = Tui.Menu("Backup de arquivos: o que copiar?", [], [
        "Pastas pessoais de todos os usuários (recomendado)",
        "Perfil completo de todos os usuários (inclui configurações e e-mails)",
        "Escolher usuários e pastas",
    ]);
    if (modo < 0) return;

    Tui.Etapa("Backup de arquivos", "Calculando o tamanho das pastas dos usuários...");
    var itens = modo == 1 ? Backup.PerfisCompletos(users) : Backup.Pastas(users);
    if (modo == 0) itens = itens.Where(x => x.Pessoal).ToList();
    if (modo == 2)
    {
        var marcados = Tui.Selecao("Escolher usuários e pastas", [],
            itens.Select(x => $"{Tui.Ajustar(x.Usuario, 18)} {Tui.Ajustar(x.Nome, 28)} {Formatar.Tamanho(x.Tamanho),9}").ToList(),
            itens.Select(x => x.Pessoal).ToArray());
        if (marcados == null) return;
        itens = itens.Where((_, k) => marcados[k]).ToList();
    }
    if (!Exigir(itens.Count > 0, "Nenhuma pasta para copiar.")) return;
    long total = itens.Sum(x => x.Tamanho);

    // Destino: qualquer unidade fora do disco do Windows antigo e fora do WinPE (X:)
    var destinos = DriveInfo.GetDrives()
        .Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable && d.Name[0] != 'X')
        .Where(d => Discos.DiscoDaLetra(d.Name[0]) is int n && n != alvo!.Numero)
        .OrderByDescending(d => raiz != null && raiz.StartsWith(d.Name, StringComparison.OrdinalIgnoreCase))
        .ToList();
    if (!Exigir(destinos.Count > 0, "Nenhuma unidade disponível para o backup. Conecte um HD externo ou pendrive.")) return;
    int i = Tui.Menu("Salvar backup em", [$"Backup: {itens.Count} pastas, {Formatar.Tamanho(total)}"], destinos.Select(d =>
        $"{d.Name}  {Tui.Ajustar(d.VolumeLabel, 20)} livre: {Formatar.Tamanho(d.AvailableFreeSpace)}" +
        (raiz != null && raiz.StartsWith(d.Name, StringComparison.OrdinalIgnoreCase) ? "   (pendrive MontaWin)" : "")).ToList());
    if (i < 0) return;
    var destino = destinos[i];
    if (!Exigir(total < destino.AvailableFreeSpace,
        $"Não cabe: o backup tem {Formatar.Tamanho(total)} e {destino.Name} tem {Formatar.Tamanho(destino.AvailableFreeSpace)} livres.")) return;
    var pasta = Path.Combine(destino.Name, "MontaWin", "Backups", sessao, "Arquivos");

    long copiado = 0;
    foreach (var item in itens)
    {
        Tui.Etapa("Backup de arquivos", $"{item.Usuario} · {item.Nome}  ({Formatar.Tamanho(item.Tamanho)})");
        Tui.Progresso(total == 0 ? 100 : copiado * 100.0 / total);
        var temp = Path.Combine(item.Perfil, "AppData", "Local", "Temp");
        if (Comandos.Executar("robocopy", $"\"{item.Origem}\" \"{item.Destino(pasta)}\" /E /R:1 /W:1 /XJ /XD \"{temp}\" /NP /NFL /NDL /NJH /NJS") >= 8)
            throw new Exception(Falha($"Falha ao copiar {item.Origem}."));
        copiado += item.Tamanho;
    }
    backupsFeitos.Add(pasta);
    Tui.Mensagem("Backup de arquivos", [$"{itens.Count} pastas copiadas ({Formatar.Tamanho(total)})", $"Salvo em {pasta}"]);
}

void Instalar()
{
    if (!Exigir(raiz != null, "Pendrive MontaWin não encontrado (pasta MontaWin\\Imagens).")) return;
    var edicoes = Directory.GetFiles(Path.Combine(raiz!, "Imagens"), "*.wim").SelectMany(f => Wim.Ler(f)).ToList();
    if (!Exigir(edicoes.Count > 0, "Nenhuma imagem .wim na pasta MontaWin\\Imagens.")) return;
    int e = Tui.Menu("Escolher a edição", [], edicoes.Select(x => $"{x.Nome}   ({Path.GetFileName(x.Arquivo)})").ToList());
    if (e < 0) return;
    var edicao = edicoes[e];
    var disco = EscolherDisco("Disco onde o Windows será instalado", alvo);
    if (disco == null) return;
    bool uefi = Discos.Uefi;
    var letras = Comandos.LetrasLivres(3);
    if (!Exigir(letras.Length == 3, "Não há letras de unidade livres.")) return;

    int modo = Tui.Menu($"Como instalar no {disco.Descricao.Trim()}?", disco.Particoes.Select(p => p.Descricao).ToList(), [
        "Apagar o disco inteiro (automático)",
        "Apagar o disco e dividir: Windows + Dados",
        "Instalar numa partição existente (mantém as outras)",
    ]);
    if (modo < 0) return;

    PlanoParticoes plano;
    var aviso = new List<string>();
    Particao? particao = null;
    if (modo < 2)
    {
        int? windowsGb = null;
        if (modo == 1)
        {
            long gbDisco = disco.Tamanho / (1L << 30);
            var resposta = Tui.Perguntar("Tamanho do Windows (C:)", [
                $"O disco tem {gbDisco} GB. Digite quantos GB ficam para o Windows.",
                "O resto vira a partição Dados (D:), que você pode manter nas próximas formatações.",
                "Recomendado: 120 GB ou mais.",
            ], "Digite o tamanho em GB e Enter, ou só Enter para cancelar");
            if (resposta.Length == 0) return;
            if (!Exigir(int.TryParse(resposta, out int gb) && gb >= 40 && gb <= gbDisco - 5,
                $"Tamanho inválido: use de 40 a {gbDisco - 5} GB.")) return;
            windowsGb = gb;
        }
        plano = Particionamento.ApagarDisco(disco.Numero, uefi, letras[0], letras[1], letras[2], windowsGb);
        aviso.Add($"TODOS os dados de {disco.Descricao.Trim()} serão apagados.");
        var fabrica = disco.Particoes.Where(p => p.Tipo == TipoParticao.Fabricante).ToList();
        if (fabrica.Count > 0)
            aviso.Add($"Este disco tem recuperação de fábrica/fabricante ({Formatar.Tamanho(fabrica.Sum(p => p.Tamanho))}): ela também será apagada.");
        var backupNoDisco = backupsFeitos.FirstOrDefault(b => Discos.DiscoDaLetra(b[0]) == disco.Numero);
        if (!Exigir(backupNoDisco == null, $"O backup em {backupNoDisco} está nesse disco e seria apagado. Escolha outro disco.")) return;
    }
    else
    {
        if (!Exigir(disco.Particoes.Count > 0, "O disco não tem partições. Use uma das opções que apagam o disco.")) return;
        while (true)
        {
            int i = Tui.Menu("Escolher a partição onde o Windows será instalado", [], disco.Particoes.Select(p => p.Descricao).ToList());
            if (i < 0) return;
            var (encontrado, erro) = Particionamento.ParticaoExistente(disco, disco.Particoes[i], uefi, letras[0], letras[1], letras[2]);
            if (encontrado == null) { Tui.Mensagem("Atenção", [erro], erro: true); continue; }
            plano = encontrado;
            particao = disco.Particoes[i];
            break;
        }
        if (!Exigir(particao.Tamanho >= 32L << 30, "A partição precisa ter pelo menos 32 GB.")) return;
        var backupNaParticao = backupsFeitos.FirstOrDefault(b => particao.Letra is char l && char.ToUpperInvariant(b[0]) == l);
        if (!Exigir(backupNaParticao == null, $"O backup em {backupNaParticao} está nessa partição e seria apagado.")) return;
        aviso.Add($"Só a {particao.Descricao.Trim()} será apagada.");
        aviso.Add("As outras partições do disco ficam intactas.");
    }
    aviso.Add($"Instalar: {edicao.Nome}   ·   Modo: {(uefi ? "UEFI (GPT)" : "BIOS (MBR)")}");
    if (disco.LetraWindows != null && pastaDrivers == null) aviso.Add("Atenção: o backup de drivers ainda não foi feito.");
    if (!Tui.Confirmar("Formatar e instalar", aviso, "FORMATAR")) return;

    Tui.Etapa("Instalando", "1/5 · Preparando as partições");
    var script = Path.Combine(Path.GetTempPath(), "montawin-diskpart.txt");
    File.WriteAllText(script, plano.Script);
    if (Comandos.Executar("diskpart", $"/s \"{script}\"") != 0) throw new Exception(Falha("O diskpart não conseguiu preparar as partições."));
    var w = plano.Windows;

    Tui.Etapa("Instalando", $"2/5 · Aplicando {edicao.Nome}");
    if (Comandos.Executar("dism", $"/Apply-Image /ImageFile:\"{edicao.Arquivo}\" /Index:{edicao.Indice} /ApplyDir:{w}:\\", Tui.Progresso) != 0)
        throw new Exception(Falha("O DISM não conseguiu aplicar a imagem."));

    if (pastaDrivers != null)
    {
        Tui.Etapa("Instalando", "3/5 · Reinstalando os drivers do backup");
        // Driver que falhar não impede a instalação: o Windows Update completa depois.
        Comandos.Executar("dism", $"/Image:{w}:\\ /Add-Driver /Driver:\"{pastaDrivers}\" /Recurse", Tui.Progresso);
    }

    Tui.Etapa("Instalando", "4/5 · Criando a inicialização");
    if (Comandos.Executar("bcdboot", $"{w}:\\Windows /s {plano.Sistema}: /f {(uefi ? "UEFI" : "BIOS")}") != 0)
        throw new Exception(Falha("O bcdboot não conseguiu criar a inicialização."));

    var avisos = new List<string> { $"{edicao.Nome} instalado.", "Remova o pendrive e reinicie." };
    if (plano.Recuperacao is char r)
    {
        Tui.Etapa("Instalando", "5/5 · Configurando a recuperação do Windows");
        if (!ConfigurarRecuperacao(w, r)) avisos.Add("Aviso: a recuperação do Windows não foi configurada (o Windows funciona normalmente).");
    }
    Tui.Progresso(100);

    alvo = null;
    if (Tui.Menu("Instalação concluída", avisos, ["Reiniciar agora", "Voltar ao menu"]) == 0)
        Comandos.Executar("wpeutil", "reboot");
}

// Copia o WinRE da imagem aplicada para a partição de recuperação e registra no Windows novo.
static bool ConfigurarRecuperacao(char windows, char recuperacao)
{
    var origem = $@"{windows}:\Windows\System32\Recovery\Winre.wim";
    if (!File.Exists(origem)) return false;
    var pasta = $@"{recuperacao}:\Recovery\WindowsRE";
    Directory.CreateDirectory(pasta);
    var destino = Path.Combine(pasta, "Winre.wim");
    if (File.Exists(destino)) File.SetAttributes(destino, FileAttributes.Normal); // o antigo fica oculto/sistema
    File.Copy(origem, destino, overwrite: true);
    return Comandos.Executar($@"{windows}:\Windows\System32\Reagentc.exe", $@"/Setreimage /Path {pasta} /Target {windows}:\Windows") == 0;
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
