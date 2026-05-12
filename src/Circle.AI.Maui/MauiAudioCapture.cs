// MauiAudioCapture.cs
//
// Real microphone capture implementation for MAUI host apps.
// Produces 16-bit, 16 kHz, mono PCM frames (~100 ms per chunk = 3200 bytes).
//
// Platform guards:
//   ANDROID        — AudioRecord via Android.Media
//   IOS / MACCATALYST — AVAudioEngine + AVAudioSession
//   WINDOWS        — Windows.Media.Capture.MediaCapture (WinRT)
//   net9.0 (headless) — throws PlatformNotSupportedException from StartAsync;
//                        CaptureAsync throws PlatformNotSupportedException.
//
// Register in MauiProgram.cs:
//   builder.Services.AddSingleton<IAudioCapture, MauiAudioCapture>();

using System.Runtime.CompilerServices;
using Circle.AI.Voice;

#if ANDROID
using Android.Media;
#elif IOS || MACCATALYST
using AVFoundation;
using Foundation;
#elif WINDOWS
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
#endif

namespace Circle.AI.Maui;

/// <summary>
/// Platform microphone capture backed by native APIs.
/// Produces 16-bit signed PCM, mono, 16 kHz audio in ~100 ms frames.
/// </summary>
/// <remarks>
/// On the headless <c>net9.0</c> TFM (used by the test project),
/// <see cref="StartAsync"/> and <see cref="CaptureAsync"/> throw
/// <see cref="PlatformNotSupportedException"/>. Use
/// <see cref="NullAudioCapture"/> in tests instead.
/// </remarks>
public sealed class MauiAudioCapture : IAudioCapture, IAsyncDisposable
{
    // 16 kHz × 1 channel × 2 bytes × 0.1 s = 3200 bytes per frame.
    private const int SampleRate   = 16_000;
    private const int Channels     = 1;
    private const int BitsPerSample = 16;
    private const int FrameMs      = 100;
    private const int FrameBytes   = SampleRate * Channels * (BitsPerSample / 8) * FrameMs / 1000;

    private bool _started;
    private bool _disposed;

#if ANDROID
    private AudioRecord? _recorder;
#elif IOS || MACCATALYST
    private AVAudioEngine? _engine;
    private AVAudioInputNode? _inputNode;
    // Temporary buffer accumulated from AVAudioEngine taps.
    private readonly System.Collections.Concurrent.BlockingCollection<byte[]> _iosQueue
        = new(boundedCapacity: 64);
#elif WINDOWS
    private MediaCapture? _mediaCapture;
    private InMemoryRandomAccessStream? _stream;
#endif

    /// <inheritdoc />
    public AudioFormat Format { get; } = AudioFormat.Pcm16Mono16k;

    /// <summary>
    /// Initialise the platform audio recording backend.
    /// Must be called before <see cref="CaptureAsync"/>.
    /// </summary>
    /// <param name="ct">Cancellation token used to abort initialisation.</param>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown on the headless <c>net9.0</c> TFM.
    /// </exception>
    public async Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;

#if ANDROID
        var minBuf = AudioRecord.GetMinBufferSize(
            SampleRate,
            ChannelIn.Mono,
            Android.Media.Encoding.Pcm16bit);
        var bufSize = Math.Max(minBuf, FrameBytes * 4);
        _recorder = new AudioRecord(
            AudioSource.Mic,
            SampleRate,
            ChannelIn.Mono,
            Android.Media.Encoding.Pcm16bit,
            bufSize);
        _recorder.StartRecording();
        _started = true;

#elif IOS || MACCATALYST
        var session = AVAudioSession.SharedInstance();
        session.SetCategory(AVAudioSessionCategory.Record);
        session.SetActive(true);

        _engine    = new AVAudioEngine();
        _inputNode = _engine.InputNode;

        // Tap at 16 kHz mono 16-bit.
        var fmt = new AVAudioFormat(SampleRate, 1);
        _inputNode.InstallTapOnBus(0, (uint)(FrameBytes * 4), fmt, (buf, _) =>
        {
            if (buf.FloatChannelData is null) return;
            var count = (int)buf.FrameLength;
            var pcm   = new byte[count * 2];
            unsafe
            {
                float* src = (float*)buf.FloatChannelData[0];
                fixed (byte* dst = pcm)
                {
                    short* s = (short*)dst;
                    for (int i = 0; i < count; i++)
                    {
                        float f = src[i];
                        if (f >  1f) f =  1f;
                        if (f < -1f) f = -1f;
                        s[i] = (short)(f * 32767f);
                    }
                }
            }
            _iosQueue.TryAdd(pcm);
        });

