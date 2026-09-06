// ZCodeWallpaper.exe — v3 纯 C# 全包版 (GUI + 守护 + CDP 注入)
// 编译: csc -nologo -target:winexe -out:ZCodeWallpaper.exe ZCodeWallpaper.cs
//       -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll
// 用法: 双击=打开面板(若已在运行则唤起其面板); --hidden=静默守护自启;
//       CLI: apply <路径> | clear | status | shot <out.png>
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Program
{
    internal const int Port = 9335;
    internal static string ExeDir = AppDomain.CurrentDomain.BaseDirectory;
    internal static string ExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.FriendlyName);
    internal static string DataDir = Path.Combine(ExeDir, "data");
    internal static Mutex InstanceMutex;
    internal static EventWaitHandle ShowGuiEvent;

    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);
    private const int ATTACH_PARENT_PROCESS = -1;

    [STAThread]
    private static void Main(string[] args)
    {
        Directory.CreateDirectory(DataDir);
        string verb = args.Length > 0 ? args[0] : "";
        // CLI 短命进程，不受单实例限制（要与常驻守护共存）。
        // 除 --hidden/--daemon 外的一切首参都进 CLI（未知命令会在 Cli.Run 里报错），防止参数异常时误入 GUI 分支静默退出。
        if (verb != "" && verb != "--hidden" && verb != "--daemon")
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
            try
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            }
            catch { }
            Cli.Run(args).Wait();
            return;
        }
        bool createdNew;
        InstanceMutex = new Mutex(true, "ZCodeWallpaperExe", out createdNew);
        ShowGuiEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "ZCodeWallpaperShowGui");
        if (!createdNew)
        {
            if (args.Length == 0) { ShowGuiEvent.Set(); } // 唤起已有实例的面板
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        bool hidden = verb == "--hidden" || verb == "--daemon";
        Application.Run(new MainForm(hidden));
    }
}

// ============================ MiniJson ============================
internal static class MiniJson
{
    public static object Parse(string s)
    {
        int i = 0;
        object v = ParseValue(s, ref i);
        return v;
    }
    private static object ParseValue(string s, ref int i)
    {
        SkipWs(s, ref i);
        if (i >= s.Length) return null;
        char c = s[i];
        if (c == '{') return ParseObj(s, ref i);
        if (c == '[') return ParseArr(s, ref i);
        if (c == '"') return ParseStr(s, ref i);
        if (c == 't') { i += 4; return true; }
        if (c == 'f') { i += 5; return false; }
        if (c == 'n') { i += 4; return null; }
        return ParseNum(s, ref i);
    }
    private static void SkipWs(string s, ref int i) { while (i < s.Length && " \t\r\n".IndexOf(s[i]) >= 0) i++; }
    private static Dictionary<string, object> ParseObj(string s, ref int i)
    {
        var d = new Dictionary<string, object>();
        i++; SkipWs(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return d; }
        while (true)
        {
            SkipWs(s, ref i);
            string k = ParseStr(s, ref i);
            SkipWs(s, ref i); i++; // :
            d[k] = ParseValue(s, ref i);
            SkipWs(s, ref i);
            if (s[i] == ',') { i++; continue; }
            i++; break;
        }
        return d;
    }
    private static List<object> ParseArr(string s, ref int i)
    {
        var l = new List<object>();
        i++; SkipWs(s, ref i);
        if (i < s.Length && s[i] == ']') { i++; return l; }
        while (true)
        {
            l.Add(ParseValue(s, ref i));
            SkipWs(s, ref i);
            if (s[i] == ',') { i++; continue; }
            i++; break;
        }
        return l;
    }
    private static string ParseStr(string s, ref int i)
    {
        var sb = new StringBuilder();
        i++;
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') break;
            if (c == '\\')
            {
                char e = s[i++];
                if (e == 'n') sb.Append('\n');
                else if (e == 't') sb.Append('\t');
                else if (e == 'r') sb.Append('\r');
                else if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; }
                else sb.Append(e);
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
    private static object ParseNum(string s, ref int i)
    {
        int start = i;
        while (i < s.Length && "0123456789+-.eE".IndexOf(s[i]) >= 0) i++;
        double d;
        double.TryParse(s.Substring(start, i - start), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out d);
        return d;
    }
    public static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            if (c == '"') sb.Append("\\\"");
            else if (c == '\\') sb.Append("\\\\");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.ToString();
    }
    public static string Str(object o) { return o == null ? null : o as string ?? Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture); }
    public static double Num(object o, double def)
    {
        if (o is bool) return (bool)o ? 1 : 0;
        if (o is double) return (double)o;
        double d;
        if (o != null && double.TryParse(o.ToString(), out d)) return d;
        return def;
    }
}

// ============================ CDP 客户端 ============================
internal sealed class Cdp : IDisposable
{
    private ClientWebSocket _ws;
    private int _seq;
    private readonly Dictionary<int, TaskCompletionSource<string>> _pending = new Dictionary<int, TaskCompletionSource<string>>();
    private readonly object _gate = new object();

    public static async Task<Cdp> ConnectAsync(string wsUrl)
    {
        var c = new Cdp();
        c._ws = new ClientWebSocket();
        await c._ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None).ConfigureAwait(false);
        Task.Run((Action)(async () => { try { await c.ReceiveLoop().ConfigureAwait(false); } catch { } c.FailAll("ws closed"); }));
        return c;
    }

    private async Task ReceiveLoop()
    {
        var buf = new byte[64 * 1024];
        while (_ws.State == WebSocketState.Open)
        {
            using (var ms = new MemoryStream())
            {
                WebSocketReceiveResult r;
                do
                {
                    r = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buf, 0, r.Count);
                } while (!r.EndOfMessage);
                string msg = Encoding.UTF8.GetString(ms.ToArray());
                object o = MiniJson.Parse(msg);
                var dict = o as Dictionary<string, object>;
                if (dict == null) continue;
                object idObj;
                if (dict.TryGetValue("id", out idObj))
                {
                    int id = (int)MiniJson.Num(idObj, -1);
                    TaskCompletionSource<string> tcs;
                    lock (_gate)
                    {
                        if (_pending.TryGetValue(id, out tcs)) { _pending.Remove(id); tcs.TrySetResult(msg); }
                    }
                }
            }
        }
    }

    private void FailAll(string why)
    {
        lock (_gate)
        {
            foreach (var tcs in _pending.Values) tcs.TrySetException(new Exception(why));
            _pending.Clear();
        }
    }

    public async Task<string> SendAsync(string method, string paramsJson)
    {
        int id = Interlocked.Increment(ref _seq);
        string req = "{\"id\":" + id + ",\"method\":\"" + method + "\",\"params\":" + paramsJson + "}";
        var tcs = new TaskCompletionSource<string>();
        lock (_gate) { _pending[id] = tcs; }
        var bytes = Encoding.UTF8.GetBytes(req);
        using (var cts = new CancellationTokenSource(20000))
        {
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token).ConfigureAwait(false);
        }
        var done = await Task.WhenAny(tcs.Task, Task.Delay(20000)).ConfigureAwait(false);
        if (done != tcs.Task)
        {
            lock (_gate) { _pending.Remove(id); }
            throw new Exception(method + " timeout");
        }
        return tcs.Task.Result;
    }

    /// <summary>执行页面表达式（表达式应返回字符串），返回该字符串。抛异常代表页面异常或 CDP 错误。</summary>
    public async Task<string> EvaluateAsync(string expr)
    {
        string p = "{\"expression\":\"" + MiniJson.Escape(expr) + "\",\"returnByValue\":true,\"awaitPromise\":true}";
        string resp = await SendAsync("Runtime.evaluate", p).ConfigureAwait(false);
        object o = MiniJson.Parse(resp);
        var dict = o as Dictionary<string, object>;
        if (dict != null && dict.ContainsKey("error"))
        {
            object err; dict.TryGetValue("error", out err);
            throw new Exception("CDP error: " + MiniJson.Str(err));
        }
        var result = dict != null && dict.ContainsKey("result") ? dict["result"] as Dictionary<string, object> : null;
        if (result != null && result.ContainsKey("exceptionDetails"))
        {
            string desc = "";
            var ed = result["exceptionDetails"] as Dictionary<string, object>;
            if (ed != null)
            {
                object exc;
                if (ed.TryGetValue("exception", out exc))
                {
                    var excDict = exc as Dictionary<string, object>;
                    if (excDict != null) desc = MiniJson.Str(excDict.ContainsKey("description") ? excDict["description"] : null);
                }
                if (string.IsNullOrEmpty(desc)) desc = MiniJson.Str(ed.ContainsKey("text") ? ed["text"] : null);
            }
            throw new Exception("page exception: " + (desc ?? "unknown"));
        }
        var inner = result != null && result.ContainsKey("result") ? result["result"] as Dictionary<string, object> : null;
        return inner == null ? null : MiniJson.Str(inner.ContainsKey("value") ? inner["value"] : null);
    }

    public async Task<byte[]> ScreenshotAsync()
    {
        string vp = await EvaluateAsync("(() => JSON.stringify({w: innerWidth, h: innerHeight}))()").ConfigureAwait(false);
        object v = MiniJson.Parse(vp);
        var d = v as Dictionary<string, object>;
        int w = (int)MiniJson.Num(d["w"], 800), h = (int)MiniJson.Num(d["h"], 600);
        string p = "{\"format\":\"png\",\"clip\":{\"x\":0,\"y\":0,\"width\":" + w + ",\"height\":" + h + ",\"scale\":1}}";
        string resp = await SendAsync("Page.captureScreenshot", p).ConfigureAwait(false);
        object o = MiniJson.Parse(resp);
        var dict = o as Dictionary<string, object>;
        var result = dict != null && dict.ContainsKey("result") ? dict["result"] as Dictionary<string, object> : null;
        return result != null && result.ContainsKey("data") ? Convert.FromBase64String(MiniJson.Str(result["data"])) : null;
    }

    public void Dispose()
    {
        try { _ws.Dispose(); } catch { }
    }
}

// ============================ 媒体服务兜底 (file:// 被 CSP 拦时用) ============================
internal sealed class MediaServer : IDisposable
{
    private TcpListener _listener;
    private string _file;
    private string _mime;

    public static MediaServer Start(string file, string mime)
    {
        var s = new MediaServer();
        s._file = file;
        s._mime = mime;
        s._listener = new TcpListener(IPAddress.Loopback, 0);
        s._listener.Start();
        Task.Run((Action)(async () => { try { await s.Loop().ConfigureAwait(false); } catch { } }));
        return s;
    }
    public int Port { get { return ((IPEndPoint)_listener.LocalEndpoint).Port; } }

    private async Task Loop()
    {
        while (true)
        {
            TcpClient c = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            Task.Run((Action)(async () =>
            {
                try
                {
                    using (c)
                    using (var stream = c.GetStream())
                    {
                        var buf = new byte[8192];
                        int n = await stream.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false);
                        string req = Encoding.ASCII.GetString(buf, 0, n);
                        long start = 0;
                        foreach (var line in req.Split('\n'))
                        {
                            if (line.StartsWith("Range:")) {
                                int eq = line.IndexOf('=');
                                int m = line.IndexOf('-', eq);
                                long v;
                                if (eq > 0 && m > eq && long.TryParse(line.Substring(eq + 1, m - eq - 1), out v)) start = v;
                            }
                        }
                        byte[] head;
                        var fi = new FileInfo(_file);
                        if (start > 0)
                        {
                            head = Encoding.ASCII.GetBytes("HTTP/1.1 206 Partial Content\r\nContent-Type: " + _mime +
                                "\r\nContent-Range: bytes " + start + "-" + (fi.Length - 1) + "/" + fi.Length +
                                "\r\nAccept-Ranges: bytes\r\nAccess-Control-Allow-Origin: *\r\nContent-Length: " + (fi.Length - start) + "\r\nConnection: close\r\n\r\n");
                        }
                        else
                        {
                            head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: " + _mime +
                                "\r\nAccept-Ranges: bytes\r\nAccess-Control-Allow-Origin: *\r\nContent-Length: " + fi.Length + "\r\nConnection: close\r\n\r\n");
                        }
                        await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
                        using (var fs = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            fs.Seek(start, SeekOrigin.Begin);
                            var chunk = new byte[256 * 1024];
                            int r;
                            while ((r = await fs.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false)) > 0)
                            {
                                await stream.WriteAsync(chunk, 0, r).ConfigureAwait(false);
                            }
                        }
                    }
                }
                catch { }
            }));
        }
    }
    public void Dispose() { try { _listener.Stop(); } catch { } }
}

