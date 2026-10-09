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

- [ ] ~~Instalar Windows 11 ARM numa VM (Parallels ou UTM) com o .NET 10 SDK~~ (adiado)
- [ ] ~~Documentar em `docs/dev/windows-vm.md` como compilar, rodar como administrador e executar a ferramenta de benchmark na VM~~ (adiado)

**Números de referência:**

- [x] Rodar o workflow uma vez só com a referência e guardar o resultado em `docs/benchmarks/baseline-winforms.json`, como registro histórico (não como limite de comparação, já que o hardware do CI muda). Resumo e observações em `docs/benchmarks/README.md`
- [ ] ~~Quando houver acesso a hardware real, registrar também esses números em `docs/benchmarks/`, com a especificação da máquina~~ (adiado)

#### 0.3 Referências visuais

- [ ] **Capturas de tela de referência** de todas as telas em `docs/fidelity/reference/`, a partir da tag `baseline-winforms`, para servir de comparação com a nova UI. Feitas **no CI** (a VM foi adiada):
  - [x] Ferramenta `c2flux-shots` (`tests/c2flux.Screenshots`): carrega o app por reflexão, como o `c2flux-bench`, abre cada janela e salva um PNG via `PrintWindow`, mais um `index.json` com o que deu certo e o que falhou. Janelas inesperadas (avisos, erros) também são capturadas
  - [x] Workflow `ui-screenshots.yml`: publica a versão escolhida, monta o disco `T:` com a árvore de teste, varre e captura. Manual (`workflow_dispatch`) ou automático quando a ferramenta muda
  - Telas: janela principal vazia e após varrer `T:\` (Tabela, Pizza, Barras, Sunburst, Treemap, Análise, Histórico de armazenamento), Busca, Configurações, Sobre, Histórico de alertas, Histórico de varreduras, Histórico de armazenamento, Mover banco de dados, Debug
  - **Só tema escuro:** na v1.4.1 o tema claro não é alcançável. O `AppSettings.Load()` força `WindowsDarkMode` e o seletor de tema nas Configurações fica oculto. A nova UI deve oferecer os dois, mas a referência de fidelidade existe só para o escuro
  - Resolução: a ferramenta pede 1920×1080 ao Windows. A escala de DPI fica a do runner (provavelmente 100%)
  - [x] Primeira execução no GitHub: as 19 capturas funcionaram (resolução trocada de 1024×768 para 1920×1080, DPI 96), com imagens nítidas e completas
  - [x] **Bug pré-existente no gráfico de pizza, corrigido** (encontrado na 0.3): itens de 0 bytes viram fatias de ângulo zero, e a fatia no topo do círculo (270°) tem largura 0. O `LinearGradientBrush` de [Chart_PieChart.cs:242](Chart_PieChart.cs#L242) lança `ArgumentException` e o app mostra a janela de exceção não tratada. Acontece com o `T:` de teste (`$BadClus`, `$Secure` e `$Volume` têm 0 B) e, provavelmente, com qualquer pasta cujos últimos itens tenham 0 bytes. Corrigido: fatias com área zero não são preenchidas. As referências passam a ser capturadas do `cross-platform` (original + correções de bugs, mesma interface)
  - [x] Capturas revisadas e salvas em `docs/fidelity/reference/` (16 telas, 796 KB), com descrição e limitações em `docs/fidelity/README.md`
  - [x] Telas restantes cobertas: as referências passaram de 16 para **42 capturas** (1,9 MB), revisadas uma a uma:
    - Menus da janela principal (File, View, Tools, Help) e menus de contexto da árvore e da barra de ferramentas, capturados sobre a janela principal. O da árvore é o menu próprio do app; o menu nativo do Explorer, usado normalmente no Windows, não será replicado (Fase 4)
    - Abas: as 5 das Configurações, as 4 da Análise e as 7 do Histórico de varreduras
    - Telas com dados: busca com resultados, histórico de alertas com entradas de exemplo, histórico de varreduras com duas varreduras comparadas (o volume é alterado entre elas) e histórico de armazenamento com dois registros e detalhes
    - Aviso de atualização (dados de exemplo) e os diálogos de `AppDialogs` (aviso, sim/não, pedido de administrador)
    - Fora: diálogos de arquivo (`AppFileDialog`), que serão substituídos pelos nativos (decisão da seção 9), e a aba "Colors" das Configurações, que o usuário não alcança (o botão nunca é adicionado à janela)
  - [x] **Bug pré-existente no histórico de varreduras, corrigido** (encontrado na 0.3): ao carregar uma varredura salva, `BuildRootEntry` ([ScanHistoryDatabaseService.cs](ScanHistoryDatabaseService.cs)) ordenava as entradas por `Depth`, que só é preenchido ao salvar e fica 0 ao ler do banco. Pastas cujo nome vem antes do da pasta-mãe em ordem alfabética (ex.: `dir-00067` dentro de `group-000`, `sparse` dentro de `tree`) eram descartadas com tudo o que contêm. No `T:` de teste, cada varredura voltava com 5.033 dos 15.147 arquivos, e "Changed files" ficava vazio. O banco estava correto. Corrigido montando a árvore da raiz para baixo; verificado localmente com o banco do runner: 15.147 arquivos e o `sparse-64MiB.bin` como alterado (+4 MB). Afetava toda comparação de varreduras (novos, excluídos, alterados e crescimento por pasta)

**Entregável:** workflow de benchmark comparativo funcionando no CI, números de referência e capturas de tela registrados.

**Situação:** ✅ concluída em 09/10/2026. Os ambientes locais (VM e hardware real) ficaram adiados. De quebra, foram encontrados e corrigidos quatro bugs do original: gráfico de pizza com itens de 0 B, `DirectoryScanner` sempre quebrando, `DirectoryScanner` sem caminhos longos e histórico de varreduras perdendo arquivos ao carregar

---

### Fase 1: Extração do núcleo (`c2flux.Core`)

**Objetivo:** separar tudo o que não é interface num projeto `net10.0` puro, sem mudar o comportamento no Windows.

- [ ] Reorganizar a solução na estrutura da seção 3 (mover arquivos sem reescrever)
- [ ] Mover para o Core os arquivos já livres de UI: `FileSystemEntry`, `ScanProgress`, `PauseToken`, `ScanPathFilter`, `SizeFormatter`, `TreeSortService`, `TreeSortMode`, `ViewMode`, `SearchCriteria`, `SearchService`, `SearchDataSource`, `ScanHistory*Service`, `ScanHistoryComparisonResult`, `StorageHistory*` (serviços e registro), `Redundancy*Service`, `ScanCacheService`, `ScanResultFileService`, `CsvExportService`, `LocalizationService`, `AppSettings`, `AppConstants`, `AppAlertLog`, `GitHubUpdateService`
- [ ] Remover do Core qualquer referência residual a `System.Windows.Forms` / `System.Drawing` (ex.: cores e fontes em `AppSettings` passam a ser tipos neutros, como hex string ou struct própria)
- [ ] **Caminhos de dados (`IAppPaths`):** substituir `AppContext.BaseDirectory` (e caminhos fixos como `%LOCALAPPDATA%\WTF\ScanCache` do `ScanCacheService`) por um serviço de caminhos:
  - Windows: manter o modo portátil (ao lado do `.exe`) quando a pasta for gravável, senão `%LOCALAPPDATA%\c2flux`
  - macOS: `~/Library/Application Support/c2flux` (dados) e `~/Library/Caches/c2flux` (cache)
  - Linux: `$XDG_CONFIG_HOME/c2flux`, `$XDG_DATA_HOME/c2flux` e `$XDG_CACHE_HOME/c2flux`
  - Migração automática dos dados do local antigo
- [ ] `LocalizationService`: carregar idiomas como recurso embutido ou de um caminho resolvido por `IAppPaths`
- [ ] Projeto WinForms passa a referenciar o Core. O app Windows continua idêntico
- [ ] Testes unitários do Core rodando em CI **nos três SOs** (matriz `windows-latest`, `macos-latest`, `ubuntu-latest`)

**Entregável:** app Windows igual ao de hoje e Core compilando e passando nos testes em Windows, macOS e Linux.

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

- [ ] Criar `IFileSystemScanner`, `ScannerSupport`, `ScanOptions` no Core
- [ ] Transformar `ScanExecutionController` em `ScannerPipeline`: recebe uma lista ordenada de scanners, tenta cada um, registra o motivo de cada fallback no `AppAlertLog` e mede o tempo (o código atual já faz isso, só passa a ser genérico)
- [ ] Adaptar os scanners do Windows à interface (sem alterar a lógica interna):
  - `C2FluxScanner` / `NtfsMftScanner` → `MftScanner` (os dois modos continuam escolhidos pela configuração `C2FluxScan`)
  - `NtQueryDirectoryScanner` → `NtQueryScanner`
  - `DirectoryScanner` → `Win32FindScanner`
- [ ] Corrigir o uso direto de scanners fora do pipeline (`MainForm.cs`, no fluxo de detalhes do histórico de armazenamento)
- [x] **Bugs pré-existentes no `DirectoryScanner`, corrigidos** (encontrados nas Fases 0.2 e 0.3): com a configuração padrão `SkipReparsePoints = true`, `_activeDirectoryIdentities` fica `null` e `ScanDirectoryContents` lança `NullReferenceException` (`DirectoryScanner.cs:248`). O último fallback de varredura do Windows nunca funcionava. Corrigido: a lista é sempre criada
  - Caminhos longos: a listagem chamava `FindFirstFileEx` sem o prefixo `\\?\`, então pastas com mais de ~260 caracteres eram registradas como puladas, sem conteúdo. No `T:` de teste faltavam 38 pastas e 37 arquivos (a cadeia `deep/` parava na profundidade 27 de 62). Corrigido usando o mesmo prefixo que o scanner já aplicava ao abrir pastas
  - Verificado no CI: depois das correções, o `win32find` encontra no `T:` exatamente as mesmas 304 pastas, 15.127 arquivos e 687.069.899 bytes do `ntquery` (profundidade 62), e o mesmo que ele no `C:\Program Files`
- [ ] Extrair código duplicado entre scanners (`CompiledPathFilter`, `DirectoryIdentity`, montagem da árvore, relatório de progresso) para utilitários comuns no Core
- [ ] **`ManagedScanner`** (fallback universal): `FileSystemEnumerable<T>` do .NET com paralelismo por diretório. Funciona em qualquer SO e é a rede de segurança final
- [ ] **Suíte de conformidade:** roda todos os scanners disponíveis no SO sobre a árvore sintética da Fase 0 e exige resultados idênticos (contagem de arquivos/pastas, tamanhos, datas, tratamento de hardlinks/symlinks, pastas sem permissão)
  - Definir a semântica esperada antes de exigir resultados iguais. Na Fase 0.2, os scanners já divergem no mesmo disco: o `ntquery` soma só o que o usuário consegue ler e conta symlinks de arquivo como arquivos de 0 bytes, enquanto os de MFT ignoram permissões e incluem os arquivos internos do NTFS e a `System Volume Information` (+31,5 MB num disco de 690 MB). Ver `docs/benchmarks/README.md`
  - Mesmo quando os totais batem, as impressões digitais diferem (`ntquery` × `win32find` no `T:` e no `C:\Program Files`). Elas incluem a data de modificação e o tamanho de cada pasta, e um dos dois provavelmente não preenche esses campos para pastas. Para investigar, o `c2flux-bench` precisa de um modo que grave a listagem entrada por entrada
- [ ] **Benchmark:** compara os scanners com BenchmarkDotNet. No Windows, o workflow comparativo da Fase 0.2 confirma que não houve regressão em relação à tag `baseline-winforms`

**Cadeias de fallback:**

| SO | 1º | 2º | 3º | Final |
|---|---|---|---|---|
| Windows | `MftScanner` (NTFS, raiz de unidade, admin) | `NtQueryScanner` | `Win32FindScanner` | `ManagedScanner` |
| macOS | `GetAttrListBulkScanner` | — | — | `ManagedScanner` |
| Linux | `IoUringStatxScanner` (opcional, kernel ≥ 5.6) | `GetDentsStatxScanner` | — | `ManagedScanner` |

**Entregável:** pipeline de varredura plugável, `ManagedScanner` funcionando nos três SOs e suíte de conformidade verde.

---

### Fase 3: Scanners nativos de alto desempenho

#### 3.1 macOS: `GetAttrListBulkScanner`

- [ ] P/Invoke para `open(O_RDONLY | O_DIRECTORY)`, `getattrlistbulk`, `close`
- [ ] Atributos solicitados numa única chamada por lote: `ATTR_CMN_NAME`, `ATTR_CMN_OBJTYPE`, `ATTR_CMN_DEVID`, `ATTR_CMN_FILEID`, `ATTR_CMN_MODTIME`, `ATTR_CMN_FLAGS`, `ATTR_FILE_LINKCOUNT`, `ATTR_FILE_DATALENGTH`, `ATTR_FILE_ALLOCSIZE`
- [ ] Buffer grande reutilizado por thread (`ArrayPool`), parsing do formato empacotado com `Span<byte>`, sem alocações por entrada além do nome
- [ ] Paralelismo: fila de diretórios com N workers (work-stealing), N padrão = núcleos lógicos, ajustável
- [ ] **Firmlinks do APFS:** detectar `/System/Volumes/Data` e deduplicar por `(devid, fileid)` para não contar o mesmo dado duas vezes ao varrer `/`
- [ ] **Hardlinks:** contar o tamanho uma vez só quando `linkcount > 1` (conjunto `(devid, fileid)`)
- [ ] Não atravessar outros volumes (comparar `devid`), exceto se `CrossMountPoints`
- [ ] Ignorar volumes de sistema somente leitura e snapshots quando apropriado
- [ ] **Acesso Total ao Disco:** detectar falhas `EPERM` em pastas protegidas (`~/Library/Mail`, `~/Library/Safari` etc.) e mostrar um aviso único na UI com um botão que abre *Ajustes do Sistema → Privacidade e Segurança → Acesso Total ao Disco*
- [ ] Tamanho lógico (`DATALENGTH`) e alocado (`ALLOCSIZE`) disponíveis para o modo de exibição
- [ ] Benchmark contra o `ManagedScanner` e contra `du -sk` no mesmo volume

#### 3.2 Linux: `GetDentsStatxScanner`

- [ ] P/Invoke para `openat`, `getdents64`, `statx`, `close` (libc)
- [ ] Ler cada diretório com `getdents64` usando um buffer grande. O `d_type` identifica pastas sem precisar de `stat`
- [ ] `statx(dirfd, nome, AT_SYMLINK_NOFOLLOW | AT_STATX_DONT_SYNC, STATX_SIZE | STATX_BLOCKS | STATX_MTIME | STATX_INO | STATX_NLINK, ...)`: chamada relativa ao diretório e apenas os campos necessários
- [ ] Tratar `d_type == DT_UNKNOWN` (alguns sistemas de arquivos, como XFS antigo e alguns FUSE) com `statx` adicional
- [ ] Paralelismo com fila de diretórios e N workers
- [ ] **Sistemas de arquivos virtuais:** ler `/proc/self/mountinfo` e ignorar `proc`, `sysfs`, `devtmpfs`, `devpts`, `cgroup*`, `tracefs`, `debugfs`, `securityfs`, `pstore`, `bpf`, `autofs` etc.
- [ ] Não atravessar pontos de montagem por padrão (comparar `stx_dev_major/minor`)
- [ ] Hardlinks contados uma vez `(dev, ino)`, symlinks nunca seguidos
- [ ] Tamanho lógico (`stx_size`) e alocado (`stx_blocks * 512`)
- [ ] Tratar `EACCES` / `EPERM` como "pasta pulada" (igual ao comportamento atual no Windows)
- [ ] Benchmark contra o `ManagedScanner`, `du -s`, `gdu` e `dua`

#### 3.3 Linux: `IoUringStatxScanner` (opcional, depois da 3.2)

- [ ] Enviar os `statx` em lote via `io_uring` (`IORING_OP_STATX`), reduzindo trocas de contexto
- [ ] Ativar só se o kernel suportar e se o benchmark mostrar ganho real (principalmente em HDD e armazenamento de rede)

#### 3.4 Windows: ajustes

- [ ] Manter a MFT como caminho principal. Avaliar se o `C2FluxScanner` e o `NtfsMftScanner` podem virar um só
- [ ] Suporte a `win-arm64` (verificar o NtfsReader em ARM64)
- [ ] Avaliar ReFS / exFAT: hoje caem no NtQuery, o que está correto

**Entregável:** varredura nativa e paralela nos três SOs, validada pela suíte de conformidade e com benchmarks publicados.

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

- [ ] Implementar cada interface nos três projetos `Platform.*`
- [ ] Substituir **todas** as chamadas diretas a `explorer.exe`, Registro, `DriveInfo` e `WindowsPrincipal` pelas interfaces
- [ ] `PartitionGridController` / `DriveComboBoxController`: trabalhar com "volumes" em vez de letras de unidade (no macOS/Linux o caminho raiz é o ponto de montagem)

**Entregável:** nenhuma chamada específica de SO fora dos projetos `Platform.*`.

---

### Fase 5: Nova interface em Avalonia (`c2flux.App`)

**Objetivo:** recriar a interface com a maior fidelidade possível à atual.

#### 5.1 Fundação

- [ ] Projeto Avalonia 11 (versão estável mais recente) com MVVM leve (CommunityToolkit.Mvvm)
- [ ] **Tema Ant Design:** portar a paleta, raios, espaçamentos, tipografia e estados (hover, foco, pressionado, desabilitado) de `AntdThemeService.cs` para estilos Avalonia (`ControlTheme`). Avaliar o Semi.Avalonia como base ou fazer o tema do zero, só com os controles usados
- [ ] Temas claro, escuro e "seguir o sistema" (reaproveitar as opções de `AppLayout`)
- [ ] Integração do `LocalizationService` com binding (troca de idioma em tempo de execução)
- [ ] **RTL:** árabe, hebraico, persa e urdu com `FlowDirection.RightToLeft`
- [ ] Fontes embutidas para resultado idêntico entre SOs, com fallback para CJK, tailandês e devanágari
- [ ] Ícone do app, barra de título (decorações nativas por padrão; avaliar barra customizada para imitar o Windows)
- [ ] Menu de aplicativo nativo no macOS (Sobre, Ajustes ⌘, , Sair ⌘Q) e atalhos com ⌘ em vez de Ctrl

#### 5.2 Gráficos (desenho customizado)

Portar de GDI+ para `DrawingContext` do Avalonia (ou SkiaSharp direto, se for preciso mais desempenho). A lógica de layout (algoritmo de treemap, ângulos do sunburst, escalas) é reaproveitada quase sem mudanças.

- [ ] `TreeEntrySizeBarView`: barras de tamanho na árvore
- [ ] `Chart_Treemap` (3,3 mil linhas, o maior): layout, cores, rótulos, hover, clique, zoom
- [ ] `Chart_Sunburst`
- [ ] `Chart_PieChart`
- [ ] `Chart_BarChart`
- [ ] `Chart_TableGridChart` / `Chart_ResponsiveTableGrid`
- [ ] `StorageHistoryChart`
- [ ] `ScanHistoryGrowthOverviewControl`
- [ ] `StatusSymbolRenderer`
- [ ] Virtualização e cache de renderização para árvores com milhões de entradas

#### 5.3 Janelas e controles

Ordem sugerida: o que é visto primeiro vem antes.

- [ ] `MainForm`: layout principal, barra de ferramentas, seletor de unidade, árvore, painel de gráficos, barra de status (`LayoutMainFormController`, `StatusMainFormController`, `TreeEntryController`, `ExportEntryController`, `PartitionGridController`, `DriveComboBoxController`)
- [ ] `SearchForm`: busca rápida
- [ ] `SettingsForm`
- [ ] `ScanHistoryForm`
- [ ] `StorageHistoryForm` + `StorageHistoryDetailsForm`
- [ ] `AdvancedFeaturesForm`: análise, redundância
- [ ] `AlertHistoryForm`
- [ ] `AboutForm`, `UpdateAvailableForm`, `DatabaseMoveForm`, `DebugClassForm`
- [ ] `AppDialogs`: caixas de mensagem no estilo Ant
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

### Em aberto

- [ ] **Nome e ícone do fork:** necessário antes da primeira release pública (Fase 6)
- [ ] **Tema base:** partir do Semi.Avalonia ou escrever o tema Ant Design do zero? Decidir no início da Fase 5
- [ ] **Native AOT:** inicialização mais rápida e binário menor, mas exige revisar reflexão e serialização JSON (usar *source generators*)
- [ ] **Linux:** quais formatos além do AppImage (deb, rpm, Flatpak)?
- [ ] **io_uring:** vale a complexidade extra? Decidir com base nos benchmarks da Fase 3.2
