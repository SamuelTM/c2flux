# Referências de fidelidade visual

Capturas da interface WinForms atual, usadas na Fase 5 do [ROADMAP](../../ROADMAP.md) como modelo para a nova interface em Avalonia. Cada tela nova deve ser comparada com a imagem de mesmo nome em `reference/`.

## `reference/`

| | |
|---|---|
| **Versão** | `cross-platform` no commit `00298dd`: a v1.4.1 original (`baseline-winforms`) mais as correções dos bugs do gráfico de pizza e do `DirectoryScanner`. A interface é idêntica à original; a única diferença visível é que o gráfico de pizza deixou de quebrar |
| **Onde** | Runner `windows-latest` do GitHub, workflow [`ui-screenshots.yml`](../../.github/workflows/ui-screenshots.yml), [execução de 09/10/2026](https://github.com/SamuelTM/c2flux/actions/runs/37980611729) |
| **Tela** | 1920×1080, DPI 96 (escala 100%), tema escuro, idioma inglês |
| **Janela principal** | 1280×800, após varrer `T:\`, um disco virtual NTFS de 4 GiB com a árvore de teste (`tests/fixtures/generate_test_tree.py --profile medium`) |
| **Detalhes** | `reference/index.json` lista cada captura, tamanho e método |

| Arquivo | Tela |
|---|---|
| `main-empty.png` | Janela principal ao abrir, sem varredura |
| `main-table.png` | Após varrer `T:\`, visualização Tabela |
| `main-pie.png`, `main-bar.png`, `main-sunburst.png`, `main-treemap.png` | As outras visualizações |
| `main-analysis.png` | Painel Análise (Extensões) |
| `main-storage-history.png` | Painel Histórico de armazenamento |
| `search.png` | Janela de busca |
| `settings.png` | Configurações (aba Geral) |
| `about.png` | Sobre |
| `alert-history.png`, `scan-history.png`, `storage-history.png` | Históricos (vazios: não há dados salvos) |
| `database-move.png`, `debug-class.png` | Diálogos auxiliares |

## Limitações conhecidas

- **Só tema escuro:** na v1.4.1 o tema claro não é alcançável (o `AppSettings.Load()` força `WindowsDarkMode`). Não existe referência para o claro.
- **Só 100% de escala:** o runner não permite mudar o DPI.
- **Conteúdo variável:** o painel de partições e a barra de status mostram o espaço livre real de `C:` e `D:` do runner, que muda a cada execução.
- **Ainda não cobertas:** detalhes do histórico de armazenamento, aviso de atualização, caixas de diálogo de `AppDialogs`, menus de contexto, outras abas das Configurações e da Análise, e históricos com dados.

Para gerar de novo: *Actions → UI screenshots → Run workflow* (`app_ref` escolhe a versão).
