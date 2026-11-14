# Desenvolvimento

Como compilar, testar, gerar os prints e publicar uma versão do SdFake.

## O que precisa estar instalado

| Para | Requisito |
| --- | --- |
| Programa de PC | Windows 10/11. O compilador C# (`csc.exe` do .NET Framework 4) já vem no Windows. |
| App Android | Android SDK (build-tools e uma plataforma; `ANDROID_HOME` ou o local padrão), JDK 17+ (`JAVA_HOME`) e Python 3. O NDK é opcional. |

Não há Visual Studio, NuGet, Gradle nem bibliotecas de terceiros. Os scripts não baixam nada e só
gravam dentro da pasta do projeto.

## Compilar

```powershell
powershell -ExecutionPolicy Bypass -File pc\build.ps1    # programa + instalador + autoteste
python android\build_apk.py --test                       # APK assinado + autoteste
```

O que cada um produz:

| Script | Saída |
| --- | --- |
| `pc\build.ps1` | `pc\dist\SdFake.exe`, `pc\dist\SdFake-Instalador.exe` (leva o programa embutido) e `pc\dist\SdFakeCli.exe`. Use `-NoTest` para pular o autoteste. |
| `android\build_apk.py` | `android\dist\SdFake.apk`, assinado. |

Os dois copiam o resultado para a pasta `Instaladores\`, que é o que vai para a página de Releases.
Essas pastas de saída não entram no Git.

O código de PC é C# 5 (o que o `csc.exe` do Windows aceita): sem interpolação de strings (`$""`),
sem `?.` e sem `nameof`.

### Chave de assinatura do Android

A chave fica em `android\keystore\` (`sdfake.jks` e `senha.txt`) e **não está no repositório**.
Na primeira compilação o script cria uma chave nova nessa pasta.

- Guarde uma cópia dessa pasta: o Android só aceita atualizar um app se o APK novo for assinado
  com a mesma chave.
- Um APK compilado por você tem outra chave. Para instalar por cima do oficial, desinstale o
  oficial antes.

### Biblioteca nativa

`android\native\sdfake.c` é compilado pelo `clang` do NDK para as quatro arquiteturas. As
bibliotecas prontas ficam em `android\native\libs\` e estão no repositório, então dá para montar o
APK sem o NDK. Se o `.c` for alterado, o script recompila (precisa do NDK).

## Testes

### Autoteste dos motores

Os motores de teste rodam contra cartões simulados em memória. É o que o `build.ps1` e o
`build_apk.py --test` executam.

```powershell
pc\obj\SdFakeCli.exe selftest
```

Cenários do Teste Completo, no PC (`pc\tests\Cli.cs`) e no Android (`android\test\SimTest.java`):

- cartão verdadeiro;
- falso que descarta as gravações;
- falso que dá a volta e regrava por cima do começo;
- falso com parte do espaço já ocupada;
- falso que passa a dar erro de gravação;
- cartão verdadeiro com blocos defeituosos;
- cancelamento no meio.

Só no PC:

- falso já reformatado no tamanho real (volume de 243 MB num dispositivo que anuncia 133,3 GB),
  que não pode resultar em VERDADEIRO;
- gravação e conferência de verdade numa pasta do disco, com a pasta de teste removida no fim;
- Teste Rápido: verdadeiro, falso que descarta, falso que dá a volta (em 8 GiB e em 32 MiB),
  pontos defeituosos e cancelamento com o conteúdo original restaurado.

### Linha de comando

```powershell
pc\obj\SdFakeCli.exe list                 # lista as unidades removíveis
pc\obj\SdFakeCli.exe full E:\             # Teste Completo em E:
pc\obj\SdFakeCli.exe full C:\tmp\x 500    # Teste Completo numa pasta, limitado a 500 MiB
pc\obj\SdFakeCli.exe quick E:             # Teste Rápido (prompt como administrador)
```

### Janela do programa

```powershell
powershell -ExecutionPolicy Bypass -File tools\gui-test.ps1
```

Abre o programa apontado para uma pasta de rascunho dentro do projeto (`--pasta` e `--limite`,
opções de diagnóstico), clica em Teste Completo, confirma e espera o resultado.

### App no emulador, com cartão falso

`tools/emu/env.sh` prepara um emulador isolado dentro de `android/build/emu` (nada em `~/.android`,
servidor adb numa porta própria). `tools/emu/ui.py` lê a tela e toca nos elementos pelo texto.

```bash
. tools/emu/env.sh
avdmanager create avd -n sdfake -k "system-images;android-34;google_apis_playstore;x86_64" -c 2000M -d pixel_5
emulator -avd sdfake -no-snapshot -port 5560 &
"$ADB" install -r Instaladores/SdFake.apk
python tools/emu/ui.py dump
```

Para simular um cartão falso (anuncia 2 GB, tem 300 MB), com o emulador desligado:

1. corte `sdcard.img` do AVD para 300 MB;
2. recrie a camada `sdcard.img.qcow2` com
   `qemu-img create -f qcow2 -F raw -b sdcard.img sdcard.img.qcow2`.

O Android monta o cartão acreditando nos 2 GB e o app deve responder FALSO com cerca de 300 MB.
Só cortar o `sdcard.img` não basta: o emulador usa a camada `.qcow2`, que continua com o tamanho
antigo.

## Prints da documentação

```powershell
powershell -ExecutionPolicy Bypass -File tools\prints.ps1
```

Gera os arquivos `docs\telas\pc-*.png` em cerca de 1 minuto. O script compila
`tools\prints\Prints.cs` junto com o código do programa, abre as janelas reais e roda os motores
reais contra cartões **simulados**. Só a lista de dispositivos é trocada por um cartão de exemplo;
nenhum disco é lido ou gravado. O print `pc-seguir.png` (menu do botão Seguir aberto) é uma captura
da tela, então deixe a janela do programa visível enquanto o script roda. Os números do cartão falso são os de um caso real (anuncia
133,3 GB, guarda 252 MB).

Os prints `android-*.png` são capturas do app no emulador (`python tools/emu/ui.py shot arquivo.png`).

Os ícones do programa e do app são desenhados por `tools\make-icons.ps1`.

## Estrutura

| Pasta | Conteúdo |
| --- | --- |
| `pc/src` | Programa Windows: `Core.cs` (padrão de dados, tipos comuns), `FullTest.cs` (Teste Completo), `QuickTest.cs` (Teste Rápido), `Win.cs` (acesso a discos e arquivos), `MainForm.cs` (janela), `WelcomeForm.cs` (boas-vindas), `Program.cs` |
| `pc/installer` | Instalador e desinstalador |
| `pc/tests` | `Cli.cs`: linha de comando, cartões simulados e autoteste |
| `pc/assets` | Ícone e logo |
| `android/src` | App Android. `engine/` é o Teste Completo, o mesmo algoritmo do PC |
| `android/res` | Telas, textos e ícones do app |
| `android/native` | Biblioteca nativa mínima (descarta o cache de leitura) |
| `android/test` | Autoteste do motor Java |
| `android/loja` | Ícone 512 px para loja de apps |
| `tools` | Gerador de ícones, gerador de prints, teste da janela e utilitários do emulador |
| `docs` | Documentação e prints |

## Trocar os endereços do desenvolvedor

Os mesmos endereços aparecem na tela de boas-vindas e no botão **Seguir** do topo.

- PC: classe `Developer` em `pc/src/WelcomeForm.cs`.
- Android: `follow_*` e `url_*` em `android/res/values/strings.xml`.

## Publicar uma versão

1. Atualize a versão em `pc/src/AssemblyInfo.cs`, `pc/installer/AssemblyInfo.cs`,
   `pc/installer/Setup.cs` (constante `Version`) e `android/build_apk.py` (`VERSION_NAME` e
   `VERSION_CODE`, que precisa aumentar a cada versão).
2. Anote as mudanças em `CHANGELOG.md`.
3. Compile os dois lados com os autotestes.
4. Publique os três arquivos de `Instaladores\`:

```bash
cd Instaladores && sha256sum SdFake-Instalador.exe SdFake-Portatil.exe SdFake.apk > SHA256SUMS.txt
gh release create v1.1.0 SdFake-Instalador.exe SdFake-Portatil.exe SdFake.apk SHA256SUMS.txt --title "SdFake 1.1.0" --notes-file notas.md
```

Os links de download do README apontam para `releases/latest/download/<arquivo>`, então os nomes
dos arquivos precisam continuar os mesmos.
