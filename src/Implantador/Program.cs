using System.Diagnostics;
using MontaWin.Implantador;

Tui.Iniciar();
Tui.InfoSistema = Sistema.Resumo() + " ";

// Pasta MontaWin no pendrive (partição NTFS): imagens, backups, drivers extras e registros ficam lá.
string? raiz = DriveInfo.GetDrives()
    .Select(d => Path.Combine(d.Name, "MontaWin"))
    .FirstOrDefault(p => Directory.Exists(Path.Combine(p, "Imagens")));
var arquivoTema = raiz == null ? null : Path.Combine(raiz, "tema.txt");
if (File.Exists(arquivoTema)) Tui.Tema(File.ReadAllText(arquivoTema));
Disco? alvo = null;
string? pastaDrivers = null;
string? pastaArquivos = null;
var backupsFeitos = new List<string>(); // pastas de backup: nunca podem ficar no disco que vai ser formatado
string sessao = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
string idPc = Sistema.IdPc();
// Perfil vinculado a este PC (pela identidade da placa-mãe): sugerido na instalação e usado no nome da pasta de backup.
Perfil? perfilDoPc = LerPerfis().FirstOrDefault(x => x.Pc == idPc);
if (raiz != null)
{
    Directory.CreateDirectory(Path.Combine(raiz, "Logs"));
    Comandos.Log = Path.Combine(raiz, "Logs", $"{sessao}.txt");
    Comandos.Registrar($"MontaWin iniciado: {Sistema.Resumo()} · PC {idPc} · perfil {perfilDoPc?.Nome ?? "(nenhum)"}");
}

