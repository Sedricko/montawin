namespace MontaWin.Implantador;

public enum NivelSaude { Desconhecida, Boa, Atencao, Falhando }

public record Saude(NivelSaude Nivel, string Texto)
{
    public static readonly Saude Desconhecida = new(NivelSaude.Desconhecida, "Saúde do disco não disponível (o disco ou a controladora não informam).");

    /// <summary>Etiqueta curta para a lista de discos.</summary>
    public string Curta => Nivel switch
    {
        NivelSaude.Boa => "Saúde OK",
        NivelSaude.Atencao => "ATENÇÃO",
        NivelSaude.Falhando => "FALHANDO",
        _ => "",
    };

    /// <summary>Lê a página de saúde do NVMe (NVME_HEALTH_INFO_LOG, 512 bytes).</summary>
    public static Saude Nvme(byte[] log)
    {
        byte critico = log[0], reserva = log[3], limiteReserva = log[4], desgaste = log[5];
        int temperatura = BitConverter.ToUInt16(log, 1) - 273; // Kelvin
        long horas = BitConverter.ToInt64(log, 128), errosMidia = BitConverter.ToInt64(log, 160);
        var detalhes = $"{desgaste}% de desgaste · {horas} h ligado · {temperatura} °C";
        if (critico != 0 || reserva < limiteReserva)
            return new(NivelSaude.Falhando, $"O SSD informa falha (alerta {critico:X2}h, reserva {reserva}%). Troque o disco. {detalhes}");
        if (desgaste >= 90 || errosMidia > 0)
            return new(NivelSaude.Atencao, $"SSD gasto ou com erros ({errosMidia} erros de mídia). {detalhes}");
        return new(NivelSaude.Boa, $"Saúde boa: {detalhes}");
    }

    /// <summary>SATA/HD: previsão de falha do SMART e a tabela de atributos (SMART READ DATA, 512 bytes).</summary>
    public static Saude Ata(bool prevêFalha, byte[] smart)
    {
        if (prevêFalha) return new(NivelSaude.Falhando, "O disco avisa pelo SMART que vai falhar. Troque o disco.");
        // Tabela: 30 atributos de 12 bytes a partir do byte 2 (id, flags, atual, pior, valor bruto de 6 bytes, reservado)
        var brutos = new Dictionary<byte, long>();
        for (int o = 2; o + 12 <= Math.Min(smart.Length, 362); o += 12)
            if (smart[o] != 0) brutos[smart[o]] = BitConverter.ToUInt32(smart, o + 5);
        if (brutos.Count == 0) return Desconhecida;
        long realocados = brutos.GetValueOrDefault((byte)5), pendentes = brutos.GetValueOrDefault((byte)197), incorrigiveis = brutos.GetValueOrDefault((byte)198);
        if (realocados + pendentes + incorrigiveis > 0)
            return new(NivelSaude.Atencao, $"Setores com defeito: {realocados} realocados, {pendentes} pendentes, {incorrigiveis} incorrigíveis.");
        return new(NivelSaude.Boa, "Saúde boa: SMART sem alertas nem setores com defeito.");
    }
}
