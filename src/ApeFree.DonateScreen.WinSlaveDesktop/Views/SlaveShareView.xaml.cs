using System.Windows;
using System.Windows.Controls;

namespace ApeFree.DonateScreen.WinSlaveDesktop.Views
{
    public partial class SlaveShareView : UserControl
    {
        public SlaveShareView()
        {
            InitializeComponent();
        }

        private async void OnActionClicked(object sender, RoutedEventArgs e)
        {
            await ViewModel.ToggleConnectAsync();
        }

        private void OnEmergencyClicked(object sender, RoutedEventArgs e)
        {
            ViewModel.EmergencyCutoff();
        }
    }
}