// ============================ 配置 ============================
internal sealed class Config
{
    public bool On = true;
    public string Type = "image"; // image | video
    public string SourcePath = "";  // 用户选择的原始文件
    public string CachedFile = "";  // data 目录里的缓存副本（含扩展名）
    public double Opacity = 0.55, Brightness = 1.0, Saturation = 1.0, Contrast = 1.0, Overlay = 0.3, Blur = 0;
    public double PanelOpacity = 0.55, PanelBlur = 10;
    public string Theme = "default"; // default | nocturne | glassy
    public string MusicPath = "";
    public double MusicVolume = 0.6;
    public bool MusicAutoplay = true; // 跟随 ZCode 窗口可见性
    public bool VideoSound = false;
    public string RotateDir = "";       // 轮播文件夹（空=关闭）
    public double RotateMinutes = 15;
    public string RotateOrder = "seq";  // seq | random
    public string AccentColor = "";     // "R,G,B"，空=主题默认
    public string GlassColor = "";      // "R,G,B"，空=主题默认

    public static string PathOf { get { return Path.Combine(Program.DataDir, "config.json"); } }
    public static Config Load()
    {
        try
        {
            var d = MiniJson.Parse(File.ReadAllText(Config.PathOf)) as Dictionary<string, object>;
            if (d == null) return null;
            var c = new Config();
            if (d.ContainsKey("on")) c.On = MiniJson.Num(d["on"], 0) > 0;
            if (d.ContainsKey("type")) c.Type = MiniJson.Str(d["type"]);
            if (d.ContainsKey("sourcePath")) c.SourcePath = MiniJson.Str(d["sourcePath"]);
            if (d.ContainsKey("cachedFile")) c.CachedFile = MiniJson.Str(d["cachedFile"]);
            if (d.ContainsKey("params"))
            {
                var p = d["params"] as Dictionary<string, object>;
                if (p != null)
                {
                    c.Opacity = MiniJson.Num(p.ContainsKey("opacity") ? p["opacity"] : null, c.Opacity);
                    c.Brightness = MiniJson.Num(p.ContainsKey("brightness") ? p["brightness"] : null, c.Brightness);
                    c.Saturation = MiniJson.Num(p.ContainsKey("saturation") ? p["saturation"] : null, c.Saturation);
                    c.Contrast = MiniJson.Num(p.ContainsKey("contrast") ? p["contrast"] : null, c.Contrast);
                    c.Overlay = MiniJson.Num(p.ContainsKey("overlay") ? p["overlay"] : null, c.Overlay);
                    c.Blur = MiniJson.Num(p.ContainsKey("blur") ? p["blur"] : null, c.Blur);
                    c.PanelOpacity = MiniJson.Num(p.ContainsKey("panelOpacity") ? p["panelOpacity"] : null, c.PanelOpacity);
                    c.PanelBlur = MiniJson.Num(p.ContainsKey("panelBlur") ? p["panelBlur"] : null, c.PanelBlur);
                    if (d.ContainsKey("theme")) c.Theme = MiniJson.Str(d["theme"]) ?? "default";
                    if (d.ContainsKey("music")) c.MusicPath = MiniJson.Str(d["music"]) ?? "";
                    if (d.ContainsKey("musicVolume")) c.MusicVolume = MiniJson.Num(d["musicVolume"], c.MusicVolume);
                    if (d.ContainsKey("musicAutoplay")) c.MusicAutoplay = MiniJson.Num(d["musicAutoplay"], 0) > 0 || (d["musicAutoplay"] is bool && (bool)d["musicAutoplay"]);
                    if (d.ContainsKey("videoSound")) c.VideoSound = MiniJson.Num(d["videoSound"], 0) > 0 || (d["videoSound"] is bool && (bool)d["videoSound"]);
                }
            }
            if (d.ContainsKey("rotateDir")) c.RotateDir = MiniJson.Str(d["rotateDir"]) ?? "";
            if (d.ContainsKey("rotateMinutes")) c.RotateMinutes = MiniJson.Num(d["rotateMinutes"], c.RotateMinutes);
            if (d.ContainsKey("rotateOrder")) c.RotateOrder = MiniJson.Str(d["rotateOrder"]) ?? "seq";
            if (d.ContainsKey("accentColor")) c.AccentColor = MiniJson.Str(d["accentColor"]) ?? "";
            if (d.ContainsKey("glassColor")) c.GlassColor = MiniJson.Str(d["glassColor"]) ?? "";
            return c;
        }
        catch { return null; }
    }
    public void Save()
    {
        var sb = new StringBuilder();
        sb.Append("{\"on\":").Append(On ? "true" : "false");
        sb.Append(",\"type\":\"").Append(MiniJson.Escape(Type)).Append("\"");
        sb.Append(",\"sourcePath\":\"").Append(MiniJson.Escape(SourcePath)).Append("\"");
        sb.Append(",\"cachedFile\":\"").Append(MiniJson.Escape(CachedFile)).Append("\"");
        sb.Append(",\"params\":{\"opacity\":").Append(Opacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"brightness\":").Append(Brightness.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"saturation\":").Append(Saturation.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"contrast\":").Append(Contrast.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"overlay\":").Append(Overlay.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"blur\":").Append(Blur.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"panelOpacity\":").Append(PanelOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"panelBlur\":").Append(PanelBlur.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append("},\"theme\":\"").Append(MiniJson.Escape(Theme)).Append("\",\"music\":\"").Append(MiniJson.Escape(MusicPath));
        sb.Append("\",\"musicVolume\":").Append(MusicVolume.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"musicAutoplay\":").Append(MusicAutoplay ? "true" : "false");
        sb.Append(",\"videoSound\":").Append(VideoSound ? "true" : "false");
        sb.Append(",\"rotateDir\":\"").Append(MiniJson.Escape(RotateDir)).Append("\"");
        sb.Append(",\"rotateMinutes\":").Append(RotateMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"rotateOrder\":\"").Append(MiniJson.Escape(RotateOrder)).Append("\"");
        sb.Append(",\"accentColor\":\"").Append(MiniJson.Escape(AccentColor)).Append("\"");
        sb.Append(",\"glassColor\":\"").Append(MiniJson.Escape(GlassColor)).Append("\"");
        sb.Append(",\"updatedAt\":\"").Append(DateTime.UtcNow.ToString("o")).Append("\"}");
        File.WriteAllText(Config.PathOf, sb.ToString());
    }
}

// ============================ 工具 ============================
internal static class Util
{
    private static readonly Dictionary<string, string> Mime = new Dictionary<string, string>
    {
        { ".png", "image/png" }, { ".jpg", "image/jpeg" }, { ".jpeg", "image/jpeg" },
        { ".webp", "image/webp" }, { ".gif", "image/gif" }, { ".mp4", "video/mp4" },
        { ".webm", "video/webm" }, { ".mov", "video/quicktime" }, { ".mp3", "audio/mpeg" },
        { ".wav", "audio/wav" }, { ".ogg", "audio/ogg" }, { ".m4a", "audio/mp4" }, { ".flac", "audio/flac" }
    };
    public static string MimeFor(string ext) { string m; return Mime.TryGetValue(ext.ToLowerInvariant(), out m) ? m : null; }
    public static bool IsVideo(string ext) { ext = ext.ToLowerInvariant(); return ext == ".mp4" || ext == ".webm" || ext == ".mov"; }
    public static bool IsAudio(string ext) { ext = ext.ToLowerInvariant(); return ext == ".mp3" || ext == ".wav" || ext == ".ogg" || ext == ".m4a" || ext == ".flac"; }
    public static long MaxBytes { get { return 200 * 1024 * 1024; } }

    /// <summary>解析颜色 "R,G,B" / "R G B" / "#RRGGBB" → 规范 "R,G,B"；无效返回 null</summary>
    public static string NormalizeRgb(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        s = s.Trim().Trim('"');
        int r, g, b;
        string hex = s.StartsWith("#") ? s.Substring(1) : null;
        if (hex != null && hex.Length == 6
            && int.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out r)
            && int.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out g)
            && int.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out b))
            return Clamp255(r) + "," + Clamp255(g) + "," + Clamp255(b);
        var parts = s.Replace(' ', ',').Split(',');
        if (parts.Length == 3
            && int.TryParse(parts[0].Trim(), out r) && int.TryParse(parts[1].Trim(), out g) && int.TryParse(parts[2].Trim(), out b))
            return Clamp255(r) + "," + Clamp255(g) + "," + Clamp255(b);
        return null;
    }
    private static int Clamp255(int v) { return v < 0 ? 0 : (v > 255 ? 255 : v); }

    /// <summary>路径清洗: 去空白/成对引号(单双全半角)、/d/xx 与 d:/xx 统一为 d:\xx</summary>
    public static string NormalizePath(string s)
    {
        if (s == null) return "";
        s = s.Trim();
        s = s.Trim(' ', '"', '\'', '“', '”', '‘', '’');
        s = s.Trim();
        // Git Bash 风格 /d/xxx → d:\xxx
        if (s.Length >= 3 && s[0] == '/' && s[2] == '/' && char.IsLetter(s[1]))
        {
            s = char.ToUpperInvariant(s[1]) + ":\\/" + s.Substring(3);
        }
        s = s.Replace('/', '\\');
        s = System.Text.RegularExpressions.Regex.Replace(s, "\\\\+", "\\");
        return s.TrimEnd('\\');
    }

    public static string FileUrl(string path)
    {
        var uri = new Uri(path);
        return uri.AbsoluteUri;
    }

    public static string Log(string msg)
    {
        string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + msg;
        try { File.AppendAllText(Path.Combine(Program.DataDir, "watch.log"), line + Environment.NewLine); } catch { }
        return line;
    }
}

// ============================ 注入器 ============================
internal static class Injector
{
    public const string Host = "http://127.0.0.1:9335";

    private static string CssTemplate()
    {
        try
        {
            string ext = Path.Combine(Program.ExeDir, "wallpaper.css");
            if (File.Exists(ext)) return File.ReadAllText(ext);
        }
        catch { }
        return Assets.DefaultCss;
    }

    public static string BuildCss(bool video)
    {
        string t = CssTemplate();
        if (video) t = System.Text.RegularExpressions.Regex.Replace(t, "--zcode-wallpaper-url:[^;]*;", "--zcode-wallpaper-url: none;");
        else t = t.Replace("__IMG_URL__", "");
        return t;
    }

    private static string FilterExpr(Config c)
    {
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "'blur({0}px) brightness({1}) saturate({2}) contrast({3})'",
            c.Blur, c.Brightness, c.Saturation, c.Contrast);
    }

    private const string InjectFn = @"
