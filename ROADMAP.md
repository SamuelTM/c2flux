# Roadmap: c² flux multiplataforma

Plano para transformar o c² flux, hoje exclusivo do Windows (WinForms + AntdUI), em um aplicativo **100% multiplataforma** (Windows, macOS e Linux). A ideia é usar **uma única base de código**, ter uma **interface o mais fiel possível à atual** e usar **o método de varredura mais rápido disponível em cada sistema**.

---

## 1. Objetivos e princípios

1. **Uma base de código.** C# / .NET 10 em todas as plataformas. Código específico de SO fica isolado atrás de interfaces, nunca espalhado pela UI.
2. **Fidelidade visual.** A nova interface deve ser reconhecível como o c² flux atual: mesmo layout, mesmas cores Ant Design, mesmos gráficos (Treemap, Sunburst, Pizza, Barras, Tabela), mesmos temas claro/escuro e os 30 idiomas.
3. **Velocidade máxima por SO.** Cada plataforma usa a sua API de varredura mais rápida, com fallback automático para métodos mais simples.
4. **Nunca quebrar o Windows.** Durante toda a migração, a versão Windows continua funcionando e sendo lançada. A troca de UI só acontece quando houver paridade.
5. **Migração incremental.** Cada fase entrega algo que compila, roda e pode ser testado.

---

## 2. Diagnóstico do código atual

| Item | Situação |
|---|---|
| Linguagem / runtime | C# sobre .NET 10 (`net10.0-windows7.0`), cerca de 51 mil linhas |
| UI | WinForms + AntdUI 2.4.3, **só Windows** (cerca de 33 mil linhas, sendo 6,6 mil só em `AntdThemeService.cs`) |
| Gráficos | Desenhados à mão com GDI+ (`Graphics`, `FillRectangle`, `DrawString`) em `Chart_*.cs`, `TreeEntrySizeBarView.cs` e `StorageHistoryChart.cs` |
| Lógica sem dependência de UI | Cerca de 34 arquivos: histórico, busca, redundância, cache, SQLite, localização, configurações, exportação CSV etc. |
| Banco de dados | `Microsoft.Data.Sqlite` + `SQLitePCLRaw.bundle_e_sqlite3`, que já são multiplataforma |
| Biblioteca NtfsReader | Compila para `net10.0` mas usa APIs Win32 (`CreateFile`, `DeviceIoControl`); só funciona no Windows |

### 2.1 Pontos específicos do Windows

| Área | Onde | O que usa |
|---|---|---|
| Varredura MFT | `C2FluxScanner.cs`, `NtfsMftScanner.cs`, `NtfsReaderFastNodeProvider.cs`, `Libraries/NtfsReader` | Leitura direta da MFT do NTFS (exige admin) |
| Varredura nativa | `NtQueryDirectoryScanner.cs` | `NtOpenFile`, `NtQueryDirectoryFile` |
| Varredura fallback | `DirectoryScanner.cs` | `FindFirstFileEx`, `GetFileInformationByHandle(Ex)` |
| Menu de contexto do Explorer | `NativeShellContextMenu.cs`, `ShellContextMenuService.cs` | `SHParseDisplayName`, `SHBindToParent`, `CreatePopupMenu`, `TrackPopupMenuEx` |
| Ícones de arquivo | `ShellIconService.cs` | `SHGetFileInfo`, `SHGetStockIconInfo`, `DrawIconEx` |
| "Abrir no Explorer" | `Chart_*.cs`, `AdvancedFeaturesForm.cs` | `Process.Start("explorer.exe", ...)` |
| Tema escuro do sistema | `AntdThemeService.cs`, `PartitionGridController.cs` | Registro (`AppsUseLightTheme`), `DwmSetWindowAttribute`, `SetWindowTheme` |
| Hooks de janela | `AntdThemeService.cs`, `StorageHistoryForm.cs` | `SetWindowsHookEx`, `WindowFromPoint`, `GetClassName` |
| Espaço livre | `StatusMainFormController.cs` | `GetDiskFreeSpace` |
| Elevação para admin | `Program.cs`, `NtfsMftScanner.cs` | `WindowsPrincipal`, `Verb = "runas"` |
| Lista de unidades | `DriveComboBoxController.cs`, `PartitionGridController.cs`, `AppFileDialog.cs` | `DriveInfo.GetDrives()` com semântica de letras de unidade |
| Diálogo de arquivos próprio | `AppFileDialog.cs` (1,7 mil linhas) | Pastas especiais do Windows + unidades |
| Dados ao lado do executável | `AppSettings.cs`, `ScanHistoryDatabaseService.cs`, `RedundancyHashCacheService.cs`, `StorageHistoryDetailsService.cs`, `AppAlertLog.cs`, `LocalizationService.cs`, `DatabaseMoveForm.cs` | `AppContext.BaseDirectory`. Funciona num app "portátil" no Windows, mas **não** dentro de um `.app` assinado no macOS nem em `/usr` / AppImage no Linux |
| Build e release | `.github/workflows/release.yml` | Só `windows-latest` e `win-x64`, gera um único ZIP |

---

## 3. Arquitetura alvo

```
c2flux.sln
├── src/
│   ├── c2flux.Core/                 net10.0 — modelo, serviços, SQLite, localização, abstrações
│   │   ├── Model/                   FileSystemEntry, ScanProgress, PauseToken, ...
│   │   ├── Scanning/                IFileSystemScanner, ScannerPipeline, ManagedScanner
│   │   ├── Platform/                IPlatformServices, IVolumeProvider, IShellIntegration, IAppPaths
│   │   └── Services/                ScanHistory, Search, Redundancy, StorageHistory, CSV, Settings, Update
│   │
│   ├── c2flux.Platform.Windows/     net10.0 (+ [SupportedOSPlatform("windows")])
│   │   ├── Scanning/                MftScanner (C2Flux + NtfsMft), NtQueryScanner, Win32FindScanner
│   │   └── Shell/                   menu de contexto, ícones, elevação, volumes, tema
│   │
│   ├── c2flux.Platform.MacOS/       net10.0 (+ [SupportedOSPlatform("macos")])
│   │   ├── Scanning/                GetAttrListBulkScanner
│   │   └── Shell/                   Finder, NSWorkspace, volumes, Acesso Total ao Disco
│   │
│   ├── c2flux.Platform.Linux/       net10.0 (+ [SupportedOSPlatform("linux")])
│   │   ├── Scanning/                GetDentsStatxScanner (+ IoUringStatxScanner opcional)
│   │   └── Shell/                   xdg-open, D-Bus FileManager1, /proc/self/mountinfo
│   │
│   ├── c2flux.App/                  Avalonia UI — janelas, controles, gráficos, tema Ant Design
│   │
│   └── c2flux.WinForms/             UI atual (mantida até a paridade, depois removida)
│
├── libs/NtfsReader/                 inalterado, referenciado só por Platform.Windows
│
└── tests/
    ├── c2flux.Core.Tests/
    ├── c2flux.Scanning.Conformance/ mesma árvore de teste, todos os scanners, resultados idênticos
    └── c2flux.Benchmarks/           BenchmarkDotNet — comparação entre scanners por SO
```

**Seleção em tempo de execução:** os três projetos `Platform.*` compilam como `net10.0` puro. A aplicação escolhe a implementação na inicialização com `OperatingSystem.IsWindows()` / `IsMacOS()` / `IsLinux()`. Os publishes por RID (`win-x64`, `osx-arm64`, `linux-x64` etc.) podem usar *trimming* para descartar o código das outras plataformas.

**Interop:** todo P/Invoke novo usa `[LibraryImport]` (gerado em tempo de compilação, compatível com AOT/trimming) em vez de `[DllImport]`.

---

## 4. Fases

### Fase 0: Preparação

**Objetivo:** ter uma base de medição e de segurança antes de mexer em qualquer coisa.

#### 0.1 Estrutura

- [x] Criar branch de longa duração `cross-platform` no fork `SamuelTM/c2flux`
- [x] Garantir que o build atual do Windows passa no CI (`dotnet build` em `windows-latest`): `.github/workflows/ci.yml`, verde no GitHub. O build também funciona no macOS com `-p:EnableWindowsTargeting=true`, útil para checagem local rápida
- [x] Marcar o commit atual com a tag `baseline-winforms` (`92ecc38`, upstream v1.4.1), a referência fixa para todas as comparações de desempenho e fidelidade
- [x] Criar uma **árvore de teste sintética** reproduzível: `tests/fixtures/generate_test_tree.py`
  - Cobre limites de tamanho, pasta muito larga, pasta muito profunda, caminhos com mais de 260 caracteres, nomes Unicode (NFC e NFD) e especiais, hardlinks, symlinks (inclusive quebrados e em loop), conteúdo duplicado, arquivo esparso, datas extremas, pastas vazias e itens sem permissão
  - Perfis `small` (cerca de 800 arquivos), `medium` (cerca de 15 mil) e `large` (cerca de 200 mil), todos determinísticos a partir de uma semente
  - Gera um manifesto JSON com cada entrada e os totais esperados, com e sem as pastas ilegíveis, para os testes de conformidade. O que o SO não suporta vai para `skipped_features`
  - Só apaga ou sobrescreve pastas que ele mesmo criou (arquivo marcador `.c2flux-test-tree`)
  - Validado no CI nos três SOs (job `test-tree`)
