# Aurora Win

Kit de formatação e implantação do Windows (uso pessoal). Um programa só, **AuroraWin.exe**, que grava um pendrive bootável a partir de uma ISO oficial do Windows. O painel que abre no boot do pendrive (backups, particionamento e instalação) vai embutido nele.

## Como usar

1. Baixe o `AuroraWin.exe` na **[última Release](../../releases/latest)** (versões de teste: **Actions → build → Artifacts → AuroraWin**) e abra (ele pede administrador).
2. **Bloco Windows:** escolha uma ISO oficial do Windows 10/11 e marque as edições. O app mostra a versão (ex.: 25H2) e o certificado do boot da ISO.
   - **.NET Framework 3.5:** ativado em cada edição a partir da própria ISO.
   - **Atualizações do mês (automático):** baixa do Microsoft Update Catalog a cumulativa e a do .NET mais novas (sem "Preview") e instala em cada edição. Também aceita uma pasta com `.msu` já baixados.
3. **Bloco Pendrive:** escolha o pendrive (16 GB ou mais). Na primeira vez tudo nele é apagado; num pendrive já gravado, **Manter** só adiciona a ISO e atualiza o painel. Antes de apagar, o app confere se o disco ainda é o mesmo pendrive escolhido.
4. **Bloco Programas:** a caixa de cada programa diz se ele **já vem marcado** no painel; o selo diz de onde instala (**no pendrive**, **internet**, **Store**, **só internet**, **arquivo**). Populares com um clique, procura no winget, instalador próprio (.exe/.msi com parâmetros silenciosos) e remover vários de uma vez (Ctrl/Shift). "Baixar os programas para o pendrive" deixa tudo instalando sem internet; o Chrome vem direto do Google (o .msi corporativo).
5. **Bloco Perfis:** os perfis do pendrive (por pessoa ou por PC): novo (começa do zero), editar, duplicar e apagar. Senhas e licença continuam sendo guardadas pelo painel, no PC.
6. **Criar pendrive:** a janela sai da frente e uma janelinha animada acompanha a gravação (anel com o percentual, etapa, tempo restante e o que já foi feito).
7. No PC, dê boot pelo pendrive: o **painel Aurora Win abre sozinho**.

**Engrenagem (Configurações):** tema (**Aurora**, **Galáxia**, **Alvorada** ou **Cristal**), que vale para o app e para o painel do pendrive; licença do Windows (opcional); certificado do boot (**Compatível 2011**, padrão, ou **Novo 2023**, tirado do próprio WinPE de ISOs atualizadas, para PCs que já têm o certificado novo); instalação mais rápida (compressão rápida); e o **PIN de administrador**.

**PIN e dados escondidos:** com o PIN definido, "Esconder os dados do pendrive no Windows" marca a parte de dados (perfis, licença, relatórios, backups e imagens) como oculta (tipo 0x27): no PC de um cliente só aparece a parte de boot. O painel do pendrive acha os dados sozinho; no app, "Mostrar com o PIN" mostra só enquanto ele está aberto. É esconder, não criptografar: quem souber usar o diskpart ainda chega aos arquivos.

O app tem janela própria (sem a moldura do Windows, cantos arredondados, blocos de vidro) e avisos no mesmo visual, sem as caixas do Windows.

## O pendrive

As pastas no pendrive continuam se chamando `MontaWin` (nome antigo), para os pendrives, perfis e backups já gravados continuarem valendo.

| Partição | Sistema | Conteúdo |
|---|---|---|
| `MONTAWIN` (2 GB) | FAT32 | Arquivos de boot (UEFI e BIOS) e `sources\boot.wim`: o WinPE da ISO com o Implantador dentro e um `winpeshl.ini` que abre o painel em vez do instalador da Microsoft |
| `MONTAWIN-DADOS` (resto) | NTFS | `MontaWin\Imagens\<nome da ISO>.wim` com as edições escolhidas, `MontaWin\Backups\<data>`, `MontaWin\Drivers` (drivers extras) e `MontaWin\Logs` (registro de cada uso) |

**Drivers extras:** coloque em `MontaWin\Drivers` as pastas com `.inf` de drivers de disco (Intel RST/RAID/VMD) quando o disco do PC não aparecer no painel. O painel carrega esses drivers ao abrir e também os instala no Windows novo.

