using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ThermoFisher.CommonCore.Data.Interfaces;
using ThermoFisher.CommonCore.RawFileReader;

// ========================================
// MethViewer - Thermo Fisher 方法文件查看器
// 支持 LC 方法 / MS 方法 / 原始数据 视图切换
// 自适应窗口，单文件部署
// ========================================

static class Program
{
    [STAThread]
    static void Main()
    {
        // 从嵌入资源加载 DLL（单文件部署核心技巧）
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string dllName = new AssemblyName(args.Name).Name + ".dll";
            string resName = "MethViewer." + dllName;

            Assembly current = Assembly.GetExecutingAssembly();
            using (Stream stream = current.GetManifestResourceStream(resName))
            {
                if (stream == null) return null;
                byte[] data = new byte[stream.Length];
                stream.Read(data, 0, data.Length);
                return Assembly.Load(data);
            }
        };

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

class MainForm : Form
{
    private Button btnOpen;
    private TextBox txtContent;
    private Label lblInfo;
    private ComboBox cboViewMode;
    private Label lblView;
    private Panel pnlTop;
    private Panel pnlInfoBar;
    private string currentFilePath = "";
    private IInstrumentMethodFileAccess currentMethod = null;
    private string rawMethodText = "";
    private string lcParsedText = "";
    private string msParsedText = "";

    public MainForm()
    {
        InitializeUI();
        // 从嵌入资源加载图标
        using (Stream iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MethViewer.MethViewer.ico"))
        {
            if (iconStream != null)
                this.Icon = new Icon(iconStream);
        }
    }

    void InitializeUI()
    {
        // 窗口设置
        this.Text = "MethViewer - Thermo Fisher Method File Viewer";
        this.Size = new Size(1000, 750);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.MinimumSize = new Size(700, 450);
        this.Font = new Font("Microsoft YaHei UI", 10F);

        // ===== 顶部面板（标题 + 按钮） =====
        pnlTop = new Panel
        {
            Dock = DockStyle.Top,
            Height = 70,
            Padding = new Padding(8, 6, 8, 4)
        };

        // 标题
        Label lblTitle = new Label
        {
            Text = "MethViewer - Thermo Fisher Method File Viewer",
            Font = new Font("Segoe UI", 13.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 90, 180),
            AutoSize = false,
            Size = new Size(480, 30),
            Location = new Point(10, 6),
            TextAlign = ContentAlignment.MiddleLeft
        };

        // 打开按钮（缩小字号）
        btnOpen = new Button
        {
            Text = "Open Method File",
            Size = new Size(145, 32),
            Location = new Point(500, 6),
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            BackColor = Color.FromArgb(0, 110, 200),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        btnOpen.FlatAppearance.BorderSize = 0;
        btnOpen.Click += BtnOpen_Click;

        // 退出按钮
        Button btnExit = new Button
        {
            Text = "Exit",
            Size = new Size(70, 32),
            Location = new Point(btnOpen.Right + 8, 6),
            Font = new Font("Segoe UI", 10.5F),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnExit.Click += (s, e) => Application.Exit();

        pnlTop.Controls.Add(lblTitle);
        pnlTop.Controls.Add(btnOpen);
        pnlTop.Controls.Add(btnExit);

        // ===== 信息栏（文件名 + 视图切换） =====
        pnlInfoBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = Color.FromArgb(242, 245, 250),
            Padding = new Padding(8, 0, 8, 0)
        };

        lblInfo = new Label
        {
            Text = "Ready - Click 'Open Method File' to begin",
            AutoSize = false,
            Size = new Size(400, 30),
            Location = new Point(10, 2),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Consolas", 9.5F),
            ForeColor = Color.FromArgb(100, 100, 100),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        // 视图模式选择
        lblView = new Label
        {
            Text = "View:",
            AutoSize = false,
            Size = new Size(48, 26),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            BackColor = Color.Green,
            ForeColor = Color.White
        };

        cboViewMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Size = new Size(180, 24),
            Font = new Font("Segoe UI", 10F),
            Enabled = false
        };
        cboViewMode.Items.AddRange(new string[] {
            "Raw Method Text",
            "LC Method (液相方法)",
            "MS Method (质谱方法)"
        });
        cboViewMode.SelectedIndex = 0;
        cboViewMode.SelectedIndexChanged += CboViewMode_Changed;

        pnlInfoBar.Controls.Add(lblInfo);
        pnlInfoBar.Controls.Add(lblView);
        pnlInfoBar.Controls.Add(cboViewMode);

        // 响应式布局：lblInfo 自适应宽度，lblView + cboViewMode 靠右对齐
        LayoutInfoBar();
        pnlInfoBar.Resize += (s, e) => LayoutInfoBar();

        // ===== 内容显示区域（填满剩余空间） =====
        txtContent = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 10F),
            BackColor = Color.White,
            ForeColor = Color.FromArgb(30, 30, 30),
            BorderStyle = BorderStyle.FixedSingle
        };
        // 允许拖放
        txtContent.AllowDrop = true;
        txtContent.DragEnter += TxtContent_DragEnter;
        txtContent.DragDrop += TxtContent_DragDrop;

        // 添加控件到表单
        this.Controls.Add(txtContent);
        this.Controls.Add(pnlInfoBar);
        this.Controls.Add(pnlTop);

        // 键盘快捷键
        this.KeyPreview = true;
        this.KeyDown += (s, e) =>
        {
            if (e.Control && e.KeyCode == Keys.O)
                BtnOpen_Click(null, null);
            if (e.KeyCode == Keys.Escape)
                Application.Exit();
        };
    }

    // ===== 拖放支持 =====
    private void TxtContent_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effect = DragDropEffects.Copy;
    }

