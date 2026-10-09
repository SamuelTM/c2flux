# Referências de fidelidade visual

Capturas da interface WinForms atual, usadas na Fase 5 do [ROADMAP](../../ROADMAP.md) como modelo para a nova interface em Avalonia. Cada tela nova deve ser comparada com a imagem de mesmo nome em `reference/`.

## `reference/`

| | |
|---|---|
| **Versão** | `cross-platform` no commit `370eb61`: a v1.4.1 original (`baseline-winforms`) mais as correções do gráfico de pizza e do `DirectoryScanner`. A interface é idêntica à original; a única diferença visível é que o gráfico de pizza deixou de quebrar |
| **Onde** | Runner `windows-latest` do GitHub, workflow [`ui-screenshots.yml`](../../.github/workflows/ui-screenshots.yml), [execução de 09/10/2026](https://github.com/SamuelTM/c2flux/actions/runs/38000804583) |
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

## Limitações conhecidas

- **Só tema escuro:** na v1.4.1 o tema claro não é alcançável (o `AppSettings.Load()` força `WindowsDarkMode`). Não existe referência para o claro.
- **Só 100% de escala:** o runner não permite mudar o DPI.
- **Conteúdo variável:** datas, o painel de partições e a barra de status mostram o momento da execução e o espaço livre real de `C:` e `D:` do runner.
- **Aba "Changed files" vazia:** o arquivo aumentado entre as varreduras não aparece como alterado, nem forçando a gravação no disco. A causa ainda não foi encontrada (ver ROADMAP, 0.3). O layout da aba é o mesmo de "New files" e "Deleted files", que estão preenchidas.
- **Fora de propósito:**
  - **Aba "Colors" das Configurações:** o botão nunca é adicionado à janela na v1.4.1, então o usuário não chega a ela.
  - **Diálogos de arquivo (`AppFileDialog`):** serão substituídos pelos nativos de cada sistema.
  - **Menu nativo do Explorer:** não será replicado (Fase 4).

Para gerar de novo: *Actions → UI screenshots → Run workflow* (`app_ref` escolhe a versão).
