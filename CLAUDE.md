# Guia para o Claude — Bloodborne Randomizer

Randomizador de inimigos, chefes e itens do Bloodborne, multiplataforma (Linux/Windows). Foi reescrito a
partir do mod da comunidade "Bloodborne Enemy and Item Randomizer" 5.2 (créditos no README), que era
só para Windows (WPF) e instável. **Repositório privado**: não publique o código nem os binários.

O dono do projeto fala português (pt-BR). Responda e escreva textos de interface e logs em português.
Ele joga com um port nativo para Linux (AppImage `Bloodborne-bbport-x86_64.AppImage`) que lê os
arquivos soltos da pasta `CUSA03173/dvdroot_ps4`.

## Preparar uma máquina nova

1. **.NET SDK 10** (sem sudo):
   ```bash
   curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
   bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
   export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
   ```
   Se o seu shell não carregar o `~/.bashrc`, exporte essas variáveis em cada comando.
   No Windows, use o instalador oficial do .NET 10 SDK.
2. **Pacote do mod original** (não está no repositório): baixe o "Bloodborne Enemy and Item Randomizer"
   5.2 no Nexus Mods, extraia, e use a pasta `5.2/dvdroot_ps4` como "pasta do mod". Os arquivos `.bak`
   dentro dela fazem parte do pacote e são a base. Não os apague.
3. **Pergunte ao usuário** onde ficam, nessa máquina: o pacote do mod, a pasta do jogo (CUSA03173) e a
   cópia limpa do jogo (pasta ou `.zip`, usada pelo Unrandomizer). Não suponha caminhos.
4. `dotnet build` na raiz. Se compilar sem avisos, está pronto.

## Comandos do dia a dia

```bash
dotnet build                                   # tudo (deve dar 0 avisos)
dotnet run --project src/BbRando.App           # interface
C=src/BbRando.Cli/bin/Debug/net10.0/BbRando.Cli
$C randomize <mod> <saida> --seed teste        # gera tudo + logs/itens.txt e logs/inimigos.txt
$C randomize <mod> <saida> --config cfg.json   # cfg.json = RandomizerSettings em JSON (só o que muda)
$C randomize <mod> <saida> --code BBR1-...     # reproduz um código de seed
$C verify <mod> <saida>                        # relê os mapas gerados; falha se faltar modelo
$C catalog <mod>                               # lista de inimigos/chefes sorteáveis
$C bosses <mod>                                # todas as partes de chefe nos mapas (nomes, vida, almas)
$C parts <arquivo.msb.dcx> <filtro>            # partes de inimigo de um mapa
$C fields <mod> <Param> <id>                   # todos os campos de uma linha de parâmetro
$C apply <saida> <jogo> / unrandomize <jogo> [--vanilla zip-ou-pasta] [--mod <mod>]
```

Executáveis autônomos (não vão para o git: o .exe passa do limite de 100 MB do GitHub):
```bash
dotnet publish src/BbRando.App -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publicado/linux-x64
# troque linux-x64 por win-x64 para o Windows
```

## Como testar uma mudança

1. `dotnet build` sem avisos.
2. `randomize` numa pasta temporária e depois `verify`.
3. **Determinismo**: gere duas vezes com a mesma seed e compare os md5 de todos os arquivos de
   `dvdroot_ps4/` e `logs/`. Precisam ser idênticos. Gere também com `--code` (o código de `seed.txt`)
   e compare de novo.
4. Leia os logs gerados para conferir se o resultado faz sentido.
5. **Nunca** rode `apply`/`unrandomize` na pasta real do jogo sem o usuário pedir. Para testar, crie
   uma pasta falsa `.../CUSA03173/dvdroot_ps4` com alguns arquivos copiados do jogo. O estado do
   instalador fica em `~/.local/share/BloodborneRandomizer/jogo-<hash>/` (backups e manifesto).
6. A interface não tem testes automáticos. Para conferir o visual no Linux/X11 dá para capturar a
   janela com `xwd -id <janela>` e converter para PNG com um script Python/PIL que lê o formato XWD.

## Arquitetura

```
src/BbRando.Core/      lógica (sem interface)
  ModPack.cs           lê o pacote do mod; prefere "arquivo.bak" (a base) a "arquivo"
  GameText.cs          textos .fmg (nomes de itens, NPCs, lugares)
  ParamBundle.cs       gameparam + paramdefs; Index() (IDs repetidos: vale o primeiro); CloneRow()
  GameData.cs          áreas, mapas, listas de proteção, arenas de chefe (dados do jogo)
  MapFile.cs           um .msb carregado + Describe() (área/sublocal/coordenadas); flag Dirty
  Enemies.cs           EnemyCatalog (pools), EnemyRandomizer, ModelTable.EnsureModels
  BossSwap.cs          modo swap (permutação de chefes entre arenas + ajuste de força)
  Items.cs             ItemCatalog (conteúdo dos lotes) e ItemRandomizer (tesouros e drops)
  Randomizer.cs        pipeline completo + escrita dos logs (SpoilerLog)
  Settings.cs          RandomizerSettings + código de seed BBR1-
  GameInstaller.cs     Aplicar no jogo / Unrandomizer (backups, manifesto, cópia limpa .zip/pasta)
  Rng.cs               xoshiro256** com fluxos por etapa
src/BbRando.App/       interface Avalonia 11.3.22 (MVVM simples, sem toolkit)
src/BbRando.Cli/       linha de comando e ferramentas de inspeção
lib/SoulsFormats.dll   biblioteca de formatos da FromSoftware (netstandard2.1), veio do pacote do mod
referencia/descompilado/  código do mod original descompilado com ILSpy (usar só como consulta)
```

