# Referências de fidelidade visual

Capturas da interface WinForms atual, usadas na Fase 5 do [ROADMAP](../../ROADMAP.md) como modelo para a nova interface em Avalonia. Cada tela nova deve ser comparada com a imagem de mesmo nome em `reference/`.

## `reference/`

| | |
|---|---|
| **Versão** | `cross-platform` no commit `c3fbce9` (Fase 3): a v1.4.1 original (`baseline-winforms`) mais as correções do gráfico de pizza, do `DirectoryScanner`, do carregamento do histórico de varreduras e das cores do Treemap/Sunburst. Nas Configurações, a opção "c²flux Scan" foi removida (Fase 3.4); o resto da interface é o original. A interface é idêntica à original; a única diferença visível é que o gráfico de pizza deixou de quebrar |
| **Onde** | Runner `windows-latest` do GitHub, workflow [`ui-screenshots.yml`](../../.github/workflows/ui-screenshots.yml), [execução de 09/10/2026](https://github.com/SamuelTM/c2flux/actions/runs/38019714371) |
| **Tela** | 1920×1080, DPI 96 (escala 100%), tema escuro, idioma inglês |
| **Dados** | `T:\` é um disco virtual NTFS de 4 GiB com a árvore de teste (`tests/fixtures/generate_test_tree.py --profile medium`). Ele é varrido duas vezes; entre as varreduras, a ferramenta adiciona um arquivo de 8 MB, apaga um e aumenta outro em 4 MB. "Salvar histórico de varreduras" e "detalhes do histórico de armazenamento" ficam ligados |
| **Detalhes** | `reference/index.json` lista cada captura, tamanho e método |

### Janela principal (1280×800)

| Arquivo | Tela |
|---|---|
| `main-empty.png` | Ao abrir, sem varredura |
| `main-table.png`, `main-pie.png`, `main-bar.png`, `main-sunburst.png`, `main-treemap.png` | Após varrer `T:\`, nas cinco visualizações |
| `main-analysis.png`, `main-analysis-file-types.png`, `main-analysis-largest-files.png`, `main-analysis-redundancies.png` | Painel Análise, nas quatro abas |
| `main-storage-history.png` | Painel Histórico de armazenamento |
| `menu-file.png`, `menu-view.png`, `menu-tools.png`, `menu-help.png` | Menus abertos sobre a janela |
| `context-menu-tree.png` | Menu de contexto da árvore (o menu próprio do app; no Windows o app normalmente usa o menu nativo do Explorer) |
| `context-menu-toolbar.png` | Menu de contexto da barra de ferramentas |

### Outras janelas

| Arquivo | Tela |
|---|---|
| `search.png`, `search-results.png` | Busca vazia e com resultados para "file-00" |
| `settings.png`, `settings-layout.png`, `settings-statistics.png`, `settings-export.png`, `settings-logging.png` | Configurações: abas General, UI, History, Export e Logging |
| `about.png` | Sobre |
| `alert-history.png`, `alert-history-entries.png` | Histórico de alertas vazio e com três exemplos (informação, aviso, erro) |
| `scan-history.png` e `scan-history-*.png` | Histórico de varreduras comparando as duas varreduras, nas sete abas |
| `storage-history.png`, `storage-history-details.png` | Histórico de armazenamento com dois registros e a janela de detalhes |
| `update-available.png` | Aviso de nova versão (dados de exemplo) |
| `dialog-warning-ok.png`, `dialog-warning-yes-no.png`, `dialog-elevation-prompt.png` | Caixas de diálogo de `AppDialogs` |
| `database-move.png`, `debug-class.png` | Diálogos auxiliares |

**Cores do Treemap e do Sunburst:** até a Fase 1, elas mudavam a cada vez que o app abria (hash do nome aleatório por processo). Agora são estáveis; duas execuções do mesmo commit produziram imagens idênticas. As cores destas referências não correspondem a nenhuma execução específica da v1.4.1, que não tinha cores fixas.

## `charts/`

Cada gráfico do WinForms sozinho, desenhado com uma árvore fixa em vez de uma varredura real, para comparar com o gráfico portado com os mesmos dados (Fase 5.2).

| | |
|---|---|
| **Versão** | `cross-platform` no commit `153c06b`, [execução de 10/10/2026](https://github.com/SamuelTM/c2flux/actions/runs/38060639616) do `ui-screenshots.yml` |
| **Dados** | `tests/fixtures/generate_chart_fixture.py` com raiz `T:\`: árvore (262 arquivos, 377 MB), histórico de armazenamento e comparação de varreduras. A versão com raiz `/fixture` fica em `tests/fixtures/charts/`, para os testes do app |
| **Tamanho** | 890×630 (área do gráfico na janela de 1280×800); árvore 360×450 |
| **Como** | `c2flux-shots --charts`: cada controle numa janela sem borda, com as cores e a fonte que ele herda na janela principal |

| Arquivo | Controle |
|---|---|
| `chart-pie.png`, `chart-bar.png`, `chart-sunburst.png`, `chart-treemap.png`, `chart-table.png` | `Chart_PieChart`, `Chart_BarChart`, `Chart_Sunburst`, `Chart_Treemap`, `Chart_TableGridChart` |
| `chart-tree.png` | `TreeEntrySizeBarView`, sem os ícones do shell. A raiz mostra 4 GB porque `T:\` é um volume de verdade no runner |
| `chart-symbols.png` | `StatusSymbolRenderer`: os quatro símbolos e o glifo +/− em 14 px e 48 px |
| `chart-storage-history.png` | `StorageHistoryChart` (660×520), 8 medições de espaço livre num volume de 500 GB |
| `chart-growth-overview.png` | `ScanHistoryGrowthOverviewControl` (1080×520), comparação de duas varreduras |

O lado Avalonia sai dos testes do app (`ChartCaptures`), com os mesmos nomes de arquivo, em `$C2FLUX_CHART_OUT` (o canvas do treemap como `chart-treemap-canvas.png`: a faixa de baixo de `chart-treemap.png`, a partir de y = 454).

Diferença média por pixel (0–255, maior canal) entre o port e a referência, na Fase 5.2: símbolos 1,0; barras 1,5; pizza 2,5; crescimento 3,1; armazenamento 3,5; sunburst 3,9; árvore 4,0; treemap 7,0. Quase toda a diferença é a fonte (Inter em tons de cinza contra Segoe UI com ClearType).

## Limitações conhecidas

- **Só tema escuro:** na v1.4.1 o tema claro não é alcançável (o `AppSettings.Load()` força `WindowsDarkMode`). Não existe referência para o claro.
- **Só 100% de escala:** o runner não permite mudar o DPI.
- **Conteúdo variável:** datas, o painel de partições e a barra de status mostram o momento da execução e o espaço livre real de `C:` e `D:` do runner.
- **Fora de propósito:**
  - **Aba "Colors" das Configurações:** o botão nunca é adicionado à janela na v1.4.1, então o usuário não chega a ela.
  - **Diálogos de arquivo (`AppFileDialog`):** serão substituídos pelos nativos de cada sistema.
  - **Menu nativo do Explorer:** não será replicado (Fase 4).

Para gerar de novo: *Actions → UI screenshots → Run workflow* (`app_ref` escolhe a versão).
