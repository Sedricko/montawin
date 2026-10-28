using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MontaWin.Implantador;
using Microsoft.Win32;

namespace MontaWin.Montador;

public class EdicaoItem(int indice, string nome)
{
    public int Indice { get; } = indice;
    public string Nome { get; } = nome;
    public bool Marcada { get; set; } = true;
}

/// <summary>Perfil na lista do app (os perfis ficam no pendrive, em MontaWin\Perfis).</summary>
public record PerfilItem(Perfil Perfil)
{
    public string Texto => $"{Perfil.Nome}   ·   {(Perfil.Usuario != null ? $"conta {Perfil.Usuario}" : "telas da Microsoft")}" +
                           $"   ·   {Perfil.Programas.Length} programas" + (Perfil.Pc != null || Perfil.Serie != null ? "   ·   vinculado a um PC" : "");
}

public partial class MainWindow : Window
{
    // O painel do pendrive (Implantador.exe) vem dentro do AuroraWin.exe; em testes locais, pode estar ao lado.
    static readonly string Implantador = Pendrive.Painel() ?? Path.Combine(AppContext.BaseDirectory, "Implantador.exe");
    IsoAberta? iso;
    readonly Configuracoes config = Configuracoes.Ler();
    Perfil? perfilEditado;
    bool perfilNovo;
    TaskCompletionSource<bool>? resposta;
    (int Disco, char Letra)? revelado; // dados escondidos mostrados com o PIN nesta sessão: voltam a ficar escondidos ao fechar
    readonly DispatcherTimer tempoAviso = new() { Interval = TimeSpan.FromSeconds(4) };

    // Os nomes por dentro são os antigos (tema.txt dos pendrives já gravados): ondas = Aurora, bloom = Galáxia, pastel = Alvorada.
    public static readonly string[] NomesTemas = ["ondas", "bloom", "pastel", "cristal"];

    // Vidro do painel, painel sólido (janelinhas), borda do painel, bloco, borda do bloco e o degradê — as mesmas do painel do boot.
    static readonly Dictionary<string, (string Painel, string Solido, string BordaPainel, string Cartao, string Borda, string De, string Ate, bool Claro)> Cores = new()
    {
        ["ondas"] = ("#C20C0824", "#F2140C3A", "#1AFFFFFF", "#14FFFFFF", "#1FFFFFFF", "#E0218A", "#3B7BFF", false),
        ["bloom"] = ("#BD0E1A3A", "#F20D1733", "#1FFFFFFF", "#14FFFFFF", "#1FFFFFFF", "#2F8CFF", "#60CDFF", false),
        ["pastel"] = ("#99FFFFFF", "#F7F6F7FD", "#CCFFFFFF", "#99FFFFFF", "#1F1B1640", "#4D5BFF", "#9B4DFF", true),
        ["cristal"] = ("#80FFFFFF", "#F7FBF7FF", "#E6FFFFFF", "#8CFFFFFF", "#261D1747", "#FF5FB0", "#36C9FF", true),
    };

    public MainWindow()
    {
        InitializeComponent();
        if (!Cores.ContainsKey(config.Tema)) config.Tema = "ondas";
        foreach (RadioButton r in Temas.Children) r.IsChecked = (string)r.Tag == config.Tema;
        AplicarTema(config.Tema);
        (BootNovo.IsChecked, BootCompativel.IsChecked, Rapida.IsChecked) = (config.BootNovo, !config.BootNovo, config.Rapida);
        (Esconder.IsChecked, Esconder.IsEnabled) = (config.Esconder && config.TemPin, config.TemPin);
        BotaoPin.Content = config.TemPin ? "Trocar PIN" : "Definir PIN";
        tempoAviso.Tick += (_, _) => { tempoAviso.Stop(); Aviso.Visibility = Visibility.Collapsed; };
        MontarPopulares();
        Closed += (_, _) =>
        {
            if (iso != null) Pendrive.FecharIso(iso.Iso);
            if (revelado is var (disco, letra)) Discos.Diskpart(Discos.ScriptEsconderDados(disco, 2, letra));
        };
        Loaded += (_, _) =>
        {
            var args = Environment.GetCommandLineArgs();
            int captura = Array.IndexOf(args, "--captura");
            if (captura > 0 && captura + 1 < args.Length)
            {
                Directory.CreateDirectory(args[captura + 1]);
                try { Capturar(args[captura + 1]); }
                catch (Exception ex) { File.WriteAllText(Path.Combine(args[captura + 1], "erro.txt"), ex.ToString()); }
                Close();
                return;
            }
            if (!File.Exists(Implantador)) Erro($"Não encontrei o painel do pendrive (Implantador.exe):\n{Implantador}");
            CarregarProgramas();
            CarregarPerfis();
            Atualizar(this, new RoutedEventArgs());
        };
    }

    // ---------- Janela solta (sem a moldura do Windows) ----------

    void RaizMudou(object sender, SizeChangedEventArgs e) =>
        Raiz.Clip = new RectangleGeometry(new Rect(Raiz.RenderSize), 26, 26); // cantos arredondados de verdade

