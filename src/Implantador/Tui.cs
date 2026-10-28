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
    public static int Menu(string titulo, IReadOnlyList<string> info, IReadOnlyList<string> opcoes, int selecionado = 0)
    {
        Moldura("Setas: navegar   Enter: escolher   Esc: voltar");
        while (true)
        {
            int y = 2;
            if (info.Count > 0) y = Caixa(y, "Informações", Simples(info));
            Caixa(y, titulo, opcoes.Select((o, i) => ((i == selecionado ? " ► " : "   ") + o, i == selecionado)).ToList());
            switch (Console.ReadKey(true).Key)
            {
                case ConsoleKey.UpArrow: selecionado = (selecionado - 1 + opcoes.Count) % opcoes.Count; break;
                case ConsoleKey.DownArrow: selecionado = (selecionado + 1) % opcoes.Count; break;
                case ConsoleKey.Enter: return selecionado;
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
    public static bool Confirmar(string titulo, IReadOnlyList<string> linhas, string palavra)
    {
        Moldura($"Digite {palavra} e Enter para continuar, ou só Enter para cancelar");
        int y = Caixa(2, titulo, Simples(linhas), ConsoleColor.Yellow);
        Escrever(1, y, "> ", Destaque);
        Console.CursorVisible = true;
        var digitado = Console.ReadLine() ?? "";
        Console.CursorVisible = false;
        return digitado.Trim().Equals(palavra, StringComparison.OrdinalIgnoreCase);
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
