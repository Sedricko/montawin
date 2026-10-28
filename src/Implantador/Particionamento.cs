using System.Text;

namespace MontaWin.Implantador;

/// <summary>Script do diskpart e as letras em que cada partição vai ficar.</summary>
public record PlanoParticoes(string Script, char Sistema, char Windows, char? Recuperacao)
{
    /// <summary>Multi boot: letras dos outros Windows, na ordem escolhida (o primeiro é <see cref="Windows"/>).</summary>
    public char[] WindowsExtras { get; init; } = [];
}

public static class Particionamento
{
    public const int RecuperacaoMb = 1024;
    const string IdRecuperacaoGpt = "de94bba4-06d1-4d40-a16a-bfd50179d6ac";

    /// <summary>
    /// Apaga o disco e cria inicialização + Windows + recuperação.
    /// Com <paramref name="windowsGb"/>, o Windows fica com esse tamanho e o resto vira a partição "Dados".
    /// </summary>
    public static PlanoParticoes ApagarDisco(int disco, bool uefi, char sistema, char windows, char recuperacao, int? windowsGb = null)
    {
        var s = Inicio(disco, uefi, sistema);

        if (windowsGb is int gb) s.AppendLine($"create partition primary size={gb * 1024}");
        else s.AppendLine("create partition primary").AppendLine($"shrink minimum={RecuperacaoMb}"); // sobra o espaço da recuperação no fim
        s.AppendLine("format quick fs=ntfs label=Windows").AppendLine($"assign letter={windows}");

        s.AppendLine(windowsGb is null ? "create partition primary" : $"create partition primary size={RecuperacaoMb}")
         .AppendLine("format quick fs=ntfs label=Recovery")
         .AppendLine($"assign letter={recuperacao}");
        if (uefi) s.AppendLine($"set id={IdRecuperacaoGpt}").AppendLine("gpt attributes=0x8000000000000001");
        else s.AppendLine("set id=27");

        if (windowsGb is not null) s.AppendLine("create partition primary").AppendLine("format quick fs=ntfs label=Dados");
        s.AppendLine("exit");
        return new PlanoParticoes(s.ToString(), sistema, windows, recuperacao);
    }

    // Apaga o disco e cria a partição de inicialização (EFI + MSR no UEFI, "Reservado pelo sistema" ativo no BIOS).
    static StringBuilder Inicio(int disco, bool uefi, char sistema)
    {
        var s = new StringBuilder();
        s.AppendLine($"select disk {disco}").AppendLine("clean");
        if (uefi)
            s.AppendLine("convert gpt")
             .AppendLine("create partition efi size=300")
             .AppendLine("format quick fs=fat32 label=System")
             .AppendLine($"assign letter={sistema}")
             .AppendLine("create partition msr size=16");
        else
            s.AppendLine("convert mbr")
             .AppendLine("create partition primary size=300")
             .AppendLine("format quick fs=ntfs label=System")
             .AppendLine("active")
             .AppendLine($"assign letter={sistema}");
        return s;
    }

    /// <summary>
    /// Multi boot: apaga o disco e cria uma partição por Windows. Cada um tem o tamanho de <paramref name="tamanhosGb"/>,
    /// e o último fica com o resto do disco. Sem partição de recuperação (cada Windows usa a sua própria).
    /// </summary>
    public static PlanoParticoes ApagarDiscoMultiboot(int disco, bool uefi, char sistema, IReadOnlyList<char> windows, IReadOnlyList<int> tamanhosGb)
    {
        var s = Inicio(disco, uefi, sistema);
        for (int i = 0; i < windows.Count; i++)
        {
            s.AppendLine(i < windows.Count - 1 ? $"create partition primary size={tamanhosGb[i] * 1024}" : "create partition primary")
             .AppendLine($"format quick fs=ntfs label=Windows{i + 1}")
             .AppendLine($"assign letter={windows[i]}");
        }
        s.AppendLine("exit");
        return new PlanoParticoes(s.ToString(), sistema, windows[0], null) { WindowsExtras = windows.Skip(1).ToArray() };
    }

    /// <summary>
    /// Formata só a partição escolhida e reaproveita a inicialização e a recuperação que já existem no disco.
    /// Retorna null com o motivo quando não dá para instalar nela.
    /// </summary>
    public static (PlanoParticoes? Plano, string Erro) ParticaoExistente(Disco disco, Particao escolhida, bool uefi, char sistema, char windows, char recuperacao)
    {
        if (!escolhida.Selecionavel) return (null, "Essa partição é protegida (inicialização, recuperação ou fabricante).");
        if (uefi != disco.Gpt)
            return (null, uefi ? "Este PC inicia em UEFI e o disco é MBR. Use uma das opções que apagam o disco."
                               : "Este PC inicia em BIOS e o disco é GPT. Use uma das opções que apagam o disco.");
        var boot = uefi
            ? disco.Particoes.FirstOrDefault(p => p.Tipo == TipoParticao.Efi)
            : disco.Particoes.FirstOrDefault(p => p.Ativa && p.Numero != escolhida.Numero);
        if (uefi && boot == null) return (null, "O disco não tem partição EFI. Use uma das opções que apagam o disco.");
        var rec = disco.Particoes.FirstOrDefault(p => p.Tipo == TipoParticao.RecuperacaoWindows);

        var s = new StringBuilder().AppendLine($"select disk {disco.Numero}");
        // Partição com letra é selecionada pela letra: não depende da numeração do diskpart.
        char letraWindows = escolhida.Letra ?? windows;
        s.AppendLine(escolhida.Letra is char l ? $"select volume {l}" : $"select partition {escolhida.Numero}")
         .AppendLine("format quick fs=ntfs label=Windows");
        if (escolhida.Letra is null) s.AppendLine($"assign letter={windows}");
        if (boot == null) s.AppendLine("active"); // MBR sem partição ativa: o próprio Windows inicia

        char letraSistema = boot == null ? letraWindows : boot.Letra ?? sistema;
        if (boot is { Letra: null }) s.AppendLine($"select partition {boot.Numero}").AppendLine($"assign letter={sistema}");
        char? letraRec = rec?.Letra ?? (rec == null ? null : recuperacao);
        if (rec is { Letra: null }) s.AppendLine($"select partition {rec.Numero}").AppendLine($"assign letter={recuperacao}");
        s.AppendLine("exit");
        return (new PlanoParticoes(s.ToString(), letraSistema, letraWindows, letraRec), "");
    }
}
