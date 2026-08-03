using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Threading;

namespace vmPing.Classes
{
    // [Certo] FASE 4 — trocado de `class` (internal) para `public class`.
    // Motivo: UI/TraceRouteWindow.axaml usa x:DataType="classes:NetworkRoute"
    // pra bindings compilados (AvaloniaUseCompiledBindingsByDefault=true no
    // .csproj); em vez de apostar que o compilador XAML resolve x:DataType
    // contra um tipo internal (não verificado contra o código-fonte do
    // XamlX/Avalonia nesta sessão), a correção trivial e sem efeito colateral
    // é só tornar o tipo público — mesma classe, mesmo assembly, ninguém de
    // fora do projeto o usa mesmo assim.
    public class NetworkRoute : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool isActive;
        public bool IsActive
        {
            get { return isActive; }
            set
            {
                if (value != isActive)
                {
                    isActive = value;
                    NotifyPropertyChanged("IsActive");
                }
            }
        }

        public string DestinationHost { get; set; }
        public IPAddress DestinationIp { get; set; }
        public int MaxHops { get; set; }
        public int PingTimeout { get; set; }
        public Stopwatch Timer { get; set; }

        public BackgroundWorker BgWorker { get; set; }
        public AutoResetEvent ResetEvent { get; set; }

        public ObservableCollection<NetworkRouteNode> networkRoute = new ObservableCollection<NetworkRouteNode>();

        private void NotifyPropertyChanged(string info)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(info));
        }
    }
}
