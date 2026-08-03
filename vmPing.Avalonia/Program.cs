using Avalonia;
using System;

namespace vmPing;

internal class Program
{
    // Ponto de entrada. Equivalente ao App.xaml.cs original (Application_Startup),
    // mas Avalonia exige um bootstrap explícito em vez do mecanismo implícito do WPF.
    [STAThread]
    public static void Main(string[] args)
    {
        // i18n: cultura definida ANTES do bootstrap do Avalonia — as janelas
        // resolvem Strings.* via x:Static na construção (ver Localization.cs).
        Classes.Localization.ApplyConfiguredCulture();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
