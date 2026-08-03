using System;
using System.Windows.Input;

namespace vmPing.Classes
{
    // [Certo] Avalonia não tem RoutedCommand/CommandBinding do WPF (o padrão que
    // MainWindow.xaml.cs original usava para ligar MenuItem + KeyGesture ao mesmo
    // handler). Substituído por ICommand simples + Window.KeyBindings do Avalonia
    // (que aceita qualquer ICommand). MenuItems usam Click direto no
    // handler; KeyBindings usam este RelayCommand só para os atalhos de teclado
    // que não têm MenuItem equivalente clicável (ex: F5 = Start/Stop, que no
    // menu já tem Click próprio — o RelayCommand aqui evita duplicar lógica).
    internal sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;

        public RelayCommand(Action execute)
        {
            _execute = execute;
        }

#pragma warning disable CS0067 // Nunca usado — mantido só para satisfazer ICommand.
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute();
    }
}
