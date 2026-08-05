using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Isolith.Core;

namespace Isolith.Sync;

/// <summary>How far the sync module has got.</summary>
public enum SyncState
{
    /// <summary>No credentials entered. The game is fully playable in this state.</summary>
    SignedOut,

    Connecting,
    SignedIn,
    Working,

    /// <summary>The last operation failed; see <see cref="SyncService.LastError"/>.</summary>
    Failed,
}

/// <summary>
/// Optional AT Protocol sync for run statistics, backed by the native
/// <c>libwolfram</c> SDK.
/// </summary>
/// <remarks>
/// <b>This is a side feature.</b> Isolith is a single-player platformer that
/// records everything it needs locally through <see cref="RunHistory"/>. Sync
/// copies completed runs into the player's own repo so their stats live
/// somewhere they control and can be read by other tools — nothing here gates
/// gameplay, and every failure is non-fatal.
///
/// All SDK calls block on network I/O, so each is dispatched to the thread pool
/// and its result marshalled back to the main thread through a deferred
/// <see cref="Callable"/>. Nothing in this class touches the scene tree, or
/// raises an event, off the main thread.
/// </remarks>
[GlobalClass]
public partial class SyncService : Node
{
    /// <summary>Default entryway, used when the player doesn't name their own PDS.</summary>
    public const string DefaultService = "https://bsky.social";

    /// <summary>Raised on the main thread whenever <see cref="State"/> changes.</summary>
    public event Action<SyncState>? StateChanged;

    /// <summary>Raised on the main thread when a run has been written to the repo.</summary>
    public event Action<RecordRef>? RunPublished;

    /// <summary>Raised on the main thread when runs have been fetched from the repo.</summary>
    public event Action<List<RunStats>>? RunsFetched;

    public SyncState State { get; private set; } = SyncState.SignedOut;

    /// <summary>Message from the most recent failure, for display in the UI.</summary>
    public string LastError { get; private set; } = string.Empty;

    /// <summary>The signed-in handle, once known.</summary>
    public string Handle { get; private set; } = string.Empty;

    /// <summary>The signed-in DID, once known.</summary>
    public string Did { get; private set; } = string.Empty;

    public bool IsSignedIn => State is SyncState.SignedIn or SyncState.Working;

    /// <summary>
    /// Guards the agent field itself. The agent serialises its own native calls;
    /// this only stops two operations racing to create or replace it.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WolframAgent? _agent;

    public override void _ExitTree()
    {
        _agent?.Dispose();
        _agent = null;
        _gate.Dispose();
    }

    // -----------------------------------------------------------------------
    // Operations
    // -----------------------------------------------------------------------

    /// <summary>
    /// Signs in with an app password.
    /// </summary>
    /// <param name="identifier">A handle, DID, or email.</param>
    /// <param name="appPassword">
    /// An app password, not the account password. App passwords are issued and
    /// revoked per-application, which is the right credential for a game to hold.
    /// It is used for this one call and never written to disk.
    /// </param>
    /// <param name="serviceUrl">PDS or entryway base URL.</param>
    public void SignIn(string identifier, string appPassword, string serviceUrl = DefaultService)
    {
        if (State is SyncState.Connecting or SyncState.Working)
            return;

        string service = string.IsNullOrWhiteSpace(serviceUrl) ? DefaultService : serviceUrl.Trim();
        SetState(SyncState.Connecting);

        Run(() =>
        {
            var agent = new WolframAgent(service);

            try
            {
                agent.Login(identifier.Trim(), appPassword);
            }
            catch
            {
                // A failed login never becomes _agent, so it must be disposed
                // here or it leaks — nothing else will ever hold a reference.
                agent.Dispose();
                throw;
            }

            WolframAgent? previous = _agent;
            _agent = agent;
            previous?.Dispose();

            string handle = agent.Handle ?? identifier.Trim();
            string did = agent.Did ?? string.Empty;

            // Callable.From keeps the hop type-safe and captures the values
            // now, rather than relying on name lookup and shared fields.
            Callable.From(() => OnSignedIn(handle, did)).CallDeferred();
        });
    }

