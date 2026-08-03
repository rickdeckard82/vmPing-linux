#!/usr/bin/env bash
# Empacota vmPing (Avalonia) como .deb binário.
# Rode numa máquina com dotnet 8 SDK e dpkg-deb (qualquer Debian/Ubuntu).
#
# Uso: ./build-deb.sh [versao]
set -euo pipefail

APP_NAME="vmping"
VERSION="${1:-1.0.1}"
ARCH="amd64"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/../vmPing.Avalonia" && pwd)"
STAGE_DIR="$(mktemp -d)"
PKG_DIR="$STAGE_DIR/${APP_NAME}_${VERSION}_${ARCH}"

trap 'rm -rf "$STAGE_DIR"' EXIT

echo "==> Publicando build self-contained linux-x64..."
dotnet publish "$PROJECT_DIR" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o "$STAGE_DIR/publish"

echo "==> Montando árvore do pacote..."
mkdir -p "$PKG_DIR/DEBIAN"
mkdir -p "$PKG_DIR/usr/lib/$APP_NAME"
mkdir -p "$PKG_DIR/usr/bin"
mkdir -p "$PKG_DIR/usr/share/applications"
mkdir -p "$PKG_DIR/usr/share/doc/$APP_NAME"

install -m 755 "$STAGE_DIR/publish/vmping" "$PKG_DIR/usr/lib/$APP_NAME/vmping"
# lintian: symlink dentro do mesmo top-level (/usr) deve ser RELATIVO, não
# absoluto (absolute-symlink-in-top-level-folder). Sobrevive melhor a montagens
# em prefixo diferente e a chroots.
ln -s "../lib/$APP_NAME/vmping" "$PKG_DIR/usr/bin/vmping"

# [Certo] BIBLIOTECAS NATIVAS — bug real encontrado na 1ª execução com pacote
# gerado: `PublishSingleFile` NÃO embute bibliotecas nativas por padrão
# (IncludeNativeLibrariesForSelfExtract=false). O publish deixa libSkiaSharp.so
# (~9 MB, renderizador do Avalonia) e libHarfBuzzSharp.so (~2,4 MB, layout de
# texto) como arquivos SEPARADOS ao lado do executável. O script copiava só o
# binário — o .deb resultante instalava um app que não tinha como renderizar
# nada. "Single file" aqui significa "o código gerenciado num arquivo só", não
# "tudo num arquivo só". Copia-se tudo que o publish produziu, exceto símbolos
# de depuração (.pdb), que não têm por que ir num pacote de distribuição.
for artifact in "$STAGE_DIR"/publish/*; do
    name="$(basename "$artifact")"
    case "$name" in
        vmping|*.pdb) continue ;;              # binário já instalado; .pdb fora
    esac
    if [ -f "$artifact" ]; then
        install -m 644 "$artifact" "$PKG_DIR/usr/lib/$APP_NAME/$name"
        echo "    biblioteca incluída: $name"
    fi
done

# [Certo] i18n — CONFIRMADO em instalação real (2026-08-02): com
# PublishSingleFile, o .NET EMBUTE os satellite assemblies de tradução no
# próprio executável; não existe pasta pt-BR/ no publish e o app instalado
# abre corretamente em português. O loop abaixo existe como rede de segurança
# caso o modo de publish mude no futuro (ex: PublishSingleFile=false, que
# volta a gerar as pastas de cultura) — sem ele, um `.deb` gerado nesse modo
# sairia só em inglês silenciosamente. Não avisa quando não encontra: a
# ausência é o comportamento normal e esperado no modo atual.
for culture_dir in "$STAGE_DIR"/publish/*/; do
    [ -d "$culture_dir" ] || continue
    culture="$(basename "$culture_dir")"
    if [ -f "$culture_dir/vmping.resources.dll" ]; then
        mkdir -p "$PKG_DIR/usr/lib/$APP_NAME/$culture"
        install -m 644 "$culture_dir/vmping.resources.dll" \
            "$PKG_DIR/usr/lib/$APP_NAME/$culture/vmping.resources.dll"
        echo "    idioma incluído: $culture"
    fi
done

# FASE 5 — correção: antes instalava o PNG de 16px como se fosse 256x256
# (ícone borrado/genérico no dock). Agora instala os 3 tamanhos reais
# extraídos do vmPing.ico original, cada um no diretório hicolor correto.
for size in 16 32 48; do
    if [ -f "$PROJECT_DIR/Assets/vmPing-${size}.png" ]; then
        mkdir -p "$PKG_DIR/usr/share/icons/hicolor/${size}x${size}/apps"
        install -m 644 "$PROJECT_DIR/Assets/vmPing-${size}.png" \
            "$PKG_DIR/usr/share/icons/hicolor/${size}x${size}/apps/vmping.png"
    fi