A ordem do pipeline (`Randomizer.Run`) importa:
1. Carregar textos, parâmetros e mapas.
2. **Montar o EnemyCatalog antes de qualquer troca** (as listas vêm dos mapas intactos).
3. Embaralhar os drops (antes do swap, para o chefe novo herdar a recompensa já embaralhada).
4. Fazer o swap de chefes.
5. Randomizar os inimigos (eles excluem os modelos dos chefes do swap).
6. Embaralhar os tesouros.
7. Rodar `EnsureModels` em todo mapa com `Dirty`.
8. Copiar os arquivos do pacote e gravar só os mapas `Dirty`, o param e os logs.

### Regras que não podem quebrar

- **Determinismo / códigos de seed**: use só `Rng`, nunca `System.Random` nem a ordem de um
  `HashSet`/`Dictionary` para sortear. Cada etapa tem um fluxo próprio (`new Rng(seed, "enemies/" + mapa)`,
  `"items/treasures"`, `"bosses/swap"`...). Renomear um fluxo ou mudar a ordem dos sorteios muda o
  resultado de códigos antigos.
- **Código BBR1-** = JSON comprimido só com o que difere do padrão. Opção nova com padrão "desligado"
  mantém os códigos antigos válidos. **Mudar o padrão de uma opção existente muda o significado dos
  códigos já compartilhados.**
- Os textos dos logs usam `CultureInfo.InvariantCulture` nas coordenadas (o sistema está em pt-BR, com
  vírgula decimal).
- A pasta de saída só é limpa se estiver vazia ou tiver o marcador `.bloodborne-randomizer-output`.
- O Unrandomizer nunca apaga: arquivos que não existem no jogo original vão para `removidos/`.

## Conhecimento sobre os dados (aprendido na engenharia reversa)

**Pacote do mod**
- Os `.bak` dos mapas são idênticos ao jogo original. Os eventos (`event/*.emevd.dcx`) e o
  `gameparam` do pacote **já vêm modificados pelo autor** e são necessários. Por isso a base é sempre o
  pacote do mod, e não o jogo limpo.
- `Mod Files/NPC Scaling File/Names.txt` + `Sizes.txt` dão o "peso" em memória de cada modelo (cNNNN),
  usado nos limites de memória. `NPCScalingFile.txt`/`NPCParamsFile.txt` eram usados por uma função de
  escalonamento que nunca era chamada no original (código morto).

