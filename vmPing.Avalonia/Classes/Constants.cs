using Avalonia.Input;

namespace vmPing.Classes
{
    class Constants
    {
        // Default probe background colors.
        public const string Color_Probe_Background_Inactive = "#eceafa";
        public const string Color_Probe_Background_Up = "#859900";
        public const string Color_Probe_Background_Down = "#dc322f";
        public const string Color_Probe_Background_Indeterminate = "#dfdf00";
        public const string Color_Probe_Background_Error = "#b58900";
        public const string Color_Probe_Background_Scanner = "#505050";

        // Default probe foreground colors.
        public const string Color_Probe_Foreground_Inactive = "#839496";
        public const string Color_Probe_Foreground_Up = "#002b36";
        public const string Color_Probe_Foreground_Down = "#002b36";
        public const string Color_Probe_Foreground_Indeterminate = "#002b36";
        public const string Color_Probe_Foreground_Error = "#000000";
        public const string Color_Probe_Foreground_Scanner = "#f0f0f0";

        // Default statistics foreground colors.
        public const string Color_Statistics_Foreground_Inactive = "#657b83";
        public const string Color_Statistics_Foreground_Up = "#fdf6e3";
        public const string Color_Statistics_Foreground_Down = "#fdf6e3";
        public const string Color_Statistics_Foreground_Indeterminate = "#111";
        public const string Color_Statistics_Foreground_Error = "#ffffff";

        // Default alias / probe title colors.
        public const string Color_Alias_Foreground_Inactive = "#000000";
        public const string Color_Alias_Foreground_Up = "#ffff00";
        public const string Color_Alias_Foreground_Down = "#ffff00";
        public const string Color_Alias_Foreground_Indeterminate = "#ffffff";
        public const string Color_Alias_Foreground_Error = "#ffff00";
        public const string Color_Alias_Foreground_Scanner = "#ffff00";

        // Default probe options.
        public const string DefaultIcmpData = "https://github.com/R-Smith/vmPing";
        public const int DefaultTimeout = 2000;       // In miliseconds.
        public const int DefaultTTL = 64;
        public const int DefaultInterval = 2000;      // In miliseconds.

        // Default audio alert file paths.
        // [Certo] Paths originais eram Windows-only (%WINDIR%). Substituídos por sons
        // do freedesktop sound theme (pacote sound-theme-freedesktop).
        //
        // Bug real reportado: o alerta de "host fora do ar" não emitia som algum,
        // enquanto o de "host no ar" funcionava. Diagnóstico por teste direto no
        // terminal (`ffplay <arquivo>`, sem o app envolvido): o antigo padrão
        // `dialog-warning.oga` — e também `dialog-error.oga` — existe no disco,
        // tem tamanho normal e o player retorna sucesso, mas NÃO produz som
        // audível neste tema. Não é bug do vmPing: são arquivos quebrados/mudos
        // do próprio sound theme, e nenhum código consegue detectar isso (o
        // player relata êxito). Trocado por `suspend-error.oga`, confirmado
        // audível, e semanticamente melhor (som descendente = algo caiu).
        //
        // Como um caminho fixo é frágil entre distros, estes são apenas os
        // PRIMEIROS candidatos: ResolveDefaultAudio (abaixo) escolhe o primeiro
        // que existir de fato no sistema.
        public static readonly string DefaultAudioDownFilePath = ResolveDefaultAudio(
            "suspend-error.oga", "bell.oga", "dialog-warning.oga", "message.oga");

        public static readonly string DefaultAudioUpFilePath = ResolveDefaultAudio(
            "complete.oga", "message.oga", "bell.oga", "dialog-information.oga");

        private const string SoundThemeDir = "/usr/share/sounds/freedesktop/stereo";

        // Devolve o primeiro candidato que existe no disco; se nenhum existir,
        // devolve o primeiro mesmo assim — assim o campo nas Opções mostra um
        // caminho plausível e o erro (arquivo não encontrado) aparece de forma
        // clara ao testar, em vez de um campo vazio sem explicação.
        private static string ResolveDefaultAudio(params string[] candidates)
        {
            foreach (var name in candidates)
            {
                var path = System.IO.Path.Combine(SoundThemeDir, name);
                if (System.IO.File.Exists(path))
                {
                    return path;
                }
            }
            return System.IO.Path.Combine(SoundThemeDir, candidates[0]);
        }

        // Key bindings.
        public const Key StatusHistoryKeyBinding = Key.F12;
        public const Key HelpKeyBinding = Key.F1;
    }
}
