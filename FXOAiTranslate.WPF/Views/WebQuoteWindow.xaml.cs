using System;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace FXOAiTranslate.WPF.Views
{
    /// <summary>
    /// FX Aggregator window displaying HTML-based multi-LP quote interface
    /// </summary>
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

                // Map a virtual host to the Documentation directory
                // This allows accessing files via https://app.local/ instead of file:///
                // which is more secure and avoids CORS issues.
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string documentationFolder = Path.Combine(baseDir, "Documentation");

                // Create Documentation folder if it doesn't exist
                if (!Directory.Exists(documentationFolder))
                {
                    Directory.CreateDirectory(documentationFolder);
                }

                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app.local",
                    documentationFolder,
                    CoreWebView2HostResourceAccessKind.Allow
                );

                // Disable default context menus for a "native" feel
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                // Keep dev tools enabled for debugging
                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

                // Navigate to the FX aggregator page via the virtual host
                webView.CoreWebView2.Navigate("https://app.local/fx_aggregator_fixed_380.html");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