- [x] Decidir as questões da seção 9 que afetam o início do trabalho (fork, diálogo de arquivos, assinatura no macOS). As demais ficam para as fases em que se tornam relevantes

#### 0.2 Estratégia de medição de desempenho no Windows

O desenvolvimento principal acontece no macOS (Apple Silicon), sem um PC Windows físico. Por isso, a medição **não depende de números absolutos**. Ela compara a versão de referência com a versão nova **na mesma máquina e na mesma execução**. Três ambientes complementares:

| Ambiente | Uso | Vantagens | Limitações |
|---|---|---|---|
| **GitHub Actions** (`windows-latest`) | Detecção automática de regressão a cada mudança nos scanners | Gratuito, roda como administrador (MFT funciona), unidade C: em NTFS com centenas de milhares de arquivos reais, disparado do Mac | Hardware compartilhado: números variam entre execuções. Só vale comparar dentro do mesmo job |
| **VM Windows 11 ARM no Mac** (Parallels ou UTM) | Desenvolvimento, depuração, inspeção visual da UI, testes manuais da MFT | Interativo, local, sem custo por execução (UTM) | Disco virtual distorce tempos absolutos. Build `win-x64` roda emulado (usar `win-arm64` quando disponível) |
| **Hardware real** (usuários beta, PC emprestado, autor original ou VM na nuvem) | Validação final e números para divulgação | Representa o uso real | Esporádico, não automatizável |

**Workflow de benchmark comparativo** (`.github/workflows/scanner-benchmark.yml`):

- [x] Ferramenta de linha de comando `c2flux-bench` (`tests/c2flux.Benchmarks`): roda **um** scanner **uma** vez e devolve em JSON o tempo, os bytes alocados, a memória retida, o pico de memória, as coletas de GC e uma impressão digital da árvore (contagens, bytes e SHA-256 de todas as entradas)
  - É compilada **uma única vez** e carrega por reflexão o `c2flux.dll` de cada versão (`AssemblyLoadContext` próprio). As duas versões são medidas exatamente pelo mesmo código, e não é preciso compilá-la contra a referência
  - Os nomes de tipo de cada scanner ficam em `ScannerDefinition` (`AppLoader.cs`). **Quando um scanner for renomeado ou movido (Fases 1 e 2), adicionar o novo nome ali**
  - Usa `new AppSettings()` (padrões de instalação nova), nunca as configurações do disco