    private void TxtContent_DragDrop(object sender, DragEventArgs e)
    {
        string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files != null && files.Length > 0)
            OpenMethodFile(files[0]);
    }

    // ===== 视图切换 =====
    private void CboViewMode_Changed(object sender, EventArgs e)
    {
        if (currentMethod == null) return;

        switch (cboViewMode.SelectedIndex)
        {
            case 0: txtContent.Text = rawMethodText; break;
            case 1: txtContent.Text = lcParsedText; break;
            case 2: txtContent.Text = msParsedText; break;
        }
        txtContent.SelectionStart = 0;
        txtContent.ScrollToCaret();
    }

    // ===== 打开文件 =====
    private void BtnOpen_Click(object sender, EventArgs e)
    {
        OpenFileDialog dlg = new OpenFileDialog
        {
            Title = "Select Thermo Fisher Method File",
            Filter = "Method Files (*.meth)|*.meth|All Files (*.*)|*.*",
            FilterIndex = 1,
            RestoreDirectory = true
        };

        if (dlg.ShowDialog() != DialogResult.OK)
            return;

        OpenMethodFile(dlg.FileName);
    }

    private void OpenMethodFile(string path)
    {
        txtContent.Clear();
        btnOpen.Enabled = false;
        btnOpen.Text = "Loading...";
        cboViewMode.Enabled = false;
        currentMethod = null;

        try
        {
            LoadAndParseMethod(path);
            cboViewMode.Enabled = true;
            cboViewMode.SelectedIndex = 0;
            txtContent.Text = rawMethodText;
            txtContent.SelectionStart = 0;
            txtContent.ScrollToCaret();
        }
        catch (Exception ex)
        {
            txtContent.Text = "Error reading method file:" + Environment.NewLine +
                              Environment.NewLine + ex.Message;
            if (ex.InnerException != null)
                txtContent.Text += Environment.NewLine + Environment.NewLine + ex.InnerException.Message;
            txtContent.Text += Environment.NewLine + Environment.NewLine + ex.StackTrace;
            lblInfo.Text = "ERROR: " + Path.GetFileName(path);
            lblInfo.ForeColor = Color.Red;
        }
        finally
        {
            btnOpen.Enabled = true;
            btnOpen.Text = "Open Method File";
        }
    }

    // ===== 核心：加载并解析方法 =====
    private void LoadAndParseMethod(string path)
    {
        currentFilePath = path;

        // 文件基本信息
        FileInfo fi = new FileInfo(path);
        lblInfo.Text = string.Format("File: {0}  |  Size: {1:N0} bytes  |  Modified: {2}",
            fi.Name, fi.Length, fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
        lblInfo.ForeColor = Color.FromArgb(80, 80, 80);

        // 使用 RawFileReader 读取
        currentMethod = InstrumentMethodFileReader.OpenMethod(path);

        if (currentMethod.IsError)
        {
            txtContent.Text = "Error: " + currentMethod.FileError.ErrorMessage;
            lblInfo.ForeColor = Color.Red;
            return;
        }

        // 构建原始方法文本（含解码的流内容）
        rawMethodText = BuildRawMethodText(currentMethod);

        // 收集各设备文本（LC 和 MS 分别按需提取）
        StringBuilder lcText = new StringBuilder();
        StringBuilder msText = new StringBuilder();

        foreach (var devKvp in currentMethod.Devices)
        {
            var devData = devKvp.Value;
            string devName = devKvp.Key;

            // MethodText 加入 LC（一般为泵/进样器等概要信息）
            if (!string.IsNullOrEmpty(devData.MethodText))
                lcText.AppendLine(devData.MethodText);

            var streams = devData.StreamBytes;
            if (streams == null) continue;

            // 判断是否为 MS 设备（MS 设备名通常包含 Exactive / TNG / Calcium 等）
            bool isMSDevice = devName.Contains("Exactive") || devName.StartsWith("TNG");

            // 尝试 Text 流
            string streamContent = null;
            if (streams.ContainsKey("Text"))
            {
                string decoded = DecodeStreamText(streams["Text"]);
                if (!string.IsNullOrEmpty(decoded) && !IsWhitespaceOnly(decoded) && IsReadableText(decoded))
                    streamContent = decoded;
            }

            // 如果 Text 流无效，尝试 Data 流（XML 格式，如 QE 系列）
            if (string.IsNullOrEmpty(streamContent) && streams.ContainsKey("Data"))
            {
                string decodedData = DecodeStreamText(streams["Data"]);
                if (!string.IsNullOrEmpty(decodedData) && !IsWhitespaceOnly(decodedData) &&
                    decodedData.TrimStart().StartsWith("<") && decodedData.Length > 100)
                    streamContent = decodedData;
            }

            // 根据设备类型分流：MS 设备→msText，LC 设备→lcText
            if (!string.IsNullOrEmpty(streamContent))
            {
                if (isMSDevice)
                    msText.AppendLine(streamContent);

                // LC 分流：仅当文本确实像 LC 方法格式时才加入（避免 Chromeleon 内部配置 XML 污染）
                if (!isMSDevice)
                {
                    string trimmedContent = streamContent.TrimStart();
                    bool looksLikeLCMethod =
                        trimmedContent.StartsWith("Program for") ||                     // Chromeleon 程序
                        trimmedContent.Contains("Flow.Nominal:") ||                      // Vanquish 格式
                        trimmedContent.Contains("PumpModule.Pump.Flow.") ||              // Vanquish Neo 格式
                        trimmedContent.Contains("NC_Pump.Flow") ||                       // Chromeleon 格式
                        trimmedContent.Contains("Time [mm:ss]") ||                       // EASY-nLC 格式
                        trimmedContent.Contains("Sample pickup:") ||                     // EASY-nLC 流程
                        trimmedContent.Contains("Sample loading:");                      // EASY-nLC 装载
                    if (looksLikeLCMethod)
                        lcText.AppendLine(streamContent);
                }
            }

            // 兜底：没有可用流时用 MethodText
            if (string.IsNullOrEmpty(streamContent) && !string.IsNullOrEmpty(devData.MethodText))
                msText.AppendLine(devData.MethodText);
        }

        lcParsedText = ParseLCMethod(lcText.ToString());
        msParsedText = ParseMSMethod(msText.ToString());
    }

    // ===== 构建原始方法文本 =====
    private string BuildRawMethodText(IInstrumentMethodFileAccess mf)
    {
        StringBuilder sb = new StringBuilder();

        var header = mf.FileHeader;
        sb.AppendLine("========================================");
        sb.AppendLine("  METHOD FILE INFORMATION");
        sb.AppendLine("========================================");
        sb.AppendLine("  Created:     " + header.CreationDate.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("  Modified:    " + header.ModifiedDate.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("  Description: " + (header.FileDescription ?? "(none)"));
        sb.AppendLine("  Devices:     " + mf.Devices.Count);
        sb.AppendLine("  Revisions:   " + header.NumberOfTimesModified);
        sb.AppendLine("========================================");
        sb.AppendLine();

        int devNum = 1;
        foreach (var devKvp in mf.Devices)
        {
            string devName = devKvp.Key;
            var devData = devKvp.Value;

            sb.AppendLine("========================================");
            sb.AppendLine("  DEVICE " + devNum + ": " + devName);
            sb.AppendLine("========================================");

            if (!string.IsNullOrEmpty(devData.MethodText))
            {
                sb.AppendLine();
                sb.AppendLine("--- Method Summary ---");
                sb.AppendLine(devData.MethodText);
            }

            var streams = devData.StreamBytes;
            if (streams != null && streams.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("--- Data Streams ---");
                foreach (var sk in streams.Keys)
                {
                    byte[] d = streams[sk];
                    string type = IsXml(d) ? "XML" : "Binary (TNG)";
                    sb.AppendLine(string.Format("  {0}: {1:N0} bytes ({2})", sk, d.Length, type));
                }

                // 解码并显示可读文本流的内容（带分行处理）
                foreach (var sk in streams.Keys)
                {
                    byte[] d = streams[sk];
                    string decoded = DecodeStreamText(d);
                    if (decoded == null) continue;
                    string trimmedStart = decoded.TrimStart();
                    // 跳过 XML 内容（如 QE 的二进制方法数据）
                    bool isXmlContent = trimmedStart.StartsWith("<");
                    // 跳过 Chromeleon 内部配置数据（非方法内容）
                    bool isChromeleonConfig = trimmedStart.StartsWith("InstrumentSetupMethod") ||
                                              trimmedStart.StartsWith("StructuredXmlData") ||
                                              trimmedStart.StartsWith("CmData");
                    if (!IsWhitespaceOnly(decoded) && IsReadableText(decoded) && !isXmlContent && !isChromeleonConfig)
                    {
                        sb.AppendLine();
                        sb.AppendLine(string.Format("  --- {0} Content ---", sk));
                        string[] contentLines = decoded.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                        foreach (string contentLine in contentLines)
                        {
                            string trimmed = contentLine.TrimEnd();
                            if (trimmed.Length > 0)
                                sb.AppendLine("  " + trimmed);
                        }
                    }
                }
            }

            sb.AppendLine();
            devNum++;
        }

        sb.AppendLine("========================================");
        sb.AppendLine("  End of method file");
        sb.AppendLine("========================================");

        return sb.ToString();
    }

    // ===== LC 方法解析（液相，兼容多种 LC 系统） =====
    private string ParseLCMethod(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "(No LC method data found)";

        // 检测 LC 格式类型
        bool isChromeleon = text.Contains("NC_Pump.Flow") || text.Contains("Program for Dionex");
        bool isEasyNLC = text.Contains("EASY-nLC") || text.Contains("Proxeon") || text.Contains("Time [mm:ss]");

        if (isChromeleon)
            return ParseChromeleonLCMethod(text);
        if (isEasyNLC)
            return ParseEasyNLCMethod(text);

        // 检测 Vanquish Neo Script 格式（Text 流格式，带 ---- Script ---- 标记）
        bool isVanquishScript = text.Contains("---- Script ----") ||
                                text.Contains("initial     Instrument Setup");
        if (isVanquishScript)
            return ParseVanquishNeoScript(text);

        // ===== 默认：Vanquish / Vanquish Neo 格式（MethodText API 格式） =====
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine("  LIQUID CHROMATOGRAPHY METHOD");
        sb.AppendLine("========================================");
        sb.AppendLine();

        // 1. 工作流与系统配置（兼容 Neo. 和无前缀两种格式）
        sb.AppendLine("── System Configuration ──");
        string flowRegime = LCExtract(text, @"FlowRegime:\s*(.+)");
        string workflow = LCExtract(text, @"InjectionWorkflow:\s*(.+)");
        string systemSetup = LCExtract(text, @"SystemSetup:\s*(.+)");
        if (!string.IsNullOrEmpty(flowRegime)) sb.AppendLine("  Flow Regime:     " + flowRegime);
        if (!string.IsNullOrEmpty(workflow))    sb.AppendLine("  Workflow:        " + workflow);
        if (!string.IsNullOrEmpty(systemSetup)) sb.AppendLine("  System Setup:    " + systemSetup);
        // 显示原始设备名称
        string instrName = LCExtract(text, @"Instrument:\s*(.+)");
        if (!string.IsNullOrEmpty(instrName)) sb.AppendLine("  Instrument:      " + instrName);
        sb.AppendLine();

        // 2. 溶剂系统（兼容 %A_Solvent 和 %A1_Equate 两种格式）
        sb.AppendLine("── Solvent System ──");
        string solventA = LCExtract(text, @"%A_Solvent:\s*(.+)");
        if (string.IsNullOrEmpty(solventA))
            solventA = LCExtract(text, @"%A1_Equate:\s*""?([^""\r\n]+)");
        string solventB = LCExtract(text, @"%B_Solvent:\s*(.+)");
        if (string.IsNullOrEmpty(solventB))
            solventB = LCExtract(text, @"%B1_Equate:\s*""?([^""\r\n]+)");
        string solventWeak = LCExtract(text, @"SolventWeakName:\s*""?([^""\r\n]+)");
        string solventStrong = LCExtract(text, @"SolventStrongName:\s*""?([^""\r\n]+)");
        if (!string.IsNullOrEmpty(solventA))    sb.AppendLine("  Mobile Phase A:  " + solventA);
        if (!string.IsNullOrEmpty(solventB))    sb.AppendLine("  Mobile Phase B:  " + solventB);
        if (!string.IsNullOrEmpty(solventWeak)) sb.AppendLine("  Weak Wash:       " + solventWeak);
        if (!string.IsNullOrEmpty(solventStrong)) sb.AppendLine("  Strong Wash:     " + solventStrong);
        sb.AppendLine();

        // 3. 泵与流速
        sb.AppendLine("── Gradient Table ──");
        var gradientSteps = ExtractGradientTable(text);
        if (gradientSteps.Count > 0)
        {
            // 检测流速单位（ml/min vs µl/min）
            string flowUnit = text.Contains("[ml/min]") ? "ml/min" : "µl/min";
            string header = string.Format("  {0,-12} {1,-10} {2,-14} {3,-8} {4,-10}", "Step", "Time(min)", "Flow(" + flowUnit + ")", "%B", "Curve");
            sb.AppendLine(header);
            sb.AppendLine("  " + new string('─', header.Length - 2));
            int stepNum = 1;
            foreach (var step in gradientSteps)
            {
                string marker = step.IsWash ? " [Wash]" : step.IsEquil ? " [Equil]" : step.IsInject ? " [Inject]" : "";
                string timeDisplay = string.IsNullOrEmpty(step.Time) ? "-" : step.Time;
                sb.AppendLine(string.Format("  {0,-12} {1,-10} {2,-14} {3,-8} {4,-10}{5}",
                    "Step " + stepNum++, timeDisplay, step.Flow, step.PercentB, step.Curve, marker));
            }
        }
        else
        {
            // Fallback: show raw flow values
            foreach (var match in Regex.Matches(text, @"Flow\.Nominal[^:]*:\s*([\d.]+)"))
                sb.AppendLine("  Flow: " + ((Match)match).Groups[1].Value + " µl/min");
            foreach (var match in Regex.Matches(text, @"%B\.Value[^:]*:\s*([\d.]+)"))
                sb.AppendLine("  %B: " + ((Match)match).Groups[1].Value + "%");
        }
        sb.AppendLine();

        // 4. 温度设置（兼容 Neo. 和 CC. 两种格式）
        sb.AppendLine("── Temperature Settings ──");
        string colTemp = LCExtract(text, @"Temperature\.Nominal[^:]*:\s*([\d.]+)");
        string sampleTemp = LCExtract(text, @"SamplerModule\.Temperature\.Nominal[^:]*:\s*([\d.]+)");
        if (!string.IsNullOrEmpty(colTemp))    sb.AppendLine("  Column Oven:     " + colTemp + " °C");
        if (!string.IsNullOrEmpty(sampleTemp)) sb.AppendLine("  Sample Compartment: " + sampleTemp + " °C");
        sb.AppendLine();

        // 5. 色谱柱（兼容 SeparationColumn 和 ColumnComp.Column_A 两种格式）
        sb.AppendLine("── Columns ──");
        var columnInfo = new Dictionary<string, string>();
        // Vanquish Neo 格式
        foreach (Match m in Regex.Matches(text, @"(SeparationColumn\d|TrapColumn\d)\.(\w+)[^:]*:\s*([^\r\n]+)"))
        {
            string key = m.Groups[1].Value + " / " + m.Groups[2].Value;
            columnInfo[key] = m.Groups[3].Value.Trim();
        }
        foreach (Match m in Regex.Matches(text, @"ColumnVoidVolume(SeparationColumn|TrapColumn)(\d)[^:]*:\s*([\d.]+)"))
        {
            string key = (m.Groups[1].Value == "SeparationColumn" ? "SeparationColumn" : "TrapColumn") + m.Groups[2].Value;
            columnInfo[key + " / VoidVolume (µl)"] = m.Groups[3].Value;
        }
        // 标准 Vanquish 格式
        foreach (Match m in Regex.Matches(text, @"ColumnComp\.(Column_\w)\.(\w+)[^:]*:\s*([^\r\n]+)"))
        {
            if (m.Groups[2].Value == "ActiveColumn" && m.Groups[3].Value.Trim() == "Yes")
            {
                string colName = m.Groups[1].Value;
                string temp = LCExtract(text, colName + @"\.Temperature\.Nominal[^:]*:\s*([^\r\n]+)");
                if (!string.IsNullOrEmpty(temp))
                    columnInfo[colName + " / Temperature"] = temp;
            }
        }
        if (columnInfo.Count > 0)
        {
            foreach (var kv in columnInfo)
                sb.AppendLine("  " + kv.Key + ": " + kv.Value);
        }
        sb.AppendLine();

        // 6. 进样器（兼容 Neo. 和无前缀两种格式）
        sb.AppendLine("── Injector / Autosampler ──");
        string injVol = LCExtract(text, @"Volume[^:]*:\s*([\d.]+)");
        string drawSpeed = LCExtract(text, @"DrawSpeed[^:]*:\s*([\d.]+)");
        string dispSpeed = LCExtract(text, @"DispenseSpeed[^:]*:\s*([\d.]+)");
        string loopVol = LCExtract(text, @"LoopVolume[^:]*:\s*([\d.]+)");
        string loadingFlow = LCExtract(text, @"LoadingFlow[^:]*:\s*([\d.]+)");
        string loadingMode = LCExtract(text, @"LoadingMode[^:]*:\s*(\w+)");
        string washMode = LCExtract(text, @"InjectWashMode[^:]*:\s*(\w+)");
        string washTime = LCExtract(text, @"WashTime[^:]*:\s*([\d.]+)");
        string injectMode = LCExtract(text, @"InjectMode[^:]*:\s*(\w+)");
        if (!string.IsNullOrEmpty(injVol))     sb.AppendLine("  Injection Volume: " + injVol + " µl");
        if (!string.IsNullOrEmpty(drawSpeed))  sb.AppendLine("  Draw Speed:      " + drawSpeed + " µl/s");
        if (!string.IsNullOrEmpty(dispSpeed))  sb.AppendLine("  Dispense Speed:  " + dispSpeed + " µl/s");
        if (!string.IsNullOrEmpty(loopVol))    sb.AppendLine("  Loop Volume:     " + loopVol + " µl");
        if (!string.IsNullOrEmpty(loadingFlow)) sb.AppendLine("  Loading Flow:    " + loadingFlow + " µl/min");
        if (!string.IsNullOrEmpty(loadingMode)) sb.AppendLine("  Loading Mode:    " + loadingMode);
        if (!string.IsNullOrEmpty(washMode))   sb.AppendLine("  Inject Wash:     " + washMode);
        if (!string.IsNullOrEmpty(washTime))   sb.AppendLine("  Wash Time:       " + washTime + " s");
        if (!string.IsNullOrEmpty(injectMode))   sb.AppendLine("  Inject Mode:     " + injectMode);
        // Trap wash
        string trapWashFlow = ExtractValue(text, @"TrapWashFlow[^:]*:\s*([\d.]+)");
        string trapWashMode = ExtractValue(text, @"TrapWashMode[^:]*:\s*(\w+)");
        if (!string.IsNullOrEmpty(trapWashFlow)) sb.AppendLine("  Trap Wash Flow:  " + trapWashFlow + " µl/min");
        if (!string.IsNullOrEmpty(trapWashMode)) sb.AppendLine("  Trap Wash Mode:  " + trapWashMode);
        sb.AppendLine();

        // 7. 柱平衡
        sb.AppendLine("── Column Equilibration ──");
        string eqFactor = ExtractValue(text, @"ColumnEquilibrationFactor[^:]*:\s*([\d.]+)");
        string eqFlow = ExtractValue(text, @"ColumnEquilibrationFlow[^:]*:\s*([\d.]+)");
        string eqMode = ExtractValue(text, @"ColumnEquilibrationMode[^:]*:\s*(\w+)");
        string eqFast = ExtractValue(text, @"FastColumnEquilibrationWanted[^:]*:\s*(\w+)");
        if (!string.IsNullOrEmpty(eqFactor)) sb.AppendLine("  Factor:    " + eqFactor);
        if (!string.IsNullOrEmpty(eqFlow))   sb.AppendLine("  Flow:      " + eqFlow + " µl/min");
        if (!string.IsNullOrEmpty(eqMode))   sb.AppendLine("  Mode:      " + eqMode);
        if (!string.IsNullOrEmpty(eqFast))   sb.AppendLine("  Fast Mode: " + eqFast);

        sb.AppendLine();
        sb.AppendLine("========================================");
        sb.AppendLine("  End of LC Method");
        sb.AppendLine("========================================");

        return sb.ToString();
    }

    // ===== Chromeleon / Dionex RSLC 液相解析 =====
    private string ParseChromeleonLCMethod(string text)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine("  LIQUID CHROMATOGRAPHY METHOD");
        sb.AppendLine("  (Dionex Chromeleon / RSLC)");
        sb.AppendLine("========================================");
        sb.AppendLine();

        // 1. 系统配置
        sb.AppendLine("── System Configuration ──");
        string samplerTemp = LCExtract(text, @"Sampler\.Temperature\.Nominal\s*=\s*([\d.]+)");
        string ovenTemp = LCExtract(text, @"ColumnOven\.(?:Temperature\.)?Nominal\s*=\s*([\d.]+)");
        string injectMode = LCExtract(text, @"InjectMode\s*=\s*(\w+)");
        if (!string.IsNullOrEmpty(samplerTemp)) sb.AppendLine("  Sampler Temp:    " + samplerTemp + " °C");
        if (!string.IsNullOrEmpty(ovenTemp))    sb.AppendLine("  Column Oven:     " + ovenTemp + " °C");
        if (!string.IsNullOrEmpty(injectMode))  sb.AppendLine("  Inject Mode:     " + injectMode);
        sb.AppendLine();

        // 2. 溶剂信息
        sb.AppendLine("── Solvent System ──");
        string solventA = LCExtract(text, @"%A\.Equate\s*=\s*\""?([^\""\r\n]+)");
        string solventB = LCExtract(text, @"%B\.Equate\s*=\s*\""?([^\""\r\n]+)");
        if (!string.IsNullOrEmpty(solventA)) sb.AppendLine("  Mobile Phase A:  " + solventA);
        if (!string.IsNullOrEmpty(solventB)) sb.AppendLine("  Mobile Phase B:  " + solventB);
        sb.AppendLine();

        // 3. 梯度表
        sb.AppendLine("── Gradient Table (NC_Pump) ──");
        var chromeGradSteps = ExtractChromeleonGradient(text);
        if (chromeGradSteps.Count > 0)
        {
            string unit = text.Contains("[nl/min]") ? "nl/min" : "µl/min";
            string hdr = string.Format("  {0,-12} {1,-10} {2,-14} {3,-8}", "Step", "Time(min)", "Flow(" + unit + ")", "%B");
            sb.AppendLine(hdr);
            sb.AppendLine("  " + new string('─', hdr.Length - 2));
            int sn = 1;
            foreach (var step in chromeGradSteps)
            {
                sb.AppendLine(string.Format("  {0,-12} {1,-10} {2,-14} {3,-8}",
                    "Step " + sn++, step.Time, step.Flow, step.PercentB));
            }
        }
        else
        {
            sb.AppendLine("  (No gradient steps found)");
        }
        sb.AppendLine();

        // 4. 纳升泵参数
        sb.AppendLine("── Nano Pump Settings ──");
        string ncFlow = LCExtract(text, @"NC_Pump\.Flow\s*=\s*([\d.]+)");
        string ncB = LCExtract(text, @"NC_Pump\.%B\s*=\s*([\d.]+)");
        string ncPLow = LCExtract(text, @"NC_Pump\.Pressure\.LowerLimit\s*=\s*(\d+)");
        string ncPHigh = LCExtract(text, @"NC_Pump\.Pressure\.UpperLimit\s*=\s*(\d+)");
        if (!string.IsNullOrEmpty(ncFlow)) sb.AppendLine("  Flow:       " + ncFlow);
        if (!string.IsNullOrEmpty(ncB))    sb.AppendLine("  %B:         " + ncB);
        if (!string.IsNullOrEmpty(ncPLow)) sb.AppendLine("  Pressure:   " + ncPLow + "-" + ncPHigh + " bar");
        sb.AppendLine();

        // 5. 进样器
        sb.AppendLine("── Autosampler ──");
        string drawSpeed = LCExtract(text, @"DrawSpeed\s*=\s*(\d+)");
        string dispSpeed = LCExtract(text, @"DispSpeed\s*=\s*(\d+)");
        string washVol = LCExtract(text, @"WashVolume\s*=\s*([\d.]+)");
        string washSpeed = LCExtract(text, @"WashSpeed\s*=\s*(\d+)");
        string puncture = LCExtract(text, @"PunctureDepth\s*=\s*([\d.]+)");
        if (!string.IsNullOrEmpty(drawSpeed)) sb.AppendLine("  Draw Speed:     " + drawSpeed + " nl/s");
        if (!string.IsNullOrEmpty(dispSpeed)) sb.AppendLine("  Dispense Speed: " + dispSpeed + " nl/s");
        if (!string.IsNullOrEmpty(washVol))   sb.AppendLine("  Wash Volume:    " + washVol + " µl");
        if (!string.IsNullOrEmpty(washSpeed)) sb.AppendLine("  Wash Speed:     " + washSpeed + " nl/s");
        if (!string.IsNullOrEmpty(puncture))  sb.AppendLine("  Puncture Depth: " + puncture + " mm");

        sb.AppendLine();
        sb.AppendLine("========================================");
        sb.AppendLine("  End of LC Method");
        sb.AppendLine("========================================");
        return sb.ToString();
    }

    // ===== Chromeleon 梯度解析 =====
    private List<GradientStep> ExtractChromeleonGradient(string text)
    {
        var steps = new List<GradientStep>();
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        GradientStep current = null;
        string lastTime = "";

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // 时间标记：如 " 0.000" 或 "15.000" 在行首
            Match mTime = Regex.Match(line, @"^(\d+\.\d+)\s");
            // Chromeleon 格式：NC_Pump.Flow = X.XXX [unit]
            Match mFlow = Regex.Match(line, @"NC_Pump\.Flow\s*=\s*([\d.]+)");
            Match mB = Regex.Match(line, @"NC_Pump\.%B\s*=\s*([\d.]+)");

            if (mTime.Success)
            {
                lastTime = mTime.Groups[1].Value;
            }

            if (mFlow.Success)
            {
                if (current != null && !string.IsNullOrEmpty(current.Flow)) { steps.Add(current); }
                current = new GradientStep();
                current.Time = lastTime;
                current.Flow = mFlow.Groups[1].Value;
            }
            if (current != null && mB.Success)
                current.PercentB = mB.Groups[1].Value;
        }
        if (current != null) steps.Add(current);

        return steps;
    }

    // ===== Easy-nLC (Proxeon) 液相解析 =====
    private string ParseEasyNLCMethod(string text)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine("  LIQUID CHROMATOGRAPHY METHOD");
        sb.AppendLine("  (EASY-nLC / Proxeon)");
        sb.AppendLine("========================================");
        sb.AppendLine();

        // 1. 样品装载参数
        sb.AppendLine("── Sample Pickup / Loading ──");
        string pickupVol = LCExtract(text, @"Sample pickup:[^\n]+\n\s*Volume[^:]*:\s*([\d.]+)");
        string pickupFlow = LCExtract(text, @"Sample pickup:[^\n]+\n\s*Volume[^:]*:\s*[\d.]+[^\n]+\n\s*Flow[^:]*:\s*([\d.]+)");
        string loadVol = LCExtract(text, @"Sample loading:[^\n]+\n\s*Volume[^:]*:\s*([\d.]+)");
        string loadFlow = LCExtract(text, @"Sample loading:[^\n]+\n\s*Volume[^:]*:\s*[\d.]+[^\n]+\n\s*Flow[^:]*:\s*([\d.]+)");
        string loadMaxP = LCExtract(text, @"Sample loading:[^\n]+\n(?:[^\n]+\n){0,2}\s*Max\. pressure[^:]*:\s*([\d.]+)");
        if (!string.IsNullOrEmpty(pickupVol)) sb.AppendLine("  Pickup Volume:    " + pickupVol + " µl");
        if (!string.IsNullOrEmpty(pickupFlow)) sb.AppendLine("  Pickup Flow:      " + pickupFlow + " µl/min");
        if (!string.IsNullOrEmpty(loadVol)) sb.AppendLine("  Loading Volume:   " + loadVol + " µl");
        if (!string.IsNullOrEmpty(loadFlow)) sb.AppendLine("  Loading Flow:     " + loadFlow + " µl/min");
        if (!string.IsNullOrEmpty(loadMaxP)) sb.AppendLine("  Max Pressure:     " + loadMaxP + " Bar");
        sb.AppendLine();

        // 2. 梯度表
        sb.AppendLine("── Gradient Table ──");
        var easySteps = ExtractEasyNLGGradient(text);
        if (easySteps.Count > 0)
        {
            string hdr = string.Format("  {0,-12} {1,-14} {2,-14} {3,-10}", "Step", "Time(mm:ss)", "Flow(nl/min)", "%B");
            sb.AppendLine(hdr);
            sb.AppendLine("  " + new string('─', hdr.Length - 2));
            int sn = 1;
            foreach (var step in easySteps)
            {
                sb.AppendLine(string.Format("  {0,-12} {1,-14} {2,-14} {3,-10}",
                    "Step " + sn++, step.Time, step.Flow, step.PercentB));
            }
        }
        else
        {
            sb.AppendLine("  (No gradient steps found)");
        }
        sb.AppendLine();

        // 3. 柱平衡
        sb.AppendLine("── Column Equilibration ──");
        string eqVol = LCExtract(text, @"Analytical column equilibration:[^\n]+\n\s*Volume[^:]*:\s*([\d.]+)");
        string eqFlow = LCExtract(text, @"Analytical column equilibration:[^\n]+\n\s*Volume[^:]*:\s*[\d.]+[^\n]+\n\s*Flow[^:]*:\s*([\d.]+)");
        string preEqVol = LCExtract(text, @"Pre-column equilibration:[^\n]+\n\s*Volume[^:]*:\s*([\d.]+)");
        if (!string.IsNullOrEmpty(preEqVol)) sb.AppendLine("  Pre-column Eq. Vol: " + preEqVol + " µl");
        if (!string.IsNullOrEmpty(eqVol)) sb.AppendLine("  Anal. Column Eq. Vol: " + eqVol + " µl");
        if (!string.IsNullOrEmpty(eqFlow)) sb.AppendLine("  Equilibration Flow: " + eqFlow + " µl/min");
        sb.AppendLine();

        // 4. 自动进样器清洗
        sb.AppendLine("── Autosampler Wash ──");
        string flushVol = LCExtract(text, @"Auto-sampler wash:[^\n]+\n\s*Flush volume[^:]*:\s*([\d.]+)");
        if (!string.IsNullOrEmpty(flushVol)) sb.AppendLine("  Flush Volume:  " + flushVol + " µl");

        sb.AppendLine();
        sb.AppendLine("========================================");
        sb.AppendLine("  End of LC Method");
        sb.AppendLine("========================================");
        return sb.ToString();
    }

    // ===== Easy-nLC 梯度解析 =====
    private List<GradientStep> ExtractEasyNLGGradient(string text)
    {
        var steps = new List<GradientStep>();
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        bool inGradient = false;

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            if (line.Contains("Gradient:")) { inGradient = true; continue; }
            if (!inGradient) continue;

            // 跳过表头行
            if (line.Contains("Time") && line.Contains("Flow")) continue;

            // 解析数据行: 00:00  00:00  300  2
            Match m = Regex.Match(line, @"^(\d+):(\d+)\s+(\d+):(\d+)\s+(\d+)\s+([\d.]+)");
            if (m.Success)
            {
                string timeStr = m.Groups[1].Value + ":" + m.Groups[2].Value;
                string flow = m.Groups[5].Value;
                string pctB = m.Groups[6].Value;
                steps.Add(new GradientStep { Time = timeStr, Flow = flow, PercentB = pctB });
            }
        }

        return steps;
    }

    // ===== Vanquish Neo Script 格式解析（SiiXcalibur/Text 流格式） =====
    private string ParseVanquishNeoScript(string text)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine("  LIQUID CHROMATOGRAPHY METHOD");
        sb.AppendLine("  (Vanquish Neo Script)");
        sb.AppendLine("========================================");
        sb.AppendLine();

        // 1. 方法基本信息
        sb.AppendLine("── Method Info ──");
        string name = LCExtract(text, @"Name:\s*(.+)");
        string runTime = LCExtract(text, @"Run time:\s*([\d.]+)\s*\[min\]");
        if (!string.IsNullOrEmpty(name))    sb.AppendLine("  Name:          " + name);
        if (!string.IsNullOrEmpty(runTime)) sb.AppendLine("  Run Time:      " + runTime + " min");
        sb.AppendLine();

        // 2. 工作流与系统配置
        sb.AppendLine("── System Configuration ──");
        string flowRegime = LCExtract(text, @"FlowRegime:\s*(\w+)");
        string workflow = LCExtract(text, @"InjectionWorkflow:\s*(\w+)");
        string systemSetup = LCExtract(text, @"SystemSetup:\s*(\w+)");
        string instrName = LCExtract(text, @"Instrument:\s*([^\r\n]+)");
        if (!string.IsNullOrEmpty(flowRegime)) sb.AppendLine("  Flow Regime:     " + flowRegime);
        if (!string.IsNullOrEmpty(workflow))    sb.AppendLine("  Workflow:        " + workflow);
        if (!string.IsNullOrEmpty(systemSetup)) sb.AppendLine("  System Setup:    " + systemSetup);
        if (!string.IsNullOrEmpty(instrName))   sb.AppendLine("  Instrument:      " + instrName);
        sb.AppendLine();

        // 3. 溶剂系统
        sb.AppendLine("── Solvent System ──");
        string solventA = LCExtract(text, @"%A_Solvent:\s*(.+)");
        string solventB = LCExtract(text, @"%B_Solvent:\s*(.+)");
        string solventWeak = LCExtract(text, @"SolventWeakName:\s*\""?([^\""\r\n]+)");
        string solventStrong = LCExtract(text, @"SolventStrongName:\s*\""?([^\""\r\n]+)");
        if (!string.IsNullOrEmpty(solventA))    sb.AppendLine("  Mobile Phase A:  " + solventA);
        if (!string.IsNullOrEmpty(solventB))    sb.AppendLine("  Mobile Phase B:  " + solventB);
        if (!string.IsNullOrEmpty(solventWeak)) sb.AppendLine("  Weak Wash:       " + solventWeak);
        if (!string.IsNullOrEmpty(solventStrong)) sb.AppendLine("  Strong Wash:     " + solventStrong);
        sb.AppendLine();

        // 4. 色谱柱
        sb.AppendLine("── Columns ──");
        // 分离柱
        string colDiam = LCExtract(text, @"SeparationColumn1\.Diameter[^:]*:\s*([^\r\n]+)");
        string colLen = LCExtract(text, @"SeparationColumn1\.Length[^:]*:\s*([^\r\n]+)");
        string colMaxP = LCExtract(text, @"SeparationColumn1\.MaximumPressure[^:]*:\s*([^\r\n]+)");
        if (!string.IsNullOrEmpty(colDiam) || !string.IsNullOrEmpty(colLen))
        {
            sb.AppendLine("  Separation Column:");
            if (!string.IsNullOrEmpty(colDiam)) sb.AppendLine("    Diameter:     " + colDiam);
            if (!string.IsNullOrEmpty(colLen))  sb.AppendLine("    Length:       " + colLen);
            if (!string.IsNullOrEmpty(colMaxP)) sb.AppendLine("    Max Pressure: " + colMaxP);
        }
        // 捕集柱
        string trapDiam = LCExtract(text, @"TrapColumn1\.Diameter[^:]*:\s*([^\r\n]+)");
        string trapLen = LCExtract(text, @"TrapColumn1\.Length[^:]*:\s*([^\r\n]+)");
        if (!string.IsNullOrEmpty(trapDiam) || !string.IsNullOrEmpty(trapLen))
        {
            sb.AppendLine("  Trap Column:");
            if (!string.IsNullOrEmpty(trapDiam)) sb.AppendLine("    Diameter:     " + trapDiam);
            if (!string.IsNullOrEmpty(trapLen))  sb.AppendLine("    Length:       " + trapLen);
        }
        sb.AppendLine();

        // 5. 温度
        sb.AppendLine("── Temperature Settings ──");
        string colTemp = LCExtract(text, @"ColumnChamber\.Temperature\.Nominal[^:]*:\s*([\d.]+)");
        string sampleTemp = LCExtract(text, @"SamplerModule\.Temperature\.Nominal[^:]*:\s*([\d.]+)");
        if (!string.IsNullOrEmpty(colTemp))    sb.AppendLine("  Column Oven:        " + colTemp + " °C");
        if (!string.IsNullOrEmpty(sampleTemp)) sb.AppendLine("  Sample Compartment: " + sampleTemp + " °C");
        sb.AppendLine();

        // 6. 进样器
        sb.AppendLine("── Injector / Autosampler ──");
        string drawSpeed = LCExtract(text, @"DrawSpeed[^:]*:\s*([\d.]+)");
        string dispSpeed = LCExtract(text, @"DispenseSpeed[^:]*:\s*([\d.]+)");
        string loadingFlow = LCExtract(text, @"LoadingFlow[^:]*:\s*([\d.]+)");
        string loadingMode = LCExtract(text, @"LoadingMode[^:]*:\s*(\w+)");
        string washMode = LCExtract(text, @"InjectWashMode[^:]*:\s*(\w+)");
        string washWeakTime = LCExtract(text, @"InjectWashWeakTime[^:]*:\s*([\d.]+)");
        string washStrongTime = LCExtract(text, @"InjectWashStrongTime[^:]*:\s*([\d.]+)");
        string trapWashFlow = LCExtract(text, @"TrapWashFlow[^:]*:\s*([\d.]+)");
        string trapWashMode = LCExtract(text, @"TrapWashMode[^:]*:\s*(\w+)");
        if (!string.IsNullOrEmpty(drawSpeed))    sb.AppendLine("  Draw Speed:        " + drawSpeed + " µl/s");
        if (!string.IsNullOrEmpty(dispSpeed))    sb.AppendLine("  Dispense Speed:    " + dispSpeed + " µl/s");
        if (!string.IsNullOrEmpty(loadingFlow))  sb.AppendLine("  Loading Flow:      " + loadingFlow + " µl/min");
        if (!string.IsNullOrEmpty(loadingMode))  sb.AppendLine("  Loading Mode:      " + loadingMode);
        if (!string.IsNullOrEmpty(washMode))     sb.AppendLine("  Inject Wash:       " + washMode);
        if (!string.IsNullOrEmpty(washWeakTime)) sb.AppendLine("  Weak Wash Time:    " + washWeakTime + " s");
        if (!string.IsNullOrEmpty(washStrongTime)) sb.AppendLine("  Strong Wash Time:  " + washStrongTime + " s");
        if (!string.IsNullOrEmpty(trapWashFlow)) sb.AppendLine("  Trap Wash Flow:    " + trapWashFlow + " µl/min");
        if (!string.IsNullOrEmpty(trapWashMode)) sb.AppendLine("  Trap Wash Mode:    " + trapWashMode);
        sb.AppendLine();

        // 7. 柱平衡
        sb.AppendLine("── Column Equilibration ──");
        string eqFactor = LCExtract(text, @"ColumnEquilibrationFactor[^:]*:\s*([\d.]+)");
        string eqFlow = LCExtract(text, @"ColumnEquilibrationFlow[^:]*:\s*([\d.]+)");
        string eqMode = LCExtract(text, @"ColumnEquilibrationMode[^:]*:\s*(\w+)");
        if (!string.IsNullOrEmpty(eqFactor)) sb.AppendLine("  Factor:    " + eqFactor);
        if (!string.IsNullOrEmpty(eqFlow))   sb.AppendLine("  Flow:      " + eqFlow + " µl/min");
        if (!string.IsNullOrEmpty(eqMode))   sb.AppendLine("  Mode:      " + eqMode);
        sb.AppendLine();

        // 8. 梯度表（解析 Script 格式时间线）
        sb.AppendLine("── Gradient Table ──");
        var scriptSteps = ExtractScriptGradientTable(text);
        if (scriptSteps.Count > 0)
        {
            string hdr = string.Format("  {0,-10} {1,-10} {2,-12} {3,-8} {4,-10}", "Step", "Time(min)", "Flow(µl/min)", "%B", "Curve");
            sb.AppendLine(hdr);
            sb.AppendLine("  " + new string('─', hdr.Length - 2));
            int sn = 1;
            foreach (var step in scriptSteps)
            {
                string marker = step.IsWash ? " [Wash]" : step.IsEquil ? " [Equil]" : step.IsInject ? " [Inject]" : "";
                sb.AppendLine(string.Format("  {0,-10} {1,-10} {2,-12} {3,-8} {4,-10}{5}",
                    "Step " + sn++, step.Time, step.Flow, step.PercentB, step.Curve, marker));
            }
        }
        else
        {
            sb.AppendLine("  (No gradient steps found)");
        }
        sb.AppendLine();

        sb.AppendLine("========================================");
        sb.AppendLine("  End of LC Method");
        sb.AppendLine("========================================");
        return sb.ToString();
    }

    // ===== Vanquish Neo Script 梯度解析 =====
    private List<GradientStep> ExtractScriptGradientTable(string text)
    {
        var steps = new List<GradientStep>();
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        GradientStep current = null;
        string lastTime = "";
        string currentStage = "";

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("----")) continue; // 跳过 ---- 分隔行

            // 检测 "initial     StageName" 或 "0.000 [min] StageName"
            Match mInit = Regex.Match(line, @"^initial\s+(\S[^\r\n]*)");
            if (mInit.Success)
            {
                currentStage = mInit.Groups[1].Value.Trim();
                continue;
            }

            // 检测时间标记行："0.000 [min] StageName"
            Match mTime = Regex.Match(line, @"^(\d+\.\d+)\s*\[min\]\s*(.*)$");
            if (mTime.Success)
            {
                string timeVal = mTime.Groups[1].Value;
                string stageName = mTime.Groups[2].Value.Trim();
                lastTime = timeVal;
                currentStage = stageName;

                // 将上一个 pending 的梯度步加入列表
                if (current != null && !string.IsNullOrEmpty(current.Flow))
                {
                    steps.Add(current);
                    current = null;
                }

                // 特殊阶段标记（无梯度参数的阶段）
                if (stageName == "Inject")
                {
                    steps.Add(new GradientStep { IsInject = true, Time = timeVal, Flow = "-", PercentB = "Inject", Curve = "-" });
                }
                else if (stageName == "Post Run")
                {
                    // Post Run 包含 StartColumnEquilibration，梯度步会后续处理
                }

                continue;
            }

            // 跳过 Instrument Setup 参数行（非梯度内容）
            if (currentStage == "Instrument Setup" || currentStage.Contains("Instrument Setup") ||
                currentStage == "Inject Preparation" || currentStage == "Start Run" ||
                currentStage == "Stop Run")
            {
                continue;
            }

            // 解析 Flow / %B / Curve
            Match mFlow = Regex.Match(line, @"Flow\.Nominal[^:]*:\s*([\d.]+)");
            Match mB = Regex.Match(line, @"%B\.Value[^:]*:\s*([\d.]+)");
            Match mCurve = Regex.Match(line, @"Curve[^:]*:\s*(\d+)");
            Match mWash = Regex.Match(line, @"StartColumnWash");
            Match mEquil = Regex.Match(line, @"StartColumnEquilibration");

            if (mFlow.Success)
            {
                if (current != null && !string.IsNullOrEmpty(current.Flow)) { steps.Add(current); }
                current = new GradientStep();
                current.Time = lastTime;
                current.Flow = mFlow.Groups[1].Value;
                // 标记阶段
                if (currentStage == "Equilibration")
                    current.IsEquil = true;
            }
            if (current != null && mB.Success)
                current.PercentB = mB.Groups[1].Value;
            if (current != null && mCurve.Success)
                current.Curve = mCurve.Groups[1].Value;

            if (mWash.Success)
            {
                if (current != null) { current.IsWash = true; steps.Add(current); current = null; }
            }
            if (mEquil.Success)
            {
                if (current != null) { steps.Add(current); current = null; }
                steps.Add(new GradientStep { IsEquil = true, Time = lastTime, Flow = "-", PercentB = "Re-equil", Curve = "-" });
            }
        }

        if (current != null && !string.IsNullOrEmpty(current.Flow))
            steps.Add(current);

        return steps;
    }

    // ===== MS 方法解析（质谱，兼容 Orbitrap、TSQ、Q Exactive XML） =====
    private string ParseMSMethod(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "(No MS method data found)";

        // 检测仪器类型
        string trimmed = text.TrimStart();
        bool isOrbitrap = Regex.IsMatch(text, @"Orbitrap", RegexOptions.IgnoreCase);
        bool isTSQ = Regex.IsMatch(text, @"TSQ\s", RegexOptions.IgnoreCase);
        bool isQExactiveXML = trimmed.StartsWith("<") && text.Contains("TargetInstrument");
        bool isEasyNLC = text.Contains("EASY-nLC") || text.Contains("Proxeon");

        if (isQExactiveXML)
            return ParseQExactiveXML(text);
        else if (isOrbitrap)
            return ParseOrbitrapMethod(text);
        else if (isTSQ)
            return ParseTSQMethod(text);
        else if (isEasyNLC)
            return ParseGenericMSMethod(text);
        else
            return ParseGenericMSMethod(text);
    }

    // ===== Q Exactive XML 方法解析 =====
    private string ParseQExactiveXML(string text)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine("  Q EXACTIVE METHOD (XML)");
        sb.AppendLine("========================================");
        sb.AppendLine();

        // 1. 仪器型号
        string targetInstr = ExtractValue(text, @"TargetInstrument=\""([^\""]+)\""");
        sb.AppendLine("  Instrument: " + (string.IsNullOrEmpty(targetInstr) ? "Q Exactive" : targetInstr));
        string desc = ExtractValue(text, @"<Description>([^<]+)</Description>");
        if (!string.IsNullOrEmpty(desc)) sb.AppendLine("  Description: " + desc);
        sb.AppendLine();

        // 2. 提取所有 ScanEvent（使用简易字符串解析替代 XML 解析器）
        var scanEvents = ExtractQEScanEvents(text);
        int eventNum = 0;
        foreach (var se in scanEvents)
        {
            eventNum++;
            string header = se.ContainsKey("FullInternalName") ? se["FullInternalName"] : "ScanEvent " + eventNum;
            sb.AppendLine("── " + header + " ──");

            if (se.ContainsKey("Polarity"))
            {
                string pol = se["Polarity"];
                sb.AppendLine("  Polarity:      " + (pol == "0" ? "Positive" : pol == "1" ? "Negative" : pol));
            }
            if (se.ContainsKey("Resolution"))
                sb.AppendLine("  Resolution:    " + FormatQEResolution(se["Resolution"]));
            if (se.ContainsKey("AGC_target"))
                sb.AppendLine("  AGC Target:    " + se["AGC_target"]);
            if (se.ContainsKey("Max_inject_time"))
                sb.AppendLine("  Max Inject:    " + se["Max_inject_time"] + " ms");
            if (se.ContainsKey("First_mass") && se.ContainsKey("Last_mass"))
                sb.AppendLine("  Scan Range:    " + se["First_mass"] + "-" + se["Last_mass"] + " m/z");
            if (se.ContainsKey("Microscans"))
                sb.AppendLine("  Microscans:    " + se["Microscans"]);
            if (se.ContainsKey("Spectrum_data_type"))
                sb.AppendLine("  Data Type:     " + (se["Spectrum_data_type"] == "0" ? "Profile" : "Centroid"));
            if (se.ContainsKey("End"))
                sb.AppendLine("  End Time:      " + se["End"] + " min");
            if (se.ContainsKey("Fragmentation_HCD"))
                sb.AppendLine("  HCD Energy:    " + se["Fragmentation_HCD"]);

            // DDA 特定参数
            if (se.ContainsKey("Repeat_count"))
                sb.AppendLine("  Repeat Count:  " + se["Repeat_count"]);
            if (se.ContainsKey("Depend_count"))
                sb.AppendLine("  Depend Count:  " + se["Depend_count"]);
            if (se.ContainsKey("Underfill_ratio"))
                sb.AppendLine("  Underfill:      " + se["Underfill_ratio"] + "%");
            if (se.ContainsKey("Default_charge_state"))
                sb.AppendLine("  Default Charge: " + se["Default_charge_state"]);
            if (se.ContainsKey("DD_dyn_exclusion_sec"))
                sb.AppendLine("  Dyn. Exclusion: " + se["DD_dyn_exclusion_sec"] + " s");
            if (se.ContainsKey("DD_first_mass"))
                sb.AppendLine("  MS/MS Start:    " + se["DD_first_mass"] + " m/z");
            if (se.ContainsKey("Intensity_threshold_min"))
                sb.AppendLine("  Min Intensity:  " + se["Intensity_threshold_min"]);

            sb.AppendLine();
        }

        sb.AppendLine("========================================");
        sb.AppendLine("  End of MS Method");
        sb.AppendLine("========================================");

        return sb.ToString();
    }

    // ===== 从 Q Exactive XML 中提取 ScanEvent 参数 =====
    private List<Dictionary<string, string>> ExtractQEScanEvents(string xml)
    {
        var events = new List<Dictionary<string, string>>();
        int idx = 0;

        while (true)
        {
            idx = xml.IndexOf("<ScanEvent ", idx);
            if (idx < 0) break;
            int endIdx = xml.IndexOf("</ScanEvent>", idx);
            if (endIdx < 0) break;

            string block = xml.Substring(idx, endIdx + 12 - idx);
            var dict = new Dictionary<string, string>();

            // 提取 FullInternalName
            Match mName = Regex.Match(block, @"FullInternalName=\""([^\""]+)\""");
            if (mName.Success) dict["FullInternalName"] = mName.Groups[1].Value;

            // 提取所有参数 <Tag>value</Tag>
            // 匹配 <TagName ... FullInternalName="...">value</TagName>
            foreach (Match m in Regex.Matches(block, @"<(\w+)[^>]*FullInternalName=\""[^\""]*\"">([^<]+)</\1>"))
            {
                string paramName = m.Groups[1].Value;
                string paramValue = m.Groups[2].Value.Trim();
                if (!dict.ContainsKey(paramName))
                    dict[paramName] = paramValue;
            }

            events.Add(dict);
            idx = endIdx + 12;
        }

        return events;
    }

    private string FormatQEResolution(string val)
    {
        int n;
        if (int.TryParse(val, out n))
        {
            if (n >= 1000)
            {
                double k = n / 1000.0;
                if (k == (int)k)
                    return ((int)k).ToString() + "K";
                else
                    return k.ToString("0.#") + "K";
            }
        }
        return val;
    }

    // ===== Orbitrap 方法解析（原有逻辑） =====
    private string ParseOrbitrapMethod(string text)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine("  MASS SPECTROMETRY METHOD");
        sb.AppendLine("========================================");
        sb.AppendLine();

        // 1. 仪器型号
        string instrument = "";
        foreach (Match m in Regex.Matches(text, @"^(Orbitrap\s+\S+)", RegexOptions.Multiline))
        {
            if (m.Groups[1].Value.Length < 30)
            { instrument = m.Groups[1].Value; break; }
        }
        if (!string.IsNullOrEmpty(instrument))
            sb.AppendLine("  Instrument: " + instrument);
        sb.AppendLine();

        // 2. 离子源
        sb.AppendLine("── Ion Source ──");
        string polarity = ExtractValue(text, @"Polarity[^:]*:\s*(.+)");
        string faims = ExtractValue(text, @"FAIMS Mode\s*=\s*(.+)");
        string defCharge = ExtractValue(text, @"Default Charge State\s*=\s*(\d+)");
        string sourceCID = ExtractValue(text, @"Source Fragmentation\s*=\s*(.+)");
        string srcGas = ExtractValue(text, @"Use Static Source Gasses\s*=\s*(.+)");
        string srcTune = ExtractValue(text, @"Use Ion Source Settings from Tune\s*=\s*(.+)");
        if (!string.IsNullOrEmpty(polarity)) sb.AppendLine("  Polarity:        " + polarity);
        if (!string.IsNullOrEmpty(faims))    sb.AppendLine("  FAIMS Mode:      " + faims);
        if (!string.IsNullOrEmpty(defCharge)) sb.AppendLine("  Default Charge:  " + defCharge);
        if (!string.IsNullOrEmpty(srcGas))   sb.AppendLine("  Static Gas:      " + srcGas);
        if (!string.IsNullOrEmpty(srcTune))  sb.AppendLine("  Source from Tune: " + srcTune);
        if (!string.IsNullOrEmpty(sourceCID)) sb.AppendLine("  Source Frag:     " + sourceCID);
        sb.AppendLine();

        // 3. MS1 全扫描参数
        sb.AppendLine("── MS¹ Full Scan ──");
        string ms1Res = ExtractValue(text, @"Orbitrap Resolution\s*=\s*(\d+K?)");
        string ms1Range = ExtractValue(text, @"Scan Range \(m/z\)\s*=\s*([\d-]+)");
        string ms1MaxIT = ExtractValue(text, @"Maximum Injection Time \(ms\)\s*=\s*(\d+)");
        string ms1AGC = ExtractValue(text, @"AGC Target\s*=\s*(\d+)");
        string ms1NormAGC = ExtractValue(text, @"Normalized AGC Target\s*=\s*([\d%]+)");
        string ms1Scan = ExtractValue(text, @"Microscans\s*=\s*(\d+)");
        string ms1Detector = ExtractValue(text, @"Detector Type\s*=\s*(.+)");
        string ms1StartTime = ExtractValue(text, @"Start Time \(min\)\s*=\s*([\d.]+)");
        string cycleTime = ExtractValue(text, @"Cycle Time \(sec\)\s*=\s*([\d.]+)");
        string wideQuad = ExtractValue(text, @"Use Wide Quad Isolation\s*=\s*(.+)");

        if (!string.IsNullOrEmpty(ms1Detector)) sb.AppendLine("  Detector:     " + ms1Detector);
        if (!string.IsNullOrEmpty(ms1Res))     sb.AppendLine("  Resolution:   " + ms1Res);
        if (!string.IsNullOrEmpty(ms1Range))   sb.AppendLine("  Scan Range:   " + ms1Range + " m/z");
        if (!string.IsNullOrEmpty(ms1MaxIT))   sb.AppendLine("  Max Inject:   " + ms1MaxIT + " ms");
        if (!string.IsNullOrEmpty(ms1AGC))     sb.AppendLine("  AGC Target:   " + ms1AGC);
        if (!string.IsNullOrEmpty(ms1NormAGC)) sb.AppendLine("  Norm. AGC:    " + ms1NormAGC);
        if (!string.IsNullOrEmpty(ms1Scan))    sb.AppendLine("  Microscans:   " + ms1Scan);
        if (!string.IsNullOrEmpty(ms1StartTime)) sb.AppendLine("  Start Time:   " + ms1StartTime + " min");
        if (!string.IsNullOrEmpty(cycleTime))  sb.AppendLine("  Cycle Time:   " + cycleTime + " sec");
        if (!string.IsNullOrEmpty(wideQuad))   sb.AppendLine("  Wide Quad:    " + wideQuad);
        sb.AppendLine();

        // 4. 动态排除
        sb.AppendLine("── Dynamic Exclusion ──");
        string exclDuration = ExtractValue(text, @"Exclusion duration \(s\)\s*=\s*(\d+)");
        string exclCharge = ExtractValue(text, @"Include charge state\(s\)\s*=\s*([\d,-]+)");
        string exclUndet = ExtractValue(text, @"Include undetermined charge states\s*=\s*(.+)");
        if (!string.IsNullOrEmpty(exclDuration)) sb.AppendLine("  Duration:      " + exclDuration + " s");
        if (!string.IsNullOrEmpty(exclCharge))   sb.AppendLine("  Charge States: " + exclCharge);
        if (!string.IsNullOrEmpty(exclUndet))    sb.AppendLine("  Undetermined:  " + exclUndet);
        sb.AppendLine();

        // 5. MS2 DDA 参数
        sb.AppendLine("── MS² DDA (Data Dependent) ──");
        string ddMode = ExtractValue(text, @"Data Dependent Mode\s*=\s*(.+)");
        string ms2Detector = "";
        string ms2Res = "";
        string ms2MaxIT = "";
        string ms2AGC = "";
        string ms2NormAGC = "";

        var resMatches = Regex.Matches(text, @"Orbitrap Resolution\s*=\s*(\d+K?)");
        if (resMatches.Count >= 2) ms2Res = resMatches[1].Groups[1].Value;
        var itMatches = Regex.Matches(text, @"Maximum Injection Time \(ms\)\s*=\s*(\d+)");
        if (itMatches.Count >= 2) ms2MaxIT = itMatches[1].Groups[1].Value;
        var agcMatches = Regex.Matches(text, @"AGC Target\s*=\s*(\d+)");
        if (agcMatches.Count >= 2) ms2AGC = agcMatches[1].Groups[1].Value;
        var nagcMatches = Regex.Matches(text, @"Normalized AGC Target\s*=\s*([\d%]+)");
        if (nagcMatches.Count >= 2) ms2NormAGC = nagcMatches[1].Groups[1].Value;
        var detMatches = Regex.Matches(text, @"Detector Type\s*=\s*(.+)");
        if (detMatches.Count >= 2) ms2Detector = detMatches[1].Groups[1].Value.Trim();

        string isolationMode = ExtractValue(text, @"Isolation Mode\s*=\s*(.+)");
        string isolationWin = ExtractValue(text, @"Isolation Window\s*=\s*([\d.]+)");
        string activationType = ExtractValue(text, @"ActivationType\s*=\s*(.+)");
        string scanRangeMode = ExtractValue(text, @"Scan Range Mode\s*=\s*(.+)");
        string timeMode = ExtractValue(text, @"Time Mode\s*=\s*(.+)");
        string multiNotch = ExtractValue(text, @"Multi-notch Isolation\s*=\s*(.+)");
        string ipa = ExtractValue(text, @"Enable Intelligent Product Acquisition[^=]*=\s*(.+)");

        if (!string.IsNullOrEmpty(ddMode))       sb.AppendLine("  DDA Mode:       " + ddMode);
        if (!string.IsNullOrEmpty(isolationMode)) sb.AppendLine("  Isolation Mode: " + isolationMode);
        if (!string.IsNullOrEmpty(isolationWin))  sb.AppendLine("  Isolation Win:  " + isolationWin + " m/z");
        if (!string.IsNullOrEmpty(activationType)) sb.AppendLine("  Activation:     " + activationType);
        if (!string.IsNullOrEmpty(ms2Detector))  sb.AppendLine("  Detector:       " + ms2Detector);
        if (!string.IsNullOrEmpty(ms2Res))       sb.AppendLine("  Resolution:     " + ms2Res);
        if (!string.IsNullOrEmpty(ms2MaxIT))     sb.AppendLine("  Max Inject:     " + ms2MaxIT + " ms");
        if (!string.IsNullOrEmpty(ms2AGC))       sb.AppendLine("  AGC Target:     " + ms2AGC);
        if (!string.IsNullOrEmpty(ms2NormAGC))   sb.AppendLine("  Norm. AGC:      " + ms2NormAGC);
        if (!string.IsNullOrEmpty(scanRangeMode)) sb.AppendLine("  Scan Range:     " + scanRangeMode);
        if (!string.IsNullOrEmpty(timeMode))     sb.AppendLine("  Time Mode:      " + timeMode);
        if (!string.IsNullOrEmpty(multiNotch))   sb.AppendLine("  Multi-Notch:    " + multiNotch);
        if (!string.IsNullOrEmpty(ipa))          sb.AppendLine("  IPA:            " + ipa);

        sb.AppendLine();
        sb.AppendLine("========================================");
        sb.AppendLine("  End of MS Method");
        sb.AppendLine("========================================");

        return sb.ToString();
    }

    // ===== TSQ / 三重四极杆方法解析 =====
    private string ParseTSQMethod(string text)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine("  TSQ TRIPLE QUADRUPOLE METHOD");
        sb.AppendLine("========================================");
        sb.AppendLine();

        // 1. 仪器型号
        string instrument = ExtractValue(text, @"^(TSQ[\s\w]+)Method Summary", RegexOptions.Multiline);
        if (string.IsNullOrEmpty(instrument))
            instrument = ExtractValue(text, @"^(TSQ[\s\w]+)", RegexOptions.Multiline);
        if (!string.IsNullOrEmpty(instrument))
            sb.AppendLine("  Instrument: " + instrument.Trim());
        // Method duration
        string msDur = ExtractValue(text, @"Method Duration \(min\)\s*=\s*([\d.]+)");
        if (!string.IsNullOrEmpty(msDur)) sb.AppendLine("  Duration:       " + msDur + " min");
        sb.AppendLine();

        // 2. 离子源
        sb.AppendLine("── Ion Source (H-ESI) ──");
        string ionSrc = ExtractValue(text, @"Ion Source Type\s*=\s*(.+)");
        string spPos = ExtractValue(text, @"Spray Voltage: Positive Ion \(V\)\s*=\s*(\d+)");
        string spNeg = ExtractValue(text, @"Spray Voltage: Negative Ion \(V\)\s*=\s*(\d+)");
        string sheath = ExtractValue(text, @"Sheath Gas \(Arb\)\s*=\s*(\d+)");
        string auxGas = ExtractValue(text, @"Aux Gas \(Arb\)\s*=\s*(\d+)");
        string sweepGas = ExtractValue(text, @"Sweep Gas \(Arb\)\s*=\s*(\d+)");
        string ionTrans = ExtractValue(text, @"Ion Transfer Tube Temp \(.C\)\s*=\s*(\d+)");
        string vapTemp = ExtractValue(text, @"Vaporizer Temp \(.C\)\s*=\s*(\d+)");
        if (!string.IsNullOrEmpty(ionSrc)) sb.AppendLine("  Source Type:      " + ionSrc);
        if (!string.IsNullOrEmpty(spPos))  sb.AppendLine("  Spray Voltage (+): " + spPos + " V");
        if (!string.IsNullOrEmpty(spNeg))  sb.AppendLine("  Spray Voltage (-): " + spNeg + " V");
        if (!string.IsNullOrEmpty(sheath)) sb.AppendLine("  Sheath Gas:       " + sheath + " Arb");
        if (!string.IsNullOrEmpty(auxGas)) sb.AppendLine("  Aux Gas:          " + auxGas + " Arb");
        if (!string.IsNullOrEmpty(sweepGas)) sb.AppendLine("  Sweep Gas:        " + sweepGas + " Arb");
        if (!string.IsNullOrEmpty(ionTrans)) sb.AppendLine("  Ion Transfer Tube: " + ionTrans + " °C");
        if (!string.IsNullOrEmpty(vapTemp)) sb.AppendLine("  Vaporizer Temp:   " + vapTemp + " °C");
        sb.AppendLine();

        // 3. SRM 参数
        sb.AppendLine("── SRM Parameters ──");
        string expType = ExtractValue(text, @"Experiment Type:\s*(.+)");
        string cycleT = ExtractValue(text, @"Cycle Time \(sec\)\s*=\s*([\d.]+)");
        string peakW = ExtractValue(text, @"Chromatographic Peak Width \(sec\)\s*=\s*([\d.]+)");
        string dataMode = ExtractValue(text, @"Data Mode\s*=\s*(.+)");
        string colGas = ExtractValue(text, @"Collision Gas Pressure \(mTorr\)\s*=\s*([\d.]+)");
        string q1Res = ExtractValue(text, @"Q1 Resolution \(FWHM\)\s*=\s*([\d.]+)");
        string q3Res = ExtractValue(text, @"Q3 Resolution \(FWHM\)\s*=\s*([\d.]+)");
        string srcFrag = ExtractValue(text, @"Source Fragmentation \(V\)\s*=\s*([\d.]+)");
        if (!string.IsNullOrEmpty(expType)) sb.AppendLine("  Experiment Type:  " + expType);
        if (!string.IsNullOrEmpty(cycleT))  sb.AppendLine("  Cycle Time:       " + cycleT + " sec");
        if (!string.IsNullOrEmpty(peakW))   sb.AppendLine("  Peak Width:       " + peakW + " sec");
        if (!string.IsNullOrEmpty(dataMode)) sb.AppendLine("  Data Mode:        " + dataMode);
        if (!string.IsNullOrEmpty(colGas))  sb.AppendLine("  Collision Gas:    " + colGas + " mTorr");
        if (!string.IsNullOrEmpty(q1Res))   sb.AppendLine("  Q1 Resolution:    " + q1Res + " FWHM");
        if (!string.IsNullOrEmpty(q3Res))   sb.AppendLine("  Q3 Resolution:    " + q3Res + " FWHM");
        if (!string.IsNullOrEmpty(srcFrag)) sb.AppendLine("  Source Frag:      " + srcFrag + " V");
        sb.AppendLine();

        // 4. SRM Transition 表
        sb.AppendLine("── SRM Transition Table ──");
        var transLines = new List<string>();
        bool inTable = false;
        foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.None))
        {
            if (line.Contains("Compound Name") && line.Contains("Precursor"))
            {
                inTable = true;
                // Print header
                string hdr = string.Format("  {0,-16} {1,-6} {2,-12} {3,-12} {4,-6} {5,-8}",
                    "Compound", "Pol", "Precursor(m/z)", "Product(m/z)", "CE(V)", "Dwell(ms)");
                sb.AppendLine(hdr);
                sb.AppendLine("  " + new string('─', hdr.Length - 2));
                continue;
            }
            if (inTable && line.Trim().Length > 0 && !line.Trim().StartsWith("-"))
            {
                // Parse: Compound Name | Start Time | End Time | Polarity | Precursor | Product | CE | Dwell | RF Lens
                string[] parts = line.Split('|');
                if (parts.Length >= 8)
                {
                    string compName = parts[0].Trim();
                    string polarity = parts[3].Trim();
                    string precursor = parts[4].Trim();
                    string product = parts[5].Trim();
                    string ce = parts[6].Trim();
                    string dwell = parts[7].Trim();
                    string pol = polarity == "Positive" ? "+" : "-";
                    sb.AppendLine(string.Format("  {0,-16} {1,-6} {2,-12} {3,-12} {4,-6} {5,-8}",
                        compName.Length > 15 ? compName.Substring(0, 15) : compName,
                        pol, precursor, product, ce, dwell));
                }
            }
        }
        sb.AppendLine();

        sb.AppendLine("========================================");
        sb.AppendLine("  End of MS Method");
        sb.AppendLine("========================================");

        return sb.ToString();
    }

    // ===== 通用 MS 方法解析（未识别仪器类型时） =====
    private string ParseGenericMSMethod(string text)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine("  MASS SPECTROMETRY METHOD (Raw)");
        sb.AppendLine("========================================");
        sb.AppendLine();

        // 显示前几行（通常包含仪器名和基本信息）
        string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines.Take(8))
        {
            string trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                sb.AppendLine("  " + trimmed);
        }

        // 尝试提取常见参数
        string methodDur = ExtractValue(text, @"Method Duration \(min\)\s*=\s*([\d.]+)");
        string ionSrc = ExtractValue(text, @"Ion Source Type\s*=\s*(.+)");
        string faims = ExtractValue(text, @"FAIMS Mode\s*=\s*(.+)");
        if (!string.IsNullOrEmpty(methodDur)) sb.AppendLine("\n  Method Duration: " + methodDur + " min");
        if (!string.IsNullOrEmpty(ionSrc)) sb.AppendLine("  Ion Source:      " + ionSrc);
        if (!string.IsNullOrEmpty(faims)) sb.AppendLine("  FAIMS Mode:      " + faims);

        sb.AppendLine();
        sb.AppendLine("========================================");
        sb.AppendLine("  End of MS Method");
        sb.AppendLine("========================================");

        return sb.ToString();
    }

    // ===== 辅助函数 =====

    private string ExtractValue(string text, string pattern, RegexOptions options = RegexOptions.None)
    {
        Match m = Regex.Match(text, pattern, options);
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    // LCExtract 是 ExtractValue 的别名，用于 LC 解析（兼容不同设备前缀）
    private string LCExtract(string text, string pattern) { return ExtractValue(text, pattern); }

    private List<GradientStep> ExtractGradientTable(string text)
    {
        var steps = new List<GradientStep>();
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        GradientStep current = null;
        string lastTime = "";
        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // 时间标记行：如 "0.000 [min]" 或 "1.800 [min]"
            Match mTime = Regex.Match(line, @"^(\d+\.\d+)\s*\[min\]");

            // Match Neo.PumpModule.Pump.xxx patterns
            Match mFlow = Regex.Match(line, @"Flow\.Nominal[^:]*:\s*([\d.]+)");
            Match mB = Regex.Match(line, @"%B\.Value[^:]*:\s*([\d.]+)");
            Match mCurve = Regex.Match(line, @"Curve[^:]*:\s*(\d+)");
            Match mWash = Regex.Match(line, @"StartColumnWash");
            Match mEquil = Regex.Match(line, @"StartColumnEquilibration");
            Match mInject = Regex.Match(line, @"Inject\s*$");

            // 记录当前时间标记，后续 Flow 步会使用它
            if (mTime.Success)
            {
                lastTime = mTime.Groups[1].Value;
                continue;
            }

            if (mFlow.Success)
            {
                if (current != null && !string.IsNullOrEmpty(current.Flow)) { steps.Add(current); }
                current = new GradientStep();
                current.Time = lastTime;   // 关联最近的时间标记
                current.Flow = mFlow.Groups[1].Value;
            }
            if (current != null && mB.Success)
                current.PercentB = mB.Groups[1].Value;
            if (current != null && mCurve.Success)
                current.Curve = mCurve.Groups[1].Value;
            if (mWash.Success)
            {
                if (current != null) { current.IsWash = true; current.Time = lastTime; steps.Add(current); current = null; }
                steps.Add(new GradientStep { IsWash = true, Time = lastTime, Flow = "-", PercentB = "Wash", Curve = "-" });
            }
            if (mEquil.Success)
            {
                if (current != null) { steps.Add(current); current = null; }
                steps.Add(new GradientStep { IsEquil = true, Time = lastTime, Flow = "-", PercentB = "Re-equil", Curve = "-" });
            }
            if (mInject.Success)
            {
                if (current != null) { steps.Add(current); current = null; }
                steps.Add(new GradientStep { IsInject = true, Time = lastTime, Flow = "-", PercentB = "Injection", Curve = "-" });
            }
        }
        if (current != null) steps.Add(current);

        return steps;
    }

    // ===== 信息栏响应式布局 =====
    private void LayoutInfoBar()
    {
        int panelW = pnlInfoBar.ClientSize.Width;
        int comboW = 180;
        int viewLabelW = 48;
        int gap = 4;
        int gap2 = 16;
        int rightMargin = 8;
        int leftMargin = 10;

        cboViewMode.Location = new Point(panelW - comboW - rightMargin, 3);
        lblView.Location = new Point(panelW - comboW - rightMargin - gap - viewLabelW, 1);
        lblInfo.Size = new Size(lblView.Left - gap2 - leftMargin, 30);
    }

    private string FormatAGC(string val)
    {
        long n;
        if (long.TryParse(val, out n))
        {
            if (n >= 1000000) return (n / 1000000.0).ToString("0.0") + "e6";
            if (n >= 1000) return (n / 1000.0).ToString("0.0") + "e3";
        }
        return val;
    }

    // ===== 流数据解码 =====
    private string DecodeStreamText(byte[] data)
    {
        if (data == null || data.Length == 0) return null;
        try
        {
            // UTF-16 LE BOM
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
                return Encoding.Unicode.GetString(data, 2, data.Length - 2);
            // UTF-16 BE BOM
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
            // UTF-8 BOM
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                return Encoding.UTF8.GetString(data, 3, data.Length - 3);

            // 检查 UTF-16 LE（偶数位有空字节）
            if (data.Length >= 4 && data[1] == 0 && data[3] == 0)
                return Encoding.Unicode.GetString(data);
            // 默认 UTF-8
            return Encoding.UTF8.GetString(data);
        }
        catch { return null; }
    }

    private bool IsWhitespaceOnly(string text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        foreach (char c in text)
            if (!char.IsWhiteSpace(c)) return false;
        return true;
    }

    private bool IsReadableText(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 20) return false;
        int printable = 0;
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || char.IsPunctuation(c))
                printable++;
        }
        return (double)printable / text.Length > 0.6;
    }

    private bool IsXml(byte[] data)
    {
        if (data == null || data.Length < 5) return false;
        int start = 0;
        if (data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) start = 3;
        if (data[0] == 0xFE && data[1] == 0xFF) start = 2;
        if (data[0] == 0xFF && data[1] == 0xFE) start = 2;
        for (int i = start; i < data.Length - 1; i++)
        {
            if (data[i] == ' ' || data[i] == '\t' || data[i] == '\r' || data[i] == '\n')
                continue;
            return data[i] == '<';
        }
        return false;
    }
}

// ===== 梯度步骤数据类 =====
class GradientStep
{
    public string Time { get; set; }
    public string Flow { get; set; }
    public string PercentB { get; set; }
    public string Curve { get; set; }
    public bool IsWash { get; set; }
    public bool IsEquil { get; set; }
    public bool IsInject { get; set; }

    public GradientStep()
    {
        Time = "";
        Flow = "";
        PercentB = "";
        Curve = "";
        IsWash = false;
        IsEquil = false;
        IsInject = false;
    }
}
