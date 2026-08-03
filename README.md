# vmPing for Linux (port não-oficial)

Repositório deste port: **https://github.com/rickdeckard82/vmPing-linux**

Este é um **port não-oficial para Linux** do [vmPing](https://github.com/R-Smith/vmPing), originalmente escrito por **Ryan Smith** para Windows (WPF/.NET Framework). Este port não é afiliado, mantido ou endossado pelo autor original.

Licença MIT, herdada do projeto original — o aviso de copyright de Ryan Smith é preservado no código, na janela de Ajuda e em `packaging/debian/copyright`, como a licença exige.

Se você curtiu o vmPing, considere apoiar o projeto original:
- Repositório: https://github.com/R-Smith/vmPing
- Discord: https://discord.gg/Guf66Zk6US
- Doação (PayPal, ao autor original): https://paypal.me/SmithRyn/15

Bugs e comportamento específicos deste port para Linux **não devem ser reportados no repositório original** — ele não tem relação com este código.

## O que é isto

Reescrita da camada de UI (WPF → [Avalonia UI](https://avaloniaui.net/)) e adaptação da lógica de rede, áudio e persistência para rodar nativamente em Linux, empacotável como `.deb`.

## Status

Funcional. Todas as 18 janelas portadas e testadas em execução real: grid de hosts com ping ICMP/TCP, traceroute (janela dedicada e inline), flood host, histórico de status com exportação, favoritos, aliases, opções completas e visão isolada por host.

Além do original, este port acrescenta:

- **Consultas DNS** (`nslookup` e `dig`) no menu, com tipo de registro, servidor específico (`@8.8.8.8`) e flags do dig.
- **Interface em português e inglês**, com seleção manual ou automática pelo idioma do sistema (Opções → Exibição).
- Reordenação de hosts por arrastar e soltar, ícones vetoriais e tema claro consistente.

As decisões técnicas do port — e os motivos por trás de cada uma — estão em [`docs/PORTING_NOTES.md`](./docs/PORTING_NOTES.md). Vale a leitura antes de mexer no código de rede, áudio ou empacotamento.

## Instalação

Baixe o `.deb` em [Releases](https://github.com/rickdeckard82/vmPing-linux/releases) e instale:

```bash
sudo apt install ./vmping_1.0.1_amd64.deb
```

Use `apt install` (e não `dpkg -i`) para que as dependências sejam resolvidas automaticamente. A instalação já aplica a capability `CAP_NET_RAW` ao binário, então o ping ICMP funciona sem `sudo`.

Dependências: `traceroute`, `bind9-dnsutils` (nslookup/dig), `libicu` e `libcap2-bin`. Recomendado ter um player de áudio (`pulseaudio-utils`, `ffmpeg` ou `alsa-utils`) para os alertas sonoros.

## Compilar do código-fonte

Requer .NET 8 SDK.

```bash
cd vmPing.Avalonia
dotnet restore
dotnet build
dotnet run
```

Rodando direto via `dotnet run` (fora do `.deb`), o binário precisa da capability `CAP_NET_RAW` para o ping ICMP — e ela é perdida a cada rebuild, então reaplique:

```bash
sudo setcap cap_net_raw+ep bin/Debug/net8.0/linux-x64/vmping
```

## Estrutura do repositório

```
vmPing.Avalonia/     código-fonte (Avalonia UI + .NET 8)
  Classes/           lógica de rede, configuração, conversores
  UI/                janelas (.axaml + code-behind)
  Properties/        strings traduzidas (en / pt-BR)
packaging/           empacotamento .deb
  build-deb.sh       publish + montagem do pacote
  debian/            control, copyright, postinst
docs/
  PORTING_NOTES.md   decisões técnicas do port (por que o código está assim)
```

## Limitações conhecidas

- **Ícone da bandeja não aparece no GNOME Shell puro** (Ubuntu, Fedora Workstation, Debian com GNOME, sem extensões): o GNOME removeu suporte nativo a área de notificação desde a versão 3.26. Instale a extensão "AppIndicator and KStatusNotifierItem Support" (`gnome-shell-extension-appindicator` ou via extensions.gnome.org) e reinicie o Shell. Não é um bug deste port — afeta qualquer app Linux com tray icon em GNOME sem essa extensão.
- **Sons do tema podem estar mudos dependendo da distro**: os alertas sonoros usam arquivos do `sound-theme-freedesktop`. Em algumas instalações certos arquivos existem mas não produzem áudio (observado com `dialog-warning.oga` e `dialog-error.oga` no Debian 13) — o player relata sucesso e nada é ouvido, então o app não tem como detectar. Se um alerta ficar mudo, use o botão **Testar** na aba Sons e escolha outro arquivo pelo **Procurar...**. É preciso ter ao menos um player de áudio instalado (`pulseaudio-utils`, `ffmpeg` ou `alsa-utils` — declarados em `Recommends`).

### Resolvido, mas registrado

- **Seletor de arquivo/pasta**: em ambientes afetados por um bug conhecido do `xdg-desktop-portal` ([#1653](https://github.com/flatpak/xdg-desktop-portal/issues/1653), [#1756](https://github.com/flatpak/xdg-desktop-portal/issues/1756)), o diálogo nativo falha com `AccessDenied`. O app tenta o seletor nativo primeiro e, se ele falhar, abre automaticamente um seletor próprio — o botão "Procurar..." funciona em qualquer ambiente.

## Gerar o pacote .deb

```bash
cd packaging
./build-deb.sh 1.0.1
sudo apt install ./vmping_1.0.1_amd64.deb
```

O script publica um build self-contained (não exige o runtime .NET instalado), monta a árvore do pacote com ícones e entrada de menu, e verifica se todos os artefatos do publish entraram no pacote. O `.deb` gerado passa `lintian` sem avisos.

## Licença

MIT, herdada do projeto original (`LICENSE` na raiz do repositório). Ver `packaging/debian/copyright` para atribuições detalhadas, incluindo componentes de terceiros embutidos no pacote (.NET runtime, Avalonia).
