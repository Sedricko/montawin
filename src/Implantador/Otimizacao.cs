using System.Text.RegularExpressions;

namespace MontaWin.Implantador;

/// <summary>
/// Deixa o Windows aplicado mais leve antes do primeiro boot: remove apps de propaganda/inúteis e ajusta o registro.
/// Só ajustes seguros: Windows Update, Microsoft Store, Defender, rede e drivers continuam como estão.
/// </summary>
public static partial class Otimizacao
{
    /// <summary>Apps pré-instalados que saem (prefixo do nome do pacote). Store, Fotos, Calculadora, Bloco de Notas etc. ficam.</summary>
    public static readonly string[] AppsRemovidos =
    [
        "Microsoft.BingNews", "Microsoft.BingWeather", "Microsoft.BingSearch", "Microsoft.GamingApp", "Microsoft.GetHelp",
        "Microsoft.Getstarted", "Microsoft.MicrosoftOfficeHub", "Microsoft.MicrosoftSolitaireCollection", "Microsoft.People",
        "Microsoft.PowerAutomateDesktop", "Microsoft.Todos", "Microsoft.WindowsFeedbackHub", "Microsoft.WindowsMaps",
        "Microsoft.Xbox.TCUI", "Microsoft.XboxGameOverlay", "Microsoft.XboxGamingOverlay", "Microsoft.XboxSpeechToTextOverlay",
        "Microsoft.ZuneMusic", "Microsoft.ZuneVideo", "Microsoft.549981C3F5F10", "Microsoft.Copilot", "Clipchamp.Clipchamp",
        "MicrosoftCorporationII.MicrosoftFamily", "Microsoft.MixedReality.Portal", "Microsoft.SkypeApp", "Microsoft.Wallet",
    ];

    // Nome completo de pacote: Nome_Versão_Arquitetura_Recurso_Editor (ex.: Microsoft.BingNews_4.55.62231.0_neutral_~_8wekyb3d8bbwe)
    [GeneratedRegex(@"[A-Za-z0-9][A-Za-z0-9.\-]*_\d+(?:\.\d+){3}_[A-Za-z0-9]+_~?[A-Za-z0-9]*_[a-z0-9]{13}")]
    private static partial Regex RegexPacote();

    /// <summary>Pacotes a remover encontrados na saída do "dism /Get-ProvisionedAppxPackages" (não depende do idioma).</summary>
    public static List<string> PacotesParaRemover(string saidaDism) =>
        RegexPacote().Matches(saidaDism).Select(m => m.Value).Distinct()
            .Where(p => AppsRemovidos.Contains(p.Split('_')[0], StringComparer.OrdinalIgnoreCase))
            .ToList();

    /// <summary>Nome que o técnico reconhece para o pacote (ex.: Microsoft.BingNews_4.55... vira "Notícias").</summary>
    public static string NomeAmigavel(string pacote) => pacote.Split('_')[0] switch
    {
        "Microsoft.BingNews" => "Notícias",
        "Microsoft.BingWeather" => "Clima",
        "Microsoft.BingSearch" => "Pesquisa do Bing",
        "Microsoft.GamingApp" => "Xbox",
        "Microsoft.GetHelp" => "Obter Ajuda",
        "Microsoft.Getstarted" => "Dicas",
        "Microsoft.MicrosoftOfficeHub" => "Atalho do Microsoft 365 (propaganda)",
        "Microsoft.MicrosoftSolitaireCollection" => "Paciência",
        "Microsoft.People" => "Pessoas",
        "Microsoft.PowerAutomateDesktop" => "Power Automate",
        "Microsoft.Todos" => "To Do",
        "Microsoft.WindowsFeedbackHub" => "Hub de Comentários",
        "Microsoft.WindowsMaps" => "Mapas",
        var x when x.StartsWith("Microsoft.Xbox") => "Xbox (sobreposição de jogos)",
        "Microsoft.ZuneMusic" => "Media Player",
        "Microsoft.ZuneVideo" => "Filmes e TV",
        "Microsoft.549981C3F5F10" => "Cortana",
        "Microsoft.Copilot" => "Copilot",
        "Clipchamp.Clipchamp" => "Clipchamp",
        "MicrosoftCorporationII.MicrosoftFamily" => "Family",
        "Microsoft.MixedReality.Portal" => "Realidade Misturada",
        "Microsoft.SkypeApp" => "Skype",
        "Microsoft.Wallet" => "Carteira",
        var x => x,
    };

    // Chaves temporárias onde os registros do Windows novo são carregados no WinPE.
    public const string ChaveSoftware = @"HKLM\MW_SOFTWARE", ChaveSystem = @"HKLM\MW_SYSTEM", ChaveUsuario = @"HKLM\MW_DEFAULT";