    /// <summary>Ends the session and clears the cached identity.</summary>
    public void SignOut()
    {
        if (!IsSignedIn)
            return;

        SetState(SyncState.Working);

        Run(() =>
        {
            try
            {
                _agent?.Logout();
            }
            catch (WolframException ex)
            {
                // Best-effort: the agent is disposed below regardless, so the
                // local session ends either way. A failed server-side logout
                // is worth a warning, not a state stuck at Working/Failed.
                GD.PushWarning($"Isolith sync: logout request failed ({ex.Message}); signing out locally anyway.");
            }
            finally
            {
                _agent?.Dispose();
                _agent = null;
            }

            Callable.From(OnSignedOut).CallDeferred();
        });
    }

    /// <summary>
    /// Writes a completed run to the player's repo. Does nothing when signed
    /// out, which is the normal case.
    /// </summary>
    public void PublishRun(RunStats run)
    {
        if (_agent is null || !IsSignedIn)
            return;

        SetState(SyncState.Working);
        string json = RunRecord.ToJson(run);

        Run(() =>
        {
            RecordRef reference = _agent!.CreateRecord(RunRecord.Collection, json);
            Callable.From(() => OnRunPublished(reference)).CallDeferred();
        });
    }

    /// <summary>Fetches previously synced runs from the player's own repo.</summary>
    public void FetchRuns(int limit = 50)
    {
        if (_agent is null || !IsSignedIn)
            return;

        SetState(SyncState.Working);

        Run(() =>
        {
            string body = _agent!.ListRecords(RunRecord.Collection, limit);
            List<RunStats> runs = RunRecord.ParseListRecords(body);

            Callable.From(() => OnRunsFetched(runs)).CallDeferred();
        });
    }

    // -----------------------------------------------------------------------
    // Thread-pool plumbing
    // -----------------------------------------------------------------------

    /// <summary>
    /// Runs a blocking SDK operation off the main thread, funnelling any failure
    /// into <see cref="SyncState.Failed"/> rather than letting it escape.
    /// </summary>
    private void Run(Action operation)
    {
        _ = Task.Run(async () =>
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                operation();
            }
            catch (WolframException ex)
            {
                Callable.From(() => OnFailed(ex.Message)).CallDeferred();
            }
            catch (Exception ex)
            {
                // Anything else is a bug in the wrapper rather than a protocol
                // error, but it still must not take the game down.
                string message = $"Unexpected sync failure: {ex.Message}";
                Callable.From(() => OnFailed(message)).CallDeferred();
            }
            finally
            {
                _gate.Release();
            }
        });
    }

    // -----------------------------------------------------------------------
    // Main-thread callbacks (invoked via CallDeferred)
    // -----------------------------------------------------------------------

    private void OnSignedIn(string handle, string did)
    {
        Handle = handle;
        Did = did;
        LastError = string.Empty;
        SetState(SyncState.SignedIn);
    }

    private void OnSignedOut()
    {
        Handle = string.Empty;
        Did = string.Empty;
        LastError = string.Empty;
        SetState(SyncState.SignedOut);
    }

    private void OnRunPublished(RecordRef reference)
    {
        SetState(SyncState.SignedIn);
        RunPublished?.Invoke(reference);
    }

    private void OnRunsFetched(List<RunStats> runs)
    {
        SetState(SyncState.SignedIn);
        RunsFetched?.Invoke(runs);
    }

    private void OnFailed(string message)
    {
        LastError = message;
        GD.PushWarning($"Isolith sync: {message}");

        // A failure after a good sign-in leaves the session usable, so fall
        // back to signed-in rather than dropping the player out entirely.
        SetState(_agent is not null && !string.IsNullOrEmpty(Did) ? SyncState.SignedIn : SyncState.Failed);
    }

    private void SetState(SyncState state)
    {
        if (State == state)
            return;

        State = state;
        StateChanged?.Invoke(state);
    }
}
