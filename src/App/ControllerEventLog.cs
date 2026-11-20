using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;

sealed class ControllerEvent
{
    public readonly DateTime Time;
    public readonly string Message;

    internal ControllerEvent(DateTime time, string message)
    {
        Time = time;
        Message = message;
    }
}

sealed class ControllerEventLog
{
    const int Capacity = 1000;
    readonly string filePath;
    Queue<ControllerEvent> entries = new Queue<ControllerEvent>();
    bool needsLoad = true;
    bool needsSave;
    DateTime retryAfter;
    internal string StorageError { get; private set; }
    public event Action Changed;

    internal ControllerEventLog(string filePath = null)
    {
        this.filePath = filePath;
        if (filePath != null) SynchronizeStorage();
    }

    internal void Add(string message)
    {
        Append(entries, new ControllerEvent(DateTime.Now, message));
        if (filePath != null)
        {
            needsSave = true;
            if (StorageError == null) SynchronizeStorage();
        }
        NotifyChanged();
    }

    internal void Clear()
    {
        if (filePath != null)
        {
            try { File.Delete(filePath); }
            catch (DirectoryNotFoundException) { }
        }
        entries.Clear();
        needsLoad = needsSave = false;
        StorageError = null;
        NotifyChanged();
    }

    internal void Remove(ControllerEvent entry)
    {
        if (!entries.Contains(entry)) return;
        var remaining = new Queue<ControllerEvent>();
        foreach (var item in entries)
            if (!ReferenceEquals(item, entry)) remaining.Enqueue(item);
        if (filePath != null)
        {
            if (needsLoad) throw new IOException("Saved events are unavailable. Try again once they finish loading.");
            SaveEvents(remaining);
            needsSave = false;
            StorageError = null;
        }
        entries = remaining;
        NotifyChanged();
    }

    internal void RetryStorage()
    {
        if (StorageError == null || DateTime.UtcNow < retryAfter) return;
        bool wasLoading = needsLoad;
        string previousError = StorageError;
        SynchronizeStorage();
        if (wasLoading != needsLoad || previousError != StorageError) NotifyChanged();
    }

    internal ControllerEvent[] Snapshot()
    {
        var snapshot = entries.ToArray();
        Array.Reverse(snapshot);
        return snapshot;
    }

    static void Append(Queue<ControllerEvent> queue, ControllerEvent entry)
    {
        if (queue.Count == Capacity) queue.Dequeue();
        queue.Enqueue(entry);
    }

    void SynchronizeStorage()
    {
        try
        {
            if (needsLoad)
            {
                var restored = ReadSavedEvents();
                foreach (ControllerEvent entry in entries) Append(restored, entry);
                entries = restored;
                needsLoad = false;
            }
            if (needsSave)
            {
                SaveEvents(entries);
                needsSave = false;
            }
            StorageError = null;
        }
        catch (IOException) { ReportStorageError(); }
        catch (UnauthorizedAccessException) { ReportStorageError(); }
        catch (SecurityException) { ReportStorageError(); }
    }

    Queue<ControllerEvent> ReadSavedEvents()
    {
        var result = new Queue<ControllerEvent>();
        try
        {
            foreach (string line in File.ReadLines(filePath, Encoding.UTF8))
            {
                int separator = line.IndexOf('\t');
                DateTime time;
                if (separator > 0 && separator < line.Length - 1 &&
                    DateTime.TryParseExact(line.Substring(0, separator), "O", CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out time))
                    Append(result, new ControllerEvent(time, line.Substring(separator + 1)));
            }
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        return result;
    }

    void SaveEvents(IEnumerable<ControllerEvent> snapshot)
    {
        string directory = Path.GetDirectoryName(filePath);
        Directory.CreateDirectory(directory);
        string staged = Path.Combine(directory, "events-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var writer = new StreamWriter(staged, false, new UTF8Encoding(false)))
                foreach (ControllerEvent entry in snapshot)
                {
                    writer.Write(entry.Time.ToString("O", CultureInfo.InvariantCulture));
                    writer.Write('\t');
                    writer.WriteLine(entry.Message);
                }
            if (File.Exists(filePath)) File.Replace(staged, filePath, null);
            else File.Move(staged, filePath);
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }

    void ReportStorageError()
    {
        StorageError = needsLoad ? "Saved events could not be read." : "Recent events could not be saved.";
        retryAfter = DateTime.UtcNow.AddSeconds(10);
    }

    void NotifyChanged()
    {
        var changed = Changed;
        if (changed != null) changed();
    }
}