    void ArrastarJanela(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    void Minimizar(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Fechar(object sender, RoutedEventArgs e) => Close();

    // ---------- Tema e configurações (engrenagem) ----------

    void TemaEscolhido(object sender, RoutedEventArgs e)
    {
        config.Tema = (string)((RadioButton)sender).Tag;
        AplicarTema(config.Tema);
        if (IsLoaded) config.Salvar();
    }

    void AplicarTema(string tema)
    {
        var c = Cores[tema];
        static Color Cor(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        Resources["CorPainel"] = new SolidColorBrush(Cor(c.Painel));
        Resources["CorPainelSolido"] = new SolidColorBrush(Cor(c.Solido));
        Resources["CorBordaPainel"] = new SolidColorBrush(Cor(c.BordaPainel));
        Resources["CorCartao"] = new SolidColorBrush(Cor(c.Cartao));
        Resources["CorBorda"] = new SolidColorBrush(Cor(c.Borda));
        Resources["CorDestaque"] = new LinearGradientBrush(Cor(c.De), Cor(c.Ate), 0);
        FundoOndas.Visibility = tema == "ondas" ? Visibility.Visible : Visibility.Collapsed;
        FundoBloom.Visibility = tema == "bloom" ? Visibility.Visible : Visibility.Collapsed;
        FundoPastel.Visibility = tema == "pastel" ? Visibility.Visible : Visibility.Collapsed;
        FundoCristal.Visibility = tema == "cristal" ? Visibility.Visible : Visibility.Collapsed;
        ThemeMode = c.Claro ? ThemeMode.Light : ThemeMode.Dark; // controles claros nos temas claros
    }

    FrameworkElement FundoDoTema => config.Tema switch { "bloom" => FundoBloom, "pastel" => FundoPastel, "cristal" => FundoCristal, _ => FundoOndas };
    Brush CorTexto => Cores[config.Tema].Claro ? new SolidColorBrush(Color.FromRgb(0x1B, 0x16, 0x40)) : Brushes.White;

    void AbrirConfig(object sender, RoutedEventArgs e) => CamadaConfig.Visibility = Visibility.Visible;

    void FecharConfig(object sender, RoutedEventArgs e)
    {
        (config.BootNovo, config.Rapida) = (BootNovo.IsChecked == true, Rapida.IsChecked == true);
        config.Salvar();
        CamadaConfig.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// O painel do boot usa o mesmo fundo deste tema: desenha uma cópia em 1920x1080 (normal e desfocada, para o "vidro")
    /// e salva em PNG. O WinPE não desenha desfoque sozinho, então ele já vai pronto.
    /// </summary>
    static void SalvarFundo(FrameworkElement modelo, string arquivo, bool desfocado)
    {
        var copia = (FrameworkElement)XamlReader.Parse(XamlWriter.Save(modelo));
        (copia.Visibility, copia.Width, copia.Height) = (Visibility.Visible, 1920, 1080);
        FrameworkElement raiz = desfocado ? new Border { Child = copia, Effect = new BlurEffect { Radius = 70, RenderingBias = RenderingBias.Quality } } : copia;
        Salvar(raiz, arquivo, 1920, 1080);
    }

    static void Salvar(FrameworkElement elemento, string arquivo, int w, int h)
    {
        elemento.Measure(new Size(w, h));
        elemento.Arrange(new Rect(0, 0, w, h));
        elemento.UpdateLayout();
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(elemento);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(arquivo);
        png.Save(f);
    }

    /// <summary>Imagens de exemplo (AuroraWin.exe --captura pasta): a janela, a engrenagem, um aviso, um perfil e a janelinha de progresso, nos 4 temas.</summary>
    void Capturar(string pasta)
    {
        CaminhoIso.Text = @"D:\ISOs\Win11_25H2_BrazilianPortuguese_x64.iso";
        InfoIso.Text = "2 edições encontradas (versão 25H2). Boot com o certificado antigo (2011) do Secure Boot.";
        ListaEdicoes.ItemsSource = new[] { new EdicaoItem(1, "Windows 11 Home"), new EdicaoItem(6, "Windows 11 Pro") };
        TituloEdicoes.Visibility = Visibility.Visible;
        ListaPendrives.ItemsSource = new[] { new Disco(2, 256L << 30, "Kingston DataTraveler Max", true, false, []) };
        ListaPendrives.SelectedIndex = 0;
        CarregarProgramas();
        ListaPerfis.ItemsSource = new[] { new PerfilItem(new Perfil("João (escritório)") { Usuario = "joao", Programas = ["Google Chrome", "WinRAR"], Pc = "x" }),
                                         new PerfilItem(new Perfil("Maria") { Usuario = "maria", Programas = ["Spotify"] }) };
        BuscaPrograma.Text = "anydesk";
        Status.Text = "Escolha a ISO e o pendrive.";
        UpdateLayout();
        foreach (RadioButton r in Temas.Children)
        {
            r.IsChecked = true;
            var conteudo = (FrameworkElement)Content;
            void Foto(string nome) { UpdateLayout(); Salvar(conteudo, Path.Combine(pasta, $"montador-{config.Tema}-{nome}.png"), (int)conteudo.ActualWidth, (int)conteudo.ActualHeight); }
            Rolagem.ScrollToTop();
            for (int pagina = 1; pagina <= 3; pagina++)
            {
                Foto(pagina.ToString());
                if (Rolagem.VerticalOffset >= Rolagem.ScrollableHeight - 1) break;
                Rolagem.ScrollToVerticalOffset(Rolagem.VerticalOffset + Rolagem.ViewportHeight * 0.9);
            }
            CamadaConfig.Visibility = Visibility.Visible; Foto("configuracoes"); CamadaConfig.Visibility = Visibility.Collapsed;
            AbrirPerfil(((PerfilItem)ListaPerfis.Items[0]).Perfil, novo: false); Foto("perfil"); CamadaPerfil.Visibility = Visibility.Collapsed;
            _ = Perguntar("Apagar e gravar o pendrive?", "TUDO o que estiver no pendrive será apagado:\nDisco 2  Kingston DataTraveler Max  256 GB",
                "Apagar e gravar", "Cancelar", perigo: true);
            Foto("aviso"); CamadaDialogo.Visibility = Visibility.Collapsed;

            var janela = NovaJanelaProgresso("Disco 2 · Kingston DataTraveler Max · 256 GB");
            foreach (var (etapa, pct) in new[] { ("Apagando e particionando o pendrive", 1.0), ("Preparando o painel de inicialização (WinPE)", 8),
                         ("Copiando Windows 11 Pro (1 de 1)", 20), ("Ativando o .NET Framework 3.5: Windows 11 Pro (1 de 1)", 47) })
                janela.Atualizar(etapa, pct);
            var cartao = (FrameworkElement)janela.Content;
            cartao.Measure(new Size(420, double.PositiveInfinity));
            Salvar(cartao, Path.Combine(pasta, $"progresso-{config.Tema}.png"), 420, (int)cartao.DesiredSize.Height);
            janela.Concluir(true, "Pendrive pronto!", "Dê boot por ele no PC: o painel Aurora Win abre sozinho.");
            cartao.Measure(new Size(420, double.PositiveInfinity));
            Salvar(cartao, Path.Combine(pasta, $"progresso-pronto-{config.Tema}.png"), 420, (int)cartao.DesiredSize.Height);
            janela.Close(); // janela criada e não mostrada também segura o app aberto

            SalvarFundo(FundoDoTema, Path.Combine(pasta, $"fundo-{config.Tema}.png"), desfocado: false);
            SalvarFundo(FundoDoTema, Path.Combine(pasta, $"fundo-desfocado-{config.Tema}.png"), desfocado: true);
        }
    }

    // ---------- Avisos no visual do app (no lugar das caixas do Windows) ----------

    Task<bool> Perguntar(string titulo, string texto, string sim, string? nao = "Cancelar", bool perigo = false, bool pedirPin = false)
    {
        CampoPin.Password = "";
        CampoPin.Visibility = pedirPin ? Visibility.Visible : Visibility.Collapsed;
        if (pedirPin) Dispatcher.BeginInvoke(() => CampoPin.Focus(), DispatcherPriority.Input);
        (TituloDialogo.Text, TextoDialogo.Text, BotaoDialogoSim.Content) = (titulo, texto, sim);
        BotaoDialogoNao.Content = nao;
        BotaoDialogoNao.Visibility = nao == null ? Visibility.Collapsed : Visibility.Visible;
        SimboloDialogo.Text = perigo ? "\uE7BA" : "\uE946";
        if (perigo) IconeDialogo.Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x35, 0x4F));
        else IconeDialogo.SetResourceReference(Border.BackgroundProperty, "CorDestaque");
        CamadaDialogo.Visibility = Visibility.Visible;
        resposta?.TrySetResult(false);
        resposta = new TaskCompletionSource<bool>();
        return resposta.Task;
    }

    void DialogoSim(object sender, RoutedEventArgs e) { CamadaDialogo.Visibility = Visibility.Collapsed; resposta?.TrySetResult(true); }
    void DialogoNao(object sender, RoutedEventArgs e) { CamadaDialogo.Visibility = Visibility.Collapsed; resposta?.TrySetResult(false); }

    /// <summary>Pede o PIN de administrador (sem PIN definido, libera). false se cancelou.</summary>
    async Task<bool> ConferirPin(string motivo)
    {
        if (!config.TemPin) return true;
        while (await Perguntar("PIN de administrador", motivo, "Continuar", pedirPin: true))
        {
            if (config.PinCerto(CampoPin.Password)) return true;
            motivo = "PIN errado. Tente de novo.";
        }
        return false;
    }

    async void DefinirPin(object sender, RoutedEventArgs e)
    {
        if (config.TemPin && !await ConferirPin("Digite o PIN atual para trocar.")) return;
        if (!await Perguntar(config.TemPin ? "Novo PIN" : "Definir o PIN", "De 4 a 12 números ou letras. Guarde bem: sem ele não dá para mostrar os dados escondidos pelo app.",
                "Salvar PIN", pedirPin: true)) return;
        if (CampoPin.Password.Length < 4) { Erro("O PIN precisa ter pelo menos 4 números ou letras."); return; }
        config.DefinirPin(CampoPin.Password);
        config.Salvar();
        (Esconder.IsEnabled, BotaoPin.Content) = (true, "Trocar PIN");
        Avisar("PIN salvo. Agora dá para esconder os dados do pendrive no Windows.");
    }

    // Esconder ou mostrar os dados: vale para as próximas gravações e já para o pendrive escolhido
    async void EsconderClicado(object sender, RoutedEventArgs e)
    {
        bool desejado = Esconder.IsChecked == true;
        Esconder.IsChecked = config.Esconder;
        if (!await ConferirPin(desejado ? "Digite o PIN para esconder os dados do pendrive." : "Digite o PIN para deixar os dados do pendrive visíveis.")) return;
        (config.Esconder, Esconder.IsChecked) = (desejado, desejado);
        config.Salvar();
        if (ListaPendrives.SelectedItem is Disco d)
        {
            if (desejado && DadosDoPendrive is char letra) Discos.Diskpart(Discos.ScriptEsconderDados(d.Numero, 2, letra));
            else if (!desejado && Discos.DadosEscondidos(d) is Particao p && Comandos.LetrasLivres(1, "MNOPQTUVYZ") is [char livre])
                Discos.Diskpart(Discos.ScriptMostrarDados(d.Numero, p.Numero, livre, mostrar: true));
            revelado = null;
            Atualizar(this, new RoutedEventArgs());
        }
        Avisar(desejado ? "Dados escondidos: no Windows só aparece a parte de boot do pendrive." : "Dados visíveis no Windows de novo.");
    }

    // Mostra os dados escondidos só enquanto o app está aberto (para ver perfis e usar "Manter")
    async void Revelar(object sender, RoutedEventArgs e)
    {
        if (ListaPendrives.SelectedItem is not Disco d || Discos.DadosEscondidos(d) is not Particao p) return;
        if (!await ConferirPin("Digite o PIN para mostrar os dados deste pendrive.")) return;
        if (Comandos.LetrasLivres(1, "MNOPQTUVYZ") is not [char livre]) { Erro("Não há letras de unidade livres."); return; }
        if (!Discos.Diskpart(Discos.ScriptMostrarDados(d.Numero, p.Numero, livre, mostrar: false))) { Erro("Não consegui dar uma letra à parte escondida do pendrive."); return; }
        revelado = (d.Numero, livre);
        Atualizar(this, new RoutedEventArgs());
    }

    void Erro(string mensagem) => _ = Perguntar("Algo não deu certo", mensagem, "Entendi", null, perigo: true);

    /// <summary>Avisinho que some sozinho em 4 segundos.</summary>
    void Avisar(string texto)
    {
        TextoAviso.Text = texto;
        Aviso.Visibility = Visibility.Visible;
        tempoAviso.Stop();
        tempoAviso.Start();
    }

    void Ocupado(bool ocupado, string status)
    {
        foreach (var c in new UIElement[] { BotaoIso, BotaoAtualizar, BotaoCriar, ListaPendrives, ListaEdicoes, Manter, BotaoAtualizacoes, AtualizarAuto,
                     BaixarProgramas, NetFx3, BotaoProcurar, BotaoAdicionar, BotaoAdicionarArquivo, BotaoRemover, BotaoArquivo, BotaoSalvarProgramas,
                     BotaoConfig, PainelPopulares, BotaoNovoPerfil, BotaoEditarPerfil, BotaoDuplicarPerfil, BotaoApagarPerfil })
            c.IsEnabled = !ocupado;
        Status.Text = status;
    }

    // ---------- Windows (ISO) ----------

    async void EscolherIso(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFileDialog { Filter = "Imagem ISO do Windows (*.iso)|*.iso" };
        if (dialogo.ShowDialog(this) != true) return;
        Ocupado(true, "Abrindo a ISO...");
        try
        {
            if (iso != null) await Task.Run(() => Pendrive.FecharIso(iso.Iso));
            iso = null;
            iso = await Task.Run(() => Pendrive.AbrirIso(dialogo.FileName));
            CaminhoIso.Text = iso.Iso;
            ListaEdicoes.ItemsSource = iso.Edicoes.Select(x => new EdicaoItem(x.Indice, x.Nome)).ToList();
            TituloEdicoes.Visibility = Visibility.Visible;
            var versao = Atualizacoes.Versao(iso.Edicoes.FirstOrDefault()?.Build ?? 0);
            var certificado = Pendrive.CertificadoDoBoot(iso.Letra);
            InfoIso.Text = (iso.Edicoes.Count == 1 ? "1 edição encontrada" : $"{iso.Edicoes.Count} edições encontradas") +
                (versao != null ? $" (versão {versao})." : $" (build {iso.Edicoes.FirstOrDefault()?.Build}: versão não reconhecida para as atualizações).") +
                (certificado != null ? $" Boot com o certificado {certificado} do Secure Boot." : "") +
                (certificado == "antigo (2011)" ? " Se algum PC recusar o boot do pendrive, use uma ISO mais nova da Microsoft." : "");
            Ocupado(false, "ISO pronta. Escolha o pendrive e clique em Criar pendrive.");
        }
        catch (Exception ex)
        {
            Ocupado(false, "Escolha a ISO e o pendrive.");
            Erro(ex.Message);
        }
    }

    void EscolherAtualizacoes(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFolderDialog { Title = "Pasta com as atualizações .msu" };
        if (dialogo.ShowDialog(this) != true) return;
        if (Directory.GetFiles(dialogo.FolderName, "*.msu").Length == 0) { Erro("Essa pasta não tem arquivos .msu."); return; }
        PastaAtualizacoes.Text = dialogo.FolderName;
        AtualizarAuto.IsChecked = false; // a pasta escolhida substitui o download automático
    }

    // ---------- Pendrive ----------

    async void Atualizar(object sender, RoutedEventArgs e)
    {
        Ocupado(true, "Procurando pendrives...");
        int? antes = (ListaPendrives.SelectedItem as Disco)?.Numero;
        var pendrives = await Task.Run(() => Discos.Listar().Where(d => d.Usb).ToList());
        ListaPendrives.ItemsSource = pendrives;
        ListaPendrives.SelectedIndex = pendrives.Count == 0 ? -1 : Math.Max(0, pendrives.FindIndex(d => d.Numero == antes));
        Ocupado(false, pendrives.Count > 0 ? "Escolha a ISO e o pendrive." : "Nenhum pendrive encontrado. Conecte um e clique em Atualizar.");
    }

    /// <summary>Letra da partição de dados do pendrive escolhido, se ele já foi gravado pelo Aurora Win.</summary>
    char? DadosDoPendrive => ListaPendrives.SelectedItem is Disco d && Pendrive.Existente(d) is (_, char dados) ? dados : null;

    // Pendrive já gravado: oferece adicionar a ISO sem apagar imagens, perfis e backups.
    void PendriveEscolhido(object sender, RoutedEventArgs e)
    {
        var existente = ListaPendrives.SelectedItem is Disco d ? Pendrive.Existente(d) : null;
        Manter.Visibility = existente != null ? Visibility.Visible : Visibility.Collapsed;
        Manter.IsChecked = existente != null;
        BotaoSalvarProgramas.Visibility = existente != null ? Visibility.Visible : Visibility.Collapsed;
        Escondido.Visibility = existente == null && ListaPendrives.SelectedItem is Disco e1 && Discos.DadosEscondidos(e1) != null ? Visibility.Visible : Visibility.Collapsed;
        // Licença já gravada nesse pendrive: aparece na engrenagem, para não precisar digitar de novo.
        // Primeira vez neste PC: a lista que o técnico já editou no pendrive vira a lista do app.
        if (existente is (_, char comLicenca) && Licenca.Text.Length == 0 && File.Exists($@"{comLicenca}:\MontaWin\licenca.txt"))
            Licenca.Text = File.ReadAllText($@"{comLicenca}:\MontaWin\licenca.txt").Trim();
        if (existente is (_, char dados) && !File.Exists(ListaProgramas.Arquivo) && File.Exists($@"{dados}:\MontaWin\Programas.txt"))
            ListaProgramas.Salvar(File.ReadAllText($@"{dados}:\MontaWin\Programas.txt"));
        CarregarProgramas();
        CarregarPerfis();
    }

    // ---------- Programas ----------

    void CarregarProgramas()
    {
        var dados = DadosDoPendrive;
        var lista = Programas.Ler(ListaProgramas.Ler());
        var itens = lista.Select(p => new ItemLista(p, ItemLista.SeloDe(p, dados))).ToList();
        ListaProgramasUi.ItemsSource = itens;
        _ = BaixarIcones(itens);
        var ids = lista.SelectMany(p => p.Ids).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (Button b in PainelPopulares.Children) b.Visibility = ids.Contains(((string Nome, string Id))b.Tag is var (_, id) ? id : "") ? Visibility.Collapsed : Visibility.Visible;
    }

    // Ícones que faltam, um por vez em segundo plano (cada um é um "winget show" + o ícone do site)
    static readonly System.Net.Http.HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static async Task BaixarIcones(List<ItemLista> itens)
    {
        foreach (var item in itens.Where(i => i.Icone == null))
            if (await Icones.Baixar(item.Programa, http) is string arquivo) item.Icone = arquivo;
    }

    void MontarPopulares()
    {
        foreach (var (nome, id) in Populares.Lista)
        {
            var b = new Button { Style = (Style)FindResource("Pilula"), Content = "+ " + nome, Tag = (nome, id), ToolTip = $"winget {id}" };
            b.Click += (_, _) =>
            {
                ListaProgramas.Salvar(ListaProgramas.Adicionar(ListaProgramas.Ler(), nome, id));
                CarregarProgramas();
                Avisar($"{nome} adicionado à lista.");
            };
            PainelPopulares.Children.Add(b);
        }
    }

    // A caixa de cada programa: marcado = já vem escolhido no painel (+ na lista)
    void MarcarPrograma(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: ItemLista item } caixa)
            ListaProgramas.Salvar(ListaProgramas.Marcar(ListaProgramas.Ler(), item.Nome, caixa.IsChecked == true));
    }

