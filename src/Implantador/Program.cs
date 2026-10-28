using System.Diagnostics;
using MontaWin.Implantador;

// Pasta MontaWin no pendrive (partição NTFS): imagens, backups, drivers extras, tema e registros ficam lá.
string? raiz = DriveInfo.GetDrives()
    .Select(d => Path.Combine(d.Name, "MontaWin"))
    .FirstOrDefault(p => Directory.Exists(Path.Combine(p, "Imagens")));
string sessao = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
if (raiz != null)
{
    Directory.CreateDirectory(Path.Combine(raiz, "Logs"));
    Comandos.Log = Path.Combine(raiz, "Logs", $"{sessao}.txt");
}
Tui.Iniciar(raiz);
Tui.InfoSistema = Sistema.Resumo() + " ";

// O disco do pendrive nunca aparece como destino da instalação (nem quando o PC o vê como disco interno).
int? discoDoPendrive = raiz == null ? null : Discos.DiscoDaLetra(raiz[0]);
Disco? alvo = null;
string? pastaDrivers = null;
string? pastaArquivos = null;
var backupsFeitos = new List<string>(); // pastas de backup: nunca podem ficar no disco que vai ser formatado
// Duas identidades de fábrica (UUID da placa e número de série): o perfil e os backups são reconhecidos por elas.
var (uuidPc, seriePc) = Sistema.Identidade();
bool pcIdentificado = uuidPc != null || seriePc != null;
// Perfil vinculado a este PC (pela identidade da placa-mãe): sugerido na instalação e usado no nome da pasta de backup.
Perfil? perfilDoPc = LerPerfis().FirstOrDefault(EstePc);
Comandos.Registrar($"MontaWin iniciado: {Sistema.Resumo()} · PC {uuidPc ?? "-"} / série {seriePc ?? "-"} · perfil {perfilDoPc?.Nome ?? "(nenhum)"} · tema {Tui.Tema}");

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

string[] acoes = ["Formatar e instalar o Windows", "Backup (drivers e arquivos)", "Restaurar backup de arquivos", "Escolher disco (para os backups)",
                  "Perfis", "Capturar imagem de um Windows", "Prompt de comando", "Reiniciar", "Desligar"];
int ultima = 0;
// PC reconhecido pelas identidades de fábrica: a primeira tela já oferece formatar com o perfil dele.
bool oferecerPerfil = perfilDoPc != null;
while (true)
{
    if (oferecerPerfil && perfilDoPc != null)
    {
        oferecerPerfil = false;
        var p = perfilDoPc;
        int r = Tui.Menu($"{p.Nome} reconhecido", [
            Sistema.Resumo() + (seriePc != null ? $" · série {seriePc}" : ""),
            $"{p.Edicao} · {DescreverInstalacao(p.Instalacao)}" + (p.Usuario != null ? $" · conta {p.Usuario}" : "") + (p.Wifi != null ? $" · Wi-Fi {p.Wifi}" : ""),
            "O perfil já sabe tudo deste PC: você só escolhe o disco e confirma."],
            [$"Formatar com o perfil \"{p.Nome}\"", "Ir para o menu (outras opções)"], voltar: null);
        if (r == 0)
        {
            try { Instalar(p); }
            catch (Exception e)
            {
                Comandos.Registrar($"ERRO: {e.Message}");
                Tui.Mensagem("Algo deu errado", e.Message.Split('\n'), erro: true);
            }
            continue;
        }
    }
    var info = new List<string>
    {
        alvo != null ? $"Disco escolhido: {Compacto(alvo.Descricao)}" : "Nenhum disco escolhido para os backups",
        pastaDrivers != null ? "Backup de drivers feito: volta sozinho no Windows novo" : "Backup de drivers: não feito",
        pastaArquivos != null ? "Backup de arquivos feito: pode voltar para o Windows novo" : "Backup de arquivos: não feito",
        perfilDoPc != null ? $"Perfil deste PC: {perfilDoPc.Nome}" : "Este PC não tem perfil (crie em Perfis)",
        raiz != null ? $"Pendrive: {raiz}" + (infsExtras.Length > 0 ? $"   ·   {infsExtras.Length} drivers extras" : "")
                     : "Pasta MontaWin\\Imagens não encontrada em nenhuma unidade",
    };
    ultima = Math.Max(0, Tui.Menu("O que vamos fazer?", info, acoes, ultima, voltar: null));
    try
    {
        switch (ultima)
        {
            case 0: Instalar(); break;
            case 1: BackupCompleto(); break;
            case 2: RestaurarManual(); break;
            case 3: alvo = EscolherDisco("Disco do Windows atual (para os backups)", alvo) ?? alvo; break;
            case 4: MenuPerfis(); break;
            case 5: CapturarImagem(); break;
            case 6: if (Tui.Grafica) Tui.Terminal(); else Tui.Pausar(PromptDeComando); break;
            case 7: Comandos.Executar("wpeutil", "reboot"); break;
            case 8: Comandos.Executar("wpeutil", "shutdown"); break;
        }
    }
    catch (Exception e)
    {
        Comandos.Registrar($"ERRO: {e.Message}");
        Tui.Mensagem("Algo deu errado", e.Message.Split('\n'), erro: true);
    }
}

