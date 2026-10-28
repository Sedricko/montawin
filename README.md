# MontaWin

Kit de formatação e implantação do Windows para técnicos. São dois programas:

| Programa | Onde roda | O que faz | Situação |
|---|---|---|---|
| **Montador** | No PC do técnico | Prepara o pendrive bootável a partir de uma ISO oficial | A fazer |
| **Implantador** | No pendrive (WinPE) | Menu próprio: escolher disco, backup de drivers e arquivos, formatar e instalar | Primeira versão |

## Implantador

Abre sozinho quando o PC dá boot pelo pendrive. Menu em tela de texto, usado com as setas:

- **Escolher disco:** lista os discos internos com modelo, tamanho e se têm Windows. Discos USB nunca aparecem, para não apagar o pendrive.
- **Backup de drivers:** exporta os drivers do Windows antigo (`dism /Export-Driver`) para o pendrive. Eles são reinstalados automaticamente depois.
- **Backup de arquivos:** três modos: pastas pessoais de todos os usuários (Área de Trabalho, Documentos, Downloads, Imagens, Músicas, Vídeos), perfil completo (inclui AppData, sem a pasta Temp) ou escolher usuários e pastas uma a uma. O destino é o pendrive ou outro disco, e o programa avisa antes se não couber.
- **Formatar e instalar:** escolhe a edição e o disco de destino (pode ser diferente do disco do backup; bloqueia se um backup estiver nele), apaga o disco (pede para digitar `FORMATAR`), cria as partições (GPT/UEFI ou MBR/BIOS), aplica a imagem com DISM, reinstala os drivers e cria a inicialização com `bcdboot`.
- **Prompt de comando, Reiniciar e Desligar.**

Ele procura a pasta `MontaWin\Imagens` (com os arquivos `.wim`) em todas as unidades. Os backups ficam em `MontaWin\Backups\<data>`.

### Testar numa máquina virtual (antes de o Montador existir)

1. Baixe o `Implantador.exe` em **Actions → build → Artifacts**.
2. Crie um disco virtual extra (VHD) formatado em NTFS com:
   ```
   MontaWin\Implantador.exe
   MontaWin\Imagens\install.wim     (copiado de sources\install.wim da ISO)
   ```
3. Dê boot na VM pela ISO oficial do Windows. Na primeira tela, aperte **Shift+F10** e rode:
   ```
   D:\MontaWin\Implantador.exe
   ```
   A letra pode ser outra; use `dir D:\`, `dir E:\` etc. para achar.

## Compilar

O GitHub Actions compila a cada push (`.github/workflows/build.yml`): roda os testes e gera o `Implantador.exe` nativo (Native AOT), que não precisa de .NET instalado.

Localmente, no Windows, com o .NET 8 SDK:

```
dotnet test tests/Implantador.Tests
dotnet publish src/Implantador -c Release -r win-x64 -o publicar
```
