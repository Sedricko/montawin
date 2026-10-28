using System.Security;

namespace MontaWin.Implantador;

/// <summary>
/// Arquivo de respostas da configuração inicial (OOBE): cria a conta local e pula as telas da Microsoft.
/// Vai em Windows\Panther\unattend.xml, onde o Windows procura no primeiro boot.
/// </summary>
public static class Unattend
{
    /// <summary>Caminho (no Windows instalado) do script que roda uma vez no primeiro login.</summary>
    public const string ScriptPrimeiroLogin = @"Windows\Setup\Scripts\MontaWin-primeiro-login.cmd";
    public const string PastaRestaurados = "Arquivos restaurados";
    public const string ScriptProgramas = @"Windows\Setup\Scripts\MontaWin-programas.cmd";
    public const string PastaInstaladores = @"Windows\Setup\Scripts\Instaladores";
    /// <summary>O Windows roda este script como sistema no fim da instalação, antes da tela de login e sem janela.</summary>
    public const string ScriptSetupComplete = @"Windows\Setup\Scripts\SetupComplete.cmd";
    public const string ScriptAntesDoLogin = @"Windows\Setup\Scripts\MontaWin-antes-do-login.cmd";
    public const string PerfilWifi = @"Windows\Setup\Scripts\MontaWin-wifi.xml";
    /// <summary>Cópia do backup de drivers dentro do Windows novo, para a segunda tentativa da conferência.</summary>
    public const string PastaDriversReserva = @"MontaWin\Drivers";
    public const string ScriptConferencia = @"Windows\Setup\Scripts\MontaWin-conferencia.ps1";
    public const string ChamadaConferencia = $"powershell -NoProfile -ExecutionPolicy Bypass -File %SystemDrive%\\{ScriptConferencia}";
    public const string ChamadaSetupComplete = $"call %SystemDrive%\\{ScriptAntesDoLogin}\r\n";

    const string Componente = "processorArchitecture=\"amd64\" publicKeyToken=\"31bf3856ad364e35\" language=\"neutral\" versionScope=\"nonSxS\"";

    /// <summary>Nome do computador na rede: até 15 letras, números ou hífen, sem ser só números.</summary>
    public static bool NomePcValido(string nome) =>
        nome.Length is > 0 and <= 15 && nome.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && !nome.All(char.IsAsciiDigit);