- [x] Orquestrador `run_benchmark.py`: um processo novo por medição, 1 rodada de aquecimento descartada + N rodadas, alternando a ordem das versões a cada rodada (R,N depois N,R) para nenhuma se beneficiar de rodar em segundo
- [x] Alvos:
  - **Estáticos:** um disco virtual NTFS (`T:`, VHDX criado no job) com a árvore sintética. Nada mais escreve nele, então **os quatro scanners, inclusive os de MFT, precisam produzir árvores idênticas** entre referência e nova versão
  - **Reais:** `C:\` inteiro (MFT) e `C:\Program Files` (NtQuery e FindFirstFile). Só tempo e memória, porque os arquivos mudam enquanto o runner trabalha
- [x] Falha se o **melhor tempo** (mínimo das rodadas) da versão nova for mais de 10% pior, ou se a **mediana** do pico de memória for 15% maior (limites ajustáveis no `workflow_dispatch`). O relatório também mostra a mediana do tempo e a dispersão
- [x] Veredictos: ✅ ok · ❌ regressão · 🟢 corrigido (falhava só na referência) · ⚠️ quebrado nas duas versões (não falha o job) · ⏭️ sem suporte no ambiente
- [x] Limpa o cache de varredura (`%LOCALAPPDATA%\WTF\ScanCache`) antes de cada execução, para uma versão não aproveitar o cache da outra
- [x] Relatório no resumo do job (`$GITHUB_STEP_SUMMARY`) e artefato JSON com todas as medições brutas
- [x] Dispara em PRs e pushes no `cross-platform` que alterem arquivos de varredura, e manualmente via `workflow_dispatch` (com opção de medir só a referência)
- [x] Primeiras execuções no GitHub confirmadas: as medições funcionam de ponta a ponta, o disco virtual `T:` é criado e os três scanners que funcionam produzem árvores idênticas nas duas versões
- [x] Ruído real observado e tratado:
  - O mesmo `C:\` levou 4,3 s numa execução e 12 s em outra (hardware diferente entre runners). Isso confirma que só vale comparar dentro do mesmo job
  - No `C:\`, o `ntfsmft` alterna entre dois patamares (cerca de 13 s e 25–31 s) nas duas versões. Com a mediana, isso gerou um falso positivo de +65,8% com código idêntico. Como a interferência do runner só soma tempo, o critério passou a ser o melhor tempo, e a mesma sequência real fica em +0,2%

**Ambiente local** (adiado: por enquanto, o CI é a única referência de desempenho e de visual no Windows):

  - Na Fase 1, o `ntfsmft` no `C:` ficou no patamar lento em **todas** as rodadas medidas das duas versões, e o melhor tempo também falhou (+13,4% com código idêntico). Agora, quando um alvo reprova só por tempo e as rodadas estão dispersas (mais de 25%), ele é medido de novo uma vez e decidido com todas as rodadas. Com os tempos reais dessa execução, passa (+0,2%); uma lentidão real de 20% injetada continua reprovando
- [ ] ~~Instalar Windows 11 ARM numa VM (Parallels ou UTM) com o .NET 10 SDK~~ (adiado)
- [ ] ~~Documentar em `docs/dev/windows-vm.md` como compilar, rodar como administrador e executar a ferramenta de benchmark na VM~~ (adiado)

**Números de referência:**

- [x] Rodar o workflow uma vez só com a referência e guardar o resultado em `docs/benchmarks/baseline-winforms.json`, como registro histórico (não como limite de comparação, já que o hardware do CI muda). Resumo e observações em `docs/benchmarks/README.md`
- [ ] ~~Quando houver acesso a hardware real, registrar também esses números em `docs/benchmarks/`, com a especificação da máquina~~ (adiado)

#### 0.3 Referências visuais

- [x] **Capturas de tela de referência** de todas as telas em `docs/fidelity/reference/`, para servir de comparação com a nova UI. Tiradas do `cross-platform` (commit `f70c14f`): a `baseline-winforms` mais as correções dos bugs encontrados nesta fase, com a mesma interface. Feitas **no CI** (a VM foi adiada):
  - [x] Ferramenta `c2flux-shots` (`tests/c2flux.Screenshots`): carrega o app por reflexão, como o `c2flux-bench`, abre cada janela e salva um PNG via `PrintWindow`, mais um `index.json` com o que deu certo e o que falhou. Janelas inesperadas (avisos, erros) também são capturadas
  - [x] Workflow `ui-screenshots.yml`: publica a versão escolhida, monta o disco `T:` com a árvore de teste, varre e captura. Manual (`workflow_dispatch`) ou automático quando a ferramenta muda
  - Telas: janela principal vazia e após varrer `T:\` (Tabela, Pizza, Barras, Sunburst, Treemap, Análise, Histórico de armazenamento), Busca, Configurações, Sobre, Histórico de alertas, Histórico de varreduras, Histórico de armazenamento, Mover banco de dados, Debug
  - **Só tema escuro:** na v1.4.1 o tema claro não é alcançável. O `AppSettings.Load()` força `WindowsDarkMode` e o seletor de tema nas Configurações fica oculto. A nova UI deve oferecer os dois, mas a referência de fidelidade existe só para o escuro
  - Resolução: a ferramenta pede 1920×1080 ao Windows. A escala de DPI fica a do runner (provavelmente 100%)
  - [x] Primeira execução no GitHub: as 19 capturas funcionaram (resolução trocada de 1024×768 para 1920×1080, DPI 96), com imagens nítidas e completas
  - [x] **Bug pré-existente no gráfico de pizza, corrigido** (encontrado na 0.3): itens de 0 bytes viram fatias de ângulo zero, e a fatia no topo do círculo (270°) tem largura 0. O `LinearGradientBrush` de [Chart_PieChart.cs:242](src/c2flux.WinForms/Chart_PieChart.cs#L242) lança `ArgumentException` e o app mostra a janela de exceção não tratada. Acontece com o `T:` de teste (`$BadClus`, `$Secure` e `$Volume` têm 0 B) e, provavelmente, com qualquer pasta cujos últimos itens tenham 0 bytes. Corrigido: fatias com área zero não são preenchidas. As referências passam a ser capturadas do `cross-platform` (original + correções de bugs, mesma interface)
  - [x] Capturas revisadas e salvas em `docs/fidelity/reference/` (16 telas, 796 KB), com descrição e limitações em `docs/fidelity/README.md`
  - [x] Telas restantes cobertas: as referências passaram de 16 para **42 capturas** (1,9 MB), todas de uma mesma execução (commit `f70c14f`) e revisadas uma a uma:
    - Menus da janela principal (File, View, Tools, Help) e menus de contexto da árvore e da barra de ferramentas, capturados sobre a janela principal. O da árvore é o menu próprio do app; o menu nativo do Explorer, usado normalmente no Windows, não será replicado (Fase 4)
    - Abas: as 5 das Configurações, as 4 da Análise e as 7 do Histórico de varreduras
    - Telas com dados: busca com resultados, histórico de alertas com entradas de exemplo, histórico de varreduras com duas varreduras comparadas (o volume é alterado entre elas) e histórico de armazenamento com dois registros e detalhes
    - Aviso de atualização (dados de exemplo) e os diálogos de `AppDialogs` (aviso, sim/não, pedido de administrador)
    - Fora: diálogos de arquivo (`AppFileDialog`), que serão substituídos pelos nativos (decisão da seção 9), e a aba "Colors" das Configurações, que o usuário não alcança (o botão nunca é adicionado à janela)
  - [x] **Bug pré-existente no histórico de varreduras, corrigido** (encontrado na 0.3): ao carregar uma varredura salva, `BuildRootEntry` ([ScanHistoryDatabaseService.cs](src/c2flux.Core/ScanHistoryDatabaseService.cs)) ordenava as entradas por `Depth`, que só é preenchido ao salvar e fica 0 ao ler do banco. Pastas cujo nome vem antes do da pasta-mãe em ordem alfabética (ex.: `dir-00067` dentro de `group-000`, `sparse` dentro de `tree`) eram descartadas com tudo o que contêm. No `T:` de teste, cada varredura voltava com 5.033 dos 15.147 arquivos, e "Changed files" ficava vazio. O banco estava correto. Corrigido montando a árvore da raiz para baixo; verificado localmente com o banco do runner: 15.147 arquivos e o `sparse-64MiB.bin` como alterado (+4 MB). Afetava toda comparação de varreduras (novos, excluídos, alterados e crescimento por pasta)

**Entregável:** workflow de benchmark comparativo funcionando no CI, números de referência e capturas de tela registrados.

**Situação:** ✅ concluída em 09/10/2026. Os ambientes locais (VM e hardware real) ficaram adiados. De quebra, foram encontrados e corrigidos quatro bugs do original: gráfico de pizza com itens de 0 B, `DirectoryScanner` sempre quebrando, `DirectoryScanner` sem caminhos longos e histórico de varreduras perdendo arquivos ao carregar

---

### Fase 1: Extração do núcleo (`c2flux.Core`)

**Objetivo:** separar tudo o que não é interface num projeto `net10.0` puro, sem mudar o comportamento no Windows.

- [x] Reorganizar a solução na estrutura da seção 3 (mover arquivos sem reescrever): app em `src/c2flux.WinForms/`, Core em `src/c2flux.Core/`, NtfsReader em `libs/NtfsReader/`, ferramentas e testes em `tests/`. Só `git mv`, preservando o histórico. Os nomes dos recursos embutidos não mudaram. Os workflows de benchmark e capturas encontram o projeto nos dois layouts, porque a `baseline-winforms` continua com ele na raiz
- [x] Mover para o Core os arquivos já livres de UI: **27 arquivos**, escolhidos por uma análise de dependências (um arquivo só vai se tudo o que ele usa também for). Mesmo namespace (`c2flux`), sem mudar código. Ficaram no app, de propósito:
  - Scanners (`C2FluxScanner`, `NtfsMftScanner`, `NtQueryDirectoryScanner`, `DirectoryScanner`, `NtfsReaderFastNodeProvider`) e `ScanExecutionController`: são do Windows e vão para `c2flux.Platform.Windows` na Fase 2
  - `RedundancyAnalysisService`: usa P/Invoke do Windows para identificar arquivos; vai para trás de uma interface de plataforma na Fase 4
- [x] Remover do Core qualquer referência a `System.Windows.Forms` / `System.Drawing`: garantido pelo compilador, porque o Core é `net10.0` puro (as cores em `AppSettings` já eram `int`). Os dois avisos que serviços do Core abriam direto (falha ao gravar configurações e histórico de armazenamento) passaram por um ponto de extensão, `AppNotifications`, que o `Program.Main` do WinForms registra com as mesmas chamadas de `AppDialogs`
- [x] **Caminhos de dados (`AppPaths`):** todos os `AppContext.BaseDirectory` dos serviços (e o `%LOCALAPPDATA%\WTF\ScanCache` do `ScanCacheService`) passam por `AppPaths`:
  - Windows: tudo ao lado do `.exe`, **exatamente como na v1.4.1**; o cache de varredura continua em `%LOCALAPPDATA%\WTF\ScanCache`. Testado: os caminhos resultantes são idênticos aos antigos
  - macOS: `~/Library/Application Support/c2flux` (configurações e dados) e `~/Library/Caches/c2flux` (cache)
  - Linux: `$XDG_CONFIG_HOME/c2flux`, `$XDG_DATA_HOME/c2flux` e `$XDG_CACHE_HOME/c2flux`, com os padrões `~/.config`, `~/.local/share` e `~/.cache` (valores relativos são ignorados, como manda a especificação XDG)
  - `C2FLUX_HOME` põe tudo numa pasta só, em qualquer sistema (instalação portátil no macOS/Linux, testes)
  - **Mudanças em relação ao plano:**
    - Classe estática em vez de interface `IAppPaths`, como os outros serviços do código; as regras são uma função pura testável em qualquer sistema
    - Sem a alternativa `%LOCALAPPDATA%\c2flux` quando a pasta do `.exe` não é gravável. O app pede para rodar como administrador: o mesmo usuário teria a pasta gravável numa execução e não na outra, e os dados alternariam entre dois lugares. O Windows fica idêntico à v1.4.1
    - Sem migração: no Windows nada muda de lugar, e no macOS/Linux não existe instalação antiga
- [x] `LocalizationService`: a pasta `Languages` era ao mesmo tempo os idiomas do app e onde o usuário instala idiomas novos (e de onde o app apaga arquivos antigos ao iniciar), o que não funciona num `.app` somente leitura. Agora são duas: idiomas do app em `ResourcesDirectory/Languages` e do usuário em `DataDirectory/Languages`; a lista junta as duas, e o arquivo do usuário vence. No Windows são a mesma pasta
- [x] Projeto WinForms passa a referenciar o Core (os pacotes do SQLite chegam por ele). `c2flux-bench` e `c2flux-shots` procuram os tipos também nos assemblies `c2flux.*` referenciados, funcionando com a `baseline-winforms` e com o layout novo
- [x] Testes do Core em `tests/c2flux.Core.Tests` (11 testes), no CI **nos três SOs** (job `core-tests`): regras do `AppPaths` por sistema, idiomas em duas pastas, configurações indo e voltando do disco e a **regressão do bug do histórico** da Fase 0.3 (falha com o código antigo, passa com o novo)
- [x] **Bug pré-existente nas cores do Treemap e do Sunburst, corrigido** (encontrado ao comparar as capturas da Fase 1): a cor de cada família e o tom de cada bloco vinham de `StringComparer.OrdinalIgnoreCase.GetHashCode()`, que o .NET torna aleatório a cada processo. A mesma pasta aparecia com cores diferentes a cada vez que o app abria (nas execuções anteriores, as cores coincidiram por acaso). Corrigido com um hash estável (FNV-1a sobre o nome em maiúsculas) em [AntdThemeService.cs](src/c2flux.WinForms/AntdThemeService.cs). As cores continuam vindo da mesma paleta, mas agora são iguais em toda execução; as referências do Treemap e do Sunburst precisam ser atualizadas
- [x] Confirmar no CI que o app Windows continua idêntico: build, testes do Core nos três SOs, benchmark sem regressão e capturas iguais às referências
  - Primeira execução (`dc0ae8d`): build e testes do Core ✅ nos três SOs; dados gravados ao lado do `.exe`, como antes; 40 das 42 capturas idênticas (diferenças ≤ 0,12%, só datas e espaço livre do runner); Treemap e Sunburst com cores trocadas (o bug acima); benchmark ✅ em todos os alvos menos `ntfsmft` no `C:`, reprovado por ruído (as rodadas medidas das duas versões ficaram todas no patamar lento, 15–30 s, contra 6,3 s limpo)
  - Execução final (`18a6d38`): CI ✅ (build e testes do Core nos três SOs). Capturas: duas execuções do mesmo commit produziram imagens idênticas (cores estáveis); fora o Treemap e o Sunburst, que mudaram de cor pela correção, as demais iguais às referências, a menos de datas e espaço livre. Referências atualizadas para esse commit. Benchmark: a primeira tentativa reprovou o `ntfsmft` no `C:` mesmo medindo de novo (rodadas de 6,5 s a 47 s nas duas versões); a segunda amostra, num runner sem interferência, deu **6.135 × 6.131 ms (−0,1%)**, com todas as rodadas entre 6,1 e 6,2 s, e passou em todos os alvos. No `T:`, os três scanners ficaram de 11% a 18% mais rápidos
  - Por que só o `ntfsmft` no `C:` oscila tanto: ele lê a MFT com o buffer padrão do NtfsReader (pequeno), enquanto o `c2flux` usa 4 MB. São muito mais leituras pequenas no `C:` do runner, um disco de nuvem com limite de operações por segundo e compartilhado com o resto da máquina. O `T:` fica no disco local temporário e não sofre com isso. Avaliar na Fase 3.4 se o `ntfsmft` deve usar o mesmo buffer de 4 MB (beneficiaria usuários com discos lentos ou de rede)

**Entregável:** app Windows igual ao de hoje e Core compilando e passando nos testes em Windows, macOS e Linux.

**Situação:** ✅ concluída em 10/10/2026. De quebra, foi encontrado e corrigido mais um bug do original (cores do Treemap/Sunburst mudando a cada abertura) e o benchmark passou a medir de novo alvos com rodadas muito dispersas.

---

### Fase 2: Abstração da varredura

**Objetivo:** formalizar o contrato que os scanners atuais já seguem implicitamente e criar a cadeia de fallback por SO.

```csharp
public interface IFileSystemScanner
{
    string Name { get; }

