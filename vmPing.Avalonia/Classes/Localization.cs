using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;

namespace vmPing.Classes
{
    // FASE 5/i18n — suporte a dois idiomas (inglês + português do Brasil).
    //
    // Mecanismo: o padrão de localização do próprio .NET (resx + satellite
    // assemblies). Properties/Strings.resx é o idioma neutro (inglês);
    // Properties/Strings.pt-BR.resx gera vmping.resources.dll na subpasta
    // pt-BR/ automaticamente pelo csproj SDK-style, sem configuração extra.
    // O ResourceManager escolhe pela CurrentUICulture da thread.
    //
    // A cultura precisa estar definida ANTES de qualquer janela ser construída
    // (as janelas leem Strings.* via x:Static no XAML compilado, resolvido na
    // construção) — por isso ApplyConfiguredCulture() é chamada no início do
    // Program.Main, antes do bootstrap do Avalonia. Consequência assumida:
    // trocar o idioma nas Options só tem efeito completo depois de reiniciar
    // o app (mesma abordagem de muitos apps desktop; documentado na UI).
    //
    // [Certo] A leitura do arquivo de config aqui é deliberadamente
    // independente de Configuration.Load(): Load() usa Util.ShowError em caso
    // de falha, que depende do Avalonia já estar de pé — chamá-la antes do
    // bootstrap arriscaria crash no caminho de erro. Esta leitura enxuta
    // engole qualquer falha e cai no modo "auto" (locale do sistema).
    public static class Localization
    {
        public const string Auto = "auto";
        public const string English = "en-US";
        public const string PortugueseBrazil = "pt-BR";

        public static void ApplyConfiguredCulture()
        {
            var language = Auto;
            try
            {
                if (File.Exists(Configuration.FilePath))
                {
                    var xd = XDocument.Load(Configuration.FilePath);
                    language = xd.Descendants("option")
                        .FirstOrDefault(o => (string?)o.Attribute("name") == "Language")
                        ?.Value ?? Auto;
                }
            }
            catch
            {
                // Config ilegível/corrompida: segue no locale do sistema. O
                // startup normal (Configuration.Load) vai reportar o problema
                // pelo caminho de erro de sempre, com UI de pé.
            }

            Apply(language);
        }

        public static void Apply(string language)
        {
            ApplicationOptions.Language = Normalize(language);

            if (ApplicationOptions.Language == Auto)
            {
                // Locale do sistema decide — não mexe em nada.
                return;
            }

            try
            {
                var culture = CultureInfo.GetCultureInfo(ApplicationOptions.Language);
                // DefaultThreadCurrentUICulture cobre também as threads de
                // probe (Task.Run), que montam mensagens via Strings.*.
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                Properties.Strings.Culture = culture;
            }
            catch (CultureNotFoundException)
            {
                // Valor inválido gravado à mão no XML: ignora, modo auto.
                ApplicationOptions.Language = Auto;
            }
        }

        private static string Normalize(string language) => language switch
        {
            English => English,
            PortugueseBrazil => PortugueseBrazil,
            // Compatibilidade com valores curtos escritos à mão no XML.
            "en" => English,
            "pt" => PortugueseBrazil,
            "pt-br" => PortugueseBrazil,
            _ => Auto,
        };
    }
}
