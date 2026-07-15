using Microsoft.Extensions.Logging;

namespace LhmExporter;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxFileBytes = 10 * 1024 * 1024;
    private const int MaxArchiveFiles = 4;

    private readonly string _filePath;
    private readonly object _lock = new();
    private StreamWriter _writer;

    public FileLoggerProvider(string filePath)
    {
        _filePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        _writer = OpenWriter(_filePath);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(() => _writer, _lock, categoryName, RotateIfNeeded);

    public void Dispose()
    {
        lock (_lock)
        {
            _writer.Dispose();
        }
    }

    private void RotateIfNeeded()
    {
        lock (_lock)
        {
            _writer.Flush();
            var info = new FileInfo(_filePath);
            if (!info.Exists || info.Length < MaxFileBytes)
                return;

            _writer.Dispose();

            for (var i = MaxArchiveFiles; i >= 1; i--)
            {
                var source = i == 1 ? _filePath : $"{_filePath}.{i - 1}";
                var target = $"{_filePath}.{i}";
                if (File.Exists(source))
                {
                    if (File.Exists(target))
                        File.Delete(target);
                    File.Move(source, target);
                }
            }

            _writer = OpenWriter(_filePath);
        }
    }

    private static StreamWriter OpenWriter(string filePath) =>
        new(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
        {
            AutoFlush = true,
        };

    private sealed class FileLogger(
        Func<StreamWriter> writerAccessor,
        object lockObj,
        string category,
        Action rotateIfNeeded) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            rotateIfNeeded();

            var msg = formatter(state, exception);
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{logLevel}] {category}: {msg}";
            if (exception != null)
                line += $" | {exception}";

            lock (lockObj)
            {
                writerAccessor().WriteLine(line);
            }
        }
    }
}
