# Auditoria do Aurora Win (app e painel)

Revisão feita sobre o código inteiro de `src/Montador` (app WPF), `src/Implantador` (painel do WinPE) e dos scripts que ele gera para o Windows novo.
Observação: o app é WPF, então o "HTML e CSS" do pedido é o **XAML** (`MainWindow.xaml`); o layout novo está nesse arquivo.

## 1. Estrutura visual (usabilidade)

- **Três abas por contexto de uso**, sem barra de rolagem: `1 · Windows e pendrive` (ISO, edições, .NET, atualizações, pendrive, resumo), `2 · Programas` (lista + populares/busca/instalador), `3 · Perfis` (lista e botões).
- **Barra fixa embaixo** com status e `Criar pendrive`: vê-se sempre o que está acontecendo.
- **Feedback de cada ação:** botões desativados enquanto algo roda (`Ocupado`), barra de progresso que anda sozinha quando não há percentual (novo), avisinho de sucesso que some em 4 s, janela de erro própria (sem MessageBox) e janelinha de progresso na gravação.
- **Regras visuais:** texto sempre na cor do tema (`CorTextoAba`), botão desativado a 60 % (legível), explicações curtas.
- **Fluxo de perfil:** perfil novo nasce com os programas marcados da lista; Wi-Fi com busca de redes e senha; perfil funciona sem pendrive gravado.

## 2. Defeitos encontrados no código que eu escrevi (função, causa, correção)

| Função / local | Por que estava ruim | Correção |
|---|---|---|
| `Pendrive.Gravar` (pasta de trabalho) | Usava a mesma pasta temporária do `Implantador.exe` extraído: apagava o painel antes de copiar (pendrive apagado e sem painel). Erro introduzido na troca de nome. | Pasta própria `AuroraWin-trabalho`. |
| `Pendrive.BootComCertificadoNovo` | `File.Copy` sobre arquivo somente leitura vindo da ISO: "Access denied". | `Substituir()` tira o atributo antes. |
| `MainWindow.Criar` / `Atualizar` / `Ocupado` | Exceção em tarefa assíncrona deixava todos os botões desabilitados; depois de gravar não relia os discos (próxima gravação apagava perfis sem avisar); `Atualizar` selecionava outro disco USB. | try/catch com `Ocupado(false)`, `Atualizar` após gravar, seleção só se for o mesmo disco. |
| `Discos.MesmoPendrive` | Comparava só modelo e tamanho: dois pendrives iguais podiam trocar e receber `clean`. | Compara também o layout das partições; reconferência antes do diskpart (pendrive e disco de instalação). |
| `MainWindow.Criar` (dados escondidos) | Regravava um pendrive com dados escondidos sem aviso e sem preservar perfis. | Bloqueia e pede "Mostrar com o PIN". |
| `ListaProgramas.LerBusca` | A saída do winget separa linhas só com `\r`: o app achava e dizia "nada encontrado". | Separa por `\r` e `\n`, ignora linhas vazias (teste com a saída real). |
| `Comandos.Executar` | Perdia os `\n` em `UltimaSaidaCompleta` (lista de Wi-Fi nunca aparecia); página de código errada no WinPE (acentos). | Guarda tudo; usa `GetOEMCP`. |
| `Icones.IconeDeVerdade` / `DoSite` | Rejeitava JPEG e PNG de 23 px e não olhava o `favicon.ico` do site: VLC, WhatsApp e CPU-Z ficavam como letra. | Aceita JPEG, PNG ≥ 20 px e busca no próprio site. |
| `Program.cs` (instalação) | Falha na restauração do backup, na cópia dos instaladores ou da recuperação abortava o laço antes do `bcdboot`: disco apagado sem boot. | Esses passos viram avisos; o boot sempre é criado. |
| `Unattend.GerarScriptPrimeiroLogin` | A senha do `unattend.xml` ficava até depois de cópias demoradas e nas cópias do Setup. | `del` no começo, nos três caminhos. |
| `BaixarProgramas` | Apagava o instalador bom antes de saber se o novo baixava. | Baixa em `.novo` e só então troca. |
| `Tui.Escrever` (prompt) | `\r` descartado: a barra do DISM virava uma linha enorme; sem limite de tamanho. | Retorno de carro reescreve a linha; limite de 2000. |
| `TelaGrafica.WndProc` (WM_PAINT) | Sem try/finally: exceção vazava o `Graphics` e o `EndPaint`; log que falha derrubava o processo. | try/finally e try/catch no log. |
| `TelaGrafica.Mostrar` | Fila de eventos manipulada por duas threads sem lock. | `lock` + `Empurrar`. |
| Telas com muito texto (`Grafico`, `Tui`) | Resumo longo empurrava o botão para fora; Info grande escondia a lista no console. | Letra menor e corte com "...". |
| `ConfigurarRecuperacao`, `SalvarRelatorio`, `Comandos.Registrar` | `IOException` só; outros erros derrubavam o painel no fim. | Tratam qualquer exceção. |
| `Perfil.Ler` | `Trim()` apagava espaços nas senhas. | Só tira o espaço depois do `=`. |
| `Sugestoes` (processador) | Intel 10ª a 13ª geração marcada como "antiga para o Windows 11". | Só 2ª a 7ª geração. |
| `RestaurarManual` | Listava qualquer pasta "Arquivos" e varria o backup inteiro pelo USB. | Procura só nos níveis `Backups\<pc>\<data>`. |
| `AdicionarArquivo` / `SalvarProgramas` | Instaladores com o mesmo nome se sobrescreviam. | Prefixo com o nome do programa em colisão; copia só os citados. |
| Nome "MontaWin" visível | Pasta do app, logs, scripts do Windows novo, janela do painel, partições. | `AuroraWin` / `Aurora Win` (a pasta `MontaWin` no pendrive continua por compatibilidade). |

## 3. Clean code

- `Redes.cs` (novo): a leitura da saída do `netsh` saiu da janela para uma classe pura, com teste.
- `ListaProgramas.AcrescentarPerfis`, `Resolver`, `Valor`: regras de lista/perfil fora da janela, com teste.
- `Atualizacoes`, `Pendrive`, `ListaProgramas`, `Redes` não dependem de WPF e rodam nos testes.
- Ainda misturado (dívida conhecida): `MainWindow.xaml.cs` (~850 linhas) concentra eventos, perfis e fluxo de gravação, e `Program.cs` do painel (~1500 linhas) tem o assistente inteiro em instruções de topo. Separar exige testar no Windows; fica para uma atualização própria.