async (cfg) => {
  window.__ZCWP = window.__ZCWP || {};
  const W = window.__ZCWP;
  const teardown = () => {
    for (const id of ['zcode-wallpaper-layer', 'zcode-wallpaper-overlay', 'zcode-wallpaper-fade']) {
      const el = document.getElementById(id); if (el) el.remove();
    }
    for (const id of ['zcode-wallpaper-audio']) {
      const el = document.getElementById(id); if (el) { try { el.pause(); } catch (e) {} el.remove(); }
    }
    if (W.onVis) { document.removeEventListener('visibilitychange', W.onVis); W.onVis = null; }
    if (W.sheet) {
      const i = document.adoptedStyleSheets.indexOf(W.sheet);
      if (i >= 0) { const c = [...document.adoptedStyleSheets]; c.splice(i, 1); document.adoptedStyleSheets = c; }
      W.sheet = null;
    }
    W.on = false; W.applyParams = null; W.setMusic = null; W.swapImage = null; W.video = null; W.audio = null;
  };
  teardown();
  if (!cfg.on) return 'CLEARED';
  if (!document.body) return 'NOBODY';
  const sheet = new CSSStyleSheet();
  sheet.replaceSync(cfg.css);
  document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];
  W.sheet = sheet;
  const layer = document.createElement('div');
  layer.id = 'zcode-wallpaper-layer';
  document.documentElement.appendChild(layer);
  const overlay = document.createElement('div');
  overlay.id = 'zcode-wallpaper-overlay';
  overlay.setAttribute('style', 'position:fixed;inset:0;z-index:-1;pointer-events:none;background:#000;');
  document.documentElement.appendChild(overlay);
  const base = 'position:fixed;inset:0;z-index:-1;pointer-events:none;';
  W.musicAutoplay = cfg.musicAutoplay !== undefined ? cfg.musicAutoplay : true;
  if (cfg.video) {
    const v = document.createElement('video');
    v.id = 'zcode-wallpaper-video';
    v.src = cfg.img; v.autoplay = true; v.loop = true; v.muted = !cfg.videoSound; v.playsInline = true;
    v.setAttribute('style', 'width:100%;height:100%;object-fit:cover;display:block;');
    layer.setAttribute('style', base);
    layer.appendChild(v);
    W.video = v;
    const pp = v.play(); if (pp && pp.catch) pp.catch(() => {});
  } else {
    layer.setAttribute('style', base + 'background-image:url(' + JSON.stringify(cfg.img) + ');background-position:center;background-size:cover;background-repeat:no-repeat;');
  }
  // 独立音乐层（与壁纸无关; audio loop 常驻, 可见性由 onVis 控制）
  if (cfg.music) {
    const a = document.createElement('audio');
    a.id = 'zcode-wallpaper-audio';
    a.src = cfg.music; a.loop = true; a.volume = cfg.musicVolume !== undefined ? cfg.musicVolume : 0.6;
    document.documentElement.appendChild(a);
    W.audio = a;
    if (W.musicAutoplay) {
      const tryPlay = () => { const mp = a.play(); if (mp && mp.catch) mp.catch(() => {}); };
      if (document.visibilityState === 'visible') tryPlay();
      else setTimeout(tryPlay, 600);
      setTimeout(() => { if (a.paused && W.musicAutoplay && document.visibilityState === 'visible') tryPlay(); }, 900);
    }
  }
  W.onVis = () => {
    const vis = document.visibilityState === 'visible';
    if (W.video) { if (!vis) { try { W.video.pause(); } catch (e) {} } else { const p = W.video.play(); if (p && p.catch) p.catch(() => {}); } }
    if (W.audio && W.musicAutoplay) { if (!vis) { try { W.audio.pause(); } catch (e) {} } else { const p = W.audio.play(); if (p && p.catch) p.catch(() => {}); } }
  };
  document.addEventListener('visibilitychange', W.onVis);
  W.applyParams = (p) => {
    const l = document.getElementById('zcode-wallpaper-layer');
    const o = document.getElementById('zcode-wallpaper-overlay');
    if (l) { l.style.opacity = p.opacity; l.style.filter = 'blur(' + p.blur + 'px) brightness(' + p.brightness + ') saturate(' + p.saturation + ') contrast(' + p.contrast + ')'; }
    if (o) o.style.opacity = p.overlay;
    document.documentElement.style.setProperty('--zcwp-panel-opacity', p.panelOpacity);
    document.documentElement.style.setProperty('--zcwp-panel-blur', p.panelBlur + 'px');
    if (p.accentColor) document.documentElement.style.setProperty('--color-accent', p.accentColor);
    else document.documentElement.style.removeProperty('--color-accent');
    if (p.glassColor) document.documentElement.style.setProperty('--zcwp-panel-rgb', p.glassColor);
    else document.documentElement.style.removeProperty('--zcwp-panel-rgb');
    if (p.theme) document.documentElement.setAttribute('data-zcwp-theme', p.theme);
    if (W.video) { W.video.muted = !(p.videoSound === true); W.video.volume = p.musicVolume !== undefined ? p.musicVolume : 0.6; }
    if (W.audio && p.musicVolume !== undefined) W.audio.volume = p.musicVolume;
  };
  W.setMusic = (m) => {
    if (m.src) {
      let a = W.audio;
      if (!a) { a = document.createElement('audio'); a.id = 'zcode-wallpaper-audio'; a.loop = true; document.documentElement.appendChild(a); W.audio = a; }
      if (a.src !== m.src) a.src = m.src;
      if (m.volume !== undefined) a.volume = m.volume;
      W.musicAutoplay = m.autoplay !== undefined ? m.autoplay : W.musicAutoplay;
      if (m.playing) { const p = a.play(); if (p && p.catch) p.catch(() => {}); }
      else a.pause();
      return 'OK';
    }
    if (W.audio) { try { W.audio.pause(); } catch (e) {} W.audio.remove(); W.audio = null; }
    return 'OK';
  };
  W.swapImage = (src) => {
    if (W.video) return 'VIDEO';
    const l = document.getElementById('zcode-wallpaper-layer');
    if (!l) return 'NO';
    const u = 'url(' + JSON.stringify(src) + ')';
    // 伪元素方案: 新图在壁纸层内部(::before)淡入, 不插兄弟 DOM — backdrop-filter 采样面保持稳定, 玻璃无真空期
    l.style.setProperty('--zcwp-next-bg', u);
    void l.offsetWidth;
    requestAnimationFrame(() => { l.style.setProperty('--zcwp-before-op', '1'); });
    setTimeout(() => {
      l.style.backgroundImage = u;
      l.style.setProperty('--zcwp-next-bg', 'none');
      l.style.setProperty('--zcwp-before-op', '0');
    }, 900);
    return 'OK';
  };
  W.applyParams(cfg);
  W.ver = 4; W.on = true;
  return 'OK';
}";

    private static string CfgJson(Config c, string url, bool video, string css)
    {
        string musicUrl = "";
        if (c.MusicPath != null && c.MusicPath.Length > 0)
        {
            try { musicUrl = Util.FileUrl(Path.Combine(Program.DataDir, "music" + Path.GetExtension(c.MusicPath))); } catch { }
        }
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{{\"on\":true,\"img\":\"{0}\",\"video\":{1},\"opacity\":{2},\"brightness\":{3},\"saturation\":{4},\"contrast\":{5},\"overlay\":{6},\"blur\":{7},\"panelOpacity\":{8},\"panelBlur\":{9},\"theme\":\"{10}\",\"music\":{11},\"musicVolume\":{12},\"musicAutoplay\":{13},\"videoSound\":{14},\"css\":\"{15}\",\"accentColor\":\"{16}\",\"glassColor\":\"{17}\"}}",
            MiniJson.Escape(url), video ? "true" : "false", c.Opacity, c.Brightness, c.Saturation, c.Contrast, c.Overlay, c.Blur, c.PanelOpacity, c.PanelBlur, MiniJson.Escape(c.Theme),
            (musicUrl.Length > 0 ? "\"" + MiniJson.Escape(musicUrl) + "\"" : "null"),
            c.MusicVolume, c.MusicAutoplay ? "true" : "false", c.VideoSound ? "true" : "false", MiniJson.Escape(css),
            MiniJson.Escape(string.IsNullOrEmpty(c.AccentColor) ? "" : "rgb(" + c.AccentColor + ")"),
            MiniJson.Escape(c.GlassColor ?? ""));
    }

    /// <summary>app\custom.css 逃生舱: 存在即拼接在主题样式之后（仅注入时读取, 改后需重注入）</summary>
    private static string CustomCss()
    {
        try
        {
            string p = Path.Combine(Program.ExeDir, "custom.css");
            if (File.Exists(p)) return File.ReadAllText(p);
        }
        catch { }
        return "";
    }

    public static async Task<string> InjectAsync(Config c, string targetWsUrl, string url, bool video)
    {
        using (var cdp = await Cdp.ConnectAsync(targetWsUrl).ConfigureAwait(false))
        {
            string css = BuildCss(video);
            string custom = CustomCss();
            if (custom.Length > 0) css = css + "\n/* custom.css */\n" + custom;
            string expr = "(" + InjectFn + ")(" + CfgJson(c, url, video, css) + ")";
            return await cdp.EvaluateAsync(expr).ConfigureAwait(false);
        }
    }

    public static async Task<string> ClearAsync(string targetWsUrl)
    {
        using (var cdp = await Cdp.ConnectAsync(targetWsUrl).ConfigureAwait(false))
        {
            string expr = "(" + InjectFn + ")({\"on\":false})";
            return await cdp.EvaluateAsync(expr).ConfigureAwait(false);
        }
    }

    /// <summary>滑块实时更新：不重建图层，只改内联样式与面板玻璃变量。返回 OK/NO。</summary>
    public static async Task<string> LiveUpdateAsync(string targetWsUrl, Config c)
    {
        string expr = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "(() => {{ const W = window.__ZCWP; if (!W || !W.on || !W.applyParams) return 'NO'; W.applyParams({{opacity:{0},brightness:{1},saturation:{2},contrast:{3},overlay:{4},blur:{5},panelOpacity:{6},panelBlur:{7},theme:\"{8}\",musicVolume:{9},videoSound:{10},accentColor:\"{11}\",glassColor:\"{12}\"}}); return 'OK'; }})()",
            c.Opacity, c.Brightness, c.Saturation, c.Contrast, c.Overlay, c.Blur, c.PanelOpacity, c.PanelBlur, MiniJson.Escape(c.Theme), c.MusicVolume, c.VideoSound ? "true" : "false",
            MiniJson.Escape(string.IsNullOrEmpty(c.AccentColor) ? "" : "rgb(" + c.AccentColor + ")"),
            MiniJson.Escape(c.GlassColor ?? ""));
        using (var cdp = await Cdp.ConnectAsync(targetWsUrl).ConfigureAwait(false))
        {
            return await cdp.EvaluateAsync(expr).ConfigureAwait(false);
        }
    }

    /// <summary>音乐命令: 内联 DOM 操作（不依赖注入层版本）; src 为空=清除; playing: 1=播/0=暂停/-1=保持现状; 同步 W.audio 供 onVis 跟随</summary>
    public static async Task<string> SetMusicAsync(string targetWsUrl, string src, int playing, double volume, bool autoplay)
    {
        string expr = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "(() => {{ const W = window.__ZCWP || {{}}; let a = document.getElementById('zcode-wallpaper-audio');"
            + " const src = {0}; const vol = {1};"
            + " if (src) {{"
            + "   if (!a) {{ a = document.createElement('audio'); a.id = 'zcode-wallpaper-audio'; a.loop = true; document.documentElement.appendChild(a); }}"
            + "   if (a.src !== src) a.src = src;"
            + "   a.volume = vol;"
            + "   W.audio = a; W.musicAutoplay = {2};"
            + "   if ({3} === 1) {{ const p = a.play(); if (p && p.catch) p.catch(() => {{}}); }} else if ({3} === 0) a.pause();"
            + "   return 'OK';"
            + " }}"
            + " if (a) {{ try {{ a.pause(); }} catch (e) {{}} a.remove(); W.audio = null; }}"
            + " return 'OK';"
            + " }})()",
            (src != null && src.Length > 0 ? "\"" + MiniJson.Escape(src) + "\"" : "null"), volume, autoplay ? "true" : "false", playing);
        using (var cdp = await Cdp.ConnectAsync(targetWsUrl).ConfigureAwait(false))
        {
            return await cdp.EvaluateAsync(expr).ConfigureAwait(false);
        }
    }

    /// <summary>轮播换图: 图层内淡入替换（不 teardown, 音乐不受影响）。返回 OK / NO(旧层或无注入层) / VIDEO。</summary>
    public static async Task<string> SwapImageAsync(string targetWsUrl, string url)
    {
        string expr = "(() => { const W = window.__ZCWP; if (!W || !W.on || !W.swapImage) return 'NO'; return W.swapImage(\""
            + MiniJson.Escape(url) + "\"); })()";
        using (var cdp = await Cdp.ConnectAsync(targetWsUrl).ConfigureAwait(false))
        {
            return await cdp.EvaluateAsync(expr).ConfigureAwait(false);
        }
    }

    /// <summary>探测图片/视频 URL 在页面里能否加载。返回 OK/FAIL/TIMEOUT。</summary>
    public static async Task<string> CheckMediaAsync(string targetWsUrl, string url, bool video)
    {
        string js = "(async () => {\n" +
            (video
                ? "  const el = document.createElement('video'); el.muted = true; el.playsInline = true; const okEv = 'loadeddata';\n"
                : "  const el = new Image(); const okEv = 'load';\n") +
            "  const url = '" + MiniJson.Escape(url) + "';\n" +
            "  const p = new Promise((res) => { el.addEventListener(okEv, () => res('OK')); el.addEventListener('error', () => res('FAIL')); setTimeout(() => res('TIMEOUT'), 5000); });\n" +
            "  el.src = url;\n" +
            (video ? "  const pp = el.play(); if (pp && pp.catch) pp.catch(() => {});\n" : "") +
            "  return await p;\n" +
            "})()";
        using (var cdp = await Cdp.ConnectAsync(targetWsUrl).ConfigureAwait(false))
        {
            return await cdp.EvaluateAsync(js).ConfigureAwait(false);
        }
    }

    public static async Task<VideoProbeResult> CheckVideoPlayingAsync(string targetWsUrl)
    {
        string js = @"
(async () => {
  const v = document.getElementById('zcode-wallpaper-video');
  if (!v) return 'NOVIDEO';
  const t0 = v.currentTime;
  await new Promise((r) => setTimeout(r, 600));
  return JSON.stringify({ playing: !v.paused && !v.ended, advanced: v.currentTime > t0, t: v.currentTime });
})()";
        using (var cdp = await Cdp.ConnectAsync(targetWsUrl).ConfigureAwait(false))
        {
            string s = await cdp.EvaluateAsync(js).ConfigureAwait(false);
            var d = MiniJson.Parse(s) as Dictionary<string, object>;
            if (d == null) return new VideoProbeResult { State = s };
            return new VideoProbeResult { HasVideo = true, Playing = MiniJson.Num(d["playing"], 0) > 0, Advanced = MiniJson.Num(d["advanced"], 0) > 0 };
        }
    }

    public sealed class VideoProbeResult
    {
        public bool HasVideo;
        public bool Playing;
        public bool Advanced;
        public string State;
    }

    // ---------- 目标发现 ----------
    public sealed class Target
    {
        public string Id;
        public string WsUrl;
        public string Title;
        public string Url;
    }

    public static List<Target> GetTargets()
    {
        string json;
        using (var wc = new WebClient())
        {
            wc.Proxy = null; // 127.0.0.1 请求绝不能走系统代理（iKuuu/clash 会黑洞它）
            wc.Encoding = Encoding.UTF8;
            json = wc.DownloadString(Host + "/json");
        }
        var list = new List<Target>();
        object o = MiniJson.Parse(json);
        var arr = o as List<object>;
        if (arr == null) return list;
        foreach (var item in arr)
        {
            var d = item as Dictionary<string, object>;
            if (d == null) continue;
            string type = MiniJson.Str(d.ContainsKey("type") ? d["type"] : null);
            string url = MiniJson.Str(d.ContainsKey("url") ? d["url"] : null) ?? "";
            if (type != "page") continue;
            if (url.StartsWith("devtools:") || url.StartsWith("chrome-extension:") || url.StartsWith("edge:")) continue;
            list.Add(new Target
            {
                Id = MiniJson.Str(d.ContainsKey("id") ? d["id"] : null),
                WsUrl = MiniJson.Str(d.ContainsKey("webSocketDebuggerUrl") ? d["webSocketDebuggerUrl"] : null),
                Title = MiniJson.Str(d.ContainsKey("title") ? d["title"] : null),
                Url = url
            });
        }
        return list;
    }

    public static bool EndpointUp()
    {
        try
        {
            using (var wc = new WebClient()) { wc.Proxy = null; wc.DownloadString(Host + "/json/version"); }
            return true;
        }
        catch { return false; }
    }
}

