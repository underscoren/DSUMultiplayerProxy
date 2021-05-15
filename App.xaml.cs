﻿using System.Windows;

namespace DSUMultiplayerProxy
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("Unhandled Error: " + e.Exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }
    }
}