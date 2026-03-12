# Monitor PC 💻

Um sistema de monitoramento de hardware robusto e elegante construído em WPF com .NET 8. O **Monitor PC** extrai e exibe dados de telemetria detalhados dos componentes do seu computador em tempo real, fornecendo um painel moderno focado em CPU, Placa-Mãe e GPU.

![Monitor PC Dashboard](Monitor_Pc/Assets/print.png) *(Nota: adicione uma screenshot do seu app aqui!)*

## ✨ Funcionalidades Principais

* **Integração com HWiNFO64**: Lê dados de telemetria avançados (temperaturas, tensões, frequências, power draw) diretamente da memória compartilhada do HWiNFO64 para oferecer a maior precisão possível.
* **Fallback Inteligente (WMI)**: Não tem o HWiNFO aberto? O sistema automaticamente faz fallback para o WMI do Windows para tentar exibir uso de CPU e outras estatísticas básicas.
* **Sistema de Alertas Inteligente**: Detecta anomalias em tempo real com base em limites predefinidos (ex: temperatura > 85°C, VCORE instável) e exibe avisos visuais na tela.
* **Dashboards Dinâmicos**: Barras de progresso com transição de cor suave e texto responsivo que refletem o estado de saúde e percentual de uso de cada componente.
* **Estatísticas Min/Max/Média**: O aplicativo coleta e analisa os valores mínimos, máximos e médios durante a sessão de uso, com a opção de reset manual.
* **Registro e Exportação (Logger)**:
  * Sistema de logging em formato CSV na pasta `logs`.
  * Geração instantânea de relatórios no Microsoft Excel com gráficos coloridos e formatação condicional baseada na tolerância das tensões (ATX Power Supply Design Guide).
* **Bandeja do Sistema (System Tray)**: Pode ser minimizado para a área de notificação do Windows e mostra pop-ups quando ocorrem alertas críticos.
* **Controle de Intervalo**: Permite alterar o ciclo de atualização dos sensores em tempo real, reduzindo impacto no sistema quando necessário (0.5s, 1s, 2s, 5s).

## 🚀 Como Usar

### Pré-requisitos
1. **Windows 10 ou 11**
2. **.NET 8.0 Desktop Runtime** instalado.
3. Para dados completos e corretos de sensores, é **altamente recomendado** usar o [HWiNFO64](https://www.hwinfo.com/).

### Configurando o HWiNFO64
Para o aplicativo receber todos os dados (voltagens completas, clocks detalhados de todos os núcleos, temperaturas, etc.):
1. Abra o HWiNFO64 e vá em `Settings` (Engrenagem).
2. Na aba **General/User Interface**, marque a caixa **"Shared Memory Support"**.
3. Deixe o HWiNFO rodando em segundo plano (Sensors-only).

### Executando o Projeto
Abra a solução no Visual Studio 2022+ e compile o projeto, ou rode via terminal:
```powershell
dotnet build Monitor_Pc.sln
dotnet run --project Monitor_Pc
```

## 🏗️ Estrutura do Projeto

A aplicação segue o padrão MVVM utilizando o `CommunityToolkit.Mvvm`.

* `MainWindow.xaml`: Interface do usuário principal responsiva baseada em Grids com layout unificado de card.
* `MainViewModel.cs`: Orquestração de dados, timers, listagem de hardware e lógica do Logger/Excel.
* `HWiNFOReader.cs`: Classe responsável por extrair dados do mapeamento de memória do HWiNFO.
* `TelemetryLogger.cs`: Arquitetura paralela de registro em arquivo `.csv`.
* `ExcelExporter.cs`: Utilitário (usando pacote *EPPlus*) para geração de relatórios tabulares em `.xlsx`.
* `AlertRule.cs`: Lógica de regras de monitoramento paramétrico para classificar severidades visuais.

## ⚠️ Sobre o Modo Diagnóstico
Se o Monitor PC for aberto sem o HWiNFO64 estar rodando com a memória compartilhada ligada, ele entrará em modo "Diagnóstico", exibindo um painel laranja de aviso e os sensores limitados que puderem ser encontrados via contadores nativos do sistema.

## 🤝 Uso
Este código foi construído iterativamente. Sinta-se livre para ramificar ou modificar. Para adicionar novos módulos como RAM, Disco e Rede, basta ajustar as exclusões feitas na classe `Diagnostic` ou dentro do arquivo `MainViewModel`.