// ============================ 守护循环 ============================
internal static class Watch
{
    private static readonly HashSet<string> Tracked = new HashSet<string>();
    private static readonly object Gate = new object();
    private static DateTime _nextRotateAt = DateTime.MinValue; // MinValue=到期待换
    private static int _rotIdx = -1;
    private static readonly Random _rand = new Random();

    /// <summary>GUI/CLI 设置轮播后调用: 下个 tick(≤3s) 立即换第一张</summary>
    public static void RequestImmediateRotate() { _nextRotateAt = DateTime.MinValue; }

    public static async Task Run(CancellationToken cancel)
    {
        int tick = 0;
        Util.Log("watch v3 启动, 端点 " + Injector.Host);
        while (!cancel.IsCancellationRequested)
        {
            tick++;
            try
            {
                Config cfg = Config.Load();
                if (cfg != null && cfg.On)
                {
                    List<Injector.Target> targets = Injector.GetTargets();
                    lock (Gate)
                    {
                        var alive = new HashSet<string>();
                        foreach (var t in targets) alive.Add(t.Id);
                        Tracked.RemoveWhere(id => !alive.Contains(id));
                    }
                    foreach (var t in targets)
                    {
                        if (cancel.IsCancellationRequested) break;
                        bool isTracked;
                        lock (Gate) { isTracked = Tracked.Contains(t.Id); }
                        bool needVerify = isTracked && tick % 5 == 0; // 每 15s 校验一次 marker
                        bool shouldInject = !isTracked;
                        string present = null;
                        try
                        {
                            if (needVerify)
                            {
                                using (var cdp = await Cdp.ConnectAsync(t.WsUrl).ConfigureAwait(false))
                                {
                                    present = await cdp.EvaluateAsync("String(!!(window.__ZCWP && window.__ZCWP.on))").ConfigureAwait(false);
                                }
                                if (present == "false") shouldInject = true;
                            }
                        }
                        catch { continue; }
                        if (!shouldInject) continue;
                        // 需要注入
                        string url; bool video;
                        if (!ResolveMediaUrl(cfg, out url, out video)) { Util.Log("媒体解析失败: " + cfg.SourcePath); lock (Gate) { Tracked.Add(t.Id); } continue; }
                        try
                        {
                            string r = await Injector.InjectAsync(cfg, t.WsUrl, url, video).ConfigureAwait(false);
                            lock (Gate) { Tracked.Add(t.Id); }
                            if (r == "OK") Util.Log("已注入: " + (t.Title ?? t.Url));
                        }
                        catch (Exception ex)
                        {
                            if (tick % 20 == 1) Util.Log("注入异常 " + t.Url + ": " + ex.Message);
                        }
                    }
                    await TryRotate(cfg, targets, cancel).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                lock (Gate) { Tracked.Clear(); }
                if (tick % 40 == 1) Util.Log("端点不可达，继续等待: " + ex.Message);
            }
            await Task.Delay(3000, cancel).ConfigureAwait(false);
        }
    }

    /// <summary>轮播 tick: 仅图片壁纸时按间隔换目录里的下一张/随机一张。换图走图层内替换, 音乐与注入层不受影响。</summary>
    private static async Task TryRotate(Config cfg, List<Injector.Target> targets, CancellationToken cancel)
    {
        if (string.IsNullOrEmpty(cfg.RotateDir) || cfg.Type != "image") return; // 视频壁纸时轮播挂起
        if (targets == null || targets.Count == 0) return; // ZCode 未开: 保持到期待出现
        if (DateTime.Now < _nextRotateAt) return;
        var files = ListRotateImages(cfg.RotateDir);
        if (files.Count == 0)
        {
            Util.Log("轮播目录无可用图片: " + cfg.RotateDir);
            _nextRotateAt = DateTime.Now.AddMinutes(Math.Max(cfg.RotateMinutes, 1));
            return;
        }
        int idx;
        if (cfg.RotateOrder == "random") idx = _rand.Next(files.Count);
        else { _rotIdx = (_rotIdx + 1) % files.Count; idx = _rotIdx; }
        for (int attempt = 0; attempt < Math.Min(3, files.Count); attempt++) // 坏文件顺延, 最多试 3 张
        {
            if (cancel.IsCancellationRequested) return;
            string pick = files[(idx + attempt) % files.Count];
            string ext = Path.GetExtension(pick).ToLowerInvariant();
            string mime = Util.MimeFor(ext);
            if (mime == null) continue;
            string cached = Path.Combine(Program.DataDir, "wallpaper" + ext);
            try
            {
                if (!string.Equals(Path.GetFullPath(pick), Path.GetFullPath(cached), StringComparison.OrdinalIgnoreCase))
                    File.Copy(pick, cached, true);
            }
            catch { continue; }
            // URL 降级链: dataURL → file://（与 Apply 同款）
            string url = "data:" + mime + ";base64," + Convert.ToBase64String(File.ReadAllBytes(cached));
            string probe = await Injector.CheckMediaAsync(targets[0].WsUrl, url, false).ConfigureAwait(false);
            if (probe != "OK")
            {
                url = Util.FileUrl(cached);
                probe = await Injector.CheckMediaAsync(targets[0].WsUrl, url, false).ConfigureAwait(false);
                if (probe != "OK") continue;
            }
            // 先落配置再换: 中途自愈重注入也是新图
            cfg.SourcePath = pick;
            cfg.CachedFile = cached;
            cfg.Save();
            foreach (var t in targets)
            {
                try
                {
                    string r = await Injector.SwapImageAsync(t.WsUrl, url).ConfigureAwait(false);
                    if (r == "OK") continue;
                    string u2; bool v2; // 旧版注入层(无 swapImage): 全量重注入兜底
                    if (ResolveMediaUrl(cfg, out u2, out v2))
                        await Injector.InjectAsync(cfg, t.WsUrl, u2, v2).ConfigureAwait(false);
                }
                catch { }
            }
            Util.Log("轮播 → " + pick);
            break;
        }
        _nextRotateAt = DateTime.Now.AddMinutes(Math.Max(cfg.RotateMinutes, 1));
    }

    /// <summary>枚举轮播目录里的图片（png/jpg/jpeg/webp/gif, ≤30MiB, 按文件名排序）</summary>
    public static List<string> ListRotateImages(string dir)
    {
        var exts = new HashSet<string> { ".png", ".jpg", ".jpeg", ".webp", ".gif" };
        var list = new List<string>();
        try
        {
            foreach (var f in Directory.GetFiles(dir))
            {
                if (!exts.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
                try { if (new FileInfo(f).Length <= 30L * 1024 * 1024) list.Add(f); } catch { }
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        return list;
    }

    /// <summary>把配置里的缓存文件解析成可用的 URL（图片 data URL / 视频 file://→本地服务），返回 false 表示文件缺失。</summary>
    public static bool ResolveMediaUrl(Config cfg, out string url, out bool video)
    {
        video = cfg.Type == "video";
        url = null;
        string cached = cfg.CachedFile;
        if (string.IsNullOrEmpty(cached) || !File.Exists(cached)) return false;
        string ext = Path.GetExtension(cached).ToLowerInvariant();
        string mime = Util.MimeFor(ext);
        if (mime == null) return false;
        if (!video)
        {
            url = "data:" + mime + ";base64," + Convert.ToBase64String(File.ReadAllBytes(cached));
            return true;
        }
        url = Util.FileUrl(cached);
        return true;
    }
}

// ============================ 默认样式模板（外部 wallpaper.css 存在时优先用外部） ============================
internal static class Assets
{
    public const string DefaultCss = @"
:root {
  --zcode-wallpaper-url: none;
  --color-background: transparent !important;
  --color-background-alt: transparent !important;
  --color-background-win-alt: transparent !important;
  --color-panel: transparent !important;
  --color-surface: transparent !important;
  --color-surface-hover: transparent !important;
  --color-idle-task-surface: transparent !important;
  --color-interaction-ask-surface: transparent !important;
  --color-interaction-confirmation-surface: transparent !important;
  --color-background-primary: transparent !important;
  --color-background-secondary: transparent !important;
  --color-background-tertiary: transparent !important;
  --color-bg: transparent !important;
  --color-card: transparent !important;
  --color-sidebar: transparent !important;
  --color-header: transparent !important;
  --background: transparent !important;
  --bg: transparent !important;
  --bg-color: transparent !important;
}
html { background: transparent !important; background-color: #161616 !important; }
body { background: transparent !important; }
#root, #app, #__next, body > div:first-child { background: transparent !important; }
#zcode-wallpaper-layer {
  position: fixed !important; inset: 0 !important; z-index: -1 !important; pointer-events: none !important;
  background-position: center !important; background-size: cover !important; background-repeat: no-repeat !important;
}
#zcode-wallpaper-layer::before {
  content: """"; position: absolute !important; inset: 0 !important;
  background-image: var(--zcwp-next-bg, none);
  background-position: center !important; background-size: cover !important; background-repeat: no-repeat !important;
  opacity: var(--zcwp-before-op, 0); transition: opacity .8s ease;
}
#zcode-wallpaper-overlay {
  position: fixed !important; inset: 0 !important; z-index: -1 !important; pointer-events: none !important; background: #000 !important;
}
[role=""dialog""], [aria-modal=""true""] {
  background-color: rgba(var(--zcwp-panel-rgb, 18, 18, 18), max(calc(var(--zcwp-panel-opacity, 0.55) + 0.25), 0.72)) !important;
  backdrop-filter: blur(calc(var(--zcwp-panel-blur, 10px) * 1.6)) saturate(1.1) !important;
  -webkit-backdrop-filter: blur(calc(var(--zcwp-panel-blur, 10px) * 1.6)) saturate(1.1) !important;
}
section.fixed.inset-0.z-50, div.fixed.inset-0.z-50 {
  background: rgba(var(--zcwp-panel-rgb, 18, 18, 18), var(--zcwp-panel-opacity, 0.55)) !important;
  backdrop-filter: blur(var(--zcwp-panel-blur, 10px)) !important;
  -webkit-backdrop-filter: blur(var(--zcwp-panel-blur, 10px)) !important;
}
[data-testid=""coding-plan-embedded-webview""] {
  border-radius: 12px !important;
  border: 1px solid rgba(var(--zcwp-sep-rgb, 255, 255, 255), 0.08) !important;
  overflow: hidden !important;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.35) !important;
}
";
}

// ============================ 主窗体 ============================
internal sealed class MainForm : Form
{
    private readonly bool _startHidden;
    private CancellationTokenSource _cts;
    private NotifyIcon _tray;
    private Label _status;
    private TextBox _pathBox;
    private TrackBar _opacity, _brightness, _saturation, _contrast, _overlay, _blur, _panelOpacity, _panelBlur;
    private Label _opacityV, _brightnessV, _saturationV, _contrastV, _overlayV, _blurV, _panelOpacityV, _panelBlurV;
    private ComboBox _themeCombo;
    private TextBox _musicPathBox;
    private Button _musicPlayBtn, _musicClearBtn;
    private TrackBar _musicVolumeBar;
    private Label _musicVolumeV;
    private CheckBox _musicFollowChk, _videoSoundChk;
    private bool _musicPlaying;
    private TextBox _rotatePathBox;
    private ComboBox _rotateIntervalCombo, _rotateOrderCombo;
    private Button _accentBtn, _glassBtn;
    private System.Windows.Forms.Timer _throttle;
    private Config _cfg;
    private bool _exitRequested;
    private bool _loadingSliders;
    private EventWaitHandle _exitEv;

    public MainForm(bool startHidden)
    {
        _startHidden = startHidden;
        _cfg = Config.Load() ?? new Config();
        BuildUi();
        BuildTray();
        _throttle = new System.Windows.Forms.Timer { Interval = 120 };
        _throttle.Tick += ThrottleTick;
        _cts = new CancellationTokenSource();
        Task.Run(() => Watch.Run(_cts.Token));
        ThreadPool.RegisterWaitForSingleObject(Program.ShowGuiEvent,
            (s, timedOut) => { try { BeginInvoke((Action)ShowPanel); } catch { } },
            null, -1, false);
        _exitEv = new EventWaitHandle(false, EventResetMode.AutoReset, "ZCodeWallpaperExitEvent");
        ThreadPool.RegisterWaitForSingleObject(_exitEv,
            (s, timedOut) => { try { BeginInvoke((Action)ExitApp); } catch { } },
            null, -1, false);
        // CLI rotate → 守护立即轮换（跨进程信号; CLI 进程里 RequestImmediateRotate 够不到守护的静态量）
        var rotateNowEv = new EventWaitHandle(false, EventResetMode.AutoReset, "ZCodeWallpaperRotateNow");
        ThreadPool.RegisterWaitForSingleObject(rotateNowEv,
            (s, timedOut) => { Watch.RequestImmediateRotate(); },
            null, -1, false);
        if (!startHidden) ShowPanel();
    }

    private void ShowPanel()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        RefreshStatusAsync();
    }

    private void BuildTray()
    {
        _tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "ZCode 壁纸 v1.4", Visible = true };
        var menu = new ContextMenu();
        menu.MenuItems.Add("打开面板", delegate { ShowPanel(); });
        menu.MenuItems.Add("截图检查", delegate { ShotAsync(); });
        menu.MenuItems.Add(new MenuItem("-"));
        menu.MenuItems.Add("卸载并恢复原状", delegate { UninstallAndExit(); });
        menu.MenuItems.Add(new MenuItem("-"));
        menu.MenuItems.Add("退出", delegate { ExitApp(); });
        _tray.ContextMenu = menu;
        _tray.DoubleClick += delegate { ShowPanel(); };
    }

    private void ExitApp()
    {
        _exitRequested = true;
        try { _tray.Visible = false; } catch { }
        _cts.Cancel();
        Close();
        Application.Exit();
    }

    private void UninstallAndExit()
    {
        _exitRequested = true;
        try { _tray.Visible = false; } catch { }
        _cts.Cancel();
        Setup.PerformUninstall(delegate (string s) { SetStatus(s); });
        Close();
        Application.Exit();
    }

    private void BuildUi()
    {
        Text = "ZCode 壁纸 v1.4";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(480, 760);
        Font = new Font("Microsoft YaHei UI", 9F);

        _status = new Label { Location = new Point(12, 10), Size = new Size(456, 22), Text = "状态: 检测中…" };
        Controls.Add(_status);

        Controls.Add(new Label { Location = new Point(12, 38), Size = new Size(200, 20), Text = "图片 / 视频路径 (粘贴自动清洗):" });
        _pathBox = new TextBox { Location = new Point(12, 60), Size = new Size(372, 23) };
        _pathBox.Leave += delegate { _pathBox.Text = Util.NormalizePath(_pathBox.Text); };
        Controls.Add(_pathBox);
        var browse = new Button { Location = new Point(392, 59), Size = new Size(76, 25), Text = "浏览…" };
        browse.Click += delegate
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "图片/视频|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.mp4;*.webm|所有文件|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK) _pathBox.Text = dlg.FileName;
            }
        };
        Controls.Add(browse);

        var apply = new Button { Location = new Point(12, 92), Size = new Size(146, 30), Text = "应用壁纸" };
        apply.Click += delegate { ApplyAsync(); };
        Controls.Add(apply);
        var clear = new Button { Location = new Point(166, 92), Size = new Size(146, 30), Text = "清除壁纸" };
        clear.Click += delegate { ClearAsync(); };
        Controls.Add(clear);
        var shot = new Button { Location = new Point(320, 92), Size = new Size(148, 30), Text = "截图检查" };
        shot.Click += delegate { ShotAsync(); };
        Controls.Add(shot);

        Controls.Add(new Label { Location = new Point(12, 128), Size = new Size(50, 20), Text = "主题:" });
        _themeCombo = new ComboBox { Location = new Point(62, 125), Size = new Size(240, 24), DropDownStyle = ComboBoxStyle.DropDownList };
        _themeCombo.Items.AddRange(new object[] { "默认（分层玻璃）", "夜光琥珀 Nocturne", "清透玻璃 Quiet" });
        _themeCombo.SelectedIndexChanged += delegate
        {
            if (_loadingSliders) return;
            _cfg.Theme = _themeCombo.SelectedIndex == 1 ? "nocturne" : (_themeCombo.SelectedIndex == 2 ? "glassy" : "default");
            _cfg.Save();
            LiveUpdateAsync();
        };
        Controls.Add(_themeCombo);

        Controls.Add(new Label { Location = new Point(12, 152), Size = new Size(456, 18), ForeColor = Color.DimGray, Text = "滑块拖动即时生效于 ZCode（无需确认），松手后自动保存" });

        int y = 176;
        _opacity = MakeSlider(ref y, "不透明度", 0, 100, ref _opacityV);
        _brightness = MakeSlider(ref y, "亮度", 30, 180, ref _brightnessV);
        _saturation = MakeSlider(ref y, "饱和度", 0, 200, ref _saturationV);
        _contrast = MakeSlider(ref y, "对比度", 50, 150, ref _contrastV);
        _overlay = MakeSlider(ref y, "遮罩压暗", 0, 80, ref _overlayV);
        _blur = MakeSlider(ref y, "模糊 px", 0, 20, ref _blurV);
        _panelOpacity = MakeSlider(ref y, "面板玻璃", 0, 100, ref _panelOpacityV);
        _panelBlur = MakeSlider(ref y, "毛玻璃 px", 0, 30, ref _panelBlurV);
        BuildMusicUi();
        BuildRotateUi();
        BuildCustomUi();
        SetSlidersFromConfig();
    }

    private void BuildMusicUi()
    {
        Controls.Add(new Label { Location = new Point(12, 556), Size = new Size(60, 20), Text = "音乐文件:" });
        _musicPathBox = new TextBox { Location = new Point(76, 553), Size = new Size(290, 23), ReadOnly = true };
        _musicPathBox.Leave += delegate { _musicPathBox.Text = Util.NormalizePath(_musicPathBox.Text); };
        Controls.Add(_musicPathBox);
        var musicBrowse = new Button { Location = new Point(374, 552), Size = new Size(94, 25), Text = "浏览…" };
        musicBrowse.Click += delegate
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "音频|*.mp3;*.wav;*.ogg;*.m4a;*.flac|所有文件|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK) SetMusicFile(dlg.FileName);
            }
        };
        Controls.Add(musicBrowse);

        _musicPlayBtn = new Button { Location = new Point(12, 588), Size = new Size(110, 28), Text = "▶ 播放" };
        _musicPlayBtn.Click += delegate { ToggleMusic(); };
        Controls.Add(_musicPlayBtn);
        _musicClearBtn = new Button { Location = new Point(130, 588), Size = new Size(90, 28), Text = "清除" };
        _musicClearBtn.Click += delegate { ClearMusic(); };
        Controls.Add(_musicClearBtn);
        Controls.Add(new Label { Location = new Point(228, 594), Size = new Size(40, 20), Text = "音量" });
        _musicVolumeBar = new TrackBar { Location = new Point(268, 588), Size = new Size(120, 32), Minimum = 0, Maximum = 100, TickFrequency = 20 };
        _musicVolumeV = new Label { Location = new Point(396, 594), Size = new Size(50, 20), Text = "" };
        _musicVolumeBar.ValueChanged += delegate
        {
            if (_loadingSliders) return;
            _musicVolumeV.Text = _musicVolumeBar.Value.ToString();
            _cfg.MusicVolume = _musicVolumeBar.Value / 100.0;
            _cfg.Save();
            _throttle.Stop();
            _throttle.Start();
        };
        Controls.Add(_musicVolumeBar);
        Controls.Add(_musicVolumeV);
        _musicFollowChk = new CheckBox { Location = new Point(12, 622), Size = new Size(200, 22), Text = "跟随窗口（最小化暂停）" };
        _musicFollowChk.CheckedChanged += delegate
        {
            if (_loadingSliders) return;
            _cfg.MusicAutoplay = _musicFollowChk.Checked;
            _cfg.Save();
            LiveMusicAsync(-1); // 只同步 autoplay 语义, 不改变播放状态
        };
        Controls.Add(_musicFollowChk);
        _videoSoundChk = new CheckBox { Location = new Point(220, 622), Size = new Size(150, 22), Text = "视频自带声音" };
        _videoSoundChk.CheckedChanged += delegate
        {
            if (_loadingSliders) return;
            _cfg.VideoSound = _videoSoundChk.Checked;
            _cfg.Save();
            if (_cfg.VideoSound) { _musicPlaying = false; _musicPlayBtn.Text = "▶ 播放"; LiveMusicAsync(0); } // 互斥: 选视频声则暂停音乐
            _throttle.Stop();
            _throttle.Start();
        };
        Controls.Add(_videoSoundChk);
    }

    private void BuildRotateUi()
    {
        Controls.Add(new Label { Location = new Point(12, 662), Size = new Size(76, 20), Text = "轮播文件夹:" });
        _rotatePathBox = new TextBox { Location = new Point(88, 659), Size = new Size(210, 23), ReadOnly = true };
        Controls.Add(_rotatePathBox);
        var rotBrowse = new Button { Location = new Point(304, 658), Size = new Size(70, 25), Text = "浏览…" };
        rotBrowse.Click += delegate
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择壁纸轮播文件夹（png/jpg/jpeg/webp/gif）";
                if (dlg.ShowDialog(this) == DialogResult.OK) SetRotateFolder(dlg.SelectedPath);
            }
        };
        Controls.Add(rotBrowse);
        var rotStop = new Button { Location = new Point(380, 658), Size = new Size(88, 25), Text = "停止轮播" };
        rotStop.Click += delegate { StopRotate(); };
        Controls.Add(rotStop);

        Controls.Add(new Label { Location = new Point(12, 694), Size = new Size(40, 20), Text = "间隔" });
        _rotateIntervalCombo = new ComboBox { Location = new Point(52, 691), Size = new Size(90, 24), DropDownStyle = ComboBoxStyle.DropDownList };
        _rotateIntervalCombo.Items.AddRange(new object[] { "1 分钟", "5 分钟", "15 分钟", "30 分钟", "60 分钟" });
        _rotateIntervalCombo.SelectedIndexChanged += delegate
        {
            if (_loadingSliders) return;
            int[] mins = { 1, 5, 15, 30, 60 };
            if (_rotateIntervalCombo.SelectedIndex >= 0) { _cfg.RotateMinutes = mins[_rotateIntervalCombo.SelectedIndex]; _cfg.Save(); }
        };
        Controls.Add(_rotateIntervalCombo);
        Controls.Add(new Label { Location = new Point(160, 694), Size = new Size(40, 20), Text = "顺序" });
        _rotateOrderCombo = new ComboBox { Location = new Point(200, 691), Size = new Size(90, 24), DropDownStyle = ComboBoxStyle.DropDownList };
        _rotateOrderCombo.Items.AddRange(new object[] { "顺序", "随机" });
        _rotateOrderCombo.SelectedIndexChanged += delegate
        {
            if (_loadingSliders) return;
            _cfg.RotateOrder = _rotateOrderCombo.SelectedIndex == 1 ? "random" : "seq";
            _cfg.Save();
        };
        Controls.Add(_rotateOrderCombo);
    }

    private void BuildCustomUi()
    {
        Controls.Add(new Label { Location = new Point(12, 728), Size = new Size(56, 20), Text = "强调色" });
        _accentBtn = new Button { Location = new Point(68, 724), Size = new Size(90, 26), Text = "选择…" };
        _accentBtn.Click += delegate { PickColor("accent"); };
        Controls.Add(_accentBtn);
        Controls.Add(new Label { Location = new Point(180, 728), Size = new Size(56, 20), Text = "玻璃色" });
        _glassBtn = new Button { Location = new Point(236, 724), Size = new Size(90, 26), Text = "选择…" };
        _glassBtn.Click += delegate { PickColor("glass"); };
        Controls.Add(_glassBtn);
        var resetBtn = new Button { Location = new Point(344, 724), Size = new Size(124, 26), Text = "恢复默认配色" };
        resetBtn.Click += delegate { ResetColors(); };
        Controls.Add(resetBtn);
    }

    private async void SetRotateFolder(string rawPath)
    {
        string path = Util.NormalizePath(rawPath);
        if (!Directory.Exists(path)) { SetStatus("[X] 目录不存在: " + path); return; }
        var imgs = Watch.ListRotateImages(path);
        if (imgs.Count == 0) { SetStatus("[X] 目录里没有可用图片 (png/jpg/jpeg/webp/gif, ≤30MiB)"); return; }
        _cfg.RotateDir = path;
        _cfg.Save();
        _rotatePathBox.Text = path;
        Watch.RequestImmediateRotate();
        SetStatus("轮播已设置 (" + imgs.Count + " 张), 3 秒内换第一张");
    }

    private void StopRotate()
    {
        _cfg.RotateDir = "";
        _cfg.Save();
        _rotatePathBox.Text = "";
        SetStatus("轮播已停止（当前壁纸保留）");
    }

    private void PickColor(string which)
    {
        using (var dlg = new ColorDialog { FullOpen = true })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            string rgb = dlg.Color.R + "," + dlg.Color.G + "," + dlg.Color.B;
            if (which == "accent") _cfg.AccentColor = rgb; else _cfg.GlassColor = rgb;
            _cfg.Save();
            UpdateColorButtons();
            LiveUpdateAsync();
            SetStatus("配色已更新 ✓");
        }
    }

    private void ResetColors()
    {
        _cfg.AccentColor = "";
        _cfg.GlassColor = "";
        _cfg.Save();
        UpdateColorButtons();
        LiveUpdateAsync();
        SetStatus("已恢复默认配色 ✓");
    }

    private void UpdateColorButtons()
    {
        SetColorButton(_accentBtn, _cfg.AccentColor);
        SetColorButton(_glassBtn, _cfg.GlassColor);
    }

    private static void SetColorButton(Button b, string rgb)
    {
        string norm = Util.NormalizeRgb(rgb);
        if (norm != null)
        {
            var p = norm.Split(',');
            b.BackColor = Color.FromArgb(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]));
            b.UseVisualStyleBackColor = false;
            b.Text = "";
        }
        else { b.BackColor = SystemColors.Control; b.UseVisualStyleBackColor = true; b.Text = "选择…"; }
    }

    private string MusicCachePath()
    {
        if (string.IsNullOrEmpty(_cfg.MusicPath)) return "";
        return Path.Combine(Program.DataDir, "music" + Path.GetExtension(_cfg.MusicPath));
    }

    /// <summary>设置音乐文件: 缓存副本 + 保存 + 开播</summary>
    private async void SetMusicFile(string rawPath)
    {
        string path = Util.NormalizePath(rawPath);
        if (!File.Exists(path)) { SetStatus("[X] 音乐文件不存在: " + path); return; }
        try
        {
            string cached = Path.Combine(Program.DataDir, "music" + Path.GetExtension(path));
            File.Copy(path, cached, true);
            _cfg.MusicPath = path;
            _cfg.MusicVolume = _musicVolumeBar.Value / 100.0;
            _cfg.VideoSound = false; // 互斥: 用音乐则关视频声音
            _loadingSliders = true;
            _videoSoundChk.Checked = false;
            _loadingSliders = false;
            _cfg.Save();
            _musicPathBox.Text = path;
            _musicPlaying = true;
            _musicPlayBtn.Text = "⏸ 暂停";
            SetStatus("音乐已设置，播放中…");
            await LiveMusicAsync(1);
        }
        catch (Exception ex) { SetStatus("[X] " + ex.Message); }
    }

    private async void ToggleMusic()
    {
        if (string.IsNullOrEmpty(_cfg.MusicPath)) { SetStatus("[X] 请先选择音乐文件"); return; }
        _musicPlaying = !_musicPlaying;
        _musicPlayBtn.Text = _musicPlaying ? "⏸ 暂停" : "▶ 播放";
        if (_musicPlaying)
        {
            _cfg.VideoSound = false; // 互斥: 播放音乐则关视频声音
            _loadingSliders = true;
            _videoSoundChk.Checked = false;
            _loadingSliders = false;
            _cfg.Save();
        }
        await LiveMusicAsync(_musicPlaying ? 1 : 0);
    }

    private void ClearMusic()
    {
        _cfg.MusicPath = "";
        _cfg.Save();
        _musicPathBox.Text = "";
        _musicPlaying = false;
        _musicPlayBtn.Text = "▶ 播放";
        LiveMusicAsync(0, true);
        SetStatus("音乐已清除");
    }

    /// <summary>liveOnly=false 时用配置音量; clear=true 传 src=null 清掉页面音频元素</summary>
    private async Task LiveMusicAsync(int playing, bool clear = false)
    {
        try
        {
            if (!Injector.EndpointUp()) { SetStatus("ZCode 未连接"); return; }
            string src = clear ? null : (File.Exists(MusicCachePath()) ? Util.FileUrl(MusicCachePath()) : null);
            foreach (var t in Injector.GetTargets())
            {
                try { await Injector.SetMusicAsync(t.WsUrl, src, playing, _cfg.MusicVolume, _cfg.MusicAutoplay); } catch { }
            }
        }
        catch (Exception ex) { SetStatus("音乐控制失败: " + ex.Message); }
    }

    private TrackBar MakeSlider(ref int y, string name, int min, int max, ref Label valueLabel)
    {
        Controls.Add(new Label { Location = new Point(12, y + 4), Size = new Size(76, 20), Text = name });
        var bar = new TrackBar
        {
            Location = new Point(90, y),
            Size = new Size(320, 40),
            Minimum = min,
            Maximum = max,
            TickFrequency = (max - min) / 4,
            SmallChange = 1,
            LargeChange = 5
        };
        Label vl = new Label { Location = new Point(416, y + 4), Size = new Size(50, 20), Text = "" };
        valueLabel = vl;
        bar.ValueChanged += delegate
        {
            if (_loadingSliders) return;
            vl.Text = bar.Value.ToString();
            SyncSlidersToConfig();
            _throttle.Stop();
            _throttle.Start();
        };
        Controls.Add(bar);
        Controls.Add(vl);
        y += 46;
        return bar;
    }

    private void SyncSlidersToConfig()
    {
        _cfg.On = true;
        _cfg.Opacity = _opacity.Value / 100.0;
        _cfg.Brightness = _brightness.Value / 100.0;
        _cfg.Saturation = _saturation.Value / 100.0;
        _cfg.Contrast = _contrast.Value / 100.0;
        _cfg.Overlay = _overlay.Value / 100.0;
        _cfg.Blur = _blur.Value;
        _cfg.PanelOpacity = _panelOpacity.Value / 100.0;
        _cfg.PanelBlur = _panelBlur.Value;
    }

    private void SetSlidersFromConfig()
    {
        _loadingSliders = true;
        _opacity.Value = Clamp((int)Math.Round(_cfg.Opacity * 100), 0, 100);
        _brightness.Value = Clamp((int)Math.Round(_cfg.Brightness * 100), 30, 180);
        _saturation.Value = Clamp((int)Math.Round(_cfg.Saturation * 100), 0, 200);
        _contrast.Value = Clamp((int)Math.Round(_cfg.Contrast * 100), 50, 150);
        _overlay.Value = Clamp((int)Math.Round(_cfg.Overlay * 100), 0, 80);
        _blur.Value = Clamp((int)Math.Round(_cfg.Blur), 0, 20);
        _panelOpacity.Value = Clamp((int)Math.Round(_cfg.PanelOpacity * 100), 0, 100);
        _panelBlur.Value = Clamp((int)Math.Round(_cfg.PanelBlur), 0, 30);
        _opacityV.Text = _opacity.Value.ToString();
        _brightnessV.Text = _brightness.Value.ToString();
        _saturationV.Text = _saturation.Value.ToString();
        _contrastV.Text = _contrast.Value.ToString();
        _overlayV.Text = _overlay.Value.ToString();
        _blurV.Text = _blur.Value.ToString();
        _panelOpacityV.Text = _panelOpacity.Value.ToString();
        _panelBlurV.Text = _panelBlur.Value.ToString();
        _themeCombo.SelectedIndex = _cfg.Theme == "nocturne" ? 1 : (_cfg.Theme == "glassy" ? 2 : 0);
        _musicVolumeBar.Value = Clamp((int)Math.Round(_cfg.MusicVolume * 100), 0, 100);
        _musicVolumeV.Text = _musicVolumeBar.Value.ToString();
        _musicFollowChk.Checked = _cfg.MusicAutoplay;
        _videoSoundChk.Checked = _cfg.VideoSound;
        _musicPathBox.Text = _cfg.MusicPath;
        _musicPlaying = false;
        _musicPlayBtn.Text = "▶ 播放";
        _rotatePathBox.Text = _cfg.RotateDir;
        int[] rotMins = { 1, 5, 15, 30, 60 };
        int rotIdx = 2;
        for (int i = 0; i < rotMins.Length; i++) { if (Math.Abs(_cfg.RotateMinutes - rotMins[i]) < 0.5) rotIdx = i; }
        _rotateIntervalCombo.SelectedIndex = rotIdx;
        _rotateOrderCombo.SelectedIndex = _cfg.RotateOrder == "random" ? 1 : 0;
        UpdateColorButtons();
        _loadingSliders = false;
    }

    private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

    private void ThrottleTick(object sender, EventArgs e)
    {
        _throttle.Stop();
        _cfg.Save();
        LiveUpdateAsync();
    }

    private async void LiveUpdateAsync()
    {
        try
        {
            if (!Injector.EndpointUp()) { SetStatus("ZCode 未连接（需从带调试端口的快捷方式启动）"); return; }
            foreach (var t in Injector.GetTargets())
            {
                try
                {
                    string r = await Injector.LiveUpdateAsync(t.WsUrl, _cfg);
                    if (r == "NO") { SetStatus("当前窗口没有壁纸层，请先应用一张壁纸"); return; }
                }
                catch { }
            }
            SetStatus("参数已实时更新 ✓");
        }
        catch (Exception ex) { SetStatus("实时更新失败: " + ex.Message); }
    }

    private async void ApplyAsync()
    {
        _pathBox.Text = Util.NormalizePath(_pathBox.Text);
        SyncSlidersToConfig();
        _cfg.Save();
        SetStatus("应用中…");
        try
        {
            string err = await Cli.Apply(_pathBox.Text, null);
            SetStatus(err == null ? "应用成功 ✓" : "[X] " + err);
            if (err == null) RefreshStatusAsync();
        }
        catch (Exception ex) { SetStatus("[X] " + ex.Message); }
    }

    private async void ClearAsync()
    {
        SetStatus("清除中…");
        try
        {
            foreach (var t in Injector.GetTargets())
            {
                try { await Injector.ClearAsync(t.WsUrl); } catch { }
            }
            try { File.Delete(Config.PathOf); } catch { }
            _cfg = new Config();
            SetSlidersFromConfig();
            SetStatus("已清除，恢复官方原状 ✓");
        }
        catch (Exception ex) { SetStatus("[X] " + ex.Message); }
    }

    private async void ShotAsync()
    {
        try
        {
            var targets = Injector.GetTargets();
            if (targets.Count == 0) { SetStatus("[X] 无页面目标"); return; }
            byte[] png;
            using (var cdp = await Cdp.ConnectAsync(targets[0].WsUrl)) png = await cdp.ScreenshotAsync();
            string outPath = Path.Combine(Program.DataDir, "shot.png");
            File.WriteAllBytes(outPath, png);
            SetStatus("截图已保存: " + outPath);
        }
        catch (Exception ex) { SetStatus("[X] 截图失败: " + ex.Message); }
    }

    private async void RefreshStatusAsync()
    {
        try
        {
            if (!Injector.EndpointUp()) { SetStatus("ZCode 未连接（需从带调试端口的快捷方式启动）"); return; }
            var targets = Injector.GetTargets();
            string marker = "?";
            if (targets.Count > 0)
            {
                using (var cdp = await Cdp.ConnectAsync(targets[0].WsUrl))
                    marker = await cdp.EvaluateAsync("String(!!(window.__ZCWP && window.__ZCWP.on))");
            }
            bool hasCfg = _cfg.On && !string.IsNullOrEmpty(_cfg.CachedFile) && File.Exists(_cfg.CachedFile);
            SetStatus("ZCode 已连接 · " + targets.Count + " 个窗口 · " + (marker == "true" ? "壁纸挂载中" : "无壁纸") + " · 配置: " + (hasCfg ? _cfg.Type : "空"));
        }
        catch (Exception ex) { SetStatus("状态获取失败: " + ex.Message); }
    }

    private void SetStatus(string s)
    {
        try { if (IsHandleCreated) BeginInvoke((Action)(() => _status.Text = s)); } catch { }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_startHidden) Hide();
    }
}

// ============================ 安装/卸载 (快捷方式+自启+桌面入口, 全部内置) ============================
internal static class Setup
{
    internal const string Flag = "--remote-debugging-port=9335";
    internal const string DesktopLnkName = "换壁纸.lnk";
    internal const string StartupLnkName = "ZCode壁纸守护.lnk";

    private static List<string> CandidateDirs()
    {
        var dirs = new List<string>();
        Action<string> add = p => { if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) dirs.Add(p); };
        add(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) + "\\Programs");
        add(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) + "\\Programs");
        add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
        add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\OneDrive\\Desktop");
        add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\TaskBar");
        add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\Microsoft\\Internet Explorer\\Quick Launch\\User Pinned\\StartMenu");
        return dirs;
    }

    /// <summary>容错递归收集 .lnk（GetFiles AllDirectories 遇不可访问目录会整体抛异常导致漏扫）</summary>
    private static List<string> GetAllLnkFiles(string dir)
    {
        var result = new List<string>();
        try
        {
            foreach (var d in Directory.GetDirectories(dir))
            {
                try { result.AddRange(GetAllLnkFiles(d)); } catch { }
            }
            result.AddRange(Directory.GetFiles(dir, "*.lnk"));
        }
        catch { }
        return result;
    }

    /// <summary>找出所有指向 ZCode.exe 的快捷方式（动态 COM, 免外部脚本）</summary>
    public static List<string> FindZcodeLinks(Action<string> log)
    {
        var found = new List<string>();
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        foreach (var dir in CandidateDirs())
        {
            foreach (var f in GetAllLnkFiles(dir))
            {
                try
                {
                    dynamic sc = shell.CreateShortcut(f);
                    string tp = (string)sc.TargetPath;
                    if (!string.IsNullOrEmpty(tp) && tp.IndexOf("ZCode.exe", StringComparison.OrdinalIgnoreCase) >= 0 && !found.Contains(f))
                    {
                        found.Add(f);
                        if (log != null) log("  发现: " + f);
                    }
                }
                catch { }
            }
        }
        return found;
    }

    /// <summary>给所有 ZCode 快捷方式备份并追加调试端口参数</summary>
    public static string AddFlags()
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        int n = 0;
        foreach (var lnk in FindZcodeLinks(null))
        {
            var bak = lnk + ".zcwp.bak";
            if (!File.Exists(bak)) { try { File.Copy(lnk, bak); } catch { } }
            dynamic sc = shell.CreateShortcut(lnk);
            string args = (string)sc.Arguments;
            if (args == null || !args.Contains("--remote-debugging-port="))
            {
                sc.Arguments = (args + " " + Flag).Trim();
                sc.Save();
                n++;
            }
        }
        return n + " 个快捷方式已加调试端口 (备份 *.zcwp.bak)";
    }

    /// <summary>还原所有 ZCode 快捷方式 (优先从备份)</summary>
    public static string RemoveFlags()
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        int restored = 0, stripped = 0;
        foreach (var lnk in FindZcodeLinks(null))
        {
            var bak = lnk + ".zcwp.bak";
            if (File.Exists(bak))
            {
                File.Copy(bak, lnk, true);
                restored++;
            }
            else
            {
                dynamic sc = shell.CreateShortcut(lnk);
                string args = (string)sc.Arguments;
                if (args != null && args.Contains("--remote-debugging-port="))
                {
                    sc.Arguments = System.Text.RegularExpressions.Regex.Replace(args, "\\s*--remote-debugging-port=\\d+", "").Trim();
                    sc.Save();
                    stripped++;
                }
            }
        }
        return restored + " 个从备份还原, " + stripped + " 个去除端口参数";
    }

    public static string CreateDesktopLnk()
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        dynamic sc = shell.CreateShortcut(Path.Combine(desktop, DesktopLnkName));
        sc.TargetPath = Program.ExePath;
        sc.WorkingDirectory = Program.ExeDir;
        sc.Description = "ZCode 换壁纸 v3";
        sc.Save();
        return "桌面入口已创建";
    }

    public static void RemoveDesktopLnk()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        try { File.Delete(Path.Combine(desktop, DesktopLnkName)); } catch { }
    }

    public static string CreateStartupLnk()
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        dynamic sc = shell.CreateShortcut(Path.Combine(startup, StartupLnkName));
        sc.TargetPath = Program.ExePath;
        sc.Arguments = "--hidden";
        sc.WorkingDirectory = Program.ExeDir;
        sc.Description = "ZCode 壁纸守护 v3 (静默自启)";
        sc.Save();
        return "开机自启已配置";
    }

    public static void RemoveStartupLnk()
    {
        string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        try { File.Delete(Path.Combine(startup, StartupLnkName)); } catch { }
    }

    /// <summary>完整卸载: 清壁纸→还原快捷方式→删自启/桌面入口→清配置。log 为输出回调。</summary>
    public static void PerformUninstall(Action<string> log)
    {
        try
        {
            foreach (var t in Injector.GetTargets())
            {
                try { Injector.ClearAsync(t.WsUrl).Wait(); } catch { }
            }
            log("[2/4] 壁纸层已清除");
        }
        catch { log("[2/4] ZCode 未连接, 跳过壁纸清除 (不影响卸载)"); }
        log("[3/4] 还原 ZCode 快捷方式: " + RemoveFlags());
        RemoveStartupLnk();
        RemoveDesktopLnk();
        foreach (var f in new[] { "config.json", "watch.log", "cli-out.log", "shot.png" })
        {
            try { File.Delete(Path.Combine(Program.DataDir, f)); } catch { }
        }
        log("[4/4] 自启与配置已清理");
    }
}

// ============================ CLI ============================
internal static class Cli
{
    /// <summary>CLI 输出双写: Console(可能丢失, 取决于宿主) + data\cli-out.log(可靠)</summary>
    private static void Out(string s)
    {
        try { System.Console.WriteLine(s); } catch { }
        try { File.AppendAllText(Path.Combine(Program.DataDir, "cli-out.log"), s + Environment.NewLine); } catch { }
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "zcwp-cli.log"), s + Environment.NewLine); } catch { }
    }

    public static async Task Run(string[] args)
    {
        try { File.Delete(Path.Combine(Program.DataDir, "cli-out.log")); } catch { }
        string verb = args[0];
        try
        {
            if (verb == "argv") { Out("argc=" + args.Length + " argv=[" + string.Join(" | ", args) + "]"); return; }
            if (verb == "setup")
            {
                Out("[1/2] 快捷方式: " + Setup.AddFlags());
                Out("[2/2] " + Setup.CreateStartupLnk() + ", " + Setup.CreateDesktopLnk());
                Out("[OK] 安装配置完成。ZCode 下次启动将带调试端口, 守护自启生效。");
                return;
            }
            if (verb == "uninstall")
            {
                Out("[1/4] 通知运行中的守护退出…");
                try { using (var ev = System.Threading.EventWaitHandle.OpenExisting("ZCodeWallpaperExitEvent")) { ev.Set(); } } catch { }
                System.Threading.Thread.Sleep(1500);
                Setup.PerformUninstall(Out);
                Out("[OK] 已恢复官方原状。ZCode 下次启动不会再注入壁纸。");
                return;
            }
            if (verb == "music")
            {
                if (args.Length < 2) { Out("用法: ZCodeWallpaper.exe music <音频路径> [--volume 60]"); return; }
                string mp = Util.NormalizePath(args[1]);
                if (!File.Exists(mp)) { Out("[X] 音乐文件不存在: " + mp); Environment.ExitCode = 1; return; }
                string ext = Path.GetExtension(mp).ToLowerInvariant();
                if (!Util.IsAudio(ext)) { Out("[X] 不支持的音频格式 " + ext + "（mp3/wav/ogg/m4a/flac）"); Environment.ExitCode = 1; return; }
                var opts2 = ParseOpts(args, 2);
                string cached = Path.Combine(Program.DataDir, "music" + ext);
                File.Copy(mp, cached, true);
                var cfg2 = Config.Load() ?? new Config();
                cfg2.MusicPath = mp;
                cfg2.VideoSound = false; // 互斥: 用音乐则关视频声音
                double v;
                if (opts2.TryGetValue("volume", out v)) cfg2.MusicVolume = v / 100.0;
                cfg2.Save();
                // 全量重注入: 刷新注入层(含音频 onVis 跟随)并随配置创建 audio 播放
                string url2; bool video2;
                if (Watch.ResolveMediaUrl(cfg2, out url2, out video2))
                {
                    foreach (var t in Injector.GetTargets())
                    {
                        try { await Injector.InjectAsync(cfg2, t.WsUrl, url2, video2).ConfigureAwait(false); } catch { }
                    }
                }
                Out("[OK] 音乐已设置并播放 (" + (int)(cfg2.MusicVolume * 100) + "% 音量): " + mp);
                return;
            }
            if (verb == "music-off")
            {
                var cfgX = Config.Load();
                if (cfgX != null) { cfgX.MusicPath = ""; cfgX.Save(); }
                foreach (var t in Injector.GetTargets())
                {
                    try { await Injector.SetMusicAsync(t.WsUrl, null, 0, 0, true).ConfigureAwait(false); } catch { }
                }
                Out("[OK] 音乐已清除");
                return;
            }
            if (verb == "rotate")
            {
                if (args.Length < 2) { Out("用法: ZCodeWallpaper.exe rotate <文件夹> [--minutes 15] [--order seq|random]"); return; }
                string dir = Util.NormalizePath(args[1]);
                if (!Directory.Exists(dir)) { Out("[X] 目录不存在: " + dir); Environment.ExitCode = 1; return; }
                var imgs = Watch.ListRotateImages(dir);
                if (imgs.Count == 0) { Out("[X] 目录里没有可用图片 (png/jpg/jpeg/webp/gif, ≤30MiB)"); Environment.ExitCode = 1; return; }
                var cfgR = Config.Load() ?? new Config();
                cfgR.RotateDir = dir;
                var optsR = ParseOpts(args, 2);
                double mm;
                if (optsR.TryGetValue("minutes", out mm) && mm >= 1) cfgR.RotateMinutes = mm;
                for (int i = 2; i + 1 < args.Length; i++)
                {
                    if (args[i] == "--order")
                    {
                        string o = (args[i + 1] ?? "").Trim().ToLowerInvariant();
                        if (o == "random" || o == "seq") cfgR.RotateOrder = o;
                    }
                }
                cfgR.Save();
                try { using (var ev = System.Threading.EventWaitHandle.OpenExisting("ZCodeWallpaperRotateNow")) { ev.Set(); } } catch { }
                Out("[OK] 轮播已设置: " + imgs.Count + " 张, 每 " + cfgR.RotateMinutes + " 分钟 (" + cfgR.RotateOrder + ")。守护 3 秒内换第一张。");
                return;
            }
            if (verb == "rotate-off")
            {
                var cfgRo = Config.Load();
                if (cfgRo != null) { cfgRo.RotateDir = ""; cfgRo.Save(); }
                Out("[OK] 轮播已停止（当前壁纸保留）");
                return;
            }
            if (verb == "status") { await Status().ConfigureAwait(false); return; }
            if (verb == "clear")
            {
                foreach (var t in Injector.GetTargets())
                {
                    try { string r = await Injector.ClearAsync(t.WsUrl).ConfigureAwait(false); if (r == "CLEARED") Out("[OK] 已清除 " + (t.Title ?? t.Url)); }
                    catch { }
                }
                try { File.Delete(Config.PathOf); } catch { }
                Out("[OK] 配置已删除");
                return;
            }
            if (verb == "shot")
            {
                var targets = Injector.GetTargets();
                if (targets.Count == 0) { Out("[X] 无页面目标"); return; }
                byte[] png;
                using (var cdp = await Cdp.ConnectAsync(targets[0].WsUrl).ConfigureAwait(false))
                {
                    png = await cdp.ScreenshotAsync().ConfigureAwait(false);
                }
                string outPath = args.Length > 1 ? Util.NormalizePath(args[1]) : Path.Combine(Program.DataDir, "shot.png");
                File.WriteAllBytes(outPath, png);
                Out("[OK] 截图: " + outPath);
                return;
            }
            if (verb == "apply")
            {
                if (args.Length < 2) { Out("用法: ZCodeWallpaper.exe apply <图片或视频路径> [--opacity 55] [--brightness 100] [--saturation 100] [--contrast 100] [--overlay 30] [--blur 0]"); return; }
                string rawPath = args[1];
                var opts = ParseOpts(args, 2);
                string themeArg = null;
                for (int i = 2; i + 1 < args.Length; i++) { if (args[i] == "--theme") { themeArg = args[i + 1]; break; } }
                string videoSoundArg = null;
                for (int i = 2; i + 1 < args.Length; i++) { if (args[i] == "--video-sound") { videoSoundArg = args[i + 1]; break; } }
                string accentArg = null, glassArg = null;
                for (int i = 2; i + 1 < args.Length; i++)
                {
                    if (args[i] == "--accent") accentArg = args[i + 1];
                    if (args[i] == "--glass") glassArg = args[i + 1];
                }
                string err = await Apply(rawPath, opts, themeArg, videoSoundArg, null, accentArg, glassArg).ConfigureAwait(false);
                if (err != null) { Out("[X] " + err); Environment.ExitCode = 1; }
                return;
            }
            Out("未知命令: " + verb);
        }
        catch (Exception ex)
        {
            Out("[X] " + ex.Message);
            Environment.ExitCode = 1;
        }
    }

    public static Dictionary<string, double> ParseOpts(string[] args, int start)
    {
        var d = new Dictionary<string, double>();
        for (int i = start; i + 1 < args.Length; i += 2)
        {
            if (args[i].StartsWith("--"))
            {
                double v;
                if (double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                    d[args[i].TrimStart('-')] = v;
            }
        }
        return d;
    }

    /// <summary>核心入口：选文件→缓存→探测可用 URL→注入→存配置。GUI 与 CLI 共用。返回错误消息，null=成功。</summary>
    public static async Task<string> Apply(string rawPath, Dictionary<string, double> opts, string themeArg = null, string videoSoundArg = null, Action<int> progress = null, string accentArg = null, string glassArg = null)
    {
        string path = Util.NormalizePath(rawPath);
        if (!File.Exists(path)) return "文件不存在: " + path;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        string mime = Util.MimeFor(ext);
        if (mime == null) return "不支持的格式 " + ext + "（支持 png/jpg/webp/gif/mp4/webm）";
        long size = new FileInfo(path).Length;
        if (size > Util.MaxBytes) return "文件超过 200MiB 上限";

        if (!Injector.EndpointUp()) return "CDP 端点不可达 (" + Injector.Host + ") — ZCode 需要从带调试端口的快捷方式启动";
        var targets = Injector.GetTargets();
        if (targets.Count == 0) return "没有可注入的页面目标";

        bool video = Util.IsVideo(ext);
        if (progress != null) progress(1);

        // 缓存副本（源文件挪走也不怕；源=缓存时跳过）
        string cached = Path.Combine(Program.DataDir, "wallpaper" + ext);
        if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(cached), StringComparison.OrdinalIgnoreCase))
            File.Copy(path, cached, true);

        var cfg = Config.Load() ?? new Config();
        cfg.On = true;
        cfg.Type = video ? "video" : "image";
        cfg.SourcePath = path;
        cfg.CachedFile = cached;
        if (opts != null)
        {
            double v;
            if (opts.TryGetValue("opacity", out v)) cfg.Opacity = v / 100.0;
            if (opts.TryGetValue("brightness", out v)) cfg.Brightness = v / 100.0;
            if (opts.TryGetValue("saturation", out v)) cfg.Saturation = v / 100.0;
            if (opts.TryGetValue("contrast", out v)) cfg.Contrast = v / 100.0;
            if (opts.TryGetValue("overlay", out v)) cfg.Overlay = v / 100.0;
            if (opts.TryGetValue("blur", out v)) cfg.Blur = v;
            if (opts.TryGetValue("panel-opacity", out v)) cfg.PanelOpacity = v / 100.0;
            if (opts.TryGetValue("panel-blur", out v)) cfg.PanelBlur = v;
            if (!string.IsNullOrEmpty(themeArg))
            {
                string t = themeArg.Trim().ToLowerInvariant();
                if (t == "nocturne" || t == "glassy" || t == "default") cfg.Theme = t;
            }
            if (!string.IsNullOrEmpty(videoSoundArg))
            {
                string s = videoSoundArg.Trim().ToLowerInvariant();
                if (s == "on" || s == "true" || s == "1") cfg.VideoSound = true;
                else if (s == "off" || s == "false" || s == "0") cfg.VideoSound = false;
            }
            if (accentArg != null)
            {
                string a = accentArg.Trim().ToLowerInvariant();
                cfg.AccentColor = (a == "default" || a == "off") ? "" : (Util.NormalizeRgb(accentArg) ?? cfg.AccentColor);
            }
            if (glassArg != null)
            {
                string g = glassArg.Trim().ToLowerInvariant();
                cfg.GlassColor = (g == "default" || g == "off") ? "" : (Util.NormalizeRgb(glassArg) ?? cfg.GlassColor);
            }
        }

        // URL 模式降级链: 图片 data URL→file://→本地服务；视频 file://→本地服务
        string url;
        bool usedServer = false;
        if (!video)
        {
            url = "data:" + mime + ";base64," + Convert.ToBase64String(File.ReadAllBytes(cached));
            string r = await Injector.CheckMediaAsync(targets[0].WsUrl, url, false).ConfigureAwait(false);
            if (r != "OK")
            {
                url = Util.FileUrl(cached);
                r = await Injector.CheckMediaAsync(targets[0].WsUrl, url, false).ConfigureAwait(false);
                if (r != "OK") { url = StartServerUrl(cached, mime, out usedServer); }
            }
        }
        else
        {
            url = Util.FileUrl(cached);
            string r = await Injector.CheckMediaAsync(targets[0].WsUrl, url, true).ConfigureAwait(false);
            if (r != "OK") { url = StartServerUrl(cached, mime, out usedServer); }
        }
        if (progress != null) progress(2);

        int ok = 0;
        foreach (var t in targets)
        {
            try
            {
                string r = await Injector.InjectAsync(cfg, t.WsUrl, url, video).ConfigureAwait(false);
                if (r == "OK") ok++;
            }
            catch { }
        }
        cfg.Save();
        Out("[OK] 已注入 " + ok + "/" + targets.Count + " 窗口 (" + cfg.Type + ", " + (usedServer ? "本地服务" : url.StartsWith("data:") ? "data URL" : "file URL") + ")");
        return null;
    }

    private static MediaServer _server;

    private static string StartServerUrl(string cached, string mime, out bool used)
    {
        if (_server != null) _server.Dispose();
        _server = MediaServer.Start(cached, mime);
        used = true;
        return "http://127.0.0.1:" + _server.Port + "/wallpaper" + Path.GetExtension(cached);
    }

    private static async Task Status()
    {
        if (!Injector.EndpointUp())
        {
            Out("[X] CDP 端点不可达 (" + Injector.Host + ")");
            return;
        }
        Out("[OK] CDP 在线");
        var targets = Injector.GetTargets();
        Config cfg = Config.Load();
        Out("配置: " + (cfg == null ? "无" : "on=" + cfg.On + " type=" + cfg.Type + " src=" + cfg.SourcePath));
        if (cfg != null && !string.IsNullOrEmpty(cfg.RotateDir))
            Out("轮播: " + cfg.RotateDir + " / 每 " + cfg.RotateMinutes + " 分钟 / " + cfg.RotateOrder);
        foreach (var t in targets)
        {
            string marker;
            try
            {
                using (var cdp = await Cdp.ConnectAsync(t.WsUrl).ConfigureAwait(false))
                {
                    marker = await cdp.EvaluateAsync("String(!!(window.__ZCWP && window.__ZCWP.on))").ConfigureAwait(false);
                }
            }
            catch (Exception ex) { marker = "err:" + ex.Message; }
            Out("  [" + (marker == "true" ? "已挂壁纸" : marker == "false" ? "无壁纸  " : marker) + "] " + (t.Title ?? "") + "  " + t.Url);
        }
    }
}