    void RemoverPrograma(object sender, RoutedEventArgs e)
    {
        var itens = ListaProgramasUi.SelectedItems.Cast<ItemLista>().ToList();
        if (itens.Count == 0) { Erro("Escolha na lista os programas que vão sair (Ctrl ou Shift + clique para vários)."); return; }
        var texto = ListaProgramas.Ler();
        foreach (var item in itens) texto = ListaProgramas.Remover(texto, item.Nome);
        ListaProgramas.Salvar(texto);
        CarregarProgramas();
        Avisar(itens.Count == 1 ? $"{itens[0].Nome} saiu da lista." : $"{itens.Count} programas saíram da lista.");
    }

    void BuscaTecla(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ProcurarPrograma(sender, e);
    }

    async void ProcurarPrograma(object sender, RoutedEventArgs e)
    {
        var busca = BuscaPrograma.Text.Trim().Replace("\"", "");
        if (busca.Length < 2) { Erro("Digite o nome do programa (ex.: anydesk)."); return; }
        Ocupado(true, $"Procurando \"{busca}\" no winget...");
        var resultados = await Task.Run(() =>
        {
            Comandos.Executar("winget", $"search \"{busca}\" --source winget --accept-source-agreements --disable-interactivity");
            return ListaProgramas.LerBusca(Comandos.UltimaSaidaCompleta);
        });
        ResultadosBusca.ItemsSource = resultados;
        ResultadosBusca.SelectedIndex = resultados.Count > 0 ? 0 : -1;
        if (resultados.Count > 0) ResultadosBusca.IsDropDownOpen = true;
        Ocupado(false, resultados.Count > 0 ? $"{resultados.Count} encontrados: escolha um e clique em Adicionar."
                                            : "Nada encontrado no winget. Tente outro nome ou use um instalador que você já tem.");
    }

