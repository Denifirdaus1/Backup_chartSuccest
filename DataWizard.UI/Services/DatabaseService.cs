using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DataWizard.UI.Services
{
    public class DatabaseService
    {
        private readonly HttpClient _httpClient;
        private readonly string _supabaseUrl;
        private readonly string _supabaseKey;

        public DatabaseService()
        {
            _supabaseUrl = "https://rrlmejrtlqnfaavyrrtf.supabase.co";
            _supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJybG1lanJ0bHFuZmFhdnlycnRmIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NDgyMzI5NzUsImV4cCI6MjA2MzgwODk3NX0.8uC7og_bfk2C-Ok6KNGAY5Ej-nz_wBz07-94BG1rUZY";

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("apikey", _supabaseKey);
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_supabaseKey}");
            _httpClient.DefaultRequestHeaders.Add("Prefer", "return=representation");
        }

        public async Task<(bool success, UserData user, string error)> ValidateUserCredentialsAsync(string username, string password)
        {
            try
            {
                Debug.WriteLine($"Attempting login for user: {username}");

                var payload = new
                {
                    p_username = username,
                    p_password = password
                };

                var jsonContent = JsonSerializer.Serialize(payload);
                Debug.WriteLine($"Login payload: {jsonContent}");

                var response = await _httpClient.PostAsync(
                    $"{_supabaseUrl}/rest/v1/rpc/fn_user_login",
                    new StringContent(jsonContent, Encoding.UTF8, "application/json")
                );

                var content = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Login API Response Status: {response.StatusCode}");
                Debug.WriteLine($"Response content: {content}");

                if (response.IsSuccessStatusCode)
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };

                    var loginResults = JsonSerializer.Deserialize<List<LoginResult>>(content, options);
                    Debug.WriteLine($"Deserialized results: {JsonSerializer.Serialize(loginResults, options)}");

                    var result = loginResults?.FirstOrDefault();
                    if (result != null)
                    {
                        Debug.WriteLine($"Login status: {result.LoginStatus}");
                        Debug.WriteLine($"User ID: {result.Id}");

                        if (result.LoginStatus == 0 && result.Id != null)
                        {
                            // Explicitly update last_login_at if needed
                            await UpdateLastLoginAsync(result.Id.Value);

                            Debug.WriteLine("Login successful");
                            return (true, new UserData
                            {
                                UserId = result.Id.Value,
                                Username = result.Username,
                                Email = result.Email,
                                FullName = result.FullName
                            }, null);
                        }
                    }

                    Debug.WriteLine("Login failed - invalid credentials");
                    return (false, null, "Invalid username or password");
                }

                Debug.WriteLine($"Login failed - HTTP {response.StatusCode}");
                return (false, null, $"Login failed: {content}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Login Error: {ex}");
                return (false, null, $"Database error: {ex.Message}");
            }
        }

        private async Task UpdateLastLoginAsync(Guid userId)
        {
            try
            {
                var updatePayload = new { last_login_at = DateTime.UtcNow };
                var jsonContent = JsonSerializer.Serialize(updatePayload);

                var response = await _httpClient.PatchAsync(
                    $"{_supabaseUrl}/rest/v1/users?id=eq.{userId}",
                    new StringContent(jsonContent, Encoding.UTF8, "application/json")
                );

                Debug.WriteLine($"Update last_login_at response: {response.StatusCode}");
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"Failed to update last_login_at: {errorContent}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating last_login_at: {ex.Message}");
            }
        }

        public async Task<(bool success, string error)> CreateUserAsync(string username, string password, string email, string fullName)
        {
            try
            {
                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);

                var checkResponse = await _httpClient.PostAsync(
                    $"{_supabaseUrl}/rest/v1/rpc/fn_check_user_exists",
                    new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            p_username = username,
                            p_email = email
                        }),
                        Encoding.UTF8,
                        "application/json"
                    )
                );

                if (checkResponse.IsSuccessStatusCode)
                {
                    var content = await checkResponse.Content.ReadAsStringAsync();
                    var userExists = JsonSerializer.Deserialize<bool>(content);

                    if (userExists)
                    {
                        return (false, "Username or email already exists");
                    }
                }
                else
                {
                    var errorContent = await checkResponse.Content.ReadAsStringAsync();
                    return (false, $"Duplicate check failed: {errorContent}");
                }

                var newUser = new
                {
                    username = username,
                    password_hash = hashedPassword,
                    email = email,
                    full_name = fullName ?? string.Empty
                };

                var response = await _httpClient.PostAsync(
                    $"{_supabaseUrl}/rest/v1/users",
                    new StringContent(
                        JsonSerializer.Serialize(newUser),
                        Encoding.UTF8,
                        "application/json"
                    )
                );

                if (response.IsSuccessStatusCode)
                {
                    return (true, null);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return (false, $"Failed to create user: {errorContent}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Database error: {ex.Message}");
            }
        }

        public async Task<List<OutputFile>> GetRecentFilesAsync(int userId, int count = 4)
        {
            var files = new List<OutputFile>();
            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_supabaseUrl}/rest/v1/output_files?select=file_id,file_name,file_path,file_size,created_date,history!inner(user_id)&history.user_id=eq.{userId}&order=created_date.desc&limit={count}"
                );

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var fileData = JsonSerializer.Deserialize<OutputFileResponse[]>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (fileData != null)
                    {
                        foreach (var file in fileData)
                        {
                            files.Add(new OutputFile
                            {
                                FileId = file.FileId,
                                FileName = file.FileName,
                                FilePath = file.FilePath,
                                FileSize = file.FileSize,
                                CreatedDate = file.CreatedDate
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching recent files: {ex.Message}");
            }
            return files;
        }

        public async Task<List<Folder>> GetUserFoldersAsync(int userId, int count = 4)
        {
            var folders = new List<Folder>();
            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_supabaseUrl}/rest/v1/folders?user_id=eq.{userId}&select=folder_id,folder_name,created_date&order=last_modified_date.desc&limit={count}"
                );

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var folderData = JsonSerializer.Deserialize<FolderResponse[]>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (folderData != null)
                    {
                        foreach (var folder in folderData)
                        {
                            folders.Add(new Folder
                            {
                                FolderId = folder.FolderId,
                                FolderName = folder.FolderName,
                                CreatedDate = folder.CreatedDate
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching folders: {ex.Message}");
            }
            return folders;
        }

        public async Task<List<ChartData>> GetFileTypeStatsAsync(int userId)
        {
            var stats = new List<ChartData>();
            try
            {
                var response = await _httpClient.PostAsync(
                    $"{_supabaseUrl}/rest/v1/rpc/fn_get_input_file_type_stats",
                    new StringContent(JsonSerializer.Serialize(new { p_user_id = userId }), Encoding.UTF8, "application/json")
                );

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var chartData = JsonSerializer.Deserialize<FileTypeStatsResponse[]>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (chartData != null)
                    {
                        foreach (var item in chartData)
                        {
                            stats.Add(new ChartData
                            {
                                Label = item.FileType,
                                Value = (int)item.UsageCount
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching file type stats: {ex.Message}");
            }
            return stats;
        }

        public async Task<int> LogHistoryAsync(int userId, int inputFileTypeId, int outputFormatId, string prompt, string processType)
        {
            Debug.WriteLine($"[LogHistoryAsync] Starting history logging for UserID: {userId}");

            try
            {
                var historyData = new
                {
                    user_id = userId,
                    input_file_type = inputFileTypeId,
                    output_format_id = outputFormatId,
                    processing_time = 0
                };

                var response = await _httpClient.PostAsync(
                    $"{_supabaseUrl}/rest/v1/history",
                    new StringContent(JsonSerializer.Serialize(historyData), Encoding.UTF8, "application/json")
                );

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var historyResult = JsonSerializer.Deserialize<HistoryResponse[]>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (historyResult != null && historyResult.Length > 0)
                    {
                        int historyId = historyResult[0].HistoryId;
                        Debug.WriteLine($"[LogHistoryAsync] Successfully logged history. HistoryID: {historyId}");
                        return historyId;
                    }
                }

                Debug.WriteLine($"[LogHistoryAsync] Failed to log history: {response.StatusCode}");
                return -1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LogHistoryAsync] Error: {ex.Message}");
                return -1;
            }
        }

        public async Task UpdateHistoryProcessingTimeAsync(int historyId, int processingTimeMs)
        {
            try
            {
                var updateData = new { processing_time = processingTimeMs };

                var response = await _httpClient.PatchAsync(
                    $"{_supabaseUrl}/rest/v1/history?history_id=eq.{historyId}",
                    new StringContent(JsonSerializer.Serialize(updateData), Encoding.UTF8, "application/json")
                );

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"Failed to update processing time: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating history processing time: {ex.Message}");
            }
        }

        public async Task LogOutputFileAsync(int historyId, string fileName, string filePath, long fileSize)
        {
            try
            {
                var fileData = new
                {
                    history_id = historyId,
                    file_name = fileName,
                    file_path = filePath,
                    file_size = fileSize
                };

                var response = await _httpClient.PostAsync(
                    $"{_supabaseUrl}/rest/v1/output_files",
                    new StringContent(JsonSerializer.Serialize(fileData), Encoding.UTF8, "application/json")
                );

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"Failed to log output file: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error logging output file: {ex.Message}");
            }
        }

        public async Task<int> GetFileTypeId(string typeName)
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_supabaseUrl}/rest/v1/file_types?type_name=eq.{typeName}&select=file_type_id"
                );

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var fileTypes = JsonSerializer.Deserialize<FileTypeResponse[]>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (fileTypes != null && fileTypes.Length > 0)
                    {
                        return fileTypes[0].FileTypeId;
                    }
                }

                return await GetFileTypeId("PDF");
            }
            catch
            {
                return 1;
            }
        }

        public async Task<int> GetOutputFormatId(string formatName)
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_supabaseUrl}/rest/v1/output_formats?format_name=eq.{formatName}&select=output_format_id"
                );

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var formats = JsonSerializer.Deserialize<OutputFormatResponse[]>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (formats != null && formats.Length > 0)
                    {
                        return formats[0].OutputFormatId;
                    }
                }

                return 1;
            }
            catch
            {
                return 1;
            }
        }

        public async Task<List<HistoryItem>> GetRecentHistoryAsync(int userId, int count)
        {
            var historyList = new List<HistoryItem>();

            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_supabaseUrl}/rest/v1/history?user_id=eq.{userId}&select=history_id,process_date,processing_time,file_types!inner(type_name),output_formats!inner(format_name)&order=process_date.desc&limit={count}"
                );

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var historyData = JsonSerializer.Deserialize<HistoryItemResponse[]>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (historyData != null)
                    {
                        foreach (var item in historyData)
                        {
                            historyList.Add(new HistoryItem
                            {
                                HistoryId = item.HistoryId,
                                InputType = item.FileTypes?.TypeName ?? "Unknown",
                                OutputFormat = item.OutputFormats?.FormatName ?? "Unknown",
                                ProcessDate = item.ProcessDate,
                                ProcessingTime = item.ProcessingTime ?? 0,
                                IsSuccess = true,
                                ProcessType = "Processing"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetRecentHistoryAsync: {ex.Message}");
                throw;
            }

            return historyList;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class LoginResult
    {
        public Guid? Id { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string FullName { get; set; }
        public int LoginStatus { get; set; }
    }

    public class UserData
    {
        public Guid UserId { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string FullName { get; set; }
    }

    public class OutputFileResponse
    {
        public int FileId { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class FolderResponse
    {
        public int FolderId { get; set; }
        public string FolderName { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class FileTypeStatsResponse
    {
        public string FileType { get; set; }
        public long UsageCount { get; set; }
    }

    public class HistoryResponse
    {
        public int HistoryId { get; set; }
    }

    public class FileTypeResponse
    {
        public int FileTypeId { get; set; }
    }

    public class OutputFormatResponse
    {
        public int OutputFormatId { get; set; }
    }

    public class HistoryItemResponse
    {
        public int HistoryId { get; set; }
        public DateTime ProcessDate { get; set; }
        public int? ProcessingTime { get; set; }
        public FileTypeInfo FileTypes { get; set; }
        public OutputFormatInfo OutputFormats { get; set; }
    }

    public class FileTypeInfo
    {
        public string TypeName { get; set; }
    }

    public class OutputFormatInfo
    {
        public string FormatName { get; set; }
    }

    public class OutputFile
    {
        public int FileId { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class HistoryItem
    {
        public int HistoryId { get; set; }
        public string InputType { get; set; }
        public string OutputFormat { get; set; }
        public DateTime ProcessDate { get; set; }
        public int ProcessingTime { get; set; }
        public bool IsSuccess { get; set; }
        public string ProcessType { get; set; }
    }

    public class Folder
    {
        public int FolderId { get; set; }
        public string FolderName { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class ChartData
    {
        public string Label { get; set; }
        public int Value { get; set; }
    }

    public class SavedFile
    {
        public Guid Id { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
