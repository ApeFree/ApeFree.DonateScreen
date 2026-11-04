using ApeFree.DonateScreen.Master;
using System.Windows;
using System.Windows.Controls;

namespace ApeFree.DonateScreen.WinDesktop
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void OnToggleListenClicked(object sender, RoutedEventArgs e)
        {
            await ViewModel.ToggleListenAsync();
        }

        private void OnCopyPeerIdClicked(object sender, RoutedEventArgs e)
        {
            ViewModel.CopyMasterPeerId();
        }

        private async void OnConnectToSlaveClicked(object sender, RoutedEventArgs e)
        {
            await ViewModel.ConnectToSlaveAsync();
        }

        private async void OnToggleRelayServerClicked(object sender, RoutedEventArgs e)
        {
            await ViewModel.RelayViewModel.ToggleServerAsync();
        }

        private void OnClearRelayLogsClicked(object sender, RoutedEventArgs e)
        {
            ViewModel.RelayViewModel.ClearLogs();
        }

        private void OnControlSlaveClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SlaveSession session)
            {
                ViewModel.OpenRemoteViewer(session);
            }
        }
    }
}