    void AdicionarBusca(object sender, RoutedEventArgs e)
    {
        if (ResultadosBusca.SelectedItem is not ResultadoBusca r) { Erro("Procure o programa e escolha um dos resultados."); return; }
        var nome = r.Nome.TrimEnd('…').Trim();
        ListaProgramas.Salvar(ListaProgramas.Adicionar(ListaProgramas.Ler(), nome, r.Id));
        CarregarProgramas();
        BuscaPrograma.Text = "";
        ResultadosBusca.ItemsSource = null;
        Avisar($"{nome} adicionado. Ele vai para o pendrive ao criar (ou em \"Salvar programas no pendrive\").");
    }

    void EscolherInstalador(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFileDialog { Filter = "Instalador (*.exe;*.msi)|*.exe;*.msi" };
        if (dialogo.ShowDialog(this) != true) return;
        ArquivoInstalador.Text = dialogo.FileName;
        NomePrograma.Text = Path.GetFileNameWithoutExtension(dialogo.FileName);
        ParametrosInstalador.Text = ListaProgramas.ParametrosSugeridos(dialogo.FileName);
    }

    void AdicionarArquivo(object sender, RoutedEventArgs e)
    {
        var nome = NomePrograma.Text.Trim();
        if (ArquivoInstalador.Text.Length == 0) { Erro("Escolha o arquivo do instalador."); return; }
        if (nome.Length == 0) { Erro("Digite o nome que vai aparecer no painel."); return; }
        // Sem espaços no nome do arquivo: a linha da lista separa o arquivo dos parâmetros pelo espaço.
        var arquivo = Path.GetFileName(ArquivoInstalador.Text).Replace(' ', '_');
        Directory.CreateDirectory(ListaProgramas.PastaInstaladores);
        File.Copy(ArquivoInstalador.Text, Path.Combine(ListaProgramas.PastaInstaladores, arquivo), overwrite: true);
        ListaProgramas.Salvar(ListaProgramas.Adicionar(ListaProgramas.Ler(), nome, $"arquivo: {arquivo} {ParametrosInstalador.Text.Trim()}".TrimEnd()));
        CarregarProgramas();
        (ArquivoInstalador.Text, NomePrograma.Text, ParametrosInstalador.Text) = ("", "", "");
        Avisar($"{nome} adicionado. O instalador vai para o pendrive ao criar.");
    }

