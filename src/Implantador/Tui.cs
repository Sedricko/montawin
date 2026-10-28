using System.Text;

namespace MontaWin.Implantador;

// Tela de texto: só usa caracteres que existem na fonte padrão do console do WinPE (CP437).
public static class Tui
{
    const ConsoleColor Borda = ConsoleColor.DarkGreen;
    const ConsoleColor Destaque = ConsoleColor.Green;

    public static string InfoSistema = "";

    static int Largura => Math.Max(Console.WindowWidth - 1, 40);
    static int Altura => Console.WindowHeight;

    public static void Iniciar()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "MontaWin";
        Console.CursorVisible = false;
    }

    static void Escrever(int x, int y, string texto, ConsoleColor cor, ConsoleColor fundo = ConsoleColor.Black)
    {
        if (y >= Altura) return;
        Console.SetCursorPosition(x, y);
        Console.ForegroundColor = cor;
        Console.BackgroundColor = fundo;
        Console.Write(texto);
        Console.ResetColor();
    }

    public static string Ajustar(string s, int n) => s.Length > n ? s[..n] : s.PadRight(n);

    static void Moldura(string rodape)
    {
        Console.BackgroundColor = ConsoleColor.Black;
        Console.Clear();
        const string nome = " MONTAWIN · Implantador";
        Escrever(0, 0, Ajustar(nome + InfoSistema.PadLeft(Largura - nome.Length), Largura), ConsoleColor.Black, Destaque);
        Escrever(0, Altura - 1, Ajustar(" " + rodape, Largura), ConsoleColor.DarkGray);
    }

    static int Caixa(int y, string titulo, IReadOnlyList<(string Texto, bool Marcado)> linhas, ConsoleColor cor = ConsoleColor.Gray)
    {
        int w = Largura;
        Escrever(0, y, "┌─ ", Borda);
        Escrever(3, y, titulo, Destaque);
        Escrever(3 + titulo.Length, y, " " + new string('─', Math.Max(0, w - titulo.Length - 5)) + "┐", Borda);
        for (int i = 0; i < linhas.Count; i++)
        {
            Escrever(0, y + 1 + i, "│", Borda);
            var (texto, marcado) = linhas[i];
            Escrever(1, y + 1 + i, " " + Ajustar(texto, w - 4) + " ", marcado ? ConsoleColor.Black : cor, marcado ? Destaque : ConsoleColor.Black);
            Escrever(w - 1, y + 1 + i, "│", Borda);
        }
        Escrever(0, y + 1 + linhas.Count, "└" + new string('─', w - 2) + "┘", Borda);
        return y + linhas.Count + 3;
    }

    static List<(string, bool)> Simples(IEnumerable<string> linhas) => linhas.Select(l => (l, false)).ToList();

    /// <summary>Lista navegável com setas. Retorna o índice escolhido ou -1 (Esc).</summary>
    public static int Menu(string titulo, IReadOnlyList<string> info, IReadOnlyList<string> opcoes, int selecionado = 0) =>
        Lista(titulo, info, opcoes, selecionado, null);

    /// <summary>Lista com caixas de marcar (Espaço marca, T marca/desmarca tudo). Retorna null no Esc.</summary>
    public static bool[]? Selecao(string titulo, IReadOnlyList<string> info, IReadOnlyList<string> opcoes, bool[] marcados)
    {
        var m = (bool[])marcados.Clone();
        return Lista(titulo, info, opcoes, 0, m) < 0 ? null : m;
    }

    static int Lista(string titulo, IReadOnlyList<string> info, IReadOnlyList<string> opcoes, int sel, bool[]? marcados)
    {
        Moldura(marcados == null ? "Setas: navegar   Enter: escolher   Esc: voltar"
                                 : "Setas: navegar   Espaço: marcar   T: todos   Enter: confirmar   Esc: voltar");
        int topo = 0;
        while (true)
        {
            int y = 2;
            if (info.Count > 0) y = Caixa(y, "Informações", Simples(info));
            // Rola a lista quando não cabe na tela
            int espaco = Math.Max(3, Altura - y - 3);
            topo = Math.Clamp(topo, Math.Max(0, sel - espaco + 1), sel);
            var linhas = new List<(string, bool)>();
            for (int i = topo; i < Math.Min(opcoes.Count, topo + espaco); i++)
            {
                var caixa = marcados == null ? "" : marcados[i] ? "[x] " : "[ ] ";
                linhas.Add(((i == sel ? " ► " : "   ") + caixa + opcoes[i], i == sel));
            }
            Caixa(y, opcoes.Count > espaco ? $"{titulo} ({sel + 1}/{opcoes.Count})" : titulo, linhas);
            switch (Console.ReadKey(true).Key)
            {
                case ConsoleKey.UpArrow: sel = (sel - 1 + opcoes.Count) % opcoes.Count; break;
                case ConsoleKey.DownArrow: sel = (sel + 1) % opcoes.Count; break;
                case ConsoleKey.Spacebar when marcados != null: marcados[sel] = !marcados[sel]; break;
                case ConsoleKey.T when marcados != null: Array.Fill(marcados, !marcados.All(x => x)); break;
                case ConsoleKey.Enter: return sel;
                case ConsoleKey.Escape: return -1;
            }
        }
    }

    public static void Mensagem(string titulo, IReadOnlyList<string> linhas, bool erro = false)
    {
        Moldura("Pressione qualquer tecla para continuar");
        Caixa(2, titulo, Simples(linhas), erro ? ConsoleColor.Red : ConsoleColor.Gray);
        Console.ReadKey(true);
    }

    /// <summary>Pede para digitar uma palavra exata antes de algo destrutivo.</summary>
    public static bool Confirmar(string titulo, IReadOnlyList<string> linhas, string palavra) =>
        Perguntar(titulo, linhas, $"Digite {palavra} e Enter para continuar, ou só Enter para cancelar")
            .Equals(palavra, StringComparison.OrdinalIgnoreCase);

    public static string Perguntar(string titulo, IReadOnlyList<string> linhas, string rodape)
    {
        Moldura(rodape);
        int y = Caixa(2, titulo, Simples(linhas), ConsoleColor.Yellow);
        Escrever(1, y, "> ", Destaque);
        Console.CursorVisible = true;
        var digitado = Console.ReadLine() ?? "";
        Console.CursorVisible = false;
        return digitado.Trim();
    }

    static string tituloProgresso = "", etapaProgresso = "";

    /// <summary>Abre a tela de progresso de uma etapa; depois atualize com <see cref="Progresso"/>.</summary>
    public static void Etapa(string titulo, string etapa)
    {
        Moldura("Aguarde, não desligue o computador");
        (tituloProgresso, etapaProgresso) = (titulo, etapa);
        Progresso(0);
    }

    public static void Progresso(double percentual)
    {
        int largura = Largura - 14;
        int cheio = (int)(Math.Clamp(percentual, 0, 100) / 100 * largura);
        Caixa(2, tituloProgresso, Simples([etapaProgresso, "", new string('█', cheio) + new string('░', largura - cheio) + $"  {percentual,3:0}%"]));
    }
}
