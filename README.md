# MontaWin

Kit de formatação e implantação do Windows para técnicos. São dois programas que trabalham juntos:

| Programa | Onde roda | O que faz |
|---|---|---|
| **MontaWin.exe** (Montador) | No PC do técnico | Grava o pendrive bootável a partir de uma ISO oficial do Windows |
| **Implantador.exe** | No pendrive (WinPE) | Painel próprio que abre no boot: backups, particionamento e instalação |

## Como usar

1. Baixe `MontaWin.exe` e `Implantador.exe` na **[última Release](../../releases/latest)** e deixe os dois **na mesma pasta**. (Versões de teste de outros branches ficam em **Actions → build → Artifacts**.)
2. Abra o `MontaWin.exe` (ele pede administrador).
3. **Escolher ISO:** use uma ISO oficial do Windows 10/11 ([download da Microsoft](https://www.microsoft.com/software-download/windows11)) e marque as edições que vão para o pendrive.
4. Escolha o **pendrive** (16 GB ou mais) e clique em **Criar pendrive**. Na primeira vez, tudo nele é apagado. Se o pendrive já foi gravado pelo MontaWin, a opção **Manter** só adiciona a nova ISO e atualiza o painel, sem apagar imagens e backups.
   - **Atualizações do mês (automático):** o MontaWin descobre a versão da ISO (ex.: 25H2), baixa do [Microsoft Update Catalog](https://www.catalog.update.microsoft.com) a atualização cumulativa e a do .NET mais novas (sem as "Preview") e instala em cada edição: o Windows já sai atualizado. Na primeira vez são uns 5 GB; depois o download é reaproveitado. Se o Catalog mudar e o download falhar, ele abre a pesquisa certa no navegador para baixar na mão. Também dá para escolher uma pasta com `.msu` já baixados.
   - **Visual do painel:** escolha o tema (**Ondas**, **Bloom** ou **Pastel**). O próprio MontaWin muda de visual na hora, e o painel do boot usa o mesmo tema.
   - **Programas:** a lista oferecida no painel. **Adicionar pela internet:** digite o nome (ex.: anydesk), clique em Procurar, escolha o resultado do winget e "Adicionar à lista". **Ou um instalador que você já tem:** escolha o `.exe`/`.msi`, confira o nome e os parâmetros silenciosos e adicione; ele vai para o pendrive e instala sem internet. A lista fica guardada neste PC e vai para todo pendrive que você criar; "Salvar programas no pendrive" atualiza um pendrive já pronto sem gravar tudo de novo.
   - **Baixar os programas para o pendrive:** baixa com o `winget` os instaladores da lista (`MontaWin\Instaladores\winget`). No PC eles instalam sem internet; os da Microsoft Store e os que não baixarem ficam para a internet.
   - **Instalação mais rápida:** grava a imagem com compressão rápida (arquivo ~30% maior, instala mais depressa). Bom para pendrive USB 3.0 ou SSD externo.
5. No PC do cliente, dê boot pelo pendrive: o **painel MontaWin abre sozinho**.

## O pendrive

| Partição | Sistema | Conteúdo |
|---|---|---|
| `MONTAWIN` (2 GB) | FAT32 | Arquivos de boot (UEFI e BIOS) e `sources\boot.wim`: o WinPE da ISO com o Implantador dentro e um `winpeshl.ini` que abre o painel em vez do instalador da Microsoft |
| `MONTAWIN-DADOS` (resto) | NTFS | `MontaWin\Imagens\<nome da ISO>.wim` com as edições escolhidas, `MontaWin\Backups\<data>`, `MontaWin\Drivers` (drivers extras) e `MontaWin\Logs` (registro de cada uso) |

**Drivers extras:** coloque em `MontaWin\Drivers` as pastas com `.inf` de drivers de disco (Intel RST/RAID/VMD) quando o disco do PC não aparecer no painel. O painel carrega esses drivers ao abrir e também os instala no Windows novo.

**Registro:** cada vez que o painel abre, ele grava em `MontaWin\Logs\<data>.txt` os comandos executados e os erros. Se algo falhar num PC, esse arquivo mostra o que aconteceu.

## O painel (Implantador)

Painel em **tela cheia**, no tema escolhido no MontaWin, usado com o mouse ou o teclado (setas, Enter, Esc). Se o WinPE não conseguir desenhar a tela gráfica, o painel abre a versão em texto, com as mesmas opções. Ao abrir, coloca o WinPE no plano de energia Alto desempenho e carrega os drivers extras do pendrive.

- **Voltar em todas as telas:** toda tela tem "← Voltar", e o Esc volta um passo (as respostas já dadas ficam guardadas).
- **Listas de marcar** (programas, drivers, pastas, ajustes): clique no item ou aperte Enter/Espaço para marcar, "Marcar todos" para tudo e "Continuar →" para seguir. Os programas começam desmarcados.
- **Confirmação em dois passos:** primeiro o resumo de tudo ("Formatar e instalar", "← Voltar e corrigir" ou "Cancelar"); depois um aviso vermelho em que você digita **SIM**.
- **Progresso detalhado:** lista de etapas, a caixa "O que está acontecendo" (partições, apps removidos, drivers, arquivos copiados com a velocidade...), o tempo decorrido e o restante.

- **Escolher disco:** lista os discos internos com modelo, tamanho e se têm Windows. Discos USB e o disco onde está a pasta MontaWin nunca aparecem, para não apagar o pendrive.
- **Backup (drivers e arquivos):** marca o que salvar (os dois vêm marcados). Na instalação, se o disco escolhido tem Windows e o backup ainda não foi feito, o painel oferece fazer antes de formatar.
- **Backup de drivers:** exporta os drivers do Windows antigo para o pendrive e mostra a lista agrupada por tipo. Os básicos (Rede, Vídeo, Áudio, Chipset, Armazenamento, USB, Bluetooth...) já vêm marcados; impressoras, firmware e tipos desconhecidos vêm desmarcados. Os marcados são reinstalados sozinhos depois da instalação.
- **Backup de arquivos:** três modos: pastas pessoais de todos os usuários (Área de Trabalho, Documentos, Downloads, Imagens, Músicas, Vídeos), perfil completo (inclui AppData, sem a pasta Temp) ou escolher usuários e pastas uma a uma. O destino é o pendrive ou outro disco, e o programa avisa antes se não couber. Copia 16 arquivos ao mesmo tempo.
- **Restaurar backup de arquivos:** copia um backup para `C:\Arquivos restaurados` de um Windows já instalado (uso manual, a qualquer momento).
- **Formatar e instalar:** escolhe a edição e o disco de destino (pode ser diferente do disco do backup; bloqueia se um backup estiver nele) e como instalar:
  - **Apagar o disco inteiro:** EFI + MSR + Windows + Recuperação (1 GB), ou MBR equivalente em PCs BIOS.
  - **Apagar e dividir:** você digita o tamanho do Windows (C:) e o resto vira a partição **Dados**.
  - **Partição existente:** formata só a partição escolhida e reaproveita EFI e recuperação; as outras ficam intactas. Partições de inicialização, recuperação e de fabricante (Dell, HP, Lenovo...) são protegidas e não podem ser escolhidas.

  Depois pergunta a **configuração inicial**: automática (cria uma conta local e pula as telas da Microsoft) ou normal, e, se houver backup de arquivos, se ele deve **voltar para o Windows novo**. Com a configuração automática, as pastas pessoais voltam para as pastas do usuário novo no primeiro login, e o arquivo com a senha (`Windows\Panther\unattend.xml`) é apagado logo em seguida. Antes de confirmar, mede a velocidade do pendrive e avisa se estiver lento (USB 2.0) ou se o notebook estiver na bateria. Mostra o resumo, pede para digitar `SIM`, avisa se a recuperação de fábrica vai ser apagada, aplica a imagem com DISM, reinstala os drivers, cria a inicialização com `bcdboot` e configura a recuperação do Windows (WinRE).
- **Saúde do disco:** lê o SMART direto do disco (NVMe: desgaste, alertas, erros e temperatura; SATA/HD: previsão de falha e setores com defeito). Mostra "Saúde OK", "ATENÇÃO" ou "FALHANDO" na lista de discos, avisa ao escolher um disco com problema e repete o aviso antes de formatar.
- **Programas no primeiro login** (com a configuração automática): lista com caixas de marcar, lida de `MontaWin\Programas.txt` (editável). Instala **antes da tela de login**, sem janelas: os do pendrive sempre e os do `winget` se houver internet (cabo de rede); o que faltar (ou precisar do usuário, como os da Microsoft Store) instala logo depois do primeiro login, em segundo plano e sem janela. Nada é instalado duas vezes, e o resultado fica em `C:\Windows\Setup\Scripts\MontaWin-programas.log`. Padrão: Google Chrome, WinRAR, Visual C++ 2008–2022, Notepad++, Lightshot, pCloud, Spotify, Microsoft 365 (Word, Excel, Outlook) e Foxit PDF Editor Pro. Programas fora do `winget` (ex.: NVIDIA App) vão na pasta `MontaWin\Instaladores`, com uma linha `Nome = arquivo: instalador*.exe parâmetros-silenciosos` na lista; o instalador é copiado para o Windows novo e roda no primeiro login.
- **Perfis:** cada perfil guarda as respostas da instalação de um PC (edição, como particionar, usuário, senha opcional, programas, restaurar arquivos e backup de drivers automático) em `MontaWin\Perfis\<nome>.txt`. Um perfil pode ser vinculado a um PC pela identidade da placa-mãe (UUID do SMBIOS): o painel o sugere sozinho nesse PC e avisa se for usado em outro. Com perfil, a instalação só pede o disco e a confirmação.
- **Backups por PC:** ficam em `MontaWin\Backups\<perfil ou modelo do PC>\<data>`, com um `pc.txt` que identifica o PC. Os drivers só são reinstalados a partir do backup feito naquele mesmo uso do painel, no mesmo PC.
- **Multi boot:** em "Formatar e instalar", a opção "Vários Windows no mesmo disco" instala de 2 a 4 Windows (2 em PCs BIOS), cada um na sua partição, com o nome da edição no menu de boot.
- **Otimização** (pergunta na instalação e opção do perfil): remove apps inúteis e de propaganda (Notícias, Clima, Xbox, Paciência, Clipchamp, To Do, Mapas, Copilot...), bloqueia apps "sugeridos" (Candy Crush etc.), desliga Widgets, deixa a pesquisa do Iniciar só no PC, telemetria no mínimo e, só em SSD, tira o atraso dos programas da inicialização. **Não mexe** em Defender, firewall, UAC, SmartScreen, Windows Update nem Microsoft Store.
- **Ajustes opcionais:** remover o OneDrive (marcado) e modo de energia Alto desempenho (desmarcado; melhor em PC de mesa).
- **Capturar imagem de um Windows:** salva um Windows instalado em `MontaWin\Imagens\Personalizadas.wim`, que aparece na lista de edições. Para usar em outros PCs, rode antes `sysprep /generalize /oobe /shutdown` nele.
- **Fim da instalação:** com perfil, reinicia sozinho em 15 s (qualquer tecla cancela); sem perfil, oferece salvar as escolhas como perfil.
- **Prompt de comando, Reiniciar e Desligar.**

## Compilar

O GitHub Actions compila a cada push (`.github/workflows/build.yml`): roda os testes, gera o `Implantador.exe` nativo (Native AOT, roda no WinPE sem .NET) e o `MontaWin.exe` (WPF, exe único que não precisa de .NET instalado).

Localmente, no Windows, com os SDKs .NET 8 e 10:

```
dotnet test tests/Implantador.Tests
dotnet publish src/Implantador -c Release -r win-x64 -o publicar
dotnet publish src/Montador -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publicar
```

## Limites conhecidos

- Só Windows 64 bits (x64).
- Não copia a pasta do OneDrive no modo "pastas pessoais" (arquivos que estão só na nuvem falham no WinPE); dá para marcar no modo "escolher pastas".
- Discos NVMe em modo RAID/Intel RST podem não aparecer no WinPE da ISO por falta de driver.
- O nome do grupo de administradores e o fuso horário da conta automática só são traduzidos para pt-BR.
