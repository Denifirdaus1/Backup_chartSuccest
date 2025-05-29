using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using System;
using Microsoft.UI;
using DataWizard.UI.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI.Text;
using Microsoft.UI.Text;
using System.Collections.Generic;
using ScottPlot;
using System.Linq;

namespace DataWizard.UI.Pages
{
    public sealed partial class HomePage : Page
    {
        private readonly DatabaseService _dbService;
        private readonly int _currentUserId = 1; // Temporary hardcoded user ID
        private ObservableCollection<OutputFile> _recentFiles;
        private ObservableCollection<Folder> _folders;

        public HomePage()
        {
            InitializeComponent();
            _dbService = new DatabaseService();
            _recentFiles = new ObservableCollection<OutputFile>();
            _folders = new ObservableCollection<Folder>();
            
            // Initialize data
            _ = LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            try
            {
                // Load recent files
                var recentFiles = await _dbService.GetRecentFilesAsync(_currentUserId, 5);
                _recentFiles.Clear();
                foreach (var file in recentFiles)
                {
                    _recentFiles.Add(file);
                }

                // Load folders
                var folders = await _dbService.GetUserFoldersAsync(_currentUserId);
                _folders.Clear();
                foreach (var folder in folders)
                {
                    _folders.Add(folder);
                }

                // Load recent history for activity display
                var history = await _dbService.GetRecentHistoryAsync(_currentUserId, 5);
                UpdateRecentFiles(history);
                UpdateFolders();

                // Load chart data
                await LoadChartDataAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading data: {ex.Message}");
                await ShowErrorDialog("Error", "Failed to load data. Please try again.");
            }
        }

        private async Task LoadChartDataAsync()
        {
            try
            {
                var stats = await _dbService.GetFileTypeStatsAsync(_currentUserId);
                
                if (stats != null && stats.Any())
                {
                    var plt = UsageChart.Plot;
                    plt.Clear();

                    // Extract data for plotting
                    var values = stats.Select(s => (double)s.Value).ToArray();
                    var labels = stats.Select(s => s.Label).ToArray();

                    // Create bar plot
                    var bar = plt.AddBar(values);
                    plt.XTicks(Enumerable.Range(0, labels.Length).Select(i => (double)i).ToArray(), labels);
                    
                    // Customize appearance
                    plt.Title("File Type Usage");
                    plt.YLabel("Count");
                    
                    UsageChart.Refresh();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading chart: {ex.Message}");
            }
        }

        private void UpdateRecentFiles(List<HistoryItem> historyItems)
        {
            RecentFilesPanel.Children.Clear();

            foreach (var item in historyItems)
            {
                var historyControl = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Margin = new Thickness(0, 0, 0, 16)
                };

                // Add icon based on output format
                var icon = new Image
                {
                    Width = 24,
                    Height = 24,
                    Source = new BitmapImage(new Uri($"ms-appx:///Assets/Microsoft {item.OutputFormat} 2024.png"))
                };

                // Add text information
                var textPanel = new StackPanel();
                textPanel.Children.Add(new TextBlock
                {
                    Text = $"{item.InputType} → {item.OutputFormat}",
                    FontWeight = FontWeights.SemiBold
                });

                textPanel.Children.Add(new TextBlock
                {
                    Text = FormatTime(item.ProcessDate),
                    Foreground = new SolidColorBrush(Colors.Gray),
                    FontSize = 12
                });

                historyControl.Children.Add(icon);
                historyControl.Children.Add(textPanel);

                RecentFilesPanel.Children.Add(historyControl);
            }
        }

        private void UpdateFolders()
        {
            FoldersPanel.Children.Clear();
            foreach (var folder in _folders)
            {
                var folderItem = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Margin = new Thickness(0, 0, 0, 16)
                };

                var icon = new Image
                {
                    Width = 24,
                    Height = 24,
                    Source = new BitmapImage(new Uri("ms-appx:///Assets/Folder.png"))
                };

                var textBlock = new TextBlock
                {
                    Text = folder.FolderName,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };

                folderItem.Children.Add(icon);
                folderItem.Children.Add(textBlock);

                FoldersPanel.Children.Add(folderItem);
            }
        }

        private string FormatTime(DateTime date)
        {
            var diff = DateTime.Now - date;
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}h ago";
            return date.ToString("dd MMM yyyy");
        }

        private async Task ShowErrorDialog(string title, string message)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private void UsageChart_Loaded(object sender, RoutedEventArgs e)
        {
            _ = LoadChartDataAsync();
        }

        private void NewProjectButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ChatPage));
        }

        private void UserProfileButton_Click(object sender, RoutedEventArgs e)
        {
            // Implement user profile functionality
            Debug.WriteLine("User profile button clicked");
        }
    }
}