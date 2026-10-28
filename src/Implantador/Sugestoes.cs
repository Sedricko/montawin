using System.Text.RegularExpressions;

namespace MontaWin.Implantador;

/// <summary>
/// Assistente da formatação: regras simples, sem internet, para o que costuma dar errado.
/// Só sugere (no resumo e nas mensagens de erro): quem decide é o técnico.
/// </summary>
public static class Sugestoes
{
    /// <summary>Avisos sobre este PC para o Windows escolhido, mostrados no resumo antes do SIM.</summary>
    public static List<string> Instalacao(string edicao, bool uefi, bool? secureBoot, double ramGb, bool ssd, long discoBytes, string? cpu)
    {
        var r = new List<string>();
        bool win11 = edicao.Contains("Windows 11", StringComparison.OrdinalIgnoreCase);
        if (win11 && ramGb < 3.5) r.Add($"Este PC tem {ramGb:0} GB de RAM e o Windows 11 pede 4 GB: vai ficar lento. Recomende mais memória ao cliente.");
        if (win11 && !uefi) r.Add("O PC está em modo BIOS (legado) e o Windows 11 pede UEFI: dá para instalar, mas se puder, ative o UEFI no setup do PC antes.");
        if (win11 && uefi && secureBoot == false) r.Add("Secure Boot desligado: o Windows instala, mas jogos com anticheat e algumas atualizações pedem ele ligado.");
        if (win11 && CpuAntiga(cpu)) r.Add($"O processador ({cpu!.Trim()}) é mais antigo que os aceitos pelo Windows 11: instala, mas a Microsoft pode não oferecer as próximas versões.");
        if (!ssd) r.Add("O disco é HD (não SSD): o Windows vai ficar lento. Se der, recomende ao cliente trocar por um SSD.");
        if (discoBytes > 0 && discoBytes < 64_000_000_000) r.Add("O disco tem menos de 64 GB: pode faltar espaço para as atualizações do Windows.");
        return r;
    }

    /// <summary>Intel Core de 7ª geração ou anterior (i5-7200U, i7-4790) e Ryzen 1000: fora da lista do Windows 11.</summary>
    public static bool CpuAntiga(string? cpu)
    {
        if (cpu == null) return false;
        if (Regex.Match(cpu, @"\bi[3579]-(\d{4,5})") is { Success: true } intel)
            return intel.Groups[1].Value.Length == 4 && intel.Groups[1].Value[0] < '8';
        if (Regex.Match(cpu, @"Ryzen\s+\d\s+(\d)\d{3}") is { Success: true } amd) return amd.Groups[1].Value == "1";
        return Regex.IsMatch(cpu, @"Core\(TM\)2|Core 2 |\bFX-|Phenom", RegexOptions.IgnoreCase);
    }

    /// <summary>O que costuma resolver um erro (pelos códigos e palavras que o DISM, o diskpart e o bcdboot mostram).</summary>
    public static string? Explicar(string erro)
    {
        bool Tem(params string[] s) => s.Any(x => erro.Contains(x, StringComparison.OrdinalIgnoreCase));
        if (Tem("0x80070070", "espaço insuficiente", "not enough space")) return "Falta espaço: no disco do Windows (escolha um tamanho maior) ou no pendrive (apague backups antigos).";
        if (Tem("0x8007000d", "0x80070570", "0x8007045d", "corromp", "corrupt"))
            return "Arquivo corrompido ou erro de leitura: o pendrive pode estar com defeito. Grave o pendrive de novo, e se repetir, troque o pendrive ou a porta USB.";
        if (Tem("0x80070015", "0x8007001f", "não está pronto", "not ready"))
            return "O disco ou o pendrive desconectou no meio: use outra porta USB (de preferência atrás do PC, direto na placa) e tente de novo.";
        if (Tem("protegido contra gravação", "write-protected", "write protected"))
            return "O disco está protegido contra gravação: confira a chave do cartão/adaptador, ou use \"attributes disk clear readonly\" no diskpart (aba Reparo do prompt).";
        if (Tem("0x80070005", "acesso negado", "access is denied", "bitlocker"))
            return "Acesso negado: o disco pode estar com BitLocker ou em uso. Veja a aba Testes do prompt (BitLocker) e destrave com a chave de recuperação.";
        if (Tem("bcdboot"))
            return "Não deu para criar a inicialização: confira se o PC está no mesmo modo (UEFI ou BIOS) em que o disco foi preparado e tente formatar o disco inteiro.";
        return null;
    }
}
