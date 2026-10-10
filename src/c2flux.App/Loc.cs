using System;
using System.ComponentModel;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

namespace c2flux
{
    // LocalizationService as a bindable source: "{l:T Menu.Settings}" in XAML
    // follows language changes at runtime, and windows follow FlowDirection.
    public sealed class Loc : INotifyPropertyChanged
    {
        public static Loc Instance { get; } = new Loc();

        private Loc()
        {
            LocalizationService.LanguageChanged += () => Dispatcher.UIThread.Post(Refresh);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string this[string key] => LocalizationService.GetText(key);

        public FlowDirection FlowDirection =>
            LocalizationService.IsRightToLeft(LocalizationService.CurrentLanguageCode)
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;

        private void Refresh()
        {
            // Avalonia's indexer bindings listen for "Item".
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FlowDirection)));
        }
    }

    // {l:T Key}: the text of Key in the current language.
    public sealed class TExtension : MarkupExtension
    {
        public TExtension(string key)
        {
            Key = key;
        }

        public string Key { get; set; }

        // Drops leading symbols ("◔ Pie chart" -> "Pie chart"), as the WinForms
        // toolbar did for texts shown next to a drawn icon.
        public bool StripSymbol { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return new ReflectionBinding("[" + Key + "]")
            {
                Source = Loc.Instance,
                Mode = BindingMode.OneWay,
                Converter = StripSymbol ? StripSymbolConverter.Instance : null,
            };
        }

        private sealed class StripSymbolConverter : Avalonia.Data.Converters.IValueConverter
        {
            public static readonly StripSymbolConverter Instance = new StripSymbolConverter();

            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                string text = (value as string ?? string.Empty).Trim();
                int start = 0;

                while (start < text.Length && !char.IsLetterOrDigit(text[start]))
                {
                    start++;
                }

                return start < text.Length ? text.Substring(start) : text;
            }

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }
    }
}
