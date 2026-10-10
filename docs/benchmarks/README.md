# Benchmarks dos scanners

Registros históricos de desempenho dos scanners. **Não são limites de comparação:** cada runner do GitHub cai em um hardware diferente (o mesmo `C:\` já levou 4,3 s numa execução e 12 s em outra). Regressões são sempre verificadas pelo workflow [`scanner-benchmark.yml`](../../.github/workflows/scanner-benchmark.yml), que compara as duas versões **no mesmo job**. Veja a Fase 0.2 do [ROADMAP](../../ROADMAP.md).

## `baseline-winforms.json`

Tag `baseline-winforms` (c² flux v1.4.1, commit `92ecc38`), medida sozinha em um runner `windows-latest` em 09/10/2026 ([execução](https://github.com/SamuelTM/c2flux/actions/runs/37971977043)). 7 rodadas + 1 de aquecimento, cada uma num processo novo.

| Scanner | Alvo | Melhor tempo | Mediana | Pico de memória | Alocado | Arquivos | Pastas |
|---|---|---|---|---|---|---|---|
| c2flux (MFT) | `T:\` (árvore sintética) | 114 ms | 117 ms | 49,8 MiB | 13,1 MiB | 15.147 | 313 |
| ntfsmft (MFT) | `T:\` | 134 ms | 136 ms | 48,3 MiB | 12,2 MiB | 15.147 | 313 |
| ntquery | `T:\` | 42 ms | 43 ms | 36,8 MiB | 5,6 MiB | 15.127 | 304 |
| win32find | `T:\` | falha (`NullReferenceException`) | — | — | — | — | — |
| c2flux (MFT) | `C:\` inteiro | 4.216 ms | 4.616 ms | 441,7 MiB | 591,0 MiB | 1.127.598 | 206.957 |
| ntfsmft (MFT) | `C:\` inteiro | 8.995 ms | 11.015 ms | 890,5 MiB | 1,2 GiB | 1.127.598 | 206.957 |
| ntquery | `C:\Program Files` | 1.102 ms | 1.180 ms | 191,3 MiB | 179,6 MiB | 265.774 | 38.286 |
| win32find | `C:\Program Files` | falha (`NullReferenceException`) | — | — | — | — | — |

O `T:` é um disco virtual NTFS de 4 GiB criado no job, com a árvore de teste gerada por `tests/fixtures/generate_test_tree.py --profile medium`.

### Observações

- **win32find** (`DirectoryScanner`) falha com a configuração padrão por um bug pré-existente (v1.3.4). A correção está prevista na Fase 2 do roadmap.
- **Os scanners não concordam entre si no `T:`**, o que é esperado, mas precisa ser definido na suíte de conformidade da Fase 2:
  - `ntquery` soma exatamente os bytes que o usuário consegue ler, segundo o manifesto da árvore (687.069.899). Ele não entra na pasta sem permissão e conta os 2 symlinks de arquivo como arquivos de 0 bytes.
  - Os scanners de MFT somam 718.544.683 bytes (+31,5 MB). Eles leem o volume diretamente, ignorando permissões, e incluem os arquivos internos do NTFS (`$MFT`, `$LogFile` etc.) e a pasta `System Volume Information`.
- **c2flux vs ntfsmft no `C:\`:** o c2flux é cerca de 2× mais rápido e usa metade da memória, com o mesmo resultado em arquivos e pastas.

## macOS: `GetAttrListBulkScanner` × `ManagedScanner` × `du`

Medido em 10/10/2026 num Mac com Apple M4 (macOS 26, SSD interno APFS), com o cache do sistema já aquecido. Os tempos são os melhores de 3 rodadas, e o do `du -sk` é o de uma rodada. Os dois scanners encontraram exatamente os mesmos arquivos, pastas e bytes, e o dump da árvore de teste foi idêntico byte a byte.

| Pasta | Arquivos | Pastas | `getattrlistbulk` | `managed` | `du -sk` |
|---|---|---|---|---|---|
| `/Applications` | 228.557 | 35.044 | 519 ms | 1.028 ms | 826 ms |
| `/usr` | 27.202 | 1.980 | 83 ms | 505 ms | 67 ms |
| `/System/Library` | 285.836 | 145.482 | 3.421 ms | 9.750 ms | 2.906 ms |

O scanner nativo é de 2× a 6× mais rápido que o `managed` e fica no nível do `du`. O `du` usa `fts`, que no macOS também lê as pastas com `getattrlistbulk`, mas não monta árvore nenhuma.
