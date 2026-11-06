// Kraven mod installer for Spider-Man: The Movie (2002, Windows).
//
// Adds the Xbox-only Bronx Zoo levels (Kraven's Test, The Mighty Hunter) as
// new levels. Built by tools/installer/build_installer.py, which embeds the
// mod files as resources named "payload/<path relative to the game folder>".
//
// What it changes, each step backed up first into <game>\KravenMod\backup and
// listed in <game>\KravenMod\install.log (uninstall undoes exactly that list):
//   1. copies the new files (levels, movies, tips, voice lines, music);
//   2. data\COMMON.pkg: in SOUNDS\GLOBAL.SND switches on the five zoo
//      cut-scene dialogue lines (";audio\levels\m7bronxzoo\zoo_* ..." ->
//      "audio\levels\m7bronxzoo\zoo_* ... bink"). The new text is appended to
//      the package's data and the file record pointed at it; nothing else in
//      the package moves. Works on whichever COMMON.pkg is installed;
//   3. SpiderMan.exe: the zero-time-step physics fix (see pc_nan_guard.py).
//      The code is found by its bytes, not by address, so another build of
//      the exe with the same code (patch 1.3, community fixes) gets it too;
//      if the code is not found the exe is left alone and this is reported;
//   4. game.ini [Flags] BONUS_LEVELS_AVAILABLE=1 (the level-select gate), in
//      the game folder and in its VirtualStore copy, if present.
// It does not touch the community fix files (d3d8.dll, dxwrapper, Xidi...).
//
// Usage: KravenModInstaller.exe [install|uninstall] ["game folder"]
//
// Builder edition (KravenModBuilder.exe, build_builder.py): carries no game
// content, only an embeddable Python ("builder/python.zip") and the mod's
// tools ("builder/tools/*.py"). It asks for the user's own Xbox disc image,
// runs tools/make_mod.py in a temp folder (levels built against this very PC
// copy, sounds as .wav), then installs those files exactly as above. Its
// GLOBAL.SND edit also drops " bink" from the lines whose sound it made as
// .wav (make_mod's unbink.txt), and switches the zoo dialogue on as .wav.
//   KravenModBuilder.exe [install|uninstall] ["game folder"] ["disc image"]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

static class KravenModInstaller
{
    const string ModDir = "KravenMod";
    static string game, backup, logPath, iso, stage;
    static List<string> log = new List<string>();
    static readonly bool builder = Assembly.GetExecutingAssembly().GetManifestResourceInfo("builder/python.zip") != null;

#if GUI
    // Window version (KravenModBuilder.exe is built with /define:GUI): pick
    // or drop the game folder and the disc image, then Install / Uninstall.
    [STAThread]
    static int Main(string[] args)
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.Run(new Window(args));
        return 0;
    }
#else
    static int Main(string[] args)
    {
        return ConsoleMain(args);
    }
#endif

    // After game (and, for the builder, iso) are set: do it.
    static void Execute(string action)
    {
        backup = Path.Combine(game, ModDir, "backup");
        logPath = Path.Combine(game, ModDir, "install.log");
        bool installed = File.Exists(logPath);
        if (GameRunning())
            throw new Exception("Spider-Man is running from this folder. Close the game first.");
        try
        {
            if (action == "uninstall") Uninstall();
            else
            {
                if (builder && (iso == null || !IsXboxImage(iso))) throw new Exception("No Xbox disc image of Spider-Man.");
                if (installed) Uninstall();
                if (builder) Build();
                Install();
            }
        }
        finally
        {
            if (stage != null)
                try { Directory.Delete(stage, true); } catch (Exception) { }
            stage = null;
        }
    }

    static int ConsoleMain(string[] args)
    {
        Console.Title = builder ? "Kraven mod builder - Spider-Man (2002)" : "Kraven mod installer - Spider-Man (2002)";
        Console.WriteLine("Kraven mod for Spider-Man: The Movie (2002, Windows)");
        Console.WriteLine("Adds the Bronx Zoo levels: Kraven's Test, The Mighty Hunter.");
        if (builder)
            Console.WriteLine("Builds them from your own Xbox disc image of Spider-Man; no game files are included.");
        Console.WriteLine();
        string action = null;
        foreach (string a in args)
        {
            string l = a.ToLowerInvariant().TrimStart('-', '/');
            if (l == "install" || l == "uninstall") action = l;
            else if (Directory.Exists(a)) game = Path.GetFullPath(a);
            else if (File.Exists(a)) iso = Path.GetFullPath(a);
        }
        try
        {
            if (game == null) game = FindGame();
            if (game == null)
            {
                Console.Write("Game folder not found. Type the folder that holds SpiderMan.exe: ");
                string t = (Console.ReadLine() ?? "").Trim().Trim('"');
                if (File.Exists(Path.Combine(t, "SpiderMan.exe"))) game = Path.GetFullPath(t);
            }
            if (game == null) return Fail("No game folder with SpiderMan.exe.");
            Console.WriteLine("Game folder: " + game);
            backup = Path.Combine(game, ModDir, "backup");
            logPath = Path.Combine(game, ModDir, "install.log");
            bool installed = File.Exists(logPath);
            Console.WriteLine(installed ? "The Kraven mod is installed here." : "The Kraven mod is not installed here.");
            if (action == null)
            {
                Console.WriteLine();
                Console.WriteLine(installed ? "  [R] reinstall   [U] uninstall   [Q] quit" : "  [I] install   [Q] quit");
                Console.Write("> ");
                string k = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                if (k == "i" || k == "r") action = "install";
                else if (k == "u") action = "uninstall";
                else return 0;
            }
            if (action != "uninstall" && builder && (iso = AskIso()) == null) return Fail("No Xbox disc image of Spider-Man.");
            Execute(action);
        }
        catch (Exception e)
        {
            return Fail(e.Message);
        }
        Pause();
        return 0;
    }

