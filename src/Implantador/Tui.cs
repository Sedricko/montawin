using System.Diagnostics;
using System.Text;

namespace MontaWin.Implantador;

public enum TipoTela { Lista, Mensagem, Pergunta, Progresso, Contagem }
public enum Alerta { Nenhum, Atencao, Perigo }
public enum TipoItem { Opcao, Caixa, MarcarTodos, Continuar, Voltar }

public readonly record struct Item(string Texto, TipoItem Tipo = TipoItem.Opcao, bool Marcado = false);

/// <summary>Tecla (com o caractere digitado) ou clique num item da lista (<see cref="Item"/> &gt;= 0).</summary>
public readonly record struct Evento(ConsoleKey Tecla, char Caractere = '\0', int Item = -1);

/// <summary>O que aparece na tela agora. A tela de texto e a gráfica desenham o mesmo modelo.</summary>
public sealed record Tela
{
    public TipoTela Tipo { get; init; }
    public string Titulo { get; init; } = "";
    public IReadOnlyList<string> Info { get; init; } = [];
    public Alerta Alerta { get; init; }
    public IReadOnlyList<Item> Itens { get; init; } = [];
    public int Selecionado { get; init; }
    public string Rodape { get; init; } = "";
    // Pergunta
    public string Rotulo { get; init; } = "";
    public string Campo { get; init; } = "";
    public bool Oculto { get; init; }
    // Progresso
    public IReadOnlyList<string> Etapas { get; init; } = [];
    public int EtapaAtual { get; init; } = -1;
    public string Detalhe { get; init; } = "";
    public double Percentual { get; init; }
    public IReadOnlyList<string> Registro { get; init; } = [];
    public DateTime Inicio { get; init; }
    public TimeSpan? Restante { get; init; }
}

public interface ISaida
{
    void Mostrar(Tela tela);
    /// <summary>Espera um evento; null se passar <paramref name="ms"/> sem nada (-1 = espera para sempre).</summary>
    Evento? Ler(int ms = -1);
    /// <summary>Sai da frente para outro programa usar o console (ex.: prompt de comando).</summary>
    void Pausar(Action acao);
}

/// <summary>
/// Telas do painel. Gráfica em tela cheia quando o WinPE permite; senão, tela de texto.
/// Toda lista tem "← Voltar" e o Esc sempre volta um passo.
/// </summary>
public static class Tui
{
    public static readonly string[] Temas = ["ondas", "bloom", "pastel"];
    public static string Tema { get; private set; } = "ondas";
    public static string InfoSistema = "";
    static ISaida saida = new TelaTexto();

    /// <summary>Abre a tela. <paramref name="pasta"/> é a pasta MontaWin do pendrive (tema.txt e o fundo do tema).</summary>
    public static void Iniciar(string? pasta)
    {
        var arquivoTema = pasta == null ? null : Path.Combine(pasta, "tema.txt");
        if (File.Exists(arquivoTema) && File.ReadAllText(arquivoTema).Trim().ToLowerInvariant() is var t && Temas.Contains(t)) Tema = t;
        try { saida = TelaGrafica.Abrir(Tema, pasta); }
        catch (Exception e)
        {
            Comandos.Registrar($"Tela gráfica indisponível, usando a de texto: {e.Message}");
            ((TelaTexto)saida).Iniciar(Tema);
        }
    }

    static void Mostrar(Tela tela) => saida.Mostrar(tela);
    static Evento Ler() => saida.Ler() ?? default;
    public static void Pausar(Action acao) => saida.Pausar(acao);

    static bool Mover(ref int sel, ConsoleKey tecla, int total)
    {
        int antes = sel;
        sel = tecla switch
        {
            ConsoleKey.UpArrow => (sel - 1 + total) % total,
            ConsoleKey.DownArrow => (sel + 1) % total,
            ConsoleKey.PageUp => Math.Max(0, sel - 8),
            ConsoleKey.PageDown => Math.Min(total - 1, sel + 8),
            ConsoleKey.Home => 0,
            ConsoleKey.End => total - 1,
            _ => sel,
        };
        return sel != antes;
    }

