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
    public const string ChamadaSetupComplete = $"call %SystemDrive%\\{ScriptProgramas} sistema\r\n";

    const string Componente = "processorArchitecture=\"amd64\" publicKeyToken=\"31bf3856ad364e35\" language=\"neutral\" versionScope=\"nonSxS\"";

    public static string Gerar(string idioma, string usuario, string senha)
    {
        // shortcut: grupo e fuso traduzidos só para pt-BR; outros idiomas usam o nome em inglês e o fuso padrão
        var (grupo, fuso) = idioma.Equals("pt-BR", StringComparison.OrdinalIgnoreCase)
            ? ("Administradores", "<TimeZone>E. South America Standard Time</TimeZone>")
            : ("Administrators", "");
        var u = SecurityElement.Escape(usuario);
        var s = SecurityElement.Escape(senha);
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <unattend xmlns="urn:schemas-microsoft-com:unattend" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
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
    public static string GerarScriptPrimeiroLogin(IEnumerable<string> pastasRestauradas, bool temProgramas = false)
    {
        var s = new System.Text.StringBuilder("@echo off\r\nrem Gerado pelo MontaWin: roda uma vez, no primeiro login.\r\n");
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
        // Programas que faltaram em segundo plano, sem janela: a Área de Trabalho aparece sem esperar os downloads.
        if (temProgramas) s.Append($"start \"\" conhost.exe --headless cmd /c %SystemDrive%\\{ScriptProgramas} usuario\r\n");
        return s.ToString();
    }

    /// <summary>
    /// Script dos programas, rodado duas vezes e sem janela (o resultado vai para MontaWin-programas.log):
    /// "sistema" pelo SetupComplete.cmd, antes da tela de login: instaladores do pendrive e, com internet, o winget
    /// (só pacotes para todos os usuários); "usuario" no primeiro login: o que faltou, pelo winget do usuário.
    /// O que já instalou fica marcado e não repete. Todos os ids vão em <paramref name="idsWinget"/>: os baixados
    /// no pendrive que falharem sem internet são tentados de novo online.
    /// </summary>
    public static string GerarScriptProgramas(IEnumerable<string> idsWinget, IEnumerable<(string Arquivo, string Parametros)>? instaladores = null,
        IEnumerable<(string Id, string Comando, bool SoUsuario)>? offline = null)
    {
        var s = new System.Text.StringBuilder("""
            @echo off
            rem Gerado pelo MontaWin: instala os programas escolhidos no painel.
            if not "%2"=="log" (call "%~f0" %1 log >>"%~dp0MontaWin-programas.log" 2>&1 & exit /b)
            setlocal
            set MODO=%1
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
            goto internet
            :sistema
            rem Antes do login o winget não está no PATH: usa o que vem com o Windows, só se houver internet
            set WINGETDIR=
            for /d %%d in ("%ProgramFiles%\WindowsApps\Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe") do if exist "%%d\winget.exe" set WINGETDIR=%%d
            if not defined WINGETDIR goto fim
            curl.exe -s -m 15 -o nul https://cdn.winget.microsoft.com/cache/source.msix || goto fim
            cd /d "%WINGETDIR%"
            set WINGET=%WINGETDIR%\winget.exe
            set ESCOPO=--scope machine
            :internet

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
        return s.ToString();
    }

    const string Silencioso = "--silent --accept-package-agreements --accept-source-agreements";
}
