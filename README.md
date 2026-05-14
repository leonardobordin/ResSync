# ResSync

> **Projeto desenvolvido inteiramente por Inteligência Artificial (GitHub Copilot / Claude).**

**ResSync** é uma aplicação Windows (WPF) que gerencia automaticamente resolução de tela, taxa de atualização e, em GPUs NVIDIA, Digital Vibrance/saturação ao detectar que jogos ou aplicativos específicos foram iniciados.

![ResSync Screenshot](docs/screenshot.png)

---

## Funcionalidades

- **Monitoramento de Processos** — Detecta automaticamente quando jogos/apps são executados com base no nome do processo.
- **Ajuste Automático de Display** — Altera resolução e taxa de atualização quando o app monitorado inicia.
- **Restauração Automática** — Reverte todas as configurações de display quando o app monitorado é encerrado.
- **Suporte Multi-Monitor** — Permite direcionar perfis para qualquer monitor conectado.
- **Integração NVIDIA** — Controla Digital Vibrance e Saturação Extra via NvAPI (`nvapi64.dll`).
- **Suporte AMD/Outras GPUs** — Mantém somente as opções de resolução, sem exibir controles NVIDIA.
- **System Tray** — Minimiza para a bandeja do sistema com menu de contexto.
- **Início com Windows** — Pode iniciar automaticamente com o Windows e já minimizado na bandeja.
- **Perfis Persistentes** — Configurações salvas em JSON em `%AppData%\ResSync\config.json`.

---

## Tecnologias

| Componente           | Tecnologia                                         |
| -------------------- | -------------------------------------------------- |
| Framework            | .NET 9.0 (WPF + Windows Forms para NotifyIcon)     |
| Linguagem            | C# 13                                              |
| Display              | Win32 P/Invoke (`ChangeDisplaySettingsEx`)              |
| Vibrance (NVIDIA)    | NvAPI (`nvapi64.dll`)                               |
| Configuração         | `System.Text.Json`                                  |
| Arquitetura          | MVVM (Model-View-ViewModel)                         |

---

## Estrutura do Projeto

```
ResSync.slnx                      # Arquivo de solução
README.md                         # Documentação do projeto
docs/
└── screenshot.png                # Captura de tela da aplicação
ResolutionManager/                # Projeto principal (WPF)
├── App.xaml / App.xaml.cs        # Ponto de entrada, system tray, ciclo de vida
├── GlobalUsings.cs               # Aliases globais (WPF vs WinForms)
├── AssemblyInfo.cs               # Informações do assembly
├── Models/
│   ├── AppConfiguration.cs       # Configuração geral da aplicação
│   ├── AppProfile.cs             # Perfil por aplicativo (resolução, vibrance, monitor)
│   ├── DisplayMonitor.cs         # Representação de um monitor físico
│   └── DisplayResolution.cs      # Resolução + taxa de atualização
├── Native/
│   └── NativeMethods.cs          # P/Invoke: display settings, gamma ramp, teclado
├── Helpers/
│   ├── Converters.cs             # Value converters WPF (Null→Visibility, Bool→Brush…)
│   └── RelayCommand.cs           # Implementação de ICommand
├── Services/
│   ├── AppLogger.cs             # Log simples em %AppData%\ResSync\
│   ├── IDisplayService.cs        # Interface — resolução, vibrance, saturação
│   ├── DisplayService.cs         # Implementação via Win32 + NvAPI
│   ├── IProcessMonitorService.cs
│   ├── ProcessMonitorService.cs  # Polling de processos (intervalo de 1s)
│   ├── IConfigurationService.cs
│   ├── ConfigurationService.cs   # Persistência JSON em %AppData%\ResSync\
│   └── NvApiService.cs           # Wrapper NvAPI (Digital Vibrance NVIDIA)
├── ViewModels/
│   ├── BaseViewModel.cs          # INotifyPropertyChanged base
│   ├── MainViewModel.cs          # ViewModel principal
│   └── ProfileViewModel.cs       # ViewModel de perfil para binding
└── Views/
    ├── MainWindow.xaml            # UI principal (custom chrome, dark theme)
    └── MainWindow.xaml.cs         # Code-behind da janela principal
```

---

## Pré-requisitos

- **Windows 10/11**
- **.NET 9.0 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet/9.0)
- GPU NVIDIA opcional para Digital Vibrance e Saturação Extra. Em AMD/outras GPUs, o app exibe apenas resolução.

---

## Como Compilar

```bash
# Build
dotnet build ResolutionManager/ResolutionManager.csproj

# Publicar self-contained para Windows x64
dotnet publish ResolutionManager/ResolutionManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish
```

---

## Build e Release Automaticos

O repositorio possui um workflow em `.github/workflows/release.yml`.

A cada `push` na branch `main`, o GitHub Actions:

1. Restaura as dependencias do projeto.
2. Publica o app self-contained para `win-x64`.
3. Compacta a pasta publicada em `ResSync-win-x64-vYYYY.MM.DD.N.zip`.
4. Gera um arquivo `.sha256` do ZIP.
5. Cria uma nova GitHub Release com tag automatica no formato `vYYYY.MM.DD.N`.

O workflow tambem pode ser executado manualmente pela aba **Actions**.

Para criar Releases automaticamente, em **Settings > Actions > General**, deixe **Workflow permissions** como **Read and write permissions**.

---

## Como Usar

1. Execute **ResSync**.
2. Crie um novo perfil clicando em **+**.
3. Selecione o executável do jogo/aplicativo.
4. Escolha o monitor de destino, a resolução de entrada e a resolução ao fechar.
5. Em GPUs NVIDIA, configure também Digital Vibrance/Saturação Extra.
6. Use **Aplicar** ou o submenu de aplicação para testar imediatamente, ou ative o monitoramento para aplicar tudo automaticamente quando o app abrir.
7. Minimize para a bandeja do sistema para manter o ResSync rodando em segundo plano.

---

## Licença

Este projeto é de uso pessoal. Todos os direitos reservados.