**SoulsFormats**
- Os nomes de arquivos dentro dos BND usam `\`, que o `Path` do Linux não separa. Use
  `ParamBundle.BinderName()`.
- `PARAM.Row.ID` é `long`. `ItemLotParam` tem IDs repetidos (use `Index()`, o primeiro vale).
  `PARAM.Write()` **não ordena** as linhas e o jogo faz busca binária, então use `CloneRow()`, que
  insere na posição certa.
- `MSBB.Model.Enemy(template)` copia uma entrada de modelo. Toda parte de inimigo precisa ter o modelo
  na tabela do mapa (`EnsureModels`).
- Em `GameData`, campos estáticos que usam outros (`.. BossNames`) precisam vir **depois** deles no
  arquivo (ordem de inicialização estática).

**Mapas**
- Variantes do mesmo lugar em momentos diferentes da história: `m23_00_00_00/01`, `m24_00_00_00/01`,
  `m24_01_00_00/01/11`, `m24_02_00_00/01`, `m27_00_00_00/01`, `m28_00_00_00/01`, `m32_00_00_00/01`.
  Itens e chefes precisam ficar iguais em todas as variantes (já tratado). Os inimigos comuns ainda
  são sorteados de forma independente em cada variante (possível melhoria).
- Masmorras de cálice: o sorteio usa os inimigos do `m29_XX_90_00` e a troca acontece no `_01`
  (exceto `m29_21_90_00` e `m29_31_90_00`). Os modelos extras estão em `m29_52_01_00/m29_52_01_91`.
- Sublocal de uma parte: `Object.CollisionName`/`Enemy.CollisionName` → parte `Collision` →
  `MapNameID` → FMG de lugares. Tesouro: `PartName2`, depois `PartName`, depois a região `PointName`.

**Textos (.fmg)** — `item.msgbnd`: 10 itens, 11 armas, 12 armaduras, 13 acessórios, 18 nomes de NPC
(via `NpcParam.nameId`), 19 lugares. `menu.msgbnd`: 31 gemas. Inimigos comuns quase nunca têm nome
no jogo; a interface mostra "Inimigo cNNNN" com as áreas onde ele aparece.

**Itens** — `ItemLotParam.lotItemCategory0X`: 0 arma, 1 armadura, 2 acessório, 4 item, 8 Blood Gem
(gerada). `EquipParamGoods.goodsType`: 1 = item-chave (chaves, emblemas, insígnias), 6 = cálice.
Os tesouros com o mesmo lote em variantes diferentes são agrupados antes do embaralhamento.

**Chefes**
- O escalonamento por área fica em `NpcParam.spEffectID6` (SpEffects 70xx no jogo base, 748x no DLC;
  multiplicam vida/dano/defesa) e em `GameClearSpEffectID` (NG+). No swap, o chefe novo recebe uma
  **cópia** da sua linha de NpcParam (IDs a partir de 899999999, contando para baixo) com `hp`,
  `getSoul`, `spEffectID6`, `GameClearSpEffectID` e os lotes de drop do chefe original da arena.
- As arenas estão em `GameData.BossArenas`: `Phases` são as partes do chefe (Gascoigne
  c2710/c2720, Ludwig c4510_0000/0002, Orphan c4540/c4541, Micolash c5510_0000/1/2, Maria
  c4520_0002/0000, Emissary c2500_0000/c2570_0001). A fase i recebe a fase i do chefe novo (ou a
  última que ele tiver). `Banned`: modelos que travavam naquela arena no original.
- Estátua inofensiva: NpcParam 252100, IA 252100, modelo c2521 (2 de vida). Substitui os ajudantes das
  lutas em grupo.
- IAs especiais ao mudar de arena: Micolash 551000 → IA 551111; 507200 → IA 507200.
- **Living Failures** fica de fora: a vida é compartilhada pelo controlador `c4030_0004` (IA 1), então
  outro chefe ali pode deixar a luta sem fim. One Reborn tem IA 0 (controlado por script).
- A barra de vida mostra o nome definido nos scripts de evento (emevd), então pode continuar com o
  nome do chefe original. Para corrigir seria preciso editar os eventos (ainda não feito).

## Bugs do mod original (já corrigidos aqui; não reintroduzir)

- Copiava os modelos de inimigo de todos os mapas para todos (Central Yharnam: 33 → 1018 entradas).
- O filtro de modelos proibidos (Upper Cathedral Ward, Research Hall) sorteava de novo sem checar a
  memória, e modelo sem tamanho conhecido contava como peso 0.
- Índice da lista principal usado na lista de cálice. Laço de "Oops All" com o tamanho da lista errada.
- "Excluir inimigos" só funcionava com cálice ligado. `m21_01` entrava duas vezes nos itens.
  `"5090"` sem o `c`. A IA 551111 era calculada e nunca aplicada.
- `new Random()` em todo lugar e campo de seed ignorado. Caminhos fixos com `\`.

## O que ainda não foi portado

As funções estão em `referencia/descompilado/MSB_Test/MainWindow.xaml.cs` (linhas aproximadas):

| Recurso | Função original |
|---|---|
| Lojas | `RandomizeShopItems` (~4055), `ShopLineupParam` |
| Armas iniciais | `RandomizeStartingWeapons` (~4051) |
| NPCs | `GenerateNPCList` / `RandomizeNPCs` (~4544–4656) |
| Golpes de armas | `RandomizeMeleeMoveset` (~6453) / `RandomizeGunMoveset` (~6541) |
| Escuridão permanente | `PermaDarknessFunction` (~6629), edita `event/common.emevd.dcx` |
| Times dos inimigos | `TeamTypeRando` (~6815), `NpcParam.teamType` |
| Chefes extras / Orphan fase 1 | `AddTheRest` (~5197), `AddOrphanPhaseOne` (~4971) |
| Modo fácil das lutas em grupo | `EasyModes` (~5080), já reaproveitado como `StatueHelpers` |

Também seria bom: nomes dos chefes na barra de vida (emevd) e inimigos comuns iguais entre as
variantes do mesmo mapa.

## Interface (convenções)

- Todo texto em pt-BR. Toda opção tem uma descrição curta embaixo (`TextBlock Classes="desc"`) e os
  avisos usam `Classes="warn"`.
- Propriedades do `MainViewModel` envolvem `Settings` e chamam `Changed()` para atualizar o código de
  seed. Opção nova = propriedade em `RandomizerSettings` + propriedade no ViewModel + controle no XAML.
- Configurações da interface: `~/.local/share/BloodborneRandomizer/interface.json` (Windows:
  `%LOCALAPPDATA%\BloodborneRandomizer`).

## Créditos

O mod original é de **Sepukkake** e **Smertin123**. A SoulsFormats é de Joseph Anderson (TKGP).