    /// <summary>Lista de opções. Retorna o índice escolhido ou -1 (Voltar/Esc). <paramref name="voltar"/> null = sem voltar (menu principal).</summary>
    public static int Menu(string titulo, IReadOnlyList<string> info, IReadOnlyList<string> opcoes, int selecionado = 0,
        string? voltar = "← Voltar", Alerta alerta = Alerta.Nenhum)
    {
        var itens = opcoes.Select(o => new Item(o)).ToList();
        if (voltar != null) itens.Add(new Item(voltar, TipoItem.Voltar));
        if (itens.Count == 0) return -1;
        int sel = Math.Clamp(selecionado, 0, itens.Count - 1);
        while (true)
        {
            Mostrar(new Tela
            {
                Tipo = TipoTela.Lista, Titulo = titulo, Info = info, Itens = itens, Selecionado = sel, Alerta = alerta,
                Rodape = voltar != null ? "Setas: navegar    Enter: escolher    Esc: voltar" : "Setas: navegar    Enter: escolher",
            });
            var e = Ler();
            if (e.Item >= 0 && e.Item < itens.Count) sel = e.Item;
            else if (e.Tecla == ConsoleKey.Escape && voltar != null) return -1;
            else if (e.Tecla != ConsoleKey.Enter) { Mover(ref sel, e.Tecla, itens.Count); continue; }
            return itens[sel].Tipo == TipoItem.Voltar ? -1 : sel;
        }
    }

    /// <summary>Lista com caixas de marcar: Enter ou Espaço marca, "Continuar" confirma. Retorna null em Voltar/Esc.</summary>
    public static bool[]? Selecao(string titulo, IReadOnlyList<string> info, IReadOnlyList<string> opcoes, bool[] marcados)
    {
        var m = (bool[])marcados.Clone();
        int sel = opcoes.Count > 0 ? 1 : 1 + opcoes.Count;
        while (true)
        {
            bool todos = m.Length > 0 && m.All(x => x);
            var itens = new List<Item> { new(todos ? "Desmarcar todos" : "Marcar todos", TipoItem.MarcarTodos, todos) };
            itens.AddRange(opcoes.Select((o, i) => new Item(o, TipoItem.Caixa, m[i])));
            int continuar = itens.Count;
            itens.Add(new Item($"Continuar →   ({m.Count(x => x)} marcado{(m.Count(x => x) == 1 ? "" : "s")})", TipoItem.Continuar));
            itens.Add(new Item("← Voltar", TipoItem.Voltar));
            Mostrar(new Tela
            {
                Tipo = TipoTela.Lista, Titulo = titulo, Info = info, Itens = itens, Selecionado = sel,
                Rodape = "Enter ou Espaço: marcar/desmarcar    Tab: ir para Continuar    Esc: voltar",
            });
            var e = Ler();
            if (e.Item >= 0 && e.Item < itens.Count) sel = e.Item;
            else if (e.Tecla == ConsoleKey.Escape) return null;
            else if (e.Tecla == ConsoleKey.Tab) { sel = continuar; continue; }
            else if (e.Tecla == ConsoleKey.T) { Array.Fill(m, !todos); continue; }
            else if (e.Tecla is not (ConsoleKey.Enter or ConsoleKey.Spacebar)) { Mover(ref sel, e.Tecla, itens.Count); continue; }
            switch (itens[sel].Tipo)
            {
                case TipoItem.MarcarTodos: Array.Fill(m, !todos); break;
                case TipoItem.Caixa: m[sel - 1] = !m[sel - 1]; break;
                case TipoItem.Continuar: return m;
                case TipoItem.Voltar: return null;
            }
        }
    }

    public static void Mensagem(string titulo, IReadOnlyList<string> linhas, bool erro = false)
    {
        Mostrar(new Tela
        {
            Tipo = TipoTela.Mensagem, Titulo = titulo, Info = linhas, Alerta = erro ? Alerta.Perigo : Alerta.Nenhum,
            Itens = [new Item("Continuar", TipoItem.Continuar)], Rodape = "Enter: continuar",
        });
        Ler();
    }

