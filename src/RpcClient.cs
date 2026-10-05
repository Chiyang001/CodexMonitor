using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodexMonitor
{
    public sealed class RpcClient : IDisposable
    {
        Process process;
        readonly ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>> pending = new ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>>();
        readonly object writeLock = new object();
        int sequence;
        bool disposed;
        public async Task ConnectAsync(string executable)
        {
            process = new Process { StartInfo = new ProcessStartInfo(executable, "app-server --listen stdio://") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            } };
            // Drain stderr without persisting diagnostics, which can contain private account data.
            process.ErrorDataReceived += delegate { };
            process.Start();
            process.BeginErrorReadLine();
            Task reader = ReadLoopAsync();
            await RequestAsync("initialize", new { clientInfo = new { name = "codex_quota_monitor", title = "Codex Quota Monitor", version = "1.0.0" } });
            Send(new { method = "initialized", @params = new { } });
        }
        async Task ReadLoopAsync()
        {
            try
            {
                string line;
                while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                {
                    Dictionary<string, object> message;
                    try { message = Json.Serializer.Deserialize<Dictionary<string, object>>(line); } catch { continue; }
                    object idValue;
                    int id;
                    if (!message.TryGetValue("id", out idValue) || !int.TryParse(Convert.ToString(idValue), out id)) continue;
                    TaskCompletionSource<Dictionary<string, object>> completion;
                    if (!pending.TryRemove(id, out completion)) continue;
                    var error = Json.Object(message, "error");
                    if (error != null) completion.TrySetException(new InvalidOperationException("Codex 额度接口暂不可用，请检查登录状态"));
                    else completion.TrySetResult(Json.Object(message, "result"));
                }
            }
            catch { }
            finally { FailPending(); }
        }
        public async Task<Dictionary<string, object>> RequestAsync(string method, object parameters)
        {
            if (disposed) throw new ObjectDisposedException("RpcClient");
            int id = Interlocked.Increment(ref sequence);
            var completion = new TaskCompletionSource<Dictionary<string, object>>();
            pending[id] = completion;
            try
            {
                Send(new { id = id, method = method, @params = parameters });
                if (await Task.WhenAny(completion.Task, Task.Delay(15000)) != completion.Task)
                    throw new TimeoutException("连接超时，请检查网络后刷新");
                return await completion.Task;
            }
            finally { TaskCompletionSource<Dictionary<string, object>> ignored; pending.TryRemove(id, out ignored); }
        }
        void Send(object message)
        {
            lock (writeLock) { process.StandardInput.WriteLine(Json.Serializer.Serialize(message)); process.StandardInput.Flush(); }
        }
        void FailPending()
        {
            foreach (var pair in pending) pair.Value.TrySetException(new IOException("Codex 连接已断开"));
            pending.Clear();
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (process != null)
            {
                try { if (!process.HasExited) { process.StandardInput.Close(); if (!process.WaitForExit(500)) process.Kill(); } } catch { }
                process.Dispose();
            }
            FailPending();
        }
    }
    public static class CodexDiscovery
    {
        public static string Executable()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_MONITOR_CODEX_PATH");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
            if (Directory.Exists(root))
            {
                var files = Directory.GetFiles(root, "codex.exe", SearchOption.AllDirectories);
                Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
                if (files.Length > 0) return files[0];
            }
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string folder in path.Split(';'))
            {
                try { string candidate = Path.Combine(folder.Trim('"'), "codex.exe"); if (File.Exists(candidate)) return candidate; } catch { }
            }
            throw new FileNotFoundException("未找到 Codex，请先安装并启动 Codex 桌面应用");
        }
        public static bool IsDesktopPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string name = Path.GetFileName(path);
            return (name.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("Codex.exe", StringComparison.OrdinalIgnoreCase)) &&
                (path.IndexOf("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 path.IndexOf(@"\OpenAI\Codex\app\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 path.IndexOf(@"\Programs\Codex\", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        public static bool DesktopRunning()
        {
            foreach (string name in new[] { "ChatGPT", "Codex" })
                foreach (Process item in Process.GetProcessesByName(name))
                    using (item) { try { if (IsDesktopPath(item.MainModule.FileName)) return true; } catch { } }
            return false;
        }
        public static string AuthStamp()
        {
            string root = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            string auth = Path.Combine(root, "auth.json");
            try { return File.Exists(auth) ? File.GetLastWriteTimeUtc(auth).Ticks + ":" + new FileInfo(auth).Length : "missing"; }
            catch { return "unavailable"; }
        }
    }
}