#if GUI
    class Window : System.Windows.Forms.Form
    {
        readonly System.Windows.Forms.TextBox gameBox = new System.Windows.Forms.TextBox(), isoBox = new System.Windows.Forms.TextBox();
        readonly System.Windows.Forms.Label gameNote = new System.Windows.Forms.Label(), isoNote = new System.Windows.Forms.Label();
        readonly System.Windows.Forms.Button install = new System.Windows.Forms.Button(), uninstall = new System.Windows.Forms.Button();
        readonly System.Windows.Forms.TextBox output = new System.Windows.Forms.TextBox();

        public Window(string[] args)
        {
            Text = "Kraven mod for Spider-Man (2002)";
            Font = new System.Drawing.Font("Segoe UI", 9.5f);
            ClientSize = new System.Drawing.Size(640, 500);
            MinimumSize = new System.Drawing.Size(520, 420);
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            AllowDrop = true;
            var t = new System.Windows.Forms.TableLayoutPanel { Dock = System.Windows.Forms.DockStyle.Fill, Padding = new System.Windows.Forms.Padding(12), ColumnCount = 2 };
            t.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100));
            t.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            Controls.Add(t);
            var intro = new System.Windows.Forms.Label
            {
                AutoSize = true, MaximumSize = new System.Drawing.Size(600, 0), Margin = new System.Windows.Forms.Padding(0, 0, 0, 10),
                Text = "Adds the Bronx Zoo levels (Kraven's Test, The Mighty Hunter) to the Windows game. " +
                       (builder ? "They are built from your own Xbox disc image; no game files come with this program. " : "") +
                       "Browse for each item, or drag it onto this window."
            };
            t.Controls.Add(intro, 0, 0); t.SetColumnSpan(intro, 2);
            Row(t, 1, "Windows game folder (the one with SpiderMan.exe)", gameBox, gameNote, BrowseGame);
            if (builder) Row(t, 4, "Xbox disc image of Spider-Man (.iso / .xiso)", isoBox, isoNote, BrowseIso);
            var buttons = new System.Windows.Forms.FlowLayoutPanel { AutoSize = true, Margin = new System.Windows.Forms.Padding(0, 8, 0, 8) };
            install.Text = "Install"; install.AutoSize = true; install.Click += delegate { Go("install"); };
            uninstall.Text = "Uninstall"; uninstall.AutoSize = true; uninstall.Click += delegate { Go("uninstall"); };
            buttons.Controls.Add(install); buttons.Controls.Add(uninstall);
            t.Controls.Add(buttons, 0, 7); t.SetColumnSpan(buttons, 2);
            output.Multiline = true; output.ReadOnly = true; output.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            output.Dock = System.Windows.Forms.DockStyle.Fill; output.Font = new System.Drawing.Font("Consolas", 9f);
            t.RowStyles.Clear();
            for (int i = 0; i < 8; i++) t.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            t.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100));
            t.Controls.Add(output, 0, 8); t.SetColumnSpan(output, 2);

            DragEnter += (s, e) => { if (e.Data.GetDataPresent(System.Windows.Forms.DataFormats.FileDrop)) e.Effect = System.Windows.Forms.DragDropEffects.Copy; };
            DragDrop += (s, e) => { foreach (string p in (string[])e.Data.GetData(System.Windows.Forms.DataFormats.FileDrop)) Take(p); };
            gameBox.TextChanged += delegate { Check(); };
            isoBox.TextChanged += delegate { Check(); };
            Console.SetOut(new BoxWriter(output));

            string autoAction = null;
            bool auto = false;
            foreach (string a in args)
            {
                if (a == "--auto") auto = true;
                else if (a == "install" || a == "uninstall") autoAction = a;
                else Take(a);
            }
            if (gameBox.Text.Length == 0) { string g = FindGame(); if (g != null) gameBox.Text = g; }
            if (builder && isoBox.Text.Length == 0) foreach (string f in FoundImages()) { isoBox.Text = f; break; }
            Check();
            if (auto && autoAction != null) Shown += delegate { Go(autoAction); };
        }

        // The window runs as the user (an elevated window takes no drops from
        // Explorer); it reopens itself elevated only when the folder needs it.
        static bool IsAdmin()
        {
            return new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        static bool CanWrite(string dir)
        {
            try
            {
                string p = Path.Combine(dir, ".kravenmod_write_test");
                File.WriteAllText(p, "");
                File.Delete(p);
                return true;
            }
            catch (Exception) { return false; }
        }

        bool Elevate(string action)
        {
            if (IsAdmin() || (CanWrite(game) && CanWrite(Path.Combine(game, "data")))) return false;
            string a = string.Format("--auto {0} \"{1}\"", action, game) + (builder ? string.Format(" \"{0}\"", iso) : "");
            try
            {
                Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, a) { Verb = "runas", UseShellExecute = true });
                Close();
            }
            catch (Exception)
            {
                System.Windows.Forms.MessageBox.Show(this, "Administrator rights are needed to change that folder.", Text);
            }
            return true;
        }

        void Row(System.Windows.Forms.TableLayoutPanel t, int row, string label, System.Windows.Forms.TextBox box,
                 System.Windows.Forms.Label note, EventHandler browse)
        {
            var l = new System.Windows.Forms.Label { Text = label, AutoSize = true, Margin = new System.Windows.Forms.Padding(0, 6, 0, 2) };
            t.Controls.Add(l, 0, row); t.SetColumnSpan(l, 2);
            box.Dock = System.Windows.Forms.DockStyle.Fill; box.AllowDrop = true;
            box.DragEnter += (s, e) => { if (e.Data.GetDataPresent(System.Windows.Forms.DataFormats.FileDrop)) e.Effect = System.Windows.Forms.DragDropEffects.Copy; };
            box.DragDrop += (s, e) => { foreach (string p in (string[])e.Data.GetData(System.Windows.Forms.DataFormats.FileDrop)) Take(p); };
            t.Controls.Add(box, 0, row + 1);
            var b = new System.Windows.Forms.Button { Text = "Browse...", AutoSize = true };
            b.Click += browse;
            t.Controls.Add(b, 1, row + 1);
            note.AutoSize = true; note.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            t.Controls.Add(note, 0, row + 2); t.SetColumnSpan(note, 2);
        }

        // a dropped / given path: a folder or SpiderMan.exe -> game; an image -> iso
        void Take(string p)
        {
            if (Directory.Exists(p)) gameBox.Text = p;
            else if (File.Exists(p) && Path.GetFileName(p).Equals("SpiderMan.exe", StringComparison.OrdinalIgnoreCase)) gameBox.Text = Path.GetDirectoryName(p);
            else if (File.Exists(p) && builder) isoBox.Text = p;
        }

        void BrowseGame(object s, EventArgs e)
        {
            using (var d = new System.Windows.Forms.OpenFileDialog { Title = "Find SpiderMan.exe in the game folder", Filter = "SpiderMan.exe|SpiderMan.exe|Programs (*.exe)|*.exe" })
                if (d.ShowDialog(this) == System.Windows.Forms.DialogResult.OK) Take(d.FileName);
        }

        void BrowseIso(object s, EventArgs e)
        {
            using (var d = new System.Windows.Forms.OpenFileDialog { Title = "Xbox disc image of Spider-Man", Filter = "Disc images (*.iso;*.xiso)|*.iso;*.xiso|All files (*.*)|*.*" })
                if (d.ShowDialog(this) == System.Windows.Forms.DialogResult.OK) Take(d.FileName);
        }

        bool GameOk() { string g = gameBox.Text.Trim(); return File.Exists(Path.Combine(g, "SpiderMan.exe")) && File.Exists(Path.Combine(g, @"data\COMMON.pkg")); }

        void Check()
        {
            bool g = GameOk();
            bool installed = g && File.Exists(Path.Combine(gameBox.Text.Trim(), ModDir, "install.log"));
            gameNote.Text = gameBox.Text.Trim().Length == 0 ? "" : !g ? "Not a Spider-Man (2002) Windows game folder." : installed ? "OK - the Kraven mod is installed here." : "OK";
            gameNote.ForeColor = g ? System.Drawing.Color.DarkGreen : System.Drawing.Color.Firebrick;
            bool i = !builder || IsXboxImage(isoBox.Text.Trim());
            if (builder)
            {
                isoNote.Text = isoBox.Text.Trim().Length == 0 ? "" : i ? "OK - an Xbox disc image" : "Not an Xbox disc image.";
                isoNote.ForeColor = i ? System.Drawing.Color.DarkGreen : System.Drawing.Color.Firebrick;
            }
            install.Text = installed ? "Reinstall" : "Install";
            install.Enabled = g && i;
            uninstall.Enabled = installed;
        }

        void Go(string action)
        {
            game = Path.GetFullPath(gameBox.Text.Trim());
            iso = builder ? Path.GetFullPath(isoBox.Text.Trim()) : null;
            if (Elevate(action)) return;
            install.Enabled = uninstall.Enabled = false;
            output.Clear();
            Console.WriteLine("Game folder: " + game);
            var th = new System.Threading.Thread(delegate ()
            {
                string err = null;
                try { Execute(action); }
                catch (Exception ex) { err = ex.Message; }
                BeginInvoke((Action)delegate
                {
                    if (err != null)
                    {
                        Console.WriteLine();
                        Console.WriteLine("ERROR: " + err);
                        System.Windows.Forms.MessageBox.Show(this, err, Text, System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    }
                    else
                        System.Windows.Forms.MessageBox.Show(this, action == "uninstall" ? "The Kraven mod was removed." : "The Kraven mod is installed.", Text);
                    Check();
                });
            });
            th.IsBackground = true;
            th.Start();
        }
    }

    // Console output into the window's text box, from any thread
    class BoxWriter : TextWriter
    {
        readonly System.Windows.Forms.TextBox box;
        public BoxWriter(System.Windows.Forms.TextBox b) { box = b; }
        public override Encoding Encoding { get { return Encoding.UTF8; } }
        public override void Write(char c) { Write(c.ToString()); }
        public override void Write(string s)
        {
            if (s == null) return;
            s = s.Replace("\r\n", "\n").Replace("\n", "\r\n");
            if (box.InvokeRequired) box.BeginInvoke((Action)delegate { box.AppendText(s); });
            else box.AppendText(s);
        }
        public override void WriteLine(string s) { Write(s + "\n"); }
        public override void WriteLine() { Write("\n"); }
    }
