using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymTrackPro.Mobile.Services;
using GymTrackPro.Shared.DTOs;

namespace GymTrackPro.Mobile.ViewModels;

public partial class TrainerDashboardViewModel : ObservableObject
{
    private readonly IApiService _apiService;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial AssignedClientDto? SelectedClient { get; set; }

    [ObservableProperty]
    public partial string RoutineName { get; set; } = "Full Body Hypertrophy";

    [ObservableProperty]
    public partial string NewExerciseName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int NewExerciseSets { get; set; } = 4;

    [ObservableProperty]
    public partial int NewExerciseReps { get; set; } = 10;

    [ObservableProperty]
    public partial decimal NewExerciseWeight { get; set; } = 50.0m;

    public ObservableCollection<AssignedClientDto> Clients { get; } = new();
    public ObservableCollection<WorkoutExerciseValidationDto> DraftExercises { get; } = new();
    public ObservableCollection<WorkoutLogResponseDto> ClientLogs { get; } = new();

    public TrainerDashboardViewModel(IApiService apiService)
    {
        _apiService = apiService;
    }

    [RelayCommand]
    public async Task LoadClientsAsync()
    {
        IsLoading = true;
        StatusMessage = string.Empty;
        try
        {
            Clients.Clear();
            var response = await _apiService.GetTrainerClientsAsync();
            if (response.Success && response.Data != null)
            {
                foreach (var client in response.Data)
                {
                    Clients.Add(client);
                }
                if (Clients.Count > 0 && SelectedClient == null)
                {
                    await SelectClientAsync(Clients[0]);
                }
            }
            else
            {
                StatusMessage = response.Message ?? "Failed to load clients.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SelectClientAsync(AssignedClientDto client)
    {
        SelectedClient = client;
        ClientLogs.Clear();
        DraftExercises.Clear();
        StatusMessage = $"Selected client: {client.FullName}";

        try
        {
            var logsResponse = await _apiService.GetClientWorkoutLogsAsync(client.MemberID);
            if (logsResponse.Success && logsResponse.Data != null)
            {
                foreach (var log in logsResponse.Data)
                {
                    ClientLogs.Add(log);
                }
            }
        }
        catch
        {
            // Non-critical if logs fail to fetch
        }
    }

    [RelayCommand]
    public void AddExercise()
    {
        if (string.IsNullOrWhiteSpace(NewExerciseName))
        {
            StatusMessage = "Please enter exercise name.";
            return;
        }

        DraftExercises.Add(new WorkoutExerciseValidationDto
        {
            Name = NewExerciseName.Trim(),
            Sets = NewExerciseSets > 0 ? NewExerciseSets : 3,
            Reps = NewExerciseReps > 0 ? NewExerciseReps : 10,
            Weight = NewExerciseWeight >= 0 ? NewExerciseWeight : 0
        });

        NewExerciseName = string.Empty;
        StatusMessage = $"Added exercise to draft routine.";
    }

    [RelayCommand]
    public async Task SubmitRoutineAsync()
    {
        if (SelectedClient == null)
        {
            StatusMessage = "Please select a client first.";
            return;
        }

        if (DraftExercises.Count == 0)
        {
            StatusMessage = "Please add at least one exercise to the routine.";
            return;
        }

        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            var exercisesJson = System.Text.Json.JsonSerializer.Serialize(DraftExercises);
            var dto = new PostRoutineDto
            {
                MemberID = SelectedClient.MemberID,
                RoutineName = RoutineName,
                ExercisesJson = exercisesJson
            };

            var response = await _apiService.PostTrainerRoutineAsync(dto);
            if (response.Success)
            {
                StatusMessage = $"🎉 Routine '{RoutineName}' assigned successfully to {SelectedClient.FullName}!";
            }
            else
            {
                StatusMessage = response.Message ?? "Failed to assign routine.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
