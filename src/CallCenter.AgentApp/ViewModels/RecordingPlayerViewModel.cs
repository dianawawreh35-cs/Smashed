using System.Windows.Threading;
using CallCenter.AgentApp.Audio;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Calls;
using CallCenter.AgentApp.Services.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace CallCenter.AgentApp.ViewModels;

/// <summary>
/// Plays the recording of a call opened from the call log: play, pause and seek
/// (A-51).
/// </summary>
/// <remarks>
/// <b>Whose recording it may be is the server's decision</b> (A-52). The call
/// log only ever holds the agent's own calls, and the server refuses anything
/// else with <c>not_your_call</c>; nothing here checks it a second time.
///
/// <b>A live call always wins.</b> The recording plays through the same headset
/// the customer is heard on, so a call ringing in pauses it, and it cannot be
/// started again until the phone is idle. An agent who cannot hear a caller
/// because last week's call is playing over them is worse than an agent who
/// has to press Play twice.
/// </remarks>
public sealed partial class RecordingPlayerViewModel : ObservableObject, IDisposable
{
    /// <summary>How often the position under the seek bar moves while playing.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(200);

    private readonly ApiClient _api;
    private readonly CallService _calls;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger<RecordingPlayerViewModel> _logger;
    private readonly DispatcherTimer _timer;

    private CancellationTokenSource? _loading;
    private MuLawPlaybackStream? _stream;
    private WaveOut? _output;

    /// <summary>
    /// Set while the timer moves the seek bar, so following the playback is not
    /// mistaken for the agent dragging it.
    /// </summary>
    private bool _following;

    public RecordingPlayerViewModel(
        ApiClient api,
        CallService calls,
        Localizer localizer,
        Dispatcher dispatcher,
        ILogger<RecordingPlayerViewModel> logger)
    {
        _api = api;
        _calls = calls;
        _dispatcher = dispatcher;
        _logger = logger;
        Localizer = localizer;

        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = Tick };
        _timer.Tick += (_, _) => FollowPlayback();

        _isOnCall = calls.State.Status != CallStatus.Idle;
        calls.StateChanged += OnCallStateChanged;

