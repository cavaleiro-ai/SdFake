# Como o SdFake funciona por dentro

Este documento explica os dois testes, o formato dos dados gravados e as proteções. Serve para
quem quer entender, auditar ou mexer no código.

## O que é um dispositivo de capacidade falsa

O controlador do cartão ou pendrive é reprogramado para anunciar um tamanho maior do que a memória
que existe. O sistema operacional formata com o tamanho anunciado e tudo parece normal. O que
acontece com os dados além do limite real depende do tipo de falso:

| Tipo | Comportamento | Como aparece no teste |
| --- | --- | --- |
| **Descarta** | A gravação "dá certo", mas nada é guardado. A leitura devolve zeros ou `0xFF`. | Blocos em branco |
| **Dá a volta** | O endereço é reduzido ao tamanho real e a gravação cai em cima do começo. | Blocos com dados de outra posição |
| **Recusa** | Passa a devolver erro de gravação ou leitura. | Erros de E/S |

O SdFake reconhece os três.

## Os dados de teste

Os dois testes gravam blocos em que **cada bloco é único e carrega a própria identidade**:

- os primeiros 8 bytes são a **chave**: a posição do bloco (no Teste Completo, número do arquivo e
  número do bloco dentro dele);
- os 8 bytes seguintes são a **semente** da sessão, sorteada a cada teste;
- o restante é preenchido por um gerador pseudoaleatório (splitmix64) iniciado a partir da
  semente e da chave.

Na leitura, o bloco esperado é gerado de novo e comparado byte a byte. Quando difere, o conteúdo
lido é classificado:

| Classificação | Significado |
| --- | --- |
| Em branco | Só `0x00` ou `0xFF`: a gravação foi descartada. |
| Sobrescrito | Semente desta sessão, mas chave de **outro** bloco: o endereço deu a volta. |
| Corrompido | Qualquer outra diferença. |
| Ilegível | A leitura devolveu erro. |

Dados aleatórios impedem que um controlador "esperto" comprima ou deduplique o conteúdo para
parecer maior. A semente por sessão impede que sobras de um teste anterior passem por dados válidos.

O formato é idêntico no C# (`pc/src/Core.cs`) e no Java (`android/src/com/sdfake/app/engine/Pattern.java`).

## Teste Completo

Disponível no PC e no Android. Trabalha no nível de arquivos, então não precisa de permissão
especial e não toca nos arquivos que já estão no cartão.

1. Cria a pasta `SdFake_Teste` e grava arquivos `SDFnnnnn.bin` em blocos de 1 MiB. Os tamanhos
   crescem (16, 32, 64, ... 512 MiB, depois 1 GiB cada) para que um falso pequeno seja pego cedo.
2. Depois de cada arquivo, faz uma **conferência por amostragem** do arquivo novo e dos antigos.
   Num falso que "dá a volta", é aqui que os arquivos antigos aparecem destruídos.
3. Na primeira falha, lê **tudo** o que já foi gravado para medir o estrago. Se os dados ruins
   passam de 2 % do que foi escrito, para de gravar: continuar só gastaria tempo.
4. Se não houver falha, quando o espaço acaba faz a **conferência final** de todos os arquivos.
5. Apaga os arquivos de teste, sempre, inclusive em cancelamento ou erro.

**Veredito**

| Veredito | Condição |
| --- | --- |
| VERDADEIRO | Todo o espaço livre foi gravado e conferido sem nenhum erro, e o volume cobre pelo menos 90 % do que o dispositivo anuncia. |
| NÃO CONFIRMADO | Sem erros, mas o volume é bem menor do que o dispositivo anuncia (veja abaixo). |
| FALSO | Os dados perdidos chegam a 2 % do que foi gravado, ou o dispositivo passou a recusar gravações. A capacidade real estimada é o espaço já ocupado antes do teste mais os dados que sobreviveram. |
| COM DEFEITO | Houve perda, mas abaixo de 2 %: trechos isolados ruins num dispositivo de capacidade aparentemente real. |
| TESTE INCOMPLETO | Cancelado antes de dar para concluir. |

**Ler do cartão, não da memória.** Se a leitura viesse do cache do sistema, um cartão falso
passaria no teste. Por isso:

- no Windows, os arquivos são abertos com `FILE_FLAG_NO_BUFFERING`, com buffers alinhados;
- no Android, o app faz `fsync` a cada 32 MiB e pede ao kernel para descartar as páginas em cache
  (`posix_fadvise(POSIX_FADV_DONTNEED)`), por uma biblioteca nativa mínima
  (`android/native/sdfake.c`), já que essa chamada não existe na API Java;
- ainda no Android, se o cache não pôde ser descartado num teste menor que duas vezes a RAM do
  aparelho, ou se a leitura foi rápida demais para ter vindo de um cartão (mais de 2000 MB/s), o
  resultado sai como NÃO CONFIRMADO.

## Teste Rápido

Só no PC. Lê e grava direto no dispositivo físico (`\\.\PhysicalDriveN`), por baixo do sistema de
arquivos, e por isso enxerga o dispositivo **inteiro**, formatado ou não.

1. Escolhe até 512 pontos numa grade regular que cobre do começo ao fim, mais o último trecho do
   dispositivo. Cada ponto tem 4 KiB.
2. **Lê e guarda** o conteúdo original de todos os pontos.
3. **Grava** um bloco único em cada ponto.
4. **Lê de volta** e classifica cada ponto.
5. **Analisa:**
   - pontos sobrescritos por outros indicam que o endereço dá a volta; o programa procura o
     tamanho em que isso acontece;
   - muitos pontos ruins no fim indicam descarte; o programa faz uma busca binária entre o último
     ponto bom e o primeiro ruim para achar o limite, com precisão de 64 KiB.
6. **Restaura** o conteúdo original de todos os pontos e confere a restauração. A ordem é
   escolhida para que, num dispositivo que dá a volta, os pontos bons sejam os últimos a ser
   gravados e fiquem corretos.

O teste completo toma poucos segundos de E/S porque são só alguns megabytes no total. O tempo que
passa disso é do próprio dispositivo.

## Volume menor do que o dispositivo anuncia

Um falso reformatado no tamanho real (por exemplo, partição de 243 MB num dispositivo que anuncia
133 GB) passa em qualquer teste no nível de arquivos, porque a parte formatada existe de verdade.

No PC, o SdFake pergunta ao Windows o tamanho do dispositivo físico
(`IOCTL_DISK_GET_DRIVE_GEOMETRY_EX`, que funciona sem administrador) e compara com o tamanho do
volume. Quando o volume tem menos de 90 % do dispositivo:

- o Teste Completo nunca responde VERDADEIRO: responde NÃO CONFIRMADO;
- o programa avisa antes de começar e oferece o Teste Rápido;
- se o Teste Rápido já rodou na mesma sessão e no mesmo dispositivo, o veredito dele prevalece.

No Android não há como consultar o dispositivo físico. O resultado bom mostra o tamanho conferido
("243 MB REAIS") e entra em alerta abaixo de 1,5 GB.

## Proteções

**PC**

- O disco do Windows e o disco de onde o programa está rodando nunca são alvo do Teste Rápido.
- Discos fixos que não são USB não aparecem na lista. Discos USB externos pedem confirmação extra.
- No Teste Rápido, todos os volumes do dispositivo são travados e desmontados antes da gravação,
  para que o Windows não escreva neles ao mesmo tempo.
- No Teste Completo, a limpeza só apaga arquivos com o nome exato dos arquivos de teste
  (`SDF` + 5 dígitos + `.bin`) dentro de `SdFake_Teste`.
- O instalador é por usuário: grava em `%LOCALAPPDATA%\Programs\SdFake` e na chave de
  desinstalação do usuário atual, sem administrador.

**Android**

- O acesso ao cartão usa o seletor de pastas do sistema (Storage Access Framework). O app não pede
  permissão de armazenamento e só enxerga a pasta que você escolheu.
- A memória interna e provedores que não sejam o armazenamento externo do sistema são recusados.
- O teste roda num serviço em primeiro plano com notificação, para o Android não encerrá-lo com a
  tela apagada.
- O app não declara a permissão de internet.

## O que o SdFake não faz

- Não mede velocidade de forma padronizada (as velocidades mostradas são as do teste, úteis como
  referência).
- Não conserta nem reformata o cartão.
- Não confere áreas já ocupadas por arquivos no Teste Completo.
