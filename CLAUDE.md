# Guia para o Claude — Bloodborne Randomizer

Randomizador de inimigos, chefes e itens do Bloodborne, multiplataforma (Linux/Windows). Foi reescrito a
partir do mod da comunidade "Bloodborne Enemy and Item Randomizer" 5.2 (créditos no README), que era
só para Windows (WPF) e instável. **Repositório privado**: não publique o código nem os binários.

O dono do projeto fala português (pt-BR). Responda e escreva textos de interface e logs em português.
Ele joga com um port nativo para Linux (AppImage `Bloodborne-bbport-x86_64.AppImage`) que lê os
arquivos soltos da pasta `CUSA03173/dvdroot_ps4`.

Desde o marco 1, o programa **não depende mais do mod original**. A base é uma **cópia limpa do jogo**
(pasta ou `.zip`), e os ajustes do mod que o randomizer precisa são gerados ou aplicados a partir de
dados embutidos. O "modo legado" (pasta do mod como base) continua existindo até o usuário validar o
modo novo no jogo.

## Preparar uma máquina nova

1. **.NET SDK 10** (sem sudo):
   ```bash
   curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
   bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
   export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
   ```
   Se o seu shell não carregar o `~/.bashrc`, exporte essas variáveis em cada comando.
   No Windows, use o instalador oficial do .NET 10 SDK.
2. **Pergunte ao usuário** onde ficam, nessa máquina: a cópia limpa do jogo (pasta ou `.zip`), a pasta
   do jogo (CUSA03173) e, só se for usar o modo legado ou regerar os patches, a pasta `5.2/dvdroot_ps4`
   do mod original (Nexus Mods). Não suponha caminhos.
3. `dotnet build` na raiz. Precisa dar **0 avisos**.
4. `dotnet test` (rápido). Com `BBR_VANILLA=<cópia limpa>` roda também os testes com os dados do jogo
   (~2 min).

## Comandos do dia a dia

```bash
dotnet build && dotnet test
dotnet run --project src/BbRando.App           # interface
C=src/BbRando.Cli/bin/Debug/net10.0/BbRando.Cli
# <fonte> = cópia limpa (pasta ou .zip); a pasta do mod (tem "Mod Files") ativa o modo legado
$C randomize <fonte> <saida> --seed teste       # gera tudo + logs/itens.txt e logs/inimigos.txt
$C randomize <fonte> <saida> --config cfg.json  # cfg.json = RandomizerSettings em JSON (só o que muda)
$C randomize <fonte> <saida> --code BBR2-...    # reproduz um código de seed
$C verify <fonte> <saida>                       # relê os mapas gerados; falha se faltar modelo
$C catalog <fonte>                              # inimigos/chefes sorteáveis
$C bosses <fonte> / parts <msb> <filtro> / fields <fonte> <Param> <id>   # inspeção
$C compare-bnd <a.dcx> <b.dcx> [--all]          # compara os arquivos internos de dois pacotes
$C apply <saida> <jogo> / unrandomize <jogo> [--vanilla zip-ou-pasta] [--mod <mod>]
# Desenvolvimento (precisam do mod original e da cópia limpa):
$C analyze-mod <mod> <limpo> referencia/analise-mod          # relatórios do que os autores mudaram
$C export-patches <mod> <limpo> src/BbRando.Core/Data/modpatches.json   # regera os patches embutidos
```

Executáveis autônomos (não vão para o git: o .exe passa do limite de 100 MB do GitHub):
```bash
dotnet publish src/BbRando.App -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publicado/linux-x64
# troque linux-x64 por win-x64 para o Windows
```

## Como testar uma mudança

1. `dotnet build` sem avisos e `dotnet test` (com `BBR_VANILLA` quando mexer na geração).
2. `randomize` numa pasta temporária e depois `verify`.
3. **Determinismo**: gere duas vezes com a mesma seed e compare os md5 de todos os arquivos. Precisam ser
   idênticos. Gere também com `--code` (o código de `seed.txt`) e compare de novo.
4. **Compatibilidade**: um código `BBR1-` (versão anterior) no modo legado tem que gerar exatamente o
   mesmo resultado que a versão anterior gerava (dá para conferir com um `git worktree` do commit antigo).