done

install -m 644 "$SCRIPT_DIR/vmping.desktop" "$PKG_DIR/usr/share/applications/vmping.desktop"
install -m 644 "$SCRIPT_DIR/debian/copyright" "$PKG_DIR/usr/share/doc/$APP_NAME/copyright"

# lintian: no-changelog. Pacote nativo exige changelog.gz comprimido com -n
# (sem timestamp) para builds reproduzíveis.
changelog_tmp="$STAGE_DIR/changelog"
cat > "$changelog_tmp" <<EOF
$APP_NAME ($VERSION) unstable; urgency=medium

  * Port do vmPing (WPF/Windows) para Linux com Avalonia UI e .NET 8.
  * Interface em português e inglês.
  * Consultas DNS (nslookup/dig) e traceroute usando os utilitários do sistema.

 -- Everton Herculano <everton@ehs.eti.br>  $(date -R)
EOF
gzip -9n -c "$changelog_tmp" > "$PKG_DIR/usr/share/doc/$APP_NAME/changelog.gz"
chmod 644 "$PKG_DIR/usr/share/doc/$APP_NAME/changelog.gz"

# lintian: no-manual-page. Man page mínima em roff, comprimida.
mkdir -p "$PKG_DIR/usr/share/man/man1"
man_tmp="$STAGE_DIR/vmping.1"
cat > "$man_tmp" <<'EOF'
.TH VMPING 1 "2026" "vmPing" "Comandos do usuário"
.SH NOME
vmping \- monitor gráfico de ping para múltiplos hosts
.SH SINOPSE
.B vmping
.RI [ -i " intervalo" ]
.RI [ -w " timeout" ]
.RI [ hostname ...]
.RI [ arquivo ...]
.SH DESCRIÇÃO
.B vmPing
monitora a disponibilidade de vários hosts simultaneamente, com codificação por
cores, histórico de mudanças de status, alertas sonoros e por e-mail. Inclui
traceroute, flood de host e consultas DNS (nslookup e dig).
.SH OPÇÕES
.TP
.BI \-i " intervalo"
Intervalo, em segundos, entre os pings. Faixa válida: 1 a 86400.
.TP
.BI \-w " timeout"
Tempo limite, em segundos, de espera por cada resposta. Faixa válida: 1 a 60.
.TP
.B \-minimized
Inicia o aplicativo minimizado.
.SH ARQUIVOS
.TP
.I ~/.config/vmPing/vmPing.xml
Configuração, favoritos e aliases do usuário.
.SH NOTAS
O ping ICMP usa sockets raw e exige a capability
.BR cap_net_raw ,
aplicada ao binário durante a instalação do pacote.
.SH AUTOR
Port para Linux por Everton Herculano.
Baseado no vmPing original de Ryan Smith <https://github.com/R-Smith/vmPing>.
.SH VEJA TAMBÉM
.BR ping (8),
.BR traceroute (1),
.BR dig (1)
EOF
gzip -9n -c "$man_tmp" > "$PKG_DIR/usr/share/man/man1/vmping.1.gz"
chmod 644 "$PKG_DIR/usr/share/man/man1/vmping.1.gz"

# lintian: embedded-library é inerente a um publish self-contained (o .NET e o
# SkiaSharp trazem expat/freetype/libjpeg/etc. estaticamente). Não há como
# "corrigir" sem abandonar o self-contained — que é justamente o que faz o
# pacote rodar sem exigir o runtime .NET instalado. Documentado como override.
mkdir -p "$PKG_DIR/usr/share/lintian/overrides"
cat > "$PKG_DIR/usr/share/lintian/overrides/$APP_NAME" <<EOF
# Publish self-contained do .NET: libSkiaSharp.so embute expat, freetype,
# libjpeg, libpng, libwebp e zlib. É o custo consciente de distribuir um
# binário que não exige o runtime .NET instalado no sistema alvo.
$APP_NAME: embedded-library
# Binário produzido pelo SDK do .NET; strip quebra o bundle single-file.
$APP_NAME: unstripped-binary-or-object
EOF
chmod 644 "$PKG_DIR/usr/share/lintian/overrides/$APP_NAME"

