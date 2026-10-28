using System.Windows;
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

    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => { if (iso != null) Pendrive.FecharIso(iso.Iso); };
        Loaded += (_, _) =>
        {
            if (!File.Exists(Implantador)) Erro($"Não encontrei o Implantador.exe ao lado do MontaWin.exe:\n{Implantador}");
            Atualizar(this, new RoutedEventArgs());
        };
    }

    void Erro(string mensagem) => MessageBox.Show(this, mensagem, "MontaWin", MessageBoxButton.OK, MessageBoxImage.Error);

    void Ocupado(bool ocupado, string status)
    {
        BotaoIso.IsEnabled = BotaoAtualizar.IsEnabled = BotaoCriar.IsEnabled = ListaPendrives.IsEnabled = ListaEdicoes.IsEnabled = !ocupado;
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
            Ocupado(false, $"{iso.Edicoes.Count} edições encontradas.");
        }
        catch (Exception ex)
        {
            Ocupado(false, "Escolha a ISO e o pendrive.");
            Erro(ex.Message);
        }
    }

    async void Atualizar(object sender, RoutedEventArgs e)
    {
        Ocupado(true, "Procurando pendrives...");
        var pendrives = await Task.Run(() => Discos.Listar().Where(d => d.Usb).ToList());
        ListaPendrives.ItemsSource = pendrives;
        ListaPendrives.SelectedIndex = pendrives.Count > 0 ? 0 : -1;
        Ocupado(false, pendrives.Count > 0 ? "Escolha a ISO e o pendrive." : "Nenhum pendrive encontrado. Conecte um e clique em Atualizar.");
    }

    async void Criar(object sender, RoutedEventArgs e)
    {
        var indices = (ListaEdicoes.ItemsSource as IEnumerable<EdicaoItem>)?.Where(x => x.Marcada).Select(x => x.Indice).ToArray() ?? [];
        if (iso == null) { Erro("Escolha a ISO do Windows."); return; }
        if (indices.Length == 0) { Erro("Marque pelo menos uma edição."); return; }
        if (ListaPendrives.SelectedItem is not Disco pendrive) { Erro("Escolha o pendrive."); return; }
        if (!File.Exists(Implantador)) { Erro($"Não encontrei o Implantador.exe:\n{Implantador}"); return; }
        long necessario = ((long)Pendrive.BootMb << 20) + new FileInfo(iso.Install).Length + (1L << 30);
        if (pendrive.Tamanho < necessario)
        {
            Erro($"O pendrive é pequeno: precisa de pelo menos {Formatar.Tamanho(necessario)}.");
            return;
        }
        if (MessageBox.Show(this, $"TUDO o que estiver no pendrive será apagado:\n\n{pendrive.Descricao.Trim()}\n\nContinuar?",
                "MontaWin", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        Ocupado(true, "Começando...");
        try
        {
            var gravacao = new Gravacao(iso, indices, pendrive, Implantador);
            await Task.Run(() => Pendrive.Gravar(gravacao, (etapa, pct) => Dispatcher.Invoke(() =>
            {
                if (etapa != null) Status.Text = etapa;
                Barra.Value = pct;
            })));
            Ocupado(false, "Pendrive pronto! Dê boot por ele no PC do cliente.");
            MessageBox.Show(this, "Pendrive pronto!\n\nDê boot por ele no PC do cliente: o painel MontaWin abre sozinho.",
                "MontaWin", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Ocupado(false, "Falhou. Veja a mensagem e tente de novo.");
            Erro(ex.Message);
        }
    }
}
