# Checklist de fidelidade (Fase 5.4)

Cada tela da interface Avalonia comparada com a referência WinForms de mesmo nome (`reference/` e `charts/`). Os números vêm de [`tests/fidelity/compare.py`](../../tests/fidelity/compare.py), que a CI roda nos três SOs e publica no resumo do job "Core and app tests" (artefato `app-captures-<so>`). A diferença é a média, por pixel, da maior diferença entre os canais (0 a 255). Windows, macOS e Linux dão o mesmo resultado com variação de até 1 ponto, porque as capturas saem da plataforma headless do Avalonia com Skia e a fonte embutida.

Legenda: ✓ confere; ~ confere com resíduo conhecido; — não se aplica (janela de tamanho fixo, ou sem o item). "Estados" cobre o que as capturas mostram: desabilitado, selecionado, marcado e, nas linhas `state-*`, o mouse em cima e o botão pressionado (o cursor real no WinForms, o mouse simulado do headless no port). "Redimensionar" com ~ nos controles de gráfico: eles redesenham em qualquer tamanho, mas só foram comparados no tamanho da referência; nas janelas, o tamanho menor (ou o mínimo) e, na principal, também um maior têm referência própria.

| Tela | Diferença | Layout | Cores | Fontes | Ícones | Estados | Redimensionar |
|---|---:|:-:|:-:|:-:|:-:|:-:|:-:|
| Símbolos de status (`chart-symbols`) | 1,0 | ✓ | ✓ | — | ✓ | — | — |
| Barras (`chart-bar`) | 0,8 | ✓ | ✓ | ~ | ✓ | — | ~ |
| Pizza (`chart-pie`) | 2,0 | ✓ | ✓ | ~ | — | — | ~ |
| Sunburst (`chart-sunburst`) | 3,8 | ✓ | ✓ | ~ | — | — | ~ |
| Árvore (`chart-tree`) | 4,0 | ✓ | ✓ | ~ | ~ | ✓ | ~ |
| Treemap (`chart-treemap`) | 5,8 | ✓ | ✓ | ~ | — | — | ~ |
| Tabela (`chart-table`) | 2,4 | ✓ | ✓ | ~ | — | ✓ | ~ |
| Histórico de armazenamento, gráfico (`chart-storage-history`) | 2,9 | ✓ | ✓ | ~ | — | — | ~ |
| Visão de crescimento (`chart-growth-overview`) | 2,4 | ✓ | ✓ | ~ | — | ~ | ~ |
| Janela principal vazia (`main-empty`) | 5,0 | ✓ | ✓ | ~ | ✓ | ✓ | ✓ |
| Configurações, Geral (`settings`) | 6,6 | ✓ | ✓ | ~ | — | ✓ | — |
| Configurações, UI / Histórico / Exportar / Log | 2,2 a 4,0 | ✓ | ✓ | ~ | — | ✓ | — |
| Busca vazia (`search`) | 2,0 | ✓ | ✓ | ~ | ✓ | ✓ | ✓ |
| Busca com resultados (`search-results`) | 9,4 | ✓ | ✓ | ~ | — | ✓ | ✓ |
| Busca no tamanho mínimo (`search-results-min`), também com o mouse em Filtros | 10,1 / 10,0 | ✓ | ✓ | ~ | — | ✓ | ✓ |
| Histórico de alertas (`alert-history-entries`) | 6,2 | ✓ | ✓ | ~ | ✓ | ✓ | ✓ |
| Sobre (`about`) | 7,1 | ✓ | ✓ | ~ | ✓ | ✓ | — |
| Janela principal em 1000×650 (`main-empty-small`), barra em duas linhas | 7,7 | ✓ | ✓ | ~ | ✓ | — | ✓ |
| Janela principal em 1600×900 (`main-empty-large`) | 3,7 | ✓ | ✓ | ~ | ✓ | — | ✓ |
| Histórico de alertas no tamanho mínimo (`alert-history-entries-min`) | 8,4 | ✓ | ✓ | ~ | ✓ | — | ✓ |
| Botão OK do Sobre com o mouse em cima e pressionado (`state-about-ok-*`) | 7,1 | ✓ | ✓ | ~ | — | ✓ | — |
| Botão de visão marcado com o mouse em cima (`state-main-toolbar-hover`) | 4,9 | ✓ | ✓ | ~ | ✓ | ✓ | — |
| Checkbox com o mouse em cima (`state-settings-checkbox-hover`; não muda em nenhum dos dois) | 6,6 | ✓ | ✓ | ~ | — | ✓ | — |
| Botão de visão desmarcado com o mouse em cima (`state-main-view-hover`) | 5,0 | ✓ | ✓ | ~ | ✓ | ✓ | — |
| Aba das configurações com o mouse em cima (`state-settings-tab-hover`) | 6,5 | ✓ | ✓ | ~ | — | ✓ | — |
| Botão de Mover banco com o mouse em cima (`state-database-move-hover`) | 7,0 | ✓ | ✓ | ~ | — | ✓ | — |
| Download (primário) com o mouse em cima (`state-update-download-hover`) | 5,0 | ✓ | ✓ | ~ | — | ✓ | — |
| Fechar do histórico de alertas com o mouse em cima (`state-alert-history-close-hover`) | 5,8 | ✓ | ✓ | ~ | — | ✓ | — |
| Atualização disponível (`update-available`) | 5,1 | ✓ | ✓ | ~ | ✓ | — | — |
| Mover banco (`database-move`) | 6,5 | ✓ | ✓ | ~ | — | ✓ | — |
| Depuração (`debug-class`; o mínimo é o tamanho inicial) | 3,3 | ✓ | ✓ | ~ | ✓ | — | ✓ |
| Análise e histórico de armazenamento embutidos | só visual | ✓ | ✓ | ~ | ✓ | ✓ | ~ |

