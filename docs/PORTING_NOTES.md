# Notas de portabilidade — WPF/Windows → Avalonia/Linux

Decisões técnicas que explicam **por que o código está assim**. Cada item aqui
custou um bug real para ser descoberto; mexer nesses pontos sem ler antes tende
a reintroduzir o problema.

---

## Rede

### `Ping` + `PingOptions.Ttl` não serve para traceroute no Linux

`Classes/Probe-Traceroute.cs` e `UI/TraceRouteWindow.axaml.cs` chamam o
utilitário `traceroute` do sistema em vez de implementar TTL incremental com
`System.Net.NetworkInformation.Ping`. Isso não é preguiça: **a API do .NET não
funciona para esse caso no Linux.**

Em `Ping.RawSocket.cs` do `dotnet/runtime`, `NeedsConnect` é `true` no Linux, e o
socket ICMP raw é conectado ao endereço de destino "to scope responses only to
the target address". Um socket raw conectado no Linux só aceita pacotes cujo
endereço de origem bata com o peer conectado — então toda resposta ICMP
*Time Exceeded* vinda de um roteador intermediário é descartada pelo kernel
antes de chegar ao código gerenciado. Resultado: todo hop que não seja o destino
final aparece como `TimedOut`, mesmo com a rede respondendo normalmente.

Sintoma se alguém "otimizar" isso de volta: traceroute mostrando apenas o último
hop, com todos os anteriores em timeout.

**Dependência criada:** pacote `traceroute` (declarado em `debian/control`).

### Detecção de host não resolvido

`Probe-Tcp.cs` compara `ex.SocketErrorCode == SocketError.HostNotFound`. O
original comparava `ex.ErrorCode == 11001` (`WSAHOST_NOT_FOUND`, código do
WinSock) — que nunca dispara no Linux.

---

## Internacionalização

### `InvariantGlobalization` anula todo o mecanismo de tradução

O `.csproj` **não** deve ter `<InvariantGlobalization>true</InvariantGlobalization>`.
Em modo invariante o .NET não carrega ICU, toda cultura colapsa na invariante, e
o `ResourceManager` nunca resolve satellite assembly nenhum — o app fica
permanentemente em inglês, ignorando tanto o locale do sistema quanto a escolha
manual, sem erro algum.

**Dependência criada:** `libicu` (lista de alternativas em `debian/control`,
porque o nome do pacote muda a cada release da distro).

### A cultura precisa ser aplicada antes do bootstrap do Avalonia

`Classes/Localization.cs` é chamado no início de `Program.Main`, **antes** de
`BuildAvaloniaApp()`. As janelas resolvem `Strings.*` via `x:Static` no momento
da construção; se a cultura for definida depois, a primeira janela já nasceu no
idioma errado.

A leitura do idioma no arquivo de configuração é deliberadamente independente de
`Configuration.Load()`: essa usa `Util.ShowError`, que depende do Avalonia estar
de pé — chamá-la pré-bootstrap arriscaria crash no caminho de erro.

### Nunca comparar texto exibido de controle

`SaveGeneralOptions` usa `PingIntervalUnits.SelectedIndex`, não o texto do
ComboBox. A versão anterior comparava com `"minutes"`/`"hours"`; ao traduzir a
interface, a comparação parou de bater e **todo intervalo virava segundos
silenciosamente** — quem configurasse "5 minutos" teria ping a cada 5 segundos.

O helper `GetComboText` foi removido justamente para não convidar à repetição.

### Layout precisa de `MinWidth`, não `Width`

Rótulos e botões usam `MinWidth`. Com `Width` fixo — dimensionado a olho para o
inglês — o português (15–30% mais longo) é truncado. `MinWidth` preserva o
alinhamento em coluna e deixa crescer conforme o texto.

---

## Áudio

### Escolha do player depende do formato do arquivo

`Probe-Util.ResolveSoundPlayerCommand` recebe o caminho e monta a lista de
candidatos conforme a extensão: `aplay` só entra para `.wav`.

Motivo: `aplay` (ALSA) só decodifica WAV/PCM cru. Dado um Ogg, ele **não recusa**
— toca os bytes do container como se fossem PCM, produzindo ruído. Para formatos
comprimidos a ordem é `paplay` → `ffplay`.

**Dependência criada:** `Recommends: pulseaudio-utils | ffmpeg | alsa-utils`.

### Falha de reprodução pode ser silenciosa e indetectável

Alguns arquivos do `sound-theme-freedesktop` existem, têm tamanho normal, o
player retorna sucesso e **nada é ouvido** (observado com `dialog-warning.oga` e
`dialog-error.oga` no Debian 13). Nenhum código detecta isso.

Por isso `Constants.ResolveDefaultAudio` escolhe o primeiro de uma **lista** de
candidatos que exista no disco, em vez de um caminho fixo — e a aba Sons tem o
botão *Testar*, que é a única forma real de verificar.

### Alerta sonoro só dispara em transição de status

`Probe-Icmp.cs` trata o primeiro estado observado de um probe (seja `Up` ou
`Down`) como silencioso de propósito: só transições entre estados já
estabelecidos disparam `OnStatusChange`. Um probe novo apontado para um host
morto **não** toca alerta — comportamento herdado do original, para não disparar
uma enxurrada de alertas ao adicionar vários hosts já fora do ar.

Consequência para testes: Stop/Start não valida alerta sonoro. É preciso derrubar
a conectividade de um probe que já esteja `Up`.

---

## Interface

### Substituições sem equivalente direto