    public static string Gerar(string idioma, string usuario, string senha, string? nomePc = null)
    {
        // shortcut: grupo e fuso traduzidos só para pt-BR; outros idiomas usam o nome em inglês e o fuso padrão
        var (grupo, fuso) = idioma.Equals("pt-BR", StringComparison.OrdinalIgnoreCase)
            ? ("Administradores", "<TimeZone>E. South America Standard Time</TimeZone>")
            : ("Administrators", "");
        var u = SecurityElement.Escape(usuario);
        var s = SecurityElement.Escape(senha);
        // O nome do computador é aplicado na fase "specialize", antes das telas iniciais.
        var especializar = nomePc == null ? "" :
            $"<settings pass=\"specialize\"><component name=\"Microsoft-Windows-Shell-Setup\" {Componente}>" +
            $"<ComputerName>{SecurityElement.Escape(nomePc)}</ComputerName></component></settings>";
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <unattend xmlns="urn:schemas-microsoft-com:unattend" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
              {especializar}
              <settings pass="oobeSystem">
                <component name="Microsoft-Windows-International-Core" {Componente}>
                  <InputLocale>{idioma}</InputLocale>
                  <SystemLocale>{idioma}</SystemLocale>
                  <UILanguage>{idioma}</UILanguage>
                  <UserLocale>{idioma}</UserLocale>
                </component>
                <component name="Microsoft-Windows-Shell-Setup" {Componente}>
                  <FirstLogonCommands>
                    <SynchronousCommand wcm:action="add">
                      <Order>1</Order>
                      <CommandLine>conhost.exe --headless cmd /c %SystemDrive%\{ScriptPrimeiroLogin}</CommandLine>
                      <Description>MontaWin</Description>
                    </SynchronousCommand>
                  </FirstLogonCommands>
                  <OOBE>
                    <HideEULAPage>true</HideEULAPage>
                    <HideOEMRegistrationScreen>true</HideOEMRegistrationScreen>
                    <HideOnlineAccountScreens>true</HideOnlineAccountScreens>
                    <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>
                    <ProtectYourPC>3</ProtectYourPC>
                  </OOBE>
                  {fuso}
                  <UserAccounts>
                    <LocalAccounts>
                      <LocalAccount wcm:action="add">
                        <Name>{u}</Name>
                        <Group>{grupo}</Group>
                        <Password><Value>{s}</Value><PlainText>true</PlainText></Password>
                      </LocalAccount>
                    </LocalAccounts>
                  </UserAccounts>
                </component>
              </settings>
            </unattend>
            """;
    }

    /// <summary>
    /// Script do primeiro login: move as pastas pessoais restauradas (ex.: "joao\Documents") para as pastas
    /// do usuário novo e apaga o unattend.xml, que guarda a senha em texto simples.
    /// </summary>
    /// <summary>Ponto de restauração "Windows recém-instalado": dá para voltar ao Windows limpo, com os programas, sem formatar.</summary>
    public const string PontoDeRestauracao = "powershell -NoProfile -Command \"Enable-ComputerRestore -Drive $env:SystemDrive\\; " +
        "Checkpoint-Computer -Description 'Windows recem-instalado (MontaWin)' -RestorePointType MODIFY_SETTINGS\" >nul 2>&1";

    public static string GerarScriptPrimeiroLogin(IEnumerable<string> pastasRestauradas, bool temProgramas = false, bool pontoRestauracao = false,
        bool conferencia = false)
    {
        var s = new System.Text.StringBuilder("@echo off\r\nchcp 65001 >nul\r\nrem Gerado pelo MontaWin: roda uma vez, no primeiro login.\r\n");
        foreach (var rel in pastasRestauradas)
        {
            var origem = $@"%SystemDrive%\{PastaRestaurados}\{rel}";
            s.Append($"robocopy \"{origem}\" \"%USERPROFILE%\\{Path.GetFileName(rel)}\" /E /MOVE /MT:16 /R:1 /W:1 >nul\r\n");
        }
        // Remove as pastas que ficaram vazias (rd sem /s não apaga pasta com arquivos)
        foreach (var usuario in pastasRestauradas.Select(r => r.Split('\\')[0]).Distinct())
            s.Append($"rd \"%SystemDrive%\\{PastaRestaurados}\\{usuario}\" 2>nul\r\n");
        s.Append($"rd \"%SystemDrive%\\{PastaRestaurados}\" 2>nul\r\n");
        s.Append("del /f /q \"%WINDIR%\\Panther\\unattend.xml\"\r\n");
        // Drivers que faltam: o Windows Update já começa a procurar agora, em vez de esperar o agendamento dele.
        s.Append("UsoClient.exe StartScan >nul 2>&1\r\n");
        // Programas que faltaram em segundo plano, sem janela: a Área de Trabalho aparece sem esperar os downloads.
        // O ponto de restauração vem depois deles (no script dos programas) ou agora, se não houver programas.
        if (temProgramas) s.Append($"start \"\" conhost.exe --headless cmd /c %SystemDrive%\\{ScriptProgramas} usuario\r\n");
        else if (pontoRestauracao) s.Append($"start \"\" conhost.exe --headless cmd /c {PontoDeRestauracao}\r\n");
        // Sem programas, a conferência já pode rodar; com eles, o script dos programas a chama no fim.
        if (conferencia && !temProgramas) s.Append($"start \"\" conhost.exe --headless {ChamadaConferencia}\r\n");
        return s.ToString();
    }

    /// <summary>Rede Wi-Fi para o Windows novo conectar sozinho (WPA2/WPA3 com senha, ou aberta sem senha).</summary>
    public static string GerarPerfilWifi(string rede, string senha)
    {
        var nome = SecurityElement.Escape(rede);
        var seguranca = senha.Length == 0
            ? "<authEncryption><authentication>open</authentication><encryption>none</encryption><useOneX>false</useOneX></authEncryption>"
            : "<authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption>" +
              $"<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{SecurityElement.Escape(senha)}</keyMaterial></sharedKey>";
        return "<?xml version=\"1.0\"?>\r\n<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">" +
               $"<name>{nome}</name><SSIDConfig><SSID><name>{nome}</name></SSID></SSIDConfig>" +
               $"<connectionType>ESS</connectionType><connectionMode>auto</connectionMode><MSM><security>{seguranca}</security></MSM></WLANProfile>\r\n";
    }

    /// <summary>
    /// Roda como sistema antes da tela de login (chamado pelo SetupComplete.cmd), sem janela: conecta o Wi-Fi, ativa com
    /// a licença do técnico e instala os programas. No fim apaga a si mesmo e o arquivo do Wi-Fi (têm a chave e a senha).
    /// </summary>
    public static string GerarScriptAntesDoLogin(string? rede, string? chave, bool programas)
    {
        var s = new System.Text.StringBuilder("@echo off\r\nchcp 65001 >nul\r\nrem Gerado pelo MontaWin: roda uma vez, antes da tela de login.\r\nset LOG=%~dp0MontaWin-antes-do-login.log\r\n");
        if (rede != null)
            s.Append($"netsh wlan add profile filename=\"%SystemDrive%\\{PerfilWifi}\" user=all >>\"%LOG%\" 2>&1\r\n" +
                     $"del /f /q \"%SystemDrive%\\{PerfilWifi}\"\r\n" +
                     $"netsh wlan connect name=\"{rede.Replace("\"", "")}\" >>\"%LOG%\" 2>&1\r\n");
        if (chave != null)
            // Chave que não é da edição instalada: o Windows só recusa e segue normal.
            s.Append("powershell -NoProfile -Command \"$s = Get-CimInstance SoftwareLicensingService; " +
                     $"Invoke-CimMethod -InputObject $s -MethodName InstallProductKey -Arguments @{{ProductKey='{chave}'}} | Out-Null; " +
                     "Invoke-CimMethod -InputObject $s -MethodName RefreshLicenseStatus | Out-Null\" >>\"%LOG%\" 2>&1\r\n");
        if (programas) s.Append($"call %SystemDrive%\\{ScriptProgramas} sistema {(rede != null ? "esperar" : "\"\"")}\r\n");
        s.Append("(goto) 2>nul & del /f /q \"%~f0\"\r\n");
        return s.ToString();
    }

    /// <summary>Chave do produto no formato XXXXX-XXXXX-XXXXX-XXXXX-XXXXX (maiúsculas).</summary>
    public static string? ChaveValida(string texto) =>
        texto.Trim().ToUpperInvariant() is var c && System.Text.RegularExpressions.Regex.IsMatch(c, "^[A-Z0-9]{5}(-[A-Z0-9]{5}){4}$") ? c : null;

    /// <summary>
    /// Script dos programas, rodado duas vezes e sem janela (o resultado vai para MontaWin-programas.log):
    /// "sistema" pelo SetupComplete.cmd, antes da tela de login: instaladores do pendrive e, com internet, o winget
    /// (só pacotes para todos os usuários); "usuario" no primeiro login: o que faltou, pelo winget do usuário.
    /// O que já instalou fica marcado e não repete. Todos os ids vão em <paramref name="idsWinget"/>: os baixados
    /// no pendrive que falharem sem internet são tentados de novo online.
    /// </summary>
    public static string GerarScriptProgramas(IEnumerable<string> idsWinget, IEnumerable<(string Arquivo, string Parametros)>? instaladores = null,
        IEnumerable<(string Id, string Comando, bool SoUsuario)>? offline = null, bool pontoRestauracao = false, bool conferencia = false)
    {
        var s = new System.Text.StringBuilder("""
            @echo off
            rem Os scripts são gravados em UTF-8: sem o chcp, nomes com acento (Redistribuíveis) não são achados
            chcp 65001 >nul
            rem Gerado pelo MontaWin: instala os programas escolhidos no painel.
            if not "%3"=="log" (call "%~f0" %1 "%~2" log >>"%~dp0MontaWin-programas.log" 2>&1 & exit /b)
            setlocal
            set MODO=%1
            set ESPERAR=%~2
            set FEITOS=%~dp0MontaWin-feitos
            md "%FEITOS%" 2>nul
            echo [%date% %time%] Instalando programas (%MODO%)

            """.ReplaceLineEndings("\r\n"));
        static string Feito(string nome) => $"\"%FEITOS%\\{nome}\"";
        foreach (var (arquivo, parametros) in instaladores ?? [])
        {
            var caminho = $"\"%SystemDrive%\\{PastaInstaladores}\\{arquivo}\"";
            if (arquivo.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) caminho = "msiexec /i " + caminho; // .msi não roda sozinho com parâmetros
            s.Append($"if not exist {Feito(arquivo)} {caminho} {parametros} && type nul>{Feito(arquivo)}\r\n".Replace("  &&", " &&"));
        }
        foreach (var (id, comando, soUsuario) in offline ?? [])
            s.Append($"{(soUsuario ? "if \"%MODO%\"==\"usuario\" " : "")}if not exist {Feito(id)} {comando} && type nul>{Feito(id)}\r\n");
        s.Append("""
            if "%MODO%"=="sistema" goto sistema
            rem No primeiro login: espera o winget do usuário ser registrado
            set tentativas=0
            :esperar
            where winget >nul 2>nul && goto usuario
            set /a tentativas+=1
            if %tentativas% geq 60 goto fim
            ping -n 6 127.0.0.1 >nul
            goto esperar
            :usuario
            set WINGET=winget
            set ESCOPO=
            rem Sem internet ainda (rede subindo, Wi-Fi conectando): espera até ~10 min antes de desistir
            set tentativas=0
            :redeusuario
            curl.exe -s -m 10 -o nul https://cdn.winget.microsoft.com/cache/source.msix && goto internet
            set /a tentativas+=1
            if %tentativas% geq 20 goto fim
            ping -n 21 127.0.0.1 >nul
            goto redeusuario
            :sistema
            rem Antes do login o winget não está no PATH: usa o que vem com o Windows, só se houver internet
            set WINGETDIR=
            for /d %%d in ("%ProgramFiles%\WindowsApps\Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe") do if exist "%%d\winget.exe" set WINGETDIR=%%d
            if not defined WINGETDIR goto fim
            rem Com Wi-Fi configurado, espera ele conectar (até ~1 min); sem ele, testa uma vez só
            set tentativas=0
            :rede
            curl.exe -s -m 10 -o nul https://cdn.winget.microsoft.com/cache/source.msix && goto temrede
            if not "%ESPERAR%"=="esperar" goto fim
            set /a tentativas+=1
            if %tentativas% geq 6 goto fim
            ping -n 6 127.0.0.1 >nul
            goto rede
            :temrede
            cd /d "%WINGETDIR%"
            set WINGET=%WINGETDIR%\winget.exe
            set ESCOPO=--scope machine
            :internet
            rem Relógio certo antes de baixar: com a hora errada, as conexões seguras (certificados) falham
            net start w32time >nul 2>&1
            w32tm /resync /force >nul 2>&1

            """.ReplaceLineEndings("\r\n"));
        // Origem sempre explícita: sem ela o winget também consulta a Microsoft Store, que no primeiro login costuma
        // falhar ("certificate did not match") e aí o winget para pedindo --source, sem instalar nada.
        foreach (var id in idsWinget.Distinct())
            s.Append(Programas.DaLoja(id)
                ? $"if \"%MODO%\"==\"usuario\" if not exist {Feito(id)} \"%WINGET%\" install --id {id} -e --source msstore {Silencioso} && type nul>{Feito(id)}\r\n"
                : $"if not exist {Feito(id)} \"%WINGET%\" install --id {id} -e --source winget %ESCOPO% {Silencioso} && type nul>{Feito(id)}\r\n");
        s.Append("""
            :fim
            echo [%date% %time%] Fim (%MODO%)
            if "%MODO%"=="usuario" rd /s /q "%~dp0Instaladores" 2>nul

            """.ReplaceLineEndings("\r\n"));
        if (pontoRestauracao) s.Append($"if \"%MODO%\"==\"usuario\" {PontoDeRestauracao}\r\n"); // depois dos programas
        if (conferencia) s.Append($"if \"%MODO%\"==\"usuario\" {ChamadaConferencia}\r\n");
        return s.ToString();
    }

    /// <summary>
    /// Conferência no primeiro login, depois dos programas: acrescenta ao relatório da Área de Trabalho quais programas
    /// instalaram, os dispositivos sem driver e se o Windows está ativado. Gravar com BOM (o PowerShell 5 lê assim os acentos).
    /// </summary>
    public static string GerarConferencia(IEnumerable<(string Nome, string[] Marcadores)> programas)
    {
        static string Ps(string t) => "'" + t.Replace("'", "''") + "'";
        var lista = string.Join(",\r\n", programas.Select(p => $"    [pscustomobject]@{{ Nome = {Ps(p.Nome)}; Marcas = @({string.Join(", ", p.Marcadores.Select(Ps))}) }}"));
        return $$"""
            # Gerado pelo MontaWin: conferência do primeiro login (roda uma vez e se apaga).
            $feitos = Join-Path $PSScriptRoot 'MontaWin-feitos'
            $programas = @(
            {{lista}}
            )
            $linhas = @('', "===== Conferência no primeiro login ($(Get-Date -Format 'dd/MM/yyyy HH:mm')) =====", '')
            if ($programas.Count -gt 0) {
                $linhas += 'Programas:'
                foreach ($p in $programas) {
                    $falta = @($p.Marcas | Where-Object { -not (Test-Path (Join-Path $feitos $_)) })
                    $linhas += '  ' + $(if ($falta.Count -eq 0) { 'OK       ' } else { 'FALHOU   ' }) + $p.Nome
                }
                $linhas += ''
            }
            function SemDriver { @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.Status -in 'Error', 'Degraded' }) }
            $sem = SemDriver
            # Segunda tentativa: instala de novo a partir da cópia do backup de drivers que o painel deixou no disco
            $reserva = Join-Path $env:SystemDrive '{{PastaDriversReserva}}'
            if ($sem.Count -gt 0 -and (Test-Path $reserva)) {
                $antes = $sem.Count
                pnputil.exe /add-driver "$reserva\*.inf" /subdirs /install | Out-Null
                Start-Sleep -Seconds 15
                $sem = SemDriver
                $linhas += "Segunda tentativa com o backup de drivers: $($antes - $sem.Count) de $antes dispositivo(s) resolvido(s)."
            }
            if ($sem.Count -eq 0 -and (Test-Path $reserva)) {
                Remove-Item $reserva -Recurse -Force -ErrorAction SilentlyContinue
                Remove-Item (Split-Path $reserva) -Force -ErrorAction SilentlyContinue # só se ficou vazia
            }
            elseif (Test-Path $reserva) { $linhas += "O backup de drivers ficou em $reserva (pode apagar quando estiver tudo certo)." }
            if ($sem.Count -eq 0) { $linhas += 'Drivers: todos os dispositivos estão funcionando.' }
            else {
                $linhas += "Drivers: $($sem.Count) dispositivo(s) com problema (o Windows Update ainda pode instalar alguns):"
                foreach ($d in $sem) { $linhas += '  - ' + $(if ($d.FriendlyName) { $d.FriendlyName } else { $d.InstanceId }) }
            }
            $licenca = Get-CimInstance SoftwareLicensingProduct -Filter "PartialProductKey IS NOT NULL AND ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f'" -ErrorAction SilentlyContinue | Select-Object -First 1
            $linhas += '', $(if ($licenca.LicenseStatus -eq 1) { 'Windows: ativado.' } else { 'Windows: NÃO ativado (sem licença ou ainda sem internet para ativar).' })
            $relatorio = Get-ChildItem "$env:PUBLIC\Desktop" -Filter 'Relat*.txt' -ErrorAction SilentlyContinue | Select-Object -First 1
            $arquivo = if ($relatorio) { $relatorio.FullName } else { Join-Path "$env:PUBLIC\Desktop" 'Relatório da formatação.txt' }
            [IO.File]::AppendAllText($arquivo, ($linhas -join "`r`n") + "`r`n", (New-Object Text.UTF8Encoding $false))
            Remove-Item $PSCommandPath -Force -ErrorAction SilentlyContinue

            """.ReplaceLineEndings("\r\n");
    }

    const string Silencioso = "--silent --accept-package-agreements --accept-source-agreements";
}
