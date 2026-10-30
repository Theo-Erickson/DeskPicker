using Avalonia.Markup.Xaml;

namespace DeskPicker
{
    // Loads Styles.axaml (control looks). Added to the app after the Fluent theme, so it wins.
    public partial class DeskStyles : Avalonia.Styling.Styles
    {
        public DeskStyles() { AvaloniaXamlLoader.Load(this); }
    }
}