## Resíduos conhecidos

- **Fonte.** A Selawik (licença OFL) tem as métricas da Segoe UI, mas o desenho das letras e a rasterização diferem: os traços do Skia chegam a branco 255, onde o GDI+ do WinForms para em 229, com a mesma tinta total (2 a 3% de diferença); e frases longas ficam cerca de 2% mais curtas. É a maior parte da diferença que sobra nas telas de texto. Só a Segoe UI resolveria, e ela não pode ser distribuída.
- **Dados.** A captura da busca com resultados usa caminhos `T:\...` também no macOS e no Linux, onde a coluna Unidade sai "/". O Sobre e a busca usam o mesmo texto da referência; Mover banco usa o caminho do banco do runner; a Análise e o histórico embutido usam a fixture dos gráficos, por isso não têm número.
- **Foco.** As referências mostram o anel de foco que o WinForms põe no primeiro controle ou na próxima aba. O Avalonia só mostra o anel de foco vindo do teclado.
- **Hover dos botões da busca.** No WinForms, o texto dos botões da busca (Filtros, Pesquisar, Cancelar, Limpar filtros) vira a cor de hover, 58 sobre o fundo 20, e some. No port o texto continua legível; só esse detalhe difere de propósito.
- **Colunas automáticas.** As colunas "auto" das tabelas Ant ficam cerca de 5 px mais largas que no AntdUI.
- **Rolagem.** As barras de rolagem são as do Fluent (finas, se expandem ao passar o mouse), parecidas com as do Windows 11 escuro, mas não idênticas.
- **Janela nativa.** As capturas não passam por uma janela nativa do Windows; a moldura e a barra de título vêm do SO e ficam fora da comparação.

## Medidas aplicadas na 5.4

- `Label` do WinForms desenha o texto 3 px para dentro, `AntdUI.Label` 1 px mais abaixo; células do `DataGridView` 3 px e cabeçalhos 5 px mais para dentro que no `AntdUI.Table`.
- `LinkLabel` desabilitado: texto cinza 143 em relevo, sobre uma cópia quase preta deslocada 1 px.
- O botão do Ko-fi estica a imagem para 179×42 (`PictureBox` com `StretchImage`).
- A barra de ferramentas desenha os ícones de 16 px em 12 px, como o AntdUI; com eles em 16 px a barra quebrava em duas linhas já em 1280×800. Cada linha da barra tem 42 px.
- Botões de diálogo: fundo 49 com o mouse em cima e 55 pressionado; botão de visão marcado com o mouse em cima: fundo #3C89E8 e texto e ícone #5CA9FF.
- Tamanhos de janela: os do WinForms incluem a moldura (16×39 px). A janela principal, o histórico de alertas e os mínimos dos detalhes do histórico e da depuração estavam com a moldura somada à área interna.
- Texto cortado em tabelas clássicas, rótulos e status termina em "..." (`TextRenderer` com `EndEllipsis`), não em "…".
- A tabela não rola mais para mostrar a linha selecionada antes do primeiro layout (o histórico de alertas abria com a primeira linha escondida).
- Botão primário de diálogo com o mouse em cima: fundo #3C89E8, texto branco. A linha de botões do histórico de alertas fica 5 px abaixo da caixa de detalhes.