**Registro:** cada vez que o painel abre, ele grava em `MontaWin\Logs\<data>.txt` os comandos executados e os erros. Se algo falhar num PC, esse arquivo mostra o que aconteceu.

## O painel (Implantador)

Painel em **tela cheia**, com o nome e o ícone do Aurora Win, no tema escolhido no app, usado com o mouse ou o teclado (setas, Enter, Esc). Se o WinPE não conseguir desenhar a tela gráfica, o painel abre a versão em texto, com as mesmas opções. Ao abrir, coloca o WinPE no plano de energia Alto desempenho e carrega os drivers extras do pendrive.

- **Voltar em todas as telas:** toda tela tem "← Voltar", e o Esc volta um passo (as respostas já dadas ficam guardadas).
- **Botões separados das opções:** "← Voltar" e "Continuar →" ficam fixos embaixo do cartão, sempre à vista; só as opções rolam. O resumo e o fim da instalação têm botões lado a lado (setas trocam o botão).
- **Mouse:** clique escolhe, a rodinha rola e a opção sob o mouse fica destacada. O segundo clique de um clique duplo não escolhe nada na tela seguinte.
- **Fonte:** o AuroraWin.exe copia a Segoe UI e a Consolas do PC do técnico para `MontaWin\Fontes`; o WinPE não tem a Segoe UI completa (o texto saía inclinado).
- **Listas de marcar** (programas, drivers, pastas, ajustes): clique no item ou aperte Enter/Espaço para marcar, "Marcar todos" para tudo e "Continuar →" para seguir (Tab vai direto para ele). Os programas marcados no app já vêm escolhidos.
- **Assistente:** o resumo traz sugestões sobre o PC (pouca RAM, modo BIOS, Secure Boot desligado, processador fora da lista do Windows 11, HD em vez de SSD, disco pequeno), e as mensagens de erro conhecidas (falta de espaço, arquivo corrompido, pendrive que desconectou, disco protegido, BitLocker, boot) vêm com "O que costuma resolver". É só sugestão: a decisão é sua.
- **Confirmação em dois passos:** primeiro o resumo de tudo ("Formatar e instalar", "← Voltar e corrigir" ou "Cancelar"); depois um aviso vermelho em que você digita **SIM**. O vermelho fica só para o que apaga dados; avisos comuns ("nenhum backup encontrado") são neutros.
- **Progresso detalhado:** lista de etapas, a caixa "O que está acontecendo" (partições, apps removidos, drivers, arquivos copiados com a velocidade...), o tempo decorrido e o restante. Na cópia do Windows mostra quanto já foi copiado e a velocidade ("3,1 de 8,6 GB · 45 MB/s").

- **Escolher disco:** lista os discos internos com modelo, tamanho e se têm Windows. Discos USB e o disco onde está a pasta MontaWin nunca aparecem, para não apagar o pendrive.
- **Backup (drivers e arquivos):** marca o que salvar (os dois vêm marcados). Na instalação, se o disco escolhido tem Windows e o backup ainda não foi feito, o painel oferece fazer antes de formatar.
- **Backup automático dos drivers:** ao formatar um disco que tem Windows, os drivers são salvos sozinhos antes de apagar (rede, Wi-Fi, vídeo, áudio...), sem perguntas, se o pendrive tiver 2 GB livres (o resumo avisa se não tiver). Se o backup falhar, a formatação segue. O perfil pode desligar.
- **Backup de drivers:** exporta os drivers do Windows antigo para o pendrive e mostra a lista agrupada por tipo. Os básicos (Rede, Vídeo, Áudio, Chipset, Armazenamento, USB, Bluetooth...) já vêm marcados; impressoras, firmware e tipos desconhecidos vêm desmarcados. Os marcados são reinstalados sozinhos depois da instalação.
- **Backup de arquivos:** três modos: pastas pessoais de todos os usuários (Área de Trabalho, Documentos, Downloads, Imagens, Músicas, Vídeos), perfil completo (inclui AppData, sem a pasta Temp) ou escolher usuários e pastas uma a uma. O destino é o pendrive ou outro disco: a lista mostra em qual **cabe** e em qual **NÃO CABE**, e se não cabe em nenhum o painel avisa antes de copiar. Sem Windows nos discos, avisa logo que não há o que salvar; com um só, ele já vem escolhido. Copia 16 arquivos ao mesmo tempo.
- **Restaurar backup de arquivos:** copia um backup para `C:\Arquivos restaurados` de um Windows já instalado (uso manual, a qualquer momento).
- **Formatar e instalar:** escolhe a edição e o disco de destino (pode ser diferente do disco do backup; bloqueia se um backup estiver nele) e como instalar:
  - **Apagar o disco inteiro:** EFI + MSR + Windows + Recuperação (1 GB), ou MBR equivalente em PCs BIOS.
  - **Apagar e dividir:** você digita o tamanho do Windows (C:) e o resto vira a partição **Dados**.
  - **Partição existente:** formata só a partição escolhida e reaproveita EFI e recuperação; as outras ficam intactas. Partições de inicialização, recuperação e de fabricante (Dell, HP, Lenovo...) são protegidas e não podem ser escolhidas.

  Depois pergunta a **configuração inicial**: automática (cria uma conta local e pula as telas da Microsoft) ou normal, e, se houver backup de arquivos, se ele deve **voltar para o Windows novo**. Com a configuração automática, as pastas pessoais voltam para as pastas do usuário novo no primeiro login, e o arquivo com a senha (`Windows\Panther\unattend.xml`) é apagado logo em seguida. Antes de confirmar, mede a velocidade do pendrive e avisa se estiver lento (USB 2.0) ou se o notebook estiver na bateria. Mostra o resumo, pede para digitar `SIM`, avisa se a recuperação de fábrica vai ser apagada, aplica a imagem com DISM, reinstala os drivers, cria a inicialização com `bcdboot` e configura a recuperação do Windows (WinRE).
