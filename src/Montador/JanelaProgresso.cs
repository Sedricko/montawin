using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Caminho = System.Windows.Shapes.Path;

namespace MontaWin.Montador;

/// <summary>
/// Janelinha flutuante que acompanha a gravação do pendrive: anel com o percentual (girando enquanto trabalha),
/// a etapa atual, o tempo restante e a lista do que já foi feito. No fim mostra o resultado e volta para o app.
/// </summary>
public sealed class JanelaProgresso : Window
{
    readonly Caminho arco = new() { StrokeThickness = 10, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    readonly Ellipse giro = new() { Width = 140, Height = 140, StrokeThickness = 3, StrokeDashArray = [2, 10], Opacity = 0.55 };
    readonly TextBlock pct = new() { FontSize = 30, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly Caminho marca = new() { Data = Geometry.Parse("M48 72 L64 88 L94 56"), StrokeThickness = 8, StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Visibility = Visibility.Collapsed };
    readonly TextBlock etapa = new() { FontSize = 17, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock tempo = new() { Opacity = 0.7, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) };
    readonly StackPanel feitas = new();
    readonly Button botao;
    readonly DateTime inicio = DateTime.Now;
    string? atual;

    public JanelaProgresso(Brush destaque, Brush painel, Brush borda, Brush texto, ImageSource? icone, string pendrive)
    {
        (Width, SizeToContent) = (420, SizeToContent.Height);
        (WindowStyle, AllowsTransparency, Background, ResizeMode) = (WindowStyle.None, true, Brushes.Transparent, ResizeMode.NoResize);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = "Aurora Win · gravando o pendrive";
        Icon = icone;
        arco.Stroke = giro.Stroke = marca.Stroke = destaque;

        var topo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        if (icone != null) topo.Children.Add(new Image { Source = icone, Width = 26, Height = 26, Margin = new Thickness(0, 0, 10, 0) });
        topo.Children.Add(new TextBlock { Text = "Aurora Win · gravando o pendrive", Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center });

        // Anel: trilho, arco do percentual, um tracejado que gira (mostra que não travou) e o check no fim
        var anel = new Grid { Width = 140, Height = 140, Margin = new Thickness(0, 16, 0, 14) };
        anel.Children.Add(new Ellipse { Width = 130, Height = 130, StrokeThickness = 10, Stroke = borda });
        anel.Children.Add(arco);
        anel.Children.Add(giro);
        anel.Children.Add(pct);
        anel.Children.Add(marca);
        giro.RenderTransformOrigin = new Point(0.5, 0.5);
        var rotacao = new RotateTransform();
        giro.RenderTransform = rotacao;
        rotacao.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(4)) { RepeatBehavior = RepeatBehavior.Forever });

        botao = new Button { Content = "Minimizar", HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.Hand, Foreground = texto,
            Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse( // pílula no visual do app (o botão padrão ficava cinza)
                "<ControlTemplate TargetType='Button' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                "<Border Background='#1FFFFFFF' BorderBrush='#33FFFFFF' BorderThickness='1' CornerRadius='14' Padding='16,6'>" +
                "<ContentPresenter HorizontalAlignment='Center' /></Border></ControlTemplate>") };
        botao.Click += (_, _) => { if (marca.Visibility == Visibility.Visible || giro.Visibility == Visibility.Collapsed) Close(); else WindowState = WindowState.Minimized; };
        var rodape = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(botao, Dock.Right);
        rodape.Children.Add(botao);
        rodape.Children.Add(new TextBlock { Text = pendrive, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });

        var conteudo = new StackPanel();
        foreach (var e in new UIElement[] { topo, anel, etapa, tempo, feitas, rodape }) conteudo.Children.Add(e);
        anel.HorizontalAlignment = HorizontalAlignment.Center;
        var moldura = new Border
        {
            Margin = new Thickness(16), Padding = new Thickness(26, 22, 26, 18), CornerRadius = new CornerRadius(24),
            Background = painel, BorderBrush = borda, BorderThickness = new Thickness(1), Child = conteudo,
            Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.45 },
        };
        TextElement.SetForeground(moldura, texto);
        Content = moldura;
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Atualizar("Começando...", 0);
    }

    /// <summary>Nova etapa (ou null = a mesma) e o percentual geral.</summary>
    public void Atualizar(string? novaEtapa, double percentual)
    {
        if (novaEtapa != null && novaEtapa != atual)
        {
            if (atual != null && atual != "Começando...") Feita(atual);
            atual = novaEtapa;
            etapa.Text = novaEtapa;
        }
        percentual = Math.Clamp(percentual, 0, 100);
        pct.Text = $"{percentual:0}%";
        arco.Data = Arco(percentual);
        var passou = DateTime.Now - inicio;
        tempo.Text = percentual < 3 ? "Calculando o tempo..." : $"Restante: ~{Math.Max(1, Math.Round(passou.TotalMinutes * (100 - percentual) / percentual))} min";
    }

    /// <summary>Fim: check (ou o X do erro), a mensagem e o botão para voltar ao app.</summary>
    public void Concluir(bool ok, string titulo, string mensagem)
    {
        if (atual != null) Feita(atual);
        giro.BeginAnimation(OpacityProperty, null);
        giro.Visibility = Visibility.Collapsed;
        pct.Visibility = Visibility.Collapsed;
        arco.Data = Arco(ok ? 100 : 0);
        if (ok) marca.Visibility = Visibility.Visible;
        else pct.Visibility = Visibility.Visible;
        if (!ok) pct.Text = "!";
        etapa.Text = titulo;
        tempo.Text = mensagem;
        botao.Content = "Voltar ao Aurora Win";
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>Texto da lista do que já foi feito, sem a frase repetida: "Baixando programas para o pendrive: Notepad++ (5 de 31)" vira "Notepad++ baixado".</summary>
    public static string Curto(string etapa)
    {
        etapa = System.Text.RegularExpressions.Regex.Replace(etapa, @"\s*\(\d+ de \d+\)$", "");
        const string baixando = "Baixando programas para o pendrive: ";
        return etapa.StartsWith(baixando) ? etapa[baixando.Length..] + " baixado" : etapa;
    }

    void Feita(string texto)
    {
        if (feitas.Children.Count >= 6) feitas.Children.RemoveAt(0); // só as últimas, para a janela não crescer
        var linha = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        linha.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Foreground = arco.Stroke,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        linha.Children.Add(new TextBlock { Text = Curto(texto), Opacity = 0.75, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 });
        feitas.Children.Add(linha);
    }

    // Arco do percentual no anel (raio 65, começando em cima, no sentido do relógio)
    static Geometry Arco(double percentual)
    {
        const double c = 70, r = 65;
        if (percentual >= 99.9) return new EllipseGeometry(new Point(c, c), r, r);
        if (percentual <= 0.1) return Geometry.Empty;
        double a = percentual / 100 * 2 * Math.PI;
        var fim = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
        var figura = new PathFigure { StartPoint = new Point(c, c - r) };
        figura.Segments.Add(new ArcSegment(fim, new Size(r, r), 0, percentual > 50, SweepDirection.Clockwise, true));
        return new PathGeometry([figura]);
    }
}
