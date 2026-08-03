using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using vmPing.UI;

namespace vmPing;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Equivalente ao App.xaml.cs original. Parse de linha de comando
        // (Classes/CommandLine.cs) e carregamento de config (Classes/Configuration.cs)
        // acontecem dentro de MainWindow (Window_Loaded/InitializeApplication),
        // igual ao app original — não aqui.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