| WPF | Avalonia | Observação |
|---|---|---|
| `Visibility.Hidden/Collapsed` | `IsVisible` (bool) | perde a distinção "reserva espaço" vs "remove espaço" |
| `RoutedCommand`/`CommandBinding` | `Click` + `Classes/RelayCommand.cs` | `ShowDialog()` é assíncrono, forçou vários métodos a `async void` |
| `ICollectionView`/`CollectionViewSource.Filter` | recomputar e reatribuir `ItemsSource` | usado no filtro do histórico de status |
| Fonte Marlett (glifos) | Unicode (▲▼●) | Marlett não existe fora do Windows |
| `System.Windows.Forms` dialogs | `IStorageProvider` + fallback próprio | ver abaixo |
| `DllImport("user32.dll")` | removido | hacks de chrome de janela, sem equivalente nem necessidade |

### Compiled bindings exigem `x:DataType`

O `.csproj` usa `AvaloniaUseCompiledBindingsByDefault`. Todo `DataTemplate`
precisa de `x:DataType` explícito, senão o binding é resolvido contra
`XamlPseudoType` e o build falha com `AVLN2000`. Classes usadas como `x:DataType`
precisam ser `public`.

### `StringFormat` iniciado por `{` precisa da escapatória `{}`

`StringFormat='{}{0:#,0}'`. Sem o `{}` inicial, o parser XAML tenta resolver o
valor como markup extension. Valores que começam com outro caractere
(`'[{0}]'`) não precisam.

### O gerador de nomes cria um campo por `Name=` no XAML

`Name="FloodHost"` gera um campo `FloodHost` na classe parcial — que colide com
um método de mesmo nome no code-behind (`CS0102`). Ao nomear controles, confira
se não existe membro homônimo.

### Estados de hover/foco do Fluent sobrescrevem `Background` local

`UI/ControlStyles.axaml` prende o fundo dos estados `:pointerover`/`:focus` ao
`Background` declarado do próprio controle. Sem isso, um `TextBox` escuro fica
branco ao passar o mouse — o template do Fluent troca o fundo do
`PART_BorderElement` pelos recursos claros do tema.

### Seletor de arquivos: nativo com fallback próprio

`OptionsWindow.PickFolderAsync`/`PickFileAsync` tentam o `IStorageProvider`
(diálogo nativo via `xdg-desktop-portal`) e caem em `UI/FileBrowserWindow` se
ele falhar.

Motivo: um bug conhecido do portal
([#1653](https://github.com/flatpak/xdg-desktop-portal/issues/1653),
[#1756](https://github.com/flatpak/xdg-desktop-portal/issues/1756)) faz a chamada
retornar `AccessDenied` em alguns ambientes. Sem tratamento, a exceção sobe até o
loop de dispatch do Avalonia e **derruba o app inteiro** — exceção não tratada em
handler assíncrono é fatal.

O seletor próprio usa apenas `System.IO`: sem D-Bus, sem portal, sem toolkit
nativo.

---

## Empacotamento

### `PublishSingleFile` não embute bibliotecas nativas

O publish deixa `libSkiaSharp.so` (renderizador do Avalonia) e
`libHarfBuzzSharp.so` como arquivos **separados** ao lado do executável. "Single
file" significa "todo o código *gerenciado* num arquivo", não "tudo num arquivo".

`build-deb.sh` copia todos os artefatos do publish (exceto `.pdb`) e compara a
contagem publish vs. pacote, falhando se algo ficar de fora.

**`ldd` não detecta essa falta**: o .NET carrega essas bibliotecas via `dlopen`
em runtime, não por link dinâmico. Um pacote quebrado passa por `ldd` limpo.

### Satellite assemblies de tradução

Com `PublishSingleFile`, o .NET embute os satellites no executável (não existe
pasta `pt-BR/` no publish, e está correto). O loop no `build-deb.sh` que copia
pastas de cultura é rede de segurança para o caso de o modo de publish mudar.

### Permissões e umask

`build-deb.sh` normaliza permissões explicitamente (diretórios 755, arquivos 644,
executáveis 755). `install`/`mkdir -p` herdam o umask do usuário — com `umask 002`
o pacote sai com **diretórios graváveis pelo grupo dentro de `/usr`**.
`--root-owner-group` corrige dono, mas não permissão.

### Não chamar `gtk-update-icon-cache` nos maintainer scripts

O `hicolor-icon-theme` já registra um trigger do dpkg que faz isso na hora certa.
Chamar manualmente duplica o trabalho fora de ordem. O mesmo vale para
`update-desktop-database` (trigger do `desktop-file-utils`).

### `set -e` e `cmd && cmd`

Em script com `set -euo pipefail`, uma linha `[ -n "$x" ] && echo ...` **encerra o
script** quando a condição é falsa. Foi o que abortou a primeira execução real do
`build-deb.sh`, num comando que era só diagnóstico auxiliar. Use `if/else` ou
`|| true`.

### `CAP_NET_RAW`

O ping ICMP com payload customizado exige socket raw. O `postinst` aplica
`setcap cap_net_raw+ep` ao binário instalado. Em builds de desenvolvimento a
capability é perdida a cada rebuild:

```bash
sudo setcap cap_net_raw+ep bin/Debug/net8.0/linux-x64/vmping
```

---

## Limitações de plataforma (não são bugs do port)

- **Tray icon no GNOME**: o GNOME Shell removeu o suporte nativo a área de
  notificação na versão 3.26. Nenhum dos dois backends do Avalonia
  (`DBusTrayIconImpl` / `XEmbedTrayIconImpl`) funciona sem a extensão
  AppIndicator.
- **Ícone no dock rodando via `dotnet run`**: o dash do GNOME resolve o ícone
  pelo `.desktop` instalado, não pela janela. Só aparece com o pacote instalado.