- **Nome do computador, Wi-Fi e licença:** a instalação pergunta o nome do PC na rede (sugere um aleatório, ex.: `PC-7K2P9QX`), a rede Wi-Fi (mostra as redes que o Windows antigo conhecia e, se o WinPE tiver Wi-Fi, as com sinal agora; só os nomes, a senha é digitada: a salva no Windows antigo é criptografada por ele e não é lida) e se ativa com a licença do pendrive. O Wi-Fi e a licença são aplicados antes da tela de login, e os arquivos com a senha e a chave são apagados logo em seguida. As telas da Microsoft continuam puladas, com ou sem rede.
- **Antes de formatar, a imagem é conferida:** o AuroraWin.exe grava a impressão digital (SHA-256) de cada imagem; o painel confere uma vez e, se a cópia no pendrive estiver corrompida, para sem apagar nada.
- **Relatório da formatação:** no fim, um arquivo na área de trabalho do cliente e uma cópia em `MontaWin\Relatorios` com o PC, o que foi escolhido e o que foi feito (sem senhas).
- **Conferência no primeiro login:** depois dos programas, acrescenta ao relatório da área de trabalho quais programas instalaram (OK/FALHOU), os dispositivos sem driver e se o Windows está ativado. Se algum dispositivo ficou sem driver, tenta de novo com a cópia do backup de drivers que o painel deixa em `C:\MontaWin\Drivers` (até 1,5 GB; apagada sozinha quando fica tudo certo).
- **Depois da instalação:** o relógio é acertado antes de baixar os programas, o Windows Update já começa a procurar os drivers que faltam no primeiro login e, se marcado, é criado o ponto de restauração "Windows recém-instalado" depois dos programas.
- **Saúde do disco:** lê o SMART direto do disco (NVMe: desgaste, alertas, erros e temperatura; SATA/HD: previsão de falha e setores com defeito). Mostra "Saúde OK", "ATENÇÃO" ou "FALHANDO" na lista de discos, avisa ao escolher um disco com problema e repete o aviso antes de formatar.
- **Programas no primeiro login** (com a configuração automática): lista com caixas de marcar, lida de `MontaWin\Programas.txt` (editável). Instala **antes da tela de login**, sem janelas: os do pendrive sempre e os do `winget` se houver internet (cabo de rede); o que faltar (ou precisar do usuário, como os da Microsoft Store) instala logo depois do primeiro login, em segundo plano e sem janela. No primeiro login, espera a internet por até 10 minutos antes de desistir. Os scripts são UTF-8 (`chcp 65001`): instaladores com acento no nome funcionam. Nada é instalado duas vezes, e o resultado fica em `C:\Windows\Setup\Scripts\MontaWin-programas.log`. Padrão: Google Chrome, WinRAR, Visual C++ 2008–2022, Notepad++, Lightshot, pCloud, Spotify, Microsoft 365 (Word, Excel, Outlook) e Foxit PDF Editor Pro. Programas fora do `winget` (ex.: NVIDIA App) vão na pasta `MontaWin\Instaladores`, com uma linha `Nome = arquivo: instalador*.exe parâmetros-silenciosos` na lista; o instalador é copiado para o Windows novo e roda no primeiro login.
- **PC reconhecido:** o painel identifica o PC pelo UUID da placa-mãe e pelo número de série (valores genéricos de fábrica são ignorados). Se ele tem perfil, a primeira tela já oferece "Formatar com o perfil": só falta escolher o disco, conferir o resumo e digitar SIM. Disco sem Windows (novo ou trocado): os drivers vêm do último backup daquele mesmo PC.
- **Recomendado ou Personalizar:** no Recomendado, conta local sem as telas da Microsoft, otimização, sem OneDrive, ponto de restauração e o backup de volta já vêm escolhidos; telas óbvias são puladas (um Windows só no pendrive, disco vazio) e o nome do PC vem sugerido. A imagem é conferida enquanto você responde.
- **Licença por PC:** usar a do pendrive, digitar a deste PC ou nenhuma; fica no perfil.
- **Perfis:** cada perfil guarda as respostas da instalação de um PC (edição, como particionar, usuário, senha opcional, programas, restaurar arquivos e backup de drivers automático) em `MontaWin\Perfis\<nome>.txt`. Um perfil pode ser vinculado a um PC pela identidade da placa-mãe (UUID do SMBIOS): o painel o sugere sozinho nesse PC e avisa se for usado em outro. Com perfil, a instalação só pede o disco e a confirmação.
- **Backups por PC:** ficam em `MontaWin\Backups\<perfil ou modelo do PC>\<data>`, com um `pc.txt` que identifica o PC. Os drivers só são reinstalados a partir do backup feito naquele mesmo uso do painel, no mesmo PC.
- **Multi boot:** em "Formatar e instalar", a opção "Vários Windows no mesmo disco" instala de 2 a 4 Windows (2 em PCs BIOS), cada um na sua partição, com o nome da edição no menu de boot.
- **Otimização** (pergunta na instalação e opção do perfil): remove apps inúteis e de propaganda (Notícias, Clima, Xbox, Paciência, Clipchamp, To Do, Mapas, Copilot...), bloqueia apps "sugeridos" (Candy Crush etc.), desliga Widgets, deixa a pesquisa do Iniciar só no PC, telemetria no mínimo e, só em SSD, tira o atraso dos programas da inicialização. **Não mexe** em Defender, firewall, UAC, SmartScreen, Windows Update nem Microsoft Store.
- **Ajustes opcionais:** remover o OneDrive (marcado) e modo de energia Alto desempenho (desmarcado; melhor em PC de mesa).
- **Capturar imagem de um Windows:** salva um Windows instalado em `MontaWin\Imagens\Personalizadas.wim`, que aparece na lista de edições. Para usar em outros PCs, rode antes `sysprep /generalize /oobe /shutdown` nele.
- **Fim da instalação:** com perfil, reinicia sozinho em 15 s (qualquer tecla cancela); sem perfil, oferece salvar as escolhas como perfil.
- **Prompt de comando dentro do painel:** o `cmd` roda escondido e a saída aparece num cartão do painel; não tem janela própria, então nada fecha o painel por engano (no WinPE, fechar o painel reinicia o PC). Volta com Esc, "← Voltar ao painel" ou `exit`; "Parar comando" encerra o que estiver travado. Abas de atalhos (F1–F8, Tab troca de aba): **Testes** (resumo do PC, discos, partições, partições sem letra, verificar C: só lendo, rede, internet, BitLocker), **Reparo** (chkdsk /f, sfc offline, DISM RestoreHealth com a imagem do pendrive, desfazer atualização travada, recriar o boot, drivers instalados, destravar BitLocker), **Backup e acesso** (pastas da raiz, e-mails do Outlook .pst/.ost, procurar arquivo, copiar pasta até protegida com `robocopy /B`, copiar um disco inteiro, dar letra a partição escondida, exportar drivers, navegar pelo Bloco de notas) e **Geral** (log, arquivos do pendrive, Bloco de notas, limpar). Os botões com "…" mudam alguma coisa e só preenchem o comando: confira e aperte Enter.
- **Reiniciar e Desligar.**

