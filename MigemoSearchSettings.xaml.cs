using System.Windows;
using System.Windows.Controls;

namespace Wox.Plugin.MigemoSearch
{
    public partial class MigemoSearchSettings : UserControl
    {
        private readonly Settings _settings;

        public MigemoSearchSettings(Settings settings)
        {
            InitializeComponent();
            _settings = settings;
        }

        private void View_Loaded(object sender, RoutedEventArgs re)
        {
            UseLocationAsWorkingDir.IsChecked = _settings.UseLocationAsWorkingDir;

            UseLocationAsWorkingDir.Checked += (o, e) =>
            {
                _settings.UseLocationAsWorkingDir = true;
            };

            UseLocationAsWorkingDir.Unchecked += (o, e) =>
            {
                _settings.UseLocationAsWorkingDir = false;
            };
        }
    }
}
