# Bloodborne Randomizer

Randomizador de inimigos e itens do Bloodborne para Linux e Windows, reescrito a partir da lógica do
"Bloodborne Enemy and Item Randomizer" 5.2 da comunidade, com os bugs de estabilidade corrigidos.

## Como usar

1. Abra `publicado/linux-x64/BloodborneRandomizer` (Linux) ou `publicado/win-x64/BloodborneRandomizer.exe` (Windows).
2. Aba **Início**: escolha a pasta `dvdroot_ps4` do mod original (5.2), a pasta de saída e a pasta do jogo.
3. Ajuste as abas **Inimigos** e **Itens** (cada opção tem uma descrição).
4. Aba **Gerar e aplicar**: **1. Randomizar** grava tudo na pasta de saída, com os logs em `logs/`.
   **2. Aplicar no jogo** copia para o jogo (com backup automático).
5. **Unrandomizer** devolve o jogo ao normal. Informando uma cópia limpa do jogo (pasta ou `.zip`),
   ele também desfaz mods aplicados antes deste programa.

**Modo swap** (aba Inimigos → Chefes): os chefes trocam de arena entre si, numa permutação, e
cada chefe aparece uma única vez. Por padrão o chefe novo herda a força do chefe original da arena,
e os ajudantes de lutas em grupo viram estátuas.

O **código de seed** (`BBR1-...`) guarda a seed e todas as opções. Quem colar o mesmo código, com o
mesmo pacote do mod, recebe exatamente o mesmo jogo.

## Linha de comando

```
BbRando.Cli randomize <pasta-do-mod> <pasta-de-saida> [--seed TEXTO | --code BBR1-...] [--config arquivo.json]
BbRando.Cli catalog <pasta-do-mod>
BbRando.Cli verify <pasta-do-mod> <pasta-de-saida>
BbRando.Cli apply <pasta-de-saida> <pasta-do-jogo>
BbRando.Cli unrandomize <pasta-do-jogo> [--vanilla pasta-ou-zip] [--mod pasta-do-mod]
```

## Compilar

Requer o .NET SDK 10. `lib/SoulsFormats.dll` vem do pacote do mod original.

```
dotnet build
dotnet publish src/BbRando.App -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publicado/linux-x64
dotnet publish src/BbRando.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publicado/win-x64
```

## Projeto

- `src/BbRando.Core`: leitura dos arquivos do jogo, randomização, logs, aplicar/desfazer.
- `src/BbRando.App`: interface (Avalonia).
- `src/BbRando.Cli`: linha de comando e verificação.
- `referencia/descompilado`: código do mod original descompilado, só para consulta (não distribuir).

## Créditos

- Mod original "Bloodborne Enemy and Item Randomizer": **Sepukkake** e **Smertin123**.
- SoulsFormats: Joseph Anderson (TKGP).

Repositório privado, para uso pessoal. Não distribuir.
