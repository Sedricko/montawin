using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MontaWin.Implantador;

/// <summary>
/// Painel em tela cheia desenhado com GDI+ (o WinPE não tem DWM: nada de desfoque ao vivo). O fundo do tema e a versão
/// desfocada dele vêm prontos do Montador (fundo.png e fundo-desfocado.png); o "vidro" é a versão desfocada recortada
/// no painel. A janela roda numa thread própria, para continuar desenhando enquanto o DISM trabalha.
/// </summary>
public sealed unsafe partial class TelaGrafica : ISaida
{
    readonly record struct Cores(uint Fundo1, uint Fundo2, uint Painel, uint BordaPainel, uint Texto, uint Suave, uint Cartao,
        uint Borda, uint Campo, uint Destaque1, uint Destaque2, uint CorDestaque, uint Perigo, uint Atencao);

    static readonly Dictionary<string, Cores> Temas = new()
    {
        ["ondas"] = new(0xFF1A0B52, 0xFF131C6E, 0xC20C0824, 0x1AFFFFFF, 0xFFF2EEFF, 0xADF2EEFF, 0x0FFFFFFF, 0x1AFFFFFF, 0x73000000,
            0xFFE0218A, 0xFF3B7BFF, 0xFFFF7AC0, 0xFFFF6B7D, 0xFFFFD66B),
        ["bloom"] = new(0xFF0B0D12, 0xFF171B25, 0xF212151C, 0x1FFFFFFF, 0xFFE8EBF2, 0xADE8EBF2, 0x0DFFFFFF, 0x1FFFFFFF, 0x66000000,
            0xFF4C8DFF, 0xFF7AB0FF, 0xFF7AB0FF, 0xFFFF6B7D, 0xFFFFD66B),
        ["pastel"] = new(0xFFEEF3FB, 0xFFDBE6F7, 0xF0FFFFFF, 0x262B4A8A, 0xFF1B2640, 0xAD1B2640, 0x0D2B4A8A, 0x262B4A8A, 0xCCFFFFFF,
            0xFF2F7BF0, 0xFF6AA5FF, 0xFF2F7BF0, 0xFFC81E3A, 0xFFA86A00),
    };

    static TelaGrafica? atual; // a janela chama o WndProc estático
    readonly Cores c;
    readonly string? pasta;
    readonly BlockingCollection<Evento> fila = new();
    readonly List<(float X, float Y, float W, float H, int Item)> alvos = []; // onde cada item foi desenhado (clique)
    nint janela, memDc, memBmp, fundo, desfocado, icone;
    int hover = -1; // item sob o mouse (destaque), -1 = nenhum
    int largura, altura;
    float s; // escala: o layout foi pensado em 1280x720
    nint fonteTitulo, fonteNormal, fontePequena, fonteForte, fonteGrande, fonteMono;
    nint linha, linhaCentro, linhaDireita, paragrafo;
    Tela? tela;
    Tela? pintada;          // a tela cujos alvos estão em "alvos"
    (string, IReadOnlyList<string>)? telaPintada; // título e textos: o que muda de uma tela para outra
    long novaEm;            // quando uma tela com outro título apareceu
    int topoLista;
    string tituloLista = "";

    TelaGrafica(string tema, string? pasta) => (c, this.pasta) = (Temas.GetValueOrDefault(tema, Temas["ondas"]), pasta);