// Plano Alto desempenho: o WinPE inicia em Equilibrado e a Microsoft recomenda trocar para acelerar a implantação.
Comandos.Executar("powercfg", "/s 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

// Drivers extras (ex.: Intel RST/RAID): carregados agora para o disco aparecer e instalados também no Windows novo.
string? pastaDriversExtras = raiz == null ? null : Path.Combine(raiz, "Drivers");
var infsExtras = pastaDriversExtras != null && Directory.Exists(pastaDriversExtras)
    ? Directory.GetFiles(pastaDriversExtras, "*.inf", SearchOption.AllDirectories) : Array.Empty<string>();
if (infsExtras.Length > 0)
{
    Tui.Etapa("Iniciando", $"Carregando {infsExtras.Length} drivers extras da pasta MontaWin\\Drivers");
    for (int i = 0; i < infsExtras.Length; i++)
    {
        Comandos.Executar("drvload", $"\"{infsExtras[i]}\"");
        Tui.Progresso((i + 1) * 100.0 / infsExtras.Length);
    }
}

string[] acoes = ["Escolher disco", "Backup de drivers", "Backup de arquivos", "Restaurar backup de arquivos", "Perfis",
                  "Capturar imagem de um Windows", "Formatar e instalar", "Prompt de comando", "Reiniciar", "Desligar"];
int ultima = 0;
while (true)
{
    var info = new List<string>
    {
        alvo?.Descricao ?? "Nenhum disco escolhido",
        pastaDrivers != null ? "Backup de drivers feito: será reinstalado após a instalação" : "Backup de drivers: não feito",
        pastaArquivos != null ? "Backup de arquivos feito: pode ser restaurado no Windows novo" : "Backup de arquivos: não feito",
        perfilDoPc != null ? $"Perfil deste PC: {perfilDoPc.Nome}" : "Este PC não tem perfil (crie em Perfis)",
        raiz != null ? $"Pendrive: {raiz}" + (infsExtras.Length > 0 ? $"   ·   {infsExtras.Length} drivers extras" : "")
                     : "Pasta MontaWin\\Imagens não encontrada em nenhuma unidade",
    };
    ultima = Math.Max(0, Tui.Menu("Ações", info, acoes, ultima));
    try
    {
        switch (ultima)
        {
            case 0: alvo = EscolherDisco("Escolher disco (Windows atual, para os backups)", alvo) ?? alvo; break;
            case 1: BackupDrivers(); break;
            case 2: BackupArquivos(); break;
            case 3: RestaurarManual(); break;
            case 4: MenuPerfis(); break;
            case 5: CapturarImagem(); break;
            case 6: Instalar(); break;
            case 7: PromptDeComando(); break;
            case 8: Comandos.Executar("wpeutil", "reboot"); break;
            case 9: Comandos.Executar("wpeutil", "shutdown"); break;
        }
    }
    catch (Exception e)
    {
        Comandos.Registrar($"ERRO: {e.Message}");
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
    if (i < 0) return null;
    var disco = discos[i];
    Comandos.Registrar($"Disco {disco.Numero} ({disco.Modelo}): {disco.Saude.Texto}");
    if (disco.Saude.Nivel >= NivelSaude.Atencao)
        Tui.Mensagem($"Saúde do disco {disco.Numero}: {disco.Saude.Curta}", [disco.Saude.Texto,
            disco.Saude.Nivel == NivelSaude.Falhando ? "Faça o backup agora e recomende a troca do disco ao cliente."
                                                     : "O disco funciona, mas vale avisar o cliente e fazer o backup."], erro: true);
    return disco;
}

bool Exigir(bool condicao, string mensagem)
{
    if (!condicao) Tui.Mensagem("Atenção", [mensagem], erro: true);
    return condicao;
}

// Pasta de backup desta vez: MontaWin\Backups\<perfil ou modelo do PC>\<data>, com a identidade do PC dentro.
string PastaBackup(string unidade)
{
    var nomePc = perfilDoPc?.Nome ?? (Sistema.Modelo() is { Length: > 0 } modelo ? modelo : "PC");
    var pasta = Path.Combine(unidade, "MontaWin", "Backups", Perfil.NomeSeguro(nomePc), sessao);
    Directory.CreateDirectory(pasta);
    File.WriteAllText(Path.Combine(pasta, "pc.txt"), $"{idPc}\r\n{Sistema.Resumo()}\r\n");
    return pasta;
}

// automatico: usado pelo perfil antes de formatar, sem perguntas (só os drivers básicos voltam).
void BackupDrivers(Disco? origem = null, bool automatico = false)
{
    origem ??= alvo;
    if (!Exigir(origem?.LetraWindows != null, "Escolha um disco que tenha Windows instalado.")) return;
    if (!Exigir(raiz != null, "Pendrive MontaWin não encontrado para salvar os drivers.")) return;
    var destino = Path.Combine(PastaBackup(Path.GetPathRoot(raiz)!), "Drivers");
    Directory.CreateDirectory(destino);
    Tui.Etapa("Backup de drivers", $"Exportando drivers de {origem!.LetraWindows}:\\Windows para {destino}");
    if (Comandos.Executar("dism", $"/Image:{origem.LetraWindows}:\\ /Export-Driver /Destination:\"{destino}\"", Tui.Progresso) != 0)
        throw new Exception(Falha("O DISM não conseguiu exportar os drivers."));

    // Exporta tudo e depois deixa desmarcar o que não deve voltar (ex.: um driver de vídeo problemático).
    var drivers = Drivers.Listar(destino);
    var marcados = automatico ? drivers.Select(d => d.Basico).ToArray()
        : Tui.Selecao("Drivers que serão reinstalados (básicos já vêm marcados; Esc mantém todos)", [],
            drivers.Select(d => d.Descricao).ToList(), drivers.Select(d => d.Basico).ToArray());
    int removidos = 0;
    for (int i = 0; marcados != null && i < drivers.Count; i++)
        if (!marcados[i]) { Directory.Delete(drivers[i].Pasta, recursive: true); removidos++; }

    pastaDrivers = destino;
    backupsFeitos.Add(destino);
    if (!automatico) Tui.Mensagem("Backup de drivers", [$"{drivers.Count - removidos} drivers salvos em {destino}", "Eles serão reinstalados automaticamente em \"Formatar e instalar\"."]);
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
    var pasta = Path.Combine(PastaBackup(destino.Name), "Arquivos");

    long copiado = 0;
    foreach (var item in itens)
    {
        Tui.Etapa("Backup de arquivos", $"{item.Usuario} · {item.Nome}  ({Formatar.Tamanho(item.Tamanho)})");
        Tui.Progresso(total == 0 ? 100 : copiado * 100.0 / total);
        var temp = Path.Combine(item.Perfil, "AppData", "Local", "Temp");
        if (Comandos.Executar("robocopy", $"\"{item.Origem}\" \"{item.Destino(pasta)}\" /E /MT:16 /R:1 /W:1 /XJ /XD \"{temp}\" /NP /NFL /NDL /NJH /NJS") >= 8)
            throw new Exception(Falha($"Falha ao copiar {item.Origem}."));
        copiado += item.Tamanho;
    }
    backupsFeitos.Add(pasta);
    pastaArquivos = pasta;
    Tui.Mensagem("Backup de arquivos", [$"{itens.Count} pastas copiadas ({Formatar.Tamanho(total)})", $"Salvo em {pasta}"]);
}

// Copia um backup de arquivos para a pasta "Arquivos restaurados" na raiz do Windows (C:\Arquivos restaurados).
void Restaurar(string backup, char windows)
{
    Tui.Etapa("Restaurando arquivos", $"{backup} -> {windows}:\\{Unattend.PastaRestaurados}");
    if (Comandos.Executar("robocopy", $"\"{backup}\" \"{windows}:\\{Unattend.PastaRestaurados}\" /E /MT:16 /R:1 /W:1 /NP /NFL /NDL /NJH /NJS") >= 8)
        throw new Exception(Falha("Falha ao restaurar os arquivos."));
}

void RestaurarManual()
{
    var backups = DriveInfo.GetDrives().Where(d => d.IsReady && d.Name[0] != 'X')
        .SelectMany(d => Directory.Exists(Path.Combine(d.Name, "MontaWin", "Backups"))
            ? Directory.GetDirectories(Path.Combine(d.Name, "MontaWin", "Backups"), "Arquivos", SearchOption.AllDirectories) : Array.Empty<string>())
        .OrderByDescending(x => Path.GetFileName(Path.GetDirectoryName(x))) // data mais recente primeiro
        .ToList();
    if (!Exigir(backups.Count > 0, "Nenhum backup de arquivos encontrado (pasta MontaWin\\Backups).")) return;
    int b = Tui.Menu("Qual backup restaurar?", [], backups);
    if (b < 0) return;
    var windows = Discos.Listar().Where(d => !d.Usb).SelectMany(d => d.Particoes.Where(p => p.TemWindows && p.Letra != null).Select(p => (d, p))).ToList();
    if (!Exigir(windows.Count > 0, "Nenhum Windows instalado encontrado nos discos internos.")) return;
    int w = Tui.Menu("Restaurar em qual Windows?", [], windows.Select(x => $"{x.p.Letra}:  {x.d.Descricao.Trim()}").ToList());
    if (w < 0) return;
    char letra = windows[w].p.Letra!.Value;
    Restaurar(backups[b], letra);
    Tui.Mensagem("Restaurar backup de arquivos", [$"Arquivos copiados para a pasta \"{Unattend.PastaRestaurados}\" na raiz do Windows ({letra}: aqui no painel; C: no Windows)."]);
}

List<Perfil> LerPerfis() => raiz == null || !Directory.Exists(Path.Combine(raiz, "Perfis")) ? new List<Perfil>()
    : Directory.GetFiles(Path.Combine(raiz, "Perfis"), "*.txt").Select(f => Perfil.Ler(File.ReadAllText(f))).OfType<Perfil>().ToList();

string ArquivoPerfil(string nome) => Path.Combine(raiz!, "Perfis", Perfil.NomeSeguro(nome) + ".txt");

List<Edicao> LerEdicoes() => Directory.GetFiles(Path.Combine(raiz!, "Imagens"), "*.wim").SelectMany(f => Wim.Ler(f)).ToList();

// Programas da lista do pendrive; instalador que não está na pasta MontaWin\Instaladores não aparece.
string? AcharInstalador(Programa x)
{
    var pasta = Path.Combine(raiz!, "Instaladores");
    return x.Instalador == null || !Directory.Exists(pasta) ? null
        : Directory.GetFiles(pasta, Programas.Separar(x.Instalador).Arquivo).OrderDescending().FirstOrDefault(); // * pega a versão mais nova
}

List<Programa> LerProgramas()
{
    var arquivo = Path.Combine(raiz!, "Programas.txt");
    return Programas.Ler(File.Exists(arquivo) ? File.ReadAllText(arquivo) : Programas.Padrao)
        .Where(x => x.Instalador == null || AcharInstalador(x) != null).ToList();
}

List<Programa>? EscolherProgramas(List<Programa> programas, Func<Programa, bool> marcado)
{
    var marcados = Tui.Selecao("Programas instalados no primeiro login (precisa de internet; Esc = nenhum)",
        ["Lista editável no pendrive: MontaWin\\Programas.txt"], programas.Select(x => x.Nome).ToList(), programas.Select(marcado).ToArray());
    return programas.Where((_, k) => marcados?[k] == true).ToList();
}

string? PerguntarUsuario()
{
    var usuario = Tui.Perguntar("Nome do usuário", ["Nome da conta local do Windows (ex.: Joao).", "Até 20 letras, sem \\ / [ ] : ; | = , + * ? < > \""],
        "Digite o nome e Enter, ou só Enter para cancelar");
    if (usuario.Length == 0) return null;
    return Exigir(usuario.Length <= 20 && usuario.IndexOfAny("\\/[]:;|=,+*?<>\"".ToCharArray()) < 0, "Nome de usuário inválido.") ? usuario : null;
}

string PerguntarSenha(string usuario) => Tui.Perguntar("Senha do usuário", [$"Senha da conta {usuario}.", "Deixe em branco para entrar sem senha."],
    "Digite a senha (ou deixe em branco) e Enter", oculto: true);

static string DescreverInstalacao(string instalacao) => instalacao switch
{
    "particao" => "partição existente",
    var d when d.StartsWith("dividir:") => $"Windows {d["dividir:".Length..]} GB + Dados",
    _ => "apagar o disco inteiro",
};

void MenuPerfis()
{
    if (!Exigir(raiz != null, "Pendrive MontaWin não encontrado (os perfis ficam em MontaWin\\Perfis).")) return;
    while (true)
    {
        var perfis = LerPerfis();
        var opcoes = perfis.Select(x => $"{x.Nome}   ({x.Edicao} · {DescreverInstalacao(x.Instalacao)})" + (x.Pc == idPc ? "   <- este PC" : "")).Prepend("+ Adicionar perfil").ToList();
        int i = Tui.Menu("Perfis", ["Cada perfil guarda as respostas da instalação de um PC.", "Com um perfil, a instalação só pede o disco e o FORMATAR."], opcoes);
        if (i < 0) return;
        if (i == 0) { AdicionarPerfil(); continue; }
        var perfil = perfis[i - 1];
        var detalhes = perfil.Texto().Split("\r\n").Where(l => l.Length > 0 && !l.StartsWith('#') && !l.StartsWith("Senha")).ToList();
        detalhes.Add(perfil.Senha == null ? "Senha: perguntada na hora" : "Senha: guardada no perfil");
        if (Tui.Menu($"Perfil {perfil.Nome}", detalhes, ["Voltar", "Apagar este perfil"]) == 1
            && Tui.Confirmar("Apagar perfil", [$"Apagar o perfil {perfil.Nome}? Os backups dele continuam no pendrive."], "APAGAR"))
        {
            File.Delete(ArquivoPerfil(perfil.Nome));
            if (perfilDoPc?.Nome == perfil.Nome) perfilDoPc = null;
        }
    }
}

void AdicionarPerfil()
{
    var nome = Tui.Perguntar("Novo perfil", ["Nome do perfil (ex.: PC Escritório, Notebook)."], "Digite o nome e Enter, ou só Enter para cancelar");
    if (nome.Length == 0 || !Exigir(Perfil.NomeSeguro(nome).Length > 0, "Nome inválido.")) return;
    if (File.Exists(ArquivoPerfil(nome)) && !Tui.Confirmar("Novo perfil", [$"Já existe um perfil {nome}. Substituir?"], "SIM")) return;

    var nomesEdicoes = LerEdicoes().Select(x => x.Nome).Distinct().ToList();
    if (!Exigir(nomesEdicoes.Count > 0, "Nenhuma imagem .wim na pasta MontaWin\\Imagens.")) return;
    int e = Tui.Menu($"{nome}: edição do Windows", [], nomesEdicoes);
    if (e < 0) return;

    int modo = Tui.Menu($"{nome}: como instalar", [], ["Apagar o disco inteiro (automático)", "Apagar o disco e dividir: Windows + Dados",
        "Instalar numa partição existente (a partição é escolhida na hora)"]);
    if (modo < 0) return;
    string instalacao = modo == 2 ? "particao" : "apagar";
    if (modo == 1)
    {
        var resposta = Tui.Perguntar($"{nome}: tamanho do Windows (C:)", ["Quantos GB ficam para o Windows? O resto vira a partição Dados.", "Recomendado: 120 GB ou mais."],
            "Digite o tamanho em GB e Enter, ou só Enter para cancelar");
        if (!int.TryParse(resposta, out int gb) || !Exigir(gb >= 40, "Use pelo menos 40 GB.")) return;
        instalacao = $"dividir:{gb}";
    }

    string? usuario = null, senha = null;
    var escolhidos = new List<Programa>();
    int configuracao = Tui.Menu($"{nome}: configuração inicial", [], ["Automática: criar conta local e pular as telas iniciais (recomendado)",
        "Normal: as telas da Microsoft aparecem no primeiro uso"]);
    if (configuracao < 0) return;
    if (configuracao == 0)
    {
        usuario = PerguntarUsuario();
        if (usuario == null) return;
        int guardar = Tui.Menu($"{nome}: senha", [], ["Perguntar a senha na hora de instalar (mais seguro)",
            "Guardar a senha no perfil (mais rápido; fica escrita no pendrive)"]);
        if (guardar < 0) return;
        if (guardar == 1) senha = PerguntarSenha(usuario);
        escolhidos = EscolherProgramas(LerProgramas(), x => x.Marcado) ?? [];
    }

    int restaurar = Tui.Menu($"{nome}: backup de arquivos", [], ["Restaurar no Windows novo quando houver backup (recomendado)", "Não restaurar"]);
    if (restaurar < 0) return;
    int drivers = Tui.Menu($"{nome}: backup de drivers", [], ["Fazer sozinho antes de formatar e reinstalar (recomendado)", "Não fazer"]);
    if (drivers < 0) return;
    int otimizar = Tui.Menu($"{nome}: otimização", ["Remove apps inúteis e propaganda, desliga Widgets e telemetria extra.", "Segurança e atualizações do Windows não são alteradas."],
        ["Otimizar o Windows (recomendado)", "Windows como a Microsoft entrega"]);
    if (otimizar < 0) return;
    var extrasPerfil = Tui.Selecao($"{nome}: ajustes opcionais", ["Espaço marca/desmarca, Enter confirma."],
        ["Remover o OneDrive", "Modo de energia Alto desempenho (melhor em PC de mesa)"], [true, false]) ?? [true, false];
    int vincular = Tui.Menu($"{nome}: este PC", [$"PC atual: {Sistema.Resumo()}"],
        ["Vincular a este PC: o painel sugere este perfil sozinho aqui", "Não vincular (perfil para usar em qualquer PC)"]);
    if (vincular < 0) return;

    var perfil = new Perfil(nome.Trim())
    {
        Pc = vincular == 0 ? idPc : null,
        Modelo = vincular == 0 ? Sistema.Modelo() : null,
        Edicao = nomesEdicoes[e],
        Instalacao = instalacao,
        Usuario = usuario,
        Senha = senha,
        Programas = escolhidos.Select(x => x.Nome).ToArray(),
        Restaurar = restaurar == 0,
        BackupDrivers = drivers == 0,
        Otimizar = otimizar == 0,
        RemoverOneDrive = extrasPerfil[0],
        AltoDesempenho = extrasPerfil[1],
    };
    Directory.CreateDirectory(Path.Combine(raiz!, "Perfis"));
    File.WriteAllText(ArquivoPerfil(perfil.Nome), perfil.Texto());
    if (perfil.Pc == idPc) perfilDoPc = perfil;
    Comandos.Registrar($"Perfil salvo: {perfil.Nome}");
    Tui.Mensagem("Perfil salvo", [$"Perfil {perfil.Nome} salvo em {ArquivoPerfil(perfil.Nome)}.", "Use em \"Formatar e instalar\"."]);
}

void Instalar()
{
    if (!Exigir(raiz != null, "Pendrive MontaWin não encontrado (pasta MontaWin\\Imagens).")) return;
    var edicoes = LerEdicoes();
    if (!Exigir(edicoes.Count > 0, "Nenhuma imagem .wim na pasta MontaWin\\Imagens.")) return;

    // Perfil: o deste PC vem primeiro e já selecionado.
    Perfil? perfil = null;
    var perfis = LerPerfis().OrderByDescending(x => x.Pc == idPc).ToList();
    if (perfis.Count > 0)
    {
        var opcoes = perfis.Select(x => $"{x.Nome}   ({x.Edicao} · {DescreverInstalacao(x.Instalacao)})" + (x.Pc == idPc ? "   <- este PC" : ""))
            .Append("Sem perfil: responder tudo agora").ToList();
        int escolha = Tui.Menu("Usar um perfil?", [], opcoes, perfis[0].Pc == idPc ? 0 : perfis.Count);
        if (escolha < 0) return;
        perfil = escolha < perfis.Count ? perfis[escolha] : null;
        if (perfil?.Pc is string pcPerfil && pcPerfil != idPc
            && !Tui.Confirmar("Perfil de outro PC", [$"O perfil {perfil.Nome} é do PC {perfil.Modelo}. Este é {Sistema.Modelo()}.", "Usar mesmo assim?"], "SIM")) return;
    }

    Edicao edicao;
    if (perfil != null)
    {
        var doPerfil = edicoes.FirstOrDefault(x => x.Nome == perfil.Edicao);
        if (!Exigir(doPerfil != null, $"A edição \"{perfil.Edicao}\" do perfil não está nas imagens do pendrive.")) return;
        edicao = doPerfil!;
    }
    else
    {
        int e = Tui.Menu("Escolher a edição", [], edicoes.Select(x => $"{x.Nome}   ({Path.GetFileName(x.Arquivo)})").ToList());
        if (e < 0) return;
        edicao = edicoes[e];
    }
    var disco = EscolherDisco("Disco onde o Windows será instalado", alvo);
    if (disco == null) return;
    bool uefi = Discos.Uefi;
    var letras = Comandos.LetrasLivres(3);
    if (!Exigir(letras.Length == 3, "Não há letras de unidade livres.")) return;

    int modo = perfil == null
        ? Tui.Menu($"Como instalar no {disco.Descricao.Trim()}?", disco.Particoes.Select(p => p.Descricao).ToList(), [
            "Apagar o disco inteiro (automático)",
            "Apagar o disco e dividir: Windows + Dados",
            "Instalar numa partição existente (mantém as outras)",
            "Vários Windows no mesmo disco (multi boot)",
        ])
        : perfil.Instalacao == "particao" ? 2 : perfil.Instalacao.StartsWith("dividir:") ? 1 : 0;
    if (modo < 0) return;

    PlanoParticoes plano;
    var aviso = new List<string>();
    if (perfil != null) aviso.Add($"Perfil: {perfil.Nome}");
    Particao? particao = null;
    int? windowsGb = null;
    var extras = new List<Edicao>(); // multi boot: os outros Windows, na ordem escolhida
    if (modo == 3)
    {
        int maximo = uefi ? 4 : 2; // MBR: no máximo 4 partições principais (inicialização + Windows)
        var marcados = Tui.Selecao($"Multi boot: marque de 2 a {maximo} Windows", ["O primeiro marcado fica como padrão no menu de boot."],
            edicoes.Select(x => $"{x.Nome}   ({Path.GetFileName(x.Arquivo)})").ToList(), edicoes.Select(x => x == edicao).ToArray());
        if (marcados == null) return;
        var escolhidas = edicoes.Where((_, k) => marcados[k]).ToList();
        if (!Exigir(escolhidas.Count >= 2 && escolhidas.Count <= maximo, $"Marque de 2 a {maximo} Windows.")) return;
        edicao = escolhidas[0];
        extras = escolhidas.Skip(1).ToList();
        long gbDisco = disco.Tamanho / (1L << 30);
        var tamanhos = new List<int>();
        for (int k = 0; k < escolhidas.Count - 1; k++)
        {
            var resposta = Tui.Perguntar($"Tamanho de {escolhidas[k].Nome}", [$"O disco tem {gbDisco} GB. Quantos GB para este Windows?",
                $"O último ({escolhidas[^1].Nome}) fica com o resto."], "Digite o tamanho em GB e Enter, ou só Enter para cancelar");
            if (resposta.Length == 0 || !Exigir(int.TryParse(resposta, out int gb) && gb >= 40, "Use pelo menos 40 GB.")) return;
            tamanhos.Add(gb);
        }
        if (!Exigir(tamanhos.Sum() + 40 <= gbDisco, $"Não cabe: sobram menos de 40 GB para {escolhidas[^1].Nome}.")) return;
        var letrasMulti = Comandos.LetrasLivres(escolhidas.Count + 1);
        if (!Exigir(letrasMulti.Length == escolhidas.Count + 1, "Não há letras de unidade livres.")) return;
        plano = Particionamento.ApagarDiscoMultiboot(disco.Numero, uefi, letrasMulti[0], letrasMulti[1..], tamanhos);
        aviso.Add($"TODOS os dados de {disco.Descricao.Trim()} serão apagados.");
        aviso.Add("Multi boot: " + string.Join(" + ", escolhidas.Select((x, k) => k < tamanhos.Count ? $"{x.Nome} ({tamanhos[k]} GB)" : $"{x.Nome} (resto)")));
        var backupNoDisco = backupsFeitos.FirstOrDefault(b => Discos.DiscoDaLetra(b[0]) == disco.Numero);
        if (!Exigir(backupNoDisco == null, $"O backup em {backupNoDisco} está nesse disco e seria apagado. Escolha outro disco.")) return;
    }
    else if (modo < 2)
    {
        if (modo == 1)
        {
            long gbDisco = disco.Tamanho / (1L << 30);
            var resposta = perfil != null ? perfil.Instalacao["dividir:".Length..] : Tui.Perguntar("Tamanho do Windows (C:)", [
                $"O disco tem {gbDisco} GB. Digite quantos GB ficam para o Windows.",
                "O resto vira a partição Dados (D:), que você pode manter nas próximas formatações.",
                "Recomendado: 120 GB ou mais.",
            ], "Digite o tamanho em GB e Enter, ou só Enter para cancelar");
            if (resposta.Length == 0) return;
            if (!Exigir(int.TryParse(resposta, out int gb) && gb >= 40 && gb <= gbDisco - 5,
                $"Tamanho do Windows inválido para este disco ({resposta} GB): use de 40 a {gbDisco - 5} GB.")) return;
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

    string? usuario;
    if (perfil != null) usuario = perfil.Usuario;
    else
    {
        int configuracao = Tui.Menu("Configuração inicial do Windows", [], [
            "Automática: criar conta local e pular as telas iniciais (recomendado)",
            "Normal: o cliente responde as telas da Microsoft no primeiro uso",
        ]);
        if (configuracao < 0) return;
        usuario = configuracao == 0 ? PerguntarUsuario() : null;
        if (configuracao == 0 && usuario == null) return;
    }
    string? unattend = null;
    string senha = "";
    if (usuario != null)
    {
        senha = perfil?.Senha ?? PerguntarSenha(usuario);
        unattend = Unattend.Gerar(edicao.Idioma, usuario, senha);
        aviso.Add($"Conta local: {usuario}{(senha.Length > 0 ? " (com senha)" : " (sem senha)")} · telas iniciais puladas");
    }

    // Programas no primeiro login: só com a configuração automática, que roda o script do primeiro login.
    var idsProgramas = new List<string>();
    var instaladores = new List<(string Caminho, string Parametros)>(); // instaladores do pendrive escolhidos
    var nomesProgramas = new List<string>();
    if (unattend != null)
    {
        var escolhidos = perfil != null
            ? LerProgramas().Where(x => perfil.Programas.Contains(x.Nome)).ToList()
            : EscolherProgramas(LerProgramas(), x => x.Marcado) ?? [];
        idsProgramas = escolhidos.SelectMany(x => x.Ids).ToList();
        instaladores = escolhidos.Where(x => x.Instalador != null).Select(x => (AcharInstalador(x)!, Programas.Separar(x.Instalador!).Parametros)).ToList();
        nomesProgramas = escolhidos.Select(x => x.Nome).ToList();
        if (escolhidos.Count > 0) aviso.Add("Programas no primeiro login: " + string.Join(", ", nomesProgramas));
    }

    int otimizacao = perfil != null ? (perfil.Otimizar ? 0 : 1) : Tui.Menu("Otimizar o Windows?", [
        "Remove apps inúteis e propaganda (Notícias, Xbox, Paciência, Clipchamp...), bloqueia apps sugeridos,",
        "desliga Widgets, pesquisa do Iniciar só no PC, telemetria no mínimo.",
        "Defender, firewall, Windows Update e Microsoft Store continuam como a Microsoft entrega."],
        ["Sim: otimizar (recomendado)", "Não: Windows como a Microsoft entrega"]);
    if (otimizacao < 0) return;
    bool otimizar = otimizacao == 0;
    bool removerOneDrive = perfil?.RemoverOneDrive ?? true, altoDesempenho = perfil?.AltoDesempenho ?? false;
    if (perfil == null)
    {
        var extrasMarcados = Tui.Selecao("Ajustes opcionais", ["Espaço marca/desmarca, Enter confirma."], [
            "Remover o OneDrive",
            "Modo de energia Alto desempenho (melhor em PC de mesa; em notebook gasta mais bateria e esquenta)",
        ], [removerOneDrive, altoDesempenho]);
        if (extrasMarcados != null) (removerOneDrive, altoDesempenho) = (extrasMarcados[0], extrasMarcados[1]);
    }
    if (removerOneDrive) aviso.Add("OneDrive removido");
    if (altoDesempenho) aviso.Add("Modo de energia: Alto desempenho");
    if (otimizar) aviso.Add("Otimização: sem apps inúteis, propaganda e Widgets" + (disco.Ssd ? " · inicialização sem atraso (SSD)" : ""));

    // Backup de drivers automático do perfil: só se o disco tem Windows e ainda não foi feito nesta vez.
    bool backupDriversAutomatico = perfil?.BackupDrivers == true && disco.LetraWindows != null && pastaDrivers == null;
    if (backupDriversAutomatico) aviso.Add("Backup de drivers (básicos) feito antes de formatar e reinstalado depois");

    bool restaurar = false;
    if (pastaArquivos != null)
    {
        int opcao = perfil != null ? (perfil.Restaurar ? 0 : 1) : Tui.Menu("Restaurar o backup de arquivos no Windows novo?", [$"Backup: {pastaArquivos}"], [
            "Sim: copiar os arquivos para o Windows novo (recomendado)",
            "Não: deixar só no backup (dá para restaurar depois pelo menu)",
        ]);
        if (opcao < 0) return;
        restaurar = opcao == 0;
        if (restaurar) aviso.Add(unattend != null ? "Backup de arquivos: volta para as pastas do usuário novo no primeiro login"
                                                  : $"Backup de arquivos: copiado para C:\\{Unattend.PastaRestaurados}");
    }
    aviso.Add($"Instalar: {edicao.Nome}   ·   Modo: {(uefi ? "UEFI (GPT)" : "BIOS (MBR)")}");
    if (disco.Saude.Nivel == NivelSaude.Falhando) aviso.Add($"DISCO FALHANDO: {disco.Saude.Texto}");
    else if (disco.Saude.Nivel == NivelSaude.Atencao) aviso.Add($"Disco com problemas: {disco.Saude.Texto}");
    Tui.Etapa("Formatar e instalar", "Verificando a velocidade do pendrive...");
    if (Sistema.AvisoPendriveLento(Sistema.VelocidadeLeitura(edicao.Arquivo), new FileInfo(edicao.Arquivo).Length) is string lento) aviso.Add(lento);
    if (Sistema.NaBateria) aviso.Add("Notebook na bateria: ligue o carregador (a instalação fica mais rápida e não corre risco de desligar).");
    if (disco.LetraWindows != null && pastaDrivers == null) aviso.Add("Atenção: o backup de drivers ainda não foi feito.");
    if (!Tui.Confirmar("Formatar e instalar", aviso, "FORMATAR")) return;
    if (backupDriversAutomatico) BackupDrivers(disco, automatico: true);

    Tui.Etapa("Instalando", "Preparando as partições");
    var scriptDiskpart = Path.Combine(Path.GetTempPath(), "montawin-diskpart.txt");
    File.WriteAllText(scriptDiskpart, plano.Script);
    if (Comandos.Executar("diskpart", $"/s \"{scriptDiskpart}\"") != 0) throw new Exception(Falha("O diskpart não conseguiu preparar as partições."));

    // Multi boot: cada bcdboot vira o padrão do menu, então o primeiro escolhido é instalado por último.
    var instalacoes = new List<(Edicao Edicao, char Letra)> { (edicao, plano.Windows) };
    instalacoes.AddRange(extras.Zip(plano.WindowsExtras));
    bool multi = instalacoes.Count > 1;
    var bcd = uefi ? $@"{plano.Sistema}:\EFI\Microsoft\Boot\BCD" : $@"{plano.Sistema}:\Boot\BCD";
    foreach (var (ed, w) in Enumerable.Reverse(instalacoes))
    {
        bool principal = w == plano.Windows;
        Tui.Etapa("Instalando", $"Aplicando {ed.Nome}");
        if (Comandos.Executar("dism", $"/Apply-Image /ImageFile:\"{ed.Arquivo}\" /Index:{ed.Indice} /ApplyDir:{w}:\\", Tui.Progresso) != 0)
            throw new Exception(Falha($"O DISM não conseguiu aplicar {ed.Nome}."));
        if (usuario != null)
        {
            Directory.CreateDirectory($@"{w}:\Windows\Panther");
            File.WriteAllText($@"{w}:\Windows\Panther\unattend.xml", Unattend.Gerar(ed.Idioma, usuario, senha));
        }
        if (otimizar || removerOneDrive || altoDesempenho)
        {
            Tui.Etapa("Instalando", $"Otimizando {ed.Nome}");
            Comandos.Registrar($"Otimização: {Otimizacao.Aplicar(w, disco.Ssd, otimizar, removerOneDrive, altoDesempenho, Tui.Progresso)} apps removidos de {ed.Nome}");
        }

        // Driver que falhar não impede a instalação: o Windows Update completa depois.
        foreach (var (pasta, nome) in new[] { (pastaDrivers, "do backup"), (infsExtras.Length > 0 ? pastaDriversExtras : null, "extras") })
        {
            if (pasta == null) continue;
            Tui.Etapa("Instalando", $"Instalando os drivers {nome} em {ed.Nome}");
            Comandos.Executar("dism", $"/Image:{w}:\\ /Add-Driver /Driver:\"{pasta}\" /Recurse", Tui.Progresso);
        }
        if (principal && restaurar) Restaurar(pastaArquivos!, w);
        if (usuario != null)
        {
            // Pastas pessoais (joao\Documents...) voltam para o usuário novo; o resto fica em C:\Arquivos restaurados.
            var pessoais = principal && restaurar
                ? Directory.GetDirectories(pastaArquivos!).SelectMany(u => Backup.PastasPessoais
                    .Where(p => Directory.Exists(Path.Combine(u, p))).Select(p => $@"{Path.GetFileName(u)}\{p}")).ToList()
                : new List<string>();
            var script = $@"{w}:\{Unattend.ScriptPrimeiroLogin}";
            Directory.CreateDirectory(Path.GetDirectoryName(script)!);
            bool temProgramas = idsProgramas.Count + instaladores.Count > 0;
            File.WriteAllText(script, Unattend.GerarScriptPrimeiroLogin(pessoais, temProgramas));
            if (temProgramas)
            {
                // Instaladores vão do pendrive para dentro do Windows novo: no primeiro login o pendrive já foi removido.
                Directory.CreateDirectory($@"{w}:\{Unattend.PastaInstaladores}");
                foreach (var (caminho, _) in instaladores)
                    File.Copy(caminho, Path.Combine($@"{w}:\{Unattend.PastaInstaladores}", Path.GetFileName(caminho)), overwrite: true);
                File.WriteAllText($@"{w}:\{Unattend.ScriptProgramas}", Unattend.GerarScriptProgramas(idsProgramas,
                    instaladores.Select(x => (Path.GetFileName(x.Caminho), x.Parametros))));
            }
        }

        Tui.Etapa("Instalando", $"Criando a inicialização de {ed.Nome}");
        if (Comandos.Executar("bcdboot", $"{w}:\\Windows /s {plano.Sistema}: /f {(uefi ? "UEFI" : "BIOS")}") != 0)
            throw new Exception(Falha("O bcdboot não conseguiu criar a inicialização."));
        // No menu de boot, cada Windows com o nome da edição (o bcdboot põe só "Windows 11" ou "Windows 10")
        if (multi) Comandos.Executar("bcdedit", $"/store \"{bcd}\" /set {{default}} description \"{ed.Nome}\"");
    }
    if (multi) Comandos.Executar("bcdedit", $"/store \"{bcd}\" /timeout 10");

    var avisos = new List<string> { string.Join(" + ", instalacoes.Select(x => x.Edicao.Nome)) + " instalado.", "Remova o pendrive e reinicie." };
    if (multi) avisos.Add("Ao ligar, o menu de boot mostra os Windows por 10 segundos.");
    if (restaurar && unattend == null) avisos.Add($"Arquivos do backup em C:\\{Unattend.PastaRestaurados}.");
    if (plano.Recuperacao is char r)
    {
        Tui.Etapa("Instalando", "Configurando a recuperação do Windows");
        if (!ConfigurarRecuperacao(plano.Windows, r)) avisos.Add("Aviso: a recuperação do Windows não foi configurada (o Windows funciona normalmente).");
    }
    Tui.Progresso(100);
    Comandos.Registrar($"Instalação concluída: {string.Join(" + ", instalacoes.Select(x => x.Edicao.Nome))} no disco {disco.Numero}");
    alvo = null;

    // Com perfil, reinicia sozinho; sem perfil, oferece salvar as escolhas como perfil.
    if (perfil != null)
    {
        if (Tui.Contagem("Instalação concluída", avisos, "Reiniciando", 15)) Comandos.Executar("wpeutil", "reboot");
        return;
    }
    if (!multi && Tui.Menu("Salvar estas escolhas como perfil?", ["Da próxima vez, a instalação só pede o disco e o FORMATAR."],
            ["Não", "Sim, salvar como perfil"]) == 1)
        SalvarComoPerfil(new Perfil("")
        {
            Edicao = edicao.Nome,
            Instalacao = modo == 2 ? "particao" : windowsGb != null ? $"dividir:{windowsGb}" : "apagar",
            Usuario = usuario,
            Senha = senha,
            Programas = nomesProgramas.ToArray(),
            Restaurar = restaurar || pastaArquivos == null,
            BackupDrivers = pastaDrivers != null,
            Otimizar = otimizar,
            RemoverOneDrive = removerOneDrive,
            AltoDesempenho = altoDesempenho,
        });
    if (Tui.Menu("Instalação concluída", avisos, ["Reiniciar agora", "Voltar ao menu"]) == 0)
        Comandos.Executar("wpeutil", "reboot");
}

void SalvarComoPerfil(Perfil escolhas)
{
    var nome = Tui.Perguntar("Salvar como perfil", ["Nome do perfil (ex.: PC Escritório)."], "Digite o nome e Enter, ou só Enter para cancelar");
    if (nome.Length == 0 || Perfil.NomeSeguro(nome).Length == 0) return;
    bool guardarSenha = escolhas.Usuario != null && Tui.Menu("Senha", [], ["Perguntar a senha na hora de instalar (mais seguro)",
        "Guardar a senha no perfil (mais rápido; fica escrita no pendrive)"]) == 1;
    bool vincular = Tui.Menu("Este PC", [$"PC atual: {Sistema.Resumo()}"],
        ["Vincular a este PC: o painel sugere este perfil sozinho aqui", "Não vincular"]) == 0;
    var perfil = escolhas with
    {
        Nome = nome.Trim(),
        Senha = guardarSenha ? escolhas.Senha : null,
        Pc = vincular ? idPc : null,
        Modelo = vincular ? Sistema.Modelo() : null,
    };
    Directory.CreateDirectory(Path.Combine(raiz!, "Perfis"));
    File.WriteAllText(ArquivoPerfil(perfil.Nome), perfil.Texto());
    if (vincular) perfilDoPc = perfil;
    Comandos.Registrar($"Perfil salvo: {perfil.Nome}");
}

// Salva um Windows instalado como imagem no pendrive (MontaWin\Imagens\Personalizadas.wim); aparece na lista de edições.
void CapturarImagem()
{
    if (!Exigir(raiz != null, "Pendrive MontaWin não encontrado.")) return;
    var windows = Discos.Listar().Where(d => !d.Usb).SelectMany(d => d.Particoes.Where(p => p.TemWindows && p.Letra != null).Select(p => (d, p))).ToList();
    if (!Exigir(windows.Count > 0, "Nenhum Windows instalado encontrado nos discos internos.")) return;
    int w = Tui.Menu("Capturar qual Windows?", [
        "Salva o Windows com programas e configurações como imagem no pendrive.",
        "Para instalar em OUTROS PCs, antes rode no Windows (como administrador):",
        @"C:\Windows\System32\Sysprep\sysprep.exe /generalize /oobe /shutdown",
        "Sem isso, a imagem serve para restaurar este mesmo PC."],
        windows.Select(x => $"{x.p.Letra}:  {x.d.Descricao.Trim()}").ToList());
    if (w < 0) return;
    char letra = windows[w].p.Letra!.Value;
    var nome = Tui.Perguntar("Nome da imagem", ["Ex.: Escritório 2026. É o nome que aparece na lista de edições."], "Digite o nome e Enter, ou só Enter para cancelar")
        .Replace("\"", "");
    if (nome.Length == 0) return;
    var unidadeWindows = new DriveInfo(letra.ToString());
    var usado = unidadeWindows.IsReady ? unidadeWindows.TotalSize - unidadeWindows.AvailableFreeSpace : 0;
    var livre = new DriveInfo(Path.GetPathRoot(raiz)!).AvailableFreeSpace;
    // A imagem comprimida costuma ficar com uns 50% do espaço usado.
    if (!Exigir(usado / 2 < livre, $"Pouco espaço no pendrive: o Windows usa {Formatar.Tamanho(usado)} e o pendrive tem {Formatar.Tamanho(livre)} livres.")) return;

    var arquivo = Path.Combine(raiz!, "Imagens", "Personalizadas.wim");
    var acao = File.Exists(arquivo) ? "/Append-Image" : "/Capture-Image /Compress:max";
    Tui.Etapa("Capturar imagem", $"Salvando {letra}: como \"{nome}\" em {arquivo}");
    if (Comandos.Executar("dism", $"{acao} /ImageFile:\"{arquivo}\" /CaptureDir:{letra}:\\ /Name:\"{nome}\"", Tui.Progresso) != 0)
        throw new Exception(Falha("O DISM não conseguiu capturar a imagem."));
    Comandos.Registrar($"Imagem capturada: {nome} ({letra}:)");
    Tui.Mensagem("Capturar imagem", [$"Imagem \"{nome}\" salva.", "Ela aparece em \"Formatar e instalar\", junto com as edições do Windows."]);
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
