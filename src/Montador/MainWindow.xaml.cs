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
            if (captura > 0 && captura + 1 < args.Length)
            {
                Directory.CreateDirectory(args[captura + 1]);
                try { Capturar(args[captura + 1]); }
                catch (Exception ex) { File.WriteAllText(Path.Combine(args[captura + 1], "erro.txt"), ex.ToString()); }
                Close();
                return;
            }
            if (!File.Exists(Implantador)) Erro($"Não encontrei o Implantador.exe ao lado do MontaWin.exe:\n{Implantador}");
            CarregarProgramas();
            Atualizar(this, new RoutedEventArgs());
        };
    }

    void TemaEscolhido(object sender, RoutedEventArgs e)
    {
        tema = (string)((RadioButton)sender).Tag;
        var c = Cores[tema];
        Brush Cor(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        Resources["CorPainel"] = Cor(c.Painel);
        Resources["CorBordaPainel"] = Cor(c.BordaPainel);
        Resources["CorCartao"] = Cor(c.Cartao);
        Resources["CorBorda"] = Cor(c.Borda);
        Resources["CorDestaque"] = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(c.De), (Color)ColorConverter.ConvertFromString(c.Ate), 0);
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
        CaminhoIso.Text = @"D:\ISOs\Win11_25H2_BrazilianPortuguese_x64.iso";
        ListaEdicoes.ItemsSource = new[] { new EdicaoItem(1, "Windows 11 Home"), new EdicaoItem(6, "Windows 11 Pro") };
        TituloEdicoes.Visibility = Visibility.Visible;
        ListaPendrives.ItemsSource = new[] { new Disco(2, 256L << 30, "Kingston DataTraveler Max", true, false, []) };
        ListaPendrives.SelectedIndex = 0;
        CarregarProgramas();
        BuscaPrograma.Text = "anydesk";
        Status.Text = "Copiando Windows 11 Pro (2 de 2)";
        Barra.Value = 62;
        foreach (RadioButton r in Temas.Children)
        {
            r.IsChecked = true;
            var conteudo = (FrameworkElement)Content;
            // A janela rola: uma imagem por "página", de cima até o fim.
            Rolagem.ScrollToTop();
            for (int pagina = 1; pagina <= 6; pagina++)
            {
                UpdateLayout();
                Salvar(conteudo, Path.Combine(pasta, $"montador-{tema}-{pagina}.png"), (int)conteudo.ActualWidth, (int)conteudo.ActualHeight);
                if (Rolagem.VerticalOffset >= Rolagem.ScrollableHeight - 1) break;
                Rolagem.ScrollToVerticalOffset(Rolagem.VerticalOffset + Rolagem.ViewportHeight * 0.9);
            }
            SalvarFundo(FundoDoTema, Path.Combine(pasta, $"fundo-{tema}.png"), desfocado: false);
            SalvarFundo(FundoDoTema, Path.Combine(pasta, $"fundo-desfocado-{tema}.png"), desfocado: true);
        }
    }

    void Erro(string mensagem) => MessageBox.Show(this, mensagem, "MontaWin", MessageBoxButton.OK, MessageBoxImage.Error);

    void Ocupado(bool ocupado, string status)
    {
        BotaoIso.IsEnabled = BotaoAtualizar.IsEnabled = BotaoCriar.IsEnabled = ListaPendrives.IsEnabled = ListaEdicoes.IsEnabled =
            Manter.IsEnabled = Rapida.IsEnabled = BotaoAtualizacoes.IsEnabled = Temas.IsEnabled = AtualizarAuto.IsEnabled = BaixarProgramas.IsEnabled = NetFx3.IsEnabled = Licenca.IsEnabled =
            BotaoProcurar.IsEnabled = BotaoAdicionar.IsEnabled = BotaoRemover.IsEnabled = BotaoArquivo.IsEnabled = BotaoSalvarProgramas.IsEnabled = !ocupado;
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
        var existente = ListaPendrives.SelectedItem is Disco d ? Pendrive.Existente(d) : null;
        Manter.Visibility = existente != null ? Visibility.Visible : Visibility.Collapsed;
        Manter.IsChecked = existente != null;
        BotaoSalvarProgramas.Visibility = existente != null ? Visibility.Visible : Visibility.Collapsed;
        // Primeira vez neste PC: a lista que o técnico já editou no pendrive vira a lista do MontaWin.
        // Licença já gravada nesse pendrive: aparece no campo, para não precisar digitar de novo.
        if (existente is (_, char comLicenca) && Licenca.Text.Length == 0 && File.Exists($@"{comLicenca}:\MontaWin\licenca.txt"))
            Licenca.Text = File.ReadAllText($@"{comLicenca}:\MontaWin\licenca.txt").Trim();
        if (existente is (_, char dados) && !File.Exists(ListaProgramas.Arquivo) && File.Exists($@"{dados}:\MontaWin\Programas.txt"))
        {
            ListaProgramas.Salvar(File.ReadAllText($@"{dados}:\MontaWin\Programas.txt"));
            CarregarProgramas();
        }
    }

    // ---------- Programas ----------

    void CarregarProgramas() =>
        ListaProgramasUi.ItemsSource = Programas.Ler(ListaProgramas.Ler()).Select(p => new ItemLista(p)).ToList();

    void RemoverPrograma(object sender, RoutedEventArgs e)
    {
        if (ListaProgramasUi.SelectedItem is not ItemLista item) { Erro("Escolha na lista o programa que vai sair."); return; }
        ListaProgramas.Salvar(ListaProgramas.Remover(ListaProgramas.Ler(), item.Programa.Nome));
        CarregarProgramas();
    }

    void BuscaTecla(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) ProcurarPrograma(sender, e);
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
        Ocupado(false, resultados.Count > 0 ? $"{resultados.Count} encontrados: escolha um e clique em Adicionar à lista."
                                            : "Nada encontrado no winget. Tente outro nome ou use um instalador que você já tem.");
    }

    void ResultadoEscolhido(object sender, RoutedEventArgs e)
    {
        if (ResultadosBusca.SelectedItem is not ResultadoBusca r) return;
        (ArquivoInstalador.Text, ParametrosInstalador.Text, NomePrograma.Text) = ("", "", r.Nome.TrimEnd('…'));
    }

    void EscolherInstalador(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFileDialog { Filter = "Instalador (*.exe;*.msi)|*.exe;*.msi" };
        if (dialogo.ShowDialog(this) != true) return;
        ResultadosBusca.SelectedIndex = -1;
        ArquivoInstalador.Text = dialogo.FileName;
        NomePrograma.Text = Path.GetFileNameWithoutExtension(dialogo.FileName);
        ParametrosInstalador.Text = ListaProgramas.ParametrosSugeridos(dialogo.FileName);
    }

    void AdicionarPrograma(object sender, RoutedEventArgs e)
    {
        var nome = NomePrograma.Text.Trim();
        if (nome.Length == 0) { Erro("Digite o nome que vai aparecer no painel."); return; }
        string valor;
        if (ArquivoInstalador.Text.Length > 0)
        {
            // Sem espaços no nome do arquivo: a linha da lista separa o arquivo dos parâmetros pelo espaço.
            var arquivo = Path.GetFileName(ArquivoInstalador.Text).Replace(' ', '_');
            Directory.CreateDirectory(ListaProgramas.PastaInstaladores);
            File.Copy(ArquivoInstalador.Text, Path.Combine(ListaProgramas.PastaInstaladores, arquivo), overwrite: true);
            valor = $"arquivo: {arquivo} {ParametrosInstalador.Text.Trim()}".TrimEnd();
        }
        else if (ResultadosBusca.SelectedItem is ResultadoBusca r) valor = r.Id;
        else { Erro("Procure um programa no winget ou escolha um arquivo instalador."); return; }
        ListaProgramas.Salvar(ListaProgramas.Adicionar(ListaProgramas.Ler(), nome, valor));
        CarregarProgramas();
        (BuscaPrograma.Text, ArquivoInstalador.Text, NomePrograma.Text, ParametrosInstalador.Text) = ("", "", "", "");
        ResultadosBusca.ItemsSource = null;
        Status.Text = $"{nome} adicionado. Ele vai para o pendrive ao criar (ou em \"Salvar programas no pendrive\").";
    }

    async void SalvarProgramasNoPendrive(object sender, RoutedEventArgs e)
    {
        if (ListaPendrives.SelectedItem is not Disco d || Pendrive.Existente(d) is not (_, char dados)) { Erro("Escolha um pendrive já gravado pelo MontaWin."); return; }
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
            Ocupado(false, "Programas salvos no pendrive." + (falhas.Count > 0 ? $" Não deu para baixar: {string.Join(", ", falhas)} (instalam pela internet)." : ""));
        }
        catch (Exception ex)
        {
            Ocupado(false, "Falhou ao salvar os programas.");
            Erro(ex.Message);
        }
    }

    async void Criar(object sender, RoutedEventArgs e)
    {
        var indices = (ListaEdicoes.ItemsSource as IEnumerable<EdicaoItem>)?.Where(x => x.Marcada).Select(x => x.Indice).ToArray() ?? [];
        if (iso == null) { Erro("Escolha a ISO do Windows."); return; }
        if (indices.Length == 0) { Erro("Marque pelo menos uma edição."); return; }
        if (ListaPendrives.SelectedItem is not Disco pendrive) { Erro("Escolha o pendrive."); return; }
        var licenca = Licenca.Text.Trim().Length == 0 ? null : Unattend.ChaveValida(Licenca.Text);
        if (Licenca.Text.Trim().Length > 0 && licenca == null) { Erro("A licença precisa ter o formato XXXXX-XXXXX-XXXXX-XXXXX-XXXXX."); return; }
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
                (fundo, desfocado), BaixarProgramas.IsChecked == true, (ListaProgramas.Ler(), ListaProgramas.PastaInstaladores),
                NetFx3.IsChecked == true, licenca);
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