    public static TelaGrafica Abrir(string tema, string? pasta)
    {
        var t = new TelaGrafica(tema, pasta);
        Exception? erro = null;
        using var pronta = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try { t.Criar(); }
            catch (Exception e) { erro = e; }
            pronta.Set();
            if (erro == null) t.Laco();
        }) { IsBackground = true, Name = "Tela" };
        thread.Start();
        pronta.Wait();
        if (erro != null) throw erro;
        ShowWindow(GetConsoleWindow(), 0); // a janela de texto fica escondida atrás
        return t;
    }

    void Criar()
    {
        Recursos(GetSystemMetrics(0), GetSystemMetrics(1));
        var classe = new WndClass
        {
            Tamanho = (uint)sizeof(WndClass), Procedimento = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&Procedimento,
            Instancia = GetModuleHandleW(null), Cursor = LoadCursorW(0, 32512),
        };
        fixed (char* nome = "AuroraWin")
        {
            classe.Nome = (nint)nome;
            if (RegisterClassExW(&classe) == 0) throw new Exception("RegisterClassEx falhou");
        }
        atual = this;
        janela = CreateWindowExW(0x40000, "AuroraWin", "Aurora Win", 0x80000000 | 0x10000000, 0, 0, largura, altura, 0, 0, classe.Instancia, 0);
        if (janela == 0) throw new Exception("CreateWindowEx falhou");
        var tela = GetDC(janela);
        memDc = CreateCompatibleDC(tela);
        memBmp = CreateCompatibleBitmap(tela, largura, altura);
        SelectObject(memDc, memBmp);
        ReleaseDC(janela, tela);
        SetTimer(janela, 1, 1000, 0); // relógio da tela de progresso
        SetForegroundWindow(janela);
        SetFocus(janela);
    }

    /// <summary>GDI+, fontes e fundo para uma tela de <paramref name="w"/> x <paramref name="h"/>.</summary>
    void Recursos(int w, int h)
    {
        var entrada = new StartupInput { Versao = 1 };
        nuint token;
        Ok(GdiplusStartup(&token, &entrada, 0), "GdiplusStartup");
        (largura, altura) = (w, h);
        if (largura < 640 || altura < 480) throw new Exception($"resolução {largura}x{altura} pequena demais");
        s = Math.Clamp(Math.Min(largura / 1280f, altura / 720f), 0.8f, 2.5f);

        // O WinPE não traz a Segoe UI completa (o texto saía inclinado): o Montador copia as fontes para MontaWin\Fontes
        nint nova = 0, colecao = 0;
        var fontes = pasta == null ? null : Path.Combine(pasta, "Fontes");
        if (fontes != null && Directory.Exists(fontes) && GdipNewPrivateFontCollection(&nova) == 0) colecao = nova;
        if (colecao != 0)
            foreach (var arquivo in Directory.GetFiles(fontes, "*.ttf"))
                GdipPrivateAddFontFile(colecao, arquivo);
        bool Familia(string nome, nint* f) => (colecao != 0 && GdipCreateFontFamilyFromName(nome, colecao, f) == 0) || GdipCreateFontFamilyFromName(nome, 0, f) == 0;

        nint familia = 0, forte = 0;
        if (!Familia("Segoe UI", &familia)) Ok(GdipGetGenericFontFamilySansSerif(&familia), "fonte");
        bool semibold = Familia("Segoe UI Semibold", &forte);
        if (!semibold) forte = familia;
        fonteTitulo = Fonte(forte, 26, semibold ? 0 : 1);
        fonteForte = Fonte(forte, 16, semibold ? 0 : 1);
        fonteGrande = Fonte(forte, 46, semibold ? 0 : 1);
        fonteNormal = Fonte(familia, 16, 0);
        fontePequena = Fonte(familia, 13, 0);
        nint mono = 0;
        if (!Familia("Consolas", &mono) && !Familia("Courier New", &mono))
            mono = familia;
        fonteMono = Fonte(mono, 14, 0);
        linha = Formato(0x1000, 0, 1);
        linhaCentro = Formato(0x1000, 1, 1);
        linhaDireita = Formato(0x1000, 2, 1);
        paragrafo = Formato(0, 0, 0);

        fundo = Fundo(pasta == null ? null : Path.Combine(pasta, "fundo.png"), gradiente: true);
        desfocado = Fundo(pasta == null ? null : Path.Combine(pasta, "fundo-desfocado.png"), gradiente: false);
        // Ícone do Aurora Win (o Montador grava MontaWin\icone.png); sem ele, um quadrado com degradê e "A"
        var arquivoIcone = pasta == null ? null : Path.Combine(pasta, "icone.png");
        nint img = 0;
        if (arquivoIcone != null && File.Exists(arquivoIcone) && GdipLoadImageFromFile(arquivoIcone, &img) == 0) icone = img;
    }

    /// <summary>Desenha uma tela num arquivo PNG, sem janela (testes e imagens de exemplo).</summary>
    public static void SalvarImagem(string tema, string? pasta, Tela t, int w, int h, string arquivo)
    {
        var tg = new TelaGrafica(tema, pasta);
        tg.Recursos(w, h);
        tg.tela = t;
        nint bmp, gr;
        Ok(GdipCreateBitmapFromScan0(w, h, 0, 0x0026200A, 0, &bmp), "bitmap");
        Ok(GdipGetImageGraphicsContext(bmp, &gr), "contexto");
        tg.Desenhar(gr);
        var png = new Guid("557CF406-1A04-11D3-9A73-0000F81EF32E");
        Ok(GdipSaveImageToFile(bmp, arquivo, &png, 0), "salvar");
        GdipDisposeImage(bmp);
    }

    static void Ok(int status, string onde)
    {
        if (status != 0) throw new Exception($"{onde}: GDI+ status {status}");
    }

    nint Fonte(nint familia, float tamanho, int estilo)
    {
        nint f;
        Ok(GdipCreateFont(familia, tamanho * s, estilo, 2, &f), "fonte");
        return f;
    }

    static nint Formato(int flags, int alinhar, int vertical)
    {
        nint f;
        Ok(GdipCreateStringFormat(flags, 0, &f), "formato");
        GdipSetStringFormatAlign(f, alinhar);
        GdipSetStringFormatLineAlign(f, vertical);
        GdipSetStringFormatTrimming(f, 3); // reticências quando não cabe
        return f;
    }

    /// <summary>Imagem do tema esticada para cobrir a tela (cortando as sobras), já no tamanho da tela para desenhar rápido.</summary>
    nint Fundo(string? arquivo, bool gradiente)
    {
        nint imagem = 0;
        if (arquivo != null && File.Exists(arquivo) && GdipLoadImageFromFile(arquivo, &imagem) != 0) imagem = 0;
        if (imagem == 0 && !gradiente) return 0;
        nint bmp, g;
        Ok(GdipCreateBitmapFromScan0(largura, altura, 0, 0x000E200B, 0, &bmp), "bitmap");
        Ok(GdipGetImageGraphicsContext(bmp, &g), "contexto");
        if (imagem != 0)
        {
            uint w, h;
            GdipGetImageWidth(imagem, &w);
            GdipGetImageHeight(imagem, &h);
            float escala = Math.Max((float)largura / w, (float)altura / h);
            int sw = (int)(largura / escala), sh = (int)(altura / escala);
            GdipSetInterpolationMode(g, 7);
            GdipDrawImageRectRectI(g, imagem, 0, 0, largura, altura, ((int)w - sw) / 2, ((int)h - sh) / 2, sw, sh, 2, 0, 0, 0);
            GdipDisposeImage(imagem);
        }
        else
        {
            var p1 = new PontoF(0, 0);
            var p2 = new PontoF(largura, altura);
            nint degrade;
            GdipCreateLineBrush(&p1, &p2, c.Fundo1, c.Fundo2, 3, &degrade);
            GdipFillRectangle(g, degrade, 0, 0, largura, altura);
            GdipDeleteBrush(degrade);
        }
        GdipDeleteGraphics(g);
        return bmp;
    }

    void Laco()
    {
        Msg m;
        while (GetMessageW(&m, 0, 0, 0) > 0)
        {
            TranslateMessage(&m);
            DispatchMessageW(&m);
        }
    }

    public void Mostrar(Tela t)
    {
        var antes = Volatile.Read(ref tela);
        // Teclas apertadas durante uma tarefa longa não podem escolher nada na tela seguinte.
        if (antes?.Tipo == TipoTela.Progresso && t.Tipo != TipoTela.Progresso) while (fila.TryTake(out _)) { }
        // Cliques que ficaram na fila foram dados na tela anterior
        else if (antes == null || Nova((antes.Titulo, antes.Info), t))
        {
            lock (fila) // a janela também empurra eventos: sem o lock, uma tecla digitada agora poderia passar na frente das que foram guardadas
            {
                var teclas = new List<Evento>();
                while (fila.TryTake(out var e)) if (e.Item < 0) teclas.Add(e);
                foreach (var e in teclas) fila.Add(e);
            }
        }
        Volatile.Write(ref tela, t);
        PostMessageW(janela, 0x8000, 0, 0);
    }

    static bool Nova((string Titulo, IReadOnlyList<string> Info)? anterior, Tela t) =>
        anterior is not var (titulo, info) || titulo != t.Titulo || !ReferenceEquals(info, t.Info);

    void Empurrar(Evento e) { lock (fila) fila.Add(e); }

    public Evento? Ler(int ms = -1) => fila.TryTake(out var e, ms) ? e : null;

    public void Pausar(Action acao)
    {
        var console = GetConsoleWindow();
        ShowWindow(janela, 0);
        ShowWindow(console, 5);
        SetForegroundWindow(console);
        try { acao(); }
        finally
        {
            ShowWindow(console, 0);
            ShowWindow(janela, 5);
            SetForegroundWindow(janela);
            while (fila.TryTake(out _)) { }
        }
    }

    [UnmanagedCallersOnly]
    static nint Procedimento(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        var t = atual;
        try
        {
            switch (msg)
            {
                case 0x000F when t != null: // WM_PAINT
                    var ps = new PaintStruct();
                    var dc = BeginPaint(hwnd, &ps);
                    nint gr = 0;
                    try
                    {
                        if (GdipCreateFromHDC(t.memDc, &gr) == 0) { var g = gr; gr = 0; t.Desenhar(g); } // Desenhar apaga o Graphics
                        BitBlt(dc, 0, 0, t.largura, t.altura, t.memDc, 0, 0, 0x00CC0020);
                    }
                    finally { if (gr != 0) GdipDeleteGraphics(gr); EndPaint(hwnd, &ps); }
                    return 0;
                case 0x0014: return 1; // WM_ERASEBKGND: o fundo é desenhado junto, sem piscar
                case 0x8000: // WM_APP: tela nova
                    InvalidateRect(hwnd, 0, 0);
                    return 0;
                case 0x0113 when t?.tela?.Tipo == TipoTela.Progresso: // WM_TIMER: atualiza o tempo decorrido
                    InvalidateRect(hwnd, 0, 0);
                    return 0;
                case 0x0100 when t != null: // WM_KEYDOWN: teclas sem caractere
                    if ((int)wParam is 8 or 9 or 13 or 27 or (>= 33 and <= 40) or 46 or (>= 112 and <= 119)) t.Empurrar(new Evento((ConsoleKey)(int)wParam));
                    return 0;
                case 0x0102 when t != null: // WM_CHAR
                    char ch = (char)wParam;
                    if (ch >= ' ' && !char.IsControl(ch))
                        t.Empurrar(new Evento(ch == ' ' ? ConsoleKey.Spacebar : char.IsAsciiLetter(ch) ? (ConsoleKey)char.ToUpperInvariant(ch) : 0, ch));
                    return 0;
                // O segundo clique de um clique duplo não pode escolher nada na tela que acabou de aparecer
                case 0x0202 when t != null && (t.pintada != Volatile.Read(ref t.tela) || Environment.TickCount64 - t.novaEm < 500):
                    return 0;
                case 0x0202 when t != null: // WM_LBUTTONUP
                    float x = (short)(lParam & 0xFFFF), y = (short)((lParam >> 16) & 0xFFFF);
                    foreach (var a in t.alvos)
                        if (x >= a.X && x <= a.X + a.W && y >= a.Y && y <= a.Y + a.H) { t.Empurrar(new Evento(0, '\0', a.Item)); break; }
                    return 0;
                case 0x0200 when t != null: // WM_MOUSEMOVE: destaca o item sob o mouse
                    float mx = (short)(lParam & 0xFFFF), my = (short)((lParam >> 16) & 0xFFFF);
                    int sob = -1;
                    foreach (var a in t.alvos)
                        if (mx >= a.X && mx <= a.X + a.W && my >= a.Y && my <= a.Y + a.H) { sob = a.Item; break; }
                    if (sob != t.hover) { t.hover = sob; InvalidateRect(hwnd, 0, 0); }
                    return 0;
                case 0x020A when t != null: // WM_MOUSEWHEEL (no prompt rola a saída; as setas são do histórico)
                    bool cima = (short)((wParam >> 16) & 0xFFFF) > 0;
                    t.Empurrar(new Evento(t.tela?.Tipo == TipoTela.Terminal ? (cima ? ConsoleKey.PageUp : ConsoleKey.PageDown) : cima ? ConsoleKey.UpArrow : ConsoleKey.DownArrow));
                    return 0;
                case 0x0010: return 0; // WM_CLOSE: Alt+F4 não fecha o painel
            }
        }
        catch (Exception e) { try { Comandos.Registrar($"Tela gráfica: {e.Message}"); } catch (Exception) { } } // nada pode escapar do WndProc
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // ---------- Desenho ----------

    nint g;
    readonly Dictionary<uint, nint> pinceis = [];
    readonly Dictionary<(uint, float), nint> canetas = [];

    nint Pincel(uint cor)
    {
        if (!pinceis.TryGetValue(cor, out var p)) { GdipCreateSolidFill(cor, &p); pinceis[cor] = p; }
        return p;
    }

    nint Caneta(uint cor, float grossura)
    {
        if (!canetas.TryGetValue((cor, grossura), out var p)) { GdipCreatePen1(cor, grossura, 2, &p); canetas[(cor, grossura)] = p; }
        return p;
    }

    static nint Arredondado(float x, float y, float w, float h, float r)
    {
        nint path;
        GdipCreatePath(0, &path);
        float d = Math.Min(r * 2, Math.Min(w, h));
        GdipAddPathArc(path, x, y, d, d, 180, 90);
        GdipAddPathArc(path, x + w - d, y, d, d, 270, 90);
        GdipAddPathArc(path, x + w - d, y + h - d, d, d, 0, 90);
        GdipAddPathArc(path, x, y + h - d, d, d, 90, 90);
        GdipClosePathFigure(path);
        return path;
    }

    void Caixa(float x, float y, float w, float h, float r, uint preenche, uint borda = 0)
    {
        var path = Arredondado(x, y, w, h, r);
        if (preenche != 0) GdipFillPath(g, Pincel(preenche), path);
        if (borda != 0) GdipDrawPath(g, Caneta(borda, 1), path);
        GdipDeletePath(path);
    }

    void Degrade(float x, float y, float w, float h, float r, uint de = 0, uint ate = 0)
    {
        var p1 = new PontoF(x, y);
        var p2 = new PontoF(x + w, y + h);
        nint pincel;
        GdipCreateLineBrush(&p1, &p2, de != 0 ? de : c.Destaque1, ate != 0 ? ate : c.Destaque2, 3, &pincel);
        var path = Arredondado(x, y, w, h, r);
        GdipFillPath(g, pincel, path);
        GdipDeletePath(path);
        GdipDeleteBrush(pincel);
    }

    void Texto(string texto, nint fonte, uint cor, float x, float y, float w, float h, nint formato = 0)
    {
        if (texto.Length == 0 || w <= 0) return;
        var r = new RetF(x, y, w, h);
        fixed (char* p = texto) GdipDrawString(g, p, texto.Length, fonte, &r, formato == 0 ? linha : formato, Pincel(cor));
    }

    float Altura(string texto, nint fonte, float w)
    {
        if (texto.Length == 0) return 8 * s;
        var r = new RetF(0, 0, w, 10000);
        RetF medida;
        int cabe, linhas;
        fixed (char* p = texto) GdipMeasureString(g, p, texto.Length, fonte, &r, paragrafo, &medida, &cabe, &linhas);
        return medida.H;
    }

    float Largura(string texto, nint fonte)
    {
        var r = new RetF(0, 0, 10000, 1000);
        RetF medida;
        int cabe, linhas;
        fixed (char* p = texto) GdipMeasureString(g, p, texto.Length, fonte, &r, linha, &medida, &cabe, &linhas);
        return medida.W;
    }

    /// <summary>
    /// Textos de lista vêm em colunas alinhadas com espaços (feitas para a tela de texto). Com fonte proporcional,
    /// cada pedaço separado por 2+ espaços vai para a posição da sua coluna, para continuar alinhado.
    /// </summary>
    void TextoEmColunas(string texto, nint fonte, uint cor, float x, float y, float w, float h)
    {
        float coluna = 0.56f * 16 * s;
        int i = 0;
        while (i < texto.Length)
        {
            while (i < texto.Length && texto[i] == ' ') i++;
            if (i >= texto.Length) break;
            int fim = texto.IndexOf("  ", i, StringComparison.Ordinal);
            if (fim < 0) fim = texto.Length;
            float px = x + i * coluna;
            Texto(texto[i..fim], fonte, cor, px, y, x + w - px, h);
            i = fim;
        }
    }

    void Marca(float x, float y, float tamanho, uint cor)
    {
        var caneta = Caneta(cor, 2.2f * s);
        GdipDrawLine(g, caneta, x + tamanho * 0.22f, y + tamanho * 0.52f, x + tamanho * 0.42f, y + tamanho * 0.72f);
        GdipDrawLine(g, caneta, x + tamanho * 0.42f, y + tamanho * 0.72f, x + tamanho * 0.78f, y + tamanho * 0.3f);
    }

    void Desenhar(nint graficos)
    {
        alvos.Clear();
        var t = Volatile.Read(ref tela);
        // Tela nova = outro título ou outros textos (duas mensagens "Atenção" seguidas também contam);
        // a mesma lista redesenhada pelas setas mantém a mesma lista de textos
        if (t != null && Nova(telaPintada, t)) { (telaPintada, novaEm) = ((t.Titulo, t.Info), Environment.TickCount64); hover = -1; } // o realce da tela anterior não vale na nova
        pintada = t;
        g = graficos;
        GdipSetSmoothingMode(g, 4);
        GdipSetTextRenderingHint(g, 4);
        GdipDrawImageRectI(g, fundo, 0, 0, largura, altura);

        // Marca no canto e o resumo do PC
        float m = 32 * s;
        if (icone != 0) GdipDrawImageRectI(g, icone, (int)m, (int)(24 * s), (int)(34 * s), (int)(34 * s));
        else
        {
            Degrade(m, 24 * s, 34 * s, 34 * s, 10 * s);
            Texto("A", fonteForte, 0xFFFFFFFF, m, 24 * s, 34 * s, 34 * s, linhaCentro);
        }
        Texto("Aurora Win", fonteForte, c.Texto, m + 44 * s, 24 * s, 300 * s, 34 * s);
        Texto(Tui.InfoSistema.Trim(), fontePequena, c.Suave, largura / 2f, 24 * s, largura / 2f - m, 34 * s, linhaDireita);

        if (t != null)
        {
            if (t.Tipo == TipoTela.Progresso) Progresso(t);
            else if (t.Tipo == TipoTela.Terminal) Terminal(t);
            else Painel(t);
        }
        GdipDeleteGraphics(g);
    }

    (float X, float Y, float W, float H) Vidro(float w, float h)
    {
        w = Math.Min(w, largura - 64 * s);
        h = Math.Min(h, altura - 120 * s);
        float x = (largura - w) / 2, y = Math.Max(76 * s, (altura - h) / 2);
        var path = Arredondado(x, y, w, h, 22 * s);
        if (desfocado != 0)
        {
            GdipSetClipPath(g, path, 0);
            GdipDrawImageRectI(g, desfocado, 0, 0, largura, altura);
            GdipResetClip(g);
        }
        GdipFillPath(g, Pincel(c.Painel), path);
        GdipDrawPath(g, Caneta(c.BordaPainel, 1), path);
        GdipDeletePath(path);
        return (x, y, w, h);
    }

    void Painel(Tela t)
    {
        float pad = 32 * s, w = Math.Min(820 * s, largura - 64 * s), dentro = w - 2 * pad;
        float linhaItem = 40 * s, espaco = 5 * s;
        bool perigo = t.Alerta == Alerta.Perigo;
        uint corInfo = t.Alerta switch { Alerta.Perigo => c.Texto, Alerta.Atencao => c.Atencao, _ => c.Suave };

        // Mede o que vai dentro para dar ao painel a altura certa
        float topoTexto = perigo ? 14 * s : 0;
        bool lista = t.Tipo == TipoTela.Lista;
        var fInfo = fonteNormal;
        float AlturaDaInfo(nint f) => t.Info.Count == 0 ? 0 : t.Info.Sum(l => Altura(l, f, dentro - (perigo ? 32 * s : 0)) + 2 * s) + 14 * s + 2 * topoTexto;
        float alturaInfo = AlturaDaInfo(fInfo);
        // Texto demais para o vidro (resumo longo, erro com muitas linhas): letra menor em vez de empurrar o botão para fora da tela
        if (!lista && pad + 40 * s + alturaInfo + (t.Tipo == TipoTela.Pergunta ? 106 * s : 0) + 46 * s + 36 * s + pad > altura - 120 * s)
            (fInfo, alturaInfo) = (fontePequena, AlturaDaInfo(fontePequena));
        // "Continuar" e "Voltar" são botões fixos embaixo, separados das opções e sempre à vista; só as opções rolam
        int botoes = lista ? t.Itens.ToList().FindIndex(i => i.Tipo is TipoItem.Continuar or TipoItem.Voltar) : -1;
        int rolaveis = botoes >= 0 ? botoes : t.Itens.Count;
        float fixo = pad + 40 * s + alturaInfo + (t.Tipo == TipoTela.Pergunta ? 30 * s + 56 * s + 20 * s : 0)
                     + (lista && botoes < 0 ? 0 : linhaItem + 10 * s) + 36 * s + pad;
        int visiveis = lista ? Math.Min(rolaveis, Math.Max(3, (int)((altura - 120 * s - fixo) / (linhaItem + espaco)))) : 0;
        // Faltam poucas opções para caber (ex.: Reiniciar e Desligar no menu em 1280x720): linhas um pouco mais baixas
        if (lista && rolaveis > visiveis && (altura - 120 * s - fixo) / rolaveis - espaco >= 30 * s)
            (linhaItem, visiveis) = ((altura - 120 * s - fixo) / rolaveis - espaco, rolaveis);
        float h = fixo + visiveis * (linhaItem + espaco);
        var (x, y, _, hp) = Vidro(w, h);

        // Título (com ícone de alerta)
        float cx = x + pad, cy = y + pad;
        if (perigo)
        {
            GdipFillEllipse(g, Pincel(0x2EFF5064), cx, cy - 4 * s, 44 * s, 44 * s);
            Texto("!", fonteTitulo, c.Perigo, cx, cy - 4 * s, 44 * s, 44 * s, linhaCentro);
            cx += 58 * s;
        }
        Texto(t.Titulo, fonteTitulo, c.Texto, cx, cy, x + w - pad - cx, 40 * s);
        if (lista && rolaveis > visiveis)
            Texto($"{t.Selecionado + 1} de {t.Itens.Count}", fontePequena, c.Suave, x + w - pad - 120 * s, cy, 120 * s, 40 * s, linhaDireita);
        cy += 40 * s + 14 * s;

        // Informações (caixa vermelha quando é perigo)
        if (t.Info.Count > 0)
        {
            float ix = x + pad, iw = dentro;
            if (perigo)
            {
                Caixa(ix, cy, iw, alturaInfo - 14 * s, 14 * s, 0x1FFF5064, 0x8CFF6B7D);
                (ix, iw) = (ix + 16 * s, iw - 32 * s);
                cy += topoTexto;
            }
            foreach (var l in t.Info)
            {
                float lh = Altura(l, fInfo, iw);
                Texto(l, fInfo, corInfo, ix, cy, iw, lh + 2 * s, paragrafo);
                cy += lh + 2 * s;
            }
            cy += 14 * s + (perigo ? topoTexto : 0);
        }

        if (t.Tipo == TipoTela.Pergunta)
        {
            Texto(t.Rotulo, fonteForte, perigo ? c.Atencao : c.Texto, x + pad, cy, dentro, 26 * s);
            cy += 30 * s;
            Caixa(x + pad, cy, dentro, 52 * s, 8 * s, c.Campo);
            GdipFillRectangle(g, Pincel(c.CorDestaque), x + pad + 4 * s, cy + 50 * s, dentro - 8 * s, 2 * s);
            var digitado = t.Oculto ? new string('•', t.Campo.Length) : t.Campo;
            while (digitado.Length > 1 && Largura(digitado, fonteNormal) > dentro - 36 * s) digitado = digitado[1..]; // texto longo: mostra o fim, onde está o cursor
            Texto(digitado, fonteNormal, c.Texto, x + pad + 14 * s, cy, dentro - 28 * s, 52 * s);
            GdipFillRectangle(g, Pincel(c.Texto), x + pad + 16 * s + Largura(digitado, fonteNormal), cy + 14 * s, 2 * s, 24 * s); // cursor
            cy += 56 * s + 20 * s;
        }

        if (lista)
        {
            if (t.Titulo != tituloLista) (tituloLista, topoLista) = (t.Titulo, 0);
            if (t.Selecionado < rolaveis) topoLista = Math.Clamp(topoLista, Math.Max(0, t.Selecionado - visiveis + 1), t.Selecionado);
            topoLista = Math.Clamp(topoLista, 0, Math.Max(0, rolaveis - visiveis));
            for (int i = topoLista; i < Math.Min(rolaveis, topoLista + visiveis); i++)
            {
                Linha(t.Itens[i], i == t.Selecionado, x + pad, cy, dentro, linhaItem, i);
                cy += linhaItem + espaco;
            }
        }
        if (botoes >= 0)
        {
            cy += 5 * s;
            for (int i = botoes; i < t.Itens.Count; i++)
            {
                var item = t.Itens[i];
                bool principal = item.Tipo == TipoItem.Continuar;
                float bw = Largura(item.Texto, fonteForte) + 44 * s, px = principal ? x + w - pad - bw : x + pad;
                if (principal) Degrade(px, cy, bw, linhaItem, 10 * s);
                else Caixa(px, cy, bw, linhaItem, 10 * s, c.Cartao, c.Borda);
                if (i == t.Selecionado) // foco do teclado
                {
                    var anel = Arredondado(px - 3 * s, cy - 3 * s, bw + 6 * s, linhaItem + 6 * s, 12 * s);
                    GdipDrawPath(g, Caneta(c.CorDestaque, 2 * s), anel);
                    GdipDeletePath(anel);
                }
                Texto(item.Texto, fonteForte, principal ? 0xFFFFFFFF : c.Texto, px, cy, bw, linhaItem, linhaCentro);
                alvos.Add((px, cy, bw, linhaItem, i));
            }
            cy += linhaItem + 10 * s;
        }
        else if (!lista)
        {
            // Botões lado a lado: Voltar à esquerda, o principal à direita
            float bx = x + w - pad, esquerda = x + pad;
            for (int i = 0; i < t.Itens.Count; i++)
            {
                var item = t.Itens[i];
                float bw = Largura(item.Texto, fonteForte) + 44 * s;
                bool principal = item.Tipo == TipoItem.Continuar;
                float px = principal ? bx - bw : esquerda;
                if (!principal) esquerda += bw + 10 * s;
                if (t.Itens.Count > 1 && i == t.Selecionado && !(perigo && principal)) // foco do teclado (o botão de perigo já se destaca sozinho)
                {
                    var anel = Arredondado(px - 3 * s, cy - 3 * s, bw + 6 * s, linhaItem + 6 * s, 12 * s);
                    GdipDrawPath(g, Caneta(c.CorDestaque, 2 * s), anel);
                    GdipDeletePath(anel);
                }
                if (principal)
                {
                    if (perigo) Degrade(px, cy, bw, linhaItem, 10 * s, 0xFFE0436B, 0xFFB8327E); // vermelho-rosado em degradê, como os outros botões
                    else Degrade(px, cy, bw, linhaItem, 10 * s);
                }
                else Caixa(px, cy, bw, linhaItem, 10 * s, c.Cartao, c.Borda);
                Texto(item.Texto, fonteForte, principal ? 0xFFFFFFFF : c.Texto, px, cy, bw, linhaItem, linhaCentro);
                alvos.Add((px, cy, bw, linhaItem, i));
            }
            cy += linhaItem + 10 * s;
        }
        Texto(t.Rodape, fontePequena, c.Suave, x + pad, y + hp - pad - 20 * s, dentro, 24 * s, linhaCentro);
    }

    void Linha(Item item, bool selecionado, float x, float y, float w, float h, int indice)
    {
        if (selecionado) Degrade(x, y, w, h, 10 * s);
        else Caixa(x, y, w, h, 10 * s, item.Tipo == TipoItem.Voltar ? 0 : c.Cartao, item.Tipo == TipoItem.Voltar ? 0 : c.Borda);
        if (!selecionado && indice == hover) Caixa(x, y, w, h, 10 * s, 0, c.CorDestaque); // mouse em cima
        alvos.Add((x, y, w, h, indice));
        uint cor = selecionado ? 0xFFFFFFFF : item.Tipo == TipoItem.Voltar ? c.Suave : item.Tipo == TipoItem.Continuar ? c.CorDestaque : c.Texto;
        float tx = x + 18 * s;
        if (item.Tipo is TipoItem.Caixa or TipoItem.MarcarTodos)
        {
            float q = 20 * s, qy = y + (h - q) / 2;
            if (item.Marcado)
            {
                Caixa(tx, qy, q, q, 5 * s, selecionado ? 0xFFFFFFFF : c.CorDestaque);
                Marca(tx, qy, q, selecionado ? c.Destaque1 : 0xFFFFFFFF);
            }
            else Caixa(tx, qy, q, q, 5 * s, 0, selecionado ? 0xFFFFFFFF : c.Suave);
            tx += q + 14 * s;
        }
        var fonte = item.Tipo is TipoItem.Continuar or TipoItem.MarcarTodos ? fonteForte : fonteNormal;
        if (item.Texto.Contains("  ")) TextoEmColunas(item.Texto, fonte, cor, tx, y, x + w - 18 * s - tx, h);
        else Texto(item.Texto, fonte, cor, tx, y, x + w - 18 * s - tx, h);
    }

    void Progresso(Tela t)
    {
        float pad = 32 * s;
        bool comEtapas = t.Etapas.Count > 0;
        float w = comEtapas ? 1040 * s : 820 * s, h = comEtapas ? Math.Max(470 * s, 120 * s + t.Etapas.Count * 34 * s) : 400 * s;
        var (x, y, wp, hp) = Vidro(w, h);
        Texto(t.Titulo, fonteTitulo, c.Texto, x + pad, y + pad, wp - 2 * pad, 40 * s);
        float cy = y + pad + 58 * s, cx = x + pad;

        if (comEtapas)
        {
            Texto("ETAPAS", fontePequena, c.Suave, cx, cy, 300 * s, 20 * s);
            float ey = cy + 28 * s;
            // Muitas etapas (um backup com vários usuários e pastas): mostra só as que etapasVisiveis, com a atual no meio
            int etapasVisiveis = Math.Max(1, (int)((hp - (ey - y) - pad - 20 * s) / (34 * s)));
            int primeira = Math.Clamp(t.EtapaAtual - etapasVisiveis / 2, 0, Math.Max(0, t.Etapas.Count - etapasVisiveis));
            for (int i = primeira; i < Math.Min(t.Etapas.Count, primeira + etapasVisiveis); i++)
            {
                float d = 20 * s, dy = ey + (30 * s - d) / 2;
                if (i < t.EtapaAtual)
                {
                    GdipFillEllipse(g, Pincel(c.CorDestaque), cx, dy, d, d);
                    Marca(cx, dy, d, 0xFFFFFFFF);
                }
                else if (i == t.EtapaAtual)
                {
                    GdipDrawEllipse(g, Caneta(c.CorDestaque, 2.5f * s), cx + s, dy + s, d - 2 * s, d - 2 * s);
                    GdipFillEllipse(g, Pincel(c.CorDestaque), cx + 6 * s, dy + 6 * s, d - 12 * s, d - 12 * s);
                }
                else GdipDrawEllipse(g, Caneta(c.Suave, 1.2f * s), cx + s, dy + s, d - 2 * s, d - 2 * s);
                Texto(t.Etapas[i], i == t.EtapaAtual ? fonteForte : fonteNormal, i == t.EtapaAtual ? c.Texto : c.Suave,
                    cx + d + 12 * s, ey, 300 * s - d - 12 * s, 30 * s);
                ey += 34 * s;
            }
            cx += 330 * s;
            GdipFillRectangle(g, Pincel(c.Borda), cx - 16 * s, cy, 1, hp - (cy - y) - pad - 34 * s);
        }

        float dentro = x + wp - pad - cx;
        Texto(t.Detalhe, fonteForte, c.Texto, cx, cy, dentro - 130 * s, 28 * s);
        Texto($"{t.Percentual:0}%", fonteGrande, c.Texto, x + wp - pad - 160 * s, cy - 14 * s, 160 * s, 60 * s, linhaDireita);
        cy += 52 * s;
        Caixa(cx, cy, dentro, 10 * s, 5 * s, c.Cartao, c.Borda);
        if (t.Percentual > 0) Degrade(cx, cy, Math.Max(10 * s, dentro * (float)(t.Percentual / 100)), 10 * s, 5 * s);
        cy += 24 * s;
        Texto($"Decorrido: {Tui.Duracao(DateTime.Now - t.Inicio)}" + (t.Restante is TimeSpan r ? $"     Restante: ~{Tui.Duracao(r)}" : ""),
            fontePequena, c.Suave, cx, cy, dentro, 22 * s);
        cy += 40 * s;

        // O que está acontecendo: as últimas ações, a mais nova embaixo
        float caixaH = y + hp - pad - 30 * s - cy;
        Caixa(cx, cy, dentro, caixaH, 14 * s, c.Cartao, c.Borda);
        Texto("O QUE ESTÁ ACONTECENDO", fontePequena, c.Suave, cx + 16 * s, cy + 12 * s, dentro - 32 * s, 20 * s);
        int cabem = Math.Max(1, (int)((caixaH - 44 * s) / (22 * s)));
        float ly = cy + 40 * s;
        foreach (var l in t.Registro.TakeLast(cabem))
        {
            Texto("• " + l, fontePequena, c.Texto, cx + 16 * s, ly, dentro - 32 * s, 22 * s);
            ly += 22 * s;
        }
        Texto(t.Rodape, fontePequena, c.Suave, x + pad, y + hp - pad - 20 * s, wp - 2 * pad, 24 * s, linhaCentro);
    }

    void Terminal(Tela t)
    {
        float pad = 28 * s, linhaItem = 40 * s;
        var (x, y, w, h) = Vidro(1100 * s, altura - 130 * s);
        float dentro = w - 2 * pad;
        Texto(t.Titulo, fonteTitulo, c.Texto, x + pad, y + pad - 4 * s, dentro, 40 * s);

        // Botões no topo: Voltar à direita do título, Parar ao lado
        float bx = x + w - pad;
        for (int i = 1; i >= 0; i--)
        {
            var item = t.Itens[i];
            float bw = Largura(item.Texto, fonteForte) + 36 * s;
            bx -= bw;
            Caixa(bx, y + pad - 4 * s, bw, linhaItem, 10 * s, c.Cartao, c.Borda);
            Texto(item.Texto, fonteForte, item.Tipo == TipoItem.Voltar ? c.CorDestaque : c.Texto, bx, y + pad - 4 * s, bw, linhaItem, linhaCentro);
            alvos.Add((bx, y + pad - 4 * s, bw, linhaItem, i));
            bx -= 10 * s;
        }

        // Abas (a ativa em degradê) e, embaixo, os botões da aba: pílulas que quebram linha
        float ay = y + pad + 50 * s;
        foreach (bool abas in new[] { true, false })
        {
            float ax = x + pad, ah = abas ? 36 * s : 32 * s;
            bool algum = false;
            for (int i = 2; i < t.Itens.Count; i++)
            {
                var item = t.Itens[i];
                if ((item.Tipo == TipoItem.Aba) != abas) continue;
                var fonte = abas ? fonteForte : fontePequena;
                float aw = Largura(item.Texto, fonte) + (abas ? 36 : 34) * s;
                if (ax + aw > x + w - pad) (ax, ay) = (x + pad, ay + ah + 8 * s);
                if (item.Marcado) Degrade(ax, ay, aw, ah, 18 * s);
                else Caixa(ax, ay, aw, ah, 18 * s, abas ? 0 : c.Cartao, c.Borda);
                Texto(item.Texto, fonte, item.Marcado ? 0xFFFFFFFF : c.Texto, ax, ay, aw, ah, linhaCentro);
                alvos.Add((ax, ay, aw, ah, i));
                ax += aw + 8 * s;
                algum = true;
            }
            if (algum) ay += ah + 10 * s;
        }

        // Saída do cmd, a mais nova embaixo; a última linha é o prompt (X:\>) com o que está sendo digitado
        float oy = ay, oh = y + h - pad - 30 * s - oy, lh = 20 * s;
        Caixa(x + pad, oy, dentro, oh, 12 * s, 0xE6141026, c.Borda);
        int cabem = Math.Max(1, (int)((oh - 24 * s) / lh));
        var linhas = t.Registro.Count > 0 ? t.Registro.ToList() : [""];
        linhas[^1] += t.Campo;
        while (linhas[^1].Length > 1 && Largura(linhas[^1], fonteMono) > dentro - 36 * s) linhas[^1] = linhas[^1][1..]; // comando longo: mostra o fim, onde está o cursor
        int fim = Math.Max(Math.Min(linhas.Count, cabem), linhas.Count - t.Selecionado);
        float ly = oy + 12 * s;
        foreach (var l in linhas.Skip(Math.Max(0, fim - cabem)).Take(cabem))
        {
            Texto(l, fonteMono, 0xFFE8E6F5, x + pad + 14 * s, ly, dentro - 28 * s, lh);
            ly += lh;
        }
        if (t.Selecionado == 0) // cursor
            GdipFillRectangle(g, Pincel(0xFFE8E6F5), x + pad + 16 * s + Largura(linhas[^1], fonteMono), ly - lh + 3 * s, 2 * s, lh - 6 * s);
        Texto(t.Rodape, fontePequena, c.Suave, x + pad, y + h - pad - 20 * s, dentro, 24 * s, linhaCentro);
    }

    // ---------- Win32 e GDI+ ----------

    [StructLayout(LayoutKind.Sequential)]
    struct WndClass
    {
        public uint Tamanho, Estilo;
        public nint Procedimento;
        public int ClsExtra, WndExtra;
        public nint Instancia, Icone, Cursor, Pincel, Menu, Nome, IconePequeno;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Msg
    {
        public nint Janela;
        public uint Mensagem;
        public nint WParam, LParam;
        public uint Hora;
        public int X, Y, Privado;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PaintStruct
    {
        public nint Dc;
        public int Apagar, Esquerda, Topo, Direita, Base, Restaurar, Incremental;
        public fixed byte Reservado[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    struct StartupInput
    {
        public uint Versao;
        public nint Callback;
        public int SemThread, SemCodecs;
    }

    [StructLayout(LayoutKind.Sequential)]
    readonly record struct PontoF(float X, float Y);

    [StructLayout(LayoutKind.Sequential)]
    record struct RetF(float X, float Y, float W, float H);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial nint GetModuleHandleW(string? nome);
    [LibraryImport("kernel32.dll")] private static partial nint GetConsoleWindow();
    [LibraryImport("user32.dll")] private static partial nint GetSystemMenu(nint hwnd, int reverter);
    [LibraryImport("user32.dll")] private static partial int DeleteMenu(nint menu, uint item, uint modo);

    /// <summary>
    /// Tira o X da janela de texto (a do Prompt de comando). Fechar por ali encerrava o painel,
    /// e no WinPE isso reinicia o PC; para sair do prompt, digita-se exit.
    /// </summary>
    public static void TravarFechar()
    {
        if (!OperatingSystem.IsWindows()) return;
        var console = GetConsoleWindow();
        if (console != 0) DeleteMenu(GetSystemMenu(console, 0), 0xF060, 0); // SC_CLOSE
    }
    [LibraryImport("user32.dll")] private static partial ushort RegisterClassExW(WndClass* classe);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint ex, string classe, string titulo, uint estilo, int x, int y, int w, int h, nint pai, nint menu, nint inst, nint param);
    [LibraryImport("user32.dll")] private static partial nint DefWindowProcW(nint hwnd, uint msg, nint w, nint l);
    [LibraryImport("user32.dll")] private static partial int GetMessageW(Msg* m, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll")] private static partial int TranslateMessage(Msg* m);
    [LibraryImport("user32.dll")] private static partial nint DispatchMessageW(Msg* m);
    [LibraryImport("user32.dll")] private static partial int PostMessageW(nint hwnd, uint msg, nint w, nint l);
    [LibraryImport("user32.dll")] private static partial int InvalidateRect(nint hwnd, nint ret, int apagar);
    [LibraryImport("user32.dll")] private static partial nint BeginPaint(nint hwnd, PaintStruct* ps);
    [LibraryImport("user32.dll")] private static partial int EndPaint(nint hwnd, PaintStruct* ps);
    [LibraryImport("user32.dll")] private static partial int GetSystemMetrics(int indice);
    [LibraryImport("user32.dll")] private static partial nuint SetTimer(nint hwnd, nuint id, uint ms, nint funcao);
    [LibraryImport("user32.dll")] private static partial int ShowWindow(nint hwnd, int modo);
    [LibraryImport("user32.dll")] private static partial int SetForegroundWindow(nint hwnd);
    [LibraryImport("user32.dll")] private static partial nint SetFocus(nint hwnd);
    [LibraryImport("user32.dll")] private static partial nint LoadCursorW(nint inst, nint nome);
    [LibraryImport("user32.dll")] private static partial nint GetDC(nint hwnd);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint hwnd, nint dc);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll")] private static partial nint CreateCompatibleBitmap(nint dc, int w, int h);
    [LibraryImport("gdi32.dll")] private static partial nint SelectObject(nint dc, nint obj);
    [LibraryImport("gdi32.dll")] private static partial int BitBlt(nint dc, int x, int y, int w, int h, nint origem, int ox, int oy, uint op);

    [LibraryImport("gdiplus.dll")] private static partial int GdiplusStartup(nuint* token, StartupInput* entrada, nint saida);
    [LibraryImport("gdiplus.dll")] private static partial int GdipCreateFromHDC(nint dc, nint* g);
    [LibraryImport("gdiplus.dll")] private static partial int GdipDeleteGraphics(nint g);
    [LibraryImport("gdiplus.dll")] private static partial int GdipSetSmoothingMode(nint g, int modo);
    [LibraryImport("gdiplus.dll")] private static partial int GdipSetTextRenderingHint(nint g, int modo);
    [LibraryImport("gdiplus.dll")] private static partial int GdipSetInterpolationMode(nint g, int modo);
    [LibraryImport("gdiplus.dll")] private static partial int GdipCreateSolidFill(uint cor, nint* pincel);
    [LibraryImport("gdiplus.dll")] private static partial int GdipCreateLineBrush(PontoF* p1, PontoF* p2, uint c1, uint c2, int repetir, nint* pincel);
    [LibraryImport("gdiplus.dll")] private static partial int GdipDeleteBrush(nint pincel);
    [LibraryImport("gdiplus.dll")] private static partial int GdipCreatePen1(uint cor, float largura, int unidade, nint* caneta);
    [LibraryImport("gdiplus.dll")] private static partial int GdipCreatePath(int modo, nint* path);
    [LibraryImport("gdiplus.dll")] private static partial int GdipDeletePath(nint path);
    [LibraryImport("gdiplus.dll")] private static partial int GdipAddPathArc(nint path, float x, float y, float w, float h, float inicio, float angulo);
    [LibraryImport("gdiplus.dll")] private static partial int GdipClosePathFigure(nint path);
    [LibraryImport("gdiplus.dll")] private static partial int GdipFillPath(nint g, nint pincel, nint path);
    [LibraryImport("gdiplus.dll")] private static partial int GdipDrawPath(nint g, nint caneta, nint path);
    [LibraryImport("gdiplus.dll")] private static partial int GdipFillEllipse(nint g, nint pincel, float x, float y, float w, float h);
    [LibraryImport("gdiplus.dll")] private static partial int GdipDrawEllipse(nint g, nint caneta, float x, float y, float w, float h);
    [LibraryImport("gdiplus.dll")] private static partial int GdipDrawLine(nint g, nint caneta, float x1, float y1, float x2, float y2);
    [LibraryImport("gdiplus.dll")] private static partial int GdipFillRectangle(nint g, nint pincel, float x, float y, float w, float h);
    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GdipCreateFontFamilyFromName(string nome, nint colecao, nint* familia);
    [LibraryImport("gdiplus.dll")] private static partial int GdipGetGenericFontFamilySansSerif(nint* familia);
    [LibraryImport("gdiplus.dll")] private static partial int GdipNewPrivateFontCollection(nint* colecao);
    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GdipPrivateAddFontFile(nint colecao, string arquivo);
    [LibraryImport("gdiplus.dll")] private static partial int GdipCreateFont(nint familia, float tamanho, int estilo, int unidade, nint* fonte);
    [LibraryImport("gdiplus.dll")] private static partial int GdipCreateStringFormat(int flags, ushort idioma, nint* formato);
    [LibraryImport("gdiplus.dll")] private static partial int GdipSetStringFormatAlign(nint formato, int alinhar);
    [LibraryImport("gdiplus.dll")] private static partial int GdipSetStringFormatLineAlign(nint formato, int alinhar);
    [LibraryImport("gdiplus.dll")] private static partial int GdipSetStringFormatTrimming(nint formato, int corte);
    [LibraryImport("gdiplus.dll")] private static partial int GdipDrawString(nint g, char* texto, int tamanho, nint fonte, RetF* layout, nint formato, nint pincel);
    [LibraryImport("gdiplus.dll")]
    private static partial int GdipMeasureString(nint g, char* texto, int tamanho, nint fonte, RetF* layout, nint formato, RetF* medida, int* cabe, int* linhas);
    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GdipLoadImageFromFile(string arquivo, nint* imagem);
    [LibraryImport("gdiplus.dll")] private static partial int GdipGetImageWidth(nint imagem, uint* w);
    [LibraryImport("gdiplus.dll")] private static partial int GdipGetImageHeight(nint imagem, uint* h);
    [LibraryImport("gdiplus.dll")] private static partial int GdipDisposeImage(nint imagem);
    [LibraryImport("gdiplus.dll")] private static partial int GdipDrawImageRectI(nint g, nint imagem, int x, int y, int w, int h);
    [LibraryImport("gdiplus.dll")]
    private static partial int GdipDrawImageRectRectI(nint g, nint imagem, int dx, int dy, int dw, int dh, int ox, int oy, int ow, int oh, int unidade, nint atributos, nint abortar, nint dados);
    [LibraryImport("gdiplus.dll")] private static partial int GdipCreateBitmapFromScan0(int w, int h, int passo, int formato, nint dados, nint* bmp);
    [LibraryImport("gdiplus.dll")] private static partial int GdipGetImageGraphicsContext(nint imagem, nint* g);
    [LibraryImport("gdiplus.dll")] private static partial int GdipSetClipPath(nint g, nint path, int modo);
    [LibraryImport("gdiplus.dll")] private static partial int GdipResetClip(nint g);
    [LibraryImport("gdiplus.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GdipSaveImageToFile(nint imagem, string arquivo, Guid* codec, nint parametros);
}
