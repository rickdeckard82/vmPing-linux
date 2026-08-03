# Segurança

## Reportar uma vulnerabilidade

Abra uma issue em https://github.com/rickdeckard82/vmPing-linux/issues. Para
algo que julgue sensível, use o e-mail do mantenedor no `debian/control`.

Este é um port não-oficial mantido por uma pessoa, sem SLA de resposta.

## Modelo de ameaça

O vmPing é um app de desktop monousuário. Ele **não** abre portas, não escuta
conexões, não roda serviço em segundo plano e não executa código vindo da rede.
As respostas de ping, traceroute e DNS são tratadas como **texto exibido**, nunca
interpretadas ou executadas.

O que ele faz que merece atenção:

| Ação | Superfície |
|---|---|
| Envia pacotes ICMP raw | exige `CAP_NET_RAW` no binário |
| Executa `traceroute`, `dig`, `nslookup`, player de áudio | processos filhos com argumentos vindos da UI |
| Grava logs e configuração | caminhos escolhidos pelo usuário |
| Envia e-mail de alerta (opcional) | credenciais SMTP guardadas em disco |

## Privilégios

O binário instalado recebe `cap_net_raw+ep` via `postinst`, **não** é `setuid
root`. Essa capability permite abrir sockets raw — o mínimo para o ping ICMP com
payload customizado — e nada além disso. O processo roda como o usuário comum.

Se preferir não conceder a capability, remova-a com
`sudo setcap -r /usr/lib/vmping/vmping`. O ping ICMP deixa de funcionar; ping TCP
(`host:porta`), traceroute e DNS continuam.

## Execução de processos externos

Todos os processos filhos são iniciados com `UseShellExecute = false` e
argumentos passados via `ProcessStartInfo.ArgumentList` — cada argumento vai
separado para `execve()`, **sem shell e sem parsing de aspas**. Não há
concatenação de string na montagem de linha de comando em nenhum ponto do
projeto.

Consequência prática: um hostname como `example.com; rm -rf ~` é passado como um
argumento único e literal para o `traceroute`, que simplesmente falha ao
resolvê-lo. Não há caminho de injeção de comando.

## Credenciais de e-mail — limitação conhecida

Se você habilitar alertas por e-mail com autenticação, usuário e senha do SMTP
são gravados em `~/.config/vmPing/vmPing.xml`, cifrados com AES.

**A cifra é ofuscação, não proteção.** A chave é derivada de uma string presente
no código-fonte público, combinada com o nome da máquina e do usuário. Quem tiver
acesso de leitura ao arquivo e ao código consegue recuperar a senha. Isso é
herdado do vmPing original e mantido por compatibilidade de formato.

Mitigação aplicada: o arquivo é criado e mantido com permissão `0600` (leitura
apenas pelo dono), em vez de herdar o umask — que na maioria das distros
produziria `0644`, legível por qualquer usuário local.

**Recomendação:** use uma conta de e-mail dedicada a alertas, com senha de
aplicativo revogável, nunca a senha principal de uma conta pessoal ou
corporativa.

## Arquivos gravados

| Caminho | Conteúdo |
|---|---|
| `~/.config/vmPing/vmPing.xml` | configuração, favoritos, aliases, credenciais SMTP (0600) |
| Diretório escolhido nas Opções | logs de ping, um arquivo por host |
| `~/Documentos/vmping-status-history.csv` | exportação manual do histórico |

Nomes de arquivo derivados de hostname passam por sanitização
(`Util.GetSafeFilename`), que remove `/`, `\`, `:` e outros caracteres — não é
possível usar um hostname para escapar do diretório de logs.

A desinstalação **não** apaga `~/.config/vmPing` — é dado do usuário.

## Componentes de terceiros

O `.deb` é self-contained: embute o runtime .NET e o Avalonia (com SkiaSharp e
HarfBuzz, que por sua vez trazem expat, freetype, libjpeg, libpng, libwebp e
zlib). Isso significa que **correções de segurança dessas bibliotecas não chegam
pelo `apt upgrade` do sistema** — dependem de um novo release deste pacote.

É a contrapartida consciente de distribuir um binário que roda sem exigir o
runtime .NET instalado. Atribuições completas em `packaging/debian/copyright`.

## Verificação do pacote

Cada release publica um `SHA256SUMS`:

```bash
sha256sum -c SHA256SUMS
```

Os pacotes não são assinados com GPG. Baixe apenas da página de Releases deste
repositório.