5. Leia os logs gerados para conferir se o resultado faz sentido.
6. **Nunca** rode `apply`/`unrandomize` na pasta real do jogo sem o usuário pedir. Para testar, crie uma
   pasta falsa `.../CUSA03173/dvdroot_ps4` com alguns arquivos copiados do jogo. O estado do instalador
   fica em `~/.local/share/BloodborneRandomizer/jogo-<hash>/` (backups e manifesto).
7. A interface não tem testes automáticos. Para conferir o visual no Linux/X11: rode o app com
   `XDG_DATA_HOME=<pasta temporária>` (para não mexer nas configurações do usuário), capture com
   `xwd -id <janela>` e converta para PNG com um script Python/PIL que lê o formato XWD.

## Arquitetura

```
src/BbRando.Core/      lógica (sem interface)
  GameSource.cs        IGameSource; VanillaGameSource (pasta ou .zip); registro da codificação Shift-JIS
  ModPack.cs           fonte do modo legado (pasta do mod; prefere "arquivo.bak", que é a base)
  ModPatches.cs        ajustes do mod original guardados como dados (Data/modpatches.json) e a aplicação
  AiScripts.cs         junta os scripts de IA de todos os mapas em cada pacote script/mXX.luabnd
  SfxMerge.cs          junta os efeitos de personagem no pacote sfx comum
  AreaScaling.cs       NpcScaler (cópias de NpcParam) + escalonamento de força dos inimigos
  GameText.cs          textos .fmg (nomes de itens, NPCs, lugares)
  ParamBundle.cs       gameparam + paramdefs; Index() (IDs repetidos: vale o primeiro); CloneRow(); AddRow()
  GameData.cs          áreas, mapas, listas de proteção, arenas de chefe
  MapFile.cs           um .msb carregado + Describe() (área/sublocal/coordenadas); flag Dirty
  Enemies.cs           EnemyCatalog (pools), EnemyRandomizer, ModelTable.EnsureModels
  BossSwap.cs          modo swap (permutação de chefes entre arenas + ajuste de força)
  BossEvents.cs        quais patches de evento entram (só arenas trocadas) + nome na barra de vida
  Items.cs             ItemCatalog (conteúdo dos lotes) e ItemRandomizer (tesouros e drops)
  Shops.cs             ShopRandomizer: lojas (ShopLineupParam) e armas iniciais dos Mensageiros
  Randomizer.cs        pipeline completo + escrita dos logs (SpoilerLog)
  Settings.cs          RandomizerSettings + código de seed BBR2- (lê BBR1-)
  GameInstaller.cs     Aplicar no jogo / Unrandomizer (backups, manifesto, cópia limpa)
  Rng.cs               xoshiro256** com fluxos por etapa
src/BbRando.App/       interface Avalonia 11.3.22 (MVVM simples, sem toolkit)
src/BbRando.Cli/       linha de comando; ModAnalysis.cs = diff mod × jogo limpo e exportação dos patches
tests/BbRando.Tests/   xUnit (CoreTests sempre; GameDataTests com BBR_VANILLA)
lib/SoulsFormats.dll   formatos da FromSoftware (netstandard2.1), veio do pacote do mod
referencia/descompilado/   mod original descompilado com ILSpy (só consulta)
referencia/analise-mod/    relatórios do diff mod × jogo limpo (resumo, params, eventos, pacotes...)
referencia/bb-emedf.json   nomes e argumentos das instruções de evento do BB (projeto DarkScript3)
```

A ordem do pipeline (`Randomizer.Run`) importa:
1. Abrir a fonte; carregar textos, parâmetros e mapas.
2. Modo jogo limpo: aplicar os patches de parâmetros e de mapas (`ModPatchSet.ApplyParams/ApplyMaps`).
3. **Montar o EnemyCatalog e o AreaScaling antes de qualquer troca** (leem os mapas intactos).
4. Embaralhar os drops (antes do swap, para o chefe novo herdar a recompensa já embaralhada).
5. Fazer o swap de chefes.
6. Randomizar os inimigos (excluem os modelos do swap; força ajustada pelo AreaScaling).
7. Embaralhar os tesouros. Depois: armas iniciais e lojas (só mexem no param).
8. Rodar `EnsureModels` em todo mapa com `Dirty`.
9. Gravar os mapas `Dirty` e o param. No modo jogo limpo: eventos e textos com os patches, scripts de
   IA mesclados e efeitos mesclados. No modo legado: copiar os arquivos do pacote do mod.
