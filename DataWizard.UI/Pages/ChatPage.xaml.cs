using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using System;
using System.IO;
using System.Threading.Tasks;
using DataWizard.Core.Services;
using System.Collections.Generic;
using DataWizard.UI.Services;
using System.Diagnostics;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Shapes;
using System.Linq;

namespace DataWizard.UI.Pages
{
    public sealed partial class ChatPage : Page
    {
        private string selectedFilePath = "";
        private readonly string outputTextPath = @"C:\Project PBTGM\DataSample\hasil_output.txt";
        private readonly DatabaseService _dbService;
        private readonly int _currentUserId = 1; // Temporary hardcoded user ID
        private Stopwatch _processTimer;

        public ChatPage()
        {
            this.InitializeComponent();
            _dbService = new DatabaseService();
            PromptBox.TextChanged += PromptBox_TextChanged;
            _processTimer = new Stopwatch();
            LoadUserPreferences();
        }

        private async void LoadUserPreferences()
        {
            try
            {
                // Get recent history to show last used format
                var recentHistory = await _dbService.GetRecentHistoryAsync(_currentUserId, 1);
                var lastFormat = recentHistory.FirstOrDefault()?.OutputFormat ?? "Excel";

                // Reset format buttons
                WordFormatButton.Style = Resources["DefaultFormatButtonStyle"] as Style;
                ExcelFormatButton.Style = Resources["DefaultFormatButtonStyle"] as Style;

                // Set preferred format
                if (lastFormat.Equals("Word", StringComparison.OrdinalIgnoreCase))
                {
                    WordFormatButton.Style = Resources["SelectedFormatButtonStyle"] as Style;
                    OutputFormatBox.SelectedIndex = 2;
                }
                else
                {
                    ExcelFormatButton.Style = Resources["SelectedFormatButtonStyle"] as Style;
                    OutputFormatBox.SelectedIndex = 1;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading preferences: {ex.Message}");
                // Default to Excel
                ExcelFormatButton.Style = Resources["SelectedFormatButtonStyle"] as Style;
                OutputFormatBox.SelectedIndex = 1;
            }
        }

        private async Task ShowDialogAsync(string title, string content)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private void PromptBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            CharCountText.Text = $"{PromptBox.Text.Length}/1000";
        }