    async void SalvarProgramasNoPendrive(object sender, RoutedEventArgs e)
    {
        if (DadosDoPendrive is not char dados) { Erro("Escolha um pendrive já gravado pelo Aurora Win."); return; }
        bool baixar = BaixarProgramas.IsChecked == true;
        Ocupado(true, "Salvando os programas no pendrive...");
        try
        {
            var falhas = await Task.Run(() =>
            {
                Pendrive.SalvarProgramas(dados, ListaProgramas.Ler(), ListaProgramas.PastaInstaladores);
                return baixar ? Pendrive.BaixarProgramas(dados, (etapa, pct) => Dispatcher.Invoke(() =>
                {
                    if (etapa != null) Status.Text = etapa;
                    Barra.Value = pct;
                })) : [];
            });
            Barra.Value = 100;
            CarregarProgramas();
            Ocupado(false, "Programas salvos no pendrive." + (falhas.Count > 0 ? $" Não deu para baixar: {string.Join(", ", falhas)} (instalam pela internet)." : ""));
        }
        catch (Exception ex)
        {
            Ocupado(false, "Falhou ao salvar os programas.");
            Erro(ex.Message);
        }
    }

    // ---------- Perfis (no pendrive) ----------

    void CarregarPerfis()
    {
        var dados = DadosDoPendrive;
        foreach (var b in new[] { BotaoNovoPerfil, BotaoEditarPerfil, BotaoDuplicarPerfil, BotaoApagarPerfil }) b.IsEnabled = dados != null;
        DicaPerfis.Text = dados != null
            ? "Cada perfil guarda as escolhas de uma pessoa ou PC (conta, rede, programas...). Com ele, o painel só pede o disco e o SIM."
            : "Escolha um pendrive já gravado pelo Aurora Win para ver e editar os perfis dele.";
        var pasta = dados is char d ? $@"{d}:\MontaWin\Perfis" : null;
        ListaPerfis.ItemsSource = pasta != null && Directory.Exists(pasta)
            ? Directory.GetFiles(pasta, "*.txt").Select(f => Perfil.Ler(File.ReadAllText(f))).OfType<Perfil>().Select(p => new PerfilItem(p)).ToList()
            : null;
    }