        localizer.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Message));
            OnPropertyChanged(nameof(PlayPauseLabel));
            OnPropertyChanged(nameof(HoldsText));
        };
    }

    /// <summary>Why there is nothing to play, when there is not.</summary>
    public enum PlaybackProblem
    {
        None,
        NeverRecorded,
        Expired,
        NotYours,
        Offline,
        Unreadable,
        NoOutputDevice,
    }

    public Localizer Localizer { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Message), nameof(HasMessage))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Message), nameof(HasMessage))]
    private PlaybackProblem _problem;

    /// <summary>The recording is in memory and can be played.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayPauseCommand))]
    [NotifyPropertyChangedFor(nameof(Message), nameof(HasMessage))]
    private bool _isPlayable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseLabel))]
    private bool _isPlaying;

    /// <summary>A call is ringing, being dialled or connected.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayPauseCommand))]
    [NotifyPropertyChangedFor(nameof(Message), nameof(HasMessage))]
    private bool _isOnCall;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    private double _durationSeconds;

    /// <summary>Where playback is, in seconds. Bound both ways to the seek bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText), nameof(IsAtHold))]
    private double _positionSeconds;

    /// <summary>
    /// When the call was on hold, read from the recording (A-51). Empty for a
    /// call never held, and for recordings made before holds were marked.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHolds), nameof(HoldsText), nameof(IsAtHold))]
    private IReadOnlyList<HoldPeriod> _holds = [];

    public bool HasHolds => Holds.Count > 0;

    /// <summary>"On hold: 1:10–1:52, 2:30–2:45" — the silences that are holds.</summary>
    public string HoldsText => HasHolds
        ? $"{Localizer["callLog.recording.onHold"]} "
          + string.Join(", ", Holds.Select(h => $"{Format(h.Start.TotalSeconds)}–{Format(h.End.TotalSeconds)}"))
        : string.Empty;

    /// <summary>
    /// Playback is inside a hold, so the silence being heard is the hold and
    /// not a fault.
    /// </summary>
    public bool IsAtHold => Holds.Any(h =>
        PositionSeconds >= h.Start.TotalSeconds && PositionSeconds < h.End.TotalSeconds);

    public string PlayPauseLabel => IsPlaying
        ? Localizer["callLog.recording.pause"]
        : Localizer["callLog.recording.play"];

    /// <summary>"0:42 / 3:10" — where it is, and how long the call was.</summary>
    public string PositionText => $"{Format(PositionSeconds)} / {Format(DurationSeconds)}";

    public bool HasMessage => Message is not null;

    /// <summary>
    /// What the agent is told instead of a player. <b>Expired and never
    /// recorded are different sentences</b> (A-33): one says the system did its
    /// job and time passed, the other that the call had no audio at all.
    /// </summary>
    public string? Message => IsLoading
        ? Localizer["callLog.recording.loading"]
        : Problem switch
        {
            PlaybackProblem.NeverRecorded => Localizer["callLog.recording.none"],
            PlaybackProblem.Expired => Localizer["callLog.recording.expired"],
            PlaybackProblem.NotYours => Localizer["callLog.recording.notYours"],
            PlaybackProblem.Offline => Localizer["callLog.recording.offline"],
            PlaybackProblem.Unreadable => Localizer["callLog.recording.unreadable"],
            PlaybackProblem.NoOutputDevice => Localizer["callLog.recording.noDevice"],
            _ => IsPlayable && IsOnCall ? Localizer["callLog.recording.onCall"] : null,
        };

    /// <summary>
    /// Fetches the recording of one call and gets it ready to play. Replaces
    /// whatever was loaded before.
    /// </summary>
    /// <remarks>
    /// Only asked for when the list says there is audio. A call the list marks
    /// as never recorded or expired is not worth a round trip to be told so.
    /// </remarks>
    public async Task LoadAsync(CallRow call)
    {
        Close();

        // A call nobody answered was never recorded (A-30), so the card says
        // nothing about audio at all rather than "no recording".
        if (!call.CanHaveRecording)
        {
            return;
        }

        if (!call.HasRecording)
        {
            Problem = call.RecordingExpired ? PlaybackProblem.Expired : PlaybackProblem.NeverRecorded;
            return;
        }

        var loading = new CancellationTokenSource();
        var token = loading.Token;
        _loading = loading;

        IsLoading = true;

        try
        {
            var result = await _api.GetRecordingAsync(call.Id, token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsOk || result.Value is null)
            {
                Problem = result.ErrorCode switch
                {
                    // The list said there was audio and the server says it has
                    // gone: retention ran in between, or the file went missing.
                    "recording_expired" => PlaybackProblem.Expired,
                    "recording_not_found" => PlaybackProblem.NeverRecorded,
                    "not_your_call" => PlaybackProblem.NotYours,
                    _ => PlaybackProblem.Offline,
                };

                return;
            }

            if (RecordingWav.Parse(result.Value) is not { } wav)
            {
                _logger.LogWarning(
                    "The recording of call {CommunicationId} is not a mu-law WAV this app can play", call.Id);

                Problem = PlaybackProblem.Unreadable;
                return;
            }

            _stream = new MuLawPlaybackStream(result.Value, wav);
            DurationSeconds = wav.Duration.TotalSeconds;
            Holds = wav.Holds;
            MoveTo(0);
            IsPlayable = true;
        }
        catch (OperationCanceledException)
        {
            // Closed, or another call opened, before it arrived.
        }
        finally
        {
            if (ReferenceEquals(_loading, loading))
            {
                _loading = null;
                IsLoading = false;
            }

            loading.Dispose();
        }
    }

    private bool CanPlayPause => IsPlayable && (IsPlaying || !IsOnCall);

    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private void PlayPause()
    {
        if (_stream is null)
        {
            return;
        }

        if (IsPlaying)
        {
            Pause();
            return;
        }

        // Played to the end: Play starts it again from the top.
        if (_stream.Position >= _stream.Length)
        {
            _stream.Position = 0;
        }

        try
        {
            if (_output is null)
            {
                // The Windows default output, which is where call audio goes too,
                // so the recording is heard in the same headset as the call.
                _output = new WaveOut();
                _output.PlaybackStopped += OnPlaybackStopped;
                _output.Init(_stream);
            }

            _output.Play();
        }
        catch (Exception ex)
        {
            // No output device, or one that went away. A message, not a crash.
            _logger.LogWarning(ex, "The recording could not be played on this laptop's output device");

            ReleaseOutput();
            Problem = PlaybackProblem.NoOutputDevice;
            return;
        }

        IsPlaying = true;
        _timer.Start();
    }

    /// <summary>
    /// The agent moved the seek bar. The timer moving it to follow the playback
    /// comes through here too, and is ignored.
    /// </summary>
    partial void OnPositionSecondsChanged(double value)
    {
        if (!_following && _stream is not null)
        {
            _stream.CurrentTime = TimeSpan.FromSeconds(value);
        }
    }

    /// <summary>Stops, and lets go of the audio and the device.</summary>
    public void Close()
    {
        try
        {
            _loading?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _timer.Stop();
        ReleaseOutput();

        _stream?.Dispose();
        _stream = null;

        IsPlaying = false;
        IsPlayable = false;
        Problem = PlaybackProblem.None;
        DurationSeconds = 0;
        Holds = [];
        MoveTo(0);
    }

    /// <summary>Stops without forgetting where it was: Play carries on from there.</summary>
    public void Pause()
    {
        _timer.Stop();
        _output?.Pause();
        IsPlaying = false;
        FollowPlayback();
    }

    public void Dispose()
    {
        _calls.StateChanged -= OnCallStateChanged;
        Close();
    }

    private void OnCallStateChanged(object? sender, CallState state)
    {
        // Raised on a SIP thread.
        _dispatcher.BeginInvoke(() =>
        {
            IsOnCall = state.Status != CallStatus.Idle;

            if (IsOnCall && IsPlaying)
            {
                Pause();
            }
        });
    }

    /// <summary>
    /// Raised when the recording plays to its end, or when the device fails
    /// part-way. Either way it has stopped, and the button should say Play.
    /// </summary>
    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (e.Exception is not null)
            {
                _logger.LogWarning(e.Exception, "Playback of a recording stopped with an error");
            }

            _timer.Stop();
            IsPlaying = false;
            FollowPlayback();
        });
    }

    private void FollowPlayback()
    {
        if (_stream is not null)
        {
            MoveTo(_stream.CurrentTime.TotalSeconds);
        }
    }

    private void MoveTo(double seconds)
    {
        _following = true;

        try
        {
            PositionSeconds = seconds;
        }
        finally
        {
            _following = false;
        }
    }

    private void ReleaseOutput()
    {
        if (_output is null)
        {
            return;
        }

        // Unhooked first: stopping raises PlaybackStopped, and this is not the
        // recording ending.
        _output.PlaybackStopped -= OnPlaybackStopped;
        _output.Stop();
        _output.Dispose();
        _output = null;
    }

    private static string Format(double seconds)
    {
        var whole = (int)Math.Max(0, seconds);
        return $"{whole / 60}:{whole % 60:D2}";
    }
}
