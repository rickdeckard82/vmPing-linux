# vmPing → Linux (.deb) — Plano de Migração

Decisão: manter C#, trocar WPF por **Avalonia UI** sobre **.NET 8**. Reescrever em outra linguagem descartaria ~7.100 linhas de lógica que já funciona para resolver um problema que é só de camada de apresentação.

[Certo] Isto **não é um recompile**. É um port real: 20 janelas XAML + 15 ResourceDictionaries precisam ser reescritas, e ~60% das classes "de lógica" também tocam tipos WPF (`System.Windows.Media.Color`, `Application.Current.Dispatcher`, `MessageBox`) e precisam de adaptação, não cópia. Quem prometer "só trocar o target framework" está enganando você.

## Inventário e classificação por esforço

### Portam quase sem alteração (lógica pura, sem `System.Windows.*`)
| Arquivo | Linhas | Observação |
|---|---|---|
| `Classes/Constants.cs` | 52 | **[correção]** meu primeiro grep (`^using`) deu falso negativo por causa do BOM no início do arquivo — na verdade usa `System.Windows.Input.Key` para 2 constantes de atalho de teclado. Corrigido no scaffold: troquei para `Avalonia.Input.Key` (mesma API, `Key.F12`/`Key.F1` existem em ambos). Também ajustei `DefaultAudioDownFilePath`/`DefaultAudioUpFilePath`, que apontavam para `%WINDIR%\Media\*.wav` (Windows-only) — agora apontam para o freedesktop sound theme. |
| `Classes/PingStatistics.cs` | 52 | copiar |
| `Classes/FloodHostNode.cs` | 93 | copiar |
| `Classes/NetworkRouteNode.cs` | 65 | copiar |
| `Classes/NetworkRoute.cs` | 43 | copiar |
| `Classes/Probe-Dns.cs` | 57 | copiar |
| `Classes/Probe-Traceroute.cs` | 98 | copiar |
| `Classes/StatusChangeLog.cs` | 60 | copiar |
| `Classes/Alias.cs` | 140 | copiar (usa `System.Xml`, portável) |
| `Classes/Favorite.cs` | 258 | copiar |

### Precisam de adaptação pontual (lógica boa, mas referenciam tipos WPF)
| Arquivo | Linhas | O que muda |
|---|---|---|
| `Classes/ApplicationOptions.cs` | 159 | `System.Windows.Media.Color` → `Avalonia.Media.Color` |
| `Classes/Probe.cs` | 248 | `Dispatcher.Invoke` → `Avalonia.Threading.Dispatcher.UIThread.Invoke` |
| `Classes/Probe-Icmp.cs` | 264 | idem + revisar ICMP raw socket (ver riscos) |
| `Classes/Probe-Tcp.cs` | 271 | idem |
| `Classes/Probe-Util.cs` | 303 | `System.Media.SoundPlayer` (Windows-only) → substituir por lib cross-platform |
| `Classes/Util.cs` | 199 | `MessageBox` → dialog Avalonia; `System.Windows` genérico |
| `Classes/CommandLine.cs` | 144 | `System.Windows.Application` → `Avalonia.Application` |
| `Classes/Configuration.cs` | 491 | portável, mas caminho de persistência (`%AppData%`) → `~/.config/vmPing` |
| `Classes/ValueConverters.cs` | 428 | `IValueConverter` do WPF → interface equivalente do Avalonia (assinatura similar, namespace diferente) |

### Reescrita completa (XAML + code-behind)
Todas as 20 janelas em `UI/*.xaml` + `*.xaml.cs` (7.144 linhas de code-behind no total, families maiores: `MainWindow.xaml.cs` 847, `OptionsWindow.xaml.cs` 931, `StatusHistoryWindow.xaml.cs` 380) e as 15 `ResourceDictionaries/*.xaml` (estilos de botão, combobox, datagrid, etc.). Avalonia usa XAML com sintaxe próxima, mas não é find-and-replace — cada `DllImport("user32.dll")` (`GetWindowLong`/`SetWindowLong`/`MonitorFromWindow`, presente em 6 janelas) e cada uso de `System.Windows.Forms` (NotifyIcon, SaveFileDialog, FolderBrowserDialog) precisa do equivalente nativo Avalonia (`TrayIcon`, `StorageProvider`).

## Riscos técnicos (não são detalhe de sintaxe — afetam funcionalidade)

1. **[Provável] Ping ICMP sem privilégio.** No Linux, socket ICMP raw exige `CAP_NET_RAW` ou root. `System.Net.NetworkInformation.Ping` não contorna isso sozinho. Solução: aplicar `setcap cap_net_raw+ep` no binário publicado, via `postinst` do pacote `.deb` (incluído no scaffold abaixo).
2. **[Certo] `System.Media.SoundPlayer`** (alertas sonoros de host down/up) é Windows-only (usa winmm.dll). Precisa de substituição — sugestão: `LibVLCSharp` ou tocar `.wav` via `aplay`/`paplay` como processo externo (mais simples, sem dependência nativa extra).
3. **[Provável] Persistência de configuração.** `Configuration.cs` grava em `%AppData%\vmPing`. Path precisa virar `~/.config/vmPing` (usar `Environment.GetFolderPath(SpecialFolder.ApplicationData)`, que já resolve corretamente em Linux no .NET moderno — isso é uma linha, não um redesenho).
4. **[Chutando] Efeitos visuais de chrome de janela** (sombra, redimensionamento customizado via `GetWindowLong`) têm equivalente direto em Avalonia (`SystemDecorations`, `ExtendClientAreaToDecorationsHint`), mas o comportamento pixel-a-pixel pode divergir entre compositores Linux (X11 vs Wayland/GNOME vs KDE). Vale testar cedo, não no fim.
5. **[Certo] Projeto original usa formato de `.csproj` legado** (`ToolsVersion="12.0"`, non-SDK-style, `TargetFrameworkVersion v4.7.2`). Terá que virar SDK-style `net8.0` de qualquer forma — outro motivo pelo qual "só mudar o target" não existe como opção.

## Fases sugeridas

1. **Esqueleto** — projeto Avalonia SDK-style, pipeline de build e empacotamento `.deb` funcionando de ponta a ponta com uma janela mínima. ✅ *(feito — ver `vmPing.Avalonia/` e `packaging/`)*
2. **Core sem UI** — `ApplicationOptions`, `Util`, `CommandLine`, `Configuration`, `ValueConverters`, `Probe`/`Probe-Icmp`/`Probe-Tcp`/`Probe-Util` portados. ✅ *(feito — ver detalhes abaixo)*
3. **MainWindow + tray icon** — a tela mais usada primeiro, prova a integração com `TrayIcon` e `Dispatcher.UIThread`. ⏳ *(scaffold mínimo já existe em `UI/MainWindow.axaml`; falta portar a MainWindow real de 847 linhas)*
4. **Janelas secundárias** — Options, Favorites, Aliases, StatusHistory, Flood/Traceroute, DialogWindow, UsageWindow, NewConfigurationWindow, PopupNotificationWindow, na ordem de uso real. ⏳ *(pendente — ver débitos deixados na Fase 2 abaixo)*
5. **Estilos** — portar `ResourceDictionaries/*.xaml` por último; é trabalho mecânico mas alto volume. ⏳ *(pendente)*
6. **Empacotamento final** — `dotnet publish -r linux-x64 --self-contained`, `setcap`, `dpkg-deb --build`. ✅ *(script pronto, não executado/verificado — ver limitações)*

### O que a Fase 2 entregou (portado para `vmPing.Avalonia/Classes/`)

`ApplicationOptions.cs`, `Util.cs`, `CommandLine.cs`, `Configuration.cs`, `ValueConverters.cs`, `Probe.cs`, `Probe-Icmp.cs`, `Probe-Tcp.cs`, `Probe-Util.cs` — todos adaptados de `System.Windows.*` para Avalonia. Mudanças que valem atenção:

- **`Constants.cs`** (categorizado antes como "porta sem alteração"): correção — usava `System.Windows.Input.Key` para atalhos de teclado, mascarado por BOM no grep inicial. Trocado para `Avalonia.Input.Key`. Também ajustei os paths de áudio padrão de `%WINDIR%\Media\*.wav` para o freedesktop sound theme.
- **`Util.ShowError`**: `MessageBox.Show` (síncrono/bloqueante) → `MsBox.Avalonia` (assíncrono). Pacote novo adicionado ao `.csproj` (MIT, ver `packaging/debian/copyright`). Mudança de comportamento: quem chama `ShowError` não bloqueia mais esperando o usuário fechar o popup.
- **`Probe-Util.PlaySound`**: `System.Media.SoundPlayer` (Windows-only) → shell-out para `paplay`/`aplay`/`ffplay`, escolhido deliberadamente para não trazer LibVLCSharp (LGPL, conflita com publish single-file).
- **`Probe-Tcp.cs`**: comparação de erro `ex.ErrorCode == 11001` (WSAHOST_NOT_FOUND, código WinSock) trocada por `ex.SocketErrorCode == SocketError.HostNotFound` (portável) — sem essa troca, a detecção de "host não resolvido" nunca dispararia no Linux.
- **`Configuration.FilePath`**: `%LOCALAPPDATA%` → `Environment.SpecialFolder.ApplicationData` (resolve para `~/.config` no Linux via XDG).
- **Débito deixado para a Fase 4** (documentado em comentários `TODO` no próprio código): `CommandLine.ShowHelpDialog`/`ShowErrorDialog` e `Configuration.IsReady` dependiam de `UsageWindow`, `DialogWindow` e `NewConfigurationWindow` (ainda não portadas) com `Window.ShowDialog()` síncrono do WPF — Avalonia só tem a versão assíncrona (`Task`), exigindo repensar esse trecho do fluxo de startup. Puseram-se fallbacks funcionais (stderr / criação de config sem perguntar) para manter o app operável nas Fases 2/3.
- **`ValueConverters.cs`**: `Visibility.Visible/Hidden/Collapsed` não existe no Avalonia (só `IsVisible` bool) — conversores que retornavam `Visibility` agora retornam `bool`. Perda de semântica: a distinção Hidden (reserva espaço) vs Collapsed (remove espaço) não tem equivalente direto; revisar caso alguma tela dependa disso ao portar o XAML na Fase 4.

### Verificação desta rodada

Chaves balanceadas em todos os arquivos novos (`{`/`}` contados por arquivo) e nenhuma referência viva a `System.Windows` fora de comentários — conferido por grep/script, não por compilador (ainda sem `dotnet` disponível neste ambiente).

## O que este scaffold NÃO faz

[Certo] Não compilei nem restaurei pacotes NuGet — o sandbox onde estou rodando não tem `dotnet` instalado e o acesso a `nuget.org`/`packages.microsoft.com` está bloqueado pelo allowlist de rede. O código em `vmPing.Avalonia/` está escrito com a API do Avalonia 11.x pela minha melhor referência, mas **não foi verificado por compilador**. Antes de confiar nele, rode localmente:

```bash
cd vmPing.Avalonia
dotnet restore
dotnet build
```

e me mande os erros de build — corrijo a partir daí. Migração de 7.000+ linhas sem loop de compilação real é trabalho às cegas; prefiro ser honesto sobre isso a fingir que validei algo que não pude validar.

## Rodada 1 de correção via build real (2026-07-26)

Você rodou `dotnet restore`/`dotnet build` de verdade (SDK 10.0.302) e caiu no primeiro erro esperado: `NU1102 — MsBox.Avalonia 3.1.6 não existe`. Eu tinha chutado esse número de versão sem conferir — errei. Corrigido consultando `api.nuget.org/v3-flatcontainer/<pacote>/index.json` diretamente (agora que tenho acesso, ao contrário de quando gerei o scaffold original):

- `MsBox.Avalonia`: só existe `3.0.0-rc2` publicado (a série 3.x nunca saiu do RC) — trocado.
- `Avalonia` / `Avalonia.Desktop` / `Avalonia.Themes.Fluent` / `Avalonia.Fonts.Inter`: já foram para a série `12.0.x`, mas `Avalonia.Diagnostics` ainda **não tem nenhum release 12.x** (parou em `11.3.17`). Em vez de misturar major versions entre os pacotes do Avalonia (risco real de conflito de resolução do NuGet), pinei tudo em `11.3.17` — a última versão em que os cinco pacotes são lockstep.

Rode `dotnet restore` de novo com o `.csproj` atualizado e mande o próximo erro, se houver. Dado que a versão do SDK/NuGet no seu ambiente pode ter avançado desde essa checagem, se o erro for de novo NU1102/NU1101 é sinal de que uma dessas versões já foi superada — me avise o número exato reportado pelo NuGet.

## Rodada 2 de correção via build real (2026-07-26)

Restore passou. `dotnet build` deu 10 erros reais, todos por dependências que eu deveria ter fechado antes e não fechei:

- **`CS0234` em 5 arquivos** (`Configuration.cs`, `Probe-Tcp.cs`, `Probe.cs`, `Util.cs`, `ValueConverters.cs`): `vmPing.Properties` não existia no projeto novo. Eu nunca portei `Properties/Strings.Designer.cs` + `Properties/Strings.resx` (39 chaves de string usadas em 5 arquivos diferentes) — puro esquecimento, não é um problema WPF. Copiados agora para `vmPing.Avalonia/Properties/`; em csproj SDK-style o `.resx` é incluído automaticamente como `EmbeddedResource` e o nome do recurso bate certinho com `vmPing.Properties.Strings` (RootNamespace já é `vmPing`), então não precisou de configuração extra.
- **`CS0246` StatusChangeLog não encontrado** (`Probe.cs`, `Probe-Util.cs` × 2): pior falha desta rodada. Na Fase 1 eu tinha catalogado `StatusChangeLog.cs`, `Alias.cs`, `Favorite.cs`, `NetworkRoute.cs`, `Probe-Dns.cs`, `Probe-Traceroute.cs` na tabela "portam quase sem alteração" — e então só copiei 4 arquivos diferentes (`Constants`, `PingStatistics`, `FloodHostNode`, `NetworkRouteNode`) por engano, sem perceber que tinha pulado esses 6. Documentei no plano que estavam prontos sem terem sido copiados de fato. Corrigido: os 6 foram conferidos de novo por grep (nenhum tem dependência WPF escondida, como o BOM tinha mascarado em `Constants.cs`) e copiados.
- **`CS0246` StatusHistoryWindow / IsolatedPingWindow não encontrados** (`Probe.cs`): esperado — são janelas da Fase 4 que `Probe.cs` referencia por campo/propriedade. Criei stubs mínimos (`UI/StatusHistoryWindow`, `UI/IsolatedPingWindow`, `UI/PopupNotificationWindow` — essa última também é chamada por `Probe-Util.TriggerStatusChange`) só para o projeto compilar: uma `Window` vazia com o construtor certo, marcada em comentário como placeholder, sem UI real nenhuma. Não confundir com Fase 4 concluída — é só uma casca.

Aproveitei para limpar os 79 avisos de nulidade (`CS8767`/`CS8612`/`CS8625`): a interface `Avalonia.Data.Converters.IValueConverter` é anotada com `object?`/parâmetros anuláveis e eu tinha copiado as assinaturas exatas do WPF (não anuláveis). Ajustado via `sed` em `ValueConverters.cs` (20 conversores × 2 métodos) e nos eventos `PropertyChangedEventHandler?` em `Probe.cs`/`FloodHostNode.cs`/`NetworkRouteNode.cs`/`PingStatistics.cs`. Isso não afeta comportamento, só limpa o output do build.

## Rodada 3 de correção via build real (2026-07-26)

Ficou só 1 erro real (mais 96 avisos de nulidade, cosméticos e sem risco — em objetos `required`/inicializados via inicializador de objeto, não via construtor, então o compilador reclama mesmo estando corretos em uso; deixados para depois): `Util.cs(116): CS0103 — ButtonEnum não existe no contexto atual`, dentro do código que eu tinha escrito contra `MsBox.Avalonia`.

Investiguei: o `.nuspec` do pacote aponta pra `github.com/CreateLab/MessageBox.Avalonia` (redireciona para `AvaloniaCommunity/MessageBox.Avalonia`). O branch `master` desse repo tem sim `MsBox.Avalonia.Enums.ButtonEnum` — mas a versão publicada no NuGet é `3.0.0-rc2`, e não consegui confirmar que o código-fonte daquele commit específico bate com o que está em `master` hoje (o repositório não tem GitHub Releases nem tags acessíveis via API para eu conferir a árvore exata do rc2). Ou seja: seria adivinhar de novo, pela terceira vez, a API de um pacote que nunca saiu de release candidate.

Decisão: **removi a dependência em `MsBox.Avalonia` inteiramente.** Implementei `UI/DialogWindow.axaml` + `.axaml.cs` — uma versão enxuta do `UI/DialogWindow.xaml` original (mesmo contrato público, `ErrorWindow`/`WarningWindow`), sem pacote externo nenhum. `Classes/Util.ShowError` agora chama isso. Efeito colateral bom: isso também fecha, adiantado, uma das janelas da Fase 4 que estava só como TODO — `DialogWindow` deixa de ser stub.

Regra que eu deveria ter seguido desde o início e vou seguir daqui pra frente: não pinar dependência de terceiros em versão pré-release (`-rc`, `-beta`, `-preview`) sem verificar a API contra o código-fonte exato daquela tag — e se não der pra verificar, não usar a dependência. `MsBox.Avalonia` violava isso; a implementação própria não tem esse risco.

Rode `dotnet build` de novo.

## Primeiro build verde (2026-07-26)

`dotnet build` teve êxito: 0 erros, 96 avisos, gerou `bin/Debug/net8.0/linux-x64/vmping.dll`. É o primeiro build real de todo este port — três rodadas de erros reais (13 no total) depois do scaffold inicial "às cegas".

[Certo] Build verde não é "app funcionando". Confirma só que a sintaxe e os tipos batem. Ainda não sei se: a `MainWindow` da Fase 1 (a de prova de conceito, com botão de ping) realmente abre em um ambiente Linux com display; o `TrayIcon` aparece de fato (código nunca testado em runtime); `Dispatcher.UIThread`/threading dos probes não trava a UI; `DialogWindow` renderiza sem erro de binding do XAML (`FindControl` por nome pode falhar silenciosamente em runtime mesmo compilando). Os 96 avisos são nulidade cosmética (praticamente todos em construtores de classes de dados que preenchem propriedade via inicializador de objeto, não no construtor — o compilador não tem como saber que a propriedade é sempre setada); não são urgentes, mas não os apago da lista.

Próximo passo real: `dotnet run` no `vmPing.Avalonia` e ver se a janela abre. Isso é uma verificação de runtime, não de compilação — outro tipo de erro totalmente diferente pode aparecer aí (crash de inicialização do Avalonia, DISPLAY ausente se rodando via SSH sem X forwarding, etc.).