10. Gravar os logs, o `seed.txt` e o `configuracao.json`.

### Regras que não podem quebrar

- **Determinismo / códigos de seed**: use só `Rng`, nunca `System.Random` nem a ordem de um
  `HashSet`/`Dictionary` para sortear. Cada etapa tem um fluxo próprio (`new Rng(seed, "enemies/" + mapa)`,
  `"items/treasures"`, `"bosses/swap"`...). Renomear um fluxo ou mudar a ordem dos sorteios muda o
  resultado de códigos antigos. O teste `Rng_ValoresConhecidos` trava os valores do gerador.
- **Código de seed** = JSON comprimido só com o que difere do padrão. `BBR2-` é o atual (inclui a fonte).
  `BBR1-` (versão anterior) é lido como modo legado e com `ScaleEnemiesToArea` desligado, se o código não
  disser o contrário. Opção nova com padrão "desligado" mantém os códigos válidos. **Mudar o padrão de
  uma opção existente muda o significado dos códigos já compartilhados.**
- **Faixas de IDs novos de NpcParam** (contando para baixo): 899999999 = swap de chefes,
  879999999 = escalonamento de inimigos. O mod original usava 900000000+ (para cima).
- Coordenadas nos logs usam `CultureInfo.InvariantCulture` (o sistema está em pt-BR, com vírgula).
- A pasta de saída só é limpa se estiver vazia ou tiver o marcador `.bloodborne-randomizer-output`.
- O Unrandomizer nunca apaga: o que não existe no jogo original vai para `removidos/`. Com a cópia
  limpa, ele também tira os restos do mod antigo (`Mod Files/`, `*.bak`).

## O que o mod original mudava (resultado do diff, `referencia/analise-mod/`)

44 arquivos alterados + 1 novo em relação ao jogo limpo:

| Mudança dos autores | Como fica aqui |
|---|---|
| `script/mXX.luabnd`: união dos scripts de IA de todos os mapas (+ m29) em cada pacote, 435 arquivos | `AiScripts` gera a mesma união (435, conferido) e mescla LUAINFO/LUAGNL **sem duplicatas** (os autores duplicavam: 1 KB → 700 KB) |
| Scripts novos/editados: `551111_battle.lua` (Wet Nurse sem escuridão), `511000_battle.lua` e 4 IAs de cálice | patches (`LuaFiles` + `Blobs`) |
| `sfx/commoneffects`: efeitos de personagem de todas as áreas (3115 → 4606 arquivos) | `SfxMerge`: .fxr de ID < 10000 ou 600000–699999 + texturas (4982 arquivos, 45 MB; o mod tem 42 MB) |
| `gameparam`: 25334 linhas NpcParam 900000000+ = 30 variantes de força por inimigo (só muda `spEffectID6`, níveis 7001…7487); `NPCScalingFile` lista base + variantes | `AreaScaling` (cópias sob demanda) |
| `gameparam`: `spEffectID6` das linhas originais de 293 inimigos trocado para 7487 (força máxima) | **não reproduzido** (era efeito colateral do remapeamento) |
| `gameparam`: `getSoul` de 28 chefes (no jogo os ecos vêm por evento), NpcThinkParam 551111 novo e 250100 alterado, SpEffect 999999 "BOSS HP", material do 511000 | patches (`ParamRows`/`ParamFields`) |
| `gameparam`: SpEffect 2010 (buff da lanterna), lote 101552150 (recompensa de Loran), ruído de float do editor | **não reproduzido** |
| `event/*.emevd`: 108 eventos em 16 arquivos. Quase tudo **remove comandos com script do chefe original** (Force Animation Playback, Request Character AI Command/Re-plan, Create NPC Part...) para chefes trocados não travarem; eventos novos em `common` (agarrão do Moon Presence) e em `m35` (91919191–94: as Living Failures morrem pela vida do controlador e a luta acaba quando a principal morre) | patches (`Events`: o evento inteiro do mod substitui o do jogo) |
| Mapas: m35 sem 13 inimigos + modelo `l005510` (memória); chefes de cálice reposicionados em `m29_10_90_01` e `m29_31_90_00` | patches (`Maps`) |
| Texto "Loran Cleric" (NPC 750000) | patches (`Texts`) |
| `chr/c5110`, `chr/c2560`, `obj/o919900`, `drawparam m29`, textos do "Great One Beast" | **não reproduzido** (o randomizer não usa; GOB é outro mod) |
| `Names.txt`/`Sizes.txt` | peso = tamanho do `chr/cNNNN.chrbnd.dcx` (idêntico) |