// Textos em colunas (feitos para listas) numa linha corrida: "Disco 0 · Samsung SSD · 476 GB".
static string Compacto(string texto) => string.Join(" · ", texto.Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

// Assistente: cada passo devolve Avancar, Voltar ou Pular (segue no sentido em que se está andando); Sair cancela tudo.
// Voltar no primeiro passo também cancela. Retorna true quando todos os passos avançaram.
static bool Assistente(params Func<int, int>[] passos)
{
    int p = 0, sentido = 1;
    while (p < passos.Length)
    {
        if (p < 0) return false;
        int r = passos[p](sentido);
        if (r == Passo.Sair) return false;
        if (r != Passo.Pular) sentido = r;
        p += sentido;
    }
    return true;
}

Disco? EscolherDisco(string titulo, Disco? sugerido)
{
    var discos = Discos.Listar().Where(d => !d.Usb && d.Numero != discoDoPendrive).ToList(); // pendrives e HDs USB nunca aparecem
    if (discos.Count == 0)
    {
        Tui.Mensagem("Escolher disco", ["Nenhum disco interno encontrado.", "Se for NVMe/RAID (Intel RST), pode faltar driver no WinPE:",
            "coloque o driver na pasta MontaWin\\Drivers do pendrive."], erro: true);
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
    File.WriteAllText(Path.Combine(pasta, "pc.txt"), $"uuid={uuidPc}\r\nserie={seriePc}\r\n{Sistema.Resumo()}\r\n");
    return pasta;
}

// automatico: usado pelo perfil antes de formatar, dentro da tela de etapas e sem perguntas (só os drivers básicos voltam).
void BackupDrivers(Disco? origem = null, bool automatico = false)
{
    origem ??= alvo;
    if (!Exigir(origem?.LetraWindows != null, "Escolha um disco que tenha Windows instalado (em \"Escolher disco\").")) return;
    if (!Exigir(raiz != null, "Pendrive MontaWin não encontrado para salvar os drivers.")) return;
    var destino = Path.Combine(PastaBackup(Path.GetPathRoot(raiz)!), "Drivers");
    Directory.CreateDirectory(destino);
    if (!automatico) Tui.Etapa("Backup de drivers", $"Copiando os drivers do Windows em {origem!.LetraWindows}:");
    Tui.Registro($"Exportando os drivers do Windows atual ({origem!.LetraWindows}:) para o pendrive");
    if (Comandos.Executar("dism", $"/Image:{origem.LetraWindows}:\\ /Export-Driver /Destination:\"{destino}\"", Tui.Progresso) != 0)
        throw new Exception(Falha("O DISM não conseguiu exportar os drivers."));

    // Exporta tudo e depois deixa desmarcar o que não deve voltar (ex.: um driver de vídeo problemático).
    var drivers = Drivers.Listar(destino);
    var marcados = automatico ? null
        : Tui.Selecao("Quais drivers voltam no Windows novo?", ["Os básicos (vídeo, rede, áudio, chipset...) já vêm marcados.", "Voltar mantém só os básicos."],
            drivers.Select(d => d.Descricao).ToList(), drivers.Select(d => d.Basico).ToArray());
    marcados ??= drivers.Select(d => d.Basico).ToArray();
    int removidos = 0;
    for (int i = 0; i < drivers.Count; i++)
        if (!marcados[i]) { Directory.Delete(drivers[i].Pasta, recursive: true); removidos++; }

    pastaDrivers = destino;
    backupsFeitos.Add(destino);
    Tui.Registro($"{drivers.Count - removidos} drivers guardados para voltar no Windows novo");
    if (!automatico) Tui.Mensagem("Backup de drivers", [$"{drivers.Count - removidos} drivers salvos em {destino}", "Eles voltam sozinhos em \"Formatar e instalar\"."]);
}

// Backup de um Windows: marca o que salvar (drivers e/ou arquivos). Retorna false se voltou sem fazer nada.
bool BackupCompleto(Disco? disco = null)
{
    disco ??= alvo?.LetraWindows != null ? alvo : EscolherDisco("De qual disco fazer o backup?", alvo);
    if (disco == null) return false;
    if (!Exigir(disco.LetraWindows != null, "Esse disco não tem Windows instalado: não há o que salvar.")) return false;
    alvo = disco;
    var m = Tui.Selecao("O que salvar?", [$"Windows encontrado em {disco.LetraWindows}: ({Compacto(disco.Descricao)})"], [
        "Drivers deste PC (os básicos voltam sozinhos no Windows novo)",
        "Arquivos dos usuários (pastas pessoais, perfil completo ou escolher pastas)",
    ], [pastaDrivers == null, pastaArquivos == null]);
    if (m == null) return false;
    if (m[0]) BackupDrivers();
    if (m[1]) BackupArquivos();
    return true;
}

void BackupArquivos()
{
    if (!Exigir(alvo?.LetraWindows != null, "Escolha um disco que tenha Windows instalado (em \"Escolher disco\").")) return;
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
        var marcados = Tui.Selecao("Escolher usuários e pastas", ["As pastas pessoais já vêm marcadas."],
            itens.Select(x => $"{Formatar.Ajustar(x.Usuario, 18)}  {Formatar.Ajustar(x.Nome, 28)}  {Formatar.Tamanho(x.Tamanho),9}").ToList(),
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
    int i = Tui.Menu("Salvar o backup em", [$"Backup: {itens.Count} pastas, {Formatar.Tamanho(total)}"], destinos.Select(d =>
        $"{d.Name}  {Formatar.Ajustar(d.VolumeLabel, 20)}  livre: {Formatar.Tamanho(d.AvailableFreeSpace)}" +
        (raiz != null && raiz.StartsWith(d.Name, StringComparison.OrdinalIgnoreCase) ? "  (pendrive MontaWin)" : "")).ToList());
    if (i < 0) return;
    var destino = destinos[i];
    if (!Exigir(total < destino.AvailableFreeSpace,
        $"Não cabe: o backup tem {Formatar.Tamanho(total)} e {destino.Name} tem {Formatar.Tamanho(destino.AvailableFreeSpace)} livres.")) return;
    var pasta = Path.Combine(PastaBackup(destino.Name), "Arquivos");

    // Uma etapa por pasta, com o tamanho como peso: a barra e o tempo restante seguem o volume copiado.
    Tui.Etapas("Backup de arquivos", itens.Select(x => ($"{x.Usuario} · {x.Nome}", (double)Math.Max(x.Tamanho, 1))).ToList());
    for (int k = 0; k < itens.Count; k++)
    {
        var item = itens[k];
        Tui.Etapa(k, $"Copiando {item.Usuario} · {item.Nome} ({Formatar.Tamanho(item.Tamanho)})");
        var relogio = Stopwatch.StartNew();
        var temp = Path.Combine(item.Perfil, "AppData", "Local", "Temp");
        if (Comandos.Executar("robocopy", $"\"{item.Origem}\" \"{item.Destino(pasta)}\" /E /MT:16 /R:1 /W:1 /XJ /XD \"{temp}\" /NP /NFL /NDL /NJH /NJS") >= 8)
            throw new Exception(Falha($"Falha ao copiar {item.Origem}."));
        Tui.Registro($"{item.Usuario} · {item.Nome}: {Formatar.Tamanho(item.Tamanho)} copiados{Velocidade(item.Tamanho, relogio.Elapsed)}");
    }
    Tui.Etapa(itens.Count, "Pronto");
    backupsFeitos.Add(pasta);
    pastaArquivos = pasta;
    Tui.Mensagem("Backup de arquivos", [$"{itens.Count} pastas copiadas ({Formatar.Tamanho(total)})", $"Salvo em {pasta}"]);
}

static string Velocidade(long bytes, TimeSpan tempo) =>
    tempo.TotalSeconds < 1 || bytes < 50_000_000 ? "" : $" em {Tui.Duracao(tempo)} ({bytes / 1e6 / tempo.TotalSeconds:0} MB/s)";

// Copia um backup de arquivos para a pasta "Arquivos restaurados" na raiz do Windows (C:\Arquivos restaurados).
void Restaurar(string backup, char windows)
{
    long bytes = Backup.Tamanho(backup);
    Tui.Registro($"Copiando {Formatar.Tamanho(bytes)} de arquivos do backup para o Windows novo");
    var relogio = Stopwatch.StartNew();
    if (Comandos.Executar("robocopy", $"\"{backup}\" \"{windows}:\\{Unattend.PastaRestaurados}\" /E /MT:16 /R:1 /W:1 /NP /NFL /NDL /NJH /NJS") >= 8)
        throw new Exception(Falha("Falha ao restaurar os arquivos."));
    Tui.Registro($"Arquivos restaurados: {Formatar.Tamanho(bytes)}{Velocidade(bytes, relogio.Elapsed)}");
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
    var windows = WindowsInstalados();
    if (!Exigir(windows.Count > 0, "Nenhum Windows instalado encontrado nos discos internos.")) return;
    int w = Tui.Menu("Restaurar em qual Windows?", [], windows.Select(x => $"{x.p.Letra}:  {Compacto(x.d.Descricao)}").ToList());
    if (w < 0) return;
    char letra = windows[w].p.Letra!.Value;
    Tui.Etapa("Restaurando arquivos", $"{backups[b]} → {letra}:\\{Unattend.PastaRestaurados}");
    Restaurar(backups[b], letra);
    Tui.Mensagem("Restaurar backup de arquivos", [$"Arquivos copiados para a pasta \"{Unattend.PastaRestaurados}\" na raiz do Windows ({letra}: aqui no painel; C: no Windows)."]);
}

List<(Disco d, Particao p)> WindowsInstalados() => Discos.Listar().Where(d => !d.Usb && d.Numero != discoDoPendrive)
    .SelectMany(d => d.Particoes.Where(p => p.TemWindows && p.Letra != null).Select(p => (d, p))).ToList();

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

// Nenhum vem marcado: o técnico marca o que este PC precisa (ou "Marcar todos").
List<Programa>? EscolherProgramas(string titulo, List<Programa> programas, List<Programa> escolhidos)
{
    bool offline = raiz != null && Directory.Exists(Path.Combine(raiz, "Instaladores", "winget"));
    var marcados = Tui.Selecao(titulo, [
        "Instalam sozinhos no Windows novo: antes da tela de login, ou logo depois dela.",
        offline ? "Os baixados no pendrive instalam sem internet; os outros precisam de internet." : "Precisa de internet no primeiro login.",
        "Lista editável no pendrive: MontaWin\\Programas.txt"],
        programas.Select(x => x.Nome).ToList(), programas.Select(p => escolhidos.Any(e => e.Nome == p.Nome)).ToArray());
    return marcados == null ? null : programas.Where((_, k) => marcados[k]).ToList();
}

string? PerguntarUsuario(string titulo, string? atual)
{
    while (true)
    {
        var usuario = Tui.Perguntar(titulo, ["Nome da conta local do Windows (ex.: Joao).", "Até 20 letras, sem \\ / [ ] : ; | = , + * ? < > \""],
            "Nome do usuário", inicial: atual ?? "");
        if (usuario == null) return null;
        if (usuario.Length > 0 && usuario.Length <= 20 && usuario.IndexOfAny("\\/[]:;|=,+*?<>\"".ToCharArray()) < 0) return usuario;
        Tui.Mensagem("Nome inválido", ["Use até 20 letras, sem \\ / [ ] : ; | = , + * ? < > \""], erro: true);
    }
}

// Nome do computador: "" = o Windows escolhe; null = voltou.
string? PerguntarNomePc(string titulo, string? atual)
{
    while (true)
    {
        var n = Tui.Perguntar(titulo, ["Como o PC aparece na rede (ex.: JOAO-ESCRITORIO).", "Até 15 letras, números ou hífen. Em branco: o Windows escolhe."],
            "Nome do computador", inicial: atual ?? "");
        if (n == null || n.Length == 0) return n;
        if (Unattend.NomePcValido(n)) return n.ToUpperInvariant();
        Tui.Mensagem("Nome inválido", ["Use até 15 letras, números ou hífen (sem espaço), e não só números."], erro: true);
    }
}

bool EstePc(Perfil p) => Sistema.MesmoPc(p.Pc, p.Serie, uuidPc, seriePc);

// Licença do PC: a do pendrive, digitada (a deste PC) ou nenhuma. Voltou = Esc.
(bool Voltou, string? Chave) EscolherLicenca(string titulo, string? atual)
{
    var doPendrive = LicencaDoPendrive();
    while (true)
    {
        var opcoes = new List<string>();
        if (doPendrive != null) opcoes.Add($"Usar a licença do pendrive (final {doPendrive[^5..]})");
        opcoes.Add(atual != null && atual != doPendrive ? $"Digitar a licença deste PC (atual: final {atual[^5..]})" : "Digitar a licença deste PC");
        opcoes.Add("Sem licença (o Windows ativa sozinho se o PC tiver licença de fábrica)");
        int sel = atual == null ? opcoes.Count - 1 : atual == doPendrive ? 0 : opcoes.Count - 2;
        int r = Tui.Menu(titulo, ["A licença precisa ser da mesma edição (Home ou Pro).", "Fica guardada no perfil deste PC, se você salvar o perfil."], opcoes, sel);
        if (r < 0) return (true, atual);
        if (r == opcoes.Count - 1) return (false, null);
        if (doPendrive != null && r == 0) return (false, doPendrive);
        var digitada = Tui.Perguntar("Licença deste PC", ["Formato: XXXXX-XXXXX-XXXXX-XXXXX-XXXXX."], "Chave do produto", inicial: atual ?? "");
        if (digitada == null) continue;
        if (Unattend.ChaveValida(digitada) is string valida) return (false, valida);
        Tui.Mensagem("Licença inválida", ["Use o formato XXXXX-XXXXX-XXXXX-XXXXX-XXXXX (25 letras e números)."], erro: true);
    }
}

// Sugestão de nome do computador a partir do usuário: "João" vira "JOAO-PC".
// Nome aleatório no estilo do Windows (PC-7K2P9QX), sem o nome do usuário; sem letras que se confundem (O/0, I/1)
static string SugestaoNomePc() =>
    "PC-" + string.Concat(Enumerable.Range(0, 7).Select(_ => "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"[Random.Shared.Next(32)]));

// Último backup de drivers deste mesmo PC (pelas identidades de fábrica): serve quando o disco não tem Windows.
(string Pasta, string Data)? UltimoBackupDeDrivers()
{
    if (raiz == null || !pcIdentificado || !Directory.Exists(Path.Combine(raiz, "Backups"))) return null;
    foreach (var pc in Directory.GetFiles(Path.Combine(raiz, "Backups"), "pc.txt", SearchOption.AllDirectories)
                 .OrderByDescending(f => Path.GetFileName(Path.GetDirectoryName(f)))) // pasta com a data: a mais nova primeiro
    {
        var linhas = File.ReadAllLines(pc);
        string? Campo(string nome) => linhas.FirstOrDefault(l => l.StartsWith(nome + "="))?[(nome.Length + 1)..] is { Length: > 0 } v ? v : null;
        var uuid = Campo("uuid") ?? (linhas.Length > 0 && Guid.TryParse(linhas[0], out _) ? linhas[0] : null); // versão antiga: só o UUID
        var drivers = Path.Combine(Path.GetDirectoryName(pc)!, "Drivers");
        if (Sistema.MesmoPc(uuid, Campo("serie"), uuidPc, seriePc) && Directory.Exists(drivers)
            && Directory.EnumerateFiles(drivers, "*.inf", SearchOption.AllDirectories).Any())
            return (drivers, Path.GetFileName(Path.GetDirectoryName(pc))!);
    }
    return null;
}

// Licença gravada pelo MontaWin.exe (MontaWin\licenca.txt); null se não houver ou estiver errada.
string? LicencaDoPendrive()
{
    var arquivo = raiz == null ? null : Path.Combine(raiz, "licenca.txt");
    return File.Exists(arquivo) ? Unattend.ChaveValida(File.ReadAllText(arquivo)) : null;
}

// Só os nomes das redes que o Windows antigo conhecia: as senhas ficam protegidas pelo Windows e não são lidas.
List<string> RedesConhecidas(Disco? d)
{
    var pasta = d?.LetraWindows is char l ? $@"{l}:\ProgramData\Microsoft\Wlansvc\Profiles\Interfaces" : null;
    if (pasta == null || !Directory.Exists(pasta)) return [];
    return Directory.GetFiles(pasta, "*.xml", SearchOption.AllDirectories)
        .Select(f =>
        {
            try { return System.Xml.Linq.XDocument.Load(f).Descendants().FirstOrDefault(e => e.Name.LocalName == "name")?.Value; }
            catch (Exception) { return null; }
        })
        .OfType<string>().Distinct().ToList();
}

// Wi-Fi do Windows novo: rede conhecida do Windows antigo, outra digitada ou nenhuma (Rede null). null = voltou.
(string? Rede, string Senha)? EscolherWifi(Disco? antigo, string? atual)
{
    var conhecidas = RedesConhecidas(antigo);
    var opcoes = conhecidas.Select(r => $"Wi-Fi: {r}").Append("Outra rede Wi-Fi (digitar o nome)").Append("Sem Wi-Fi (cabo de rede, ou conectar depois)").ToList();
    while (true)
    {
        int sel = atual == null ? opcoes.Count - 1 : conhecidas.Contains(atual) ? conhecidas.IndexOf(atual) : conhecidas.Count;
        int i = Tui.Menu("Internet no Windows novo", [
            "Com o Wi-Fi configurado, o Windows já liga conectado e os programas da internet instalam antes da tela de login.",
            "As telas iniciais da Microsoft continuam puladas, com ou sem rede."], opcoes, sel);
        if (i < 0) return null;
        if (i == opcoes.Count - 1) return (null, "");
        var nome = i < conhecidas.Count ? conhecidas[i]
            : Tui.Perguntar("Rede Wi-Fi", ["Nome da rede exatamente como aparece (maiúsculas e minúsculas contam)."], "Nome da rede", inicial: atual ?? "");
        if (string.IsNullOrWhiteSpace(nome)) continue;
        var senha = Tui.Perguntar($"Senha do Wi-Fi {nome}", ["Deixe em branco se a rede for aberta.",
            "A senha é apagada do Windows novo assim que a rede é configurada."], "Senha do Wi-Fi", oculto: true);
        if (senha == null) continue;
        return (nome.Trim(), senha);
    }
}

string? PerguntarSenha(string titulo, string usuario) => Tui.Perguntar(titulo, [$"Senha da conta {usuario}.", "Deixe em branco para entrar sem senha."],
    "Senha", oculto: true);

static string DescreverInstalacao(string instalacao) => instalacao switch
{
    "particao" => "partição existente",
    var d when d.StartsWith("dividir:") => $"Windows {d["dividir:".Length..]} GB + Dados",
    _ => "apagar o disco inteiro",
};

void MenuPerfis()
{
    if (!Exigir(raiz != null, "Pendrive MontaWin não encontrado (os perfis ficam em MontaWin\\Perfis).")) return;
    int sel = 0;
    while (true)
    {
        var perfis = LerPerfis();
        var opcoes = perfis.Select(x => $"{x.Nome}  ({x.Edicao} · {DescreverInstalacao(x.Instalacao)})" + (EstePc(x) ? "  ← este PC" : "")).Prepend("+ Adicionar perfil").ToList();
        sel = Tui.Menu("Perfis", ["Cada perfil guarda as respostas da instalação de um PC.", "Com um perfil, a instalação só pede o disco e a confirmação."], opcoes, sel);
        if (sel < 0) return;
        if (sel == 0) { AdicionarPerfil(); continue; }
        var perfil = perfis[sel - 1];
        var detalhes = perfil.Texto().Split("\r\n").Where(l => l.Length > 0 && !l.StartsWith('#') && !l.StartsWith("Senha") && !l.EndsWith("= ")).ToList();
        detalhes.Add(perfil.Senha == null ? "Senha: perguntada na hora" : "Senha: guardada no perfil");
        if (Tui.Menu($"Perfil {perfil.Nome}", detalhes, ["Apagar este perfil"]) == 0
            && Tui.Confirmar("Apagar perfil", [$"Apagar o perfil {perfil.Nome}? Os backups dele continuam no pendrive."], "Apagar"))
        {
            File.Delete(ArquivoPerfil(perfil.Nome));
            if (perfilDoPc?.Nome == perfil.Nome) perfilDoPc = null;
        }
    }
}

void AdicionarPerfil()
{
    var nomesEdicoes = LerEdicoes().Select(x => x.Nome).Distinct().ToList();
    if (!Exigir(nomesEdicoes.Count > 0, "Nenhuma imagem .wim na pasta MontaWin\\Imagens.")) return;
    var programas = LerProgramas();
    string nome = "";
    int edicao = 0, modo = 0, configuracao = 0, guardar = 0, gb = 120;
    string? usuario = null, senha = null;
    var escolhidos = new List<Programa>();
    bool restaurar = true, drivers = true, otimizar = true, removerOneDrive = true, altoDesempenho = false, vincular = true, pontoRestauracao = true;
    string? chave = null;
    string? nomePc = null, wifi = null, senhaWifi = null;

    if (!Assistente(
        _ =>
        {
            while (true)
            {
                var n = Tui.Perguntar("Novo perfil", ["Um perfil guarda as respostas da instalação de um PC."], "Nome do perfil (ex.: PC Escritório)", inicial: nome);
                if (n == null) return Passo.Voltar;
                if (Perfil.NomeSeguro(n).Length == 0) { Tui.Mensagem("Nome inválido", ["Use letras e números."], erro: true); continue; }
                if (File.Exists(ArquivoPerfil(n)) && !Tui.Confirmar("Perfil já existe", [$"Já existe um perfil {n}. Substituir?"])) continue;
                nome = n.Trim();
                return Passo.Avancar;
            }
        },
        _ => Escolha(Tui.Menu($"{nome}: qual Windows", [], nomesEdicoes, edicao), ref edicao),
        _ => Escolha(Tui.Menu($"{nome}: como instalar", [], ["Apagar o disco inteiro (automático)", "Apagar o disco e dividir: Windows + Dados",
            "Instalar numa partição existente (a partição é escolhida na hora)"], modo), ref modo),
        _ =>
        {
            if (modo != 1) return Passo.Pular;
            while (true)
            {
                var r = Tui.Perguntar($"{nome}: tamanho do Windows (C:)", ["O resto do disco vira a partição Dados.", "Recomendado: 120 GB ou mais."],
                    "Tamanho do Windows em GB", inicial: gb.ToString());
                if (r == null) return Passo.Voltar;
                if (int.TryParse(r, out gb) && gb >= 40) return Passo.Avancar;
                Tui.Mensagem("Tamanho inválido", ["Use pelo menos 40 GB."], erro: true);
            }
        },
        _ => Escolha(Tui.Menu($"{nome}: configuração inicial", [], ["Automática: criar conta local e pular as telas iniciais (recomendado)",
            "Normal: as telas da Microsoft aparecem no primeiro uso"], configuracao), ref configuracao),
        _ =>
        {
            if (configuracao != 0) { usuario = null; return Passo.Pular; }
            usuario = PerguntarUsuario($"{nome}: usuário", usuario);
            return usuario == null ? Passo.Voltar : Passo.Avancar;
        },
        _ => usuario == null ? Passo.Pular : Escolha(Tui.Menu($"{nome}: senha", [], ["Perguntar a senha na hora de instalar (mais seguro)",
            "Guardar a senha no perfil (mais rápido; fica escrita no pendrive)"], guardar), ref guardar),
        _ =>
        {
            if (usuario == null || guardar == 0) { senha = null; return Passo.Pular; }
            senha = PerguntarSenha($"{nome}: senha", usuario);
            return senha == null ? Passo.Voltar : Passo.Avancar;
        },
        _ =>
        {
            if (usuario == null) { nomePc = null; return Passo.Pular; }
            var n = PerguntarNomePc($"{nome}: nome do computador", nomePc);
            if (n == null) return Passo.Voltar;
            nomePc = n.Length == 0 ? null : n;
            return Passo.Avancar;
        },
        _ =>
        {
            var r = Tui.Perguntar($"{nome}: Wi-Fi", ["Rede Wi-Fi que o Windows novo usa (exatamente como aparece).", "Em branco: sem Wi-Fi (cabo de rede)."],
                "Nome da rede", inicial: wifi ?? "");
            if (r == null) return Passo.Voltar;
            wifi = r.Length == 0 ? null : r;
            return Passo.Avancar;
        },
        _ =>
        {
            // Mesma regra da senha da conta: só fica no perfil se escolheu guardar.
            if (wifi == null || guardar == 0) { senhaWifi = null; return Passo.Pular; }
            senhaWifi = Tui.Perguntar($"{nome}: senha do Wi-Fi {wifi}", ["Deixe em branco se a rede for aberta."], "Senha do Wi-Fi", oculto: true);
            return senhaWifi == null ? Passo.Voltar : Passo.Avancar;
        },
        _ =>
        {
            var (voltou, c) = EscolherLicenca($"{nome}: licença do Windows", chave);
            if (voltou) return Passo.Voltar;
            chave = c;
            return Passo.Avancar;
        },
        _ =>
        {
            if (usuario == null) { escolhidos = []; return Passo.Pular; }
            var e = EscolherProgramas($"{nome}: programas", programas, escolhidos);
            if (e == null) return Passo.Voltar;
            escolhidos = e;
            return Passo.Avancar;
        },
        _ => SimNao(Tui.Menu($"{nome}: backup de arquivos", [], ["Restaurar no Windows novo quando houver backup (recomendado)", "Não restaurar"], restaurar ? 0 : 1), ref restaurar),
        _ => SimNao(Tui.Menu($"{nome}: backup de drivers", [], ["Fazer sozinho antes de formatar e reinstalar (recomendado)", "Não fazer"], drivers ? 0 : 1), ref drivers),
        _ => SimNao(Tui.Menu($"{nome}: otimização", ["Remove apps inúteis e propaganda, desliga Widgets e telemetria extra.",
            "Segurança e atualizações do Windows não são alteradas."], ["Otimizar o Windows (recomendado)", "Windows como a Microsoft entrega"], otimizar ? 0 : 1), ref otimizar),
        _ =>
        {
            var m = Tui.Selecao($"{nome}: ajustes opcionais", [], ["Remover o OneDrive", "Modo de energia Alto desempenho (melhor em PC de mesa)",
                "Ponto de restauração \"Windows recém-instalado\" (usa 2 a 5 GB do disco)"], [removerOneDrive, altoDesempenho, pontoRestauracao]);
            if (m == null) return Passo.Voltar;
            (removerOneDrive, altoDesempenho, pontoRestauracao) = (m[0], m[1], m[2]);
            return Passo.Avancar;
        },
        _ =>
        {
            // Sem identidade de fábrica, vincular confundiria PCs iguais: o perfil é escolhido na hora.
            if (!pcIdentificado) { vincular = false; return Passo.Pular; }
            return SimNao(Tui.Menu($"{nome}: este PC", [$"PC atual: {Sistema.Resumo()}", $"Identificado pela placa-mãe{(seriePc != null ? $" e pelo número de série {seriePc}" : "")}."],
                ["Vincular a este PC: o painel reconhece este PC e já oferece o perfil ao ligar", "Não vincular (perfil para usar em qualquer PC)"], vincular ? 0 : 1), ref vincular);
        }))
        return;

    var perfil = new Perfil(nome)
    {
        Pc = vincular ? uuidPc : null,
        Serie = vincular ? seriePc : null,
        Modelo = vincular ? Sistema.Modelo() : null,
        Edicao = nomesEdicoes[edicao],
        Instalacao = modo == 2 ? "particao" : modo == 1 ? $"dividir:{gb}" : "apagar",
        Usuario = usuario,
        Senha = senha,
        Programas = escolhidos.Select(x => x.Nome).ToArray(),
        Restaurar = restaurar,
        BackupDrivers = drivers,
        Otimizar = otimizar,
        RemoverOneDrive = removerOneDrive,
        AltoDesempenho = altoDesempenho,
        NomePc = nomePc,
        Wifi = wifi,
        SenhaWifi = senhaWifi,
        Chave = chave,
        PontoRestauracao = pontoRestauracao,
    };
    Directory.CreateDirectory(Path.Combine(raiz!, "Perfis"));
    File.WriteAllText(ArquivoPerfil(perfil.Nome), perfil.Texto());
    if (EstePc(perfil)) perfilDoPc = perfil;
    Comandos.Registrar($"Perfil salvo: {perfil.Nome}");
    Tui.Mensagem("Perfil salvo", [$"Perfil {perfil.Nome} salvo em {ArquivoPerfil(perfil.Nome)}.", "Use em \"Formatar e instalar\"."]);
}

// Resposta de um menu dentro do assistente: guarda a escolha e avança, ou volta.
static int Escolha(int resposta, ref int escolha)
{
    if (resposta < 0) return Passo.Voltar;
    escolha = resposta;
    return Passo.Avancar;
}

static int SimNao(int resposta, ref bool sim)
{
    if (resposta < 0) return Passo.Voltar;
    sim = resposta == 0;
    return Passo.Avancar;
}

// inicial: perfil já escolhido (PC reconhecido na abertura): o assistente começa direto no disco.
void Instalar(Perfil? inicial = null)
{
    if (!Exigir(raiz != null, "Pendrive MontaWin não encontrado (pasta MontaWin\\Imagens).")) return;
    var edicoes = LerEdicoes();
    if (!Exigir(edicoes.Count > 0, "Nenhuma imagem .wim na pasta MontaWin\\Imagens.")) return;
    var perfis = LerPerfis().OrderByDescending(EstePc).ToList();
    var programas = LerProgramas();
    bool uefi = Discos.Uefi;

    Perfil? perfil = null;
    Edicao edicao = edicoes[0];
    Disco disco = null!;
    int modo = 0, configuracao = 0;
    PlanoParticoes plano = null!;
    Particao? particao = null;
    int? windowsGb = null;
    var extras = new List<Edicao>(); // multi boot: os outros Windows, na ordem escolhida
    var tamanhos = new List<int>();
    string? usuario = null;
    string senha = "";
    var escolhidos = new List<Programa>();
    bool otimizar = true, removerOneDrive = true, altoDesempenho = false, restaurar = pastaArquivos != null, pontoRestauracao = true;
    string? nomePc = null, rede = null, chave = LicencaDoPendrive();
    string senhaRede = "";
    bool recomendado = true;
    (string Pasta, string Data)? driversAnteriores = null;
    // A imagem é conferida em segundo plano enquanto as perguntas são respondidas: no resumo, já está pronta.
    var conferencias = new Dictionary<string, Task<bool?>>();
    void Conferir(Edicao e)
    {
        if (!conferencias.ContainsKey(e.Arquivo)) conferencias[e.Arquivo] = Task.Run(() => Wim.Conferir(e.Arquivo));
    }
    Edicao? velocidadeMedida = null;
    string? avisoLento = null;

    string? BackupNoDisco() => backupsFeitos.FirstOrDefault(b => Discos.DiscoDaLetra(b[0]) == disco.Numero);

    if (!Assistente(
        // Perfil: o deste PC vem primeiro e já selecionado
        _ =>
        {
            void Aplicar(Perfil p) =>
                (otimizar, removerOneDrive, altoDesempenho, restaurar, pontoRestauracao) =
                    (p.Otimizar, p.RemoverOneDrive, p.AltoDesempenho, p.Restaurar && pastaArquivos != null, p.PontoRestauracao);
            if (inicial != null)
            {
                (perfil, inicial) = (inicial, null); // só na primeira passada; voltando aqui, mostra a lista
                Aplicar(perfil);
                return Passo.Pular;
            }
            if (perfis.Count == 0) return Passo.Pular;
            var opcoes = perfis.Select(x => $"{x.Nome}  ({x.Edicao} · {DescreverInstalacao(x.Instalacao)})" + (EstePc(x) ? "  ← este PC" : ""))
                .Append("Sem perfil: responder tudo agora").ToList();
            while (true)
            {
                int e = Tui.Menu("Usar um perfil?", ["Com um perfil, as respostas já vêm prontas: só falta o disco e a confirmação."], opcoes,
                    perfil != null ? perfis.IndexOf(perfil) : EstePc(perfis[0]) ? 0 : perfis.Count);
                if (e < 0) return Passo.Voltar;
                var p = e < perfis.Count ? perfis[e] : null;
                if (p != null && (p.Pc != null || p.Serie != null) && !EstePc(p)
                    && !Tui.Confirmar("Perfil de outro PC", [$"O perfil {p.Nome} é do PC {p.Modelo}. Este é {Sistema.Modelo()}.", "Os drivers do backup podem não servir. Usar mesmo assim?"]))
                    continue;
                perfil = p;
                if (perfil != null) Aplicar(perfil);
                return Passo.Avancar;
            }
        },
        sentido =>
        {
            if (perfil != null)
            {
                if (sentido < 0) return Passo.Pular;
                var doPerfil = edicoes.FirstOrDefault(x => x.Nome == perfil.Edicao);
                if (!Exigir(doPerfil != null, $"A edição \"{perfil.Edicao}\" do perfil não está nas imagens do pendrive.")) return Passo.Voltar;
                edicao = doPerfil!;
                Conferir(edicao);
                return Passo.Avancar;
            }
            if (edicoes.Count == 1) { Conferir(edicao); return Passo.Pular; } // só um Windows no pendrive: nada a escolher
            int e = Tui.Menu("Qual Windows instalar?", [], edicoes.Select(x => $"{x.Nome}  ({Path.GetFileName(x.Arquivo)})").ToList(), edicoes.IndexOf(edicao));
            if (e < 0) return Passo.Voltar;
            edicao = edicoes[e];
            Conferir(edicao);
            return Passo.Avancar;
        },
        _ =>
        {
            var d = EscolherDisco("Em qual disco instalar?", disco ?? alvo);
            if (d == null) return Passo.Voltar;
            disco = d;
            // Disco sem Windows (novo ou trocado): os drivers vêm do último backup deste mesmo PC, se houver.
            driversAnteriores = disco.LetraWindows == null && pastaDrivers == null ? UltimoBackupDeDrivers() : null;
            return Passo.Avancar;
        },
        // Disco com Windows e sem backup: oferece antes de formatar (depois não tem volta)
        _ =>
        {
            if (disco.LetraWindows == null || (pastaArquivos != null && (pastaDrivers != null || perfil?.BackupDrivers == true))) return Passo.Pular;
            int r = Tui.Menu("Fazer backup antes de formatar?", [$"O disco tem um Windows em {disco.LetraWindows}:. Depois de formatar, os arquivos não voltam.",
                pastaDrivers != null ? "Drivers: backup feito." : "Drivers: backup não feito.", pastaArquivos != null ? "Arquivos: backup feito." : "Arquivos: backup não feito."],
                ["Sim: escolher o que salvar (recomendado)", "Não precisa"]);
            if (r < 0) return Passo.Voltar;
            if (r == 0 && !BackupCompleto(disco)) return Passo.Voltar;
            restaurar = pastaArquivos != null && (perfil?.Restaurar ?? true);
            return Passo.Avancar;
        },
        _ =>
        {
            if (perfil != null) return Passo.Pular;
            int r = Tui.Menu("Como configurar o Windows?", [
                "Recomendado: conta local sem as telas da Microsoft, Windows otimizado e sem OneDrive, ponto de restauração",
                "e o backup de arquivos de volta. Você ainda escolhe a partição, a conta, o Wi-Fi, a licença e os programas."],
                ["Recomendado (menos perguntas)", "Personalizar: escolher cada opção"], recomendado ? 0 : 1);
            if (r < 0) return Passo.Voltar;
            recomendado = r == 0;
            if (recomendado)
                (configuracao, otimizar, removerOneDrive, altoDesempenho, pontoRestauracao, restaurar) = (0, true, true, false, true, pastaArquivos != null);
            return Passo.Avancar;
        },
        sentido =>
        {
            if (perfil != null)
            {
                if (sentido < 0) return Passo.Pular;
                modo = perfil.Instalacao == "particao" ? 2 : perfil.Instalacao.StartsWith("dividir:") ? 1 : 0;
                return Passo.Avancar;
            }
            if (disco.Particoes.Count == 0) { modo = 0; return Passo.Pular; } // disco vazio: não há o que manter
            return Escolha(Tui.Menu($"Como instalar no {Compacto(disco.Descricao)}?", disco.Particoes.Select(p => p.Descricao).ToList(), [
                "Apagar o disco inteiro (automático)",
                "Apagar o disco e dividir: Windows + Dados",
                "Instalar numa partição existente (mantém as outras)",
                "Vários Windows no mesmo disco (multi boot)",
            ], modo), ref modo);
        },
        // Partições: o plano do diskpart conforme o modo
        sentido =>
        {
            var letras = Comandos.LetrasLivres(3);
            if (!Exigir(letras.Length == 3, "Não há letras de unidade livres.")) return Passo.Sair;
            bool automatico = modo == 0 || (modo == 1 && perfil != null);
            if (automatico && sentido < 0) return Passo.Pular;
            if (modo != 2 && !Exigir(BackupNoDisco() == null, $"O backup em {BackupNoDisco()} está nesse disco e seria apagado. Escolha outro disco.")) return Passo.Voltar;
            (particao, windowsGb) = (null, null);
            extras.Clear();
            if (modo == 0)
                plano = Particionamento.ApagarDisco(disco.Numero, uefi, letras[0], letras[1], letras[2]);
            else if (modo == 1)
            {
                long gbDisco = disco.Tamanho / (1L << 30);
                bool perguntar = perfil == null;
                while (true)
                {
                    var resposta = !perguntar ? perfil!.Instalacao["dividir:".Length..] : Tui.Perguntar("Tamanho do Windows (C:)", [
                        $"O disco tem {gbDisco} GB. O resto vira a partição Dados (D:), que você pode manter nas próximas formatações.",
                        "Recomendado: 120 GB ou mais."], "Tamanho do Windows em GB", inicial: (tamanhos.FirstOrDefault() is > 0 and var t ? t : 120).ToString());
                    if (resposta == null) return Passo.Voltar;
                    if (int.TryParse(resposta, out int gb) && gb >= 40 && gb <= gbDisco - 5) { windowsGb = gb; tamanhos = [gb]; break; }
                    // O tamanho do perfil não cabe neste disco: pergunta em vez de travar a instalação
                    Tui.Mensagem("Tamanho inválido", [$"Use de 40 a {gbDisco - 5} GB para este disco."], erro: true);
                    perguntar = true;
                }
                plano = Particionamento.ApagarDisco(disco.Numero, uefi, letras[0], letras[1], letras[2], windowsGb);
            }
            else if (modo == 2)
            {
                if (!Exigir(disco.Particoes.Count > 0, "O disco não tem partições. Use uma das opções que apagam o disco.")) return Passo.Voltar;
                while (true)
                {
                    int i = Tui.Menu("Em qual partição instalar?", ["Só a partição escolhida é apagada; as outras ficam intactas."], disco.Particoes.Select(p => p.Descricao).ToList());
                    if (i < 0) return Passo.Voltar;
                    var p = disco.Particoes[i];
                    var (encontrado, erro) = Particionamento.ParticaoExistente(disco, p, uefi, letras[0], letras[1], letras[2]);
                    if (encontrado == null) { Tui.Mensagem("Essa partição não serve", [erro], erro: true); continue; }
                    if (p.Tamanho < 32L << 30) { Tui.Mensagem("Partição pequena", ["A partição precisa ter pelo menos 32 GB."], erro: true); continue; }
                    if (backupsFeitos.FirstOrDefault(b => p.Letra is char l && char.ToUpperInvariant(b[0]) == l) is string noBackup)
                    {
                        Tui.Mensagem("Backup nessa partição", [$"O backup em {noBackup} está nessa partição e seria apagado."], erro: true);
                        continue;
                    }
                    (plano, particao) = (encontrado, p);
                    break;
                }
            }
            else
            {
                int maximo = uefi ? 4 : 2; // MBR: no máximo 4 partições principais (inicialização + Windows)
                long gbDisco = disco.Tamanho / (1L << 30);
                while (true)
                {
                    var marcados = Tui.Selecao($"Multi boot: marque de 2 a {maximo} Windows", ["O primeiro marcado fica como padrão no menu de boot."],
                        edicoes.Select(x => $"{x.Nome}  ({Path.GetFileName(x.Arquivo)})").ToList(), edicoes.Select(x => x == edicao || extras.Contains(x)).ToArray());
                    if (marcados == null) return Passo.Voltar;
                    var escolhidas = edicoes.Where((_, k) => marcados[k]).ToList();
                    if (escolhidas.Count < 2 || escolhidas.Count > maximo) { Tui.Mensagem("Multi boot", [$"Marque de 2 a {maximo} Windows."], erro: true); continue; }
                    var novos = new List<int>();
                    for (int k = 0; k < escolhidas.Count - 1; k++)
                    {
                        var resposta = Tui.Perguntar($"Tamanho de {escolhidas[k].Nome}", [$"O disco tem {gbDisco} GB. Quantos GB para este Windows?",
                            $"O último ({escolhidas[^1].Nome}) fica com o resto."], "Tamanho em GB", inicial: "120");
                        if (resposta == null) break;
                        if (!int.TryParse(resposta, out int gb) || gb < 40) { Tui.Mensagem("Tamanho inválido", ["Use pelo menos 40 GB."], erro: true); k--; continue; }
                        novos.Add(gb);
                    }
                    if (novos.Count < escolhidas.Count - 1) continue;
                    if (novos.Sum() + 40 > gbDisco) { Tui.Mensagem("Não cabe", [$"Sobram menos de 40 GB para {escolhidas[^1].Nome}."], erro: true); continue; }
                    var letrasMulti = Comandos.LetrasLivres(escolhidas.Count + 1);
                    if (!Exigir(letrasMulti.Length == escolhidas.Count + 1, "Não há letras de unidade livres.")) return Passo.Sair;
                    plano = Particionamento.ApagarDiscoMultiboot(disco.Numero, uefi, letrasMulti[0], letrasMulti[1..], novos);
                    (edicao, tamanhos) = (escolhidas[0], novos);
                    extras.AddRange(escolhidas.Skip(1));
                    break;
                }
            }
            return Passo.Avancar;
        },
        sentido =>
        {
            if (perfil != null)
            {
                if (sentido < 0) return Passo.Pular;
                usuario = perfil.Usuario;
                configuracao = usuario != null ? 0 : 1;
                return Passo.Avancar;
            }
            if (recomendado) { configuracao = 0; return Passo.Pular; }
            return Escolha(Tui.Menu("Configuração inicial do Windows", [], [
                "Automática: criar conta local e pular as telas iniciais (recomendado)",
                "Normal: o cliente responde as telas da Microsoft no primeiro uso",
            ], configuracao), ref configuracao);
        },
        _ =>
        {
            if (perfil != null) return Passo.Pular;
            if (configuracao != 0) { usuario = null; return Passo.Pular; }
            usuario = PerguntarUsuario("Nome do usuário", usuario);
            return usuario == null ? Passo.Voltar : Passo.Avancar;
        },
        _ =>
        {
            if (usuario == null) { senha = ""; return Passo.Pular; }
            if (perfil?.Senha != null) { senha = perfil.Senha; return Passo.Pular; }
            var s = PerguntarSenha("Senha do usuário", usuario);
            if (s == null) return Passo.Voltar;
            senha = s;
            return Passo.Avancar;
        },
        _ =>
        {
            if (usuario == null) { nomePc = null; return Passo.Pular; }
            if (perfil != null) { nomePc = perfil.NomePc; return Passo.Pular; }
            var n = PerguntarNomePc("Nome do computador", nomePc ?? SugestaoNomePc());
            if (n == null) return Passo.Voltar;
            nomePc = n.Length == 0 ? null : n;
            return Passo.Avancar;
        },
        _ =>
        {
            if (perfil != null)
            {
                (rede, senhaRede) = (perfil.Wifi, perfil.SenhaWifi ?? "");
                if (rede == null || perfil.SenhaWifi != null) return Passo.Pular;
                var sw = Tui.Perguntar($"Senha do Wi-Fi {rede}", ["Rede do perfil. Deixe em branco se a rede for aberta."], "Senha do Wi-Fi", oculto: true);
                if (sw == null) return Passo.Voltar;
                senhaRede = sw;
                return Passo.Avancar;
            }
            var escolha = EscolherWifi(disco, rede);
            if (escolha == null) return Passo.Voltar;
            (rede, senhaRede) = escolha.Value;
            return Passo.Avancar;
        },
        _ =>
        {
            if (perfil != null) { chave = perfil.Chave; return Passo.Pular; }
            var (voltou, c) = EscolherLicenca("Licença do Windows", chave);
            if (voltou) return Passo.Voltar;
            chave = c;
            return Passo.Avancar;
        },
        // Programas no primeiro login: só com a configuração automática, que roda o script do primeiro login.
        _ =>
        {
            if (usuario == null) { escolhidos = []; return Passo.Pular; }
            if (perfil != null) { escolhidos = programas.Where(x => perfil.Programas.Contains(x.Nome)).ToList(); return Passo.Pular; }
            var e = EscolherProgramas("Quais programas instalar?", programas, escolhidos);
            if (e == null) return Passo.Voltar;
            escolhidos = e;
            return Passo.Avancar;
        },
        _ => perfil != null || recomendado ? Passo.Pular : SimNao(Tui.Menu("Otimizar o Windows?", [
            "Remove apps inúteis e propaganda (Notícias, Xbox, Paciência, Clipchamp...), bloqueia apps sugeridos,",
            "desliga Widgets, pesquisa do Iniciar só no PC, telemetria no mínimo.",
            "Defender, firewall, Windows Update e Microsoft Store continuam como a Microsoft entrega."],
            ["Sim: otimizar (recomendado)", "Não: Windows como a Microsoft entrega"], otimizar ? 0 : 1), ref otimizar),
        _ =>
        {
            if (perfil != null || recomendado) return Passo.Pular;
            var m = Tui.Selecao("Ajustes opcionais", [], [
                "Remover o OneDrive",
                "Modo de energia Alto desempenho (melhor em PC de mesa; em notebook gasta mais bateria e esquenta)",
                "Ponto de restauração \"Windows recém-instalado\" depois dos programas (usa 2 a 5 GB do disco)",
            ], [removerOneDrive, altoDesempenho, pontoRestauracao]);
            if (m == null) return Passo.Voltar;
            (removerOneDrive, altoDesempenho, pontoRestauracao) = (m[0], m[1], m[2]);
            return Passo.Avancar;
        },
        _ => pastaArquivos == null || perfil != null || recomendado ? Passo.Pular
            : SimNao(Tui.Menu("Restaurar o backup de arquivos no Windows novo?", [$"Backup: {pastaArquivos}"], [
                "Sim: copiar os arquivos para o Windows novo (recomendado)",
                "Não: deixar só no backup (dá para restaurar depois pelo menu)",
            ], restaurar ? 0 : 1), ref restaurar),
        // Resumo: tudo numa tela, com Voltar para corrigir
        _ =>
        {
            if (velocidadeMedida != edicao)
            {
                foreach (var e in extras.Prepend(edicao).DistinctBy(x => x.Arquivo))
                {
                    Conferir(e);
                    var conferencia = conferencias[e.Arquivo];
                    if (!conferencia.IsCompleted) Tui.Etapa("Conferindo", $"Terminando de conferir a imagem do Windows ({Path.GetFileName(e.Arquivo)})...");
                    if (conferencia.Result == false)
                    {
                        Tui.Mensagem("Imagem do Windows corrompida", [$"O arquivo {Path.GetFileName(e.Arquivo)} no pendrive está com defeito.",
                            "Nada foi apagado. Grave o pendrive de novo no MontaWin.exe (ou troque o pendrive, se repetir)."], erro: true);
                        return Passo.Sair;
                    }
                }
                Tui.Etapa("Conferindo", "Verificando a velocidade do pendrive...");
                avisoLento = Sistema.AvisoPendriveLento(Sistema.VelocidadeLeitura(edicao.Arquivo), new FileInfo(edicao.Arquivo).Length);
                velocidadeMedida = edicao;
            }
            int r = Tui.Menu("Confira antes de começar", Resumo(), ["Formatar e instalar", "Cancelar e voltar ao menu"], 0, "← Voltar e corrigir");
            return r switch { 0 => Passo.Avancar, 1 => Passo.Sair, _ => Passo.Voltar };
        },
        // Último passo: aviso vermelho e digitar SIM
        _ => Tui.Confirmar(modo == 2 ? "Isso vai apagar a partição" : "Isso vai apagar tudo", modo == 2
            ? [$"Só a {Compacto(particao!.Descricao)} será apagada.", "As outras partições do disco ficam intactas. Não tem como desfazer."]
            : [$"TODOS os arquivos, programas e partições do {Compacto(disco.Descricao)} serão apagados.", "Não tem como desfazer."], "Formatar e instalar")
            ? Passo.Avancar : Passo.Voltar))
        return;

    List<string> Resumo()
    {
        var r = new List<string>();
        if (perfil != null) r.Add($"Perfil: {perfil.Nome}");
        r.Add($"Windows: {string.Join(" + ", extras.Prepend(edicao).Select(x => x.Nome))}  ·  {(uefi ? "UEFI (GPT)" : "BIOS (MBR)")}");
        r.Add($"Disco: {Compacto(disco.Descricao)}");
        r.Add("Partições: " + modo switch
        {
            0 => "apagar tudo e criar Windows + recuperação",
            1 => $"Windows {windowsGb} GB + Dados (o resto) + recuperação",
            2 => $"só a {Compacto(particao!.Descricao)}",
            _ => string.Join(" + ", extras.Prepend(edicao).Select((x, k) => k < tamanhos.Count ? $"{x.Nome} ({tamanhos[k]} GB)" : $"{x.Nome} (resto)")),
        });
        r.Add(usuario != null ? $"Conta: {usuario} ({(senha.Length > 0 ? "com senha" : "sem senha")}) · telas iniciais da Microsoft puladas"
                              : "Conta: o cliente cria nas telas da Microsoft");
        if (nomePc != null) r.Add($"Nome do computador: {nomePc}");
        r.Add(rede != null ? $"Internet: Wi-Fi {rede} (o Windows já liga conectado)" : "Internet: cabo de rede, ou conectar depois");
        r.Add(chave != null ? $"Licença: final {chave[^5..]} (ativa antes da tela de login)" : "Licença: nenhuma (ativa sozinho se o PC tiver licença de fábrica)");
        if (usuario != null && pontoRestauracao) r.Add("Ponto de restauração: \"Windows recém-instalado\", depois dos programas");
        if (usuario != null) r.Add("Programas: " + (escolhidos.Count > 0 ? string.Join(", ", escolhidos.Select(x => x.Nome)) : "nenhum"));
        var ajustes = new List<string>();
        if (otimizar) ajustes.Add("otimizado" + (disco.Ssd ? " (SSD: inicialização sem atraso)" : ""));
        if (removerOneDrive) ajustes.Add("sem OneDrive");
        if (altoDesempenho) ajustes.Add("Alto desempenho");
        r.Add("Ajustes: " + (ajustes.Count > 0 ? string.Join(", ", ajustes) : "nenhum (Windows como a Microsoft entrega)"));
        if (perfil?.BackupDrivers == true && disco.LetraWindows != null && pastaDrivers == null) r.Add("Drivers: backup dos básicos antes de formatar, reinstalados depois");
        else if (pastaDrivers != null) r.Add("Drivers: o backup volta no Windows novo");
        else if (driversAnteriores is var (_, data)) r.Add($"Drivers: o último backup deste PC ({data}) volta no Windows novo");
        if (restaurar) r.Add("Arquivos: o backup volta " + (usuario != null ? "para as pastas do usuário novo" : $"em C:\\{Unattend.PastaRestaurados}"));
        var fabrica = disco.Particoes.Where(p => p.Tipo == TipoParticao.Fabricante).ToList();
        if (modo != 2 && fabrica.Count > 0) r.Add($"Atenção: a recuperação de fábrica do fabricante ({Formatar.Tamanho(fabrica.Sum(p => p.Tamanho))}) também será apagada.");
        if (disco.Saude.Nivel == NivelSaude.Falhando) r.Add($"Atenção, DISCO FALHANDO: {disco.Saude.Texto}");
        else if (disco.Saude.Nivel == NivelSaude.Atencao) r.Add($"Atenção, disco com problemas: {disco.Saude.Texto}");
        if (avisoLento != null) r.Add($"Atenção: {avisoLento}");
        if (Sistema.NaBateria) r.Add("Atenção: notebook na bateria. Ligue o carregador.");
        if (disco.LetraWindows != null && pastaDrivers == null && perfil?.BackupDrivers != true) r.Add("Atenção: o backup de drivers deste Windows ainda não foi feito.");
        return r;
    }

    Executar(perfil, edicao, extras, disco, modo, plano, particao, uefi, usuario, senha, escolhidos, otimizar, removerOneDrive, altoDesempenho, restaurar, windowsGb,
        nomePc, rede, senhaRede, chave, pontoRestauracao, Resumo(), pastaDrivers ?? driversAnteriores?.Pasta);
}

void Executar(Perfil? perfil, Edicao edicao, List<Edicao> extras, Disco disco, int modo, PlanoParticoes plano, Particao? particao, bool uefi,
    string? usuario, string senha, List<Programa> escolhidos, bool otimizar, bool removerOneDrive, bool altoDesempenho, bool restaurar, int? windowsGb,
    string? nomePc, string? rede, string senhaRede, string? chave, bool pontoRestauracao, List<string> resumo, string? drivers)
{
    Tui.Etapa("Instalando o Windows", "Preparando..."); // a tela de confirmação não fica parada enquanto calcula
    bool backupDriversAutomatico = perfil?.BackupDrivers == true && disco.LetraWindows != null && pastaDrivers == null;
    var instalacoes = new List<(Edicao Edicao, char Letra)> { (edicao, plano.Windows) };
    instalacoes.AddRange(extras.Zip(plano.WindowsExtras));
    bool multi = instalacoes.Count > 1;
    var bcd = uefi ? $@"{plano.Sistema}:\EFI\Microsoft\Boot\BCD" : $@"{plano.Sistema}:\Boot\BCD";

    // Programas: instaladores baixados no pendrive (arquivo: ou winget offline) vão junto; o resto vem da internet.
    var instaladores = escolhidos.Where(x => x.Instalador != null).Select(x => (AcharInstalador(x)!, Programas.Separar(x.Instalador!).Parametros)).ToList();
    var pastaWinget = Path.Combine(raiz!, "Instaladores", "winget");
    var offline = new List<(string Id, string Pasta, string Comando, bool SoUsuario)>();
    var ids = escolhidos.SelectMany(x => x.Ids).ToList(); // todos: o que falhar sem internet é tentado online
    foreach (var id in ids)
    {
        var pasta = Path.Combine(pastaWinget, id);
        if (Directory.Exists(pasta) && Programas.Offline(pasta, $@"%SystemDrive%\{Unattend.PastaInstaladores}\{id}") is (string comando, bool soUsuario))
            offline.Add((id, pasta, comando, soUsuario));
    }
    bool temProgramas = escolhidos.Count > 0;

    // A tarefa vira uma lista de etapas com peso (tempo aproximado): a tela mostra cada uma e o tempo restante.
    var trabalho = new List<(string Nome, double Peso, Action Fazer)>();
    if (backupDriversAutomatico) trabalho.Add(("Backup dos drivers", 6, () => BackupDrivers(disco, automatico: true)));
    trabalho.Add((modo == 2 ? "Preparando a partição" : "Preparando o disco", 2, () =>
    {
        var scriptDiskpart = Path.Combine(Path.GetTempPath(), "montawin-diskpart.txt");
        File.WriteAllText(scriptDiskpart, plano.Script);
        Tui.Registro(modo == 2 ? $"Formatando a partição {particao!.Numero}" : $"Apagando o disco {disco.Numero} e criando as partições ({(uefi ? "GPT/UEFI" : "MBR/BIOS")})");
        if (Comandos.Executar("diskpart", $"/s \"{scriptDiskpart}\"") != 0) throw new Exception(Falha("O diskpart não conseguiu preparar as partições."));
        Tui.Registro("Partições prontas");
    }));
    // Multi boot: cada bcdboot vira o padrão do menu, então o primeiro escolhido é instalado por último.
    foreach (var (ed, w) in Enumerable.Reverse(instalacoes))
    {
        bool principal = w == plano.Windows;
        string de = multi ? $" ({ed.Nome})" : "";
        trabalho.Add(($"Copiando o Windows{de}", 55, () =>
        {
            Tui.Registro($"Copiando os arquivos do {ed.Nome} para o disco");
            var relogio = Stopwatch.StartNew();
            if (Comandos.Executar("dism", $"/Apply-Image /ImageFile:\"{ed.Arquivo}\" /Index:{ed.Indice} /ApplyDir:{w}:\\", Tui.Progresso) != 0)
                throw new Exception(Falha($"O DISM não conseguiu aplicar {ed.Nome}."));
            Tui.Registro($"{ed.Nome} copiado em {Tui.Duracao(relogio.Elapsed)}");
            if (usuario != null)
            {
                Directory.CreateDirectory($@"{w}:\Windows\Panther");
                File.WriteAllText($@"{w}:\Windows\Panther\unattend.xml", Unattend.Gerar(ed.Idioma, usuario, senha, nomePc));
                Tui.Registro($"Conta local {usuario} preparada; as telas iniciais da Microsoft serão puladas");
            }
        }));
        if (otimizar || removerOneDrive || altoDesempenho)
            trabalho.Add(($"Otimizando{de}", 8, () =>
            {
                int n = Otimizacao.Aplicar(w, disco.Ssd, otimizar, removerOneDrive, altoDesempenho, Tui.Progresso, app => Tui.Registro($"Removido: {app}"));
                if (otimizar) Tui.Registro($"{n} apps removidos · Widgets, propaganda e apps sugeridos desligados · pesquisa só no PC");
                if (otimizar && disco.Ssd) Tui.Registro("SSD: programas da inicialização sem atraso");
                if (removerOneDrive) Tui.Registro("OneDrive não será instalado");
                if (altoDesempenho) Tui.Registro("Modo de energia Alto desempenho ativado");
            }));
        if (drivers != null || backupDriversAutomatico || infsExtras.Length > 0)
            trabalho.Add(($"Instalando os drivers{de}", 6, () =>
            {
                // Driver que falhar não impede a instalação: o Windows Update completa depois.
                foreach (var (pasta, nome) in new[] { (pastaDrivers ?? drivers, "do backup deste PC"), (infsExtras.Length > 0 ? pastaDriversExtras : null, "extras") })
                {
                    if (pasta == null) continue;
                    Tui.Registro($"Instalando {Directory.GetFiles(pasta, "*.inf", SearchOption.AllDirectories).Length} drivers {nome}");
                    Comandos.Executar("dism", $"/Image:{w}:\\ /Add-Driver /Driver:\"{pasta}\" /Recurse", Tui.Progresso);
                }
            }));
        if (principal && restaurar)
            trabalho.Add(("Restaurando os arquivos", Math.Clamp(Backup.Tamanho(pastaArquivos!) / 1e9 * 2, 2, 60), () => Restaurar(pastaArquivos!, w)));
        trabalho.Add(($"Inicialização e primeiro login{de}", 2, () =>
        {
            if (usuario != null)
            {
                // Pastas pessoais (joao\Documents...) voltam para o usuário novo; o resto fica em C:\Arquivos restaurados.
                var pessoais = principal && restaurar
                    ? Directory.GetDirectories(pastaArquivos!).SelectMany(u => Backup.PastasPessoais
                        .Where(p => Directory.Exists(Path.Combine(u, p))).Select(p => $@"{Path.GetFileName(u)}\{p}")).ToList()
                    : new List<string>();
                var script = $@"{w}:\{Unattend.ScriptPrimeiroLogin}";
                Directory.CreateDirectory(Path.GetDirectoryName(script)!);
                File.WriteAllText(script, Unattend.GerarScriptPrimeiroLogin(pessoais, temProgramas, pontoRestauracao));
                if (temProgramas)
                {
                    // Instaladores vão do pendrive para dentro do Windows novo: no primeiro login o pendrive já foi removido.
                    var destino = $@"{w}:\{Unattend.PastaInstaladores}";
                    Directory.CreateDirectory(destino);
                    foreach (var (caminho, _) in instaladores)
                        File.Copy(caminho, Path.Combine(destino, Path.GetFileName(caminho)), overwrite: true);
                    foreach (var (id, pasta, _, _) in offline)
                    {
                        Directory.CreateDirectory(Path.Combine(destino, id));
                        foreach (var arquivo in Directory.GetFiles(pasta))
                            File.Copy(arquivo, Path.Combine(destino, id, Path.GetFileName(arquivo)), overwrite: true);
                    }
                    File.WriteAllText($@"{w}:\{Unattend.ScriptProgramas}", Unattend.GerarScriptProgramas(ids,
                        instaladores.Select(x => (Path.GetFileName(x.Item1), x.Parametros)), offline.Select(x => (x.Id, x.Comando, x.SoUsuario)), pontoRestauracao));
                    Tui.Registro($"{escolhidos.Count} programas no primeiro login: {string.Join(", ", escolhidos.Select(x => x.Nome))}" +
                        (offline.Count + instaladores.Count > 0 ? $" ({offline.Count + instaladores.Count} sem precisar de internet)" : ""));
                }
            }
            // Antes da tela de login, como sistema e sem janela: Wi-Fi, licença e programas. Se a imagem já tem um SetupComplete, só acrescenta.
            bool programasAntes = usuario != null && temProgramas;
            if (rede != null || chave != null || programasAntes)
            {
                Directory.CreateDirectory($@"{w}:\Windows\Setup\Scripts");
                if (rede != null)
                {
                    File.WriteAllText($@"{w}:\{Unattend.PerfilWifi}", Unattend.GerarPerfilWifi(rede, senhaRede));
                    Tui.Registro($"Wi-Fi {rede} configurado: o Windows já liga conectado");
                }
                if (chave != null) Tui.Registro($"Licença do pendrive (final {chave[^5..]}): ativa antes da tela de login");
                File.WriteAllText($@"{w}:\{Unattend.ScriptAntesDoLogin}", Unattend.GerarScriptAntesDoLogin(rede, chave, programasAntes));
                File.AppendAllText($@"{w}:\{Unattend.ScriptSetupComplete}", Unattend.ChamadaSetupComplete);
            }
            if (Comandos.Executar("bcdboot", $"{w}:\\Windows /s {plano.Sistema}: /f {(uefi ? "UEFI" : "BIOS")}") != 0)
                throw new Exception(Falha("O bcdboot não conseguiu criar a inicialização."));
            // No menu de boot, cada Windows com o nome da edição (o bcdboot põe só "Windows 11" ou "Windows 10")
            if (multi) Comandos.Executar("bcdedit", $"/store \"{bcd}\" /set {{default}} description \"{ed.Nome}\"");
            Tui.Registro($"Inicialização {(uefi ? "UEFI" : "BIOS")} criada para {ed.Nome}");
        }));
    }
    var avisos = new List<string> { string.Join(" + ", instalacoes.Select(x => x.Edicao.Nome)) + " instalado.", "Remova o pendrive e reinicie." };
    if (plano.Recuperacao is char r)
        trabalho.Add(("Recuperação do Windows", 2, () =>
        {
            if (ConfigurarRecuperacao(plano.Windows, r)) Tui.Registro("Recuperação do Windows configurada");
            else avisos.Add("Aviso: a recuperação do Windows não foi configurada (o Windows funciona normalmente).");
        }));

    Comandos.Registrar($"Instalando {edicao.Nome} no disco {disco.Numero} (modo {modo}{(windowsGb != null ? $", {windowsGb} GB" : "")})");
    Tui.Etapas("Instalando o Windows", trabalho.Select(x => (x.Nome, x.Peso)).ToList());
    for (int i = 0; i < trabalho.Count; i++)
    {
        Tui.Etapa(i, trabalho[i].Nome);
        trabalho[i].Fazer();
    }
    if (multi) Comandos.Executar("bcdedit", $"/store \"{bcd}\" /timeout 10");
    Tui.Etapa(trabalho.Count, "Pronto!");
    Tui.Registro($"Instalação concluída: {string.Join(" + ", instalacoes.Select(x => x.Edicao.Nome))} no disco {disco.Numero}");
    if (SalvarRelatorio(plano.Windows, resumo) is string relatorio) avisos.Add($"Relatório na área de trabalho e no pendrive ({relatorio}).");
    alvo = null;
    if (multi) avisos.Add("Ao ligar, o menu de boot mostra os Windows por 10 segundos.");
    if (restaurar && usuario == null) avisos.Add($"Arquivos do backup em C:\\{Unattend.PastaRestaurados}.");
    if (temProgramas && usuario != null)
        avisos.Add("Programas: instalam antes do primeiro login (os do pendrive sempre; os da internet se houver rede) e o resto logo depois, sem janelas.");

    // Com perfil, reinicia sozinho; sem perfil, oferece salvar as escolhas como perfil.
    if (perfil != null)
    {
        if (Tui.Contagem("Instalação concluída", avisos, "Reiniciando", 15)) Comandos.Executar("wpeutil", "reboot");
        return;
    }
    if (!multi && Tui.Menu("Salvar estas escolhas como perfil?", ["Da próxima vez neste PC, a instalação só pede o disco e a confirmação."],
            ["Não", "Sim, salvar como perfil"], voltar: null) == 1)
        SalvarComoPerfil(new Perfil("")
        {
            Edicao = edicao.Nome,
            Instalacao = modo == 2 ? "particao" : windowsGb != null ? $"dividir:{windowsGb}" : "apagar",
            Usuario = usuario,
            Senha = senha,
            Programas = escolhidos.Select(x => x.Nome).ToArray(),
            Restaurar = restaurar || pastaArquivos == null,
            BackupDrivers = pastaDrivers != null,
            Otimizar = otimizar,
            RemoverOneDrive = removerOneDrive,
            AltoDesempenho = altoDesempenho,
            NomePc = nomePc,
            Wifi = rede,
            SenhaWifi = senhaRede,
            Chave = chave,
            PontoRestauracao = pontoRestauracao,
        });
    if (Tui.Menu("Instalação concluída", avisos, ["Reiniciar agora", "Voltar ao menu"], voltar: null) == 0)
        Comandos.Executar("wpeutil", "reboot");
}

// Relatório da formatação: na área de trabalho do Windows novo (para o cliente) e em MontaWin\Relatorios (para você).
string? SalvarRelatorio(char windows, List<string> resumo)
{
    var linhas = new List<string>
    {
        "RELATÓRIO DA FORMATAÇÃO (MontaWin)",
        $"Data: {DateTime.Now:dd/MM/yyyy HH:mm}",
        $"Computador: {Sistema.Resumo()}",
        $"Número de série: {seriePc ?? "não informado pelo fabricante"}",
        "",
        "O QUE FOI ESCOLHIDO",
    };
    linhas.AddRange(resumo.Select(l => "  " + l));
    linhas.AddRange(["", "O QUE FOI FEITO"]);
    linhas.AddRange(Tui.Registros().Select(l => "  " + l));
    linhas.AddRange(["", "Programas e licença são aplicados antes da tela de login e no primeiro login; o resultado fica em",
        @"C:\Windows\Setup\Scripts\MontaWin-programas.log e MontaWin-antes-do-login.log."]);
    var texto = string.Join("\r\n", linhas) + "\r\n";
    var nome = $"{sessao} {Perfil.NomeSeguro(Sistema.Modelo() is { Length: > 0 } m ? m : "PC")}.txt";
    try
    {
        Directory.CreateDirectory($@"{windows}:\Users\Public\Desktop");
        File.WriteAllText($@"{windows}:\Users\Public\Desktop\Relatório da formatação.txt", texto);
        if (raiz == null) return "área de trabalho";
        Directory.CreateDirectory(Path.Combine(raiz, "Relatorios"));
        File.WriteAllText(Path.Combine(raiz, "Relatorios", nome), texto);
        return $"MontaWin\\Relatorios\\{nome}";
    }
    catch (IOException e)
    {
        Comandos.Registrar($"Relatório não salvo: {e.Message}"); // o relatório nunca derruba a instalação
        return null;
    }
}

void SalvarComoPerfil(Perfil escolhas)
{
    var nome = Tui.Perguntar("Salvar como perfil", ["Um perfil guarda as respostas desta instalação."], "Nome do perfil (ex.: PC Escritório)");
    if (string.IsNullOrEmpty(nome) || Perfil.NomeSeguro(nome).Length == 0) return;
    bool guardarSenha = (escolhas.Usuario != null || escolhas.Wifi != null) && Tui.Menu("Senhas (conta e Wi-Fi)", [], ["Perguntar a senha na hora de instalar (mais seguro)",
        "Guardar a senha no perfil (mais rápido; fica escrita no pendrive)"], voltar: null) == 1;
    bool vincular = pcIdentificado && Tui.Menu("Este PC", [$"PC atual: {Sistema.Resumo()}"],
        ["Vincular a este PC: o painel reconhece este PC e já oferece o perfil ao ligar", "Não vincular"], voltar: null) == 0;
    var perfil = escolhas with
    {
        Nome = nome.Trim(),
        Senha = guardarSenha ? escolhas.Senha : null,
        SenhaWifi = guardarSenha ? escolhas.SenhaWifi : null,
        Pc = vincular ? uuidPc : null,
        Serie = vincular ? seriePc : null,
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
    var windows = WindowsInstalados();
    if (!Exigir(windows.Count > 0, "Nenhum Windows instalado encontrado nos discos internos.")) return;
    int w = Tui.Menu("Capturar qual Windows?", [
        "Salva o Windows com programas e configurações como imagem no pendrive.",
        "Para instalar em OUTROS PCs, antes rode no Windows (como administrador):",
        @"C:\Windows\System32\Sysprep\sysprep.exe /generalize /oobe /shutdown",
        "Sem isso, a imagem serve para restaurar este mesmo PC."],
        windows.Select(x => $"{x.p.Letra}:  {Compacto(x.d.Descricao)}").ToList());
    if (w < 0) return;
    char letra = windows[w].p.Letra!.Value;
    var nome = Tui.Perguntar("Nome da imagem", ["É o nome que aparece na lista de edições."], "Nome da imagem (ex.: Escritório 2026)")?.Replace("\"", "");
    if (string.IsNullOrEmpty(nome)) return;
    var unidadeWindows = new DriveInfo(letra.ToString());
    var usado = unidadeWindows.IsReady ? unidadeWindows.TotalSize - unidadeWindows.AvailableFreeSpace : 0;
    var livre = new DriveInfo(Path.GetPathRoot(raiz)!).AvailableFreeSpace;
    // A imagem comprimida costuma ficar com uns 50% do espaço usado.
    if (!Exigir(usado / 2 < livre, $"Pouco espaço no pendrive: o Windows usa {Formatar.Tamanho(usado)} e o pendrive tem {Formatar.Tamanho(livre)} livres.")) return;

    var arquivo = Path.Combine(raiz!, "Imagens", "Personalizadas.wim");
    var acao = File.Exists(arquivo) ? "/Append-Image" : "/Capture-Image /Compress:max";
    Tui.Etapa("Capturar imagem", $"Salvando {letra}: como \"{nome}\"");
    Tui.Registro($"Comprimindo o Windows de {letra}: ({Formatar.Tamanho(usado)} usados) em {arquivo}");
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
    Console.WriteLine("Digite exit para voltar ao MontaWin.");
    Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = false })!.WaitForExit();
}

static class Passo
{
    public const int Avancar = 1, Voltar = -1, Pular = 0, Sair = 2;
}
