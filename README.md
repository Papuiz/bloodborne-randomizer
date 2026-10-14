# Bloodborne Randomizer

Randomizador de inimigos, chefes e itens do Bloodborne para Linux e Windows, reescrito a partir da
lógica do "Bloodborne Enemy and Item Randomizer" 5.2 da comunidade, com os bugs de estabilidade
corrigidos. Não precisa mais do mod original: basta uma cópia limpa do jogo (pasta ou `.zip`).

## Como usar

1. Abra `publicado/linux-x64/BloodborneRandomizer` (Linux) ou `publicado/win-x64/BloodborneRandomizer.exe` (Windows).
2. Aba **Início**: escolha a cópia limpa do jogo (pasta ou `.zip`), a pasta de saída e a pasta do jogo.
   O modo legado (pasta `dvdroot_ps4` do mod 5.2 como base) fica num painel recolhido.
3. Ajuste as abas **Inimigos** e **Itens** (cada opção tem uma descrição).
4. Aba **Gerar e aplicar**: **1. Randomizar** grava tudo na pasta de saída, com os logs em `logs/`.
   **2. Aplicar no jogo** copia para o jogo (com backup automático).
5. **Unrandomizer** devolve o jogo ao normal a partir da cópia limpa. Ele também remove os restos do
   randomizer antigo (`Mod Files`, `.bak`), movendo-os para uma pasta de "removidos".

**Modo swap** (aba Inimigos → Chefes): os chefes trocam de arena entre si, numa permutação, e
cada chefe aparece uma única vez. Por padrão o chefe novo herda a força do chefe original da arena,
e os ajudantes de lutas em grupo viram estátuas.

Os inimigos levados para outra área recebem a força do inimigo que estava naquele lugar.

O **código de seed** (`BBR2-...`) guarda a seed e todas as opções. Quem colar o mesmo código recebe
exatamente o mesmo jogo. Os códigos `BBR1-` da versão anterior continuam funcionando no modo legado.

## Linha de comando

```
BbRando.Cli randomize <copia-limpa> <pasta-de-saida> [--seed TEXTO | --code BBR2-...] [--config arquivo.json]
BbRando.Cli catalog <copia-limpa>
BbRando.Cli verify <copia-limpa> <pasta-de-saida>
BbRando.Cli apply <pasta-de-saida> <pasta-do-jogo>
BbRando.Cli unrandomize <pasta-do-jogo> [--vanilla pasta-ou-zip] [--mod pasta-do-mod]
```

## Compilar

Requer o .NET SDK 10. `lib/SoulsFormats.dll` vem do pacote do mod original. Testes: `dotnet test`
(com `BBR_VANILLA=<cópia limpa>` roda também os que usam os dados do jogo).

```
dotnet build
dotnet publish src/BbRando.App -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publicado/linux-x64
dotnet publish src/BbRando.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publicado/win-x64
```

## Projeto

- `src/BbRando.Core`: leitura dos arquivos do jogo, randomização, logs, aplicar/desfazer.
- `src/BbRando.App`: interface (Avalonia).
- `src/BbRando.Cli`: linha de comando, verificação e análise do mod original.
- `tests/BbRando.Tests`: testes automatizados.
- `referencia/descompilado`: código do mod original descompilado, só para consulta (não distribuir).

## Créditos

- Mod original "Bloodborne Enemy and Item Randomizer": **Sepukkake** e **Smertin123**.
- SoulsFormats: Joseph Anderson (TKGP).
- Tabela de instruções de evento (`referencia/bb-emedf.json`): projeto DarkScript3.

Repositório privado, para uso pessoal. Não distribuir.
