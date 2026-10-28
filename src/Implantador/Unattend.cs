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
                      <CommandLine>cmd /c "%SystemDrive%\{ScriptPrimeiroLogin}"</CommandLine>
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
        // Programas em outra janela (minimizada): a Área de Trabalho aparece sem esperar os downloads.
        if (temProgramas) s.Append($"start \"MontaWin - instalando programas\" /min cmd /c \"%SystemDrive%\\{ScriptProgramas}\"\r\n");
        return s.ToString();
    }

    /// <summary>
    /// Roda os instaladores copiados do pendrive (arquivo + parâmetros silenciosos) e depois os programas do winget,
    /// esperando o winget ficar disponível (ele é registrado logo depois do 1º login).
    /// </summary>
    public static string GerarScriptProgramas(IEnumerable<string> idsWinget, IEnumerable<(string Arquivo, string Parametros)>? instaladores = null)
    {
        var s = new System.Text.StringBuilder("""
            @echo off
            rem Gerado pelo MontaWin: instala os programas escolhidos no painel.
            echo Instalando programas (MontaWin). Pode usar o computador enquanto isso.

            """.Replace("\n", "\r\n"));
        var locais = instaladores?.ToList() ?? [];
        foreach (var (arquivo, parametros) in locais)
            s.Append($"start \"\" /wait \"%SystemDrive%\\{PastaInstaladores}\\{arquivo}\" {parametros}\r\n".Replace(" \r\n", "\r\n"));
        if (locais.Count > 0) s.Append($"rd /s /q \"%SystemDrive%\\{PastaInstaladores}\"\r\n");
        s.Append("""
            set tentativas=0
            :esperar
            where winget >nul 2>nul && goto instalar
            set /a tentativas+=1
            if %tentativas% geq 60 goto fim
            timeout /t 5 /nobreak >nul
            goto esperar
            :instalar

            """.Replace("\n", "\r\n"));
        foreach (var id in idsWinget)
            s.Append($"winget install --id {id} -e --silent --accept-package-agreements --accept-source-agreements\r\n");
        s.Append(":fim\r\n");
        return s.ToString();
    }
}