    // Rápido e sem efeitos colaterais: SO, sistema de arquivos, permissões, tipo de caminho.
    ScannerSupport GetSupport(string rootPath);

    Task<FileSystemEntry> ScanAsync(
        string rootPath,
        ScanOptions options,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken,
        PauseToken pauseToken);
}

public sealed record ScannerSupport(bool IsSupported, string ReasonKey = null);

public sealed class ScanOptions
{
    public bool CrossMountPoints { get; init; }       // padrão: false
    public bool FollowSymlinks { get; init; }         // padrão: false (sempre)
    public SizeMode SizeMode { get; init; }           // Lógico ou AlocadoEmDisco
    public IReadOnlyList<string> ExcludedPaths { get; init; }
    public int? MaxDegreeOfParallelism { get; init; }
}
```

- [x] Criar `IFileSystemScanner` e `ScannerSupport` no Core (`src/c2flux.Core/Scanning/`). A interface ganhou também `StatusTextKey` e `FailureAlertKey`, porque cada scanner tem seu texto de status e seu aviso de falha; e `IScannerDiagnostics`, opcional, para os detalhes de desempenho do NtQuery. **Sem `ScanOptions` por enquanto:** nenhum scanner atual usaria essas opções (montagens, links, modo de tamanho), e as que existem vêm de `AppSettings`. Entra quando os scanners nativos da Fase 3 precisarem
- [x] Transformar `ScanExecutionController` em `ScannerPipeline`: tenta os scanners com suporte, em ordem; a falha de um registra o aviso dele e passa para o próximo; a do último é a falha da varredura; cancelamento nunca é falha. Mesmos textos de status, avisos e entradas "Performance" do log. O `ScanExecutionController` foi removido
- [x] Adaptar os scanners do Windows à interface (sem alterar a lógica interna), no projeto novo `src/c2flux.Platform.Windows` (os cinco arquivos movidos com `git mv`):
  - `C2FluxScanner` / `NtfsMftScanner` → `MftScanner` (os dois modos continuam escolhidos pela configuração `C2FluxScan`)
  - `NtQueryDirectoryScanner` → `NtQueryScanner`
  - `DirectoryScanner` → `Win32FindScanner`
- [x] Corrigir o uso direto de scanners fora do pipeline: os detalhes do histórico de armazenamento passam por `IStorageHistorySnapshotSource` (Core). No Windows, faz o que o `MainForm` fazia (captura pela MFT com `C2FluxScanner` e, se falhar, NtQuery sem montar a árvore); nos outros sistemas, usa a própria árvore da varredura
- [x] **Bugs pré-existentes no `DirectoryScanner`, corrigidos** (encontrados nas Fases 0.2 e 0.3): com a configuração padrão `SkipReparsePoints = true`, `_activeDirectoryIdentities` fica `null` e `ScanDirectoryContents` lança `NullReferenceException` (`DirectoryScanner.cs:248`). O último fallback de varredura do Windows nunca funcionava. Corrigido: a lista é sempre criada
  - Caminhos longos: a listagem chamava `FindFirstFileEx` sem o prefixo `\\?\`, então pastas com mais de ~260 caracteres eram registradas como puladas, sem conteúdo. No `T:` de teste faltavam 38 pastas e 37 arquivos (a cadeia `deep/` parava na profundidade 27 de 62). Corrigido usando o mesmo prefixo que o scanner já aplicava ao abrir pastas
  - Verificado no CI: depois das correções, o `win32find` encontra no `T:` exatamente as mesmas 304 pastas, 15.127 arquivos e 687.069.899 bytes do `ntquery` (profundidade 62), e o mesmo que ele no `C:\Program Files`
- [x] Extrair código duplicado entre scanners, **só o que era idêntico**: `DirectoryIdentity` (com a leitura nativa) era igual byte a byte no `DirectoryScanner` e no `NtQueryDirectoryScanner`, e a ordenação era igual no `DirectoryScanner` e no `NtfsMftScanner` (agora `ScanTree.SortChildrenBySizeDescending`). O `CompiledPathFilter` estava inteiro dentro de um comentário desde que o filtro de exclusões foi desativado: removido. As outras rotinas (ordenação do C2Flux e do NtQuery, retratos ao vivo, progresso) **diferem** entre os scanners; uni-las mudaria o comportamento, então ficam
- [x] **`ManagedScanner`** (fallback universal): `FileSystemEnumerable` com um grupo de workers, a mesma árvore dos scanners do Windows (`AllFiles`, "mostrar arquivos", tamanhos, ordem, progresso ao vivo). Links simbólicos aparecem como entradas de 0 bytes e nunca são seguidos; pastas sem permissão ficam vazias e são reportadas como puladas. No Windows é o último da cadeia (antes, uma falha do `DirectoryScanner` encerrava a varredura); no macOS e no Linux é, por enquanto, o único. Testes no Core com uma árvore real (link em loop, pasta sem permissão, arquivos ocultos da árvore). Limites, que os scanners nativos da Fase 3 resolvem: não detecta pontos de montagem nem sistemas de arquivos virtuais, e conta cada hardlink
- [x] **Suíte de conformidade:** roda todos os scanners disponíveis no SO sobre a árvore sintética da Fase 0 e compara com o manifesto, entrada por entrada (`c2flux-bench --dump` + `tests/conformance/check_conformance.py`, workflow `scanner-conformance.yml` nos três SOs)
  - No macOS (local), o `ManagedScanner` bate exatamente com o manifesto: nenhum arquivo faltando ou a mais, nenhum tamanho, data ou tipo diferente; o conteúdo da pasta sem permissão não aparece e os links aparecem como arquivos de 0 bytes
  - [x] Primeira execução no CI (`f5d4729`): `managed` passou nos três SOs; `ntquery` passou; encontrou **dois bugs do original**, corrigidos:
    - **Scanners de MFT com hardlinks** (NtfsReader): um arquivo com vários links aparecia uma vez só, com o nome de um link na pasta de outro (`hardlinks/c/original.bin` no lugar de `a/original.bin`, `b/link-1.bin` e `c/link-2.bin`), e as pastas dos outros links ficavam vazias. O leitor pegava a pasta-mãe de todos os `$FILE_NAME`, mas o nome só do primeiro. Agora os dois vêm do mesmo atributo. O arquivo continua contado uma vez, o que corresponde ao espaço em disco. Afeta o uso real: o Windows usa muitos hardlinks (`C:\Windows\WinSxS`)
    - **`DirectoryScanner` com datas no ano ~3620**: `LastWriteTimeUtcTicks` já guarda ticks do `DateTime`, mas era passado a `DateTime.FromFileTimeUtc`, que soma de novo o deslocamento de 1601
  - [x] Regras (o relatório reprova o job): nenhuma entrada a mais; nenhuma diferença de tamanho, data ou tipo; pastas com a soma dos filhos; nenhuma entrada faltando; links simbólicos podem aparecer de formas diferentes, mas nunca são seguidos (0 bytes, sem filhos). Por scanner: hardlinks listados um a um (`each`) ou uma vez (`once`, MFT); pasta sem permissão ausente (`enforced`) ou visível (`ignored`, MFT, que lê o volume diretamente). Aplicadas aos dumps da primeira execução, reprovam exatamente os dois bugs acima e mais nada
  - [x] Confirmado no CI (`87ae89b`): os 5 scanners do Windows e o `managed` no macOS e no Linux passam
  - NtfsReader, de quebra: o nome escolhido para cada arquivo agora segue a prioridade Win32 > POSIX > DOS (o leitor não lê registros de extensão, e o nome longo pode estar num deles, deixando só o 8.3 no registro base), e "ainda sem nome" deixou de ser `NameIndex == 0`, que é um nome válido
  - Definir a semântica esperada antes de exigir resultados iguais. Na Fase 0.2, os scanners já divergem no mesmo disco: o `ntquery` soma só o que o usuário consegue ler e conta symlinks de arquivo como arquivos de 0 bytes, enquanto os de MFT ignoram permissões e incluem os arquivos internos do NTFS e a `System Volume Information` (+31,5 MB num disco de 690 MB). Ver `docs/benchmarks/README.md`
  - Mesmo quando os totais batem, as impressões digitais diferem (`ntquery` × `win32find` no `T:` e no `C:\Program Files`). Elas incluem a data de modificação e o tamanho de cada pasta, e um dos dois provavelmente não preenche esses campos para pastas. Para investigar, o `c2flux-bench` precisa de um modo que grave a listagem entrada por entrada
- [x] **Benchmark:** primeira execução (`f5d4729`) sem regressão em nenhum alvo; o `ManagedScanner` no Windows ficou **mais rápido que o NtQuery** (40 × 45 ms no `T:`, 1.493 × 1.549 ms no `C:\Program Files`). A árvore diferente da referência num alvo estático deixou de reprovar e virou nota: a correção agora é verificada pela conformidade, e correções de bugs mudam a árvore de propósito (a `baseline-winforms` mantém os bugs). Com as correções (`87ae89b`): sem regressão; no `T:` os scanners ficaram de 9% a 14% mais rápidos que a referência, e o `managed` foi o mais rápido (39 ms). Antes: o `ManagedScanner` entrou no workflow comparativo (`T:` e `C:\Program Files`), com o veredicto novo 🆕 porque não existe na `baseline-winforms`. **Sem BenchmarkDotNet:** o `c2flux-bench` já mede cada scanner num processo novo, compara as duas versões no mesmo job e trata o ruído do runner; o BenchmarkDotNet não acrescentaria nada a isso. Falta confirmar no CI que não houve regressão

**Cadeias de fallback:**

| SO | 1º | 2º | 3º | Final |
|---|---|---|---|---|
| Windows | `MftScanner` (NTFS, raiz de unidade, admin) | `NtQueryScanner` | `Win32FindScanner` | `ManagedScanner` |
| macOS | `GetAttrListBulkScanner` | — | — | `ManagedScanner` |
| Linux | `IoUringStatxScanner` (opcional, kernel ≥ 5.6) | `GetDentsStatxScanner` | — | `ManagedScanner` |

**Entregável:** pipeline de varredura plugável, `ManagedScanner` funcionando nos três SOs e suíte de conformidade verde.

**Situação:** ✅ concluída em 10/10/2026. Capturas da interface iguais às referências (só datas variam). De quebra, mais dois bugs do original corrigidos: caminho errado de arquivos com hardlinks nos scanners de MFT e datas no ano ~3620 no `DirectoryScanner`.

---

### Fase 3: Scanners nativos de alto desempenho

#### 3.1 macOS: `GetAttrListBulkScanner`

- [x] Medir antes: o `ManagedScanner` ficou de 1,2× a 7× atrás do `du -sk` (`/usr`: 505 × 67 ms; `/System/Library`: 9,7 × 2,6 s), o que justificou o scanner nativo
- [x] `src/c2flux.Platform.MacOS/Scanning/GetAttrListBulkScanner.cs`: `open` + `getattrlistbulk` em lotes num buffer de 256 KB (`ArrayPool`), lendo nome, tipo, data e tamanho lógico (`ATTR_FILE_DATALENGTH`, o mesmo `st_size` do `managed`). Cadeia do macOS: `GetAttrListBulkScanner` → `ManagedScanner` (`MacScanners`)
- [x] **Sem duplicar código:** o `ManagedScanner` ganhou uma função de leitura de pasta trocável (`DirectoryReader`). O scanner do macOS só troca essa leitura e reaproveita os workers, a árvore, os tamanhos e o progresso
- [x] Não atravessa outros volumes: antes de ler cada pasta, compara o devid dela (`fgetattrlist`) com o da raiz. Verificado: varrendo `/Volumes`, o `SSD` (outro volume) fica vazio, e `Macintosh HD` (link para `/`) não é seguido
- [x] **Firmlinks do APFS:** o `stat` mostra `/` e `/System/Volumes/Data` com o **mesmo** devid, então a regra de volume não basta. Ao varrer `/`, a pasta `/System/Volumes/Data` não é lida, porque o volume de dados já aparece pelos firmlinks da raiz (`/Users`, `/Applications`…). Não testado varrendo `/` de verdade, para não disparar os pedidos de privacidade do macOS
- [x] Resultado: dump da árvore de teste **idêntico** ao do `managed`; de 2× a 6× mais rápido que ele, no nível do `du` (números em `docs/benchmarks/README.md`). Conformidade no CI do macOS
- [ ] ~~Paralelismo próprio, buffer por thread~~: desnecessário, vem do motor do `managed`
- [ ] **Adiado:** hardlinks contados uma vez e tamanho alocado (`ATTR_FILE_LINKCOUNT`/`ALLOCSIZE`). Hardlinks são raros no macOS; o tamanho alocado só faz sentido quando a interface tiver esse modo de exibição (`shortcut:` no código)
- [ ] **Adiado para a Fase 5:** aviso de **Acesso Total ao Disco** com botão para os Ajustes. O scanner já reporta as pastas protegidas como puladas (`EPERM`); falta a interface
- [ ] ~~Ignorar volumes de sistema somente leitura e snapshots~~: coberto pela regra de volume (VM, Preboot, Update etc. têm devid próprio)

#### 3.2 Linux: `LinuxScanner` (no lugar do `GetDentsStatxScanner`)

- [x] **Medir antes:** no Ubuntu do CI, o `ManagedScanner` **já é mais rápido que o `du`** em `/usr` (2,1 × 2,6 s, 629 mil arquivos, cache aquecido). O `FileSystemEnumerable` do .NET no Linux já usa `getdents64` e `fstatat`, e o motor do `managed` lê várias pastas em paralelo. Um scanner com `getdents64` + `statx` próprios não se paga: descartado
- [x] O que faltava no Linux era **correção**: varrendo `/`, o `managed` entraria em `/proc`, `/sys`, `/dev`, `/run` e em outros discos. `src/c2flux.Platform.Linux/Scanning/LinuxScanner.cs` lê `/proc/self/mountinfo` uma vez por varredura e não entra em nenhum ponto de montagem abaixo da raiz (como o `du -x`). Os sistemas de arquivos virtuais também são montagens, então a mesma regra os cobre, sem lista de tipos. A leitura das pastas continua sendo a do `managed`. Cadeia do Linux: `LinuxScanner` → `ManagedScanner` (`LinuxScanners`)
- [x] Testes do parser do `mountinfo` (incluindo os caminhos com espaço, que vêm em octal) e da regra de montagem, nos três SOs. No CI do Ubuntu: conformidade com o `LinuxScanner` e varredura de `/`, que reprova se houver qualquer entrada dentro de `/proc`, `/sys`, `/dev` ou `/run`
- [x] Confirmado no CI (`1ffc4d7`): conformidade do `LinuxScanner` ✅; varredura de `/` no Ubuntu com 905.989 arquivos e 147.089 pastas em 20,9 s, sem nenhuma entrada dentro de `/proc`, `/sys`, `/dev` ou `/run`
- [ ] **Adiado:** hardlinks contados uma vez e tamanho alocado (`stx_blocks`), pelos mesmos motivos do macOS

#### 3.3 Linux: `IoUringStatxScanner`

- [ ] ~~`statx` em lote via `io_uring`~~: **descartado** junto com o `GetDentsStatxScanner`, já que o `managed` está à frente do `du`. Reavaliar só se aparecer um caso real lento (disco de rede, HDD)

#### 3.4 Windows: ajustes

- [x] Manter a MFT como caminho principal, **com um scanner só**. O `NtfsMftScanner` (o original) e o `C2FluxScanner` (opcional desde a v1.2.38, pela opção "c²flux Scan") chegavam ao mesmo resultado; o c2flux fazia isso cerca de 2× mais rápido e com metade da memória, mas não mostrava a árvore crescendo durante a varredura. Decisão: removidos o `NtfsMftScanner` e a opção "c²flux Scan"; o `C2FluxScanner` passou a mandar a árvore ao vivo (raiz e os primeiros 100 filhos), a cada 100 mil arquivos montados e no final. Como ele primeiro lê toda a MFT e só depois monta a árvore, a árvore aparece a partir da montagem, não durante a leitura da MFT. O teste de suporte (NTFS fixo + administrador) foi para o `C2FluxScanner`. Nas Configurações, as linhas abaixo da opção subiram 36 px. Um `settings.json` com `C2FluxScan` continua carregando (a propriedade é ignorada)
- [x] Confirmado no CI (`c3fbce9`): benchmark do c2flux sem regressão (−1,1% no `C:`, −10,3% no `T:`: os retratos ao vivo não custam nada mensurável); conformidade ✅; Configurações sem a opção e sem buraco no layout. Referências visuais atualizadas para esse commit
- [ ] ~~Suporte a `win-arm64`~~: **movido para a Fase 6** (publicar e testar o RID é empacotamento). O NtfsReader é .NET puro, e as chamadas nativas são as mesmas
- [x] Avaliar ReFS / exFAT: caem no NtQuery, o que está correto (a MFT só existe no NTFS)

**Entregável:** varredura nativa e paralela nos três SOs, validada pela suíte de conformidade e com benchmarks publicados.

**Situação:** ✅ concluída em 10/10/2026. macOS com scanner nativo (`getattrlistbulk`, de 2× a 6× mais rápido que o `managed`); Linux sem scanner nativo, porque o `managed` já é mais rápido que o `du`, mas sem atravessar montagens; Windows com um scanner de MFT só. Adiados: hardlinks contados uma vez e tamanho alocado no macOS e no Linux, aviso de Acesso Total ao Disco (Fase 5) e `win-arm64` (Fase 6).

---

### Fase 4: Serviços de plataforma

**Objetivo:** abstrair tudo que não é varredura mas depende do SO.

| Interface | Windows | macOS | Linux |
|---|---|---|---|
| `IVolumeProvider`: listar unidades/volumes, rótulo, sistema de arquivos, total/livre | `DriveInfo` + `GetDiskFreeSpace` | `getmntinfo` / `statfs`, filtrando volumes de sistema e mostrando "Macintosh HD" | `/proc/self/mountinfo` + `statvfs`, filtrando pseudo-FS e snaps/loops |
| `IFileManager`: "mostrar no gerenciador", "abrir" | `explorer.exe /select,` | `open -R` / `NSWorkspace` | D-Bus `org.freedesktop.FileManager1.ShowItems`, com fallback `xdg-open` na pasta pai |
| `IShellContextMenu`: menu de contexto nativo | Código atual (`NativeShellContextMenu`) | Menu próprio: Mostrar no Finder, Abrir, Copiar caminho, Mover para o Lixo, Informações | Menu próprio: Abrir, Abrir pasta, Copiar caminho, Mover para a Lixeira |
| `IFileIconProvider`: ícones por tipo | `SHGetFileInfo` (código atual) | `NSWorkspace.iconForFile` via interop Objective-C | Tema de ícones freedesktop por tipo MIME (`shared-mime-info`), com conjunto de ícones próprio como fallback |
| `ITrashService`: excluir para a lixeira | `SHFileOperation` / `IFileOperation` | `NSFileManager.trashItem` | Especificação freedesktop Trash (`~/.local/share/Trash`) ou `gio trash` |
| `IPrivilegeService`: elevação | `runas` (código atual) | Não aplicável (orientar sobre Acesso Total ao Disco) | Opcional: `pkexec` para varrer pastas de root |
| `IThemeDetector`: claro/escuro do SO | Registro (código atual) | Fornecido pelo Avalonia (`PlatformSettings`) | Fornecido pelo Avalonia (portal freedesktop) |
| `IAppPaths`: pastas de dados | Fase 1 | Fase 1 | Fase 1 |

**Versão enxuta (decisão de 10/10/2026):** só o que a Fase 5 com certeza vai usar e que dá para testar sem interface. O resto fica para quando uma tela precisar, ou sai do plano.

- [x] **Análise de duplicados portátil:** o `RedundancyAnalysisService` foi para o Core. Ele pulava todo arquivo cujo ID do Windows não pudesse ler, então fora do Windows não acharia nenhum duplicado. A identidade de arquivo virou um ponto de extensão (`FileIdentities.Reader`): Windows com o código de antes (ID de 128 bits + USN, `WindowsFileIdentity`), macOS com `getattrlist` (`MacFileIdentity`) e Linux com `statx` (`LinuxFileIdentity`), todos seguindo links. O padrão, sem leitor registrado, usa o caminho: funciona, mas hardlinks aparecem como duplicados. Testado de ponta a ponta no macOS: acha o par duplicado e conta o hardlink como a mesma cópia
- [x] **Volumes** (`Volumes.List()` no Core, no lugar de `IVolumeProvider`): o `DriveInfo` do .NET já funciona nos três SOs, e o que muda é quais montagens mostrar. macOS: `/` e `/Volumes/*`, com o nome do volume de inicialização ("Macintosh HD") tirado do link em `/Volumes`; Linux: sem sistemas virtuais, imagens (`squashfs`, `overlay`) nem `/proc`, `/sys`, `/dev`, `/run`, `/snap`; Windows: como hoje. As regras são funções puras testadas em todos os SOs. Neste Mac a lista dá exatamente "Macintosh HD" e "SSD"
- [x] **Gerenciador de arquivos** (`FileManager.Reveal`/`Open` no Core, no lugar de `IFileManager`): `explorer.exe /select,` no Windows, `open -R` no macOS e, no Linux, D-Bus `FileManager1.ShowItems` com `xdg-open` na pasta-mãe como alternativa. Comandos testados nos três SOs; não executados aqui, para não abrir janelas na máquina do usuário
- [ ] **Para a Fase 5**, quando a tela precisar: ícones de arquivo, menu de contexto, tema claro/escuro (o Avalonia já detecta nos três SOs), tamanho de cluster na barra de status e **elevação no Windows** (pedido para rodar como administrador, hoje no `Program.cs` do WinForms; sem ele a MFT não roda e o Windows cai sempre no NtQuery, mais lento). No macOS e no Linux não há elevação: o app roda sem privilégios
- [ ] ~~Lixeira~~: **fora do porte**, não esquecida: o app original não exclui arquivos em nenhuma tela, então seria um recurso novo. Pode entrar depois como item à parte
- [ ] ~~Substituir as chamadas do WinForms pelas interfaces~~: **desnecessário**: o WinForms só roda no Windows e será removido na Fase 7; adaptá-lo seria trabalho num código que vai ser apagado
- [x] Confirmado no CI (`a5f1568`): 33 testes do Core nos três SOs (incluindo identidade com hardlink no Linux e a lista de volumes de cada runner); conformidade, benchmark e capturas sem mudança (aba Redundâncias idêntica)

**Entregável (revisto):** análise de duplicados, lista de volumes e gerenciador de arquivos funcionando nos três SOs, prontos para a Fase 5.

**Situação:** ✅ concluída em 10/10/2026, na versão enxuta.

---

### Fase 5: Nova interface em Avalonia (`c2flux.App`)

**Objetivo:** recriar a interface com a maior fidelidade possível à atual.

#### 5.1 Fundação

- [x] Projeto Avalonia com MVVM leve (CommunityToolkit.Mvvm): `src/c2flux.App`, ao lado do WinForms até a Fase 7. **Desvio:** Avalonia **12.1.3** em vez de 11 — a 12 já era a estável mais recente (a 12.1.4 tinha 1 dia e ficou para depois). Testes em `tests/c2flux.App.Tests` (plataforma *headless* do Avalonia, xunit v3), no CI nos três SOs
- [x] **Tema Ant Design:** paleta (claro e escuro) e raios (6 px nos controles, 8 px em popups) de `AntdThemeService.cs` em `Themes/AntTokens.axaml`, fonte de 12 px (Segoe UI 9 pt). **Decisão:** sem Semi.Avalonia (outra linguagem visual, que teria de ser desfeita); a base é o `FluentTheme` que já vem no Avalonia, recolorido pela paleta. Os `ControlTheme` com os estados (hover, foco, pressionado, desabilitado) de cada controle são escritos nas Fases 5.3 e 5.4, à medida que os controles são portados e comparados com as referências
- [x] Temas claro, escuro e "seguir o sistema" (`AppLayout`: `WindowsDefault` segue o SO). O `AppSettings` do Core deixou de forçar o escuro; quem força agora é o WinForms, que só tem o escuro pronto
- [x] Integração do `LocalizationService` com binding: `{l:T Chave}` no XAML, atualizado pelo evento `LocalizationService.LanguageChanged`
- [x] **RTL:** árabe, hebraico, persa e urdu (`LocalizationService.IsRightToLeft`) viram `FlowDirection.RightToLeft` em todas as janelas. Espelhamento de cada tela: conferir nas capturas da Fase 5.3 em diante
- [x] Fontes: **Selawik** embutida (SIL OFL, da Microsoft, com as métricas da Segoe UI do WinForms: larguras de texto iguais às do original em todos os SOs). Na 5.1 era a Inter, ~15% mais larga, o que quebrava a barra de ferramentas em duas linhas; a troca baixou a diferença de todos os gráficos. CJK, tailandês e devanágari usam o *fallback* automático para fontes do sistema. No Linux, o pacote da Fase 6 deve depender de `fonts-noto-cjk`
- [x] Ícone do app (o mesmo `c2flux.png`/`.ico` do WinForms) e decorações nativas da janela. Barra customizada: não por enquanto; reavaliar depois das capturas da Fase 5.3
- [x] Menu de aplicativo nativo no macOS: Sobre e Ajustes (⌘,) no menu do app, desabilitados até as janelas existirem (Fase 5.4); Ocultar, Serviços e Sair (⌘Q) o Avalonia já adiciona. Atalhos da Fase 5.3: usar `PlatformHotkeyConfiguration.CommandModifiers` (⌘ no macOS, Ctrl nos outros), nunca `Ctrl` fixo

#### 5.2 Gráficos (desenho customizado) ✅

Portar de GDI+ para `DrawingContext` do Avalonia (ou SkiaSharp direto, se for preciso mais desempenho). A lógica de layout (algoritmo de treemap, ângulos do sunburst, escalas) é reaproveitada quase sem mudanças.

- [x] `TreeEntrySizeBarView` → `EntryTree`: árvore inteira (seleção, teclado, expandir, rolagem), desenhando só as linhas visíveis. Ícones de arquivo, pasta e unidade ficam para o serviço de ícones da Fase 5
- [x] `Chart_Treemap` → `Treemap`: o canvas (layout squarified, famílias, rótulos, "Other (n)", hover, clique, duplo clique para zoom, menu de contexto), com cache em bitmap como no original. Diferença média de 7 níveis por pixel contra a referência, quase toda na fonte. A tabela de cima vai para a 5.3, com as outras tabelas
- [x] `Chart_Sunburst` → `Sunburst`. O `PathGradientBrush` do GDI+ virou gradiente radial (quase idêntico na comparação)
- [x] `Chart_PieChart` → `PieChart`
- [x] `Chart_BarChart` → `BarChart` (espaço do ícone vazio até o serviço de ícones)
- [x] `StorageHistoryChart`
- [x] `ScanHistoryGrowthOverviewControl` → `GrowthOverview` (o seletor de visão usa o `ComboBox` do Fluent até o tema dos controles da 5.3)
- [x] `StatusSymbolRenderer`
- [x] **Comparação:** `tests/fixtures/generate_chart_fixture.py` gera uma árvore fixa; `c2flux-shots --charts` desenha cada gráfico do WinForms com ela (`docs/fidelity/charts/`) e os testes do app desenham o port com os mesmos dados (`ChartCaptures`). Achados: o WinForms mistura transparência em luz linear (`ChartColors.BlendLinear`); o texto segue as métricas da Segoe UI e do `TextRenderer` (`DrawnControl`); com `PixelOffsetMode.Half`, um traço de 1 px em *c* acende o pixel *c* − 1; o destaque do sistema é `#0078D4`. Diferença média de 1 a 7 níveis por pixel em todos os gráficos (`docs/fidelity/README.md`)
- [x] Virtualização e cache de renderização para árvores com milhões de entradas: a árvore desenha só as linhas visíveis (200 mil filhos expandidos em 215 ms, eram 2 s antes de medir a largura só pelos textos mais longos); o treemap guarda o desenho num bitmap (redesenho em 0 ms) e limita 160 blocos por pasta (1 milhão de arquivos em ~0,6 s, como o algoritmo original). Medido no macOS, headless, Release

#### 5.3 Janelas e controles

Ordem sugerida: o que é visto primeiro vem antes.

- [x] Tabelas: `DrawnTable<T>` próprio (decisão: sem `DataGrid`, descontinuado, nem `TreeDataGrid`/`TableView`, pagos), desenhado como o `AntdUI.Table` — cabeçalho fixo, ordenação em três cliques, colunas redimensionáveis, hover, seleção, teclado, tooltip de texto cortado, só as linhas visíveis desenhadas. `EntryTable` (`Chart_TableGridChart`) e `TreemapView` (o `Chart_Treemap` inteiro: caminho, tabela, divisor e treemap). Diferença média contra a referência: 1,4 e 4,4 níveis por pixel. O `Chart_ResponsiveTableGrid` (base dos resultados da busca) vira um `DrawnTable` junto com o `SearchForm`
- [ ] `MainForm`: layout principal, barra de ferramentas, seletor de unidade, árvore, painel de gráficos, barra de status (`LayoutMainFormController`, `StatusMainFormController`, `TreeEntryController`, `ExportEntryController`, `PartitionGridController`, `DriveComboBoxController`)
  - [x] Janela, menu (nativo no macOS, dentro da janela no Windows/Linux), barra de ferramentas com os ícones desenhados, árvore, painel de partições, visualizações e barra de status (contadores de alerta, resumo com tamanho de cluster, progresso)
  - [x] Varredura: sessões por unidade, progresso, árvore ao vivo, pausar e cancelar, histórico de armazenamento (com detalhes) e de varreduras, aviso de pastas puladas. Teste headless varre uma pasta de ponta a ponta
  - [x] Achado: `DriveInfo.GetDrives` em paralelo derruba o processo no macOS (`getmntinfo` não é seguro entre threads); `Volumes.List` serializa as chamadas
  - [x] Menu de contexto da árvore (o próprio do app; o do Explorer ficou fora, Fase 4), exportação CSV e cópias, salvar e carregar varredura (diálogos nativos), argumento de inicialização, botões da barra ocultáveis (menu de contexto) e grupos reordenáveis pela alça
  - [x] Sobre e verificação de atualização
  - [x] Ícones de arquivo, pasta e unidade: shell do Windows (`SHGetFileInfo`), `NSWorkspace` no macOS, desenhados no Linux (sem tema de ícones freedesktop por enquanto)
  - [x] Elevação no Windows (iniciar elevado, prompt de elevação) e aviso de Acesso Total ao Disco no macOS (uma vez por execução, ao varrer algo que inclui a pasta pessoal, com botão para os Ajustes)
  - [ ] Visões embutidas: Análise (`AdvancedFeaturesForm`) e Histórico de armazenamento (`StorageHistoryForm`); hoje os botões ficam desabilitados
- [ ] `SearchForm`: busca rápida
- [x] `SettingsForm` → `SettingsWindow` (abas Geral, UI, Histórico, Exportar e Log; menu Configurações e ⌘, no macOS; as opções só do Windows somem nos outros SOs e as linhas sobem). Corrigido: os campos do histórico de varredura obsoleto ficavam por cima dos detalhes na aba Histórico e saíram; o painel de partições e a altura das barras agora seguem a configuração
- [ ] `ScanHistoryForm`
- [ ] `StorageHistoryForm` + `StorageHistoryDetailsForm`
- [ ] `AdvancedFeaturesForm`: análise, redundância
- [x] `AlertHistoryForm` → `AlertHistoryWindow`, com o estilo clássico do `DrawnTable` (o `DataGridView` temático) e seleção múltipla; abre pelos contadores da barra de status
- [x] `AboutForm`, `UpdateAvailableForm` (Sobre pelo menu Ajuda e pelo menu do app no macOS; verificação de atualização ao iniciar)
- [x] `DatabaseMoveForm`, `DebugClassForm` → `DatabaseMoveWindow`, `DebugClassWindow` (abrem pela `SettingsForm`; ligar quando ela for portada)
- [x] `AppDialogs`: aviso com OK, aviso com Sim/Não, pergunta Sim/Não, aviso Repetir/Cancelar e prompt de elevação, com os ícones do Windows desenhados
- [ ] `AppFileDialog`: **substituir** pelo `StorageProvider` nativo do Avalonia (diálogos nativos de cada SO), conforme decidido na seção 9

#### 5.4 Validação de fidelidade

- [ ] Capturas de tela da nova UI no Windows comparadas lado a lado com as referências da Fase 0
- [ ] Checklist por tela: layout, cores, fontes, ícones, estados, comportamento de redimensionamento
- [ ] Testes de UI headless (`Avalonia.Headless`) para fluxos principais: varrer, navegar, buscar, exportar

**Entregável:** nova UI com paridade funcional, rodando nos três SOs.

---

### Fase 6: Empacotamento, distribuição e atualização

- [ ] **CI em matriz** (`windows-latest`, `macos-latest`, `ubuntu-latest`): build, testes e conformidade de scanners em cada push
- [ ] **Publicação por RID**, self-contained (sem exigir .NET instalado), com trimming. Avaliar Native AOT (Avalonia suporta)

| SO | RIDs | Formatos |
|---|---|---|
| Windows | `win-x64`, `win-arm64` | ZIP portátil (como hoje) + instalador opcional (MSIX ou Inno Setup) |
| macOS | `osx-arm64`, `osx-x64` → app universal via `lipo` | `.app` em `.dmg` com assinatura *ad-hoc*, sem notarização (seção 9) + instruções de primeira abertura no README |
| Linux | `linux-x64`, `linux-arm64` | AppImage (principal), `.deb`, `.rpm`, opcional Flatpak / Flathub |

- [ ] Adaptar `release.yml` para gerar todos os artefatos e anexá-los à release
- [ ] **`GitHubUpdateService`:** escolher o asset certo por SO/arquitetura e manter a lógica atual de notificação
- [ ] Integração com o desktop:
  - Linux: arquivo `.desktop`, ícones em vários tamanhos, metainfo AppStream
  - macOS: `Info.plist` (nome, ícone `.icns`, versão, `NSHumanReadableCopyright`)
- [ ] Atualizar README, `docs/Troubleshooting.md` e o site (`docs/index.html`) com instruções por SO

**Entregável:** releases automáticas para os três SOs a partir de uma única tag.

---

### Fase 7: Paridade, transição e lançamento 2.0

- [ ] Matriz de funcionalidades (seção 5) completamente marcada
- [ ] Período beta público (`v2.0.0-beta.N`) com builds para os três SOs
- [ ] Coleta de feedback, sobretudo de usuários Windows, para detectar regressões de fidelidade ou desempenho
- [ ] Remover o projeto `c2flux.WinForms` e as dependências do AntdUI
- [ ] Lançar `v2.0.0`

---

## 5. Matriz de funcionalidades

Legenda: ✅ igual ao atual · 🟡 adaptado ao SO · ⛔ não se aplica

| Funcionalidade | Windows | macOS | Linux |
|---|---|---|---|
| Varredura de unidade/pasta | ✅ MFT / NtQuery | 🟡 getattrlistbulk | 🟡 getdents64 + statx |
| Pausar / cancelar varredura | ✅ | ✅ | ✅ |
| Árvore com barras de tamanho | ✅ | ✅ | ✅ |
| Treemap / Sunburst / Pizza / Barras / Tabela | ✅ | ✅ | ✅ |
| Busca rápida | ✅ | ✅ | ✅ |
| Histórico de varreduras e comparação | ✅ | ✅ | ✅ |
| Histórico de armazenamento | ✅ | ✅ | ✅ |
| Análise de redundância (duplicados) | ✅ | ✅ | ✅ |
| Exportação CSV | ✅ | ✅ | ✅ |
| 30 idiomas, incluindo RTL | ✅ | ✅ | ✅ |
| Tema claro/escuro/sistema | ✅ | ✅ | ✅ |
| Menu de contexto | ✅ shell nativo | 🟡 menu próprio | 🟡 menu próprio |
| Ícones de arquivo do sistema | ✅ | 🟡 NSWorkspace | 🟡 tema freedesktop |
| Mostrar no gerenciador de arquivos | ✅ Explorer | 🟡 Finder | 🟡 FileManager1 / xdg-open |
| Elevação de privilégios | ✅ runas | ⛔ (Acesso Total ao Disco) | 🟡 pkexec (opcional) |
| Verificação de atualização | ✅ | ✅ | ✅ |
| Modo portátil | ✅ | ⛔ | 🟡 AppImage |

---

## 6. Riscos e mitigações

| Risco | Impacto | Mitigação |
|---|---|---|
| Fidelidade visual menor que o esperado (AntdUI não existe para Avalonia) | Alto | Capturas de referência na Fase 0, checklist por tela, tema portado diretamente dos valores do `AntdThemeService` |
| Varredura no macOS/Linux mais lenta que a MFT no Windows | Médio | Inevitável: não existe equivalente à MFT. Comunicar claramente. Meta: ficar no nível das melhores ferramentas nativas (DaisyDisk, gdu, dua) |
| Divergências entre scanners (tamanhos, hardlinks, permissões) | Alto | Suíte de conformidade obrigatória no CI |
| Treemap com milhões de itens lento no novo renderizador | Médio | Cache de renderização, desenho em bitmap fora da thread de UI, SkiaSharp direto se necessário |
| Permissões do macOS (TCC) confundindo o usuário | Médio | Detecção automática e aviso com atalho para os Ajustes |
| App de macOS sem notarização (decisão da seção 9) | Médio | Usuários veem o aviso do Gatekeeper na primeira abertura. Mitigar com instruções claras com imagens no README e no site, e reavaliar a conta Apple Developer se a adoção no macOS crescer |
| Fragmentação do Linux (distros, sistemas de arquivos, DEs) | Médio | AppImage como formato principal, `ManagedScanner` como fallback, testes em Ubuntu, Fedora e Arch |
| Fork independente fica sem as correções do projeto original | Baixo/Médio | Acompanhar as releases do upstream e trazer correções relevantes pontualmente (*cherry-pick*), principalmente nos scanners do Windows |
| Licenças | Baixo | Projeto GPL-3.0 (fork permitido, deve continuar GPL e manter créditos). NtfsReader é LGPL-2.1. Avalonia e SkiaSharp são MIT, compatíveis |

---

## 7. Critérios de "100% multiplataforma"

O projeto é considerado concluído quando:

1. Uma única tag gera builds para Windows, macOS e Linux, sem passos manuais.
2. Nenhum arquivo fora de `c2flux.Platform.*` contém P/Invoke, Registro ou caminhos específicos de SO.
3. A suíte de conformidade de scanners passa nos três SOs.
4. Todas as linhas da matriz de funcionalidades estão marcadas.
5. A versão Windows não regrediu em desempenho de varredura (workflow comparativo da Fase 0.2 contra `baseline-winforms`, confirmado em hardware real) nem em funcionalidades.
6. As capturas de tela da nova UI no Windows passaram pelo checklist de fidelidade.

---

## 8. Ordem de execução recomendada

```
Fase 0 ──► Fase 1 ──► Fase 2 ──┬──► Fase 3 (scanners nativos) ──┐
                               │                               ├──► Fase 6 ──► Fase 7
                               └──► Fase 4 ──► Fase 5 (UI) ────┘
```

As Fases 3 e 4/5 podem andar em paralelo depois da Fase 2: os scanners nativos não dependem da nova UI, e a nova UI pode ser desenvolvida usando o `ManagedScanner` enquanto os nativos ficam prontos.

**Primeiro marco visível:** Fases 1 + 2 + um esqueleto da Fase 5 (janela principal com árvore e Treemap) rodando no macOS com o `ManagedScanner`.

---

## 9. Decisões

### Tomadas

- [x] **Fork ou upstream:** **fork independente.** Consequências: novo nome e ícone próprios (ver abaixo), créditos ao c² flux original e ao autor no README e na janela Sobre, licença GPL-3.0 mantida. A sincronização com o upstream deixa de ser obrigatória; correções relevantes do original podem ser trazidas pontualmente (*cherry-pick*)
- [x] **Diálogo de arquivos:** **nativo de cada SO** (`StorageProvider` do Avalonia). O `AppFileDialog` customizado não será portado
- [x] **Distribuição no macOS:** **sem conta Apple Developer.** O app é distribuído só com assinatura *ad-hoc* (exigida para rodar em Apple Silicon e aplicada automaticamente pelo `dotnet publish`), sem notarização. Na primeira abertura, o usuário precisa liberar em *Ajustes do Sistema → Privacidade e Segurança → Abrir mesmo assim*
- [x] **Tema base:** **`FluentTheme` do Avalonia recolorido com a paleta Ant**, sem Semi.Avalonia (ver Fase 5.1)

### Em aberto

- [ ] **Nome e ícone do fork:** necessário antes da primeira release pública (Fase 6)
- [ ] **Native AOT:** inicialização mais rápida e binário menor, mas exige revisar reflexão e serialização JSON (usar *source generators*)
- [ ] **Linux:** quais formatos além do AppImage (deb, rpm, Flatpak)?
- [ ] **io_uring:** vale a complexidade extra? Decidir com base nos benchmarks da Fase 3.2