## Compilar

O GitHub Actions compila a cada push (`.github/workflows/build.yml`): roda os testes, gera o painel `Implantador.exe` nativo (Native AOT, roda no WinPE sem .NET) e o `AuroraWin.exe` (WPF, exe único que não precisa de .NET instalado), com o painel embutido.

Localmente, no Windows, com os SDKs .NET 8 e 10:

```
dotnet test tests/Implantador.Tests
dotnet publish src/Implantador -c Release -r win-x64 -o publicar
dotnet publish src/Montador -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:PainelExe=%CD%\publicar\Implantador.exe -o publicar
```

## Próximas atualizações

- **Desempenho:** otimizar a imagem uma vez no app (em vez de remover os apps a cada formatação), sem tirar a opção "Windows como a Microsoft entrega".
- **Programas:** aviso e atualização quando sair versão nova dos que estão no pendrive.
- **Ícones dos programas:** hoje vêm do site do produto (conhecidos), do próprio instalador .exe (no pendrive ou adicionado por arquivo) e da página do winget, e o ícone genérico é descartado. Falta: instalador .msi e programas só da internet sem site no winget ainda ficam com a letra.
- **Versões novas do Windows** (ex.: ISO build 26300): reconhecer sozinho qual atualização do mês serve, sem precisar atualizar o app.
- **Vários Windows no mesmo pendrive (10/11 junto com 8.1 e 7)** — projeto:
  - **Lista organizada:** a escolha do Windows agrupa por versão (Windows 11, Windows 10, Windows 8.1 / 7), do mais novo para o mais velho, com o selo "antigo" no 8.1 e no 7.
  - **Windows 7 e 8.1 com o que funciona neles:** ao escolher, uma tela explica o que muda; o painel desliga sozinho a otimização (só serve para 10/11), a conferência do primeiro login e os programas do winget (ficam só os instaladores do pendrive), ativa a licença pelo `slmgr` em vez do PowerShell novo, e roda o primeiro login com `cmd` normal (o `conhost --headless` não existe no 7/8: sem isso o arquivo com a senha não seria apagado).
  - **Boot sempre do Windows mais novo:** ao gravar com "Manter", se a ISO for mais velha que a que já está no pendrive, o painel continua no WinPE da mais nova (drivers de PCs novos, bcdboot com UEFI); só a imagem da ISO velha é acrescentada.
  - **Sugestão pelo PC:** o assistente avisa quando o Windows 7 não serve para o PC (Secure Boot ligado, disco NVMe, só USB 3, mais de 4 TB) e sugere o 10 ou o 11. Sempre só sugestão.
  - Testes: lista ordenada, regras do 7/8.1 (scripts sem `conhost --headless`, sem winget, licença pelo slmgr) e a escolha do WinPE pelo build.
