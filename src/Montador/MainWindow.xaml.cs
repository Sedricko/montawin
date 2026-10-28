using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using MontaWin.Implantador;
using Microsoft.Win32;

namespace MontaWin.Montador;

public class EdicaoItem(int indice, string nome)
{
    public int Indice { get; } = indice;
    public string Nome { get; } = nome;
    public bool Marcada { get; set; } = true;
}

public partial class MainWindow : Window
{
    // O Implantador.exe vai junto do MontaWin.exe e é colocado dentro do boot.wim do pendrive.
    static readonly string Implantador = Path.Combine(AppContext.BaseDirectory, "Implantador.exe");
    IsoAberta? iso;
    string tema = "ondas";

    // Painel, BordaPainel, Cartao, Borda (ARGB) e Destaque (degradê) — mesmas cores do painel no boot.
    static readonly Dictionary<string, (string Painel, string BordaPainel, string Cartao, string Borda, string De, string Ate)> Cores = new()
    {
        ["ondas"] = ("#C20C0824", "#1AFFFFFF", "#0FFFFFFF", "#1AFFFFFF", "#E0218A", "#3B7BFF"),
        ["bloom"] = ("#BD0E1A3A", "#1FFFFFFF", "#0FFFFFFF", "#1AFFFFFF", "#2F8CFF", "#60CDFF"),
        ["pastel"] = ("#99FFFFFF", "#CCFFFFFF", "#8CFFFFFF", "#1F1B1640", "#4D5BFF", "#9B4DFF"),
    };

    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => { if (iso != null) Pendrive.FecharIso(iso.Iso); };
        Loaded += (_, _) =>
        {
            var args = Environment.GetCommandLineArgs();
            int captura = Array.IndexOf(args, "--captura");
            if (captura > 0 && captura + 1 < args.Length) { Capturar(args[captura + 1]); return; }
            if (!File.Exists(Implantador)) Erro($"Não encontrei o Implantador.exe ao lado do MontaWin.exe:\n{Implantador}");
            Atualizar(this, new RoutedEventArgs());
        };
    }

    void TemaEscolhido(object sender, RoutedEventArgs e)
    {
        tema = (string)((RadioButton)sender).Tag;
        var c = Cores[tema];
        Brush Cor(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        Resources["Painel"] = Cor(c.Painel);
        Resources["BordaPainel"] = Cor(c.BordaPainel);
        Resources["Cartao"] = Cor(c.Cartao);
        Resources["Borda"] = Cor(c.Borda);
        Resources["Destaque"] = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(c.De), (Color)ColorConverter.ConvertFromString(c.Ate), 0);
        FundoOndas.Visibility = tema == "ondas" ? Visibility.Visible : Visibility.Collapsed;
        FundoBloom.Visibility = tema == "bloom" ? Visibility.Visible : Visibility.Collapsed;
        FundoPastel.Visibility = tema == "pastel" ? Visibility.Visible : Visibility.Collapsed;
        ThemeMode = tema == "pastel" ? ThemeMode.Light : ThemeMode.Dark; // controles claros no tema claro
    }

    FrameworkElement FundoDoTema => tema switch { "bloom" => FundoBloom, "pastel" => FundoPastel, _ => FundoOndas };

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

    /// <summary>Imagens de exemplo (MontaWin.exe --captura pasta): esta janela e os fundos do painel, nos 3 temas.</summary>
    void Capturar(string pasta)
    {
        Directory.CreateDirectory(pasta);
        CaminhoIso.Text = @"D:\ISOs\Win11_25H2_BrazilianPortuguese_x64.iso";
        ListaEdicoes.ItemsSource = new[] { new EdicaoItem(1, "Windows 11 Home"), new EdicaoItem(6, "Windows 11 Pro") };
        TituloEdicoes.Visibility = Visibility.Visible;
        ListaPendrives.ItemsSource = new[] { new Disco(2, 256L << 30, "Kingston DataTraveler Max", true, false, []) };
        ListaPendrives.SelectedIndex = 0;
        Status.Text = "Copiando Windows 11 Pro (2 de 2)";
        Barra.Value = 62;
        foreach (RadioButton r in Temas.Children)
        {
            r.IsChecked = true;
            UpdateLayout();
            var conteudo = (FrameworkElement)Content;
            Salvar(conteudo, Path.Combine(pasta, $"montador-{tema}.png"), (int)conteudo.ActualWidth, (int)conteudo.ActualHeight);
            SalvarFundo(FundoDoTema, Path.Combine(pasta, $"fundo-{tema}.png"), desfocado: false);
            SalvarFundo(FundoDoTema, Path.Combine(pasta, $"fundo-desfocado-{tema}.png"), desfocado: true);
        }
        Close();
    }

    void Erro(string mensagem) => MessageBox.Show(this, mensagem, "MontaWin", MessageBoxButton.OK, MessageBoxImage.Error);

    void Ocupado(bool ocupado, string status)
    {
        BotaoIso.IsEnabled = BotaoAtualizar.IsEnabled = BotaoCriar.IsEnabled = ListaPendrives.IsEnabled = ListaEdicoes.IsEnabled =
            Manter.IsEnabled = Rapida.IsEnabled = BotaoAtualizacoes.IsEnabled = Temas.IsEnabled = AtualizarAuto.IsEnabled = BaixarProgramas.IsEnabled = !ocupado;
        Status.Text = status;
    }

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
            Ocupado(false, (iso.Edicoes.Count == 1 ? "1 edição encontrada" : $"{iso.Edicoes.Count} edições encontradas") +
                (versao != null ? $" (versão {versao})." : "."));
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

    async void Atualizar(object sender, RoutedEventArgs e)
    {
        Ocupado(true, "Procurando pendrives...");
        var pendrives = await Task.Run(() => Discos.Listar().Where(d => d.Usb).ToList());
        ListaPendrives.ItemsSource = pendrives;
        ListaPendrives.SelectedIndex = pendrives.Count > 0 ? 0 : -1;
        Ocupado(false, pendrives.Count > 0 ? "Escolha a ISO e o pendrive." : "Nenhum pendrive encontrado. Conecte um e clique em Atualizar.");
    }

    // Pendrive já gravado pelo MontaWin: oferece adicionar a ISO sem apagar imagens e backups.
    void PendriveEscolhido(object sender, RoutedEventArgs e)
    {
        bool existente = ListaPendrives.SelectedItem is Disco d && Pendrive.Existente(d) != null;
        Manter.Visibility = existente ? Visibility.Visible : Visibility.Collapsed;
        Manter.IsChecked = existente;
    }

    async void Criar(object sender, RoutedEventArgs e)
    {
        var indices = (ListaEdicoes.ItemsSource as IEnumerable<EdicaoItem>)?.Where(x => x.Marcada).Select(x => x.Indice).ToArray() ?? [];
        if (iso == null) { Erro("Escolha a ISO do Windows."); return; }
        if (indices.Length == 0) { Erro("Marque pelo menos uma edição."); return; }
        if (ListaPendrives.SelectedItem is not Disco pendrive) { Erro("Escolha o pendrive."); return; }
        if (!File.Exists(Implantador)) { Erro($"Não encontrei o Implantador.exe:\n{Implantador}"); return; }
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
        var pergunta = manter != null
            ? $"A ISO será adicionada ao pendrive e o painel será atualizado. Imagens e backups que já estão nele ficam.\n\n{pendrive.Descricao.Trim()}\n\nContinuar?"
            : $"TUDO o que estiver no pendrive será apagado:\n\n{pendrive.Descricao.Trim()}\n\nContinuar?";
        if (MessageBox.Show(this, pergunta, "MontaWin", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        Ocupado(true, "Começando...");
        void Progresso(string? etapa, double pct) => Dispatcher.Invoke(() =>
        {
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
                Ocupado(false, "Não deu para baixar as atualizações automaticamente.");
                // Reserva: abre a pesquisa certa no Catalog para baixar na mão e escolher a pasta.
                if (Atualizacoes.Versao(build) != null && MessageBox.Show(this,
                        $"Não consegui baixar as atualizações sozinho:\n{ex.Message}\n\nAbrir o Microsoft Update Catalog já pesquisado na versão certa? " +
                        "Baixe a \"Cumulative Update\" mais nova (sem \"Preview\"), escolha a pasta em \"Ou use uma pasta\" e crie de novo.\n\n" +
                        "Ou desmarque \"Baixar e instalar as atualizações\" para criar sem elas.",
                        "MontaWin", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(Atualizacoes.UrlPesquisa(build)) { UseShellExecute = true });
                return;
            }
        }
        try
        {
            var fundo = Path.Combine(Path.GetTempPath(), "MontaWin-fundo.png");
            var desfocado = Path.Combine(Path.GetTempPath(), "MontaWin-fundo-desfocado.png");
            SalvarFundo(FundoDoTema, fundo, desfocado: false);
            SalvarFundo(FundoDoTema, desfocado, desfocado: true);
            var gravacao = new Gravacao(iso, indices, pendrive, Implantador, Rapida.IsChecked == true, manter, atualizacoes, tema,
                (fundo, desfocado), BaixarProgramas.IsChecked == true);
            var falhas = await Task.Run(() => Pendrive.Gravar(gravacao, Progresso));
            Ocupado(false, "Pendrive pronto! Dê boot por ele no PC do cliente.");
            MessageBox.Show(this, "Pendrive pronto!\n\nDê boot por ele no PC do cliente: o painel MontaWin abre sozinho." +
                (falhas.Count > 0 ? $"\n\nNão deu para baixar: {string.Join(", ", falhas)}. Esses instalam pela internet no primeiro login." : ""),
                "MontaWin", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Ocupado(false, "Falhou. Veja a mensagem e tente de novo.");
            Erro(ex.Message);
        }
    }
}
