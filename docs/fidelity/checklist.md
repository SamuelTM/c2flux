# Checklist de fidelidade (Fase 5.4)

Cada tela da interface Avalonia comparada com a referência WinForms de mesmo nome (`reference/` e `charts/`). Os números vêm de [`tests/fidelity/compare.py`](../../tests/fidelity/compare.py), que a CI roda nos três SOs e publica no resumo do job "Core and app tests" (artefato `app-captures-<so>`). A diferença é a média, por pixel, da maior diferença entre os canais (0 a 255). Windows, macOS e Linux dão o mesmo resultado com variação de até 1 ponto, porque as capturas saem da plataforma headless do Avalonia com Skia e a fonte embutida.

Legenda: ✓ confere; ~ confere com resíduo conhecido; ? ainda não verificado; — não se aplica (janela de tamanho fixo, ou sem o item). "Estados" cobre o que as capturas mostram (desabilitado, selecionado, marcado); hover e pressionado não foram comparados.

| Tela | Diferença | Layout | Cores | Fontes | Ícones | Estados | Redimensionar |
|---|---:|:-:|:-:|:-:|:-:|:-:|:-:|
| Símbolos de status (`chart-symbols`) | 1,0 | ✓ | ✓ | — | ✓ | — | — |
| Barras (`chart-bar`) | 0,8 | ✓ | ✓ | ~ | ✓ | — | ? |
| Pizza (`chart-pie`) | 2,0 | ✓ | ✓ | ~ | — | — | ? |
| Sunburst (`chart-sunburst`) | 3,8 | ✓ | ✓ | ~ | — | — | ? |
| Árvore (`chart-tree`) | 4,0 | ✓ | ✓ | ~ | ~ | ✓ | ? |
| Treemap (`chart-treemap`) | 5,8 | ✓ | ✓ | ~ | — | — | ? |
| Tabela (`chart-table`) | 2,4 | ✓ | ✓ | ~ | — | ✓ | ? |
| Histórico de armazenamento, gráfico (`chart-storage-history`) | 2,9 | ✓ | ✓ | ~ | — | — | ? |
| Visão de crescimento (`chart-growth-overview`) | 2,4 | ✓ | ✓ | ~ | — | ~ | ? |
| Janela principal vazia (`main-empty`) | 5,8 | ✓ | ✓ | ~ | ~ | ✓ | ? |
| Configurações, Geral (`settings`) | 6,6 | ✓ | ✓ | ~ | — | ✓ | — |
| Configurações, UI / Histórico / Exportar / Log | 2,2 a 4,0 | ✓ | ✓ | ~ | — | ✓ | — |
| Busca vazia (`search`) | 2,0 | ✓ | ✓ | ~ | ✓ | ✓ | ? |
| Busca com resultados (`search-results`) | 9,4 | ✓ | ✓ | ~ | — | ✓ | ? |
| Histórico de alertas (`alert-history-entries`) | 6,3 | ✓ | ✓ | ~ | ✓ | ✓ | ? |
| Sobre (`about`) | 7,1 | ✓ | ✓ | ~ | ✓ | ✓ | — |
| Atualização disponível (`update-available`) | 5,1 | ✓ | ✓ | ~ | ✓ | — | — |
| Mover banco (`database-move`) | 6,5 | ✓ | ✓ | ~ | — | ✓ | — |
| Depuração (`debug-class`) | 3,3 | ✓ | ✓ | ~ | ✓ | — | ? |
| Análise e histórico de armazenamento embutidos | só visual | ✓ | ✓ | ~ | ✓ | ✓ | ? |

## Resíduos conhecidos

- **Fonte.** A Selawik (licença OFL) tem as métricas da Segoe UI, mas o desenho das letras e a rasterização diferem: os traços do Skia chegam a branco 255, onde o GDI+ do WinForms para em 229, com a mesma tinta total (2 a 3% de diferença); e frases longas ficam cerca de 2% mais curtas. É a maior parte da diferença que sobra nas telas de texto. Só a Segoe UI resolveria, e ela não pode ser distribuída.
- **Dados.** A captura da busca com resultados usa caminhos `T:\...` também no macOS e no Linux, onde a coluna Unidade sai "/". O Sobre e a busca usam o mesmo texto da referência; Mover banco usa o caminho do banco do runner; a Análise e o histórico embutido usam a fixture dos gráficos, por isso não têm número.
- **Foco.** As referências mostram o anel de foco que o WinForms põe no primeiro controle ou na próxima aba. O Avalonia só mostra o anel de foco vindo do teclado.
- **Colunas automáticas.** As colunas "auto" das tabelas Ant ficam cerca de 5 px mais largas que no AntdUI.
- **Rolagem.** As barras de rolagem são as do Fluent (finas, se expandem ao passar o mouse), parecidas com as do Windows 11 escuro, mas não idênticas.
- **Janela nativa.** As capturas não passam por uma janela nativa do Windows; a moldura e a barra de título vêm do SO e ficam fora da comparação.

## Medidas aplicadas na 5.4

- `Label` do WinForms desenha o texto 3 px para dentro, `AntdUI.Label` 1 px mais abaixo; células do `DataGridView` 3 px e cabeçalhos 5 px mais para dentro que no `AntdUI.Table`.
- `LinkLabel` desabilitado: texto cinza 143 em relevo, sobre uma cópia quase preta deslocada 1 px.
- O botão do Ko-fi estica a imagem para 179×42 (`PictureBox` com `StretchImage`).