sed "s/@VERSION@/$VERSION/" "$SCRIPT_DIR/debian/control" > "$PKG_DIR/DEBIAN/control"
install -m 755 "$SCRIPT_DIR/debian/postinst" "$PKG_DIR/DEBIAN/postinst"
# postrm NÃO é instalado: só continha comentários e `exit 0` (lintian:
# maintainer-script-empty). Script de mantenedor vazio é ruído — o arquivo
# fonte fica no repositório documentando a decisão de não limpar ~/.config.

# lintian: non-standard-dir-perm. `install`/`mkdir -p` herdam o umask do
# usuário (0002 aqui, resultando em diretórios 0775 group-writable dentro de
# /usr — indesejado em qualquer pacote). --root-owner-group corrige dono, mas
# não permissão. Normaliza tudo: diretórios 755, e arquivos 644 exceto os
# executáveis, que são reaplicados logo abaixo.
find "$PKG_DIR" -type d -exec chmod 755 {} +
find "$PKG_DIR" -type f -exec chmod 644 {} +
chmod 755 "$PKG_DIR/usr/lib/$APP_NAME/vmping"
chmod 755 "$PKG_DIR/DEBIAN/postinst"

echo "==> Verificando o binário publicado..."
# Dependências nativas reais do binário — rodado ANTES do dpkg-deb porque o
# diretório de stage é apagado no trap de saída.
if command -v ldd >/dev/null 2>&1; then
    echo "--- ldd (bibliotecas do SISTEMA não encontradas, se houver) ---"
    missing="$(ldd "$PKG_DIR/usr/lib/$APP_NAME/vmping" 2>/dev/null | grep -i "not found" || true)"
    if [ -n "$missing" ]; then
        echo "$missing"
        echo "ATENÇÃO: bibliotecas faltando acima — revise o Depends do control." >&2
    else
        echo "    nenhuma biblioteca do sistema faltando."
    fi
    # [Certo] ldd NÃO detecta libSkiaSharp/libHarfBuzzSharp faltando: o .NET as
    # carrega via dlopen em runtime, não por link dinâmico — por isso o ldd
    # passou limpo mesmo com o pacote quebrado na execução anterior. A checagem
    # abaixo compara o que o publish gerou com o que entrou no pacote.
    echo "--- artefatos do publish vs. pacote ---"
    pub_count="$(find "$STAGE_DIR/publish" -maxdepth 1 -type f ! -name '*.pdb' | wc -l)"
    pkg_count="$(find "$PKG_DIR/usr/lib/$APP_NAME" -maxdepth 1 -type f | wc -l)"
    echo "    publish: $pub_count arquivo(s) (sem .pdb) | pacote: $pkg_count"
    if [ "$pub_count" -ne "$pkg_count" ]; then
        echo "ATENÇÃO: contagem diferente — algum artefato do publish ficou de fora." >&2
    fi
fi

# [Certo] Sem InvariantGlobalization (removido na rodada de i18n), o runtime
# exige ICU. O nome do pacote muda a cada release da distro, daí a lista de
# alternativas no Depends — este aviso mostra qual está no sistema atual.
# Nota: os `|| true` não são decorativos — com `set -e`, um `[ ... ] && echo`
# que avalia falso encerra o script inteiro (foi o que abortou a 1ª execução
# real, antes de gerar o .deb).
if command -v dpkg >/dev/null 2>&1; then
    icu_pkg="$(dpkg -l 2>/dev/null | awk '/^ii +libicu[0-9]/ {print $2; exit}')" || true
    if [ -n "${icu_pkg:-}" ]; then
        echo "    ICU deste sistema: $icu_pkg (confira se está no Depends do control)"
    else
        echo "    AVISO: nenhum pacote libicu* instalado — o app pode não iniciar aqui." >&2
    fi
fi

echo "==> Construindo pacote .deb..."
OUT="${APP_NAME}_${VERSION}_${ARCH}.deb"
dpkg-deb --build --root-owner-group "$PKG_DIR" "$OUT"

echo
echo "==> Pronto: $OUT"
echo "Verificações recomendadas antes de distribuir:"
echo "  dpkg -c $OUT                  # conteúdo/permissões"
echo "  dpkg -I $OUT                  # metadados e dependências"
echo "  lintian $OUT                  # (se instalado) lint do pacote"
echo "  sudo apt install ./$OUT       # instala resolvendo dependências (melhor que dpkg -i)"
echo "  vmping                        # teste real; confira também o ícone no menu de aplicativos"