    /// <summary>Ajustes de registro (argumentos do reg add). O usuário padrão vale para toda conta nova.</summary>
    public static readonly (string Chave, string Valor, int Dado)[] Registro =
    [
        // Sem apps "sugeridos" (Candy Crush etc.) e sem propaganda da Microsoft
        ($@"{ChaveSoftware}\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1),
        ($@"{ChaveSoftware}\Policies\Microsoft\Windows\CloudContent", "DisableCloudOptimizedContent", 1),
        ($@"{ChaveSoftware}\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1),
        // Telemetria no mínimo permitido no Home/Pro e serviço de telemetria desligado
        ($@"{ChaveSoftware}\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 1),
        ($@"{ChaveSystem}\ControlSet001\Services\DiagTrack", "Start", 4),
        // Widgets (notícias) desligados
        ($@"{ChaveSoftware}\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0),
        // Pesquisa do Iniciar só no PC (sem Bing): mais rápida
        ($@"{ChaveUsuario}\Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1),
        ($@"{ChaveUsuario}\Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0),
        // Sem apps instalados sozinhos e sem dicas/sugestões
        ($@"{ChaveUsuario}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", 0),
        ($@"{ChaveUsuario}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "PreInstalledAppsEnabled", 0),
        ($@"{ChaveUsuario}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "OemPreInstalledAppsEnabled", 0),
        ($@"{ChaveUsuario}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0),
        ($@"{ChaveUsuario}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 0),
        ($@"{ChaveUsuario}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338389Enabled", 0),
        ($@"{ChaveUsuario}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353694Enabled", 0),
        ($@"{ChaveUsuario}\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-353696Enabled", 0),
        // Barra de tarefas sem Widgets e sem Copilot
        ($@"{ChaveUsuario}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", 0),
        ($@"{ChaveUsuario}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", 0),
        // Programas da inicialização sem o atraso padrão de ~10 s
        ($@"{ChaveUsuario}\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0),
    ];

    /// <summary>Arquivos de registro do Windows aplicado e a chave onde cada um é carregado.</summary>
    public static (string Chave, string Arquivo)[] Hives(char windows) =>
    [
        (ChaveSoftware, $@"{windows}:\Windows\System32\config\SOFTWARE"),
        (ChaveSystem, $@"{windows}:\Windows\System32\config\SYSTEM"),
        (ChaveUsuario, $@"{windows}:\Users\Default\NTUSER.DAT"),
    ];

    /// <summary>Opcionais: OneDrive não se instala para contas novas; plano Alto desempenho ativo.</summary>
    public static IEnumerable<string> ComandosOpcionais(bool removerOneDrive, bool altoDesempenho)
    {
        if (removerOneDrive)
            yield return $@"delete ""{ChaveUsuario}\Software\Microsoft\Windows\CurrentVersion\Run"" /v OneDriveSetup /f";
        if (altoDesempenho)
            yield return $@"add ""{ChaveSystem}\ControlSet001\Control\Power\User\PowerSchemes"" /v ActivePowerScheme /t REG_SZ /d 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c /f";
    }

    const string SoEmSsd = "StartupDelayInMSec"; // em HD, abrir tudo junto deixa o começo mais lento

    public static IEnumerable<(string Chave, string Valor, int Dado)> AjustesPara(bool ssd) => Registro.Where(r => ssd || r.Valor != SoEmSsd);

    /// <summary>Aplica tudo no Windows em <paramref name="windows"/>. Retorna quantos apps saíram.</summary>
    public static int Aplicar(char windows, bool ssd, bool otimizar, bool removerOneDrive, bool altoDesempenho, Action<double>? progresso = null,
        Action<string>? removido = null)
    {
        var pacotes = new List<string>();
        if (otimizar)
        {
            Comandos.Executar("dism", $@"/Image:{windows}:\ /Get-ProvisionedAppxPackages");
            pacotes = PacotesParaRemover(Comandos.UltimaSaidaCompleta);
        }
        for (int i = 0; i < pacotes.Count; i++)
        {
            if (Comandos.Executar("dism", $@"/Image:{windows}:\ /Remove-ProvisionedAppxPackage /PackageName:{pacotes[i]}") == 0)
                removido?.Invoke(NomeAmigavel(pacotes[i]));
            progresso?.Invoke((i + 1) * 80.0 / Math.Max(pacotes.Count, 1));
        }

        var carregadas = new List<string>();
        try
        {
            foreach (var (chave, arquivo) in Hives(windows))
                if (File.Exists(arquivo) && Comandos.Executar("reg", $"load {chave} \"{arquivo}\"") == 0) carregadas.Add(chave);
            foreach (var (chave, valor, dado) in (otimizar ? AjustesPara(ssd) : Enumerable.Empty<(string Chave, string Valor, int Dado)>()).Where(r => carregadas.Any(c => r.Chave.StartsWith(c + @"\"))))
                Comandos.Executar("reg", $"add \"{chave}\" /v {valor} /t REG_DWORD /d {dado} /f");
            foreach (var comando in ComandosOpcionais(removerOneDrive, altoDesempenho))
                Comandos.Executar("reg", comando);
        }
        finally
        {
            // Sem descarregar, o Windows novo não consegue abrir o próprio registro no primeiro boot.
            foreach (var chave in carregadas) Comandos.Executar("reg", $"unload {chave}");
        }
        progresso?.Invoke(100);
        return pacotes.Count;
    }
}