    /// <summary>Lê um texto digitado. Retorna null no Esc/Voltar; com <paramref name="oculto"/> mostra • no lugar (senhas).</summary>
    public static string? Perguntar(string titulo, IReadOnlyList<string> linhas, string rotulo, bool oculto = false,
        Alerta alerta = Alerta.Nenhum, string inicial = "", string continuar = "Continuar →")
    {
        var texto = new StringBuilder(inicial);
        while (true)
        {
            Mostrar(new Tela
            {
                Tipo = TipoTela.Pergunta, Titulo = titulo, Info = linhas, Rotulo = rotulo, Campo = texto.ToString(), Oculto = oculto,
                Alerta = alerta, Itens = [new Item(continuar, TipoItem.Continuar), new Item("← Voltar", TipoItem.Voltar)],
                Rodape = "Enter: continuar    Esc: voltar",
            });
            var e = Ler();
            if (e.Item == 1 || e.Tecla == ConsoleKey.Escape) return null;
            if (e.Item == 0 || e.Tecla == ConsoleKey.Enter) return oculto ? texto.ToString() : texto.ToString().Trim();
            if (e.Tecla == ConsoleKey.Backspace) { if (texto.Length > 0) texto.Length--; }
            else if (e.Caractere >= ' ' && texto.Length < 120) texto.Append(e.Caractere);
        }
    }

    /// <summary>Pede para digitar a palavra (SIM) antes de algo destrutivo. Esc ou Enter vazio = não.</summary>
    public static bool Confirmar(string titulo, IReadOnlyList<string> linhas, string acao = "Confirmar", string palavra = "SIM") =>
        Perguntar(titulo, linhas, $"Para confirmar, digite {palavra} e aperte Enter", alerta: Alerta.Perigo, continuar: acao)?
            .Equals(palavra, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Contagem regressiva; qualquer tecla cancela. Retorna true se chegou a zero.</summary>
    public static bool Contagem(string titulo, IReadOnlyList<string> linhas, string acao, int segundos)
    {
        for (int s = segundos; s > 0; s--)
        {
            Mostrar(new Tela
            {
                Tipo = TipoTela.Contagem, Titulo = titulo, Info = [.. linhas, "", $"{acao} em {s} s..."],
                Itens = [new Item("Cancelar", TipoItem.Voltar)], Rodape = "Qualquer tecla cancela",
            });
            var limite = Stopwatch.StartNew();
            while (limite.ElapsedMilliseconds < 1000)
                if (saida.Ler((int)(1000 - limite.ElapsedMilliseconds)) != null) return false;
        }
        return true;
    }

    // Progresso: tela simples (uma etapa) ou lista de etapas com pesos (instalação).
    static string tituloProgresso = "", detalhe = "";
    static string[] nomesEtapas = [];
    static double[] pesos = [];
    static int etapaAtual = -1;
    static double percentualEtapa;
    static readonly List<string> registro = [];
    static DateTime inicio;
    static long ultimoDesenho;

    /// <summary>Começa uma tarefa com várias etapas; o peso é o tempo aproximado de cada uma (para a barra e o tempo restante).</summary>
    public static void Etapas(string titulo, IReadOnlyList<(string Nome, double Peso)> etapas)
    {
        tituloProgresso = titulo;
        nomesEtapas = etapas.Select(x => x.Nome).ToArray();
        pesos = etapas.Select(x => x.Peso).ToArray();
        (etapaAtual, percentualEtapa, detalhe, inicio) = (-1, 0, "", DateTime.Now);
        lock (registro) registro.Clear();
    }

    /// <summary>Passa para a etapa <paramref name="indice"/> da lista de <see cref="Etapas"/>.</summary>
    public static void Etapa(int indice, string texto)
    {
        (etapaAtual, percentualEtapa, detalhe) = (indice, 0, texto);
        DesenharProgresso(forcar: true);
    }

    /// <summary>Tela de progresso simples (sem lista de etapas).</summary>
    public static void Etapa(string titulo, string texto)
    {
        if (titulo != tituloProgresso || nomesEtapas.Length > 0)
        {
            Etapas(titulo, []);
        }
        (percentualEtapa, detalhe) = (0, texto);
        DesenharProgresso(forcar: true);
    }

    public static void Progresso(double percentual)
    {
        percentualEtapa = Math.Clamp(percentual, 0, 100);
        DesenharProgresso(forcar: percentual >= 100);
    }

    /// <summary>Linha na caixa "O que está acontecendo" (e no registro do pendrive).</summary>
    public static void Registro(string texto)
    {
        Comandos.Registrar(texto);
        lock (registro)
        {
            registro.Add(texto);
            if (registro.Count > 40) registro.RemoveAt(0);
        }
        DesenharProgresso(forcar: true);
    }

    /// <summary>Percentual total: etapas prontas + a parte feita da atual, pelos pesos.</summary>
    public static double PercentualTotal(double[] pesos, int atual, double percentualAtual)
    {
        if (pesos.Length == 0) return percentualAtual;
        double total = pesos.Sum();
        if (total <= 0 || atual < 0) return 0;
        double feito = pesos.Take(atual).Sum() + pesos[Math.Min(atual, pesos.Length - 1)] * percentualAtual / 100;
        return Math.Clamp(feito / total * 100, 0, 100);
    }

    /// <summary>Estimativa pelo ritmo até agora; só depois de um tempo, para não chutar no começo.</summary>
    public static TimeSpan? Restante(TimeSpan decorrido, double percentual) =>
        percentual < 3 || percentual >= 100 || decorrido.TotalSeconds < 20 ? null
            : TimeSpan.FromSeconds(decorrido.TotalSeconds * (100 - percentual) / percentual);

    static void DesenharProgresso(bool forcar)
    {
        // O DISM manda o percentual muitas vezes por segundo: redesenha no máximo 5 vezes por segundo.
        long agora = Environment.TickCount64;
        if (!forcar && agora - ultimoDesenho < 200) return;
        ultimoDesenho = agora;
        double total = PercentualTotal(pesos, etapaAtual, percentualEtapa);
        string[] linhas;
        lock (registro) linhas = registro.ToArray();
        Mostrar(new Tela
        {
            Tipo = TipoTela.Progresso, Titulo = tituloProgresso, Etapas = nomesEtapas, EtapaAtual = etapaAtual, Detalhe = detalhe,
            Percentual = total, Registro = linhas, Inicio = inicio, Restante = Restante(DateTime.Now - inicio, total),
            Rodape = "Aguarde, não desligue o computador",
        });
    }

    public static string Duracao(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:00} min" : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds:00} s" : $"{t.Seconds} s";
}

/// <summary>Tela de texto: só usa caracteres que existem na fonte padrão do console do WinPE (CP437).</summary>
sealed class TelaTexto : ISaida
{
    ConsoleColor borda = ConsoleColor.DarkMagenta, destaque = ConsoleColor.Magenta;
    Tela? anterior;