- **Atualizar o pendrive sem apagar e sem ISO** — projeto:
  - Botão **"Atualizar o pendrive"** (num pendrive já gravado): troca só o painel (Implantador dentro do boot.wim do próprio pendrive), o tema, os fundos, o ícone, a licença e a lista de programas. Não precisa da ISO e leva uns 2 minutos, em vez de copiar o Windows de novo.
  - **Windows do pendrive como lista:** ver quais ISOs estão nele (versão, edições, tamanho), **acrescentar** outra e **remover** uma sem mexer nas outras, nos perfis e nos backups.
  - **Aviso de versão nova:** ao conectar um pendrive gravado com uma versão antiga do app, oferece atualizar o painel.
  - Apagar o pendrive só na primeira vez ou quando o técnico pedir ("Apagar e recriar"), sempre com a confirmação.
- **Restaurar por programa (favoritos e senhas)** — projeto:
  - No backup, um modo **"migração"** copia as pastas de perfil dos programas que a pessoa escolher (navegadores, Outlook/e-mail, etc.), com os favoritos e o cofre de senhas **do jeito que estão** (criptografados pelo próprio programa) — nunca uma lista de senhas em texto.
  - Na restauração, uma tela mostra o que há no backup (Chrome, Firefox, Outlook...) e a pessoa marca o que devolver. O Aurora Win devolve cada pasta no lugar certo do programa **já instalado** (ex.: `AppData\Roaming\Mozilla` para o Firefox).
  - **Limite honesto:** Firefox devolve os favoritos e as senhas copiando a pasta. Chrome/Edge (Chromium): os favoritos voltam, mas as senhas salvas são presas ao login do Windows antigo e muitas vezes **não** voltam só copiando a pasta — o certo é a pessoa entrar na conta Google/Microsoft do navegador, que sincroniza as senhas. Outlook: contas e e-mails (.pst/.ost) voltam; a senha de cada conta é digitada uma vez.
  - Sem varredura do PC nem arquivo com senhas decifradas (um arquivo desses no pendrive seria um risco para o cliente).