#endif

    static List<string> FoundImages()
    {
        var found = new List<string>();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (string d in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Path.Combine(home, "Desktop"),
                                     Path.Combine(home, "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) })
        {
            if (!Directory.Exists(d)) continue;
            foreach (string f in Directory.GetFiles(d, "*.iso"))
                if (!found.Contains(f) && Path.GetFileName(f).IndexOf("spider", StringComparison.OrdinalIgnoreCase) >= 0 && IsXboxImage(f))
                    found.Add(f);
        }
        return found;
    }

    // ---------------------------------------------------------------- builder

    static readonly string[] OffsetsXdvdfs = { "0", "18300000", "FD90000", "2080000" };

    static bool IsXboxImage(string path)
    {
        try
        {
            using (var f = File.OpenRead(path))
                foreach (string o in OffsetsXdvdfs)
                {
                    long at = Convert.ToInt64(o, 16) + 32 * 2048;
                    if (at + 20 > f.Length) continue;
                    f.Seek(at, SeekOrigin.Begin);
                    byte[] h = new byte[20];
                    f.Read(h, 0, 20);
                    if (Encoding.ASCII.GetString(h) == "MICROSOFT*XBOX*MEDIA") return true;
                }
        }
        catch (Exception) { }
        return false;
    }

    static string AskIso()
    {
        if (iso != null)
        {
            if (IsXboxImage(iso)) return iso;
            Console.WriteLine("  " + iso + " is not an Xbox disc image.");
        }
        // offer images found in the usual places
        var found = new List<string>();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (string d in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Path.Combine(home, "Desktop"),
                                     Path.Combine(home, "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) })
        {
            if (!Directory.Exists(d)) continue;
            foreach (string f in Directory.GetFiles(d, "*.iso"))
                if (!found.Contains(f) && Path.GetFileName(f).IndexOf("spider", StringComparison.OrdinalIgnoreCase) >= 0 && IsXboxImage(f))
                    found.Add(f);
        }
        Console.WriteLine();
        Console.WriteLine("The mod is built from your own Xbox disc image of Spider-Man (an .iso / .xiso).");
        for (int i = 0; i < found.Count; i++)
            Console.WriteLine(string.Format("  [{0}] {1}", i + 1, found[i]));
        Console.Write(found.Count > 0 ? "Pick a number, or type the image's path: " : "Type the image's path: ");
        string t = (Console.ReadLine() ?? "").Trim().Trim('"');
        int k;
        if (int.TryParse(t, out k) && k >= 1 && k <= found.Count) return found[k - 1];
        if (File.Exists(t) && IsXboxImage(t)) return Path.GetFullPath(t);
        if (File.Exists(t)) Console.WriteLine("  That file is not an Xbox disc image.");
        return null;
    }

    static void Build()
    {
        Console.WriteLine();
        Console.WriteLine("Building the mod from " + iso + " ...");
        stage = Path.Combine(Path.GetTempPath(), "KravenModBuild_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string py = Path.Combine(stage, "python"), tools = Path.Combine(stage, "tools");
        Directory.CreateDirectory(py);
        Directory.CreateDirectory(tools);
        string zip = Path.Combine(stage, "python.zip");
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("builder/python.zip"))
        using (FileStream f = File.Create(zip))
            s.CopyTo(f);
        UnzipTo(zip, py);
        foreach (string n in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            if (n.StartsWith("builder/tools/"))
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(n))
                using (FileStream f = File.Create(Path.Combine(tools, n.Substring("builder/tools/".Length))))
                    s.CopyTo(f);
        var psi = new ProcessStartInfo(Path.Combine(py, "python.exe"),
            string.Format("-u \"{0}\" \"{1}\" \"{2}\" \"{3}\"", Path.Combine(tools, "make_mod.py"), iso, game, Path.Combine(stage, "out")));
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.WorkingDirectory = stage;
        var tail = new List<string>();
        using (Process p = Process.Start(psi))
        {
            p.ErrorDataReceived += (o, e) => { if (e.Data != null) lock (tail) tail.Add(e.Data); };
            p.BeginErrorReadLine();
            string line;
            while ((line = p.StandardOutput.ReadLine()) != null)
            {
                if (line.StartsWith(">> ")) Console.WriteLine("  " + line.Substring(3));
                lock (tail) { tail.Add(line); if (tail.Count > 40) tail.RemoveAt(0); }
            }
            p.WaitForExit();
            if (p.ExitCode != 0)
            {
                Console.WriteLine();
                lock (tail) foreach (string l in tail) Console.WriteLine("    " + l);
                throw new Exception("building the mod failed (details above).");
            }
        }
    }

    static void UnzipTo(string zip, string dir)
    {
        // needs /r:System.IO.Compression.FileSystem.dll /r:System.IO.Compression.dll (build_builder.py)
        System.IO.Compression.ZipFile.ExtractToDirectory(zip, dir);
        if (!File.Exists(Path.Combine(dir, "python.exe"))) throw new Exception("could not unpack the bundled Python.");
    }

    static bool GameRunning()
    {
        string exe = Path.Combine(game, "SpiderMan.exe");
        foreach (Process p in Process.GetProcessesByName("SpiderMan"))
        {
            try
            {
                if (string.Equals(Path.GetFullPath(p.MainModule.FileName), exe, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception)
            {
                return true;      // can't tell where it runs from: be safe
            }
        }
        return false;
    }

    static int Fail(string msg)
    {
        Console.WriteLine();
        Console.WriteLine("ERROR: " + msg);
        Pause();
        return 1;
    }

    static void Pause()
    {
        if (Environment.UserInteractive && !Console.IsInputRedirected)
        {
            Console.WriteLine();
            Console.WriteLine("Press Enter to close.");
            Console.ReadLine();
        }
    }

    static string FindGame()
    {
        var c = new List<string>();
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string here = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        c.Add(here);
        c.Add(Path.Combine(pf86, @"Activision\Spider-Man (tm) Movie"));
        c.Add(Path.Combine(pf, @"Activision\Spider-Man (tm) Movie"));
        foreach (string root in new[] { @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(root))
            {
                if (k == null) continue;
                foreach (string s in k.GetSubKeyNames())
                    using (var e = k.OpenSubKey(s))
                    {
                        string name = e == null ? null : e.GetValue("DisplayName") as string;
                        string loc = e == null ? null : e.GetValue("InstallLocation") as string;
                        if (name != null && loc != null && name.IndexOf("Spider-Man", StringComparison.OrdinalIgnoreCase) >= 0) c.Add(loc);
                    }
            }
        }
        foreach (string d in c)
            if (!string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, "SpiderMan.exe")) && File.Exists(Path.Combine(d, @"data\COMMON.pkg")))
                return Path.GetFullPath(d);
        return null;
    }

    // ---------------------------------------------------------------- install

    static void Install()
    {
        Console.WriteLine();
        Console.WriteLine("Installing...");
        CheckGame();
        if (!builder) ChooseVariant();
        Directory.CreateDirectory(backup);
        log.Clear();
        try
        {
            CopyPayload();
            PatchCommon();
            PatchExe();
            EnableLevels();
        }
        finally
        {
            File.WriteAllLines(logPath, log.ToArray());
        }
        Console.WriteLine();
        Console.WriteLine("Done. Start the game; the zoo levels are in the level select");
        Console.WriteLine("(Kraven's Test / The Mighty Hunter) and follow Escape From Oscorp.");
    }

    static void CheckGame()
    {
        byte[] exe = File.ReadAllBytes(Path.Combine(game, "SpiderMan.exe"));
        if (IndexOf(exe, Encoding.ASCII.GetBytes("bronxzoo_b"), 0) < 0 && IndexOf(exe, Encoding.ASCII.GetBytes("BRONXZOO_B"), 0) < 0
            && IndexOf(exe, Encoding.ASCII.GetBytes("bronxzoo\\bronxzoo_b"), 0) < 0)
        {
            // the level table spells it M7bronxzoo\bronxzoo_b
            if (IndexOfI(exe, "bronxzoo") < 0)
                throw new Exception("This SpiderMan.exe has no Bronx Zoo entry in its level table; it is not a build this mod supports.");
        }
    }

    // The level packages come in one version per game build ("variants"):
    // they carry that build's own pause/results/game-over menu code (see
    // pc_menus.py), so they must match the build's level scripts. Resource
    // "variants.txt" lists: name, MD5 of that build's M6OSCORP\OSCORP_B.SX
    // (the menu source), VA of its physics hook. Variant files are
    // "variant/<name>/<path>"; shared files "payload/<path>".
    static string variant;

    static void ChooseVariant()
    {
        var rows = new List<string[]>();
        using (var r = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("variants.txt")))
        {
            string line;
            while ((line = r.ReadLine()) != null)
                if (line.Trim().Length > 0) rows.Add(line.Split('\t'));
        }
        string sx = null;
        byte[] script = ReadPkgEntry(Path.Combine(game, @"data\levels\OSCORP_B.pkg"), @"M6OSCORP\", "OSCORP_B.SX");
        if (script != null) sx = Md5(script);
        uint hook = HookVa(File.ReadAllBytes(Path.Combine(game, "SpiderMan.exe")));
        foreach (string[] v in rows)
            if (sx != null && v[1] == sx) { variant = v[0]; Console.WriteLine("  game build: " + v[0] + " (matched by its level scripts)"); return; }
        foreach (string[] v in rows)
            if (hook != 0 && Convert.ToUInt32(v[2], 16) == hook) { variant = v[0]; Console.WriteLine("  game build: " + v[0] + " (matched by its exe)"); return; }
        variant = rows[0][0];
        Console.WriteLine("  warning: this game build is not one the mod was made for; using the " + variant + " levels.");
        Console.WriteLine("    The levels should load; if a menu misbehaves, that is the likely cause.");
    }

    // (source, path in the game folder); a source is a resource name, or
    // "file:" + a path (the builder's freshly made files)
    static IEnumerable<KeyValuePair<string, string>> Payload()
    {
        if (builder)
        {
            string files = Path.Combine(stage, "out", "files") + Path.DirectorySeparatorChar;
            foreach (string f in Directory.GetFiles(files, "*", SearchOption.AllDirectories))
                yield return new KeyValuePair<string, string>("file:" + f, f.Substring(files.Length));
            yield break;
        }
        string mine = "variant/" + variant + "/";
        foreach (string n in Assembly.GetExecutingAssembly().GetManifestResourceNames())
        {
            if (n.StartsWith("payload/")) yield return new KeyValuePair<string, string>(n, n.Substring(8));
            else if (n.StartsWith(mine)) yield return new KeyValuePair<string, string>(n, n.Substring(mine.Length));
        }
    }

    static string Md5(byte[] b)
    {
        using (var m = System.Security.Cryptography.MD5.Create())
            return BitConverter.ToString(m.ComputeHash(b)).Replace("-", "").ToLowerInvariant();
    }

    static byte[] ReadPkgEntry(string path, string dir, string name)
    {
        if (!File.Exists(path)) return null;
        byte[] d = File.ReadAllBytes(path);
        uint ndir = BitConverter.ToUInt32(d, 0), nfile = BitConverter.ToUInt32(d, 4), dsize = BitConverter.ToUInt32(d, 8);
        int at = 12;
        var inDir = new HashSet<uint>();
        for (int i = 0; i < ndir; i++)
        {
            string dn = CStr(d, at, 0x40);
            uint n = BitConverter.ToUInt32(d, at + 0x40);
            at += 0x44;
            for (int k = 0; k < n; k++)
                if (dn.Equals(dir, StringComparison.OrdinalIgnoreCase)) inDir.Add(BitConverter.ToUInt32(d, at + 4 * k));
            at += 4 * (int)n;
        }
        long dataBase = d.LongLength - dsize;
        for (int i = 0; i < nfile; i++)
        {
            int r = at + 0x30 * i;
            if (!inDir.Contains((uint)i) || !CStr(d, r, 0x28).Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            uint size = BitConverter.ToUInt32(d, r + 0x28), off = BitConverter.ToUInt32(d, r + 0x2C);
            byte[] o = new byte[size];
            Array.Copy(d, dataBase + off, o, 0, size);
            return o;
        }
        return null;
    }

    // VA of the physics hook, patched or not; 0 if not found
    static uint HookVa(byte[] d)
    {
        int pe = BitConverter.ToInt32(d, 0x3C);
        uint imageBase = BitConverter.ToUInt32(d, pe + 0x34);
        int nsec = BitConverter.ToUInt16(d, pe + 6), optSize = BitConverter.ToUInt16(d, pe + 20);
        for (int i = 0; i < nsec; i++)
        {
            int s = pe + 24 + optSize + 40 * i;
            if ((BitConverter.ToUInt32(d, s + 36) & 0x20000000) == 0) continue;
            uint textVa = BitConverter.ToUInt32(d, s + 12);
            int textLen = BitConverter.ToInt32(d, s + 16), textOff = BitConverter.ToInt32(d, s + 20);
            int[] tail = Tail(Sig, 6);
            for (int o = textOff; o + Sig.Length <= textOff + textLen; o++)
                if (Match(d, o + 6, tail) && (Match(d, o, Sig) || d[o] == 0xE9))
                    return imageBase + textVa + (uint)(o - textOff);
            return 0;
        }
        return 0;
    }

    static void CopyPayload()
    {
        int added = 0, replaced = 0;
        foreach (var p in Payload())
        {
            string res = p.Key;
            string rel = p.Value.Replace('/', '\\');
            string dst = Path.Combine(game, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            if (File.Exists(dst) && SameAsResource(dst, res))
            {
                // already the mod's own file (an earlier or manual install):
                // nothing to back up, and uninstall removes it
                log.Add("ADD\t" + rel);
                added++;
                continue;
            }
            if (File.Exists(dst))
            {
                Backup(rel);
                log.Add("REPLACE\t" + rel);
                replaced++;
            }
            else
            {
                log.Add("ADD\t" + rel);
                added++;
            }
            using (Stream s = OpenSource(res))
            using (FileStream f = File.Create(dst))
                s.CopyTo(f);
        }
        Console.WriteLine(string.Format("  files: {0} added, {1} replaced (originals backed up)", added, replaced));
    }

    static Stream OpenSource(string res)
    {
        return res.StartsWith("file:") ? (Stream)File.OpenRead(res.Substring(5))
                                       : Assembly.GetExecutingAssembly().GetManifestResourceStream(res);
    }

    static bool SameAsResource(string path, string res)
    {
        using (Stream a = OpenSource(res))
        using (FileStream b = File.OpenRead(path))
        {
            if (a.Length != b.Length) return false;
            byte[] x = new byte[1 << 16], y = new byte[1 << 16];
            while (true)
            {
                int n = a.Read(x, 0, x.Length);
                if (n == 0) return true;
                int m = 0;
                while (m < n)
                {
                    int k = b.Read(y, m, n - m);
                    if (k == 0) return false;
                    m += k;
                }
                for (int i = 0; i < n; i++)
                    if (x[i] != y[i]) return false;
            }
        }
    }

    static void Backup(string rel)
    {
        string b = Path.Combine(backup, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(b));
        if (!File.Exists(b)) File.Copy(Path.Combine(game, rel), b);
    }

    // data\COMMON.pkg: u32 dirs, files, data_size; dirs { name[0x40], u32 n,
    // n x u32 index }; files { name[0x28], u32 size, u32 offset }; data = the
    // last data_size bytes, offsets from its start.
    static readonly Regex ZooLine = new Regex(@"^;?(audio\\levels\\m7bronxzoo\\zoo_[bc]_\w+[ \t]+voice[ \t]+cd[ \t]+volume[ \t]+\d+)[ \t]*(bink)?[ \t]*(?=\r?$)",
                                             RegexOptions.IgnoreCase | RegexOptions.Multiline);

    static void PatchCommon()
    {
        string rel = @"data\COMMON.pkg";
        string path = Path.Combine(game, rel);
        byte[] d = File.ReadAllBytes(path);
        uint ndir = BitConverter.ToUInt32(d, 0), nfile = BitConverter.ToUInt32(d, 4), dsize = BitConverter.ToUInt32(d, 8);
        int at = 12;
        var sounds = new HashSet<uint>();
        for (int i = 0; i < ndir; i++)
        {
            string dn = CStr(d, at, 0x40);
            uint n = BitConverter.ToUInt32(d, at + 0x40);
            at += 0x44;
            for (int k = 0; k < n; k++)
                if (dn.Equals(@"SOUNDS\", StringComparison.OrdinalIgnoreCase)) sounds.Add(BitConverter.ToUInt32(d, at + 4 * k));
            at += 4 * (int)n;
        }
        int recs = at;
        long dataBase = d.LongLength - dsize;
        if (dataBase != recs + 0x30L * nfile) throw new Exception("COMMON.pkg: unexpected layout.");
        int rec = -1;
        for (int i = 0; i < nfile; i++)
            if (sounds.Contains((uint)i) && CStr(d, recs + 0x30 * i, 0x28).Equals("GLOBAL.SND", StringComparison.OrdinalIgnoreCase)) rec = recs + 0x30 * i;
        if (rec < 0) throw new Exception(@"COMMON.pkg has no SOUNDS\GLOBAL.SND.");
        uint size = BitConverter.ToUInt32(d, rec + 0x28), off = BitConverter.ToUInt32(d, rec + 0x2C);
        string text = Encoding.GetEncoding(1252).GetString(d, (int)(dataBase + off), (int)size);
        if (text.IndexOf(@"characters\kraven", StringComparison.OrdinalIgnoreCase) < 0)
            Console.WriteLine(@"  warning: GLOBAL.SND declares no Kraven voice lines; Kraven may be silent.");
        int changed = 0;
        // prebuilt installer: the zoo dialogue ships as Bink; builder: as .wav
        string nt = ZooLine.Replace(text, delegate (Match m)
        {
            string want = m.Groups[1].Value + (builder ? "" : " bink");
            if (m.Value == want) return m.Value;
            changed++;
            return want;
        });
        if (builder)
        {
            // every sound the builder made as .wav although declared bink
            foreach (string p in File.ReadAllLines(Path.Combine(stage, "out", "unbink.txt"), Encoding.GetEncoding(1252)))
            {
                if (p.Trim().Length == 0) continue;
                var line = new Regex(@"^(" + Regex.Escape(p.Trim()) + @"[ \t][^\r\n]*?)[ \t]+bink[ \t]*(?=\r?$)",
                                     RegexOptions.IgnoreCase | RegexOptions.Multiline);
                nt = line.Replace(nt, delegate (Match m) { changed++; return m.Groups[1].Value; });
            }
        }
        if (changed == 0)
        {
            Console.WriteLine("  COMMON.pkg: zoo dialogue already switched on");
            return;
        }
        Backup(rel);
        byte[] add = Encoding.GetEncoding(1252).GetBytes(nt);
        int pad = (int)((4 - dsize % 4) % 4);
        uint newOff = dsize + (uint)pad;
        using (var f = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
        {
            f.Seek(0, SeekOrigin.End);
            f.Write(new byte[pad], 0, pad);
            f.Write(add, 0, add.Length);
            // every record that pointed at the old text (aliases) moves too
            for (int i = 0; i < nfile; i++)
            {
                int r = recs + 0x30 * i;
                if (BitConverter.ToUInt32(d, r + 0x28) == size && BitConverter.ToUInt32(d, r + 0x2C) == off)
                {
                    f.Seek(r + 0x28, SeekOrigin.Begin);
                    f.Write(BitConverter.GetBytes((uint)add.Length), 0, 4);
                    f.Write(BitConverter.GetBytes(newOff), 0, 4);
                }
            }
            f.Seek(8, SeekOrigin.Begin);
            f.Write(BitConverter.GetBytes(newOff + (uint)add.Length), 0, 4);
        }
        log.Add("PATCH\t" + rel);
        Console.WriteLine(builder
            ? string.Format("  COMMON.pkg: {0} sound lines pointed at the new .wav files (zoo dialogue switched on)", changed)
            : string.Format("  COMMON.pkg: {0} zoo dialogue lines switched on", changed));
    }

    // SpiderMan.exe: FUN_004C5060 (the per-entity physics step) rebuilds the
    // velocity as displacement * (1/dt); with dt = 0 that is NaN, which in the
    // zoo left Spider-Man's height NaN (every trap fired, the level ended at
    // once, Kraven's top half vanished). The fld 1.0 before the fdiv becomes a
    // jump to a cave in int3 padding: if dt is +-0 skip to just after
    // "velocity = ...", else do the fld and go on.
    static readonly int[] Sig = Hex(
        "d9 05 ?? ?? ?? ?? d8 b4 24 14 01 00 00 83 ec 0c 8d 4c 24 2c d9 44 24 24 d8 c9 d9 5c 24 08 d9 44 24 20 d8 c9" +
        " d9 5c 24 04 d8 4c 24 1c d9 1c 24 e8 ?? ?? ?? ?? d9 44 24 34 d8 44 24 28 83 ec 0c 8d 4c 24 38 d9 5c 24 08" +
        " d9 44 24 3c d8 44 24 30 d9 5c 24 04 d9 44 24 38 d8 44 24 2c d9 1c 24 e8 ?? ?? ?? ?? 8d 44 24 2c 8b cd 50" +
        " e8 ?? ?? ?? ??");
    static readonly byte[] CaveHead = { 0x8b, 0x84, 0x24, 0x14, 0x01, 0x00, 0x00, 0xa9, 0xff, 0xff, 0xff, 0x7f, 0x0f, 0x84 };

    static void PatchExe()
    {
        string rel = "SpiderMan.exe";
        string path = Path.Combine(game, rel);
        byte[] d = File.ReadAllBytes(path);
        int pe = BitConverter.ToInt32(d, 0x3C);
        uint imageBase = BitConverter.ToUInt32(d, pe + 0x34);
        int nsec = BitConverter.ToUInt16(d, pe + 6), optSize = BitConverter.ToUInt16(d, pe + 20);
        int textOff = 0, textLen = 0;
        uint textVa = 0;
        for (int i = 0; i < nsec; i++)
        {
            int s = pe + 24 + optSize + 40 * i;
            uint chr = BitConverter.ToUInt32(d, s + 36);
            if ((chr & 0x20000000) != 0 && textLen == 0)   // first executable section
            {
                textVa = BitConverter.ToUInt32(d, s + 12);
                textLen = BitConverter.ToInt32(d, s + 16);
                textOff = BitConverter.ToInt32(d, s + 20);
            }
        }
        Func<int, uint> va = o => imageBase + textVa + (uint)(o - textOff);
        // already patched? (a jmp whose target starts with the cave code)
        var hits = new List<int>();
        for (int o = textOff; o + Sig.Length <= textOff + textLen; o++)
            if (Match(d, o, Sig)) hits.Add(o);
        if (hits.Count == 0)
        {
            for (int o = textOff; o + 6 <= textOff + textLen; o++)
            {
                if (d[o] != 0xE9 || d[o + 5] != 0x90) continue;
                long t = o + 5 + BitConverter.ToInt32(d, o + 1);
                if (t > 0 && t + CaveHead.Length < d.Length && Match(d, (int)t, CaveHead) && Match(d, o + 6, Tail(Sig, 6)))
                {
                    Console.WriteLine("  SpiderMan.exe: physics fix already present");
                    return;
                }
            }
            Console.WriteLine("  SpiderMan.exe: physics code not recognised in this build; left unchanged.");
            Console.WriteLine("    (Without it Spider-Man can glitch at the zoo's start: traps firing, instant mission complete.)");
            return;
        }
        if (hits.Count > 1) { Console.WriteLine("  SpiderMan.exe: physics code found more than once; left unchanged."); return; }
        int hook = hits[0];
        int resume = hook + 6, skip = hook + Sig.Length;
        byte[] orig = new byte[6];
        Array.Copy(d, hook, orig, 0, 6);
        // cave: 64 bytes of int3 padding, 16-aligned, after a 16-byte guard
        int cave = -1;
        for (int o = textOff; o + 96 <= textOff + textLen && cave < 0; o++)
        {
            if (d[o] != 0xCC) continue;
            int run = 0;
            while (o + run < textOff + textLen && d[o + run] == 0xCC) run++;
            int start = (o + 16 + 15) & ~15;
            if (start + 48 <= o + run) cave = start;
            o += run;
        }
        if (cave < 0) { Console.WriteLine("  SpiderMan.exe: no free padding for the physics fix; left unchanged."); return; }
        var c = new List<byte>(CaveHead);
        int jz = c.Count; c.AddRange(new byte[4]);
        c.AddRange(orig);
        int jmp = c.Count; c.Add(0xE9); c.AddRange(new byte[4]);
        byte[] cb = c.ToArray();
        Array.Copy(BitConverter.GetBytes(skip - (cave + jz + 4)), 0, cb, jz, 4);
        Array.Copy(BitConverter.GetBytes(resume - (cave + jmp + 5)), 0, cb, jmp + 1, 4);
        Backup(rel);
        Array.Copy(cb, 0, d, cave, cb.Length);
        d[hook] = 0xE9;
        Array.Copy(BitConverter.GetBytes(cave - (hook + 5)), 0, d, hook + 1, 4);
        d[hook + 5] = 0x90;
        File.WriteAllBytes(path, d);
        log.Add("PATCH\t" + rel);
        Console.WriteLine(string.Format("  SpiderMan.exe: physics fix applied at {0:X} (cave {1:X})", va(hook), va(cave)));
    }

    static void EnableLevels()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string vs = Path.Combine(local, "VirtualStore", game.Substring(Path.GetPathRoot(game).Length));
        foreach (string dir in new[] { game, vs })
        {
            string p = Path.Combine(dir, "game.ini");
            if (!File.Exists(p)) continue;
            string t = File.ReadAllText(p, Encoding.GetEncoding(1252));
            Match sec = Regex.Match(t, @"(?im)^\[Flags\][ \t]*\r?$");
            if (!sec.Success) continue;
            int end = t.Length;
            Match next = Regex.Match(t.Substring(sec.Index + sec.Length), @"(?m)^\[");
            if (next.Success) end = sec.Index + sec.Length + next.Index;
            string body = t.Substring(sec.Index + sec.Length, end - sec.Index - sec.Length);
            Regex flag = new Regex(@"(?im)^[ \t]*BONUS_LEVELS_AVAILABLE[ \t]*=[^\r\n]*");
            string nb = flag.IsMatch(body) ? flag.Replace(body, "BONUS_LEVELS_AVAILABLE=1")
                                           : (t.Contains("\r\n") ? "\r\n" : "\n") + "BONUS_LEVELS_AVAILABLE=1" + body;
            string nt = t.Substring(0, sec.Index + sec.Length) + nb + t.Substring(end);
            if (nt == t) continue;
            string rel = dir == game ? "game.ini" : null;
            if (rel != null) { Backup(rel); log.Add("PATCH\tgame.ini"); }
            else
            {
                string b = Path.Combine(backup, "VirtualStore-game.ini");
                if (!File.Exists(b)) File.Copy(p, b);
                log.Add("VSPATCH\t" + p);
            }
            File.WriteAllText(p, nt, Encoding.GetEncoding(1252));
            Console.WriteLine("  game.ini: level-select gate opened (" + (rel != null ? "game folder" : "VirtualStore") + ")");
        }
    }

    // -------------------------------------------------------------- uninstall

    static void Uninstall()
    {
        Console.WriteLine();
        Console.WriteLine("Removing the Kraven mod...");
        if (!File.Exists(logPath)) { Console.WriteLine("  nothing to remove"); return; }
        int n = 0;
        foreach (string line in File.ReadAllLines(logPath))
        {
            string[] p = line.Split('\t');
            if (p.Length != 2) continue;
            if (p[0] == "ADD")
            {
                string f = Path.Combine(game, p[1]);
                if (File.Exists(f)) { File.Delete(f); n++; }
            }
            else if (p[0] == "REPLACE" || p[0] == "PATCH")
            {
                string b = Path.Combine(backup, p[1]);
                if (File.Exists(b)) { File.Copy(b, Path.Combine(game, p[1]), true); n++; }
            }
            else if (p[0] == "VSPATCH")
            {
                string b = Path.Combine(backup, "VirtualStore-game.ini");
                if (File.Exists(b)) { File.Copy(b, p[1], true); n++; }
            }
        }
        Directory.Delete(Path.Combine(game, ModDir), true);
        Console.WriteLine(string.Format("  {0} files removed or restored", n));
    }

    // ---------------------------------------------------------------- helpers

    static string CStr(byte[] d, int at, int len)
    {
        int e = at;
        while (e < at + len && d[e] != 0) e++;
        return Encoding.GetEncoding(1252).GetString(d, at, e - at);
    }

    static int[] Hex(string s)
    {
        var r = new List<int>();
        foreach (string t in s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            r.Add(t == "??" ? -1 : Convert.ToInt32(t, 16));
        return r.ToArray();
    }

    static int[] Tail(int[] a, int from)
    {
        int[] r = new int[a.Length - from];
        Array.Copy(a, from, r, 0, r.Length);
        return r;
    }

    static bool Match(byte[] d, int at, int[] sig)
    {
        if (at < 0 || at + sig.Length > d.Length) return false;
        for (int i = 0; i < sig.Length; i++)
            if (sig[i] >= 0 && d[at + i] != sig[i]) return false;
        return true;
    }

    static bool Match(byte[] d, int at, byte[] sig)
    {
        if (at < 0 || at + sig.Length > d.Length) return false;
        for (int i = 0; i < sig.Length; i++)
            if (d[at + i] != sig[i]) return false;
        return true;
    }

    static int IndexOf(byte[] d, byte[] s, int from)
    {
        for (int i = from; i + s.Length <= d.Length; i++)
            if (Match(d, i, s)) return i;
        return -1;
    }

    static int IndexOfI(byte[] d, string s)
    {
        byte[] lo = Encoding.ASCII.GetBytes(s.ToLowerInvariant()), up = Encoding.ASCII.GetBytes(s.ToUpperInvariant());
        for (int i = 0; i + lo.Length <= d.Length; i++)
        {
            int k = 0;
            while (k < lo.Length && (d[i + k] == lo[k] || d[i + k] == up[k])) k++;
            if (k == lo.Length) return i;
        }
        return -1;
    }
}