## Primeira execução real (2026-07-26)

`dotnet run` funcionou: janela abriu (tema escuro do FluentTheme renderizado corretamente), ping ICMP para `8.8.8.8` retornou em 9ms — **sem precisar de `setcap`/root**. Prova que o pipeline Avalonia inicializa corretamente neste ambiente e que `System.Net.NetworkInformation.Ping` funciona ponta a ponta no Linux.

[Provável] O ping ter funcionado sem `CAP_NET_RAW` é quase certamente porque a distro do usuário tem `net.ipv4.ping_group_range` configurado para permitir ICMP echo unprivileged (comum em Ubuntu/Debian desktop modernos — `sysctl net.ipv4.ping_group_range` costuma vir como `0 2147483647` de fábrica). Isso NÃO é garantido em toda distro Linux (servidores mínimos, containers, algumas distros mais travadas deixam esse range vazio por padrão) — por isso o `postinst` do `.deb` continua aplicando `setcap cap_net_raw+ep` no binário publicado como rede de segurança, independente de o ambiente de dev não precisar disso.

O que ainda não foi testado em runtime: `TrayIcon` (o botão "Ping" cobre a tela toda, não há indício visual de ícone na bandeja no screenshot), `DialogWindow` (nada disparou erro ainda para testar `Util.ShowError`), e nenhuma das janelas-stub da Fase 4.

Conferido por script (chaves balanceadas, zero `System.Windows` fora de comentário) mas **não recompilado por mim** — ainda sem `dotnet` neste ambiente. Rode `dotnet build` de novo.

## Rodada 5 de correção via build real (2026-07-26)

A correção da Rodada 4 funcionou (nenhum `CS0246`/`CS0103` voltou). Mas apareceram 18 erros novos, todos do mesmo tipo e mesma causa: `MainWindow.axaml(...): Avalonia error AVLN2000: Unable to resolve property or method of name 'Status'/'Alias'/'Hostname'/'Type'/'Statistics'/'History'/'IsActive' on type 'XamlX.TypeSystem.XamlPseudoType'`.

Causa raiz: o `.csproj` tem `<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>` (decisão que eu tinha tomado no scaffold da Fase 1, para pegar erro de binding em tempo de compilação em vez de silenciosamente em runtime — WPF não tem esse recurso, então bindings quebrados lá só aparecem no Output em runtime). Isso significa que todo `DataTemplate` precisa de `x:DataType` explícito para o compilador saber contra qual classe resolver `Status`, `Alias` etc. — sem isso, o binding é resolvido contra um tipo placeholder (`XamlPseudoType`), daí o erro. Eu escrevi o `ItemsControl.ItemTemplate` inteiro (as 18 propriedades bindadas: `Status`, `Alias`, `Hostname`, `Type`, `Statistics.*`, `History`, `IsActive`) sem declarar `x:DataType` no `<DataTemplate>` — esquecimento meu, não é uma limitação do Avalonia.

Fix: adicionado `x:DataType="classes:Probe"` no `<DataTemplate>` de `UI/MainWindow.axaml` (linha 109). Conferido contra `Classes/Probe.cs` real (`namespace vmPing.Classes`) que as 7 propriedades usadas no template (`Status`, `Alias`, `Hostname`, `Type`, `Statistics`, `History`, `IsActive`) existem com esse nome e esse casing exatos — bate 100%. Era o único `DataTemplate` do arquivo, então não há outro lugar com o mesmo problema em `MainWindow.axaml`.

Rode `dotnet build` de novo.

## Segundo build verde — MainWindow real compila (2026-07-26)

`dotnet build` teve êxito: 0 erros, 101 avisos, `vmping.dll` gerado. Os 101 avisos são os mesmos 96 de nulidade cosmética de antes mais alguns novos do mesmo tipo (agora vindos também de `MainWindow.axaml.cs`) e um `IL3000` informativo sobre `Assembly.Location` em publish single-file (não afeta o build de Debug, só relevante quando publicar — ver Fase 6). Nenhum é bloqueante.

[Certo] Isso fecha o ciclo de compilação da Fase 3: `MainWindow.axaml`/`.axaml.cs` (as ~1000 linhas reais, não mais a prova de conceito) compilam limpo. Ainda não sabemos se roda — `TrayIcon`, `DialogWindow`, o grid dinâmico de probes com `x:DataType` recém-adicionado, e nenhuma das 9 janelas-stub nunca foram executadas. Próximo passo real: `dotnet run` e testar pelo menos: a janela abre com o menu completo; dá pra adicionar um probe e pingar; o ícone aparece na bandeja; `Options`/`Status History`/etc. abrem sem crash (mesmo que vazios).

## Primeira execução real da MainWindow completa (2026-07-26)

`dotnet run` abriu a janela real (não mais a prova de conceito): menu completo (Add, Columns, Stop All (F5), ⋯), grid dinâmico com dois hosts lado a lado, cores de status por conversor funcionando (vermelho para erro, lilás claro para probe parado), watermark "Enter a hostname" funcionando, estatísticas Sent/Received/Lost atualizando ao vivo, histórico de ping rolando. Ou seja: `x:DataType` resolveu de verdade, o grid dinâmico funciona, os conversores funcionam.

Erro encontrado ao pingar `8.8.8.8`: `Unable to send custom ping payload. Run prog[ram under privileged user account or grant cap_net_raw capability using setcap(8)]`, repetido a cada tentativa, `Sent: 4 Received: 0 Lost: 4`.

[Certo] Essa não é uma string nossa — é literalmente o recurso `net_ping_utility_custom_payload` do `dotnet/runtime` (`src/libraries/System.Net.Ping/src/Resources/Strings.resx`), propagado como `ex.Message` no `catch` de `Probe-Icmp.cs` (`DisplayIcmpReply`, linha ~257, que já existia assim desde a Fase 2). Confirmei a causa lendo `Ping.Unix.cs` da tag `v8.0.8` do `dotnet/runtime`: no Linux, `Ping.SendPingAsync` escolhe entre dois caminhos — socket raw ICMP (`RawSocketPermissions.CanUseRawSockets`) se tiver `CAP_NET_RAW`/root, ou fallback via utilitário `ping` do sistema operacional se não tiver. O utilitário `ping` do SO não permite embutir um payload customizado no pacote — só o socket raw permite. E `ApplicationOptions.Buffer` (`Classes/ApplicationOptions.cs`) **sempre** é setado com `Constants.DefaultIcmpData` (o payload clássico do vmPing, "abcdefghi...") e passado explicitamente pro `SendPingAsync`, nunca `null`. Ou seja: todo ping desse app no Linux exige o caminho de socket raw, sem exceção — não tem como cair no caminho "sem privilégio, sem payload customizado" que funcionou na Fase 1 (aquela prova de conceito chamava `Ping.Send(host)` sem buffer nenhum).

Isso não é bug do port. É exatamente a razão de existir do `packaging/debian/postinst` (`setcap cap_net_raw+ep` no binário publicado) — e a própria mensagem do .NET recomenda a mesma coisa. O que falta é só aplicar isso também no binário de Debug pra testar localmente sem instalar o `.deb`:

```
sudo setcap cap_net_raw+ep bin/Debug/net8.0/linux-x64/vmping
```

Atenção: essa capability fica anexada ao arquivo específico (inode). Cada novo `dotnet build`/`dotnet run` reescreve o binário e apaga a capability — precisa rodar o `setcap` de novo depois de cada rebuild antes de testar ping de novo. (Alternativa mais rápida pro ciclo de dev, com a contrapartida de gerar artefatos de build como root: `sudo dotnet run`.)

## Ping confirmado + bug real de asset (2026-07-26)

Depois do `setcap`, ping ICMP funcionou de ponta a ponta: 3 probes simultâneos (`1.1.1.1`, `8.8.8.8`, `terra.com.br` com resolução de hostname), RTT correto, `Sent`/`Received`/`Lost` batendo, cor verde de sucesso pelo conversor. Confirma socket raw ICMP, grid dinâmico, `x:DataType`, e os conversores de status todos funcionando de verdade, não só compilando.

Erro real reportado pelo terminal (não travou a app, só um aviso): `resource avares://vmPing.Avalonia/Assets/vmPing-16.png could not be found`. Causa: em `SetupTrayIcon()` (`MainWindow.axaml.cs`, linha 654) eu tinha escrito a URI `avares://` usando o **nome do projeto** (`vmPing.Avalonia`) em vez do **nome do assembly** (`vmping`, definido em `<AssemblyName>` no `.csproj`) — `avares://` resolve pelo nome real do assembly compilado, não pelo nome do `.csproj`/pasta. O arquivo `Assets/vmPing-16.png` existe e está corretamente incluído via `<AvaloniaResource Include="Assets\**" />`; só a URI estava errada. Corrigido para `avares://vmping/Assets/vmPing-16.png`. Não achei nenhuma outra ocorrência de `avares://` no projeto (só essa).

Rode `dotnet build` (não precisa de `setcap` de novo só pra conferir esse fix — só quando for testar ping outra vez) e confirme que o aviso não aparece mais e que o ícone da bandeja aparece.

## Fase 3 — MainWindow real (2026-07-26)

Portado `UI/MainWindow.axaml`/`.axaml.cs` (o original tinha 637 + 847 linhas). Decisão explícita de escopo, registrada também no cabeçalho do `.axaml`: **funcional primeiro, visual depois**. O que foi portado de verdade — todo o comportamento:

- Grid dinâmico de hosts (`ItemsControl` + `UniformGrid`), adicionar/remover probe, ping/stop por host, contagem de colunas via slider.
- Menu completo (Add, Columns, Start/Stop All, Options, Status History, Input Addresses, Popup Alerts, Favorite Sets, Aliases, Traceroute, Flood Host, New Instance, Help) com atalhos de teclado.
- Bandeja do sistema (`TrayIcon` com menu Options/Status History/Exit) — e aqui o Avalonia é mais simples que o WPF original: não precisa do hack de reflection (`GetMethod("ShowContextMenu", NonPublic)`) que o `NotifyIcon` do WinForms exigia para abrir o menu no botão direito.
- Favoritos e aliases (carregar/aplicar/persistir), usando as classes já portadas na Fase 2.
- Fechar para bandeja / minimizar para bandeja, conforme `ApplicationOptions.IsExitToTrayEnabled`/`IsMinimizeToTrayEnabled`.

O que foi **deliberadamente simplificado ou adiado**, e por quê:

- **ControlTemplates customizados dos botões** (bordas arredondadas, hover/pressed) → botões padrão do Avalonia. Dependem de `ResourceDictionaries/*.xaml`, que é trabalho da Fase 5.
- **Ícones vetoriais** (`StaticResource icon.*`) → texto/glifos Unicode (✕ ⛶ ✎). Mesma razão.
- **Fonte Marlett** pro glifo de status → trocada por Unicode (▲▼●) direto em `ValueConverters.ProbeStatusToGlyphConverter` — Marlett não existe fora do Windows, então isso não era opcional, é correção mesmo.
- **`Controls/AutoScrollListBox.cs`** (WPF `AdornerLayer` customizado) → substituído por uma versão simplificada que rola pro fim toda vez que `Probe.HistoryAsString` muda (evento que `Classes/Probe.cs` já dispara). Perde o indicador visual "tem conteúdo novo abaixo"; mantém o comportamento funcional (rolar para o ping mais recente).
- **Drag-and-drop para reordenar probes** → **não portado nesta fase**. `Avalonia.Input.DragDrop.DoDragDrop` é assíncrono (`Task<DragDropEffects>`), diferente do WPF que é síncrono — dá pra portar, mas é escopo de Fase 5, não Fase 3.
- **9 janelas que MainWindow abre** (Options, MultiInput, TraceRoute, FloodHost, NewFavorite, ManageFavorites, ManageAliases, EditAlias, Help) → stubs mínimos, criados só para o projeto compilar e rodar. `NewFavoriteWindow` e `EditAliasWindow` são exceção: a UI é mínima mas a lógica de salvar já é real (chamam `Favorite.Save`/`Alias.Add` de verdade), porque essas classes já foram portadas na Fase 2 e não fazia sentido fingir que não.

Mudança de arquitetura que apareceu pela primeira vez de forma pesada aqui: `RoutedCommand`/`CommandBinding`/`Window.ShowDialog() == true` (WPF) não têm equivalente direto no Avalonia. Troquei por: `Click` direto nos `MenuItem` + uma classe `RelayCommand : ICommand` pequena só para os atalhos de teclado sem `MenuItem` clicável (`Classes/RelayCommand.cs`); e `await janela.ShowDialog<bool>(this)` no lugar de `wnd.Owner = this; wnd.ShowDialog() == true` — o que forçou pelo menos 6 métodos a virar `async void`.

[Certo] Nada disso rodou ainda. Validação feita por script: chaves balanceadas em 39 arquivos `.cs`, e os 15 `.axaml` são XML bem-formado (parseados com `xml.etree.ElementTree`, não só contagem de tags). Isso pega erro de sintaxe, não erro de tipo/API — o próximo `dotnet build` é quem vai dizer se `ContainerFromIndex`, `RangeBaseValueChangedEventArgs`, `TrayIcon.Clicked`, `ObjectConverters.Equal` e companhia existem com esses nomes exatos na versão 11.3.17. Pelo histórico desta conversa, é bem provável que não bata de primeira — manda o erro.

## Rodada 4 de correção via build real (2026-07-26)

1 erro real reportado: `MainWindow.axaml.cs(209,63): CS0246 — RangeBaseValueChangedEventArgs não encontrado`, no handler `ColumnCount_ValueChanged`. Conferi o código-fonte real da tag `11.3.17` (`src/Avalonia.Controls/Primitives/RangeBase.cs`): o tipo existe e o nome está certo, só que mora em `Avalonia.Controls.Primitives`, não em `Avalonia.Controls` (eu tinha assumido que estava no namespace já importado). Corrigido: adicionado `using Avalonia.Controls.Primitives;` em `MainWindow.axaml.cs`.

Aproveitei que já estava com acesso ao código-fonte real da tag pra verificar, antes do usuário rodar de novo, todas as APIs que eu tinha deixado marcadas como não-verificadas no fechamento da Fase 3. Resultado — todas bateram, nenhuma outra correção necessária:

- **`Window.Closing`/`ShowDialog<T>`/`ShowDialog`/`Close(object?)`/`WindowStateProperty`** (`Window.cs`): assinaturas batem exatamente com o que já estava escrito.
- **`TopLevel.Opened`** (`TopLevel.cs`): `event EventHandler? Opened` existe e é herdado por `Window` — bate com `Opened="Window_Opened"` no XAML.
- **`Control.Loaded`/`Control.Unloaded`** (`Control.cs`): existem como `event EventHandler<RoutedEventArgs>?`, ao contrário do que eu temia (não é óbvio que Avalonia replicaria esse conceito do WPF) — bate com `Loaded="Window_Loaded"`, `Loaded="History_Loaded" Unloaded="History_Unloaded"`, `Loaded="Hostname_Loaded"`.
- **`TrayIcon.Clicked`** (`TrayIcon.cs`): `event EventHandler? Clicked` existe, dispara a partir do `ITrayIconImpl.OnClicked` da plataforma — bate com `_trayIcon.Clicked += (_, _) => RestoreFromTray();`.
- **`ItemsControl.ContainerFromIndex(int)`** (`ItemsControl.cs`): existe, retorna `Control?` — bate com o uso em `FocusHostnameAt`.
- **`ObjectConverters.Equal`** (`Avalonia.Base/Data/Converters/ObjectConverters.cs`): existe. E o mais importante, que eu não tinha checado antes: `Avalonia.Data.Converters` está na lista de namespaces do `XmlnsDefinition` da xmlns padrão `https://github.com/avaloniaui` (`Avalonia.Base/Properties/AssemblyInfo.cs`) — então `{x:Static ObjectConverters.Equal}` no XAML resolve sem precisar de prefixo extra. Isso era um risco real que não tinha confirmado.
- **`MenuItem.InputGesture`** (`MenuItem.cs`): descoberta importante — a propriedade é do tipo `KeyGesture?`, não `string`. O XAML usa `InputGesture="F10"`, `"Ctrl+A"`, etc. como string. Isso só funciona porque `KeyGesture` tem um método estático `Parse(string)` (`KeyGesture.cs`), e o compilador XAML do Avalonia usa essa convenção (`Parse(string)` estático público) como conversor implícito de string — mesmo padrão usado por `Point`, `Thickness`, `Color`. Testei o parser manualmente contra as strings usadas (`F10`, `F5`, `F12`, `F2`, `F1`, `Ctrl+A`, `Ctrl+T`, `Ctrl+F`, `Ctrl+N`) e todas resolvem corretamente (chave sem modificador ou com `Ctrl+`).

## Traceroute testado + tray icon não aparece no GNOME (2026-07-26)

Traceroute contra `8.8.8.8` mostrou os primeiros 3 hops como `TimedOut`. Conferido `Probe-Traceroute.cs`: usa o mesmo `Ping.SendPingAsync` com payload customizado + `Ttl` incremental — se fosse falta de `CAP_NET_RAW` apareceria a mesma mensagem de exceção do ping normal (`Unable to send custom ping payload...`), não `TimedOut`. Como apareceu `TimedOut` (um `IPStatus` normal, não uma exceção), o socket raw está funcionando; só não voltou resposta ICMP TTL-exceeded dentro do timeout. [Provável] é comportamento de rede real (roteadores intermediários/NAT/firewall frequentemente não respondem a esses pacotes nos primeiros hops) — não é bug do port. Não investigado mais a fundo porque não é um erro de compilação/execução, é característica de rede do ambiente do usuário.

Tray icon: o aviso do asset (`vmPing-16.png`) sumiu depois da correção da URI, mas o ícone não apareceu na área de notificação do GNOME. Investigado o backend Linux do Avalonia 11.3.17 direto no repo oficial: existem duas implementações, `src/Avalonia.FreeDesktop/DBusTrayIconImpl.cs` (protocolo moderno `StatusNotifierItem` via D-Bus) e `src/Avalonia.X11/XEmbedTrayIconImpl.cs` (protocolo antigo `XEmbed` systray do X11) — o Avalonia tenta um e cai para o outro.

[Provável] Isso não é bug do port nem do Avalonia: o **GNOME Shell removeu suporte nativo a área de notificação por padrão desde a versão 3.26** (2017). Nenhum dos dois protocolos funciona em GNOME puro sem uma extensão — é a mesma limitação que afeta qualquer app GTK/Qt/Electron com tray icon rodando em Ubuntu, Fedora Workstation ou Debian com GNOME (todos vêm sem essa extensão instalada por padrão). Não tem correção possível do lado do código: quem quiser o ícone visível precisa instalar a extensão "AppIndicator and KStatusNotifierItem Support" (pacote `gnome-shell-extension-appindicator` no Debian/Ubuntu, ou via extensions.gnome.org) e reiniciar o Shell.