    // Outra pessoa começa do zero: sem programas e sem vínculo com PC
    void NovoPerfil(object sender, RoutedEventArgs e) =>
        AbrirPerfil(new Perfil("") { Edicao = iso?.Edicoes.FirstOrDefault()?.Nome ?? "Windows 11 Pro", Usuario = "" }, novo: true);

    void EditarPerfil(object sender, RoutedEventArgs e)
    {
        if (ListaPerfis.SelectedItem is PerfilItem item) AbrirPerfil(item.Perfil, novo: false);
        else if (sender is Button) Erro("Escolha um perfil na lista.");
    }

    void DuplicarPerfil(object sender, RoutedEventArgs e)
    {
        if (ListaPerfis.SelectedItem is not PerfilItem item) { Erro("Escolha o perfil que vai servir de modelo."); return; }
        AbrirPerfil(item.Perfil with { Nome = item.Perfil.Nome + " (cópia)", Pc = null, Serie = null, Modelo = null }, novo: true);
    }

    async void ApagarPerfil(object sender, RoutedEventArgs e)
    {
        if (ListaPerfis.SelectedItem is not PerfilItem item || DadosDoPendrive is not char dados) { Erro("Escolha um perfil na lista."); return; }
        if (!await Perguntar($"Apagar o perfil {item.Perfil.Nome}?", "Os backups dele continuam no pendrive.", "Apagar", perigo: true)) return;
        File.Delete(ArquivoPerfil(dados, item.Perfil.Nome));
        CarregarPerfis();
        Avisar($"Perfil {item.Perfil.Nome} apagado.");
    }

    static string ArquivoPerfil(char dados, string nome) => $@"{dados}:\MontaWin\Perfis\{Perfil.NomeSeguro(nome)}.txt";

