# MontaWin

Kit de formatação e implantação do Windows para técnicos. São dois programas que trabalham juntos:

| Programa | Onde roda | O que faz |
|---|---|---|
| **MontaWin.exe** (Montador) | No PC do técnico | Grava o pendrive bootável a partir de uma ISO oficial do Windows |
| **Implantador.exe** | No pendrive (WinPE) | Painel próprio que abre no boot: backups, particionamento e instalação |

## Como usar

1. Baixe o pacote em **Actions → build (o mais recente) → Artifacts → MontaWin**. Deixe `MontaWin.exe` e `Implantador.exe` **na mesma pasta**.
2. Abra o `MontaWin.exe` (ele pede administrador).
3. **Escolher ISO:** use uma ISO oficial do Windows 10/11 ([download da Microsoft](https://www.microsoft.com/software-download/windows11)) e marque as edições que vão para o pendrive.
4. Escolha o **pendrive** (16 GB ou mais) e clique em **Criar pendrive**. Na primeira vez, tudo nele é apagado. Se o pendrive já foi gravado pelo MontaWin, a opção **Manter** só adiciona a nova ISO e atualiza o painel, sem apagar imagens e backups.
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

Menu em tela de texto, usado com as setas. Ao abrir, coloca o WinPE no plano de energia Alto desempenho e carrega os drivers extras do pendrive.

- **Escolher disco:** lista os discos internos com modelo, tamanho e se têm Windows. Discos USB nunca aparecem, para não apagar o pendrive.
- **Backup de drivers:** exporta os drivers do Windows antigo para o pendrive e mostra a lista agrupada por tipo. Os básicos (Rede, Vídeo, Áudio, Chipset, Armazenamento, USB, Bluetooth...) já vêm marcados; impressoras, firmware e tipos desconhecidos vêm desmarcados. Os marcados são reinstalados sozinhos depois da instalação.
- **Backup de arquivos:** três modos: pastas pessoais de todos os usuários (Área de Trabalho, Documentos, Downloads, Imagens, Músicas, Vídeos), perfil completo (inclui AppData, sem a pasta Temp) ou escolher usuários e pastas uma a uma. O destino é o pendrive ou outro disco, e o programa avisa antes se não couber. Copia 16 arquivos ao mesmo tempo.
- **Restaurar backup de arquivos:** copia um backup para `C:\Arquivos restaurados` de um Windows já instalado (uso manual, a qualquer momento).
- **Formatar e instalar:** escolhe a edição e o disco de destino (pode ser diferente do disco do backup; bloqueia se um backup estiver nele) e como instalar:
  - **Apagar o disco inteiro:** EFI + MSR + Windows + Recuperação (1 GB), ou MBR equivalente em PCs BIOS.
  - **Apagar e dividir:** você digita o tamanho do Windows (C:) e o resto vira a partição **Dados**.
  - **Partição existente:** formata só a partição escolhida e reaproveita EFI e recuperação; as outras ficam intactas. Partições de inicialização, recuperação e de fabricante (Dell, HP, Lenovo...) são protegidas e não podem ser escolhidas.

  Depois pergunta a **configuração inicial**: automática (cria uma conta local e pula as telas da Microsoft) ou normal, e, se houver backup de arquivos, se ele deve **voltar para o Windows novo**. Com a configuração automática, as pastas pessoais voltam para as pastas do usuário novo no primeiro login, e o arquivo com a senha (`Windows\Panther\unattend.xml`) é apagado logo em seguida. Antes de confirmar, mede a velocidade do pendrive e avisa se estiver lento (USB 2.0) ou se o notebook estiver na bateria. Pede para digitar `FORMATAR`, avisa se a recuperação de fábrica vai ser apagada, aplica a imagem com DISM, reinstala os drivers, cria a inicialização com `bcdboot` e configura a recuperação do Windows (WinRE).
- **Saúde do disco:** lê o SMART direto do disco (NVMe: desgaste, alertas, erros e temperatura; SATA/HD: previsão de falha e setores com defeito). Mostra "Saúde OK", "ATENÇÃO" ou "FALHANDO" na lista de discos, avisa ao escolher um disco com problema e repete o aviso antes de formatar.
- **Programas no primeiro login** (com a configuração automática): lista com caixas de marcar, lida de `MontaWin\Programas.txt` (editável). Instala pelo `winget` em segundo plano, na versão mais nova; precisa de internet. Padrão: Google Chrome, WinRAR, Visual C++ 2008–2022, Notepad++, Lightshot, pCloud, Spotify, Microsoft 365 (Word, Excel, Outlook) e Foxit PDF Editor Pro.
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