No modo jogo limpo, `BossEvents` só aplica os patches de evento das **arenas que mudaram de chefe**
(arena que ficou igual mantém o evento original, com animação de entrada). Como decide:
- cada evento é ligado às arenas pelas entidades que cita (fases, ajudantes e `Extras` de `BossArena`);
  eventos do mesmo arquivo que citam as mesmas entidades de 7 dígitos (ex.: geradores 35038xx das Living
  Failures) são decididos juntos; evento sem arena entra se alguma arena daquele arquivo mudou
  (`common`: se alguma arena mudou);
- construtores (evento 0/50): o mod só acrescenta `Initialize Event` (2000[0]) e `Set Event Flag`
  (2003[2]); cada linha segue o evento iniciado, ou o evento com o mesmo número da flag, ou o mapa do
  número (1 AA B xxxx → mAA_0B). As flags 1AAB1802/1702 ligadas no m24_01 têm o número dos eventos de
  entrada dos chefes (parecem marcar a entrada como já vista);
- arquivo que fica igual ao do jogo não é gravado (sem swap não sai nenhum `.emevd`).
Com todas as arenas trocadas o resultado é igual ao mod (teste). O modo legado continua copiando tudo.

## Conhecimento sobre os dados

**SoulsFormats**
- Os nomes de arquivos dentro dos BND usam `\`, que o `Path` do Linux não separa. Use
  `ParamBundle.BinderName()` ou corte no último `\`/`/`.
- `PARAM.Row.ID` é `long`. `ItemLotParam` tem IDs repetidos (use `Index()`, o primeiro vale).
  `PARAM.Write()` **não ordena** as linhas e o jogo faz busca binária, então use `CloneRow()`/`AddRow()`.
- `MSBB.Model.Enemy(template)` copia uma entrada de modelo; toda parte de inimigo precisa ter o modelo
  na tabela do mapa (`EnsureModels`).
- `EMEVD`: `Events[].Instructions` (Bank, ID, ArgData). Para ler os argumentos, use
  `referencia/bb-emedf.json` (tipos 0 u8, 1 u16, 2 u32, 3 s8, 4 s16, 5 s32, 6 f32, alinhados).
  Instruções úteis: 2003[11] Display Boss Health Bar, 2003[12] Handle Boss Defeat, 2004[08] Set SpEffect.
- Em `GameData`, campos estáticos que usam outros (`.. BossNames`) precisam vir **depois** deles no arquivo.

**Mapas**
- Variantes do mesmo lugar em momentos diferentes da história: `m23_00_00_00/01`, `m24_00_00_00/01`,
  `m24_01_00_00/01/11`, `m24_02_00_00/01`, `m27_00_00_00/01`, `m28_00_00_00/01`, `m32_00_00_00/01`.
  Itens e chefes ficam iguais em todas as variantes; inimigos comuns ainda são sorteados em cada uma.
- Masmorras de cálice: o sorteio usa os inimigos do `m29_XX_90_00` e a troca acontece no `_01`
  (exceto `m29_21_90_00` e `m29_31_90_00`). Os modelos extras estão em `m29_52_01_00/m29_52_01_91`.
- Sublocal: `Object.CollisionName`/`Enemy.CollisionName` → `Collision.MapNameID` → FMG de lugares.
  Tesouro: `PartName2`, depois `PartName`, depois a região `PointName`.

**Textos (.fmg)** — `item.msgbnd`: 10 itens, 11 armas, 12 armaduras, 13 acessórios, 18 nomes de NPC (via
`NpcParam.nameId`), 19 lugares. `menu.msgbnd`: 31 gemas. Inimigos comuns quase nunca têm nome no jogo.

**Itens** — `ItemLotParam.lotItemCategory0X`: 0 arma, 1 armadura, 2 acessório, 4 item, 8 Blood Gem.
`EquipParamGoods.goodsType`: 1 = item-chave, 6 = cálice.

**Lojas (ShopLineupParam)** — centena de milhar do ID = loja (1 Mensageiros, 2 insight, 5 e 6 lojas
secundárias com consumíveis/Uncanny/Lost/cálices Sinister); dezena de milhar = faixa de preço (1: 0–4;
5 e 6: 1–4). Todas as faixas têm os mesmos itens e flags, só o `value` muda. Campos: `equipId`, `value`,
`eventFlag` (marca "já comprado" dos itens únicos), `qwcId` (desbloqueio: insígnia), `sellQuantity`
(-1 = sem limite), `shopType` (0 ecos, 5 insight), `equipType` (0 arma, 1 armadura, 3 item). Linhas
2000–2002/2010–2011 = armas grátis do começo; `1` e `9000xx` são de teste. **CharaInitParam não define arma
inicial no BB.** `EquipParamWeapon`: `right/leftHandEquipable`; requisitos em `properStrength/Agility/
Magic/Faith` (For/Hab/Sangue/Arcano); cada nível +n é uma linha (ID + 100·n); Uncanny/Lost = +10000/+20000.

**Força (escalonamento)** — `NpcParam.spEffectID6` com SpEffect 7000–7499 multiplica vida/dano/defesa
(70xx jogo base, 748x DLC, 73xx cálice); `GameClearSpEffectID` é o de NG+. Inimigo novo herda os dois do
inimigo que **estava naquele lugar**; reserva: o mais comum entre os nativos do mapa (tabela no log).
Por mapa não basta: a Cathedral Ward mistura regiões de começo e fim de jogo.

**Chefes**
- No swap, o chefe novo recebe uma cópia da sua linha de NpcParam com `hp`, `getSoul`, `spEffectID6`,
  `GameClearSpEffectID` e os lotes de drop do chefe original da arena.
- Arenas em `GameData.BossArenas` (`Phases` = partes do chefe na ordem, `Helpers` = ajudantes que viram
  estátua, `Banned` = modelos proibidos). Gascoigne c2710/c2720, Ludwig c4510_0000/0002, Orphan
  c4540/c4541, Maria c4520_0002/0000, Emissary c2500_0000/c2570_0001.
- **c5510 é a Mergo's Wet Nurse** (não o Micolash; c5510_0001/0002 são os clones dela). Micolash usa
  modelo humano c0000 e não entra no swap. Wet Nurse fora da arena: IA 551000 → 551111 ("sem
  escuridão", criada pelo mod e incluída nos patches).
- Living Failures entra no swap (grupo) graças aos eventos m35 do mod; os ajudantes 0001–0003 viram estátua.
- Estátua: NpcParam 252100, IA 252100, modelo c2521 (2 de vida).
- Barra de vida = instrução 2003[11] (on, entidade, slot, nome) com valores diretos no jogo base (nas
  masmorras de cálice vem por parâmetro: não tratado). O nome **não** é sempre o `nameId` da NpcParam
  (Gehrman 804000, Shadows 212010/20/30, Wet Nurse 0): `BossEvents` usa o nome que a barra do chefe novo
  tem na arena dele, por fase. Barras em ajudantes que viraram estátua são desligadas; na Wet Nurse a única
  barra está no clone c5510_0002 e passa para c5510_0000. One Reborn: barra na parte invisível
  `c1050_0120`; Living Failures: no controlador `c4030_0004` (os dois em `Extras`).

## Bugs do mod original (já corrigidos aqui; não reintroduzir)

- Copiava os modelos de inimigo de todos os mapas para todos (Central Yharnam: 33 → 1018 entradas).
- O filtro de modelos proibidos (Upper Cathedral Ward, Research Hall) sorteava de novo sem checar a
  memória, e modelo sem tamanho conhecido contava como peso 0.
- Índice da lista principal usado na lista de cálice. Laço de "Oops All" com o tamanho da lista errada.
- "Excluir inimigos" só funcionava com cálice ligado. `m21_01` entrava duas vezes nos itens.
  `"5090"` sem o `c`. A IA 551111 era calculada e nunca aplicada.
- `new Random()` em todo lugar e campo de seed ignorado. Caminhos fixos com `\`.
- Força por área num nível fixo por mapa e linhas originais de inimigos alteradas para força máxima.

## Roteiro (plano aprovado pelo usuário)

**Marco 1 — concluído:** fonte "jogo limpo"; análise do mod (`analyze-mod`); patches embutidos; IA e
efeitos gerados; escalonamento por lugar; Wet Nurse e Living Failures; códigos BBR2; testes.
**Falta o usuário testar o modo jogo limpo no jogo.**

Próximos marcos (cada um termina com teste no jogo pelo usuário):
1. **Paridade e limpeza**: depois do teste, decidir se o modo legado sai. Patches de evento só nas arenas
   trocadas — **feito, falta teste no jogo**.
2. **Lojas e armas iniciais** (`Shops.cs`) — **feito, falta teste no jogo**: permutação por loja e tipo,
   preço/limite/flag de compra vão com o item, desbloqueio fica no lugar, mesmo mapeamento em todas as
   faixas; armas iniciais sorteadas por mão (direita/esquerda) com requisitos baixados para os da arma
   original. Fluxos `shops/lineup` e `shops/starting`. Log em `itens.txt`.
3. **Nome certo na barra de vida** — **feito (modo jogo limpo), falta teste no jogo**. Cálices ainda não.
4. **Opções caóticas** (aba "Caos", cada uma com o seu fluxo de `Rng`): NPCs (`RandomizeNPCs`), golpes
   de armas (`RandomizeMelee/GunMoveset`), inimigos brigando entre si (`TeamTypeRando`), sons de IA
   (`AiSoundParamRandomizer`), rostos (`VfxRandomizer` FaceGen/Face), sangue (`DecalRandomizer`), falas
   (`TalkRandomizer`), gemas (`GemGenParamRandomizer`, 3/6 efeitos), projéteis (`BulletRandomizer`),
   escuridão permanente (`PermaDarknessFunction`), armaduras. Revisar cada uma no descompilado.
5. **Itens-chave com lógica** (`KeyLogic.cs`): tabela de requisitos por tesouro (chaves, chefes, áreas),
   preenchimento "assumed fill" que garante zerar, e validador `check-logic` na CLI. Só os itens-chave que
   estão em tesouros.
6. **Contínuo**: inimigos iguais entre variantes do mesmo mapa; teste no Windows; releases privadas com
   `gh release create` (os executáveis não cabem no git).

Funções do original no descompilado (`referencia/descompilado/MSB_Test/MainWindow.xaml.cs`): lojas
`RandomizeShopItems` ~4055, armas iniciais ~4051, NPCs ~4544–4656, golpes ~6453/~6541, escuridão
~6629, times ~6815, chefes extras `AddTheRest` ~5197, Orphan fase 1 ~4971, modo fácil `EasyModes`
~5080, sons de IA ~8669, sangue ~9837, falas ~9905, projéteis ~9973, rostos ~10053, gemas ~10123.

## Interface (convenções)

- Todo texto em pt-BR. Toda opção tem uma descrição curta embaixo (`TextBlock Classes="desc"`); avisos
  usam `Classes="warn"`.
- Propriedades do `MainViewModel` envolvem `Settings` e chamam `Changed()` para atualizar o código de
  seed. Opção nova = propriedade em `RandomizerSettings` + propriedade no ViewModel + controle no XAML.
- Configurações da interface: `~/.local/share/BloodborneRandomizer/interface.json` (Windows:
  `%LOCALAPPDATA%\BloodborneRandomizer`).

## Créditos

O mod original é de **Sepukkake** e **Smertin123** (os patches em `Data/modpatches.json` são o trabalho
deles, guardado como dados). SoulsFormats: Joseph Anderson (TKGP). Tabela de instruções de evento:
projeto DarkScript3.