- **Instalação remota, ao lado do Windows antigo** — projeto:
  - O cliente baixa o **AuroraWin-Remoto.exe**, abre como administrador e lê o aviso. Você entra pelo AnyDesk/TeamViewer e configura o **perfil** no mesmo assistente do painel. O cliente digita **SIM** na tela dele (sempre, e fica registrado no relatório com data e hora).
  - Depois disso a ferramenta faz **toda a formatação sozinha**: encolhe a C:, cria a partição nova, aplica a imagem (DISM, com o Windows antigo funcionando), conta, drivers, programas, otimização, relatório, cria a entrada de boot e reinicia no Windows novo. Você acompanha ao vivo pelo AnyDesk.
  - **Proteções, conferidas antes de começar (se uma falhar, nada é alterado):** espaço livre de pelo menos 40 GB além do Windows antigo; BitLocker (suspende ou recusa); notebook na tomada; saúde do disco (SMART: não segue se "falhando"); imagem conferida pelo SHA-256; modo de boot compatível (UEFI/BIOS, Secure Boot); só mexe na C: e na partição nova, nunca em outras partições de dados.
  - **O Windows antigo some sozinho, depois de um prazo:** no começo ele fica como plano B e como origem dos arquivos do cliente (o novo copia de lá, sem backup externo), e o menu de boot mostra os dois, com o novo como padrão e o antigo chamado **"Windows antigo (recuperação)"**, para uma emergência (entrar nele, ou copiar arquivos dele pela aba "Backup e acesso" do prompt do pendrive). O prazo (14 dias, ajustável no perfil) já é dito no aviso que o cliente autoriza com o SIM. Uma tarefa agendada no Windows novo remove a partição antiga quando o prazo acaba.
  - **Proteções da remoção automática:** só remove se o Windows novo é o que está rodando e já iniciou bem várias vezes; só apaga a partição que o instalador registrou como a antiga (pelo identificador do volume, conferindo que tem um Windows dentro), nunca outra; o cliente é avisado alguns dias antes e tem um botão **"Manter o antigo"** (e um "Apagar agora"); se o espaço do novo estiver no fim, ela adia em vez de apagar; tudo vai para o log.
  - Cancelar até a hora de reiniciar; log completo e relatório no fim. A autorização é sempre dada na hora, nunca guardada no perfil.
  - Primeiro passo (menos risco): **pós-instalação remota** num Windows já instalado (programas do perfil, otimização, nome do PC, ponto de restauração e relatório), sem apagar nada. Testar muito numa VM antes de qualquer cliente real.
  - Limites: precisa de espaço livre e de a C: poder ser encolhida; o espaço do Windows antigo, depois de apagado, vira uma partição de dados; o arquivo baixado pode ser barrado por antivírus/SmartScreen (o ideal é assiná-lo).
- **Senhas criptografadas** com o PIN (hoje os dados ficam escondidos, não criptografados).

## Limites conhecidos

- Só Windows 64 bits (x64).
- Não copia a pasta do OneDrive no modo "pastas pessoais" (arquivos que estão só na nuvem falham no WinPE); dá para marcar no modo "escolher pastas".
- Discos NVMe em modo RAID/Intel RST podem não aparecer no WinPE da ISO por falta de driver.
- O nome do grupo de administradores e o fuso horário da conta automática só são traduzidos para pt-BR.
- O WinPE da ISO não tem Wi-Fi: no painel só aparecem as redes conhecidas do Windows antigo. A internet do painel é só por cabo.
- Sem internet no primeiro boot, o Windows pode mostrar "Algo não deu certo (OOBEZDP)" na busca de atualizações da configuração inicial: clique em Pular. Os programas da internet ficam para quando houver rede.
- No primeiro login o Windows 24H2 pode abrir sozinho uma janela preta do `tzsync.exe` (acerto do fuso): é do Windows e fecha sozinha.