Ação: documentar isso no `README.md` do port como limitação conhecida, já que todo usuário GNOME sem a extensão vai bater nisso.

## Fase 4 — primeira rodada (2026-07-26)

Escopo real da Fase 4 acabou maior que os 9 stubs originalmente catalogados: o inventário da Fase 1 tinha deixado de fora `UsageWindow`, `NewConfigurationWindow` e `NewAliasWindow` (esta última nem existia como stub — é uma janela distinta de `EditAliasWindow`: pede host+alias para criar, enquanto `EditAliasWindow` só pede alias porque o host já vem de um probe existente).

Portado nesta rodada:

- **`UsageWindow`**: conteúdo estático (uso de CLI), conectado de verdade em `CommandLine.ParseArguments` (antes só escrevia no stderr — débito da Fase 2).
- **`NewConfigurationWindow`**: UI portada e funcional isoladamente, mas **ainda não conectada** a `Configuration.IsReady()` — conectar exigiria tornar `IsReady()`/`Save()` assíncronos, e `Save()` é chamado de dezenas de pontos no código (toda vez que uma opção/alias/favorito muda). Decisão consciente de não fazer esse refactor maior nesta rodada; `IsReady()` continua criando a config no local padrão sem perguntar.
- **`NewAliasWindow`**: criada do zero (host + alias, com validação via `Alias.IsHostInvalid`/`IsNameInvalid`).
- **`EditAliasWindow`**: já era funcional desde a Fase 3; só ajustei os botões pra inglês.
- **`DialogWindow`**: **bug real corrigido** — `OK_Click`/`Cancel_Click` chamavam `Close()` sem argumento, o que faz `ShowDialog<bool>(owner)` sempre voltar `false` (default), pra OK e pra Cancel igual. Não dava problema em `ErrorWindow` (só usado via `.Show()`/`.ShowDialog()` não-genérico, resultado ignorado) mas quebraria silenciosamente `WarningWindow` com `ShowDialog<bool>` — que passou a ser usado agora em `NewFavoriteWindow` (aviso de sobrescrita) e nas duas janelas de "Manage" (confirmação de exclusão). Corrigido para `Close(true)`/`Close(false)`.
- **`MultiInputWindow`**: já era funcional; UI e textos revisados (inglês, header descritivo).
- **`NewFavoriteWindow`**: evoluída de "só título" pra formulário completo (título, colunas, hosts multi-linha), com validação de coluna (1–10), nome inválido, hosts vazios, e aviso de sobrescrita via `DialogWindow.WarningWindow` quando o título já existe.
- **`ManageFavoritesWindow`**: portada do zero — `ListBox` de títulos + painel de conteúdo do favorito selecionado + New/Edit/Remove.
- **`ManageAliasesWindow`**: portada do zero — `ListBox` com `DataTemplate` (host + alias) + New/Edit/Remove.

Decisões técnicas registradas:

- **`DataGrid` do WPF → `ListBox`** em ambas as janelas de "Manage": `Avalonia.Controls.DataGrid` é um pacote NuGet separado, não usado até agora — decidi não adicionar uma dependência nova só por paridade visual quando `ListBox` cobre a função (lista + seleção). Fica pra Fase 5 se fizer falta visualmente.
- **Hack de esconder minimizar/maximizar via `user32.dll`** (presente em `ManageFavoritesWindow`/`ManageAliasesWindow`/`NewFavoriteWindow` originais): removido, é Windows-only P/Invoke, sem equivalente ou necessidade no Linux.
- **Drag-and-drop de arquivo de hosts** (`NewFavoriteWindow`, `MultiInputWindow` originais): não portado, documentado no XAML de cada janela — mesma razão já registrada na Fase 3 pro grid principal (API de DragDrop do Avalonia é assíncrona, port não trivial, fica pra Fase 5).
- **`x:CompileBindings="False"`** no `DataTemplate` de `ManageAliasesWindow`: o item é `KeyValuePair<string,string>` (retorno de `Alias.GetAll()`); escrever a sintaxe genérica correta de `x:DataType` pra isso em XAML (`KeyValuePair(x:String,x:String)`) não foi verificada contra fonte real, e o risco de adivinhar errado não valia a pena — desabilitei compiled bindings só nesse `DataTemplate` em vez de arriscar.
- **Risco não verificado**: os botões Edit/Remove de `ManageFavoritesWindow`/`ManageAliasesWindow` usam `{Binding #Nome.SelectedItem, Converter={x:Static ObjectConverters.IsNotNull}}` (sintaxe curta `#ElementName`) sem `x:DataType` no escopo. [Provável] bindings por `ElementName`/`#` resolvem contra o tipo do controle nomeado (conhecido estaticamente), não contra a `DataContext` ambiente, então não deveriam precisar de `x:DataType` — mas isso não foi confirmado contra o código-fonte do compilador XAML do Avalonia, é inferência. Se `dotnet build` der `AVLN2000` de novo aqui, é o próximo ponto a investigar.

Rode `dotnet build` de novo — mudança grande nesta rodada (`CommandLine.cs` reescrito pra async, `MainWindow.Window_Loaded` virou `async void`, 2 janelas novas, 4 janelas com UI real nova).

## Rodada 6 de correção via build real (2026-07-26)

1 erro real: `UsageWindow.axaml — AVLN1001: An XML comment cannot contain '--'` na linha 12. Causa: escrevi um comentário XAML mencionando a flag `--help` — dois hífens seguidos dentro de `<!-- -->` é XML inválido (regra do próprio formato, não do Avalonia). Erro bobo, meu — deveria ter lembrado dessa regra de XML ao escrever o comentário. Corrigido: reescrevi o texto do comentário pra não usar `--help` literal. Conferi todos os outros `.axaml` tocados nesta rodada via script (busca por `--` fora de `<!--`/`-->`) — só esse arquivo tinha o problema.

Rode `dotnet build` de novo.

## Fase 4 — segunda rodada: IsolatedPingWindow, StatusHistoryWindow, PopupNotificationWindow, HelpWindow (2026-07-26)

Completadas as quatro janelas "menores" restantes da Fase 4 (as que sobravam antes de `TraceRouteWindow`/`FloodHostWindow`/`OptionsWindow`).

- **`IsolatedPingWindow`**: portada do zero — janela dedicada a um único probe (aberta via duplo-clique/menu de contexto num host do grid principal), reaproveita os mesmos conversores de `Probe` (`ProbeStatusToBackgroundBrushConverter` etc.) já usados em `MainWindow`. Histórico mostrado num `TextBox` somente-leitura (`Text="{Binding HistoryAsString, Mode=OneWay}"`) em vez do `AutoScrollListBox` customizado original — mesma simplificação já usada pro grid principal na Fase 3. Linha de estatísticas só visível para `ProbeType.Ping` via `{Binding Type, Converter={x:Static ObjectConverters.Equal}, ConverterParameter={x:Static classes:ProbeType.Ping}}`. Animação de flash ao atualizar e auto-scroll-com-seleção do original: não portados (cosmético, Fase 5). `Window_Closed` limpa `probe.IsolatedWindow` pra evitar referência pendurada.
- **`StatusHistoryWindow`**: portada do zero (original 349+380 linhas) — maior desta rodada. Trocas: `WindowChrome` customizado (botões desenhados à mão, `DragMove`, P/Invoke `WM_GETMINMAXINFO` só existia pra acertar tamanho maximizado considerando a taskbar do Windows) → decoração nativa do Avalonia/gerenciador de janelas Linux, que já resolve isso de graça. `DataGrid` → `ListBox` + `DataTemplate` (mesma decisão de sempre, sem adicionar pacote novo). Filtro (texto + checkboxes Up/Down/Start/Stop) reimplementado como recompute-e-reatribui em `ItemsSource` a cada mudança — não existe `ICollectionView`/`CollectionViewSource.Filter` no Avalonia core. Export: original usava `System.Windows.Forms.SaveFileDialog` (Windows-only); trocado por escrita direta em `~/vmping-status-history.csv`, não `IStorageProvider.SaveFilePickerAsync` — a forma exata de `FilePickerSaveOptions` não deu pra verificar contra o código-fonte nesta sessão (rate limit da API do GitHub), e prefiro entregar uma exportação certa num caminho fixo a arriscar adivinhar a API do picker. Posição/tamanho entre aberturas: mantido via campos estáticos (era código gerenciado puro no original, só a parte de P/Invoke foi removida).
- **`PopupNotificationWindow`**: portada do zero — popup sem moldura no canto inferior direito da tela (`SystemDecorations="None"`, `Topmost="True"`, `ShowInTaskbar="False"`), mostra as últimas mudanças de status, dimensiona a altura conforme o número de itens (95/110/126/147/172px), auto-dismiss configurável via `DispatcherTimer`. Clique restaura a `MainWindow`. Botão ⤢ abre/foca `StatusHistoryWindow` reaproveitando o padrão singleton já usado em `Probe.StatusHistoryWindow`. **[Provável]** posicionamento usa `Screens.Primary?.WorkingArea` — `Screens.Primary` foi confirmado contra o código-fonte real, mas `Screen.WorkingArea` (existência/tipo exato) não, por causa do mesmo rate limit da API do GitHub que afetou o item anterior. Se o build reclamar de `WorkingArea`, é o primeiro lugar a olhar. Animação de entrada (fade/scale) e glifo Marlett: não portados (mesmo padrão de sempre — cosmético/Windows-only).
- **`HelpWindow`**: portada do zero (original 321 linhas de XAML). Trocas: `FlowDocument`/`FlowDocumentScrollViewer` (WPF, sem equivalente no Avalonia core) → `ScrollViewer` + `StackPanel` com o mesmo texto integral, em ordem contínua. `TreeView` de navegação lateral (Intro/Basic Usage/Extra Features/Options/Command Line Usage, cada item chamando `.BringIntoView()`) → removida; vira uma página única rolável na mesma ordem das seções — não é perda de conteúdo, só do atalho de pular direto pra uma seção (fica pra Fase 5 se fizer falta). `Hyperlink` (tipo específico de `FlowDocument`) → `TextBlock` com `PointerPressed` chamando `Process.Start(UseShellExecute: true)` — no Linux isso é obrigatório pra abrir a URL via `xdg-open`; sem `UseShellExecute=true` o .NET tenta executar a URL como binário e lança `Win32Exception` (comportamento de plataforma documentado, não específico do Avalonia). Ícone vetorial `icon.add` → texto "Add Host" em negrito sem ícone. `DropShadowEffect` do título e ícone da janela: não portados (cosmético).
- **`DialogWindow`**: ganhou um novo factory, `InfoWindow(title, message)` — usado pela confirmação de sucesso do Export de `StatusHistoryWindow`. O enum `DialogIcon.Info` já existia desde a Fase 3 mas nunca tinha factory que o usasse.
- **`MainWindow.axaml.cs`**: removido o `TODO` que pedia pra replicar `Topmost = ApplicationOptions.IsAlwaysOnTopEnabled` em `StatusHistoryWindow`/`HelpWindow`/`IsolatedPingWindow` reais — as três já fazem isso em seus próprios construtores agora.

Riscos não verificados desta rodada (ambos por causa de rate limit da API do GitHub, mesma causa raiz da rodada anterior): `Screen.WorkingArea` em `PopupNotificationWindow`, e a forma exata de `FilePickerSaveOptions` (contornada, não usada).

Com isso fecham as quatro janelas "menores" da Fase 4. Faltam `TraceRouteWindow` (janela dedicada, distinta do traceroute inline já funcional no grid principal), `FloodHostWindow`, e por último `OptionsWindow` (a maior, ~1800 linhas somadas).

Checkpoint pedido ao usuário aqui, mas ele mandou `dotnet build`/`dotnet run` real antes de eu confirmar por texto — screenshots mostraram `HelpWindow` (conteúdo certo, seções, comando de linha) e `StatusHistoryWindow` (entradas reais com cor por status, filtros na ordem certa) funcionando de verdade em runtime. Export e Isolated Ping não foram confirmados nessa rodada de teste — ficou registrado como risco não testado, não validado, e segui mesmo assim a pedido do usuário ("continue").

## Fase 4 — TraceRouteWindow (2026-07-26)

Portada `TraceRouteWindow` (original 272+180 linhas, janela dedicada de traceroute — diferente do traceroute inline no grid principal, que já usa `Classes/Probe-Traceroute.cs` desde a Fase 2/3). Trocas:

- `ControlTemplate` customizado do `TextBox` só pra mostrar "Enter target address" como placeholder → `TextBox.Watermark` nativo do Avalonia, zero XAML extra.
- `DataGrid` → `ListBox` + `DataTemplate` (mesma decisão de sempre — Hop / IP Address / RTT em colunas de `Grid`).
- Os 3 `DataTrigger` do original (`HostAddress == "Timed out"/"Invalid hostname"/"0.0.0.0"` → vermelho + esconder RTT) → dois conversores novos em `Classes/ValueConverters.cs` (`HopAddressToBrushConverter`, `HopAddressToRttVisibleConverter`), Avalonia não tem `DataTrigger`.
- Ícone animado piscando no status bar (`Storyboard`/`DoubleAnimation` de opacidade) e ícone do botão Trace/Stop (`BooleanToImageConverter` → recurso vetorial que ainda não existe, `icon.play`/`icon.stop-circle` — **nem estava sendo usado em lugar nenhum do projeto ainda, confirmado por grep antes de decidir não usar aqui também**): não portados, cosmético, Fase 5.
- `IsCancel="{Binding IsActive}"` (convenção do WPF: Escape aciona o botão marcado `IsCancel`, mesmo fora de diálogo modal): sem equivalente no Avalonia, não portado — clicar em Stop continua funcionando normalmente, só perde o atalho de teclado.
- **Correção de tipo, não de sintaxe**: `Classes/NetworkRoute.cs` e `Classes/NetworkRouteNode.cs` (portadas na Fase 2 "quase sem alteração") eram `class` sem modificador (`internal`, padrão do C#). `TraceRouteWindow.axaml` precisa de `x:DataType="classes:NetworkRoute"`/`x:DataType="classes:NetworkRouteNode"` pra bindings compilados — em vez de apostar que o compilador XAML resolve `x:DataType` contra tipo `internal` (não verificado contra o código-fonte do XamlX/Avalonia nesta sessão, mesmo problema de rate-limit de rodadas anteriores), troquei as duas classes pra `public class`. Mudança sem efeito colateral: mesmo assembly, ninguém fora do projeto usa esses tipos de qualquer forma.
- `BackgroundWorker`/`AutoResetEvent` do original: mantidos como estão. `BackgroundWorker` é parte da base do .NET desde o Core 3.0, cross-platform, sem dependência de WinForms/WPF — e já estava compilando sem erro desde os builds verdes da Fase 2 (prova real, não suposição: se não compilasse, `CS0246` já teria aparecido nos builds anteriores, já que `NetworkRoute.cs` sempre esteve no projeto).

Rode `dotnet build` de novo — mudança nova: 1 janela, 2 conversores novos, 2 classes que viraram `public`.

Build confirmado limpo pelo usuário. [Certo] Isso valida sintaxe e tipos — inclusive que `x:DataType` contra tipo `public` (depois da troca de `internal`) resolve sem erro, e que os 2 conversores novos batem com o `IValueConverter` do Avalonia. Não valida runtime: a janela em si (abrir, digitar host, clicar Trace, ver hops aparecendo, cor vermelha em timeout) ainda não foi testada rodando. Seguindo direto pra `FloodHostWindow` como já tinha avisado; a validação de runtime de `TraceRouteWindow` fica pendente pro próximo lote de testes do usuário, junto com Export/Isolated Ping que também ainda não foram confirmados.

## Fase 4 — FloodHostWindow (2026-07-26)

Portada `FloodHostWindow` (original 223+107 linhas). `Classes/FloodHostNode.cs` já era `public class` desde a Fase 2 (ao contrário de `NetworkRoute`/`NetworkRouteNode` na rodada anterior) — não precisou de correção de visibilidade. Trocas:

- Animação de cor de fundo da janela pulsando enquanto o flood está ativo (`ColorAnimation` em `Window.Style`/`DataTrigger`) e texto de status piscando (`Storyboard`) → não portados, cosmético, Fase 5 — o texto "Flood in progress..." aparece/some via `IsVisible="{Binding IsActive}"` (bind direto bool→bool, sem precisar do `BooleanToHiddenVisibilityConverter` que o original usava, porque o alvo já é bool no Avalonia).
- Ícone do botão (`BooleanToImageConverter`) → só texto "Flood Host"/"Stop Flood", mesmo padrão do botão Trace de `TraceRouteWindow`.
- `ControlTemplate` customizado do `TextBox` (só existia pra manter bordas customizadas, sem placeholder) → `TextBox` padrão do Avalonia.
- Truque de sobreposição do original (o `Label` de instrução ocupa `Grid.Row="1" Grid.RowSpan="2"`, por cima da grade de estatísticas, escondido só depois do primeiro clique) → replicado literalmente com um `Border Name="InformationOverlay"` na mesma célula, escondido via `IsVisible = false` no `Click`, igual ao original (`lblInformation.Visibility = Collapsed`, também sem lógica de reexibir — comportamento preservado, não é regressão).
- `StringFormat='{0:#,0}'` nos três contadores (Packets Sent/Received/Lost): mesma sintaxe já confirmada funcionando em `PopupNotificationWindow` (`StringFormat='[{0}]'`), sem o prefixo de escape `{}` do WPF — não é necessário aqui porque `StringFormat` é uma propriedade dentro da extensão `{Binding ...}`, não um valor de atributo no nível raiz.
- `BackgroundWorker` mantido (mesma razão de `TraceRouteWindow`: cross-platform desde .NET Core 3.0, sem `Task.Delay` entre pacotes no loop — é o mesmo comportamento "sem limitador de banda além do timeout do próprio ping" do original).

Com isso fecham as duas janelas dedicadas de traceroute/flood. Falta só `OptionsWindow` (a maior, ~1800 linhas somadas) pra fechar a Fase 4 por completo.

## Rodada 7 de correção via build real (2026-07-26)

1 erro real: `FloodHostWindow.g.cs(14,51): CS0102 — O tipo 'FloodHostWindow' já contém uma definição para 'FloodHost'`. Causa: o botão no XAML tem `Name="FloodHost"`, e o gerador de nomes do Avalonia (`Avalonia.Generators.NameGenerator`) cria automaticamente um campo `FloodHost` no partial class pra cada controle nomeado — eu também tinha escrito um método `public void FloodHost(FloodHostNode node)` no code-behind, mesmo nome, mesma classe parcial. Erro meu — não tinha me atentado que o gerador de nomes cria campos com o `Name` literal do XAML, então qualquer `Name="X"` no XAML colide com um método/propriedade `X` no code-behind. Corrigido: renomeei o método pra `ToggleFlood`.

Antes do usuário rodar de novo, varri todos os outros `.axaml`/`.axaml.cs` tocados nesta sessão (script comparando todo `Name="..."` do XAML contra métodos/propriedades/campos de mesmo nome no code-behind) — nenhum outro caso encontrado. Aproveitei também pra fechar um aviso de nulidade que tinha ficado de fora da limpeza da Rodada 2 (`Classes/NetworkRoute.cs`, `PropertyChangedEventHandler` não anulável).

Rode `dotnet build` de novo.

## Rodada 8 de correção via build real (2026-07-26)

3 erros reais, todos a mesma causa: `FloodHostWindow.axaml` linhas 53/58/63 — `AVLN2000: Unable to resolve type # from namespace`, nos três `StringFormat='{0:#,0}'` dos contadores de pacotes.

Causa: eu tinha decidido explicitamente **não** usar o prefixo de escape `{}` do WPF (`StringFormat='{}{0:...}'`), com base em `PopupNotificationWindow.axaml` funcionar sem ele (`StringFormat='[{0}]'`). Raciocínio errado — a regra não é "escapar sempre" nem "nunca escapar dentro de uma markup extension", é mais simples: **o parser XAML trata qualquer valor de propriedade que comece literalmente com `{` como uma possível markup extension aninhada, ponto**. `[{0}]` começa com `[`, não precisa de escape. `{0:#,0}` começa com `{`, precisa — e sem o escape, o parser tentou resolver `#` como se fosse um nome de tipo depois de separar por `:`. Corrigido para `StringFormat='{}{0:#,0}'` nos três lugares.

Conferi por grep se algum outro `.axaml` desta sessão tem `StringFormat='{...` sem o prefixo — só esses três em `FloodHostWindow.axaml` tinham o padrão, nenhum outro arquivo.

Build confirmado limpo pelo usuário. [Certo] Válido pra sintaxe/tipos das duas janelas novas (`TraceRouteWindow`, `FloodHostWindow`) e pros dois fixes desta rodada. [Provável, não verificado] Ainda não testado em runtime se o `BackgroundWorker.ProgressChanged` (usado nas duas janelas) marshal corretamente pra UI thread sob o `SynchronizationContext` do Avalonia — o raciocínio é que `new BackgroundWorker()` é sempre construído na UI thread (dentro do construtor da janela, chamado a partir de um clique de menu), então o `SynchronizationContext.Current` capturado nesse momento já é o `AvaloniaSynchronizationContext` instalado pelo dispatcher da UI; isso não foi confirmado contra o código-fonte do Avalonia nesta sessão. Continua pendente de teste real: abrir `Traceroute`/`Flood Host` e ver se os hops/contadores atualizam ao vivo (não só travam até o fim), além de `Export`/`Isolated Ping` que já estavam pendentes de rodadas anteriores.

Seguindo para `OptionsWindow` (a última e maior janela da Fase 4) — vai ser um lote grande antes do próximo checkpoint de build, dado o tamanho (~1800 linhas somadas).

**Runtime confirmado por screenshot** (2026-07-26): `TraceRouteWindow` rodando de verdade — hop "Timed out" em vermelho (`HopAddressToBrushConverter` funcionando), status bar "Tracing route..." visível. `FloodHostWindow` rodando com **794 pacotes enviados/recebidos, 0 perdidos** — esse volume só é possível com atualização ao vivo real dos contadores, o que confirma que `BackgroundWorker.ProgressChanged`/o binding de `PacketsSent`/`PacketsReceived` marsham corretamente pra UI thread sob o `SynchronizationContext` do Avalonia. Isso fecha o risco [Provável] que eu tinha registrado sobre esse ponto — vira [Certo]. `Export`/`Isolated Ping` continuam sem confirmação.

## Fase 4 — OptionsWindow, a última janela (2026-07-26)

Portada `OptionsWindow` (original 898+931 linhas — a maior janela do app, e a última pendente da Fase 4). 8 abas: General, Advanced, Notifications, Email, Sounds, Logging, Display, Colors. Decisões técnicas registradas (a versão completa está no cabeçalho-comentário do próprio `.axaml`):

- **Visibilidade condicional** (painéis que aparecem/somem conforme um checkbox/radio/combobox): o original usava `DataTrigger` com `Binding ElementName=X`. Em vez de arriscar a sintaxe `{Binding #Nome.Prop}` sem `x:DataType` — ainda não confirmada contra um build real nesta sessão (risco aberto desde `ManageFavoritesWindow`/`ManageAliasesWindow`) — resolvi tudo de forma imperativa: handlers de `Click`/`Checked`/`SelectionChanged` alterando `IsVisible` diretamente. Mais código, zero risco de sintaxe novo numa janela que já é a maior superfície de risco do port.
- **Guarda `_isReady`** em vez de `if (IsLoaded)`: o WPF tem `FrameworkElement.IsLoaded`; não achei confirmação de um equivalente direto no Avalonia. Troquei por um campo `bool` setado ao final do construtor — mesmo efeito (impedir que os handlers de `RadioButton.Checked`/`ComboBox.SelectionChanged` rodem durante a construção da árvore XAML, antes de controles "irmãos" declarados mais abaixo no arquivo estarem garantidamente atribuídos), sem depender de API não verificada.
- **`ComboBox.Text`** (usado no original pra ler/escrever o texto de `PingIntervalUnits`/`InitialFavorite` sem `IsEditable`): evitado por completo — troquei por `SelectedIndex`/`SelectedItem` + leitura do `Content` do `ComboBoxItem` selecionado (`GetComboText`), API que tenho certeza que existe, em vez de confiar que `ComboBox.Text` do Avalonia se comporta como o do WPF nesse cenário não-editável.
- **`Tag` em `ComboBoxItem`** (usado no original pra saber se o modo de início selecionado era "Standard" ou "Favorite"): evitado — não tinha certeza de que `Control.Tag` existe no Avalonia (não é um dado que eu tenha confirmado nesta sessão). Troquei por comparação direta de `SelectedIndex` contra o enum `ApplicationOptions.StartMode`.
- **Amostra de cor ao vivo** (`Border.Background` bound ao `Text` de cada `TextBox` de cor, 20 instâncias) e **`Label` com estilo `LabelToolTip`** (ícone + tooltip): não portados — a primeira por ser mais uma instância do risco de binding por `ElementName` acima; a segunda porque o estilo vem de `ResourceDictionaries/*.xaml` (Fase 5). Troquei os tooltips por `ToolTip.Tip` nativo, direto no controle — mantém o texto de ajuda, só perde o ícone.
- **`PasswordBox`** (WPF) → `TextBox` com `PasswordChar="●"`; senha lida via `.Text` em vez de `.Password`/`.SecurePassword`. Pro `SendTestEmail` (que espera `SecureString`, assinatura já portada na Fase 2), construo o `SecureString` manualmente a partir do texto.
- **`FolderBrowserDialog`/`OpenFileDialog`** (`System.Windows.Forms`, Windows-only): trocados por `IStorageProvider.OpenFolderPickerAsync`/`OpenFilePickerAsync` (API real do Avalonia 11.x). **[Provável, NÃO verificado nesta sessão]** — tentei confirmar a forma exata de `FolderPickerOpenOptions`/`FilePickerOpenOptions`/`FilePickerFileType` contra o código-fonte, mas a rede ficou indisponível (timeout) bem na hora de checar. Diferente do caso do CSV export (Fase 4, rodada anterior), aqui não dava pra substituir por um caminho fixo sem perder função de verdade — então implementei pela minha melhor referência da API real e documentei o risco. Se o build reclamar de algum desses três tipos, é o primeiro lugar a olhar.
- **`SoundPlayer`** nos botões "Test" de áudio: reaproveitado `Probe.PlaySound` (já portado na Fase 2 pro alerta de verdade, só mudei de `private` pra `internal` pra ficar acessível daqui — mesmo assembly, sem mudança de comportamento).
- **`DllImport user32.dll`** (esconder botões minimizar/maximizar via `SourceInitialized`): não portado, Windows-only, sem equivalente/necessidade no Linux.
- **`PreviewTextInput`** (bloquear tecla inválida durante a digitação, em ~6 campos numéricos + cores HTML): não portado — a validação de verdade (Regex, faixa de valores) continua idêntica ao original no momento do Save; só perde o bloqueio em tempo real de tecla.
- Toda a lógica de validação/gravação em `ApplicationOptions` (`SaveGeneralOptions`, `SaveAdvancedOptions`, `SaveEmailAlertOptions` etc.) foi portada **linha a linha**, incluindo comportamentos "esquisitos" do original que decidi não "corrigir" (ex: `InitialProbeCount > 20` vira `2`, não o valor máximo) — mesma disciplina de fidelidade usada em todo o port até aqui.

Antes de escrever, varri Name-vs-método em todos os `.axaml`/`.axaml.cs` tocados nesta sessão (mesmo script da Rodada 7) — zero colisão desta vez, incluindo os ~70 controles nomeados desta janela.

Com isso, **as 17 janelas secundárias da Fase 4 estão todas portadas**. Falta: rodar `dotnet build` (o maior lote de mudança desta fase inteira — 1 janela nova de ~750 linhas somadas, `Classes/Probe-Util.cs` com 1 método que virou `internal`), depois um novo lote de testes reais cobrindo pelo menos: abrir Options, navegar pelas 8 abas, salvar com sucesso, forçar pelo menos um erro de validação, testar Browse (pasta e arquivo — é onde mora o risco não verificado).

## Bug real reportado: TraceRouteWindow travava em "Tracing route..." (2026-07-26)

Usuário reportou por screenshot: hop 1 aparecia como "Timed out" (esperado — mesmo comportamento já visto no traceroute inline do grid principal, alguns roteadores não respondem a TTL baixo) e depois disso o trace travava indefinidamente, sem nenhum hop novo nem mudança na barra de status. `traceroute` do terminal Linux funcionando normalmente contra o mesmo host — descartando problema de rede/DNS.

[Certo] Causa raiz: `BackgroundThread_TraceRoute` tinha `catch { break; }` ao redor de cada `ping.Send` — herdado do original sem alteração. No Windows isso é inofensivo (raramente lança pra um IP válido); no Linux é o caminho mais provável de falha de verdade: se o binário perder a capability `cap_net_raw` (acontece a cada `dotnet build` novo — precisa de `sudo setcap cap_net_raw+ep` de novo, como documentado desde a primeira execução real desta sessão), a partir do próximo hop `ping.Send` lança a mesma exceção "Unable to send custom ping payload..." já vista antes — e o `catch` engolia isso silenciosamente, sem nunca reportar nada pra UI. Resultado: a barra de status ficava presa em "Tracing route..." pra sempre, sem nenhuma pista do que aconteceu.

Corrigido em `UI/TraceRouteWindow.axaml.cs`:
- O `catch` agora captura a exceção de verdade e reporta via `bgWorker.ReportProgress(-2, ex.Message)`, exibido na barra de status como `"• Error: <mensagem>"`.
- Caso novo, que também não existia no original: se o laço termina naturalmente (30 hops esgotados sem nunca receber `IPStatus.Success`), agora reporta `-3` e mostra "Trace ended (max hops reached...)" — antes a barra de status também ficava presa em "Tracing route..." nesse cenário, só que silenciosamente, e ninguém tinha notado porque não é o caminho mais comum de teste.
- Bônus (bug latente, não relacionado ao reportado): `BackgroundThread_ProgressChanged` fazia `pingReply.Status`/`.Address` sem checar se `e.UserState as PingReply` deu `null` — improvável de acontecer mas era uma NRE esperando pra acontecer; adicionado guard.

[Provável] Ainda não confirmado se o `setcap` ausente era de fato a causa (vs. simplesmente lentidão — cada hop com timeout leva ~4s com o retry, e sabemos que pelo menos os primeiros hops costumam dar timeout nessa rede). Pedido ao usuário: reaplicar `sudo setcap cap_net_raw+ep bin/Debug/net8.0/linux-x64/vmping` depois do próximo build e testar de novo — agora, se ainda travar, a barra de status vai dizer exatamente por quê.

**Confirmado por screenshot** (2026-07-26): depois do `setcap`, `TraceRouteWindow` completou o trace de ponta a ponta contra `8.8.8.8` — 7 hops "Timed out" (vermelho) + hop 8 com o IP de destino (verde, `[7 ms]`) + "★ Trace complete" na barra de status. Comparado lado a lado com `traceroute 8.8.8.8` real do terminal Linux: mesmo padrão exato de hops respondendo (o `traceroute` do SO usa 3 sondas por hop e mostra os IPs intermediários reais nos hops 1–7, enquanto o app mostra "Timed out" nesses mesmos hops — [Provável] esses roteadores intermediários respondem ao `traceroute` do SO, que usa UDP/pacotes diferentes, mas não respondem da mesma forma ao ICMP com payload customizado do `Ping.Send`; **não é mais um bug**, é uma diferença de técnica de sondagem entre as duas ferramentas, não um defeito do port). [Certo] Confirma: hipótese do `setcap` estava certa, `HopAddressToBrushConverter` funciona (vermelho/verde), `BackgroundWorker.ProgressChanged` itera corretamente por todos os 8 hops sem travar, e o novo caminho de tratamento de erro não interferiu no caminho de sucesso.

**Status da Fase 4: as 17 janelas estão portadas E `TraceRouteWindow` tem confirmação real de runtime completa, incluindo o caminho de erro.** Ainda sem confirmação de runtime: `OptionsWindow` (nenhuma tela testada ainda — é onde mora o risco não verificado do `IStorageProvider`), `FloodHostWindow` (só o "acontece"/contadores, não os botões Browse/Test que ele não tem, então esse já está coberto), `Export`/`Isolated Ping` de rodadas anteriores.

## Descoberta real: Ping+TTL não funciona pra traceroute no Linux — TraceRouteWindow reescrita (2026-07-26)

Usuário pediu que o traceroute do app mostrasse o caminho completo igual ao `traceroute` do terminal Linux (IP + reverso de cada salto, com menos informação — sem RTT). Investigando por que o app só via "Timed out" em todo hop que não fosse o destino final (enquanto o `traceroute` do terminal via os 8 saltos normalmente), fui direto no código-fonte real do `dotnet/runtime` (`Ping.RawSocket.cs`, tag `v8.0.8`, via busca em grep.app já que a API do GitHub seguiu instável nesta sessão):

[Certo] **Causa raiz confirmada, não é bug do port**: em `GetRawSocket` (`Ping.RawSocket.cs`), há este trecho:
```csharp
if (NeedsConnect && ...) { socket.Connect(socketConfig.EndPoint); }
```
e `NeedsConnect => OperatingSystem.IsLinux()` (`Ping.Unix.cs`). Ou seja: **no Linux, e só no Linux**, o socket ICMP raw usado por `System.Net.NetworkInformation.Ping` é conectado (`connect()`) ao endereço de destino "to scope responses only to the target address". Um socket raw conectado no Linux só aceita pacotes cujo endereço de origem bate com o peer conectado — então qualquer resposta ICMP Time Exceeded vinda de um roteador intermediário (endereço de origem diferente do destino final) é descartada pelo kernel antes mesmo de chegar no código gerenciado do .NET. Resultado: `Ping.Send`/`SendPingAsync` com `PingOptions.Ttl` variável nunca recebe resposta de hop nenhum, exceto o hop que efetivamente é o destino (cujo endereço de origem bate com o socket conectado) — exatamente o padrão observado (só o hop 8, o destino, aparecia; 1-7 sempre "Timed out", mesmo respondendo normalmente ao `traceroute` real).

Isso **também explica retroativamente** uma conclusão errada registrada numa rodada anterior desta sessão ("Traceroute testado + tray icon não aparece"): os "3 primeiros hops como TimedOut" do traceroute inline do grid principal (`Classes/Probe-Traceroute.cs`, que usa a mesma API `SendPingAsync` com `PingOptions.Ttl`) tinham sido atribuídos a "comportamento de rede real, roteadores que não respondem" — [Provável] essa explicação estava incompleta ou errada; é a mesma limitação de socket conectado no Linux, não os roteadores estarem em silêncio. **`Probe-Traceroute.cs` (traceroute inline do grid principal) tem a mesma limitação e não foi corrigido nesta rodada** — só `TraceRouteWindow` (a janela dedicada, que foi o que o usuário pediu). Fica registrado como pendência conhecida; avisar o usuário.

**Correção**: `UI/TraceRouteWindow.axaml.cs` foi reescrita pra chamar o utilitário `traceroute` do sistema operacional (`Process` + `ArgumentList = ["-q","1","-w","2",host]`, saída lida linha a linha via `OutputDataReceived`/regex) em vez de `Ping`+`PingOptions.Ttl` — mesmo padrão já usado em `Classes/Probe-Util.cs` pro áudio (API .NET não cobre o cenário Linux → chama o utilitário nativo). Efeito colateral bom: a saída passa a bater exatamente com o `traceroute` real, incluindo resolução reversa de DNS (não usei `-n`) — que é literalmente o que foi pedido. Coluna de RTT removida do `ListBox` (info a menos, por pedido explícito do usuário); volta a ter só Hop + `nome (ip)` por linha.

Consequência de empacotamento: adicionada dependência `traceroute` em `packaging/debian/control` (`Depends:`), já que a janela agora depende dele de verdade — sem o pacote instalado, o botão Trace mostra um erro na barra de status em vez de travar silenciosamente (`ProcessStartInfo`/`Process.Start()` lança `Win32Exception` "No such file or directory", capturado e reportado).

Rode `dotnet build` de novo — mudança cirúrgica (1 janela reescrita, sem afetar as outras 16, sem afetar `Classes/NetworkRoute.cs`/`NetworkRouteNode.cs`).

**Confirmado por screenshot** (2026-07-26): `TraceRouteWindow` agora mostra os 8 hops reais (`_gateway (172.16.0.65)`, `10.99.99.2`, ..., `as15169.saopaulo.sp.ix.br (187.16.216.55)`, ..., `dns.google (8.8.8.8)`), com resolução reversa de DNS onde existe PTR e só o IP quando não existe — bate exatamente com a saída do `traceroute` do terminal mostrada antes pelo usuário. Confirma o diagnóstico (socket conectado no Linux) e a correção (shell-out) 100%. Na mesma leva de screenshots, o traceroute inline do grid principal (`Classes/Probe-Traceroute.cs`) segue com o bug antigo à vista (hops 3-7 "TimedOut", só o hop 8 responde) — confirma visualmente que a mesma limitação afeta os dois caminhos, exatamente como registrado acima. Ainda não corrigido, aguardando decisão do usuário.

**Usuário pediu pra corrigir também.** `Classes/Probe-Traceroute.cs` reescrito com a mesma abordagem (`Process` + `traceroute -q 1 -w 2 <host>`), adaptado ao estilo `async`/`CancellationToken` do arquivo (diferente de `TraceRouteWindow`, que usa `BackgroundWorker`) — usei `await process.StandardOutput.ReadLineAsync()` num laço em vez de `OutputDataReceived` + `Dispatcher.Post`, porque aqui a continuação de `await` já retoma na UI thread automaticamente pelo `SynchronizationContext` capturado (mesma razão pela qual `AddHistory` já funcionava sem marshaling explícito depois de `await ping.SendPingAsync(...)` no código antigo — mantive esse padrão em vez de trazer manipulação de thread nova). Cancelamento (parar o probe) tratado via `cancellationToken.Register(() => TryKill(process))`, que mata o processo se o usuário parar o probe no meio do trace. `RedirectStandardError` deliberadamente **não** habilitado aqui (diferente de `TraceRouteWindow`) — não leio esse stream, e não redirecioná-lo evita qualquer risco de o processo travar escrevendo num pipe de erro cheio que ninguém está drenando.

Formato de cada hop no histórico do probe: mantive o RTT (diferente da janela dedicada, onde o usuário pediu explicitamente pra tirar) porque aqui não houve pedido de simplificar — só a correção do bug. `nome (ip)` quando há resolução reversa, só `ip` quando não há, `[X ms]` quando a sonda respondeu, "Timed out" quando não.

Rode `dotnet build` de novo — 1 arquivo reescrito (`Probe-Traceroute.cs`), mesma dependência `traceroute` do `.deb` já cobre os dois caminhos.

## Colorir o probe de traceroute (verde/vermelho) igual ping/tcp (2026-07-26)

Usuário confirmou que o traceroute inline do grid funcionou e pediu o mesmo tratamento visual que ping/tcp já têm: fundo verde quando o trace chega no destino, vermelho quando não chega ou dá erro (via `ProbeStatusToBackgroundBrushConverter`, `Up`→`#859900`, `Down`→`#dc322f`, já existente e usado por `Probe-Icmp.cs`/`Probe-Tcp.cs` sem alteração nenhuma).

[Certo] Complicador real: `traceroute` não imprime nenhum indicador explícito de sucesso/falha — ele só para de imprimir linhas, seja porque chegou ao destino, seja porque esgotou o `-m <hops>` padrão sem chegar. Não dá pra decidir a cor olhando "o processo terminou com código X" (o utilitário sai com 0 nos dois casos). Solução: resolver o IP de destino uma vez no início (`ResolveDestinationIp`, `IPAddress.TryParse` com fallback pra `Dns.GetHostAddressesAsync`) e comparar contra o IP do último hop que a regex conseguiu parsear (`lastHopIp`, agora capturado por `ParseHopLine`, que passou a devolver `(string Text, string? Ip)?` em vez de só a string formatada). Se baterem, `ProbeStatus.Up`; senão, `ProbeStatus.Down`.

[Certo] Cuidado que já tinha sido registrado antes pra `StopProbe`, agora aplicado aqui de propósito: `StartStop()` chama `StopProbe(ProbeStatus.Inactive)` de forma síncrona assim que o usuário clica em Parar — isso já seta `Status`/`IsActive` antes do `PerformTraceroute` (que é `async void`) perceber o cancelamento e desenrolar. Se eu setasse `Status = Up/Down` sem checar, ia sobrescrever a cor "Inactive" que o próprio clique do usuário já tinha aplicado, com uma cor de sucesso/falha desatualizada. Por isso todo `Status = ProbeStatus.Up/Down` novo (caminho de sucesso, "não conseguiu iniciar o processo", `catch`) está condicionado a `!cancellationToken.IsCancellationRequested`.

Verificado por script (chaves/parênteses balanceados: 42/42, 75/75) — ainda sem confirmação de build real nem de runtime pra esta mudança específica.

Rode `dotnet build` de novo e teste: iniciar um traceroute contra um host que responde (ex. `8.8.8.8`) deve terminar com o fundo do probe verde; contra um host que não responde ou não existe, vermelho. Também vale conferir que **parar manualmente** um traceroute em andamento continua deixando o probe na cor de "Inactive" de sempre, não verde/vermelho.

**Confirmado pelo usuário** ("tudo correto", 2026-07-26) — [Chutando] não ficou explícito se cobriu os três cenários (verde/vermelho/parar-no-meio) ou só o build+caso feliz; registrado como confirmado a partir do relato do usuário, sem detalhamento por escrito de qual caminho foi exercitado.

## OptionsWindow testada em runtime + bug real de áudio (2026-07-26)

Usuário abriu `OptionsWindow` de verdade pela primeira vez (aba General, depois Sounds). Aba General renderiza correta (dropdown de unidade de intervalo, checkbox "Save as vmPing defaults", startup mode, contadores). Segundo print mostrava um arquivo de log (`~/ping/8.8.8.8.txt`) sendo escrito ao vivo pelo app — confirma que `WriteToLog`/log de probe individual funciona em runtime, não só compila.

**Bug real reportado**: "os sons fizeram um barulho horrível" ao clicar Test na aba Sounds, testando os áudios padrão (`Constants.DefaultAudioDownFilePath`/`DefaultAudioUpFilePath`, ambos `.oga`, Ogg Vorbis).

[Certo] Causa raiz: `ResolveSoundPlayerCommand` (`Classes/Probe-Util.cs`) tentava `paplay` → `aplay` → `ffplay`, nessa ordem, sem olhar pra extensão do arquivo. `aplay` (alsa-utils) só decodifica WAV/PCM cru — não entende Ogg/MP3. Quando o arquivo não é WAV, `aplay` não recusa a tocar: ele empurra os bytes crus do container Ogg pro DAC como se fossem PCM (parâmetros default, tipicamente 8-bit/8kHz mono) — o clássico resultado de "tocar um .mp3 no aplay" é ruído/estática horrível, exatamente o relatado. [Provável] o sistema do usuário não tinha `paplay` disponível (ou a checagem `which paplay` falhou por outro motivo), então caiu direto no `aplay` pro `.oga` padrão.

Corrigido em `Classes/Probe-Util.cs`: `ResolveSoundPlayerCommand` agora recebe o `path` e monta a lista de candidatos condicionada à extensão — `aplay` só entra na lista pra arquivos `.wav` (onde ele decodifica certo); pra qualquer outra extensão a lista é `paplay` → `ffplay`, nunca `aplay`. Mensagem de erro (quando nenhum player compatível é encontrado) também ficou mais específica pro caso não-WAV, mencionando que `aplay` não serve e sugerindo instalar `ffmpeg` como alternativa a `pulseaudio-utils`.

Aproveitado pra corrigir um problema relacionado, notado ao ler o código do botão Browse de áudio (`OptionsWindow.axaml.cs`, `AudioFileBrowse`): o filtro do `FilePickerOpenOptions` só listava `*.wav`, escondendo os próprios arquivos padrão do app (`.oga`) do seletor. Ampliado pra `*.wav, *.oga, *.ogg, *.mp3` (mais "Todos os arquivos", que já existia).

**Ainda em aberto, preciso que o usuário confirme**: o comentário "eu digitei, mas seria interessante poder abrir e selecionar o diretorio" não deixou claro se o botão Browse foi clicado e não fez nada, ou se simplesmente não foi tentado. O código (`BrowseLogPath_Click`/`BrowseLogStatusChangesPath_Click`/`AudioFileBrowse`, todos via `TopLevel.GetTopLevel(this)?.StorageProvider`) está sintaticamente correto e compila, mas é exatamente o `[Provável]`/API não verificada contra fonte desde a Fase 4 — só um clique real confirma se o picker abre.

Verificado por script (chaves/parênteses balanceados nos dois arquivos tocados) — build real e teste de áudio ainda pendentes.

Rode `dotnet build` de novo, teste o botão "Test" de áudio (deve tocar limpo agora, sem estática) e clique especificamente em "Browse..." (tanto na aba Sounds quanto na aba Logging) pra eu saber se o seletor de arquivo/pasta abre de verdade.

### Alerta sonoro em transição real: confirmado funcionando (2026-07-26)

Sequência de testes reais até fechar isso:

1. Fix do `aplay`/Ogg confirmado — "Test" na aba Sounds tocou limpo.
2. Stop/Start de um probe não tocou som — **não é bug**: `Probe-Icmp.cs` (linhas 60-64 e 135-142) trata o primeiro estado observado de um probe novo (seja `Up` ou `Down`) como silencioso de propósito, sem passar por `OnStatusChange` — só transições entre estados já estabelecidos (`Up`↔`Down`) disparam alerta. Corrigi minha própria sugestão de teste depois de reler o código: apontar um probe novo pra um host que nunca responde bate exatamente nesse caminho silencioso, não prova nada sobre o alerta.
3. Teste correto (desligar Wi-Fi com um probe já `Up` contra `8.8.8.8`) reproduziu a transição de verdade — popup próprio do app (`PopupNotificationWindow`) apareceu mostrando "8.8.8.8 → down", cor vermelha no grid — mas sem som ainda.
4. [Provável] Hipótese levantada: `TriggerStatusChange` se autodesativa (`ApplicationOptions.IsAudioDownAlertEnabled = false`) na primeira exceção de `PlaySound`, e o erro é mostrado numa janela não-modal (`Util.ShowError` usa `.Show()`) fácil de ficar escondida atrás de outras janelas — provável que uma falha de um teste anterior (antes do fix do `aplay`) tenha desarmado o alerta silenciosamente.

**Confirmado pelo usuário**: "sons funcionando perfeitamente" — depois de reabilitar/testar de novo, o alerta sonoro dispara corretamente em transição real de status. Fecha o item de áudio da Fase 4 por completo: `PlaySound`, seleção de player por formato, alerta em transição real (não só o botão Test) — tudo confirmado em runtime.

**Ainda em aberto**: confirmação de que os botões "Browse..." (seletor de pasta/arquivo via `IStorageProvider`) realmente abrem um diálogo — não testado explicitamente ainda, é o último risco não verificado da Fase 4.

### Bug real e grave: clicar em "Browse..." derrubava o app inteiro (2026-07-26)

Usuário testou o botão Browse e reportou stack trace + app fechado:
```
Unhandled exception. Tmds.DBus.Protocol.DBusException: org.freedesktop.DBus.Error.AccessDenied:
Portal operation not allowed: Unable to open /proc/146585/root
  at Avalonia.FreeDesktop.DBusSystemDialog.OpenFolderPickerAsync(...)
  at Avalonia.Platform.Storage.FallbackStorageProvider.OpenFolderPickerAsync(...)
  at vmPing.UI.OptionsWindow.BrowseLogPath_Click(...)
```

[Certo] Duas causas empilhadas, uma de cada lado:

1. **Causa imediata do crash (bug real do port, corrigido agora)**: os três handlers de Browse (`BrowseLogPath_Click`, `BrowseLogStatusChangesPath_Click`, `AudioFileBrowse`) chamavam `IStorageProvider.OpenFolderPickerAsync`/`OpenFilePickerAsync` sem nenhum `try/catch`. Uma exceção não capturada dentro de um handler de evento assíncrono do Avalonia sobe até o loop de dispatch (`Dispatcher.ExecuteJob`) e é fatal — derruba o processo inteiro, não só a janela. Isso não é específico do erro de portal visto aqui: qualquer falha nesse caminho (D-Bus fora do ar, sessão sem portal configurado, timeout) teria o mesmo efeito catastrófico.
2. **Causa do erro em si (ambiente, fora do nosso controle)**: `AccessDenied: Portal operation not allowed: Unable to open /proc/<pid>/root`, vindo de `Avalonia.FreeDesktop.DBusSystemDialog` — o Avalonia tentou o portal `org.freedesktop.portal.FileChooser` via D-Bus (`FallbackStorageProvider`, ou seja, o diálogo nativo GTK direto não estava disponível e ele caiu pro portal) e o `xdg-desktop-portal`/`xdg-document-portal` recusou o pedido. [Provável] é uma política de AppArmor específica de algumas distros recentes que nega ao portal ler `/proc/PID/root` pra verificar o processo chamador quando o app não é empacotado como Flatpak/Snap — problema de configuração do sistema do usuário, não do código do vmPing.

**Fix**: os três handlers agora envolvem a chamada do picker em `try/catch`; em caso de falha, mostram um erro amigável ("Não foi possível abrir o seletor... Digite o caminho manualmente") via o mesmo `ShowError`/`DialogWindow` já usado no resto da janela, em vez de deixar a exceção subir. Novo helper `ShowFolderPickerError(Exception)`. O app não crasha mais nesse caminho, ponto final — mesmo que o picker nunca funcione neste ambiente específico do usuário, o pior caso agora é "mostra um erro, você digita o caminho manualmente", que já é comprovadamente funcional (log path, áudio, etc., todos testados via digitação direta).

Não tentei corrigir a causa raiz do `AccessDenied` (é configuração de sistema/AppArmor, não código); documentando aqui como limitação conhecida de ambiente pro README, junto da limitação já registrada do tray icon no GNOME.

Verificado por script (chaves/parênteses balanceados em `OptionsWindow.axaml.cs`: 145/145, 297/297) — build real ainda pendente.

Rode `dotnet build` de novo e clica em Browse outra vez — agora, mesmo que o portal continue recusando, esperado é aparecer uma janela de erro em vez do app fechar.

**Confirmado por screenshot** (2026-07-26): clicou em Browse na aba Logging, o portal recusou de novo com o mesmo `AccessDenied` (limitação de ambiente, esperada), mas dessa vez o app mostrou o diálogo de erro (`"Não foi possível abrir o seletor... Digite o caminho manualmente"`) e continuou rodando normalmente — sem crash. Fix confirmado em runtime.

**Fase 4 encerrada de vez**: as 17 janelas estão portadas, com confirmação de runtime cobrindo os pontos de maior risco — traceroute (janela + inline, incluindo cor de sucesso/falha), áudio (formato de arquivo + transição real de status), e agora o seletor de arquivo/pasta (com fallback seguro quando o portal do sistema recusa). Nenhum crash pendente conhecido.

### Ajuste no fallback do seletor: preencher caminho padrão em vez de deixar em branco (2026-07-26)

Usuário pediu, depois de ver o erro do portal: "mude para ele abrir o diretorio do usuario ou o /tmp". [Provável] correção de premissa necessária aqui: o `AccessDenied` acontece na abertura do próprio diálogo do portal (falha verificando o processo chamador via `/proc/<pid>/root`), antes de qualquer pasta ser escolhida — não tem como "mandar abrir outro diretório" pra contornar, porque a falha independe do que seria pedido ao seletor.

O que resolve o incômodo de verdade (digitar o caminho do zero toda vez que o picker falha): `BrowseLogPath_Click`/`BrowseLogStatusChangesPath_Click` agora passam o próprio `TextBox` pro novo parâmetro `fallbackTarget` de `ShowFolderPickerError`; se o campo estiver vazio no momento da falha, é preenchido com um padrão (`Environment.GetFolderPath(SpecialFolder.UserProfile)`, com `/tmp` como reserva se a pasta do usuário não existir/não resolver) — pra `LogStatusChangesPath` o padrão é `<pasta>/vmping-status.txt`, igual ao caminho que o picker bem-sucedido já monta. O botão de áudio (`AudioFileBrowse`) não recebeu esse fallback: os campos de áudio já vêm preenchidos com os defaults do freedesktop sound theme (`Constants.DefaultAudio*FilePath`), preencher por cima seria pior, não melhor. Mensagem de erro ajustada pra só mencionar "preenchi um caminho padrão" quando isso de fato aconteceu.

Verificado por script (chaves/parênteses balanceados: 151/151, 309/309) — build real ainda pendente.

Rode `dotnet build` de novo e testa o Browse de Log Output de novo: com o campo vazio, o erro deve continuar aparecendo (o portal ainda vai recusar), mas agora o campo de texto deve vir preenchido com sua pasta pessoal (ou `/tmp`) em vez de ficar em branco.

### Bug do portal identificado como upstream conhecido, não perseguido mais (2026-07-26)

Investigação real via `journalctl --user -u xdg-desktop-portal -b`: sem nenhuma linha `DENIED` de AppArmor (descarta essa hipótese anterior). A pista real: `Realtime error: Could not get pidns for pid <N>: Could not fstatat ns/pid: Não é um diretório`, nos horários exatos dos cliques em Browse. Busca confirmou: é bug documentado do próprio `xdg-desktop-portal` (`xdp_pidfd_get_namespace()` em `xdp-utils.c`, chamada com um pidfd anônimo onde a função espera um fd de diretório tipo `/proc/$pid/task/$pid`) — issues abertos `flatpak/xdg-desktop-portal` #1653 e #1756, reproduzido em apps completamente não relacionados ao vmPing (mpv, YouTube no navegador). [Provável] mesma função utilitária compartilhada por múltiplas interfaces do portal, então o `AccessDenied` do `FileChooser` e o log rotulado "Realtime" plausivelmente compartilham a causa raiz, mesmo sem confirmação linha-a-linha do código-fonte do portal. Sem fix upstream publicado nos issues encontrados; único alívio relatado é `systemctl --user restart xdg-desktop-portal.service` (temporário, até o próximo boot).

Decisão: não perseguir mais — é bug de terceiro (o portal do sistema), fora do código do vmPing, sem fix disponível. O fallback já implementado (não crasha, preenche caminho padrão) é a solução completa do lado do app.

### IsolatedPingWindow e Export confirmados em runtime (2026-07-26)

Últimos dois pontos sem confirmação de runtime da Fase 4, fechados por screenshot:

- **`IsolatedPingWindow`**: abriu com título/hostname corretos, histórico de ping ao vivo (réplicas com timestamp e RTT), cor de fundo verde (`Up`) e estatísticas `Sent/Received/Lost` atualizando — tudo via `Probe` reaproveitado do grid principal, como esperado.
- **`Export` (`StatusHistoryWindow`)**: escreveu `vmping-status-history.csv` em `Environment.SpecialFolder.Personal` (resolveu pra `/home/everton/Documentos`, confirmando que a resolução de pasta localizada via XDG user dirs funciona certo no .NET/Linux) e mostrou o diálogo de confirmação com o caminho. Não passa pelo `IStorageProvider`, então não esbarra no bug do portal documentado acima — caminho fixo, sem seletor.

**Fase 4 encerrada por completo, sem nenhum item de runtime pendente.** As 17 janelas portadas, todas testadas de verdade (não só compiladas): grid principal, traceroute (janela + inline, com cor de sucesso/falha), flood host, options (todas as abas com risco real — áudio, browse — testadas e com fallback seguro), isolated ping, status history + export, help, e as janelas de favoritos/aliases/config já confirmadas em rodadas anteriores.

Próximos blocos: Fase 5 (estilos/ícones/drag-and-drop/animações) e Fase 6 (empacotamento `.deb` final verificado).

## Fase 5 — rodada 1: tema claro, ícones vetoriais, estilos base (2026-08-02)

Decisão de escopo da Fase 5 (usuário disse "siga para fase 5" sem especificar profundidade): **não** portar 1:1 os 17 `ResourceDictionaries/*.xaml` do WPF (~3200 linhas de `ControlTemplate`+`Trigger` — modelo incompatível com o sistema de estilos do Avalonia, que usa seletores `:pointerover`/`:pressed`). Em vez disso, rodadas incrementais de maior impacto visual por esforço, cada uma fechando com build/teste real (o padrão que funcionou a sessão inteira). Esta rodada:

1. **`App.axaml` — `RequestedThemeVariant` `Default` → `Light`**: o vmPing original é um app de tema claro fixo. Com `Default`, o Fluent seguia o tema do sistema — no desktop escuro do usuário, menus/dropdowns/diálogos ficavam escuros por cima das janelas claras do port (visível em vários screenshots da Fase 4: menu escuro sobre janela `#e1e1e1`). Uma linha, provavelmente a maior correção de fidelidade visual de toda a fase.
2. **`UI/Icons.axaml` (novo)**: subconjunto de `ResourceDictionaries/Icons.xaml` portado como `StreamGeometry` puro (pencil, window-restore, window-close, add, columns-grid, play, stop-circle, delete). `DrawingImage`/`GeometryDrawing` do WPF trocados pelo idioma Avalonia (`StreamGeometry` + `<Path>` no ponto de uso — mesmo padrão que o tema Fluent usa internamente); as variantes de cor duplicadas do original (-black/-white/-red com o mesmo path) viram um path só com `Fill` no ponto de uso. Path data copiado literalmente (sintaxe idêntica nos dois frameworks); ícones de `RectangleGeometry` convertidos pra path retangular à mão. [Provável] prefixo de fill rule `F1` removido — o parser do Avalonia deve aceitá-lo, mas não foi verificado contra fonte; ícones sólidos renderizam igual nas duas rules, e se algum aparecer com "buraco" errado é o primeiro lugar a olhar.
3. **`UI/ControlStyles.axaml` (novo)**: estilos globais sobrescrevendo só o que o Fluent faz diferente do visual original — cantos quadrados (`CornerRadius=0`) e paleta do `ButtonStandardStyle` (`#e1e1e1`/borda `#abadb3`/hover `#c9def5`/pressed `#fafafa`/disabled `#d0d0d0`) pra `Button`, fundo branco/borda fina pra `TextBox`/`ComboBox`. Hover/pressed via o padrão canônico `/template/ ContentPresenter#PART_ContentPresenter` (setar só `Button:pointerover Background` não tem efeito no Fluent). Classe `probe-action` pros botõezinhos do grid (fundo transparente, ícone 11x11).
4. **`UI/MainWindow.axaml`**: glifos Unicode ✎ ⛶ ✕ dos botões de cada probe trocados por `<Path>` com os ícones vetoriais, `Fill` bindado ao mesmo conversor de cor do alias (ícone acompanha a cor de status, legível sobre qualquer fundo).

Não replicado de propósito: `FocusVisualStyle` tracejado, estilos dark dedicados (janelas escuras usam os mesmos botões claros), ScrollBar/Slider/DataGrid/MenuItem customizados (alto volume, baixo retorno — entram depois se fizer falta). Rodadas seguintes planejadas: ícones nos menus, drag-and-drop (reordenar probes, arquivo de hosts), animações (pulso do FloodHost, fade do popup).

Verificado: os 4 `.axaml` tocados são XML bem-formado (ElementTree). Riscos pro build: `StreamGeometry` como recurso XAML com path no conteúdo do elemento ([Certo] — idioma do próprio Fluent), `ResourceInclude`/`StyleInclude` com URI `avares://vmping/...` ([Certo] nome do assembly, mesma regra do fix do tray icon), `Path` sem prefixo xmlns ([Certo] Shapes está na xmlns padrão).

Rode `dotnet build` e depois `dotnet run`: esperado — app inteiro claro mesmo com desktop escuro (menus incluídos), botões quadrados cinza-claro com hover azulado, e os três botõezinhos de cada probe com ícones vetoriais (lápis/janela/x) na cor do texto do alias.

**Confirmado por screenshot** (2026-08-02): tema claro aplicado (barra de menu clara), botões quadrados (Stop/Ping), ícones vetoriais nos três botões de cada probe renderizando com a cor de status correta (amarelo sobre probe verde/Up, preto sobre probe inativo). `StreamGeometry` sem o prefixo `F1` renderizou certo — risco fechado. Rodada 1 da Fase 5 concluída.

## Fase 5 — rodada 2: drag-and-drop pra reordenar probes (2026-08-02)

Portado de `ProbeTitle_PreviewMouseMove`/`Probe_Drop` do original (o item adiado desde a Fase 3 por causa da API assíncrona). Mapeamento WPF → Avalonia:

- **Iniciar o arrasto**: `PreviewMouseMove` + `e.LeftButton == Pressed` + `DragDrop.DoDragDrop(DependencyObject, data, effects)` (síncrono) → `PointerMoved` + `e.GetCurrentPoint(control).Properties.IsLeftButtonPressed` + `await DragDrop.DoDragDrop(e, data, effects)` (assíncrono, recebe o `PointerEventArgs` do gesto). Alça de arrasto: o `TextBlock` do alias (equivalente ao `Label` do original), com `Background="Transparent"` obrigatório — sem isso o hit-test do TextBlock só cobre os glifos do texto, não a área toda.
- **Aceitar o drop**: `AllowDrop="True"` + `Drop="Probe_Drop"` por elemento no XAML (WPF) → `DragDrop.AllowDrop="True"` (attached property, funciona no XAML) no `Border` raiz do item + **um único `AddHandler(DragDrop.DropEvent/DragOverEvent, ...)` no `ItemsControl`** — Avalonia não liga evento attached por atributo XAML, mas os eventos são roteados (bubbling), então um handler no ancestral cobre todos os itens. `DragOver` seta `DragEffects = Move` só quando o payload tem o formato próprio (`"vmping/probe"`), recusando arrastos vindos de fora.
- **Resolver o alvo**: `sender as Label/DockPanel` (WPF, um handler por elemento) → `(e.Source as Control)?.DataContext as Probe` — `e.Source` é o elemento mais fundo sob o cursor e todo elemento do template herda o `DataContext` do item.
- **Reordenar**: idêntico ao original — `RemoveAt(oldIndex)` + `Insert(newIndex, source)` na `ObservableCollection`.

Verificado: XML bem-formado, chaves/parênteses balanceados (134/134, 359/359), sem colisão `Name=` × membros novos do code-behind (regra do CS0102 desta sessão).

Rode `dotnet build` + `dotnet run` e teste: arrastar um probe pela barra de título (área do alias, acima do histórico) e soltar sobre outro probe deve trocar a posição dos dois no grid. Atenção esperada: o arrasto começa imediatamente ao mover com o botão pressionado (sem threshold, igual ao original) — clicar e mexer 1px já inicia.

**Confirmado pelo usuário** (2026-08-02): "drag and drop funcionando". Rodada 2 concluída — fecha o débito de drag-and-drop aberto desde a Fase 3.

## Fase 5 — rodada 3: animações (2026-08-02)

As duas animações adiadas desde a Fase 4, portadas de Storyboard/DoubleAnimation (WPF) pra `Style.Animations` com keyframes (nativo do Avalonia):

- **`FloodHostWindow` — pulso enquanto o flood roda**: o original animava a cor de fundo da janela (`ColorAnimation`). Decisão: animar `Opacity` (double) de um `Border` overlay vermelho (`PulseOverlay`, `IsHitTestVisible=False`, `ZIndex=1`, visível só com `IsActive`) em vez de interpolar cor de Brush — animação de double é o caminho mais garantido do Avalonia; interpolação de cor existe mas não foi verificada contra fonte. Ciclo 0 → 0.14 → 0 em 1.2s, infinito. Bônus na mesma janela: "Flood in progress..." piscando (opacidade 1 → 0.25 → 1), que o original também tinha e a Fase 4 tinha deixado estático. Estrutura do XAML mudou de `Grid` raiz pra `Panel` [raiz] > overlay + Grid.
- **`PopupNotificationWindow` — fade-in de entrada**: `Opacity="0"` no XAML + `DoubleTransition` de 250ms + `Opened += (_,_) => Opacity = 1` no code-behind. Failsafe deliberado: se a transição não rodar (compositor/ambiente), o `Opened` ainda seta 1 e o popup aparece — nunca fica invisível. A animação de escala do original não foi portada (só o fade).
- **Não portado (consciente)**: flash de atualização do `IsolatedPingWindow` — valor baixo, risco de piscar constantemente com ping a cada 2s.

[Provável] `IterationCount="INFINITE"` e a sintaxe `Style.Animations`/`KeyFrame Cue` são o idioma documentado do Avalonia, mas não foram verificadas contra o código-fonte da 11.3.17 nesta sessão — se o build der erro de XAML, é o primeiro lugar a olhar.

Verificado: XML bem-formado nos 2 `.axaml`, chaves balanceadas no `.cs`, sem colisão `Name=` nova.

Rode `dotnet build` + `dotnet run` e teste: (1) Flood Host contra um IP válido — fundo da janela deve pulsar em tom avermelhado e o texto "Flood in progress..." piscar enquanto ativo, parando ao clicar Stop; (2) provocar um popup de notificação (host down com popup habilitado) — deve surgir com fade suave em vez de aparecer seco.

**Parcialmente confirmado por screenshot** (2026-08-02): build verde (105 avisos), flood rodando com o texto "Flood in progress..." capturado esmaecido no meio do ciclo de piscar — animação de keyframes rodando de verdade (`IterationCount="INFINITE"`/`Style.Animations` funcionam na 11.3.17, risco fechado). Pulso do fundo e fade do popup não confirmáveis por frame estático — pendente confirmação visual do usuário.

Nota registrada do log de build: 4 avisos `CS0618` novos — `DataObject`/`DragDrop.DoDragDrop`/`DragEventArgs.Data` estão marcados obsoletos na 11.3.17 ("Use DataTransfer instead"). Decisão: manter a API deprecada — está confirmada funcionando em runtime pelo usuário, e trocar por `DataTransfer` (API não verificada contra fonte nesta sessão) só pra eliminar aviso seria troca de risco ruim. Anotado como melhoria futura se o port um dia subir pra Avalonia 12.x (onde a API antiga pode sumir de vez).

## i18n — rodada 1: infraestrutura de idioma (en + pt-BR) e MainWindow (2026-08-02)

Pedido do usuário antes do empacotamento: app em dois idiomas. Mecanismo: o padrão nativo do .NET (resx + satellite assembly), que o projeto já usava pela metade — `Properties/Strings.resx` veio do original com ~99 chaves (menus, tooltips, mensagens de probe, e-mail, erros), mas o port tinha **hardcodado em inglês nos `.axaml`/`.cs` em vez de usar as chaves existentes**. Inventário real: 223 strings únicas hardcoded em 17 janelas (114 só na OptionsWindow).

Entregue nesta rodada:

- **`Classes/Localization.cs` (novo)**: `ApplyConfiguredCulture()` — chamada no início do `Program.Main`, ANTES do bootstrap do Avalonia (necessário porque as janelas resolvem `Strings.*` via `x:Static` na construção). Lê só o nó `Language` do vmPing.xml com leitura enxuta independente de `Configuration.Load()` (que usa `Util.ShowError` no caminho de erro — depende de UI de pé, não pode rodar pré-bootstrap). Valores: `auto` (locale do sistema, default), `en-US`, `pt-BR`. Seta `CultureInfo.DefaultThreadCurrentUICulture` (cobre as threads de probe) + `Strings.Culture`.
- **`Properties/Strings.pt-BR.resx` (novo)**: tradução completa das 106 chaves (99 originais + 7 novas). Verificado por script: conjunto de chaves idêntico ao neutro nos dois sentidos. Chave ausente cairia no inglês (fallback do ResourceManager) — sem crash. O csproj SDK-style gera o satellite `pt-BR/vmping.resources.dll` sozinho. [Provável] **Risco anotado pra Fase 6**: publish single-file — conferir se o satellite entra no bundle ou precisa ir como subpasta no `.deb`.
- **Chaves novas** (resx + `Strings.Designer.cs` editado à mão no padrão gerado): `MainWindow_Watermark`, `Menu_InputAddresses`, `Menu_NewInstance`, `Tray_Exit`, `Options_Language`, `Options_LanguageAuto`, `Options_LanguageRestart`.
- **`MainWindow.axaml`**: todos os headers de menu, tooltips, watermark e o Ping/Stop (via `TrueValue`/`FalseValue` do conversor com `x:Static`) religados ao resx. Idem tray (`MainWindow.axaml.cs`).
- **`Probe-Icmp.cs`/`Probe-Util.cs`**: mensagens de probe ("Reply from", "Request timed out.", "Pinging", "Sent/Received/Lost") religadas às chaves que o original já tinha.
- **`OptionsWindow` (aba Display)**: seletor de idioma (Auto/English/Português (Brasil) — nomes de idioma sempre no idioma nativo, convenção de seletor). Aplica a cultura na hora pro que é montado em runtime; textos `x:Static` só mudam ao reiniciar (nota de reinício na própria aba). Persiste no vmPing.xml (nó `Language`) pela mesma regra das outras opções: só com "Save as vmPing defaults" marcado.

O que AINDA está em inglês (rodadas 2-4, já combinadas): OptionsWindow inteira (114 strings), as 14 janelas restantes, mensagens de diálogo hardcoded no code-behind, e a prosa longa de Help/Usage.

Verificado por script: XML ok nos 4 XAML/resx tocados, chaves/parênteses balanceados nos 9 `.cs`, sem colisão `Name=`, resx pt-BR ≡ neutro.

Rode `dotnet build` + `dotnet run`. Teste: (1) com o sistema em pt-BR, o app deve abrir já em português (menus, tooltips, "Pingando"/"Resposta de"/"Enviados:") sem mexer em nada; (2) Options → Display → Language → English + OK (com Save as defaults marcado) → reiniciar → app em inglês; (3) voltar pra Auto ou Português e conferir a volta.

### Bug real: idioma não mudava — InvariantGlobalization (2026-08-02)

Usuário reportou: seletor aparece, mas nada muda nem depois de salvar — e (visível no screenshot) o app estava em inglês mesmo com o sistema em pt-BR, ou seja, até o modo Auto estava quebrado.

[Certo] Causa raiz: `<InvariantGlobalization>true</InvariantGlobalization>` no `.csproj`, colocado por mim no scaffold da Fase 1 (prática comum pra reduzir tamanho/dependências em publish self-contained — na época o app era single-language e a flag era inofensiva). Em modo invariante, o .NET não carrega ICU, toda cultura colapsa na invariante, e o `ResourceManager` nunca resolve satellite assembly nenhum — o mecanismo inteiro de i18n construído nesta rodada ficava morto por baixo, com o código todo certo. Flag removida.

Consequências e pendências registradas:
- O runtime agora exige ICU (`libicu*`) no sistema. Qualquer desktop Linux tem; **Fase 6**: adicionar a dependência no `Depends:` do `packaging/debian/control` com o nome exato do pacote da distro alvo (conferir com `dpkg -l | grep libicu` — Debian 12 usa `libicu72`, Debian 13 outro número).
- Timestamps/formatação de números passam a seguir o locale real do sistema em vez do invariante (efeito colateral desejável — é o comportamento normal de qualquer app).
- Detalhe de UX a observar no reteste: a persistência do idioma segue a regra das outras opções — **só grava no vmPing.xml com "Save as vmPing defaults" marcado** (no screenshot do usuário o checkbox estava desmarcado). Se isso tropeçar o usuário de novo, vale quebrar a convenção e persistir o idioma sempre.

**Confirmado pelo usuário** (2026-08-02): "agora deu certo" — depois de remover InvariantGlobalization, idioma funciona (screenshot mostra "_Parar todos (F5)" em português).

## i18n — rodada 2: HelpWindow traduzida + remoção dos mnemônicos "_" (2026-08-02)

Dois pedidos do usuário no mesmo turno:

1. **"remova o underline antes do texto"**: os valores `_Add Host`/`_Columns`/`_Start All (F5)`/`_Stop All (F5)` herdaram do resx original o marcador de mnemônico do WPF (`_` = tecla de atalho com Alt, renderizado como sublinhado lá). No Avalonia, o `TextBlock` usado no header do Start/Stop renderiza o `_` literal — visível no screenshot do usuário ("_Parar todos (F5)"). Removido o `_` inicial dos 4 valores nos dois resx (perde-se o mnemônico Alt+letra, que de todo modo não estava funcionando de forma consistente no port — os atalhos reais são os InputGesture F5/Ctrl+A/etc., intactos).
2. **Help em inglês**: todo o conteúdo da `HelpWindow` extraído pra 34 chaves novas `Help_*` (resx neutro + pt-BR + `Strings.Designer.cs`, injetados por script pra evitar erro de digitação em 84 blocos manuais; verificado por script: conjuntos de chaves idênticos nos dois resx — 136 cada —, toda chave usada no XAML tem propriedade no Designer). Perdas deliberadas registradas no cabeçalho do `.axaml`: negrito inline no meio de parágrafo (Runs compostos) virou texto corrido com aspas — 1 parágrafo = 1 chave de tradução, em vez de 4-5 fragmentos que quebram em idiomas com ordem de frase diferente; dois typos do texto original corrigidos ("continuosly", "occurrs"); atalho de Add Host corrigido de Alt-A (original) pra Ctrl-A (o que o port realmente usa).

Ainda em inglês (rodadas seguintes): OptionsWindow (114 strings), UsageWindow (CLI help, 19), demais janelas menores e mensagens de diálogo do code-behind.

Rode `dotnet build` + `dotnet run`: menu sem `_` inicial, e Help (F1) inteira em português com o sistema em pt-BR.

**Confirmado pelo usuário** (2026-08-02): "agora esta certo" — underscores fora e Help traduzida.

## Ícone do app no dock (2026-08-02)

Usuário reportou (screenshot do dock): vmPing rodando com o ícone genérico de engrenagem. Duas causas empilhadas:

1. **[Certo] `Icon="/Assets/vmPing.ico"` na MainWindow**: no Linux o Avalonia decodifica imagens via Skia, que não lê `.ico` — a janela ficava sem ícone nenhum (`_NET_WM_ICON` vazio), e o GNOME cai no genérico. Trocado por `/Assets/vmPing-48.png`. Os PNGs de 16/32/48 foram extraídos do próprio `vmPing.ico` original (Pillow; o .ico só contém esses 3 tamanhos, não há 256px no original).
2. **`build-deb.sh` instalava `vmPing-16.png` (16 pixels!) como `hicolor/256x256/apps/vmping.png`** — mesmo com o `.desktop` certo (`Icon=vmping`, `StartupWMClass=vmping`), o dock mostraria um ícone de 16px esticado. Corrigido: instala os 3 tamanhos reais nos diretórios hicolor corretos (`16x16`/`32x32`/`48x48`), e o `postinst` agora roda `gtk-update-icon-cache` (silencioso, ignora falha) pro ícone aparecer sem relogin.

Expectativa honesta pro usuário: rodando via `dotnet run`, o fix nº 1 já deve fazer o ícone real aparecer no Alt-Tab e [Provável] no dock (o GNOME usa o ícone da janela quando não acha `.desktop` correspondente). O ícone "bonito" e permanente no dock/menu de aplicativos só vem com o `.deb` instalado (fix nº 2), que casa a janela com o `.desktop` via `StartupWMClass`.

**Follow-up** (2026-08-02): usuário confirmou que o dock continuou com a engrenagem via `dotnet run` — o tooltip do dock mostrando "vmping" confirma que o WM_CLASS está certo e o que falta é só o `.desktop` instalado (o dash do GNOME não usa `_NET_WM_ICON` pra ícone de dock nesse cenário). Comportamento esperado, não bug; fornecido ao usuário um `.desktop` de desenvolvimento em `~/.local/share/applications/vmping-dev.desktop` (aponta pro binário de Debug + PNG 48px do repo, `StartupWMClass=vmping`) como paliativo até o `.deb` da Fase 6. Usuário decidiu não usar o paliativo — validação do ícone fica pro teste de instalação real na Fase 6.

## Feature nova (fora do escopo do original): Nslookup e Dig no menu (2026-08-02)

Pedido do usuário: opções de nslookup e dig no menu principal, logo abaixo do Traceroute. Não existe no vmPing original — é a primeira feature própria do port, não um port de comportamento.

Implementação:
- **`UI/DnsLookupWindow.axaml`/.cs (novos)**: UMA janela parametrizada pela ferramenta (construtor recebe `"nslookup"` ou `"dig"`; título e header seguem a ferramenta) em vez de duas janelas quase idênticas. Mesmo padrão de shell-out da `TraceRouteWindow` — chama o utilitário do sistema e mostra a saída crua num TextBox monospace escuro (pra `dig`, a saída crua É o produto). Diferenças deliberadas vs TraceRouteWindow: sem streaming linha a linha (consulta DNS termina em segundos — lê stdout+stderr até o fim com `ReadToEndAsync`), e stdout/stderr lidos **em paralelo** (`Task.WhenAll`) pra não deadlockar com pipe cheio. Botão desabilitado durante a consulta; processo morto no fechamento da janela; exceção do `Process.Start` (utilitário ausente) mostrada na área de saída em vez de crashar — mesma lição do bug do Browse/portal.
- **`MainWindow`**: dois `MenuItem` novos abaixo do Traceroute (`NslookupMenu`/`DigMenu`), handlers de uma linha.
- **i18n desde o nascimento**: 4 chaves novas nos dois resx + Designer (`Menu_Nslookup`, `Menu_Dig`, `DnsLookup_Watermark`, `DnsLookup_Run` — "Lookup"/"Consultar"). Total agora: 140 chaves em cada resx, verificadas idênticas.
- **`packaging/debian/control`**: `Depends` ganhou `bind9-dnsutils | dnsutils` (pacote que fornece nslookup e dig no Debian/Ubuntu).

Verificado por script: XML ok, chaves balanceadas, sem colisão `Name=` × membros, resx neutro ≡ pt-BR, toda chave com propriedade no Designer.

Rode `dotnet build` + `dotnet run`: menu ⋯ deve mostrar Nslookup e Dig entre Traceroute e Flood de host; cada um abre uma janela própria; consultar `google.com` nos dois deve mostrar a saída idêntica à do terminal.

**Confirmado por screenshot** (2026-08-02): nslookup funcionando (consulta real contra `neolink.com.br`, saída idêntica ao terminal, em pt na UI). **Bug real reportado junto**: a caixa de saída escura ficava BRANCA ao passar o mouse por cima — o template do TextBox no Fluent troca o fundo do Border interno (`PART_BorderElement`) nos estados `:pointerover`/`:focus` pelos recursos claros do tema, atropelando o `Background` local. Corrigido em `ControlStyles.axaml` com dois estilos globais que prendem o fundo de hover/foco ao `Background` declarado do próprio TextBox (`{Binding $parent[TextBox].Background}`) — de quebra corrige o mesmo bug latente no histórico escuro do `IsolatedPingWindow`. [Provável] o nome do part (`PART_BorderElement`) não foi verificado contra fonte; se o hover continuar branco, plano B documentado no próprio arquivo: trocar a saída por `SelectableTextBlock`.

### Botões: conteúdo centralizado + estilo primário azul (2026-08-02)

Pedido do usuário (screenshot do botão "Consultar" com texto grudado na esquerda): centralizar o texto e "deixar mais bonitos". Causa do desalinhamento: o Fluent do Avalonia alinha o conteúdo do Button à ESQUERDA por padrão (diferente do WPF, que centraliza) — nunca tinha ficado evidente porque os botões anteriores tinham texto justo na largura. Ajustes em `ControlStyles.axaml`, todos globais:

- `HorizontalContentAlignment`/`VerticalContentAlignment = Center` (a correção em si).
- `Padding 14,6` e `CornerRadius 3` — desvio consciente do quadrado 0px do original, a pedido explícito do usuário ("mais bonitos"); registrado que isso diverge da fidelidade estrita ao vmPing original.
- **Estilo primário**: `Button[IsDefault=True]` (Ping/Trace/Consultar/OK — todo botão de ação principal) ganhou o azul do `ButtonStyle` original (#268bd2, hover #46abf2, pressionado #066bb2, texto branco), via seletor por propriedade. Recupera a hierarquia visual do original (botão principal azul vs. secundários cinza), que o port tinha achatado.

Rode `dotnet build` + `dotnet run` e confira os botões nas várias janelas (Consultar, Ping, Trace, OK/Cancel das Options).

### DnsLookupWindow: tipo de registro, servidor e flags do dig (2026-08-02)

Pedido do usuário (com screenshot do dig funcionando contra `neolink.com.br`): tipo de registro (MX etc.), flags `+short`/`+answer` selecionáveis abaixo do campo, e servidor específico (`@8.8.8.8`).

Implementado em `DnsLookupWindow` — linha de opções abaixo do campo de consulta: ComboBox de tipo ((padrão)/A/AAAA/MX/NS/TXT/CNAME/SOA/PTR/SRV/ANY), campo de servidor (aceita com ou sem `@` — o code-behind normaliza) e os dois checkboxes de flag. Decisões de correção técnica (não literais ao pedido):

- **`+short`/`+answer` só existem no dig** — no nslookup os checkboxes ficam ocultos (não desabilitados: ocultos, pra não sugerir que a ferramenta tem a opção). Tipo e servidor funcionam nas duas: dig `[@srv] host TIPO [flags]`, nslookup `-type=TIPO host [srv]`.
- **`+answer` mapeia pra `+noall +answer`**: `+answer` sozinho no dig não muda nada visível (a seção de resposta já aparece por padrão); o uso consagrado "só a resposta" é o par `+noall +answer`. Documentado no código.

3 chaves i18n novas (`DnsLookup_Type`/`_Server`/`_TypeDefault`) — total 143 por resx, verificados idênticos; chaves/colisões/XML verificados por script.

Rode `dotnet build` + `dotnet run` e teste no dig: `neolink.com.br` + tipo MX; `+short` marcado (só os IPs/hosts, sem cabeçalho); servidor `8.8.8.8` (o cabeçalho da resposta deve mostrar `SERVER: 8.8.8.8#53`). No nslookup: tipo MX deve funcionar e os checkboxes de flag nem aparecer.

**Confirmado pelo usuário** (2026-08-02): "tudo certo com dig e o nslookup". Pedido de ajuste na sequência: **remover o seletor de tipo do nslookup** (deixar só a consulta padrão) — feito: no nslookup o ComboBox e seu rótulo ficam ocultos junto com as flags; o campo de servidor continua disponível.

## i18n — rodada 3: tradução completa da UI restante (2026-08-02)

Pedido: "traduza também o texto em flood, options e em todo restante". Levantamento real: 158 strings hardcoded únicas em 16 `.axaml` (114 só na OptionsWindow) + 28 mensagens de diálogo no code-behind.

Execução por script (o volume torna edição manual um gerador de erro de digitação): mapa `texto_inglês → (chave, tradução)` revisado à mão, injeção nos dois resx + `Strings.Designer.cs`, e substituição dos atributos (`Text`/`Content`/`Header`/`Title`/`Watermark`/`ToolTip.Tip`) por `{x:Static p:Strings.Chave}`, com inserção automática do `xmlns:p` onde faltava. **126 chaves novas** + 27 de mensagens de diálogo + 3 de rótulo de botão. Total: **296 chaves por idioma**, verificadas idênticas.

Cobertura: OptionsWindow (8 abas completas, incluindo os textos longos de ajuda de cada opção), FloodHost, Usage (ajuda de CLI), StatusHistory, TraceRoute, DnsLookup, MultiInput, NewFavorite, NewAlias, EditAlias, NewConfiguration, ManageFavorites, ManageAliases, DialogWindow, PopupNotification — e as mensagens de erro/validação de `OptionsWindow`, `NewFavoriteWindow`, `StatusHistoryWindow`, `Favorite.cs`, `Probe-Util.cs`.

Não traduzidos de propósito: URL do projeto, placeholder de versão/copyright, e os exemplos literais de linha de comando (`vmping -i 5 -w 2 ...`, `target_host`, `-i interval`) — são sintaxe, não prosa.

**[Certo] Bug real INTRODUZIDO pela tradução e corrigido na mesma rodada** (o tipo de armadilha que justifica revisar i18n em massa, não só rodar o script): `SaveGeneralOptions` decidia o multiplicador do intervalo de ping comparando o **texto** do ComboBox (`"minutes"`/`"hours"`). Com os itens traduzidos, a comparação nunca bateria e **todo intervalo viraria segundos, silenciosamente** — um usuário configurando "5 minutos" teria ping a cada 5 segundos. Trocado por `SelectedIndex` (mesma fonte que `PopulateGeneralOptions` já usava pra selecionar). O helper `GetComboText`, que só existia pra isso, foi removido com um comentário explicando por que não deve voltar. Varredura por outras comparações do mesmo tipo: só sobraram as que usam títulos de favoritos (dado do usuário, não traduzível) — seguras.

Verificado por script: XML válido em todos os `.axaml`, 296 chaves idênticas nos dois resx, nenhuma chave usada sem propriedade no Designer, nenhuma propriedade duplicada, `xmlns:p` presente em todo arquivo que usa `p:`, chaves/parênteses balanceados em todos os `.cs`.

Rode `dotnet build` + `dotnet run`. Teste principal: abrir Options (todas as 8 abas devem estar em português), Flood de host, Ajuda, Histórico de status. **Teste de regressão importante**: Options → Geral → intervalo "1" + unidade "minutos" → OK → reabrir e conferir que continua "1 minuto" (e não virou 1 segundo) — é o bug que a tradução quase introduziu.

### Correção de layout: textos cortados em pt-BR (2026-08-02)

Usuário reportou por screenshot: "Tempo de vida (T…", "Dados de pacote pe…", "Intervalo de ping…", "segundo" (por "segundos") — vários rótulos truncados na OptionsWindow.

[Certo] Causa: `Width` FIXO em rótulos e botões, dimensionado a olho para o inglês. Português é tipicamente 15-30% mais longo ("Time to live:" → "Tempo de vida (TTL):"), então estoura e o Avalonia corta. Não é bug de tradução, é layout rígido — o erro clássico de internacionalizar UI feita num idioma só.

Correção estrutural (não caso a caso): `Width` → `MinWidth` em todos os `TextBlock`/`RadioButton`/`CheckBox` (28 ocorrências) e `Button` (30 ocorrências) de todas as janelas. `MinWidth` preserva o alinhamento em coluna que o `Width` fixo garantia, mas deixa o controle crescer quando o texto exige — funciona pros dois idiomas sem número mágico por idioma. Também: combo de unidades 100→110px e a janela de Options 600×540 → 680×560 (o conteúdo em pt precisa de mais respiro horizontal).

Verificado: XML válido em todos os `.axaml`; nenhum rótulo com `Width` fixo restante.

Rode `dotnet build` + `dotnet run` e confira as abas Geral e Avançado das Options — nenhum rótulo deve aparecer cortado.

## Fase 6 — empacotamento .deb: revisão do script antes da primeira execução real (2026-08-02)

O `build-deb.sh` foi escrito na Fase 1 e nunca rodou. Antes de pedir a execução, revisei-o contra tudo que mudou desde então — dois problemas reais que teriam gerado um pacote quebrado ou silenciosamente incompleto:

1. **[Certo] O idioma pt-BR sumiria do pacote.** Com `PublishSingleFile=true`, os satellite assemblies de tradução **não** são embutidos no executável: o publish os deixa como `pt-BR/vmping.resources.dll` numa subpasta ao lado do binário. O script só instalava o executável — o `.deb` sairia só em inglês, e o pior: silenciosamente, porque o `ResourceManager` cai no idioma neutro sem erro nenhum quando não acha o satellite. Era o risco que eu tinha anotado ao criar o `Strings.pt-BR.resx` e que se concretizaria aqui. Corrigido: o script agora varre `publish/*/` procurando `vmping.resources.dll`, instala cada cultura encontrada em `/usr/lib/vmping/<cultura>/`, imprime quais idiomas entraram, e **avisa em stderr se não achar nenhum** (falha barulhenta em vez de silenciosa).
2. **[Certo] Falta da dependência de ICU.** Ao remover `InvariantGlobalization` (necessário pra i18n funcionar), o runtime passou a exigir ICU, mas o `Depends:` não tinha. Num sistema sem ICU o app nem inicia. Complicador: o nome do pacote muda a cada release da distro (`libicu67`/`70`/`71`/`72`/`74`/`76`). Solução: lista de alternativas no `Depends` cobrindo Debian 11-13 e Ubuntu 20.04-24.04, e o script agora imprime qual ICU está instalado no sistema de build pra conferência.

Outros ajustes na mesma passada:
- `ldd` movido pra **antes** do `dpkg-deb` — no script original ele rodava depois, apontando pra um diretório que o `trap` de saída já tinha apagado (nunca teria funcionado); agora filtra por "not found" e avisa se faltar biblioteca nativa.
- Sugestão de instalação trocada de `dpkg -i` pra `apt install ./arquivo.deb`, que resolve as dependências em vez de falhar com "dependency problems".
- `postrm`: atualiza o cache de ícones na remoção (espelhando o postinst) e documenta explicitamente que `~/.config/vmPing` **não** é apagado — é dado do usuário, e o postrm roda como root.
- `vmping.desktop`: `GenericName`/`Comment`/`Keywords` com traduções `[pt_BR]` (o `.desktop` tem localização própria, independente do resx do app), `StartupNotify` e `Version=1.0` do padrão freedesktop.

Sintaxe dos três scripts shell verificada (`bash -n`). O que só a execução real pode dizer: se o publish self-contained conclui, o tamanho final do `.deb`, se o `lintian` reclama de algo, e se o ícone aparece no menu de aplicativos depois de instalado (o teste que ficou pendente da Fase 5).

### Primeira execução real do build-deb.sh (2026-08-02)

Publish self-contained concluiu (0 erros, os 106 avisos de sempre) e `ldd` não achou biblioteca faltando. Mas o script **abortou antes de gerar o `.deb`** — "==> Construindo pacote .deb..." nunca apareceu.

**[Certo] Bug meu, clássico de `set -e`**: a linha `[ -n "$icu_pkg" ] && echo ...` que eu tinha acabado de adicionar retorna código 1 quando a variável está vazia; com `set -euo pipefail`, isso encerra o script inteiro. Ironia registrada: o comando que abortou o build era um *diagnóstico auxiliar* que eu adicionei pra ajudar, não parte do empacotamento. Corrigido com `|| true` explícito e `if/else` no lugar do `&&`, mais um comentário no código explicando por que o `|| true` não é decorativo. Mesmo tratamento aplicado ao bloco do `ldd` (que já tinha `||` no fim e por isso sobreviveu) — reescrito pra capturar a saída numa variável em vez de depender de encadeamento de códigos de retorno.

**Aviso do satellite disparou** — nenhuma pasta `pt-BR/` no publish. [Provável] não é falha: com `PublishSingleFile`, o .NET 6+ embute os satellites no próprio executável em vez de deixá-los em subpastas. Como não dá pra afirmar sem ver, o aviso agora **lista o conteúdo real do publish** para diagnóstico e indica o teste decisivo: instalar e abrir o app com `LANG=pt_BR.UTF-8`. Se abrir em português, os satellites estão embutidos e o loop de cópia é inofensivo; se abrir em inglês, aí sim é problema real e o caminho é `-p:PublishSingleFile=false` ou incluir a pasta de cultura explicitamente.

### Bug crítico: pacote saía sem as bibliotecas nativas (2026-08-02)

Segunda execução gerou o `.deb` (76 MB de binário), mas a listagem de diagnóstico do publish — adicionada por causa do aviso do satellite — expôs um problema bem pior, que teria passado despercebido:

```
libHarfBuzzSharp.so   2.471.408
libSkiaSharp.so       9.244.960
vmping               76.860.687
vmping.pdb              106.192
```

[Certo] **`PublishSingleFile` NÃO embute bibliotecas nativas** (`IncludeNativeLibrariesForSelfExtract=false` é o padrão): elas ficam como arquivos separados ao lado do executável. O script copiava **só** o binário — o `.deb` instalava um app sem `libSkiaSharp.so` (o renderizador do Avalonia) nem `libHarfBuzzSharp.so` (layout de texto). Provável resultado: não abre. "Single file" significa "todo o código *gerenciado* num arquivo", não "tudo num arquivo".

Agravante metodológico registrado: **o `ldd` passou limpo mesmo com o pacote quebrado**, porque o .NET carrega essas `.so` via `dlopen` em runtime, não por link dinâmico — a verificação que eu tinha colocado pra pegar exatamente esse tipo de erro era estruturalmente incapaz de pegá-lo. Se o aviso do satellite (um falso alarme, provavelmente) não tivesse me feito imprimir a listagem do publish, o pacote sairia quebrado com todos os checks "verdes".

Corrigido: o script agora copia **todos** os arquivos do publish (exceto `.pdb`, que não tem por que ir num pacote de distribuição), listando cada um, e compara a contagem de artefatos do publish com a do pacote — falha barulhenta se algo ficar de fora. Lição: verificação que só olha o que você lembrou de verificar não vale muito; comparar "o que foi produzido" contra "o que foi empacotado" é a checagem que pega o desconhecido.

### Pacote completo e primeira instalação real (2026-08-02)

Terceira execução: as duas `.so` incluídas, contagem publish/pacote batendo (3 e 3), `.deb` de 28,4 MB gerado. `sudo apt install ./vmping_1.0.0_amd64.deb` instalou e `vmping` abriu.

**[Certo] Erro real na instalação, culpa minha**: no meio dos gatilhos apareceu
```
gtk-update-icon-cache: The generated cache was invalid.
WARNING: icon cache generation failed
```
Causa: eu tinha adicionado `gtk-update-icon-cache` no `postinst`/`postrm` achando que ajudaria o ícone a aparecer sem relogin. É justamente o que **não** se deve fazer num pacote Debian: o `hicolor-icon-theme` já registra um *trigger* do dpkg que atualiza o cache no momento certo (visível na própria saída, em "Processando gatilhos para hicolor-icon-theme"). A chamada manual roda fora de ordem e corrompe o cache que o trigger depois tenta usar. Removida dos dois scripts, com comentário explicando por que não deve voltar. Mesma regra registrada pro `update-desktop-database` (trigger do `desktop-file-utils`, também visível na saída).

A "Nota" final do apt sobre o usuário `_apt` é benigna: o apt avisa que baixou o `.deb` sem sandbox porque o arquivo estava no `$HOME` do usuário. Some se o pacote for instalado de outro caminho; não é problema do pacote.

**Correção do diagnóstico do cache de ícones**: depois de remover a chamada manual do `postinst`/`postrm`, o erro `gtk-update-icon-cache: The generated cache was invalid` **continuou**. Ou seja: minha primeira hipótese (chamada duplicada corrompendo o cache) estava errada — a remoção era correta por outros motivos (duplicar trigger é má prática), mas não era a causa.

[Certo] Causa real isolada por teste controlado: rodando `sudo gtk-update-icon-cache -f /usr/share/icons/hicolor` **com o vmping desinstalado** (`dpkg -r vmping`), o erro se repete exatamente igual. Logo, é uma condição pré-existente do sistema do usuário — algum outro ícone em `/usr/share/icons/hicolor` está inválido e derruba a geração do cache inteiro. Verificado em paralelo que os três PNGs do vmPing estão íntegros (assinatura PNG correta, chunks `IHDR`/`IDAT`/`IEND`, RGBA em 16/32/48). **Não é problema do pacote nem do port**; o vmPing só serviu de gatilho pro usuário reparar num aviso que já existia. Registrado aqui pra não ser reinvestigado como bug do projeto.

### Fase 6 CONCLUÍDA — pacote instalado e validado (2026-08-02)

Instalação real do `.deb` confirmada pelo usuário nos três pontos que faltavam:

1. **Interface em português** — resolve o falso alarme do satellite: com `PublishSingleFile`, o .NET **embute** os satellite assemblies no executável (por isso não existe pasta `pt-BR/` no publish) e o app instalado abre corretamente em pt-BR. O aviso do script foi removido (ausência da pasta é o comportamento normal); o loop de cópia ficou como rede de segurança caso o modo de publish mude no futuro, com comentário explicando por quê.
2. **Ping ICMP sem sudo** — primeira validação real do `setcap cap_net_raw+ep` aplicado pelo `postinst`. Até aqui o `setcap` era aplicado à mão a cada build de Debug; agora está provado que a instalação resolve isso sozinha, que era exatamente o propósito do postinst desde a Fase 1.
3. **Ícone no menu de aplicativos** — fecha a pendência aberta na Fase 5 (o ícone genérico no dock rodando via `dotnet run`). Confirmado que era só a ausência do `.desktop` instalado: com o pacote, o `StartupWMClass=vmping` casa a janela com a entrada e o ícone correto aparece. Note que apareceu **apesar** do cache de ícones quebrado do sistema — o que reforça que aquele erro é ortogonal.

**Estado do projeto**: as 6 fases concluídas. O port está funcional, empacotado, instalável e validado em execução real — pronto para publicação em https://github.com/rickdeckard82/vmPing-linux.

Pendências conhecidas, todas de baixa prioridade e documentadas em seus lugares: tray icon invisível no GNOME sem extensão AppIndicator (limitação da plataforma, no README); 4 avisos `CS0618` de API de drag-and-drop deprecada na Avalonia 11.3.17 (funciona, migrar só se subir pra 12.x); ~100 avisos de nulidade cosméticos herdados do código original; e os itens de estilo deliberadamente não portados (ScrollBar/Slider/DataGrid customizados, ícones nos menus).

**Confirmado pelo usuário** (2026-08-02): "ficou bom e funcional" — seletor próprio validado em runtime nos três botões Browse. Com isso o bug do `xdg-desktop-portal` deixa de ser limitação do app: virou um caminho degradado que o usuário nem percebe. README atualizado (a seção de limitações agora registra o seletor como *resolvido*, e ganhou a limitação real dos sons mudos do tema).

**O port está pronto para publicação.** Checklist final antes do Release: `sha256sum vmping_1.0.0_amd64.deb > SHA256SUMS`, e copiar as limitações conhecidas do README para a descrição do Release.

### Conformidade Debian: correções apontadas pelo lintian (2026-08-02)

`lintian` rodado no pacote final. Separando o que é problema real do que é política inaplicável a um app self-contained:

**Corrigido (erros/avisos reais, todos culpa do meu empacotamento):**

- `E: multiline-field Depends` — eu tinha quebrado o `Depends:` em duas linhas por legibilidade. Formato de campo do `control` não aceita continuação assim para listas de dependência; algumas ferramentas fazem parsing errado. Voltou pra uma linha só.
- `W: non-standard-dir-perm 0775 != 0755` (vários) — `install`/`mkdir -p` herdam o umask do usuário (0002 nesta máquina), gerando **diretórios group-writable dentro de `/usr`**. `--root-owner-group` corrige dono mas não permissão. Adicionada normalização explícita: diretórios 755, arquivos 644, executáveis 755 reaplicados depois. Esse era o achado com maior peso de segurança da lista.
- `W: absolute-symlink-in-top-level-folder` — `/usr/bin/vmping` apontava com caminho absoluto para `/usr/lib/vmping/vmping`. Dentro do mesmo top-level o symlink deve ser relativo (`../lib/vmping/vmping`), que sobrevive a chroot e a prefixo diferente.
- `E: no-changelog` — pacote nativo exige `changelog.gz`. Gerado pelo script a partir da versão passada na linha de comando, comprimido com `gzip -9n` (o `-n` omite timestamp, para build reproduzível).
- `W: no-manual-page` — escrita uma `vmping.1` em roff (sinopse, opções de CLI reais, arquivo de configuração, nota sobre `cap_net_raw`, atribuição ao autor original e `SEE ALSO` para ping/traceroute/dig).
- `W: maintainer-script-empty [postrm]` — o `postrm` tinha ficado só com comentários e `exit 0` depois de eu remover a chamada indevida do `gtk-update-icon-cache`. Script de mantenedor vazio é ruído: deixou de ser instalado no pacote. O arquivo permanece no repositório documentando a decisão de **não** apagar `~/.config/vmPing`.

**Não corrigido, com `lintian-overrides` documentando por quê:**

- `E: embedded-library` (expat, freetype, libjpeg, libpng, libwebp, zlib dentro de `libSkiaSharp.so`) e `unstripped-binary-or-object` — inerentes ao publish self-contained. "Corrigir" significaria abandonar o self-contained e exigir o runtime .NET instalado no sistema alvo, que é exatamente o que o pacote existe para evitar. Registrado como override explícito em `/usr/share/lintian/overrides/vmping`, com justificativa — que é a forma correta de dizer "eu sei, e é intencional" em vez de deixar o erro solto.

[Certo] Vale a distinção registrada: `lintian` valida contra a Debian Policy, escrita para pacotes dos repositórios oficiais. Para distribuição via GitHub Releases boa parte é inaplicável — mas as seis correções acima seriam problemas reais em qualquer contexto (permissão de diretório, symlink frágil, campo malformado, ausência de documentação padrão).

**Resultado: `lintian` passa 100% limpo** — nenhum erro, aviso ou informativo. Melhor que a minha previsão (eu esperava que os `embedded-library` continuassem visíveis; o `lintian-overrides` embarcado no próprio pacote foi respeitado). O `.deb` está em conformidade com a Debian Policy no que se aplica a um app self-contained.

### Som de "host fora do ar" mudo — arquivo quebrado no sound theme (2026-08-02)

Usuário reportou: alerta de "host no ar" toca, o de "host fora do ar" não emite som nenhum — nem pelo botão Testar, nem derrubando a conexão de verdade.

[Certo] **Não é bug do vmPing.** Diagnóstico por eliminação, testando fora do app: `ffplay -nodisp -autoexit -loglevel quiet /usr/share/sounds/freedesktop/stereo/dialog-warning.oga` fica **mudo no terminal**, enquanto `complete.oga` toca normalmente. O arquivo existe (12 KB), o player retorna sucesso, e nada é ouvido — `dialog-error.oga` tem o mesmo comportamento. São arquivos quebrados/mudos do próprio `sound-theme-freedesktop` nesta instalação. Nenhum código consegue detectar isso: do ponto de vista do app, a reprodução foi bem-sucedida.

Achado colateral da investigação: **`paplay` nem existe neste sistema** — o app vinha usando o `ffplay` como fallback por acaso. O `.deb` não declarava nenhum player de áudio como dependência; num sistema sem `paplay`, `ffplay` e `aplay`, os alertas sonoros falhariam sem explicação. Adicionado `Recommends: pulseaudio-utils | ffmpeg | alsa-utils` (Recommends, não Depends: o app é plenamente utilizável sem som).

Correções aplicadas:

- **Som padrão de "down" trocado** de `dialog-warning.oga` para `suspend-error.oga` (confirmado audível pelo usuário; som descendente, semanticamente adequado a "caiu"). O de "up" continua `complete.oga`.
- **Caminho fixo virou lista de candidatos**: `Constants.ResolveDefaultAudio(...)` devolve o primeiro arquivo que existe de fato no disco (`suspend-error` → `bell` → `dialog-warning` → `message` para down; `complete` → `message` → `bell` → `dialog-information` para up). Trocar um caminho fixo por outro caminho fixo só mudaria de qual distro o padrão quebra. As constantes passaram de `const` para `static readonly` — verificado que nenhum uso exigia constante de compilação.
- **Bug latente corrigido junto**: `ApplicationOptions.AudioUpFilePath`/`AudioDownFilePath` não tinham valor inicial — ficavam `null` até o usuário marcar o checkbox nas Opções (que preenche o campo). Funcionava por acaso pelo caminho da UI, mas qualquer outro caminho (config antiga sem o nó, alerta disparado antes de abrir as Opções) pegaria `null`. Agora nascem com o padrão resolvido.

**Atenção na atualização**: o `vmPing.xml` já existente do usuário guarda o caminho antigo (`dialog-warning.oga`); o novo padrão só vale para configs novas ou campos vazios. Para adotar o som novo, é preciso trocar o caminho na aba Sons (ou apagar o `~/.config/vmPing/vmPing.xml`).

### Seletor de arquivos próprio — contornando o bug do portal (2026-08-02)

Usuário: "ainda não consigo procurar". Correto — até aqui o botão Browse só tinha deixado de derrubar o app; o seletor em si continuava inutilizável nesse ambiente por causa do bug do `xdg-desktop-portal`.

[Certo] Decisão: parar de tratar isso como limitação aceitável. O bug é upstream e sem correção, mas isso não obriga o app a ficar sem a funcionalidade — dá pra implementar o seletor sem depender de portal nenhum.

**`UI/FileBrowserWindow.axaml`/`.cs` (novo)**: seletor de arquivos e pastas próprio, escrito só com `System.IO` — sem D-Bus, sem portal, sem toolkit nativo. Campo de caminho editável (Enter navega), botão "Acima", lista com diretórios primeiro e depois arquivos (ordem alfabética case-insensitive, ocultos filtrados), duplo clique navega/escolhe, filtro por extensão quando aplicável, e `Close(caminho)` devolvendo `string?` pra quem chamou. Pasta sem permissão de leitura mostra o motivo na própria lista em vez de fechar ou engolir o erro.

**Estratégia nos três botões Browse** (`PickFolderAsync`/`PickFileAsync`): tenta o seletor NATIVO primeiro — onde o portal funciona, o usuário ganha marcadores, pastas recentes e integração com o desktop — e cai automaticamente no seletor próprio se ele falhar. Ninguém fica sem seletor, e ninguém perde o nativo por causa de um ambiente quebrado.

Terceira e última rodada dos botões Browse; o histórico ficou registrado em comentário no próprio `OptionsWindow.axaml.cs` (crash → erro tratado → fallback funcional). `ShowFolderPickerError`/`GetFallbackDirectory` removidos: o fallback deixou de ser "mostrar erro e preencher um caminho padrão" e passou a ser um seletor que funciona.

**Erro real pego pela verificação, antes do build**: usei `.Select()` no `OptionsWindow` mas `System.Linq` não estava importado ali (o `using System.Collections.Generic`, que ficou órfão com a remoção do `IReadOnlyList<IStorageFile>`, deu lugar a ele). Teria quebrado o build — a checagem de usings vs. uso real valeu a rodada.

4 chaves i18n novas (`FileBrowser_Title`, `_TitleFolder`, `_Up`, `_Go`) — 303 por idioma, verificadas idênticas, todas com propriedade no Designer e handlers do XAML conferidos contra o code-behind.

## Estado final do projeto (2026-08-02)

As 6 fases concluídas, `.deb` validado por instalação real e por `lintian`. Resumo do que foi entregue: 18 janelas portadas de WPF para Avalonia, ~7.100 linhas de lógica preservadas e adaptadas, interface bilíngue (pt-BR/en, 299 chaves), recursos novos além do original (consultas DNS via nslookup/dig, drag-and-drop de reordenação), e empacotamento `.deb` que instala, aplica `cap_net_raw` automaticamente, registra ícone e entrada de menu, e desinstala limpo.

Bugs reais encontrados e corrigidos ao longo da migração que valem memória, por serem do tipo que reaparece: `Ping`+TTL não funciona para traceroute no Linux (limitação do dotnet/runtime, resolvida por shell-out); `InvariantGlobalization` anula silenciosamente todo o mecanismo de i18n; `aplay` toca lixo em vez de recusar formato não-WAV; comparar texto de ComboBox quebra ao traduzir a UI; `PublishSingleFile` não embute bibliotecas nativas; e `ldd` é incapaz de detectar dependências carregadas via `dlopen`.

### Identidade do fork: repositório próprio e atribuição (2026-08-02)

Usuário vai publicar o port em `https://github.com/rickdeckard82/vmPing-linux` e pediu pra ajustar a Ajuda com o repositório dele e o copyright, "mas deixe o antigo referenciado".

[Certo] Isso não é só preferência — a licença MIT do original **exige** preservar o aviso de copyright e a permissão em qualquer redistribuição. Estruturado como atribuição em três camadas, em todos os lugares onde o projeto se identifica:

- **`HelpWindow`**: título "vmPing" + subtítulo "Port para Linux (Avalonia)", link do fork em destaque, versão e `Copyright (C) 2026 Everton Herculano`; abaixo, bloco de atribuição — "Baseado no vmPing, de Ryan Smith", `Copyright (C) Ryan Smith`, link do projeto original (clicável, handler separado) e "Distribuído sob a Licença MIT". Os dois links abrem via `xdg-open` pelo mesmo helper.
- **`packaging/debian/control`**: `Homepage:` do fork, descrição atualizada (menciona traceroute, DNS e os dois idiomas) e linha de atribuição ao original com a licença.
- **`packaging/debian/copyright`**: `Source:` passa a apontar pro fork, com `Comment:` explicando a derivação e linkando o original. Os blocos por-arquivo (que já atribuíam corretamente cada arquivo copiado ao Ryan Smith) ficaram intactos.
- **`README.md`**: link do repositório do fork no topo, nota explícita sobre a licença MIT herdada e onde o aviso de copyright é preservado, e seção de status reescrita (o texto ainda dizia "9 janelas secundárias são stubs", desatualizado desde a Fase 4) listando o que o port acrescenta ao original: consultas DNS, dois idiomas, drag-and-drop, ícones vetoriais.

3 chaves i18n novas (`Help_ForkSubtitle`, `Help_BasedOn`, `Help_License`) — 299 por idioma, verificadas idênticas. Nomes próprios e URLs ficam fora do resx de propósito.

### Alinhamento das linhas rótulo+campo (2026-08-02)

Usuário reportou (screenshot de "Timeout de ping: [2] segundos"): rótulo e caixa desalinhados dentro da mesma linha. Causa: num `StackPanel Orientation="Horizontal"` o Avalonia estica os filhos verticalmente por padrão; o `TextBlock` fica no topo e a `TextBox` (mais alta) parece descer. Corrigido por script: `VerticalAlignment="Center"` em todos os filhos de linhas horizontais (111 controles em 14 janelas), preservando quem já tinha alinhamento próprio. Verificação semântica (nomes de propriedade de alinhamento válidos) rodada junto, seguindo a lição do erro do `MaxMinWidth` logo abaixo.

**Erro real de build, culpa minha** (`AVLN2000: Unable to resolve suitable regular or attached property MaxMinWidth`): no script de conversão usei `.replace('Width="', 'MinWidth="')` sem limite de palavra, então `MaxWidth="300"` (StatusHistoryWindow) virou `MaxMinWidth="300"`. Corrigido; varredura adicionada pra garantir que só existem `Width`/`MinWidth`/`MaxWidth` como nomes de propriedade em todos os `.axaml`. Lição registrada: edição em massa por script precisa de verificação **semântica** do resultado (nomes de propriedade válidos), não só de XML bem-formado — o XML estava perfeito, o nome da propriedade é que não existia.