    void AbrirPerfil(Perfil p, bool novo)
    {
        (perfilEditado, perfilNovo) = (p, novo);
        TituloPerfil.Text = novo ? "Novo perfil" : $"Perfil {p.Nome}";
        (PerfilNome.Text, PerfilEdicao.Text, PerfilUsuario.Text, PerfilNomePc.Text, PerfilWifi.Text) =
            (p.Nome, p.Edicao, p.Usuario ?? "", p.NomePc ?? "", p.Wifi ?? "");
        PerfilInstalacao.SelectedIndex = p.Instalacao == "particao" ? 2 : p.Instalacao.StartsWith("dividir:") ? 1 : 0;
        PerfilGb.Text = p.Instalacao.StartsWith("dividir:") ? p.Instalacao["dividir:".Length..] : "120";
        (PerfilOtimizar.IsChecked, PerfilOneDrive.IsChecked, PerfilAlto.IsChecked) = (p.Otimizar, p.RemoverOneDrive, p.AltoDesempenho);
        (PerfilPonto.IsChecked, PerfilRestaurar.IsChecked, PerfilDrivers.IsChecked) = (p.PontoRestauracao, p.Restaurar, p.BackupDrivers);
        PerfilSenhas.Text = $"Senha da conta: {(p.Senha != null ? "guardada no perfil" : "perguntada na hora")}." +
            (p.Wifi != null ? $" Senha do Wi-Fi: {(p.SenhaWifi != null ? "guardada" : "perguntada na hora")}." : "") +
            (p.Pc != null || p.Serie != null ? $" Vinculado ao PC {p.Modelo ?? p.Serie}." : " Não vinculado a um PC (o painel vincula ao salvar lá).") +
            " Senhas e licença se guardam pelo painel, no PC.";
        PerfilProgramas.ItemsSource = Programas.Ler(ListaProgramas.Ler()).Select(x => new ItemLista(x with { Marcado = p.Programas.Contains(x.Nome) }, "")).ToList();
        CamadaPerfil.Visibility = Visibility.Visible;
    }

    void CancelarPerfil(object sender, RoutedEventArgs e) => CamadaPerfil.Visibility = Visibility.Collapsed;