    public void Iniciar(string tema)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "MontaWin";
        Console.CursorVisible = false;
        (borda, destaque) = tema switch
        {
            "bloom" => (ConsoleColor.DarkCyan, ConsoleColor.Cyan),
            "pastel" => (ConsoleColor.DarkBlue, ConsoleColor.Blue),
            _ => (ConsoleColor.DarkMagenta, ConsoleColor.Magenta),
        };
    }

    static int Largura => Math.Max(Console.WindowWidth - 1, 40);
    static int Altura => Console.WindowHeight;

    static void Escrever(int x, int y, string texto, ConsoleColor cor, ConsoleColor fundo = ConsoleColor.Black)
    {
        if (y >= Altura - 1 || y < 0) return;
        Console.SetCursorPosition(x, y);
        Console.ForegroundColor = cor;
        Console.BackgroundColor = fundo;
        Console.Write(texto);
        Console.ResetColor();
    }

    int Caixa(int y, string titulo, IReadOnlyList<(string Texto, bool Marcado)> linhas, ConsoleColor cor = ConsoleColor.Gray)
    {
        int w = Largura;
        Escrever(0, y, "┌─ ", borda);
        Escrever(3, y, titulo, destaque);
        Escrever(3 + titulo.Length, y, " " + new string('─', Math.Max(0, w - titulo.Length - 5)) + "┐", borda);
        for (int i = 0; i < linhas.Count; i++)
        {
            Escrever(0, y + 1 + i, "│", borda);
            var (texto, marcado) = linhas[i];
            Escrever(1, y + 1 + i, " " + Formatar.Ajustar(texto, w - 4) + " ", marcado ? ConsoleColor.Black : cor, marcado ? destaque : ConsoleColor.Black);
            Escrever(w - 1, y + 1 + i, "│", borda);
        }
        Escrever(0, y + 1 + linhas.Count, "└" + new string('─', w - 2) + "┘", borda);
        return y + linhas.Count + 3;
    }

    static List<(string, bool)> Simples(IEnumerable<string> linhas) => linhas.Select(l => (l, false)).ToList();

