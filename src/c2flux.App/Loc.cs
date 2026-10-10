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

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return new ReflectionBinding("[" + Key + "]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
        }
    }
}