        private async Task<bool> SelectFileAsync()
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".xlsx");
            picker.FileTypeFilter.Add(".xls");
            picker.FileTypeFilter.Add(".csv");
            picker.FileTypeFilter.Add(".docx");
            picker.FileTypeFilter.Add(".pdf");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                selectedFilePath = file.Path;
                return true;
            }
            return false;
        }

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            string prompt = PromptBox.Text.Trim();
            string outputFormat = (OutputFormatBox.SelectedItem as ComboBoxItem)?.Content?.ToString().ToLower() ?? "excel";
            string mode = (ModeBox.SelectedItem as ComboBoxItem)?.Content?.ToString().ToLower() ?? "file";

            if (string.IsNullOrWhiteSpace(prompt))
            {
                await ShowDialogAsync("Validation Error", "Please enter a prompt.");
                return;
            }

            if ((mode == "file" || mode == "ocr") && string.IsNullOrWhiteSpace(selectedFilePath))
            {
                await ShowDialogAsync("Validation Error", $"Please select a file for {mode.ToUpper()} mode.");
                return;
            }

            try
            {
                _processTimer.Restart();
                WelcomePanel.Visibility = Visibility.Collapsed;
                AnswerBox.Visibility = Visibility.Visible;
                OutputBox.Text = "Processing data... Please wait.";

                // Get input file type ID
                int inputFileTypeId;
                if (mode == "prompt-only")
                {
                    inputFileTypeId = await _dbService.GetFileTypeId("PROMPT");
                }
                else if (!string.IsNullOrEmpty(selectedFilePath))
                {
                    string fileExtension = Path.GetExtension(selectedFilePath).TrimStart('.').ToUpper();
                    inputFileTypeId = await _dbService.GetFileTypeId(fileExtension);
                }
                else
                {
                    inputFileTypeId = await _dbService.GetFileTypeId("UNKNOWN");
                }

                // Get output format ID
                int outputFormatId = outputFormat == "word" ?
                    await _dbService.GetOutputFormatId("Word") :
                    await _dbService.GetOutputFormatId("Excel");

                // Log history before processing
                int historyId = await _dbService.LogHistoryAsync(
                    _currentUserId,
                    inputFileTypeId,
                    outputFormatId,
                    prompt,
                    mode);

                // Process the request
                string result = await PythonRunner.RunPythonScriptAsync(
                    mode == "prompt-only" ? "none" : selectedFilePath ?? "none",
                    outputTextPath,
                    prompt,
                    outputFormat,
                    mode);

                _processTimer.Stop();
                int processingTimeMs = (int)_processTimer.ElapsedMilliseconds;

                if (result == "Success" || result == "OK")
                {
                    if (File.Exists(outputTextPath))
                    {
                        string output = File.ReadAllText(outputTextPath);
                        if (output.StartsWith("[ERROR]"))
                        {
                            OutputBox.Text = $"Process failed: {output}";
                            await _dbService.UpdateHistoryStatusAsync(historyId, false, processingTimeMs);
                            return;
                        }

                        OutputBox.Text = output;
                        string outputFileName = string.Empty;
                        string outputFilePath = string.Empty;

                        if (outputFormat == "excel")
                        {
                            string parsedExcelPath = PythonRunner.GetParsedExcelPath(outputTextPath);
                            if (File.Exists(parsedExcelPath))
                            {
                                outputFilePath = parsedExcelPath;
                                outputFileName = Path.GetFileName(parsedExcelPath);
                                ResultFileText.Text = outputFileName;

                                // Log output file
                                FileInfo fileInfo = new FileInfo(parsedExcelPath);
                                await _dbService.LogOutputFileAsync(
                                    historyId,
                                    outputFileName,
                                    outputFilePath,
                                    fileInfo.Length);
                            }
                        }
                        else if (outputFormat == "word")
                        {
                            string wordPath = outputTextPath.Replace("hasil_output.txt", "hasil_output_output.docx");
                            if (File.Exists(wordPath))
                            {
                                outputFilePath = wordPath;
                                outputFileName = Path.GetFileName(wordPath);
                                ResultFileText.Text = outputFileName;

                                // Log output file
                                FileInfo fileInfo = new FileInfo(wordPath);
                                await _dbService.LogOutputFileAsync(
                                    historyId,
                                    outputFileName,
                                    outputFilePath,
                                    fileInfo.Length);
                            }
                        }

                        // Update history with processing time
                        await _dbService.UpdateHistoryProcessingTimeAsync(historyId, processingTimeMs);
                    }
                    else
                    {
                        OutputBox.Text = "Process completed but output file not found.";
                        await _dbService.UpdateHistoryStatusAsync(historyId, false, processingTimeMs);
                    }
                }
                else
                {
                    OutputBox.Text = $"Process failed: {result}";
                    await _dbService.UpdateHistoryStatusAsync(historyId, false, processingTimeMs);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in RunButton_Click: {ex}");
                OutputBox.Text = $"An error occurred: {ex.Message}";
                await ShowDialogAsync("Error", "An unexpected error occurred. Please try again.");
            }
        }

        private async void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var historyData = await _dbService.GetRecentHistoryAsync(_currentUserId, 10);
                var stackPanel = new StackPanel { Spacing = 10 };

                if (!historyData.Any())
                {
                    stackPanel.Children.Add(new TextBlock
                    {
                        Text = "No conversion history found.",
                        FontSize = 14,
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                else
                {
                    foreach (var history in historyData)
                    {
                        var itemContainer = CreateHistoryItemContainer(history);
                        stackPanel.Children.Add(itemContainer);

                        if (history != historyData.Last())
                        {
                            stackPanel.Children.Add(new Rectangle
                            {
                                Height = 1,
                                Fill = new SolidColorBrush(Microsoft.UI.Colors.LightGray),
                                Margin = new Thickness(0, 8, 0, 8)
                            });
                        }
                    }
                }

                var dialog = new ContentDialog
                {
                    Title = "Conversion History",
                    Content = new ScrollViewer
                    {
                        Content = stackPanel,
                        MaxHeight = 500
                    },
                    CloseButtonText = "Close",
                    XamlRoot = this.XamlRoot
                };

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading history: {ex}");
                await ShowDialogAsync("Error", "Failed to load history. Please try again.");
            }
        }

        private StackPanel CreateHistoryItemContainer(HistoryItem history)
        {
            var container = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12
            };

            var icon = new Image
            {
                Width = 28,
                Height = 28,
                Source = new BitmapImage(new Uri($"ms-appx:///Assets/Microsoft {history.OutputFormat} 2024.png"))
            };

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock
            {
                Text = $"{history.InputType} → {history.OutputFormat}",
                FontSize = 14,
                FontWeight = history.IsSuccess ? FontWeights.Normal : FontWeights.SemiBold,
                Foreground = history.IsSuccess ?
                    new SolidColorBrush(Microsoft.UI.Colors.Black) :
                    new SolidColorBrush(Microsoft.UI.Colors.Red)
            });

            info.Children.Add(new TextBlock
            {
                Text = $"{history.ProcessDate:dd/MM/yyyy HH:mm} • {history.ProcessingTime}ms",
                FontSize = 12,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray)
            });

            container.Children.Add(icon);
            container.Children.Add(info);

            return container;
        }

        private async void SaveFileButton_Click(object sender, RoutedEventArgs e)
        {
            var savePicker = new FileSavePicker();
            savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            savePicker.FileTypeChoices.Add("Excel Files", new List<string>() { ".xlsx" });
            savePicker.FileTypeChoices.Add("Word Documents", new List<string>() { ".docx" });
            savePicker.SuggestedFileName = ResultFileText.Text;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

            var file = await savePicker.PickSaveFileAsync();
            if (file != null)
            {
                try
                {
                    int currentFolderId = 1; // Default folder ID
                    await _dbService.SaveFileToFolderAsync(
                        _currentUserId,
                        currentFolderId,
                        file.Name,
                        file.Path);

                    OutputBox.Text = $"File saved to: {file.Path}";
                }
                catch (Exception ex)
                {
                    await ShowDialogAsync("Error", $"Failed to save file: {ex.Message}");
                }
            }
        }

        private async void FileToFileButton_Click(object sender, RoutedEventArgs e)
        {
            ModeBox.SelectedIndex = 0;
            await SelectFileAsync();
        }

        private async void PromptToFileButton_Click(object sender, RoutedEventArgs e)
        {
            ModeBox.SelectedIndex = 2;
            await ShowDialogAsync("Reminder", "Please select your output format (Word or Excel) before proceeding.");
            PromptBox.Focus(FocusState.Programmatic);
        }

        private async void OcrToFileButton_Click(object sender, RoutedEventArgs e)
        {
            ModeBox.SelectedIndex = 1;
            await SelectFileAsync();
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(HomePage));
        }

        private async void AddAttachmentButton_Click(object sender, RoutedEventArgs e)
        {
            await SelectFileAsync();
        }

        private async void UseImageButton_Click(object sender, RoutedEventArgs e)
        {
            ModeBox.SelectedIndex = 1;
            await SelectFileAsync();
        }

        private async void OutputFormatButton_Click(object sender, RoutedEventArgs e)
        {
            Button clickedButton = sender as Button;
            string format = clickedButton.Tag.ToString();

            WordFormatButton.Style = Resources["DefaultFormatButtonStyle"] as Style;
            ExcelFormatButton.Style = Resources["DefaultFormatButtonStyle"] as Style;

            clickedButton.Style = Resources["SelectedFormatButtonStyle"] as Style;
            OutputFormatBox.SelectedIndex = format == "word" ? 2 : 1;
        }

        private void RefreshPromptButton_Click(object sender, RoutedEventArgs e)
        {
            PromptBox.Text = "";
            selectedFilePath = "";
            OutputBox.Text = "";
            OutputFormatBox.SelectedIndex = 0;
            ModeBox.SelectedIndex = 0;

            WordFormatButton.Style = Resources["DefaultFormatButtonStyle"] as Style;
            ExcelFormatButton.Style = Resources["DefaultFormatButtonStyle"] as Style;

            WelcomePanel.Visibility = Visibility.Visible;
            AnswerBox.Visibility = Visibility.Collapsed;
        }

        private void SelectFileButton_Click(object sender, RoutedEventArgs e)
        {
            _ = SelectFileAsync();
        }
    }
}