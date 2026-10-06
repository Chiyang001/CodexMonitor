using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

// A separate native child window keeps Explorer and the Electron renderer isolated.
public class TaskbarText : Form {
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    delegate bool EnumCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    readonly int ownerPid;
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    IntPtr taskbar;
    string five = "—", week = "—";
    bool enabled, dark = true, singleLine;
    float fontSize = 12;
    string layout = "two";
    int offset = 340;
    public TaskbarText(int pid) {
        ownerPid = pid; Text = "Codex Monitor Taskbar Quota";
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None; BackColor = Color.Magenta; TransparencyKey = Color.Magenta;
        DoubleBuffered = true; Font = new Font("Segoe UI", 9f);
        timer.Interval = 750; timer.Tick += delegate { TickHost(); }; timer.Start();
        MouseClick += delegate(object s, MouseEventArgs e) { if(e.Button == MouseButtons.Right) Emit("menu"); };
        MouseDoubleClick += delegate { Emit("settings"); };
    }
    void Emit(string action) { Console.WriteLine("{\"action\":\"" + action + "\"}"); Console.Out.Flush(); }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var p=base.CreateParams; p.ExStyle |= 0x08000080; return p; } }
    protected override void OnLoad(EventArgs e) {
        base.OnLoad(e); Hide();
        new Thread(delegate() {
            string line;
            try { while((line=Console.ReadLine()) != null) {
                var data=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(line);
                BeginInvoke((Action)delegate {
                    enabled=Convert.ToBoolean(data["enabled"]); five=Convert.ToString(data["five"]); week=Convert.ToString(data["week"]);
                    offset=Convert.ToInt32(data["offset"]); dark=Convert.ToBoolean(data["dark"]); fontSize=Convert.ToSingle(data["fontSize"]); layout=Convert.ToString(data["layout"]); TickHost(); Invalidate();
                });
            } } catch { }
            try { BeginInvoke((Action)delegate { Close(); }); } catch { }
        }) { IsBackground=true }.Start();
    }
    void TickHost() {
        try { if(Process.GetProcessById(ownerPid).HasExited) { Close(); return; } } catch { Close(); return; }
        if(!enabled) { Hide(); return; }
        var parent=FindWindow("Shell_TrayWnd",null);
        if(parent==IntPtr.Zero) { Hide(); return; }
        if(taskbar!=parent || GetParent(Handle)!=parent) {
            Hide(); int style=GetWindowLong(Handle,-16);
            SetWindowLong(Handle,-16,(style & unchecked((int)~0x80000000)) | 0x40000000);
            SetParent(Handle,parent); taskbar=parent;
        }
        if(GetParent(Handle)!=parent) { Hide(); Console.WriteLine("{\"error\":\"Cannot attach taskbar window\"}"); return; }
        Rect area; GetClientRect(parent,out area);
        int h=area.Bottom-area.Top, w=area.Right-area.Left;
        // Horizontal taskbars only: do not cover a vertical taskbar's buttons.
        if(h>150 || w<350) { Hide(); return; }
        float scale=Math.Max(1,GetDpiForWindow(parent)/96f);
        if(Math.Abs(Font.Size - fontSize*scale)>.1f) { var old=Font; Font=new Font("Segoe UI",fontSize*scale,GraphicsUnit.Pixel); old.Dispose(); } int panelWidth, panelHeight=h;
        using(var g=CreateGraphics()) {
            singleLine=layout=="one";
            if(!singleLine && Font.GetHeight(g)*2+4>h) {
                var old=Font; Font=new Font("Segoe UI",Font.Size*(h-4)/(Font.GetHeight(g)*2),GraphicsUnit.Pixel); old.Dispose();
            }
            var text=singleLine ? "5h 剩余 " + five + "   每周剩余 " + week : "每周剩余  " + week;
            panelWidth=(int)Math.Ceiling(g.MeasureString(text,Font).Width + 12*scale);
        }
        panelWidth=Math.Min(panelWidth,w);
        int x=Math.Max(0,Math.Min((int)(offset*scale),w-panelWidth));
        SetWindowPos(Handle,new IntPtr(0),x,(h-panelHeight)/2,panelWidth,panelHeight,0x0010|0x0040);
        if(!Visible) Show();
        Console.WriteLine("{\"attached\":true,\"x\":"+x+",\"width\":"+panelWidth+",\"height\":"+panelHeight+",\"fontSize\":"+fontSize+",\"layout\":\""+layout+"\"}"); Console.Out.Flush();
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); Color fg=dark ? Color.FromArgb(238,242,247) : Color.FromArgb(32,37,44);
        int line=ClientSize.Height/2;
        e.Graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
        using(var brush=new SolidBrush(fg)) using(var format=new StringFormat()) {
            format.LineAlignment=StringAlignment.Center;
            if(singleLine) { e.Graphics.DrawString("5h 剩余 " + five + "   每周剩余 " + week,Font,brush,new RectangleF(0,0,Width,Height),format); return; }
            e.Graphics.DrawString("5h 剩余   " + five,Font,brush,new RectangleF(0,0,Width,line),format);
            e.Graphics.DrawString("每周剩余  " + week,Font,brush,new RectangleF(0,line,Width,Height-line),format);
        }
    }
    [STAThread] public static void Main(string[] args) {
        SetProcessDPIAware(); Application.EnableVisualStyles();
        Application.Run(new TaskbarText(int.Parse(args[0])));
    }
}
