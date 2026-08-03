using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using vmPing.Properties;

namespace vmPing.Classes
{
    // [Certo] Avalonia não tem System.Windows.Visibility (Visible/Hidden/Collapsed).
    // Controles Avalonia usam a propriedade bool IsVisible diretamente. Os
    // conversores abaixo que retornavam Visibility agora retornam bool.
    // PERDA DE SEMÂNTICA: WPF distinguia Hidden (invisível mas ainda ocupa
    // espaço no layout) de Collapsed (invisível e remove o espaço). Avalonia
    // IsVisible=false se comporta como o Collapsed do WPF — não existe
    // equivalente direto ao Hidden. Se alguma tela do vmPing original dependia
    // de reservar espaço com Hidden (revisar Fase 4 ao portar cada XAML), a
    // alternativa é usar Opacity=0 + IsHitTestVisible=false num elemento que
    // continua no layout, em vez de IsVisible=false.

    public class BoolToValueConverter<T> : IValueConverter
    {
        public T FalseValue { get; set; }
        public T TrueValue { get; set; }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is bool v && v
                ? TrueValue
                : FalseValue;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value != null && value.Equals(TrueValue);
        }
    }

    public class BoolToStringConverter : BoolToValueConverter<string> { }

    // Antes: bool -> Visibility.Visible/Hidden. Agora: bool -> bool (para IsVisible).
    public class BooleanToHiddenVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is bool v && v;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }

    public class InverseBooleanConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return !(value is bool v && v);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }

    // Antes: bool -> Visibility.Collapsed/Visible. Agora: bool -> bool (IsVisible invertido).
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return !(value is bool v && v);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }

    // [Chutando] Original verificava Visibility.Hidden OU Collapsed. Sem esses dois
    // estados no Avalonia, este conversor vira equivalente a InverseBooleanConverter
    // (ambos operam sobre bool agora). Mantido como classe separada só para não
    // quebrar nomes referenciados em XAML ainda não portado (Fase 4) — considerar
    // remover e apontar os bindings direto para InverseBooleanConverter.
    public class InverseHiddenToBoolConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is bool v && !v;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }

    public class BooleanToImageConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool v && !v)
            {
                return (DrawingImage)Application.Current.Resources["icon.play"];
            }
            else
            {
                return (DrawingImage)Application.Current.Resources["icon.stop-circle"];
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }

    // [Provável] Avalonia não tem BrushConverter (TypeConverter do WPF).
    // Brush.Parse(string) é o equivalente direto — aceita os mesmos formatos
    // hex ("#RRGGBB"/"#AARRGGBB") usados em Constants.cs / ApplicationOptions.
    internal static class ProbeBrushHelper
    {
        public static IBrush FromColorString(string color)
        {
            try
            {
                return Brush.Parse(color);
            }
            catch
            {
                return Brushes.Transparent;
            }
        }
    }

    public class ProbeStatusToBackgroundBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (!(value is ProbeStatus))
            {
                return Brushes.Transparent;
            }

            switch ((ProbeStatus)value)
            {
                case ProbeStatus.Up:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.BackgroundColor_Probe_Up);
                case ProbeStatus.Down:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.BackgroundColor_Probe_Down);
                case ProbeStatus.Error:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.BackgroundColor_Probe_Error);
                case ProbeStatus.LatencyHigh:
                case ProbeStatus.Indeterminate:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.BackgroundColor_Probe_Indeterminate);
                case ProbeStatus.Scanner:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.BackgroundColor_Probe_Scanner);
                default:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.BackgroundColor_Probe_Inactive);
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }

    public class ProbeStatusToForegroundBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (!(value is ProbeStatus))
            {
                return Brushes.Transparent;
            }

            switch ((ProbeStatus)value)
            {
                case ProbeStatus.Up:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Probe_Up);
                case ProbeStatus.Down:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Probe_Down);
                case ProbeStatus.Error:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Probe_Error);
                case ProbeStatus.LatencyHigh:
                case ProbeStatus.Indeterminate:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Probe_Indeterminate);
                case ProbeStatus.Scanner:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Probe_Scanner);
                default:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Probe_Inactive);
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }

    public class ProbeStatusToStatisticsBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (!(value is ProbeStatus))
            {
                return Brushes.Transparent;
            }

            switch ((ProbeStatus)value)
            {
                case ProbeStatus.Up:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Stats_Up);
                case ProbeStatus.Down:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Stats_Down);
                case ProbeStatus.Error:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Stats_Error);
                case ProbeStatus.LatencyHigh:
                case ProbeStatus.Indeterminate:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Stats_Indeterminate);
                default:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Stats_Inactive);
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }

    public class ProbeStatusToAliasBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (!(value is ProbeStatus))
            {
                return Brushes.Transparent;
            }

            switch ((ProbeStatus)value)
            {
                case ProbeStatus.Up:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Alias_Up);
                case ProbeStatus.Down:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Alias_Down);
                case ProbeStatus.Error:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Alias_Error);
                case ProbeStatus.LatencyHigh:
                case ProbeStatus.Indeterminate:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Alias_Indeterminate);
                case ProbeStatus.Scanner:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Alias_Scanner);
                default:
                    return ProbeBrushHelper.FromColorString(ApplicationOptions.ForegroundColor_Alias_Inactive);
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }

    public class StringToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var str = value as string;
            if (string.IsNullOrWhiteSpace(str))
            {
                return BindingOperations.DoNothing;
            }

            try
            {
                return Brush.Parse(str);
            }
            catch
            {
                return BindingOperations.DoNothing;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }

    public class HostnameFontsizeConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (!(value is double))
            {
                return 12.5;
            }

            double width = (double)value;

            if (width > 250) return 18;
            if (width > 225) return 17;
            if (width > 200) return 16;
            if (width > 175) return 15;
            if (width > 150) return 14;
            if (width > 125) return 13;
            return 12.5;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }

    // Antes: double -> Visibility.Visible/Collapsed. Agora: double -> bool (IsVisible).
    public class ButtonTextVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is double v && v > 300;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }

    // [Certo] Original usava a fonte Marlett (Windows-only, glifos de sistema
    // mapeados em posições de caractere tipo "t"/"u"/"i") para desenhar um
    // triângulo de status ao lado do hostname. Marlett não existe no Linux.
    // Troquei para caracteres Unicode simples (▲▼●) — MainWindow.axaml não deve
    // mais setar FontFamily="Marlett" no TextBlock que consome este conversor.
    public class ProbeStatusToGlyphConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (!(value is ProbeStatus))
            {
                return string.Empty;
            }

            switch ((ProbeStatus)value)
            {
                case ProbeStatus.Up:
                    return "▲"; // ▲
                case ProbeStatus.Down:
                    return "▼"; // ▼
                case ProbeStatus.LatencyHigh:
                case ProbeStatus.Indeterminate:
                    return "●"; // ●
                default:
                    return string.Empty;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }


    public class ProbeCountToGlobalStartStopText : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            long count = (value is long v) ? v : 0;
            return count > 0
                ? Strings.Toolbar_StopAll
                : Strings.Toolbar_StartAll;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }

    public class ProbeCountToGlobalStartStopIcon : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            long count = (value is long v) ? v : 0;
            return count > 0
                ? (DrawingImage)Application.Current.Resources["icon.stop-circle"]
                : (DrawingImage)Application.Current.Resources["icon.play"];
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }

    public class ProbeTypeToFontSizeConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (!(value is ProbeType))
            {
                return ApplicationOptions.FontSize_Probe;
            }

            switch ((ProbeType)value)
            {
                case ProbeType.Ping:
                    return ApplicationOptions.FontSize_Probe;
                default:
                    return ApplicationOptions.FontSize_Scanner;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }

    public class StringLengthToBoolConverter : IValueConverter
    {
        // Return true if string is not empty.
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return !string.IsNullOrEmpty(value as string);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }

    // [Certo] FASE 4 — usado por UI/StatusHistoryWindow. Cores fixas extraídas
    // de UI/StatusHistoryWindow.xaml original (não vêm de ApplicationOptions
    // como os conversores de status do grid principal, porque no original
    // também eram cores fixas hard-coded nos DataTrigger do DataGrid).
    public class StatusChangeLogToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not ProbeStatus status)
            {
                return Brushes.White;
            }

            switch (status)
            {
                case ProbeStatus.Down:
                case ProbeStatus.Error:
                    return Brush.Parse("#dc322f");
                case ProbeStatus.Up:
                    return Brush.Parse("#859900");
                case ProbeStatus.Start:
                    return Brush.Parse("#61b8ff");
                case ProbeStatus.Stop:
                    return Brush.Parse("#ecce51");
                default:
                    return Brushes.White;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }

    // [Certo] FASE 4 — usado por UI/TraceRouteWindow. No original, o DataGrid
    // usava três <DataTrigger Binding="{Binding HostAddress}" Value="..."> pra
    // pintar de vermelho e esconder a coluna de RTT quando o hop não teve
    // resposta válida ("Timed out", "Invalid hostname", "0.0.0.0" — os três
    // valores literais que Classes/NetworkRoute.cs / TraceRouteWindow escrevem
    // em HostAddress nesses casos). Avalonia não tem DataTrigger; centralizado
    // aqui em vez de repetir a mesma checagem em dois converters soltos.
    public static class HopAddressStatus
    {
        public static bool IsError(string? address) =>
            address == "Timed out" || address == "Invalid hostname" || address == "0.0.0.0";
    }

    public class HopAddressToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return HopAddressStatus.IsError(value as string)
                ? Brush.Parse("#dc322f")
                : Brush.Parse("#b6fab4");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }

    public class HopAddressToRttVisibleConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return !HopAddressStatus.IsError(value as string);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingOperations.DoNothing;
        }
    }
}