    async void SalvarPerfil(object sender, RoutedEventArgs e)
    {
        if (perfilEditado is not Perfil antigo || DadosDoPendrive is not char dados) return;
        var nome = PerfilNome.Text.Trim();
        if (Perfil.NomeSeguro(nome).Length == 0) { Erro("Dê um nome ao perfil (ex.: João ou PC da recepção)."); return; }
        var nomePc = PerfilNomePc.Text.Trim();
        if (nomePc.Length > 0 && !Unattend.NomePcValido(nomePc)) { Erro("Nome do computador: até 15 letras, números ou hífen, sem ser só números."); return; }
        int gb = 0;
        if (PerfilInstalacao.SelectedIndex == 1 && (!int.TryParse(PerfilGb.Text, out gb) || gb < 40)) { Erro("Windows + Dados: o tamanho do Windows precisa ser de pelo menos 40 GB."); return; }
        var usuario = PerfilUsuario.Text.Trim();
        var wifi = PerfilWifi.Text.Trim();
        var arquivo = ArquivoPerfil(dados, nome);
        bool renomeou = !perfilNovo && !Perfil.NomeSeguro(nome).Equals(Perfil.NomeSeguro(antigo.Nome), StringComparison.OrdinalIgnoreCase);
        if ((perfilNovo || renomeou) && File.Exists(arquivo) && !await Perguntar("Já existe um perfil com esse nome", $"Substituir o perfil {nome}?", "Substituir")) return;
        var perfil = antigo with
        {
            Nome = nome,
            Edicao = PerfilEdicao.Text.Trim(),
            Instalacao = PerfilInstalacao.SelectedIndex switch { 1 => $"dividir:{gb}", 2 => "particao", _ => "apagar" },
            Usuario = usuario.Length > 0 ? usuario : null,
            Senha = usuario.Length > 0 ? antigo.Senha : null,
            NomePc = nomePc.Length > 0 ? nomePc.ToUpperInvariant() : null,
            Wifi = wifi.Length > 0 ? wifi : null,
            SenhaWifi = wifi == antigo.Wifi ? antigo.SenhaWifi : null, // a senha era da rede antiga
            Otimizar = PerfilOtimizar.IsChecked == true,
            RemoverOneDrive = PerfilOneDrive.IsChecked == true,
            AltoDesempenho = PerfilAlto.IsChecked == true,
            PontoRestauracao = PerfilPonto.IsChecked == true,
            Restaurar = PerfilRestaurar.IsChecked == true,
            BackupDrivers = PerfilDrivers.IsChecked == true,
            Programas = ((IEnumerable<ItemLista>)PerfilProgramas.ItemsSource).Where(x => x.Marcado).Select(x => x.Nome).ToArray(),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        File.WriteAllText(arquivo, perfil.Texto());
        if (renomeou) File.Delete(ArquivoPerfil(dados, antigo.Nome));
        CamadaPerfil.Visibility = Visibility.Collapsed;
        CarregarPerfis();
        Avisar($"Perfil {nome} salvo no pendrive.");
    }

    // ---------- Criar o pendrive ----------

    JanelaProgresso NovaJanelaProgresso(string pendrive) =>
        new((Brush)Resources["CorDestaque"], (Brush)Resources["CorPainelSolido"], (Brush)Resources["CorBorda"], CorTexto,
            new BitmapImage(new Uri("pack://application:,,,/icone-app.png")), pendrive);

    async void Criar(object sender, RoutedEventArgs e)
    {
        var indices = (ListaEdicoes.ItemsSource as IEnumerable<EdicaoItem>)?.Where(x => x.Marcada).Select(x => x.Indice).ToArray() ?? [];
        if (iso == null) { Erro("Escolha a ISO do Windows (bloco Windows)."); return; }
        if (indices.Length == 0) { Erro("Marque pelo menos uma edição."); return; }
        if (ListaPendrives.SelectedItem is not Disco pendrive) { Erro("Escolha o pendrive."); return; }
        var licenca = Licenca.Text.Trim().Length == 0 ? null : Unattend.ChaveValida(Licenca.Text);
        if (Licenca.Text.Trim().Length > 0 && licenca == null) { Erro("A licença (na engrenagem) precisa ter o formato XXXXX-XXXXX-XXXXX-XXXXX-XXXXX."); return; }
        if (!File.Exists(Implantador)) { Erro($"Não encontrei o painel do pendrive (Implantador.exe):\n{Implantador}"); return; }
        var manter = Manter.IsChecked == true ? Pendrive.Existente(pendrive) : null;
        long imagem = new FileInfo(iso.Install).Length;
        long necessario = manter != null ? imagem : ((long)Pendrive.BootMb << 20) + imagem + (1L << 30);
        long disponivel = manter is (_, char livre) ? new DriveInfo(livre.ToString()).AvailableFreeSpace : pendrive.Tamanho;
        if (disponivel < necessario)
        {
            Erro($"Não cabe: precisa de {Formatar.Tamanho(necessario)} e o pendrive tem {Formatar.Tamanho(disponivel)}" +
                 (manter != null ? " livres. Desmarque \"Manter\" para apagar e recriar o pendrive." : "."));
            return;
        }
        bool confirmou = manter != null
            ? await Perguntar("Adicionar ao pendrive?", $"A ISO será adicionada e o painel atualizado. Imagens, perfis e backups que já estão nele ficam.\n\n{pendrive.Descricao.Trim()}", "Adicionar e atualizar")
            : await Perguntar("Apagar e gravar o pendrive?", $"TUDO o que estiver no pendrive será apagado:\n\n{pendrive.Descricao.Trim()}" +
                (Pendrive.Existente(pendrive) is (_, char dadosAntigos) && Pendrive.AvisoAoApagar(dadosAntigos) is string aviso ? "\n\n" + aviso : ""),
                "Apagar e gravar", perigo: true);
        if (!confirmou) return;

        // A janela grande sai da frente: a janelinha animada acompanha a gravação e devolve o app no fim
        var janela = NovaJanelaProgresso(pendrive.Descricao.Trim());
        janela.Closed += (_, _) => { Show(); Activate(); };
        Ocupado(true, "Gravando o pendrive...");
        Hide();
        janela.Show();
        void Progresso(string? etapa, double pct) => Dispatcher.Invoke(() =>
        {
            janela.Atualizar(etapa, pct);
            if (etapa != null) Status.Text = etapa;
            Barra.Value = pct;
        });
        string? atualizacoes = PastaAtualizacoes.Text.Length > 0 ? PastaAtualizacoes.Text : null;
        int build = iso.Edicoes.First(x => x.Indice == indices[0]).Build;
        if (atualizacoes == null && AtualizarAuto.IsChecked == true)
        {
            try
            {
                var (pasta, descricao) = await Task.Run(() => Atualizacoes.Baixar(build, Progresso));
                atualizacoes = pasta;
                Status.Text = $"Atualizações baixadas: {descricao}";
            }
            catch (Exception ex)
            {
                janela.Close();
                Ocupado(false, "Não deu para baixar as atualizações automaticamente.");
                // Versão não reconhecida: diz qual build é e como seguir (sem pesquisa certa para abrir).
                if (Atualizacoes.Versao(build) == null)
                {
                    Erro($"Não reconheci a versão desta ISO (build {build}), então não sei quais atualizações servem.\n\n" +
                         "Desmarque \"Baixar e instalar as atualizações do mês\" para criar sem elas, ou escolha uma pasta com os .msu baixados.");
                    return;
                }
                // Reserva: abre a pesquisa certa no Catalog para baixar na mão e escolher a pasta.
                if (await Perguntar("Não consegui baixar as atualizações sozinho", $"{ex.Message}\n\nAbrir o Microsoft Update Catalog já pesquisado na versão certa? " +
                        "Baixe a \"Cumulative Update\" mais nova (sem \"Preview\"), escolha a pasta no bloco Windows e crie de novo. " +
                        "Ou desmarque \"Baixar e instalar as atualizações\" para criar sem elas.", "Abrir o Catalog", "Agora não"))
                    Process.Start(new ProcessStartInfo(Atualizacoes.UrlPesquisa(build)) { UseShellExecute = true });
                return;
            }
        }
        try
        {
            var fundo = Path.Combine(Path.GetTempPath(), "AuroraWin-fundo.png");
            var desfocado = Path.Combine(Path.GetTempPath(), "AuroraWin-fundo-desfocado.png");
            SalvarFundo(FundoDoTema, fundo, desfocado: false);
            SalvarFundo(FundoDoTema, desfocado, desfocado: true);
            var gravacao = new Gravacao(iso, indices, pendrive, Implantador, config.Rapida, manter, atualizacoes, config.Tema,
                (fundo, desfocado), BaixarProgramas.IsChecked == true, (ListaProgramas.Ler(), ListaProgramas.PastaInstaladores),
                NetFx3.IsChecked == true, licenca, config.BootNovo, config.Esconder && config.TemPin);
            var falhas = await Task.Run(() => Pendrive.Gravar(gravacao, Progresso));
            if (gravacao.Esconder) revelado = null; // a gravação já escondeu de novo
            Ocupado(false, "Pendrive pronto! Dê boot por ele no PC.");
            janela.Concluir(true, "Pendrive pronto!", "Dê boot por ele no PC: o painel Aurora Win abre sozinho." +
                (falhas.Count > 0 ? $"\nNão deu para baixar: {string.Join(", ", falhas)}. Esses instalam pela internet no primeiro login." : ""));
            CarregarProgramas();
            CarregarPerfis();
        }
        catch (Exception ex)
        {
            Ocupado(false, "Falhou. Veja a mensagem e tente de novo.");
            janela.Concluir(false, "Não deu certo", ex.Message.Split('\n')[0]);
            Erro(ex.Message);
        }
    }
}
