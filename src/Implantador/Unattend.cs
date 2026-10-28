using System.Security;

namespace MontaWin.Implantador;

/// <summary>
/// Arquivo de respostas da configuração inicial (OOBE): cria a conta local e pula as telas da Microsoft.
/// Vai em Windows\Panther\unattend.xml, onde o Windows procura no primeiro boot.
/// </summary>
public static class Unattend
{
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
}