    public void Mostrar(Tela t)
    {
        // Limpa só quando a tela muda de verdade; ao mexer na seleção ou no texto, só reescreve (sem piscar).
        if (anterior == null || anterior.Tipo != t.Tipo || anterior.Titulo != t.Titulo || anterior.Info.Count != t.Info.Count
            || anterior.Itens.Count != t.Itens.Count || anterior.Etapas.Count != t.Etapas.Count || anterior.Registro.Count != t.Registro.Count)
        {
            Console.BackgroundColor = ConsoleColor.Black;
            Console.Clear();
            const string nome = " MONTAWIN · Implantador";
            Escrever(0, 0, Formatar.Ajustar(nome + Tui.InfoSistema.PadLeft(Largura - nome.Length), Largura), ConsoleColor.Black, destaque);
        }
        // Teclas apertadas durante uma tarefa longa não podem escolher nada na tela seguinte.
        if (anterior?.Tipo == TipoTela.Progresso && t.Tipo != TipoTela.Progresso) while (Console.KeyAvailable) Console.ReadKey(true);
        anterior = t;
        Console.SetCursorPosition(0, Altura - 1);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(Formatar.Ajustar(" " + t.Rodape, Largura));
        Console.ResetColor();

        var cor = t.Alerta switch { Alerta.Perigo => ConsoleColor.Red, Alerta.Atencao => ConsoleColor.Yellow, _ => ConsoleColor.Gray };
        int y = 2;
        switch (t.Tipo)
        {
            case TipoTela.Lista:
            case TipoTela.Mensagem:
            case TipoTela.Contagem:
                if (t.Tipo != TipoTela.Lista) { Caixa(y, t.Titulo, Simples(t.Info), cor); break; }
                if (t.Info.Count > 0) y = Caixa(y, "Informações", Simples(t.Info), cor);
                // Rola a lista quando não cabe na tela
                int espaco = Math.Max(3, Altura - y - 3);
                int topo = Math.Clamp(0, Math.Max(0, t.Selecionado - espaco + 1), t.Selecionado);
                var linhas = new List<(string, bool)>();
                for (int i = topo; i < Math.Min(t.Itens.Count, topo + espaco); i++)
                {
                    var item = t.Itens[i];
                    var caixa = item.Tipo is TipoItem.Caixa or TipoItem.MarcarTodos ? (item.Marcado ? "[x] " : "[ ] ") : "";
                    linhas.Add(((i == t.Selecionado ? " ► " : "   ") + caixa + item.Texto, i == t.Selecionado));
                }
                Caixa(y, t.Itens.Count > espaco ? $"{t.Titulo} ({t.Selecionado + 1}/{t.Itens.Count})" : t.Titulo, linhas);
                break;
            case TipoTela.Pergunta:
                y = Caixa(y, t.Titulo, Simples(t.Info.Append("").Append(t.Rotulo)), t.Alerta == Alerta.Nenhum ? ConsoleColor.Yellow : cor);
                Escrever(1, y, Formatar.Ajustar("> " + (t.Oculto ? new string('*', t.Campo.Length) : t.Campo) + "_", Largura - 2), destaque);
                break;
            case TipoTela.Progresso:
                var corpo = new List<string>();
                for (int i = 0; i < t.Etapas.Count; i++)
                    corpo.Add((i < t.EtapaAtual ? " √ " : i == t.EtapaAtual ? " ► " : "   ") + t.Etapas[i]);
                if (corpo.Count > 0) corpo.Add("");
                int largura = Largura - 14;
                int cheio = (int)(t.Percentual / 100 * largura);
                var decorrido = DateTime.Now - t.Inicio;
                corpo.Add(t.Detalhe);
                corpo.Add(new string('█', cheio) + new string('░', largura - cheio) + $"  {t.Percentual,3:0}%");
                corpo.Add($"Decorrido: {Tui.Duracao(decorrido)}" + (t.Restante is TimeSpan r ? $"   ·   Restante: ~{Tui.Duracao(r)}" : ""));
                y = Caixa(y, t.Titulo, Simples(corpo));
                int cabe = Math.Max(1, Altura - y - 3);
                if (t.Registro.Count > 0) Caixa(y, "O que está acontecendo", Simples(t.Registro.TakeLast(cabe)), ConsoleColor.DarkGray);
                break;
        }
    }

    public Evento? Ler(int ms = -1)
    {
        var limite = Stopwatch.StartNew();
        while (ms >= 0 && !Console.KeyAvailable)
        {
            if (limite.ElapsedMilliseconds >= ms) return null;
            Thread.Sleep(50);
        }
        var k = Console.ReadKey(true);
        return new Evento(k.Key, k.KeyChar);
    }

    public void Pausar(Action acao)
    {
        Console.Clear();
        Console.CursorVisible = true;
        acao();
        Console.CursorVisible = false;
        anterior = null;
    }
}