        _engine.Prepare();
        _engine.StartAndReturnError(out _);
        _started = true;
        await Task.CompletedTask.ConfigureAwait(false);

#elif WINDOWS
        _mediaCapture = new MediaCapture();
        var settings  = new MediaCaptureInitializationSettings
        {
            StreamingCaptureMode = StreamingCaptureMode.Audio,
        };
        await _mediaCapture.InitializeAsync(settings).AsTask(ct).ConfigureAwait(false);
        _stream = new InMemoryRandomAccessStream();
        _started = true;

#else
        await Task.CompletedTask.ConfigureAwait(false);
        throw new PlatformNotSupportedException(
            "MauiAudioCapture requires a MAUI platform target (Android, iOS, macCatalyst, or Windows). " +
            "Use NullAudioCapture in tests targeting net9.0.");
#endif
    }

    /// <summary>
    /// Stop the recording backend and release the microphone resource.
    /// </summary>
    /// <param name="ct">Cancellation token (not used; included for symmetry).</param>
    public Task StopAsync(CancellationToken ct = default)
    {
        if (_disposed || !_started) return Task.CompletedTask;

#if ANDROID
        _recorder?.Stop();
        _recorder?.Release();
        _recorder?.Dispose();
        _recorder = null;
#elif IOS || MACCATALYST
        _engine?.Stop();
        _inputNode?.RemoveTapOnBus(0);
        _iosQueue.CompleteAdding();
        _engine?.Dispose();
        _engine = null;
#elif WINDOWS
        _stream?.Dispose();
        _stream = null;
        _mediaCapture?.Dispose();
        _mediaCapture = null;
#endif
        _started = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown on the headless <c>net9.0</c> TFM or when <see cref="StartAsync"/>
    /// has not been called.
    /// </exception>
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> CaptureAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

#if ANDROID
        if (_recorder is null)
            throw new InvalidOperationException(
                "Call StartAsync before CaptureAsync.");

        var buffer = new byte[FrameBytes];
        while (!ct.IsCancellationRequested)
        {
            int read = _recorder.Read(buffer, 0, buffer.Length);
            if (read <= 0) break;
            var chunk = new byte[read];
            Buffer.BlockCopy(buffer, 0, chunk, 0, read);
            yield return chunk;
        }

#elif IOS || MACCATALYST
        if (_engine is null)
            throw new InvalidOperationException(
                "Call StartAsync before CaptureAsync.");

        while (!ct.IsCancellationRequested && !_iosQueue.IsCompleted)
        {
            if (_iosQueue.TryTake(out var chunk, millisecondsTimeout: 200))
            {
                yield return chunk;
            }
            else
            {
                await Task.Yield();
            }
        }

#elif WINDOWS
        if (_mediaCapture is null)
            throw new InvalidOperationException(
                "Call StartAsync before CaptureAsync.");

        // Windows MediaCapture reads frames into a shared stream; we poll it.
        var frameBytes  = FrameBytes;
        var buffer      = new byte[frameBytes];
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(FrameMs, ct).ConfigureAwait(false);
            if (_stream is null) break;

            var len = (uint)Math.Min(_stream.Size, (ulong)frameBytes);
            if (len == 0) continue;

            var ibuf = new Windows.Storage.Streams.Buffer(len);
            await _stream.ReadAsync(ibuf, len, InputStreamOptions.None).AsTask(ct).ConfigureAwait(false);
            DataReader.FromBuffer(ibuf).ReadBytes(buffer);
            var chunk = new byte[len];
            Buffer.BlockCopy(buffer, 0, chunk, 0, (int)len);
            yield return chunk;
        }

#else
        await Task.CompletedTask.ConfigureAwait(false);
        throw new PlatformNotSupportedException(
            "MauiAudioCapture.CaptureAsync is not supported on net9.0. " +
            "Use NullAudioCapture in headless / test targets.");
        yield break; // Unreachable — required by the compiler for async iterator method.
#endif
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync().ConfigureAwait(false);
    }
}
