using System;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace FXOAiTranslate.WPF.Views
{
    public partial class WebQuoteWindow : Window
    {
        public WebQuoteWindow()
        {
            InitializeComponent();
            InitializeWebView();
        }

        private async void InitializeWebView()
        {
            try
            {
                // Ensure the CoreWebView2 environment is initialized
                await webView.EnsureCoreWebView2Async();

                // Map a virtual host to the local assets directory
                // This allows accessing files via https://app.local/ instead of file:///
                // which is more secure and avoids CORS issues.
                string assetsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Web");
                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app.local", 
                    assetsFolder, 
                    CoreWebView2HostResourceAccessKind.Allow
                );

                // Disable default context menus and dev tools for a "native" feel
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                // webView.CoreWebView2.Settings.AreDevToolsEnabled = false; // Keep enabled for now for debugging

                // Navigate to the index page via the virtual host
                webView.CoreWebView2.Navigate("https://app.local/index.html");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
